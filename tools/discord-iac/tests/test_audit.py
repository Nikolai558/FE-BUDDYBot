import unittest

import yaml

from discord_iac import permissions as P
from discord_iac.audit import find_problems, render_problems, render_report
from discord_iac.export import _scalar, export_yaml
from discord_iac.state import GuildState

GUILD = "100"
BOT = "900"


def bits(*names):
    return str(P.to_bits(list(names)))


def ow(target, kind, allow=(), deny=()):
    return {"id": target, "type": kind, "allow": bits(*allow), "deny": bits(*deny)}


def role(rid, name, position, perms=(), **extra):
    r = {"id": rid, "name": name, "position": position, "permissions": bits(*perms),
         "color": 0, "colors": {"primary_color": 0}, "hoist": False, "mentionable": False, "managed": False}
    r.update(extra)
    return r


def channel(cid, name, ctype, position, parent=None, overwrites=(), topic=None):
    return {"id": cid, "name": name, "type": ctype, "position": position, "parent_id": parent,
            "permission_overwrites": list(overwrites), "topic": topic}


def snapshot():
    staff_ow = ow("201", 0, allow=["view_channel"])
    hidden = ow(GUILD, 0, deny=["view_channel"])
    return {
        "version": 1,
        "taken_at": "2026-10-12T00:00:00+00:00",
        "guild": {"id": GUILD, "name": "Test Server", "owner_id": "1"},
        "bot": {"user_id": BOT, "username": "bot", "role_ids": ["300"]},
        "roles": [
            role(GUILD, "@everyone", 0, ["view_channel", "send_messages", "read_message_history"]),
            role("200", "Admin", 5, ["administrator"], color=0xE74C3C, colors={"primary_color": 0xE74C3C}),
            role("300", "Bot", 3, ["manage_roles", "manage_channels", "view_channel"], managed=True,
                 tags={"bot_id": BOT}),
            role("201", "Moderator", 2, ["manage_messages", "send_messages", "kick_members"], hoist=True),
            role("202", "Member", 1, []),
            role("203", "Member", 4, []),
        ],
        "role_member_counts": {"200": 2, "300": 1, "201": 4, "202": 10, "203": 0},
        "channels": [
            channel("10", "Info", 4, 0),
            channel("11", "rules", 0, 0, "10", [ow(GUILD, 0, deny=["send_messages"])], topic="Read me: now"),
            channel("12", "announcements", 5, 1, "10"),
            channel("20", "Staff", 4, 1, overwrites=[hidden, staff_ow]),
            channel("21", "staff-chat", 0, 0, "20", [hidden, staff_ow]),
            channel("22", "staff-voice", 2, 1, "20", [hidden, staff_ow, ow("555", 1, allow=["speak"])]),
            channel("30", "welcome", 0, 0),
        ],
        "members": {"555": {"name": "Pat", "in_guild": True}},
    }


class AuditTests(unittest.TestCase):
    def setUp(self):
        self.state = GuildState(snapshot())

    def test_permission_round_trip(self):
        self.assertEqual(P.to_names(P.to_bits(["pin_messages", "view_channel"])), ["view_channel", "pin_messages"])
        self.assertEqual(P.to_names(1 << 47), ["unknown_bit_47"])
        with self.assertRaises(P.UnknownPermission):
            P.to_bits(["send_message"])

    def test_role_order_and_hierarchy(self):
        self.assertEqual([r["id"] for r in self.state.roles], ["200", "203", "300", "201", "202", GUILD])
        self.assertEqual(self.state.bot_top_role["id"], "300")
        self.assertTrue(self.state.bot_can_manage_role("201"))
        self.assertFalse(self.state.bot_can_manage_role("203"))
        self.assertFalse(self.state.bot_can_manage_role("300"))

    def test_sync_detection(self):
        ch = self.state.channel_by_id
        self.assertTrue(self.state.is_synced(ch["21"]))
        self.assertFalse(self.state.is_synced(ch["22"]))
        self.assertFalse(self.state.is_synced(ch["11"]))
        self.assertTrue(self.state.is_synced(ch["12"]))
        self.assertIsNone(self.state.is_synced(ch["30"]))

    def test_problems(self):
        text = " ".join(render_problems(find_problems(self.state)).split())
        self.assertIn("#rules in Info: @everyone deny [send_messages] with no staff allow; Moderator loses send_messages", text)
        self.assertIn("#staff-voice in Staff: extra member Pat (555)", text)
        self.assertIn("Roles with Administrator", text)
        self.assertIn("role 'Member' x2", text)
        self.assertIn("- Admin - Member Fix:", text)
        # Moderator is allowed view_channel on the Staff category, so it is not flagged there.
        self.assertNotIn("category Staff (and", text)
        self.assertIn("- #staff-chat (text)", text)

    def test_report_renders(self):
        report = render_report(self.state)
        self.assertIn("CATEGORY Staff  [id 20; 1/2 channels synced]", report)
        self.assertIn("BOT'S HIGHEST ROLE", report)

    def test_export_is_valid_yaml_in_config_schema(self):
        doc = yaml.safe_load(export_yaml(self.state))
        self.assertEqual([r["name"] for r in doc["roles"]], ["Admin", "Member", "Bot", "Moderator", "Member", "@everyone"])
        self.assertEqual(doc["roles"][0]["color"], "#e74c3c")
        self.assertIn("id", doc["roles"][1])  # duplicate name gets an id
        self.assertTrue(doc["roles"][2]["managed"])
        self.assertEqual(doc["uncategorized"][0]["name"], "welcome")
        info, staff = doc["categories"]
        self.assertEqual(info["overwrites"], {})
        self.assertEqual(info["channels"][0]["topic"], "Read me: now")
        self.assertEqual(info["channels"][0]["overwrites"], {"@everyone": {"deny": ["send_messages"]}})
        self.assertTrue(info["channels"][1]["sync"])
        self.assertEqual(staff["overwrites"]["Moderator"], {"allow": ["view_channel"]})
        self.assertEqual(staff["channels"][1]["overwrites"]["member:555"], {"allow": ["speak"]})
        self.assertEqual(doc["delete"], {"roles": [], "categories": [], "channels": []})

    def test_long_permission_lists_wrap_and_parse(self):
        snap = snapshot()
        snap["roles"][0]["permissions"] = str(P.ALL_KNOWN & ~P.ADMINISTRATOR)
        doc = yaml.safe_load(export_yaml(GuildState(snap)))
        self.assertEqual(len(doc["roles"][-1]["permissions"]), len(P.PERMISSION_BITS) - 1)

    def test_scalar_quoting(self):
        for s in ["general", "ARTCC Staff", "yes", "123", "#rules", "a: b", "emoji 🎉", "null", "-dash", "x\ny"]:
            self.assertEqual(yaml.safe_load(f"k: {_scalar(s)}")["k"], s, s)


if __name__ == "__main__":
    unittest.main()
