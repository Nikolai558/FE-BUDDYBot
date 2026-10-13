"""Diff a desired state against the live server and turn it into API changes.

The desired state comes from desired.yaml (`resolve_config`) or from a snapshot
(`resolve_snapshot`, for restore). `build_plan` turns it into ordered changes,
plus warnings and blockers (things the bot isn't allowed to do).
"""

from __future__ import annotations

import copy
from collections.abc import Callable
from dataclasses import dataclass, field

from . import permissions as P
from .config import UNSET, Config, OverwriteSpec
from .state import (
    CATEGORY,
    OW_MEMBER,
    OW_ROLE,
    TOPIC_TYPES,
    TYPE_IDS,
    VOICE_LIKE,
    GuildState,
    Overwrite,
    channel_type_name,
    overwrites_of,
)

NEW = "new:"  # id placeholder for something apply hasn't created yet

PH_ROLES, PH_ROLE_ORDER, PH_CATEGORIES, PH_CATEGORY_ORDER, PH_CHANNELS, PH_CHANNEL_ORDER, PH_OVERWRITES, PH_DELETES = range(1, 9)
PHASES = {
    PH_ROLES: "Roles",
    PH_ROLE_ORDER: "Role order",
    PH_CATEGORIES: "Categories",
    PH_CATEGORY_ORDER: "Category order",
    PH_CHANNELS: "Channels",
    PH_CHANNEL_ORDER: "Channel order",
    PH_OVERWRITES: "Overwrites",
    PH_DELETES: "Deletes",
}

FIX_ADMIN = ("Simplest: temporarily turn on Administrator for the bot's highest role (Server Settings > Roles), "
             "run apply, then turn it off again.")


# ---------------------------------------------------------------------------
# resolved targets


@dataclass
class TRole:
    name: str
    live: dict | None
    color: object = UNSET
    hoist: object = UNSET
    mentionable: object = UNSET
    permissions: object = UNSET

    @property
    def ref(self) -> str:
        return self.live["id"] if self.live else NEW + self.name


@dataclass
class TOw:
    label: str
    target: str  # id, or NEW+role name
    kind: int
    allow: int
    deny: int


@dataclass
class TCat:
    name: str
    live: dict | None
    overwrites: dict[str, TOw] | None

    @property
    def ref(self) -> str:
        return self.live["id"] if self.live else NEW + "category:" + self.name


@dataclass
class TChan:
    name: str
    live: dict | None
    type: int | None
    topic: object = UNSET
    parent: object = UNSET  # UNSET = don't move; None = no category; else a TCat ref
    overwrites: dict[str, TOw] | None = None

    @property
    def ref(self) -> str:
        return self.live["id"] if self.live else NEW + "channel:" + self.name


@dataclass
class Target:
    roles: list[TRole] = field(default_factory=list)
    role_order: list[TRole] | None = None
    categories: list[TCat] = field(default_factory=list)
    category_order: bool = False
    channels: list[TChan] = field(default_factory=list)
    ordered_parents: list[object] = field(default_factory=list)  # parents whose child order is managed
    deletes: list[tuple[str, dict]] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    role_label: Callable[[str], str] | None = None  # role id -> name to show (desired name if renamed)
    restore_from: dict | None = None  # restore: the snapshot being restored


# ---------------------------------------------------------------------------
# plan


@dataclass
class Change:
    phase: int
    text: str
    method: str
    path: str
    body: object = None
    details: list[str] = field(default_factory=list)
    follows: str | None = None  # shown folded under this change (synced channels)


@dataclass
class Plan:
    changes: list[Change]
    warnings: list[str]
    errors: list[str]
    blockers: list[str]
    fixes: list[str]
    manual: list[tuple[str, list[str]]] = field(default_factory=list)  # role changes the bot can't make

    @property
    def ok(self) -> bool:
        return not self.errors and not self.blockers


def names(value: int) -> str:
    return ", ".join(P.to_names(value)) or "-"


# ---------------------------------------------------------------------------
# resolving desired.yaml against the live server


def resolve_config(state: GuildState, cfg: Config) -> Target:
    t = Target()
    gid = state.guild_id

    # roles -----------------------------------------------------------------
    by_desired_name: dict[str, TRole] = {}
    if cfg.roles is not None:
        used: set[str] = set()
        for spec in cfg.roles:
            live = None
            if spec.is_everyone:
                live = state.everyone
            elif spec.id:
                live = state.role_by_id.get(spec.id)
                if live is None:
                    t.errors.append(f'role "{spec.name}": no role with id {spec.id}')
                    continue
            else:
                matches = [r for r in state.roles if r["name"] == spec.name and r["id"] != gid]
                if len(matches) > 1:
                    t.errors.append(f'role "{spec.name}": {len(matches)} live roles have that name; add `id:` to pick one')
                    continue
                live = matches[0] if matches else None
            if live is not None:
                if live["id"] in used:
                    t.errors.append(f'role "{spec.name}" matches a role already listed')
                    continue
                used.add(live["id"])
            if spec.managed and live is None:
                t.errors.append(f'role "{spec.name}" is marked managed but doesn\'t exist; the tool can\'t create managed roles')
                continue
            tr = TRole(spec.name, live, spec.color, spec.hoist, spec.mentionable, spec.permissions)
            t.roles.append(tr)
            by_desired_name[spec.name] = tr
        t.role_order = [r for r in t.roles if not (r.live and state.is_everyone(r.live["id"]))]
        for r in state.roles:
            if r["id"] not in used and r["id"] != gid:
                t.warnings.append(f'role "{r["name"]}" isn\'t in the config; left as is')

    id_to_desired_name = {r.live["id"]: r.name for r in t.roles if r.live}

    def role_label(role_id: str) -> str:
        if role_id == gid:
            return "@everyone"
        if role_id in id_to_desired_name:
            return id_to_desired_name[role_id]
        role = state.role_by_id.get(role_id)
        return role["name"] if role else f"deleted role {role_id}"

    t.role_label = role_label

    def resolve_ows(specs: list[OverwriteSpec] | None, where: str) -> dict[str, TOw] | None:
        if specs is None:
            return None
        out: dict[str, TOw] = {}
        for s in specs:
            key = s.key
            if key == "@everyone":
                target, kind, label = gid, OW_ROLE, "@everyone"
            elif key.startswith("member:") and key[7:].isdigit():
                target, kind = key[7:], OW_MEMBER
                label = state.target_label(Overwrite(target, OW_MEMBER, 0, 0))
            elif key.startswith("role:") and key[5:].isdigit():
                target, kind = key[5:], OW_ROLE
                if target not in state.role_by_id:
                    t.errors.append(f"{where}: no role with id {target}")
                    continue
                label = role_label(target)
            elif key in by_desired_name:
                tr = by_desired_name[key]
                target, kind, label = tr.ref, OW_ROLE, key
            else:
                matches = [r for r in state.roles if r["name"] == key and r["id"] != gid]
                if len(matches) != 1:
                    problem = "no role" if not matches else f"{len(matches)} roles"
                    t.errors.append(f'{where}: {problem} named "{key}" (use "role:<id>" if it\'s ambiguous)')
                    continue
                target, kind, label = matches[0]["id"], OW_ROLE, role_label(matches[0]["id"])
            if target in out:
                t.errors.append(f"{where}: {label} is listed twice")
                continue
            out[target] = TOw(label, target, kind, s.allow, s.deny)
        return out

    # categories and channels -------------------------------------------------
    if cfg.categories is not None:
        used_cats: set[str] = set()
        used_chans: set[str] = set()
        non_cat = state.all_non_category_channels()

        def match_channel(spec, pool, used, what) -> dict | None | bool:
            if spec.id:
                live = next((c for c in pool if c["id"] == spec.id), None)
                if live is None:
                    t.errors.append(f'{what} "{spec.name}": no {what} with id {spec.id}')
                    return False
            else:
                matches = [c for c in pool if c["name"] == spec.name]
                if len(matches) > 1:
                    t.errors.append(f'{what} "{spec.name}": {len(matches)} exist with that name; add `id:` to pick one')
                    return False
                live = matches[0] if matches else None
            if live is not None:
                if live["id"] in used:
                    t.errors.append(f'{what} "{spec.name}" matches one already listed')
                    return False
                used.add(live["id"])
            return live

        def add_channel(spec, parent_ref, cat_ows) -> None:
            live = match_channel(spec, non_cat, used_chans, "channel")
            if live is False:
                return
            where = f"#{spec.name}"
            ctype = TYPE_IDS[spec.type] if spec.type else None
            if live is None and ctype is None:
                t.errors.append(f"{where}: `type` is needed to create it")
                return
            if live is not None and ctype is not None and ctype != live["type"]:
                if {ctype, live["type"]} != {0, 5}:
                    t.errors.append(f"{where}: can't change a {channel_type_name(live)} channel into {spec.type}")
                    return
            final_type = ctype if ctype is not None else live["type"]
            if spec.topic is not UNSET and spec.topic is not None and final_type not in TOPIC_TYPES:
                t.errors.append(f"{where}: {channel_type_name({'type': final_type})} channels have no topic")
                return
            ows = copy.deepcopy(cat_ows) if spec.sync else resolve_ows(spec.overwrites, where)
            t.channels.append(TChan(spec.name, live, ctype, spec.topic, parent_ref, ows))

        if cfg.uncategorized is not None:
            t.ordered_parents.append(None)
            for spec in cfg.uncategorized:
                add_channel(spec, None, None)

        for cs in cfg.categories:
            live = match_channel(cs, state.categories, used_cats, "category")
            if live is False:
                continue
            tc = TCat(cs.name, live, resolve_ows(cs.overwrites, f"category {cs.name}"))
            t.categories.append(tc)
            t.ordered_parents.append(tc.ref)
            for spec in cs.channels:
                add_channel(spec, tc.ref, tc.overwrites)
        t.category_order = True

        for c in state.categories:
            if c["id"] not in used_cats:
                t.warnings.append(f'category "{c["name"]}" isn\'t in the config; left as is')
        for c in non_cat:
            if c["id"] not in used_chans:
                t.warnings.append(f'#{c["name"]} isn\'t in the config; left as is')
        if any(c.live is None for c in t.channels):
            t.warnings.append("channels the bot can't view are hidden from it (from 2026-11-16 the API omits them), "
                              "so make sure each channel marked `create` really doesn't exist yet")

    # deletes -----------------------------------------------------------------
    listed_roles = {r.live["id"] for r in t.roles if r.live}
    listed_chans = {c.live["id"] for c in t.channels if c.live} | {c.live["id"] for c in t.categories if c.live}
    for ref in cfg.delete_roles:
        live = _find(state.roles, ref, lambda r: r["id"] != gid)
        if isinstance(live, str):
            t.errors.append(f"delete.roles: {live}")
        elif live is None:
            t.warnings.append(f'delete.roles: "{ref}" doesn\'t exist (already deleted?)')
        elif live["id"] in listed_roles:
            t.errors.append(f'delete.roles: "{ref}" is also listed under roles')
        elif live.get("managed"):
            t.errors.append(f'delete.roles: "{ref}" is a managed role; remove its bot/integration instead')
        else:
            t.deletes.append(("role", live))
    for ref in cfg.delete_channels:
        live = _find(state.all_non_category_channels(), ref, lambda c: True)
        _add_channel_delete(t, ref, live, listed_chans, "channels")
    for ref in cfg.delete_categories:
        live = _find(state.categories, ref, lambda c: True)
        _add_channel_delete(t, ref, live, listed_chans, "categories")
        if isinstance(live, dict):
            staying = [c["name"] for c in state.children.get(live["id"], [])
                       if not any(tc.live and tc.live["id"] == c["id"] and tc.parent != live["id"] for tc in t.channels)]
            if staying:
                t.warnings.append(f'deleting category "{live["name"]}" leaves {", ".join("#" + n for n in staying)} '
                                  "without a category (Discord doesn't delete the channels in it)")
    return t


def _find(pool: list[dict], ref: str, ok) -> dict | None | str:
    if ref.startswith("id:"):
        return next((x for x in pool if x["id"] == ref[3:] and ok(x)), None)
    matches = [x for x in pool if x["name"] == ref and ok(x)]
    if len(matches) > 1:
        return f'"{ref}" matches {len(matches)}; use "id:<id>"'
    return matches[0] if matches else None


def _add_channel_delete(t: Target, ref: str, live, listed: set[str], section: str) -> None:
    if isinstance(live, str):
        t.errors.append(f"delete.{section}: {live}")
    elif live is None:
        t.warnings.append(f'delete.{section}: "{ref}" doesn\'t exist (already deleted?)')
    elif live["id"] in listed:
        t.errors.append(f'delete.{section}: "{ref}" is also listed in the config')
    else:
        t.deletes.append(("category" if live["type"] == CATEGORY else "channel", live))


# ---------------------------------------------------------------------------
# resolving a snapshot (restore): role permissions and overwrites only


def resolve_snapshot(state: GuildState, snapshot: dict) -> Target:
    t = Target(restore_from=snapshot)
    if snapshot["guild"]["id"] != state.guild_id:
        t.errors.append(f"snapshot is for server {snapshot['guild']['id']}, not {state.guild_id}")
        return t
    for r in snapshot["roles"]:
        live = state.role_by_id.get(r["id"])
        if live is None:
            t.warnings.append(f'role "{r["name"]}" was deleted since the snapshot; restore can\'t recreate it')
            continue
        name = "@everyone" if state.is_everyone(r["id"]) else live["name"]
        t.roles.append(TRole(name, live, permissions=int(r["permissions"])))
    t.role_label = lambda rid: "@everyone" if rid == state.guild_id else (
        state.role_by_id[rid]["name"] if rid in state.role_by_id else f"deleted role {rid}")

    for c in snapshot["channels"]:
        live = state.channel_by_id.get(c["id"])
        if live is None:
            t.warnings.append(f'#{c["name"]} was deleted since the snapshot; restore can\'t recreate it')
            continue
        ows: dict[str, TOw] = {}
        for ow in overwrites_of(c).values():
            if ow.kind == OW_ROLE and ow.target_id not in state.role_by_id:
                t.warnings.append(f'#{live["name"]}: skipped the overwrite for deleted role {ow.target_id}')
                continue
            label = t.role_label(ow.target_id) if ow.kind == OW_ROLE else state.target_label(ow)
            ows[ow.target_id] = TOw(label, ow.target_id, ow.kind, ow.allow, ow.deny)
        if live["type"] == CATEGORY:
            t.categories.append(TCat(live["name"], live, ows))
        else:
            t.channels.append(TChan(live["name"], live, None, overwrites=ows))
    return t


# ---------------------------------------------------------------------------
# building the plan


class _Builder:
    def __init__(self, state: GuildState, target: Target):
        self.s = state
        self.t = target
        self.changes: list[Change] = []
        self.warnings = list(target.warnings)
        self.blockers: list[str] = []
        self.fixes: list[str] = []
        self.manual: list[tuple[str, list[str]]] = []
        self.gid = state.guild_id
        self.bot_base = state.bot_base_permissions
        self.bot_admin = self.bot_base == P.ALL_KNOWN
        self.role_label = target.role_label or (lambda rid: rid)
        self.cant_see: list[str] = []
        self.lost: dict[int, list[str]] = {}
        self.cat_names = {c.ref: c.name for c in target.categories}
        for c in state.categories:
            self.cat_names.setdefault(c["id"], c["name"])

    # -- helpers ---------------------------------------------------------------

    def add(self, phase: int, text: str, method: str, path: str, body=None, details=None) -> Change:
        ch = Change(phase, text, method, path, body, details or [])
        self.changes.append(ch)
        return ch

    def block(self, msg: str, fix: str | None = None) -> None:
        if msg not in self.blockers:
            self.blockers.append(msg)
        if fix and fix not in self.fixes:
            self.fixes.append(fix)

    def need_base(self, perm: str, why: str) -> None:
        if not self.bot_admin and not P.has(self.bot_base, perm):
            self.block(f"the bot lacks {perm} (needed to {why})",
                       f"Give the bot's role {perm}. " + FIX_ADMIN)

    def need_in_channel(self, ch: dict, perm: str, why: str) -> None:
        if self.bot_admin:
            return
        perms = self.s.bot_channel_permissions(ch)
        label = self.where(ch)
        if not perms & P.VIEW_CHANNEL:
            if label not in self.cant_see:
                self.cant_see.append(label)
        elif not P.has(perms, perm):
            self.block(f"the bot lacks {perm} in {label} (needed to {why})",
                       f"Give the bot's role {perm}, or allow it to Buddy Helper there. " + FIX_ADMIN)

    def can_grant(self, bits: int, ch: dict | None, label: str) -> None:
        """Overwrite bits the bot may set: ones it has in the server or parent (docs)."""
        if self.bot_admin or not bits:
            return
        has_mr_overwrite = ch is not None and self._bot_has_manage_roles_overwrite(ch)
        if P.has(bits, "manage_roles") and not has_mr_overwrite:
            self.block(f"{label}: only a bot with Administrator can set manage_roles in an overwrite",
                       FIX_ADMIN)
        if has_mr_overwrite:
            return
        allowed = self.bot_base
        parent = self.s.parent(ch) if ch else None
        if parent is not None:
            allowed |= self.s.bot_channel_permissions(parent)
        missing = bits & ~allowed & ~P.MANAGE_ROLES
        if missing:
            self.block(f"{label}: the bot can't set {names(missing)} in overwrites because it doesn't have them",
                       f"Give the bot's role {names(missing)}. " + FIX_ADMIN)

    def _bot_has_manage_roles_overwrite(self, ch: dict) -> bool:
        holders = set(self.s.bot_role_ids) | {self.s.bot_user_id}
        return any(ow.target_id in holders and ow.allow & P.MANAGE_ROLES for ow in overwrites_of(ch).values())

    def where(self, ch: dict | None, name: str | None = None) -> str:
        if ch is None:
            return f"#{name}"
        if ch["type"] == CATEGORY:
            return f'category "{self.cat_names.get(ch["id"], ch["name"])}"'
        return f"#{name or ch['name']}"

    def parent_label(self, ref) -> str:
        if ref is None:
            return "(no category)"
        return f'"{self.cat_names.get(ref, ref.removeprefix(NEW + "category:"))}"'

    # -- phases ------------------------------------------------------------------

    def roles(self) -> None:
        s = self.s
        for tr in self.t.roles:
            if tr.live is None:
                perms = tr.permissions if tr.permissions is not UNSET else 0
                body = {"name": tr.name, "permissions": str(perms),
                        "hoist": bool(tr.hoist), "mentionable": bool(tr.mentionable)}
                details = [f"permissions: {names(perms)}"]
                if tr.color not in (UNSET, None):
                    body["colors"] = {"primary_color": tr.color}
                    details.append(f"color: #{tr.color:06x}")
                if tr.hoist is True:
                    details.append("hoisted")
                if tr.mentionable is True:
                    details.append("mentionable")
                self.add(PH_ROLES, f'create role "{tr.name}"', "POST", f"/guilds/{self.gid}/roles", body, details)
                self.need_base("manage_roles", f'create role "{tr.name}"')
                missing = perms & ~self.bot_base
                if missing and not self.bot_admin:
                    self.block(f'role "{tr.name}": the bot can\'t grant {names(missing)} because it doesn\'t have them',
                               f"Give the bot's role {names(missing)}. " + FIX_ADMIN)
                continue

            role = tr.live
            rid = role["id"]
            everyone = s.is_everyone(rid)
            body: dict = {}
            details: list[str] = []
            if not everyone and tr.name != role["name"]:
                body["name"] = tr.name
                details.append(f'name: "{role["name"]}" -> "{tr.name}"')
            if not everyone and tr.color is not UNSET:
                live_color = _role_color(role)
                want = tr.color or 0
                if want != live_color:
                    colors = dict(role.get("colors") or {})
                    colors["primary_color"] = want
                    body["colors"] = {k: v for k, v in colors.items() if v is not None}
                    details.append(f"color: {_hex(live_color)} -> {_hex(want)}")
            for key in ("hoist", "mentionable"):
                want = getattr(tr, key)
                if not everyone and want is not UNSET and bool(role.get(key)) != want:
                    body[key] = want
                    details.append(f"{key}: {bool(role.get(key))} -> {want}")
            old = int(role["permissions"])
            if tr.permissions is not UNSET and tr.permissions != old:
                new = tr.permissions
                body["permissions"] = str(new)
                details.extend(_perm_delta(old, new))
            if not body:
                continue
            label = "@everyone" if everyone else tr.name
            if self._hand_only(role):
                why = ("it's managed by the bot's integration" if role.get("managed")
                       else "it's the bot's own highest role")
                self.manual.append((f'role "{label}" ({why}): Server Settings > Roles > {role["name"]}', details))
                continue
            self.add(PH_ROLES, f'update role "{label}"', "PATCH", f"/guilds/{self.gid}/roles/{rid}", body, details)
            self.need_base("manage_roles", f'edit role "{label}"')
            if not everyone and not s.bot_can_manage_role(rid):
                top = s.bot_top_role
                if top and top["id"] == rid:
                    self.block(f'role "{label}" is the bot\'s own highest role; a bot can\'t edit it',
                               f'Change "{label}" in the Discord app, or leave it as it is in the config.')
                else:
                    self.block(f'role "{label}" is at or above the bot\'s highest role, so the bot can\'t edit it',
                               "Drag the bot's role above it in Server Settings > Roles.")
            if "permissions" in body:
                added = tr.permissions & ~old
                missing = added & ~self.bot_base
                if missing and not self.bot_admin:
                    self.block(f'role "{label}": the bot can\'t grant {names(missing)} because it doesn\'t have them',
                               f"Give the bot's role {names(missing)}. " + FIX_ADMIN)

    def _hand_only(self, role: dict) -> bool:
        """Roles the bot can never edit: managed ones and its own highest role."""
        top = self.s.bot_top_role
        return bool(role.get("managed")) or (top is not None and top["id"] == role["id"])

    def role_order(self) -> None:
        if self.t.role_order is None:
            return
        s = self.s
        top = s.bot_top_role
        top_rank = s.role_rank(top["id"]) if top else -1
        live_ids = [r["id"] for r in s.roles if not s.is_everyone(r["id"])]
        desired = _merge_order([tr.ref for tr in self.t.role_order], live_ids, set())
        new_refs = [tr.ref for tr in self.t.role_order if tr.live is None]
        current = live_ids + new_refs  # Discord creates roles at the bottom

        def movable(ref: str) -> bool:
            if ref.startswith(NEW):
                return True
            role = s.role_by_id[ref]
            return not role.get("managed") and s.role_rank(ref) < top_rank

        if top is not None and top["id"] in desired:
            above = desired[: desired.index(top["id"])]
            for ref in above:
                if movable(ref):
                    self.block(f'can\'t put "{self._role_name(ref)}" above the bot\'s highest role "{top["name"]}"',
                               "Move it below the bot's role in the config, or drag the bot's role higher.")
        want = [r for r in desired if movable(r)]
        have = [r for r in current if movable(r)]
        if want == have:
            return
        body = None
        if not any(r.startswith(NEW) for r in want):
            slots = sorted((int(s.role_by_id[r]["position"]) for r in want), reverse=True)
            body = [{"id": r, "position": p} for r, p in zip(want, slots)
                    if int(s.role_by_id[r]["position"]) != p]
        moved = _moved(want, have)
        details = [("* " if r in moved else "  ") + self._role_name(r) for r in want]
        fixed = [r for r in desired if not movable(r)]
        if fixed:
            details.append("(kept where they are: " + ", ".join(self._role_name(r) for r in fixed) + ")")
        self.add(PH_ROLE_ORDER, "reorder roles (top to bottom, * = moves)", "PATCH",
                 f"/guilds/{self.gid}/roles", body, details)
        self.need_base("manage_roles", "reorder roles")

    def _role_name(self, ref: str) -> str:
        if ref.startswith(NEW):
            return ref[len(NEW):] + " (new)"
        return self.role_label(ref)

    def categories(self) -> None:
        for tc in self.t.categories:
            if tc.live is None:
                ows = tc.overwrites or {}
                body = {"name": tc.name, "type": CATEGORY, "permission_overwrites": _ow_body(ows)}
                details = _ow_details(ows)
                self.add(PH_CATEGORIES, f'create category "{tc.name}"', "POST", f"/guilds/{self.gid}/channels",
                         body, details)
                self.need_base("manage_channels", f'create category "{tc.name}"')
                for ow in ows.values():
                    self.can_grant(ow.allow | ow.deny, None, f'category "{tc.name}"')
            elif tc.name != tc.live["name"]:
                self.add(PH_CATEGORIES, f'rename category "{tc.live["name"]}" -> "{tc.name}"', "PATCH",
                         f"/channels/{tc.live['id']}", {"name": tc.name})
                self.need_in_channel(tc.live, "manage_channels", "rename it")

    def category_order(self) -> None:
        if not self.t.category_order:
            return
        s = self.s
        live_ids = [c["id"] for c in s.categories]
        desired = _merge_order([tc.ref for tc in self.t.categories], live_ids, set())
        current = live_ids + [tc.ref for tc in self.t.categories if tc.live is None]
        if desired == current:
            return
        body = None if any(r.startswith(NEW) for r in desired) else [
            {"id": r, "position": i} for i, r in enumerate(desired)]
        moved = _moved(desired, current)
        details = [("+ " if r.startswith(NEW) else "* " if r in moved else "  ") + self.cat_names.get(r, r)
                   for r in desired]
        self.add(PH_CATEGORY_ORDER, "reorder categories (top to bottom, + = new, * = moves)", "PATCH",
                 f"/guilds/{self.gid}/channels", body, details)
        self.need_base("manage_channels", "reorder categories")

    def channels(self) -> None:
        for tch in self.t.channels:
            if tch.live is None:
                ows = tch.overwrites
                if ows is None:
                    parent = next((c for c in self.t.categories if c.ref == tch.parent), None)
                    ows = copy.deepcopy(parent.overwrites) if parent and parent.overwrites is not None else {}
                body = {"name": tch.name, "type": tch.type, "parent_id": tch.parent or None,
                        "permission_overwrites": _ow_body(ows)}
                details = [f"type: {channel_type_name({'type': tch.type})}", f"in: {self.parent_label(tch.parent or None)}"]
                if tch.topic not in (UNSET, None):
                    body["topic"] = tch.topic
                    details.append(f"topic: {tch.topic}")
                details.extend(_ow_details(ows))
                self.add(PH_CHANNELS, f"create #{tch.name}", "POST", f"/guilds/{self.gid}/channels", body, details)
                self.need_base("manage_channels", f"create #{tch.name}")
                for ow in ows.values():
                    self.can_grant(ow.allow | ow.deny, None, f"#{tch.name}")
                continue

            live = tch.live
            body = {}
            details = []
            if tch.name != live["name"]:
                body["name"] = tch.name
                details.append(f'name: "{live["name"]}" -> "{tch.name}"')
            if tch.type is not None and tch.type != live["type"]:
                body["type"] = tch.type
                details.append(f"type: {channel_type_name(live)} -> {channel_type_name({'type': tch.type})}")
            if tch.topic is not UNSET and (live.get("topic") or None) != tch.topic:
                body["topic"] = tch.topic
                details.append(f"topic: {live.get('topic') or '(none)'!r} -> {tch.topic or '(none)'!r}")
            if body:
                self.add(PH_CHANNELS, f"update #{tch.name}", "PATCH", f"/channels/{live['id']}", body, details)
                self.need_in_channel(live, "manage_channels", f"edit #{tch.name}")
            if tch.parent is not UNSET and tch.parent != live.get("parent_id"):
                old = live.get("parent_id")
                self.add(PH_CHANNELS,
                         f"move #{tch.name}: {self.parent_label(old)} -> {self.parent_label(tch.parent)}",
                         "PATCH", f"/guilds/{self.gid}/channels",
                         [{"id": live["id"], "parent_id": tch.parent, "lock_permissions": False}])
                self.need_in_channel(live, "manage_channels", f"move #{tch.name}")
                dest = self.s.channel_by_id.get(tch.parent) if tch.parent else None
                if dest is not None:
                    self.need_in_channel(dest, "manage_channels", f"move #{tch.name} into it")

    def channel_order(self) -> None:
        s = self.s
        matched = {c.live["id"] for c in self.t.channels if c.live}
        for parent in self.t.ordered_parents:
            kids = [c for c in self.t.channels if c.parent == parent]
            live_kids = [c["id"] for c in s.children.get(parent, [])] if (parent is None or not parent.startswith(NEW)) else []
            desired_refs = _merge_order([c.ref for c in kids], live_kids, matched)
            by_ref = {c.ref: c for c in kids}

            def voice(ref: str) -> bool:
                if ref in by_ref:
                    tc = by_ref[ref]
                    return (tc.type if tc.type is not None else tc.live["type"]) in VOICE_LIKE
                return s.channel_by_id[ref]["type"] in VOICE_LIKE

            desired_refs.sort(key=voice)  # Discord always lists voice below text
            staying = [r for r in live_kids if r in desired_refs]
            arriving = [r for r in desired_refs if r not in live_kids]
            current = sorted(staying + arriving, key=voice)
            if desired_refs == current and not arriving:
                continue
            body = None if any(r.startswith(NEW) for r in desired_refs) else [
                {"id": r, "position": i} for i, r in enumerate(desired_refs)]
            label = self.parent_label(parent)

            def nm(ref: str) -> str:
                return "#" + (by_ref[ref].name if ref in by_ref else s.channel_by_id[ref]["name"])

            moved = _moved(desired_refs, current)
            details = [("+ " if r in arriving else "* " if r in moved else "  ") + nm(r) for r in desired_refs]
            self.add(PH_CHANNEL_ORDER, f"reorder channels in {label} (+ = arrives, * = moves)", "PATCH",
                     f"/guilds/{self.gid}/channels", body, details)
            self.need_base("manage_channels", f"reorder channels in {label}")

    def overwrites(self) -> None:
        for tc in self.t.categories:
            if tc.live is None or tc.overwrites is None:
                continue
            where = f'category "{tc.name}"'
            cat_change = self._diff_overwrites(tc.live, tc.overwrites, where)
            for tch in self.t.channels:
                if tch.parent != tc.ref or tch.live is None or tch.overwrites is None:
                    continue
                change = self._diff_overwrites(tch.live, tch.overwrites, f"#{tch.name}")
                # A channel that is synced now and stays synced gets the same update as its
                # category; fold it under the category's entry to keep the plan readable.
                if (change and cat_change and sorted(change.details) == sorted(cat_change.details)
                        and overwrites_of(tch.live) == overwrites_of(tc.live)
                        and tch.overwrites == tc.overwrites):
                    change.follows = cat_change.text
        for tch in self.t.channels:
            if tch.live is not None and tch.overwrites is not None and not self._in_managed_category(tch):
                self._diff_overwrites(tch.live, tch.overwrites, f"#{tch.name}")

    def _in_managed_category(self, tch: TChan) -> bool:
        return any(tc.ref == tch.parent and tc.live is not None and tc.overwrites is not None
                   for tc in self.t.categories)

    def _diff_overwrites(self, ch: dict, desired: dict[str, TOw], where: str) -> Change | None:
        live = overwrites_of(ch)
        details: list[str] = []
        changed_bits = 0
        for target, ow in desired.items():
            lo = live.get(target)
            if lo is None:
                details.append(f"+ add {ow.label}: {_allow_deny(ow.allow, ow.deny)}")
                changed_bits |= ow.allow | ow.deny
            elif (lo.allow, lo.deny) != (ow.allow, ow.deny):
                parts = []
                if lo.allow != ow.allow:
                    parts.append("allow " + _delta(lo.allow, ow.allow))
                if lo.deny != ow.deny:
                    parts.append("deny " + _delta(lo.deny, ow.deny))
                details.append(f"~ change {ow.label}: " + "; ".join(parts))
                changed_bits |= (lo.allow ^ ow.allow) | (lo.deny ^ ow.deny)
        for target, lo in live.items():
            if target not in desired:
                label = self.role_label(target) if lo.kind == OW_ROLE else self.s.target_label(lo)
                details.append(f"- remove {label}: {_allow_deny(lo.allow, lo.deny)}")
                changed_bits |= lo.allow | lo.deny
        if not details:
            return None
        change = self.add(PH_OVERWRITES, f"{where}: update overwrites", "PATCH", f"/channels/{ch['id']}",
                          {"permission_overwrites": _ow_body(desired)}, details)
        self.need_in_channel(ch, "manage_roles", f"edit overwrites on {where}")
        self.need_in_channel(ch, "manage_channels", f"edit overwrites on {where}")
        self.can_grant(changed_bits, ch, where)
        return change

    def deletes(self) -> None:
        for kind, live in self.t.deletes:
            if kind == "role":
                self.add(PH_DELETES, f'delete role "{live["name"]}"', "DELETE",
                         f"/guilds/{self.gid}/roles/{live['id']}")
                self.need_base("manage_roles", f'delete role "{live["name"]}"')
                if not self.s.bot_can_manage_role(live["id"]):
                    self.block(f'role "{live["name"]}" is at or above the bot\'s highest role, so it can\'t delete it',
                               "Drag the bot's role above it in Server Settings > Roles.")
            else:
                label = f'category "{live["name"]}"' if kind == "category" else f"#{live['name']}"
                self.add(PH_DELETES, f"delete {label}", "DELETE", f"/channels/{live['id']}")
                self.need_in_channel(live, "manage_channels", f"delete {label}")

    def bot_keeps_permissions(self) -> None:
        """Never reduce the bot's own permissions, server-wide or in any channel.

        For restore, going back to exactly what the bot had in the snapshot is allowed;
        otherwise restore could never undo an apply that gave the bot more."""
        s = self.s
        floor = None
        if self.t.restore_from is not None:
            floor = GuildState(_without_bot_admin(self.t.restore_from, self.t.restore_from["bot"]["role_ids"]))
        before = _without_bot_admin(s.raw, s.bot_role_ids)
        after = copy.deepcopy(before)
        roles = {r["id"]: r for r in after["roles"]}
        for tr in self.t.roles:
            if tr.live and tr.permissions is not UNSET and not self._hand_only(tr.live):
                if tr.live["id"] in s.bot_role_ids:
                    roles[tr.live["id"]]["permissions"] = str(tr.permissions & ~P.ADMINISTRATOR)
                else:
                    roles[tr.live["id"]]["permissions"] = str(tr.permissions)
        deleted = {live["id"] for kind, live in self.t.deletes}
        after["roles"] = [r for r in after["roles"] if r["id"] not in deleted]
        after["bot"]["role_ids"] = [r for r in after["bot"]["role_ids"] if r not in deleted]
        chans = {c["id"]: c for c in after["channels"]}
        for item in [*self.t.categories, *self.t.channels]:
            if item.live is None or item.overwrites is None:
                continue
            chans[item.live["id"]]["permission_overwrites"] = [
                {"id": o.target, "type": o.kind, "allow": str(o.allow), "deny": str(o.deny)}
                for o in item.overwrites.values() if not o.target.startswith(NEW)
            ]
        b, a = GuildState(before), GuildState(after)
        lost = b.bot_base_permissions & ~a.bot_base_permissions
        if floor is not None:
            lost &= floor.bot_base_permissions
        if lost:
            self.block(f"the bot would lose {names(lost)} server-wide", "Keep those on a role the bot has.")
        for cid, ch in b.channel_by_id.items():
            if cid in deleted or cid not in a.channel_by_id:
                continue
            lost = b.bot_channel_permissions(ch) & ~a.bot_channel_permissions(a.channel_by_id[cid])
            if floor is not None and cid in floor.channel_by_id:
                lost &= floor.bot_channel_permissions(floor.channel_by_id[cid])
            if lost:
                self.lost.setdefault(lost, []).append(self.where(ch))
        for bits, where in self.lost.items():
            self.block(f"the bot would lose {names(bits)} in: {', '.join(where)}",
                       "Allow what the bot needs to Buddy Helper on that channel or its category.")

    def bot_coverage(self) -> None:
        """With every config change made (by the tool and by hand), the bot must have its
        integration role's permissions in every channel."""
        s = self.s
        bot_role = next((tr for tr in self.t.roles if tr.live and tr.live.get("managed")
                         and tr.live.get("tags", {}).get("bot_id") == s.bot_user_id
                         and tr.permissions is not UNSET), None)
        if bot_role is None or self.t.restore_from is not None:
            return
        needs = bot_role.permissions & ~P.ADMINISTRATOR
        end = copy.deepcopy(s.raw)
        roles = {r["id"]: r for r in end["roles"]}
        for tr in self.t.roles:
            if tr.live and tr.permissions is not UNSET:
                roles[tr.live["id"]]["permissions"] = str(tr.permissions)
        chans = {c["id"]: c for c in end["channels"]}
        for item in [*self.t.categories, *self.t.channels]:
            if item.live is not None and item.overwrites is not None:
                chans[item.live["id"]]["permission_overwrites"] = [
                    {"id": o.target, "type": o.kind, "allow": str(o.allow), "deny": str(o.deny)}
                    for o in item.overwrites.values() if not o.target.startswith(NEW)]
        e = GuildState(end)
        missing_base = needs & ~e.bot_base_permissions
        if missing_base:
            self.warnings.append(f"once the roles match the config, the bot won't have {names(missing_base)} "
                                 f'(listed on "{bot_role.name}"); check the bot\'s roles in the config')
            return
        gaps: dict[int, list[str]] = {}
        for ch in e.channel_by_id.values():
            missing = needs & ~e.bot_channel_permissions(ch)
            if missing:
                gaps.setdefault(missing, []).append(self.where(ch))
        for bits, where in gaps.items():
            self.warnings.append(f'once everything matches the config, the bot would be missing {names(bits)} in: '
                                 + ", ".join(where) + f' (allow them to "{bot_role.name}" there in the config)')

    def finish(self) -> None:
        if any(int(self.s.role_by_id[r]["permissions"]) & P.ADMINISTRATOR
               for r in self.s.bot_role_ids if r in self.s.role_by_id):
            self.warnings.append("the bot has Administrator. If that was only for this apply, turn it off afterwards "
                                 "(Server Settings > Roles).")
        if self.cant_see:
            self.blockers.insert(0, "the bot can't see these, so it can't change them: " + ", ".join(self.cant_see))
            fix = ("Let the bot see them: allow view_channel to Buddy Helper on them in the Discord app. "
                   + FIX_ADMIN)
            if fix not in self.fixes:
                self.fixes.insert(0, fix)


def build_plan(state: GuildState, target: Target) -> Plan:
    b = _Builder(state, target)
    if not target.errors:
        b.roles()
        b.role_order()
        b.categories()
        b.category_order()
        b.channels()
        b.channel_order()
        b.overwrites()
        b.deletes()
        b.bot_keeps_permissions()
        b.bot_coverage()
        b.finish()
    return Plan(b.changes, b.warnings, list(target.errors), b.blockers, b.fixes, b.manual)


# ---------------------------------------------------------------------------
# helpers


def _merge_order(desired: list[str], live_order: list[str], skip: set[str]) -> list[str]:
    """Desired order, with live items the config doesn't mention kept after their live predecessor."""
    result = list(desired)
    listed = set(desired) | skip
    for i, ref in enumerate(live_order):
        if ref in listed:
            continue
        prev = next((live_order[j] for j in range(i - 1, -1, -1) if live_order[j] in result), None)
        result.insert(result.index(prev) + 1 if prev else 0, ref)
    return result


def _moved(desired: list[str], current: list[str]) -> set[str]:
    """Items whose place relative to the others (present in both lists) changes."""
    common = set(desired) & set(current)
    d = [r for r in desired if r in common]
    c = [r for r in current if r in common]
    # Longest common subsequence keeps the most items still; everything else "moves".
    n, m = len(d), len(c)
    lcs = [[0] * (m + 1) for _ in range(n + 1)]
    for i in range(n - 1, -1, -1):
        for j in range(m - 1, -1, -1):
            lcs[i][j] = lcs[i + 1][j + 1] + 1 if d[i] == c[j] else max(lcs[i + 1][j], lcs[i][j + 1])
    still: set[str] = set()
    i = j = 0
    while i < n and j < m:
        if d[i] == c[j]:
            still.add(d[i])
            i += 1
            j += 1
        elif lcs[i + 1][j] >= lcs[i][j + 1]:
            i += 1
        else:
            j += 1
    return common - still


def _without_bot_admin(raw: dict, bot_role_ids: list[str]) -> dict:
    """Snapshot copy with Administrator removed from the bot's roles, so a temporary
    Administrator grant doesn't hide a permission the bot would really lose."""
    out = copy.deepcopy(raw)
    for r in out["roles"]:
        if r["id"] in bot_role_ids:
            r["permissions"] = str(int(r["permissions"]) & ~P.ADMINISTRATOR)
    return out


def _role_color(role: dict) -> int:
    colors = role.get("colors") or {}
    return int(colors.get("primary_color", role.get("color", 0)) or 0)


def _hex(value: int) -> str:
    return f"#{value:06x}" if value else "none"


def _perm_delta(old: int, new: int) -> list[str]:
    out = []
    if new & ~old:
        out.append("permissions + " + names(new & ~old))
    if old & ~new:
        out.append("permissions - " + names(old & ~new))
    return out


def _delta(old: int, new: int) -> str:
    parts = []
    if new & ~old:
        parts.append("+ " + names(new & ~old))
    if old & ~new:
        parts.append("- " + names(old & ~new))
    return "; ".join(parts)


def _allow_deny(allow: int, deny: int) -> str:
    if not allow and not deny:
        return "neutral (allows and denies nothing)"
    parts = []
    if allow:
        parts.append(f"allow {names(allow)}")
    if deny:
        parts.append(f"deny {names(deny)}")
    return " | ".join(parts)


def _ow_body(ows: dict[str, TOw]) -> list[dict]:
    return [{"id": o.target, "type": o.kind, "allow": str(o.allow), "deny": str(o.deny)} for o in ows.values()]


def _ow_details(ows: dict[str, TOw]) -> list[str]:
    if not ows:
        return ["overwrites: none"]
    return [f"overwrite {o.label}: {_allow_deny(o.allow, o.deny)}" for o in ows.values()]


# ---------------------------------------------------------------------------
# output


def render_plan(plan: Plan) -> str:
    out: list[str] = []
    if plan.errors:
        out.append("CONFIG ERRORS (fix these first)")
        out.extend(f"  ! {e}" for e in plan.errors)
        out.append("")
    for phase, title in PHASES.items():
        items = [c for c in plan.changes if c.phase == phase]
        if not items:
            continue
        out.append(f"{title.upper()} ({len(items)})")
        for c in items:
            if c.follows:
                continue
            sym = {"POST": "+", "DELETE": "-"}.get(c.method, "~")
            out.append(f"  {sym} {c.text}")
            out.extend(f"        {d}" for d in c.details)
            followers = [f.text.split(":")[0] for f in items if f.follows == c.text]
            if followers:
                out.append(f"        (same update on {len(followers)} synced channel(s): {', '.join(followers)})")
        out.append("")
    if plan.manual:
        out.append("DO BY HAND (the bot can't change these roles; apply leaves them alone)")
        for text, details in plan.manual:
            out.append(f"  > {text}")
            out.extend(f"        {d}" for d in details)
        out.append("")
    if plan.warnings:
        out.append("WARNINGS")
        out.extend(f"  - {w}" for w in plan.warnings)
        out.append("")
    if plan.blockers:
        out.append("THE BOT CAN'T DO THIS YET (apply will refuse)")
        out.extend(f"  x {b}" for b in plan.blockers)
        out.append("  How to fix:")
        out.extend(f"    - {f}" for f in plan.fixes)
        out.append("")
    if not plan.changes and not plan.errors:
        out.append("No changes for the tool to make." if plan.manual else "No changes. The server matches the config.")
    else:
        out.append(f"{len(plan.changes)} change(s).")
    return "\n".join(out)
