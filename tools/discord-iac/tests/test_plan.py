import copy
import io
import json
import unittest
from contextlib import redirect_stdout

import yaml

from discord_iac import permissions as P
from discord_iac.apply import ApplyAborted, execute
from discord_iac.config import ConfigError, parse_config
from discord_iac.export import export_yaml
from discord_iac.plan import NEW, build_plan, resolve_config, resolve_snapshot
from discord_iac.state import GuildState, fetch_snapshot

GUILD = "100"
BOT = "900"
BOT_ROLE = "400"


def bits(*names):
    return str(P.to_bits(list(names)))


def ow(target, kind=0, allow=(), deny=()):
    return {"id": target, "type": kind, "allow": bits(*allow), "deny": bits(*deny)}


def role(rid, name, position, perms=(), **extra):
    r = {"id": rid, "name": name, "position": position, "permissions": bits(*perms), "color": 0,
         "colors": {"primary_color": 0, "secondary_color": None, "tertiary_color": None},
         "hoist": False, "mentionable": False, "managed": False}
    r.update(extra)
    return r


def channel(cid, name, ctype, position, parent=None, overwrites=(), topic=None):
    return {"id": cid, "name": name, "type": ctype, "position": position, "parent_id": parent,
            "permission_overwrites": list(overwrites), "topic": topic}


def base_snapshot():
    staff_ows = [ow(GUILD, deny=["view_channel"]), ow("201", allow=["view_channel"]),
                 ow(BOT_ROLE, allow=["view_channel"])]
    bot_perms = P.to_names(P.ALL_KNOWN & ~P.ADMINISTRATOR)
    return {
        "version": 1,
        "taken_at": "2026-10-13T00:00:00+00:00",
        "guild": {"id": GUILD, "name": "Test", "owner_id": "1"},
        "bot": {"user_id": BOT, "username": "bot", "role_ids": [BOT_ROLE]},
        "roles": [
            role(GUILD, "@everyone", 0, ["view_channel", "send_messages"]),
            role(BOT_ROLE, "BotRole", 5, bot_perms, managed=True, tags={"bot_id": BOT}),
            role("200", "Admin", 4, ["manage_messages", "send_messages"], hoist=True),
            role("201", "Mod", 3, ["manage_messages"]),
            role("202", "Member", 2, []),
            role("203", "Old", 1, []),
        ],
        "role_member_counts": {},
        "channels": [
            channel("10", "Info", 4, 0),
            channel("11", "rules", 0, 0, "10", [ow(GUILD, deny=["send_messages"])]),
            channel("12", "news", 0, 0),
            channel("20", "Staff", 4, 1, overwrites=staff_ows),
            channel("21", "staff", 0, 0, "20", staff_ows),
        ],
        "members": {},
    }


DESIRED = """
roles:
  - name: BotRole
    managed: true
  - name: Admin
    permissions: [manage_messages, send_messages, view_audit_log]
  - name: Helper
    color: "#336699"
    hoist: true
    permissions: [kick_members]
  - name: Moderator
    id: "201"
    permissions: [manage_messages, send_messages]
  - name: Member
    permissions: [view_channel]
  - name: "@everyone"
    permissions: [read_message_history]
uncategorized: []
categories:
  - name: Info
    overwrites:
      "@everyone":
        allow: [view_channel]
    channels:
      - name: rules
        type: text
        sync: true
      - name: news
        type: text
        topic: Release news
        sync: true
      - name: announcements
        type: announcement
        sync: true
  - name: Voice
    overwrites: {}
    channels:
      - name: Lounge
        type: voice
        sync: true
  - name: Staff
    overwrites:
      "@everyone":
        deny: [view_channel]
      Moderator:
        allow: [view_channel]
      Helper:
        allow: [view_channel]
      BotRole:
        allow: [view_channel]
    channels:
      - name: staff
        type: text
        sync: true
"""


class FakeDiscord:
    """Just enough of the REST API, in memory, for plan/apply round trips."""

    def __init__(self, snapshot):
        self.s = copy.deepcopy(snapshot)
        self.calls = []
        self._next = 7000

    def _id(self):
        self._next += 1
        return str(self._next)

    # reads used by fetch_snapshot
    def get_current_user(self):
        return {"id": self.s["bot"]["user_id"], "username": "bot"}

    def get_guild(self, gid):
        return dict(self.s["guild"])

    def get_roles(self, gid):
        return copy.deepcopy(self.s["roles"])

    def get_channels(self, gid):
        return copy.deepcopy(self.s["channels"])

    def get_role_member_counts(self, gid):
        return {}

    def get_member(self, gid, uid):
        if uid == self.s["bot"]["user_id"]:
            return {"user": {"id": uid, "username": "bot"}, "roles": list(self.s["bot"]["role_ids"])}
        return {"user": {"id": uid, "username": f"user{uid}"}, "roles": []}

    def request(self, method, path, json=None, reason=None, params=None):
        assert method == "GET" or reason, f"{method} {path} has no audit log reason"
        assert NEW not in str(json), f"placeholder sent to the API: {json}"
        self.calls.append((method, path, copy.deepcopy(json), reason))
        parts = path.strip("/").split("/")
        roles = {r["id"]: r for r in self.s["roles"]}
        chans = {c["id"]: c for c in self.s["channels"]}
        if parts[:3] == ["guilds", GUILD, "roles"]:
            if method == "POST":
                for r in self.s["roles"]:
                    if r["id"] != GUILD:
                        r["position"] += 1
                new = role(self._id(), json["name"], 1)
                new["permissions"] = json.get("permissions", "0")
                self._role_fields(new, json)
                self.s["roles"].append(new)
                return new
            if method == "PATCH" and len(parts) == 3:
                for item in json:
                    roles[item["id"]]["position"] = item["position"]
                return self.s["roles"]
            if method == "PATCH":
                self._role_fields(roles[parts[3]], json)
                return roles[parts[3]]
            if method == "DELETE":
                self.s["roles"] = [r for r in self.s["roles"] if r["id"] != parts[3]]
                return None
        if parts[:3] == ["guilds", GUILD, "channels"]:
            if method == "POST":
                siblings = [c for c in self.s["channels"] if c.get("parent_id") == json.get("parent_id")]
                new = channel(self._id(), json["name"], json["type"], len(siblings), json.get("parent_id"),
                              [self._ow(o) for o in json.get("permission_overwrites", [])], json.get("topic"))
                self.s["channels"].append(new)
                return new
            if method == "PATCH":
                for item in json:
                    c = chans[item["id"]]
                    if "parent_id" in item:
                        c["parent_id"] = item["parent_id"]
                        c["position"] = 99
                    if "position" in item:
                        c["position"] = item["position"]
                return None
        if parts[0] == "channels":
            c = chans[parts[1]]
            if method == "PATCH":
                for key in ("name", "topic", "type"):
                    if key in json:
                        c[key] = json[key]
                if "permission_overwrites" in json:
                    c["permission_overwrites"] = [self._ow(o) for o in json["permission_overwrites"]]
                return c
            if method == "DELETE":
                self.s["channels"] = [x for x in self.s["channels"] if x["id"] != parts[1]]
                return c
        raise AssertionError(f"unhandled {method} {path}")

    @staticmethod
    def _ow(o):
        return {"id": o["id"], "type": o["type"], "allow": str(o.get("allow") or 0), "deny": str(o.get("deny") or 0)}

    @staticmethod
    def _role_fields(r, body):
        for key in ("name", "permissions", "hoist", "mentionable"):
            if key in body:
                r[key] = body[key]
        if "colors" in body:
            r["colors"] = {**r["colors"], **body["colors"]}
            r["color"] = body["colors"]["primary_color"]


def plan_for(snapshot, desired_yaml=DESIRED):
    state = GuildState(snapshot)
    return build_plan(state, resolve_config(state, parse_config(yaml.safe_load(desired_yaml))))


class ConfigTests(unittest.TestCase):
    def test_rejects_typos_and_contradictions(self):
        bad = [
            ("roles: [{name: A, permission: []}]", "unknown key"),
            ("roles: [{name: A, permissions: [send_message]}]", "unknown permission"),
            ("roles: [{name: '@everyone', permissions: []}, {name: A}]", "must be the last"),
            ("categories: [{name: C, overwrites: {}, channels: [{name: x, sync: true, overwrites: {}}]}]",
             "either `sync: true` or `overwrites`"),
            ("categories: [{name: C, overwrites: {A: {allow: [view_channel], deny: [view_channel]}}}]",
             "both allows and denies"),
            ("uncategorized: [{name: x, sync: true}]", "can't `sync`"),
        ]
        for text, message in bad:
            with self.assertRaises(ConfigError, msg=text) as ctx:
                parse_config(yaml.safe_load(text))
            self.assertIn(message, str(ctx.exception))


class PlanTests(unittest.TestCase):
    def test_export_round_trip_is_a_no_op(self):
        snap = base_snapshot()
        exported = export_yaml(GuildState(snap))
        plan = plan_for(snap, exported)
        self.assertEqual(plan.changes, [], [c.text for c in plan.changes])
        self.assertTrue(plan.ok)

    def test_plan_contents(self):
        plan = plan_for(base_snapshot())
        self.assertTrue(plan.ok, plan.errors + plan.blockers)
        texts = [c.text for c in plan.changes]
        self.assertIn('create role "Helper"', texts)
        self.assertIn('update role "Moderator"', texts)  # renamed through its id
        self.assertIn('create category "Voice"', texts)
        self.assertIn("create #announcements", texts)
        self.assertIn('move #news: (no category) -> "Info"', texts)
        self.assertIn('category "Info": update overwrites', texts)
        # Old isn't listed, so it is only warned about, never deleted.
        self.assertFalse(any("Old" in t for t in texts))
        self.assertTrue(any('"Old" isn\'t in the config' in w for w in plan.warnings))

    def test_blocks_what_the_bot_cannot_do(self):
        snap = base_snapshot()
        # Hide Staff from the bot and put Admin above the bot's role.
        for c in snap["channels"]:
            if c["id"] in ("20", "21"):
                c["permission_overwrites"] = [o for o in c["permission_overwrites"] if o["id"] != BOT_ROLE]
        next(r for r in snap["roles"] if r["id"] == "200")["position"] = 9
        desired = DESIRED.replace("      BotRole:\n        allow: [view_channel]\n", "")
        plan = plan_for(snap, desired)
        text = "\n".join(plan.blockers)
        self.assertIn('can\'t see these, so it can\'t change them: category "Staff", #staff', text)
        self.assertIn('role "Admin" is at or above the bot\'s highest role', text)

    def test_never_reduces_the_bot(self):
        desired = DESIRED.replace("      BotRole:\n        allow: [view_channel]\n", "")
        plan = plan_for(base_snapshot(), desired)
        self.assertTrue(any("the bot would lose view_channel" in b for b in plan.blockers), plan.blockers)

    def test_duplicate_names_are_refused(self):
        snap = base_snapshot()
        snap["roles"].append(role("204", "Member", 1, []))
        plan = plan_for(snap)
        self.assertTrue(any("2 live roles have that name" in e for e in plan.errors))

    def test_bot_roles_are_hand_steps_and_coverage_is_checked(self):
        desired = DESIRED.replace("  - name: BotRole\n    managed: true\n",
                                  "  - name: BotRole\n    managed: true\n"
                                  "    permissions: [view_channel, send_messages]\n")
        plan = plan_for(base_snapshot(), desired)
        self.assertTrue(plan.ok, plan.blockers)
        self.assertTrue(any(text.startswith('role "BotRole"') for text, _ in plan.manual))
        self.assertNotIn('update role "BotRole"', [c.text for c in plan.changes])
        # Drop the bot's view allow on Staff from the config: the final-state check must notice.
        uncovered = desired.replace("      BotRole:\n        allow: [view_channel]\n", "")
        warnings = "\n".join(plan_for(base_snapshot(), uncovered).warnings)
        self.assertIn("the bot would be missing view_channel in: category \"Staff\", #staff", warnings)

    def test_delete_only_when_listed(self):
        plan = plan_for(base_snapshot(), DESIRED + "delete:\n  roles: [Old]\n")
        self.assertIn('delete role "Old"', [c.text for c in plan.changes])


class ApplyTests(unittest.TestCase):
    def run_apply(self, fake, plan_fn):
        confirmed = plan_fn(GuildState(fetch_snapshot(fake, GUILD)))
        with redirect_stdout(io.StringIO()):
            execute(fake, GUILD, confirmed, plan_fn, "discord-iac test")
        return plan_fn(GuildState(fetch_snapshot(fake, GUILD)))

    def desired_fn(self, desired=DESIRED):
        cfg = parse_config(yaml.safe_load(desired))
        return lambda state: build_plan(state, resolve_config(state, cfg))

    def test_apply_reaches_the_desired_state(self):
        fake = FakeDiscord(base_snapshot())
        after = self.run_apply(fake, self.desired_fn())
        self.assertEqual([c.text for c in after.changes], [])
        state = GuildState(fetch_snapshot(fake, GUILD))
        names = [r["name"] for r in state.roles]
        self.assertEqual(names[:5], ["BotRole", "Admin", "Helper", "Moderator", "Member"])
        info = next(c for c in state.categories if c["name"] == "Info")
        self.assertEqual([c["name"] for c in state.children[info["id"]]], ["rules", "news", "announcements"])
        self.assertTrue(all(state.is_synced(c) for c in state.children[info["id"]]))
        news = next(c for c in state.all_non_category_channels() if c["name"] == "news")
        self.assertEqual(news["topic"], "Release news")
        self.assertTrue(all(reason.startswith("discord-iac test: ") for m, _, _, reason in fake.calls if m != "GET"))

    def test_stops_when_the_server_changed_after_confirming(self):
        fake = FakeDiscord(base_snapshot())
        plan_fn = self.desired_fn()
        confirmed = plan_fn(GuildState(fetch_snapshot(fake, GUILD)))
        confirmed.changes = [c for c in confirmed.changes if c.text != 'update role "Member"']
        with redirect_stdout(io.StringIO()), self.assertRaises(ApplyAborted):
            execute(fake, GUILD, confirmed, plan_fn, "discord-iac test")

    def test_restore_puts_permissions_and_overwrites_back(self):
        original = base_snapshot()
        fake = FakeDiscord(original)
        self.run_apply(fake, self.desired_fn())
        restore_fn = lambda state: build_plan(state, resolve_snapshot(state, original))
        after = self.run_apply(fake, restore_fn)
        self.assertEqual([c.text for c in after.changes], [])
        state = GuildState(fetch_snapshot(fake, GUILD))
        for r in original["roles"]:
            self.assertEqual(state.role_by_id[r["id"]]["permissions"], r["permissions"], r["name"])
        rules = state.channel_by_id["11"]
        self.assertEqual(json.dumps(rules["permission_overwrites"]),
                         json.dumps(original["channels"][1]["permission_overwrites"]))


if __name__ == "__main__":
    unittest.main()
