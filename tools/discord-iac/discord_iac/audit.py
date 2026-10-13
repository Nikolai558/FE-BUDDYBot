"""The `audit` report and its problem checks."""

from __future__ import annotations

import textwrap
from dataclasses import dataclass, field

from . import permissions as P
from .state import (
    OW_MEMBER,
    OW_ROLE,
    GuildState,
    Overwrite,
    channel_type_name,
    overwrites_of,
)

INDENT = "    "
WIDTH = 100

# Server-level permissions @everyone should never hold.
EVERYONE_DANGEROUS = (
    "administrator", "manage_guild", "manage_roles", "manage_channels", "manage_messages",
    "manage_threads", "manage_webhooks", "manage_nicknames", "manage_guild_expressions",
    "manage_events", "kick_members", "ban_members", "moderate_members", "mention_everyone",
    "view_audit_log", "mute_members", "deafen_members", "move_members",
)


@dataclass
class Problem:
    title: str
    lines: list[str] = field(default_factory=list)
    fix: str | None = None


def names(value: int) -> str:
    return ", ".join(P.to_names(value)) or "-"


def wrap(text: str, indent: str) -> list[str]:
    return textwrap.wrap(text, WIDTH, initial_indent=indent, subsequent_indent=indent + "  ") or [indent]


# ---------------------------------------------------------------------------
# report


def render_report(state: GuildState) -> str:
    out: list[str] = []
    top = state.bot_top_role
    out.append(f"SERVER  {state.guild_name}  (id {state.guild_id})")
    out.append(f"Taken   {state.raw['taken_at']}")
    out.append(
        f"Bot     user {state.bot_user_id}; highest role: "
        + (f"{top['name']} (#{_role_number(state, top['id'])} from the top)" if top else "none")
    )
    out.extend(wrap("Bot server permissions: " + _bot_perm_summary(state), "        "))
    out.append("")

    out.append(_heading("ROLES (top to bottom)"))
    for i, role in enumerate(state.roles, 1):
        label = "@everyone" if state.is_everyone(role["id"]) else role["name"]
        tags = []
        if role.get("managed"):
            tags.append("managed")
        if role.get("hoist"):
            tags.append("hoisted")
        if role.get("mentionable"):
            tags.append("mentionable")
        if top and role["id"] == top["id"]:
            tags.append("BOT'S HIGHEST ROLE")
        elif not state.is_everyone(role["id"]) and not state.bot_can_manage_role(role["id"]):
            tags.append("bot can't manage")
        count = _member_count(state, role)
        head = f"{i:>3}. {label}"
        meta = [f"id {role['id']}"]
        if count is not None:
            meta.append(f"{count} member{'s' if count != 1 else ''}")
        meta.extend(tags)
        out.append(f"{head}  [{'; '.join(meta)}]")
        perms = int(role["permissions"])
        if perms & P.ADMINISTRATOR:
            out.append(f"{INDENT}  ADMINISTRATOR (all permissions, ignores every overwrite)")
            rest = perms & ~P.ADMINISTRATOR
            if rest:
                out.extend(wrap("also set: " + names(rest), INDENT + "  "))
        else:
            out.extend(wrap(names(perms), INDENT + "  "))
    out.append("")

    out.append(_heading("CHANNELS (category -> channels, with overwrites)"))
    out.append("Legend: 'synced' = channel overwrites match the category's exactly.")
    out.append("")
    uncategorized = state.children.get(None, [])
    if uncategorized:
        out.append("(no category)")
        for ch in uncategorized:
            out.extend(_channel_lines(state, ch))
        out.append("")
    for cat in state.categories:
        kids = state.children.get(cat["id"], [])
        synced = sum(1 for c in kids if state.is_synced(c))
        out.append(f"CATEGORY {cat['name']}  [id {cat['id']}; {synced}/{len(kids)} channels synced]")
        out.extend(_overwrite_lines(state, overwrites_of(cat), INDENT))
        for ch in kids:
            out.extend(_channel_lines(state, ch))
        out.append("")
    return "\n".join(out)


def _channel_lines(state: GuildState, ch: dict) -> list[str]:
    synced = state.is_synced(ch)
    sync_text = {True: "synced", False: "NOT synced", None: "no category"}[synced]
    lines = [f"{INDENT}#{ch['name']}  [{channel_type_name(ch)}; id {ch['id']}; {sync_text}]"]
    if synced is False or synced is None:
        lines.extend(_overwrite_lines(state, overwrites_of(ch), INDENT * 2))
    return lines


def _overwrite_lines(state: GuildState, ows: dict[str, Overwrite], indent: str) -> list[str]:
    if not ows:
        return [f"{indent}(no overwrites)"]
    lines = []
    for ow in _ordered(state, ows):
        label = state.target_label(ow)
        parts = []
        if ow.allow:
            parts.append(f"allow: {names(ow.allow)}")
        if ow.deny:
            parts.append(f"deny: {names(ow.deny)}")
        if not parts:
            parts.append("(neutral, allows and denies nothing)")
        lines.extend(wrap(f"{label:<22} " + "  |  ".join(parts), indent))
    return lines


def _ordered(state: GuildState, ows: dict[str, Overwrite]) -> list[Overwrite]:
    def key(ow: Overwrite):
        if ow.kind == OW_MEMBER:
            return (2, 0)
        if state.is_everyone(ow.target_id):
            return (0, 0)
        return (1, -state.role_rank(ow.target_id) if ow.target_id in state.role_by_id else 1)

    return sorted(ows.values(), key=key)


def _heading(text: str) -> str:
    return f"{text}\n{'=' * len(text)}"


def _role_number(state: GuildState, role_id: str) -> int:
    return next(i for i, r in enumerate(state.roles, 1) if r["id"] == role_id)


def _member_count(state: GuildState, role: dict) -> int | None:
    if state.member_counts is None or state.is_everyone(role["id"]):
        return None
    return state.member_counts.get(role["id"], 0)


def _bot_perm_summary(state: GuildState) -> str:
    perms = state.bot_base_permissions
    if perms == P.ALL_KNOWN:
        return "ADMINISTRATOR (everything)"
    return names(perms)


# ---------------------------------------------------------------------------
# problems


def find_problems(state: GuildState) -> list[Problem]:
    checks = [
        _check_bot_basics,
        _check_everyone_deny_blocks_staff,
        _check_unsynced,
        _check_admin_roles,
        _check_member_overwrites,
        _check_unmanageable_roles,
        _check_everyone_dangerous,
        _check_duplicates,
        _check_orphan_overwrites,
        _check_bot_cannot_view,
        _check_unknown_bits,
    ]
    problems: list[Problem] = []
    for check in checks:
        problem = check(state)
        if problem and problem.lines:
            problems.append(problem)
    return problems


def render_problems(problems: list[Problem]) -> str:
    out = [_heading(f"LIKELY PROBLEMS ({len(problems)})")]
    if not problems:
        out.append("None found.")
        return "\n".join(out)
    for i, p in enumerate(problems, 1):
        out.append("")
        out.append(f"[{i}] {p.title}")
        for line in p.lines:
            out.extend(wrap("- " + line, INDENT))
        if p.fix:
            out.extend(wrap("Fix: " + p.fix, INDENT))
    return "\n".join(out)


def _check_bot_basics(state: GuildState) -> Problem:
    p = Problem("The bot is missing permissions apply will need")
    perms = state.bot_base_permissions
    if not perms & P.MANAGE_ROLES:
        p.lines.append("no Manage Roles: it can't edit roles or channel overwrites")
    if not perms & P.MANAGE_CHANNELS:
        p.lines.append("no Manage Channels: it can't create, edit or move channels")
    if state.bot_top_role is None:
        p.lines.append("the bot has no role besides @everyone, so it can't manage any role")
    p.fix = ("audit only reads, so this doesn't block it. Before `apply`, give the bot's role these "
             "permissions (or temporarily Administrator).")
    return p


def _check_everyone_deny_blocks_staff(state: GuildState) -> Problem:
    """@everyone channel denies override server-level role grants, except Administrator."""
    p = Problem("Channels that deny @everyone something staff roles have no allow for")
    staff = [r for r in state.staff_roles() if not int(r["permissions"]) & P.ADMINISTRATOR]
    admins = [r["name"] for r in state.roles if int(r["permissions"]) & P.ADMINISTRATOR
              and not state.is_everyone(r["id"])]

    def check(target: dict, label: str) -> None:
        ows = overwrites_of(target)
        everyone = ows.get(state.guild_id)
        if not everyone or not everyone.deny:
            return
        denied_any_staff_allows = 0
        blocked: list[str] = []
        for role in staff:
            ow = ows.get(role["id"])
            if ow and ow.kind == OW_ROLE:
                denied_any_staff_allows |= ow.allow
        uncovered = everyone.deny & ~denied_any_staff_allows
        if not uncovered:
            return
        # Only report it when a staff role actually loses something it has at server level.
        for role in staff:
            server = state.base_permissions([role["id"]])
            here = state.channel_permissions(target, [role["id"]])
            lost = server & ~here & everyone.deny
            if lost:
                blocked.append(f"{role['name']} loses {names(lost)}")
        if blocked:
            p.lines.append(f"{label}: @everyone deny [{names(uncovered)}] with no staff allow; "
                           + "; ".join(blocked))

    for ch in state.children.get(None, []):
        check(ch, f"#{ch['name']}")
    for cat in state.categories:
        kids = state.children.get(cat["id"], [])
        n_synced = sum(1 for c in kids if state.is_synced(c))
        check(cat, f"category {cat['name']}" + (f" (and its {n_synced} synced channels)" if n_synced else ""))
        for ch in kids:
            if not state.is_synced(ch):
                check(ch, f"#{ch['name']} in {cat['name']}")
    p.fix = ("an @everyone deny in a channel beats a role's server-level permission; only an allow "
             "overwrite for that role (or Administrator) gets it back. "
             + (f"Administrator roles not affected: {', '.join(admins)}. " if admins else "")
             + "Either allow the permission to the staff role on the channel/category, or stop "
               "granting it to @everyone at server level and drop the deny.")
    return p


def _check_unsynced(state: GuildState) -> Problem:
    p = Problem("Channels not synced to a category that has overwrites")
    for cat in state.categories:
        cat_ows = overwrites_of(cat)
        if not cat_ows:
            continue
        for ch in state.children.get(cat["id"], []):
            if state.is_synced(ch):
                continue
            diff = _overwrite_diff(state, cat_ows, overwrites_of(ch))
            p.lines.append(f"#{ch['name']} in {cat['name']}: " + "; ".join(diff))
    p.fix = ("if there's no reason for the difference, set `sync: true` on the channel in desired.yaml "
             "(or click 'Sync Now' in the channel's Permissions page).")
    return p


def _overwrite_diff(state: GuildState, cat: dict[str, Overwrite], ch: dict[str, Overwrite]) -> list[str]:
    out = []
    for tid in sorted(set(cat) | set(ch), key=lambda t: (t != state.guild_id, t)):
        a, b = cat.get(tid), ch.get(tid)
        label = state.target_label(a or b)
        if a and not b:
            out.append(f"missing category's {label} overwrite")
        elif b and not a:
            out.append(f"extra {label} overwrite (allow {names(b.allow)} / deny {names(b.deny)})")
        elif a != b:
            bits = []
            if a.allow != b.allow:
                bits.append(_delta("allow", a.allow, b.allow))
            if a.deny != b.deny:
                bits.append(_delta("deny", a.deny, b.deny))
            out.append(f"{label} " + ", ".join(bits))
    return out


def _delta(kind: str, cat: int, ch: int) -> str:
    plus, minus = ch & ~cat, cat & ~ch
    parts = []
    if plus:
        parts.append(f"+{kind} {names(plus)}")
    if minus:
        parts.append(f"-{kind} {names(minus)}")
    return " ".join(parts)


def _check_admin_roles(state: GuildState) -> Problem:
    p = Problem("Roles with Administrator")
    for role in state.roles:
        if int(role["permissions"]) & P.ADMINISTRATOR and not state.is_everyone(role["id"]):
            count = _member_count(state, role)
            extra = f" ({count} members)" if count is not None else ""
            managed = " - managed role (a bot/integration asked for Administrator)" if role.get("managed") else ""
            p.lines.append(f"{role['name']}{extra}{managed}")
    p.fix = ("Administrator bypasses every overwrite and can't be limited per channel. Keep it to the "
             "fewest people possible; give moderators specific permissions instead.")
    return p


def _check_member_overwrites(state: GuildState) -> Problem:
    p = Problem("Personal (member) overwrites")
    for ch in [*state.categories, *state.all_non_category_channels()]:
        if ch["type"] != 4 and state.is_synced(ch):
            continue  # shown once on the category
        for ow in overwrites_of(ch).values():
            if ow.kind == OW_MEMBER:
                where = f"category {ch['name']}" if ch["type"] == 4 else f"#{ch['name']}"
                p.lines.append(f"{where}: {state.target_label(ow)} allow [{names(ow.allow)}] deny [{names(ow.deny)}]")
    p.fix = "replace with a role where possible; personal overwrites are easy to forget about."
    return p


def _check_unmanageable_roles(state: GuildState) -> Problem:
    p = Problem("Roles the bot can't manage (at or above its highest role)")
    top = state.bot_top_role
    for role in state.roles:
        if state.is_everyone(role["id"]):
            continue
        if top and role["id"] == top["id"]:
            continue  # a bot can never edit its own highest role; that's expected, not a problem
        if not state.bot_can_manage_role(role["id"]):
            note = ""
            if role.get("managed"):
                note = " (managed; never modified anyway)"
            p.lines.append(role["name"] + note)
    p.fix = ("in Server Settings > Roles, drag the bot's role above every role you want the tool to edit. "
             "It can never edit its own highest role or anything above it.")
    return p


def _check_everyone_dangerous(state: GuildState) -> Problem:
    p = Problem("@everyone has moderation or management permissions at server level")
    perms = int(state.everyone["permissions"])
    bad = [n for n in EVERYONE_DANGEROUS if P.has(perms, n)]
    if bad:
        p.lines.append(", ".join(bad))
    p.fix = "remove them from @everyone in desired.yaml; give them to staff roles instead."
    return p


def _check_duplicates(state: GuildState) -> Problem:
    p = Problem("Duplicate names (plan/apply match by name and will refuse to guess)")
    for name, count in state.role_name_counts().items():
        if count > 1:
            p.lines.append(f"role {name!r} x{count}")
    seen: dict[str, int] = {}
    for c in state.categories:
        seen[c["name"]] = seen.get(c["name"], 0) + 1
    for name, count in seen.items():
        if count > 1:
            p.lines.append(f"category {name!r} x{count}")
    seen = {}
    for c in state.all_non_category_channels():
        seen[c["name"]] = seen.get(c["name"], 0) + 1
    for name, count in seen.items():
        if count > 1:
            p.lines.append(f"channel {name!r} x{count}")
    p.fix = "rename one, or keep the `id:` that current.yaml writes for duplicates so plan knows which is which."
    return p


def _check_orphan_overwrites(state: GuildState) -> Problem:
    p = Problem("Overwrites for deleted roles or members who left")
    for ch in [*state.categories, *state.all_non_category_channels()]:
        for ow in overwrites_of(ch).values():
            gone = (ow.kind == OW_ROLE and ow.target_id not in state.role_by_id) or (
                ow.kind == OW_MEMBER and not (state.members.get(ow.target_id) or {}).get("in_guild", True)
            )
            if gone:
                p.lines.append(f"{ch['name']}: {state.target_label(ow)}")
    p.fix = "harmless but clutter; drop them from desired.yaml and apply will remove them."
    return p


def _check_bot_cannot_view(state: GuildState) -> Problem:
    p = Problem("Channels the bot can't see")
    for ch in [*state.categories, *state.all_non_category_channels()]:
        if not state.bot_channel_permissions(ch) & P.VIEW_CHANNEL:
            p.lines.append(f"category {ch['name']}" if ch["type"] == 4
                           else f"#{ch['name']} ({channel_type_name(ch)})")
    p.fix = ("from 2026-11-16 Discord's API hides channels a bot can't view, so the tool won't see or manage "
             "these. Allow view_channel to the bot's role on them (or give it Administrator).")
    return p


def _check_unknown_bits(state: GuildState) -> Problem:
    p = Problem("Permission bits this tool doesn't have a name for")
    seen: set[str] = set()
    values = [int(r["permissions"]) for r in state.roles]
    for ch in state.channel_by_id.values():
        for ow in overwrites_of(ch).values():
            values += [ow.allow, ow.deny]
    for v in values:
        seen.update(n for n in P.to_names(v) if n.startswith("unknown_bit_"))
    if seen:
        p.lines.append(", ".join(sorted(seen)))
    p.fix = ("the bit isn't in Discord's permission table: either a new permission (look it up and add it "
             "to discord_iac/permissions.py) or a retired one Discord still sets. Kept as-is either way.")
    return p
