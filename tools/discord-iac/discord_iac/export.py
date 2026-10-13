"""Write live state as YAML in the same schema `plan`/`apply` read."""

from __future__ import annotations

import json
import re

import yaml

from . import permissions as P
from .state import (
    OW_MEMBER,
    TOPIC_TYPES,
    GuildState,
    Overwrite,
    channel_type_name,
    overwrites_of,
)

WRAP = 96

HEADER = """\
# Discord server config for discord-iac.
#
# Exported from "{guild}" at {taken_at}. To make changes: copy this file to
# desired.yaml, edit it, then run `plan` and `apply`. Full reference: README.md.
#
#   roles:          top to bottom. @everyone is always last and only its
#                   permissions can change. `managed: true` roles (bots,
#                   integrations, Server Booster) are never modified.
#   uncategorized:  channels that are not in a category.
#   categories:     top to bottom, each with its channels top to bottom.
#                   A channel either has `sync: true` (same overwrites as its
#                   category) or its own `overwrites`.
#   overwrites:     keyed by role name, "@everyone", "member:<user id>", or
#                   "role:<role id>" when a role name is ambiguous.
#                   Leaving `allow`/`deny` out means an empty list.
#   id:             only written when two things share a name; it tells
#                   plan which one you mean.
#   delete:         nothing is ever deleted unless it is listed here.
"""


def export_yaml(state: GuildState) -> str:
    out: list[str] = [HEADER.format(guild=state.guild_name, taken_at=state.raw["taken_at"])]
    role_dupes = {n for n, c in state.role_name_counts().items() if c > 1}

    out.append("roles:")
    for role in state.roles:
        is_everyone = state.is_everyone(role["id"])
        out.append(f"  - name: {_scalar('@everyone' if is_everyone else role['name'])}")
        if role["name"] in role_dupes and not is_everyone:
            out.append(f"    id: {_scalar(role['id'])}")
        if role.get("managed"):
            out.append(f"    managed: true  # {_managed_reason(state, role)}; never modified")
        if not is_everyone:
            out.append(f"    color: {_color(role)}")
            out.append(f"    hoist: {_bool(role.get('hoist'))}")
            out.append(f"    mentionable: {_bool(role.get('mentionable'))}")
        out.extend(_perm_list("permissions", P.to_names(role["permissions"]), indent=4))

    channel_dupes = _channel_dupes(state)
    category_dupes = _dupes(c["name"] for c in state.categories)

    out.append("")
    uncategorized = state.children.get(None, [])
    if uncategorized:
        out.append("uncategorized:")
        for ch in uncategorized:
            out.extend(_channel(state, ch, indent=2, dupes=channel_dupes, role_dupes=role_dupes))
    else:
        out.append("uncategorized: []")

    out.append("")
    out.append("categories:" if state.categories else "categories: []")
    for cat in state.categories:
        out.append(f"  - name: {_scalar(cat['name'])}")
        if cat["name"] in category_dupes:
            out.append(f"    id: {_scalar(cat['id'])}")
        out.extend(_overwrites(state, overwrites_of(cat), indent=4, role_dupes=role_dupes))
        kids = state.children.get(cat["id"], [])
        if not kids:
            out.append("    channels: []")
            continue
        out.append("    channels:")
        for ch in kids:
            out.extend(_channel(state, ch, indent=6, dupes=channel_dupes, role_dupes=role_dupes))

    out.append("")
    out.append("# Nothing is deleted unless it is listed here, by name (or \"id:<id>\").")
    out.append("delete:")
    out.append("  roles: []")
    out.append("  categories: []")
    out.append("  channels: []")
    out.append("")
    return "\n".join(out)


def _channel(state: GuildState, ch: dict, *, indent: int, dupes: set[str], role_dupes: set[str]) -> list[str]:
    pad = " " * indent
    lines = [f"{pad}- name: {_scalar(ch['name'])}"]
    pad2 = pad + "  "
    if ch["name"] in dupes:
        lines.append(f"{pad2}id: {_scalar(ch['id'])}")
    lines.append(f"{pad2}type: {channel_type_name(ch)}")
    if ch["type"] in TOPIC_TYPES and ch.get("topic"):
        lines.append(f"{pad2}topic: {_scalar(ch['topic'])}")
    if state.is_synced(ch):
        lines.append(f"{pad2}sync: true")
    else:
        lines.extend(_overwrites(state, overwrites_of(ch), indent=indent + 2, role_dupes=role_dupes))
    return lines


def _overwrites(state: GuildState, ows: dict[str, Overwrite], *, indent: int, role_dupes: set[str]) -> list[str]:
    pad = " " * indent
    if not ows:
        return [f"{pad}overwrites: {{}}"]
    lines = [f"{pad}overwrites:"]
    for ow in _sorted_overwrites(state, ows.values()):
        key, comment = overwrite_key(state, ow, role_dupes)
        lines.append(f"{pad}  {_scalar(key)}:" + (f"  # {comment}" if comment else ""))
        allow, deny = P.to_names(ow.allow), P.to_names(ow.deny)
        if not allow and not deny:
            lines.append(f"{pad}    allow: []")
            lines.append(f"{pad}    deny: []")
        if allow:
            lines.extend(_perm_list("allow", allow, indent=indent + 4))
        if deny:
            lines.extend(_perm_list("deny", deny, indent=indent + 4))
    return lines


def overwrite_key(state: GuildState, ow: Overwrite, role_dupes: set[str]) -> tuple[str, str | None]:
    """Config key for an overwrite target, plus an optional explanatory comment."""
    if ow.kind == OW_MEMBER:
        info = state.members.get(ow.target_id) or {}
        if not info.get("in_guild", True):
            return f"member:{ow.target_id}", "no longer in the server"
        return f"member:{ow.target_id}", info.get("name")
    if state.is_everyone(ow.target_id):
        return "@everyone", None
    role = state.role_by_id.get(ow.target_id)
    if role is None:
        return f"role:{ow.target_id}", "deleted role"
    if role["name"] in role_dupes or role["name"] == "@everyone" or _looks_like_id_key(role["name"]):
        return f"role:{ow.target_id}", role["name"]
    return role["name"], None


def _sorted_overwrites(state: GuildState, ows) -> list[Overwrite]:
    # @everyone first, then roles top to bottom, then members.
    def key(ow: Overwrite):
        if ow.kind == OW_MEMBER:
            return (2, 0, int(ow.target_id))
        if state.is_everyone(ow.target_id):
            return (0, 0, 0)
        rank = state.role_rank(ow.target_id) if ow.target_id in state.role_by_id else -1
        return (1, -rank, int(ow.target_id))

    return sorted(ows, key=key)


def _perm_list(key: str, names: list[str], *, indent: int) -> list[str]:
    pad = " " * indent
    if not names:
        return [f"{pad}{key}: []"]
    lines: list[str] = []
    current = f"{pad}{key}: ["
    for i, name in enumerate(names):
        piece = name + (", " if i < len(names) - 1 else "]")
        if len(current) + len(piece.rstrip()) > WRAP and not current.endswith("["):
            lines.append(current.rstrip())
            current = pad + "  "
        current += piece
    lines.append(current)
    return lines


def _managed_reason(state: GuildState, role: dict) -> str:
    tags = role.get("tags", {})
    if "bot_id" in tags:
        return "this tool's bot" if tags["bot_id"] == state.bot_user_id else "bot role"
    if "premium_subscriber" in tags:
        return "Server Booster role"
    if "integration_id" in tags:
        return "integration role"
    return "managed role"


def _color(role: dict) -> str:
    colors = role.get("colors") or {}
    value = colors.get("primary_color", role.get("color", 0)) or 0
    if not value:
        return "null"
    return f'"#{value:06x}"'


def _bool(value) -> str:
    return "true" if value else "false"


_PLAIN = re.compile(r"^[A-Za-z][A-Za-z0-9 _.\-'()&]*$")


def _scalar(value: str) -> str:
    """A YAML scalar for a string: plain when that round-trips, else double-quoted."""
    s = str(value)
    if _PLAIN.match(s) and not s.endswith(" "):
        try:
            if yaml.safe_load(f"k: {s}") == {"k": s}:
                return s
        except yaml.YAMLError:
            pass
    # JSON strings are valid YAML double-quoted scalars.
    return json.dumps(s, ensure_ascii=False)


def _looks_like_id_key(name: str) -> bool:
    return bool(re.match(r"^(role|member|id):\d+$", name))


def _dupes(names) -> set[str]:
    seen: set[str] = set()
    dupes: set[str] = set()
    for n in names:
        (dupes if n in seen else seen).add(n)
    return dupes


def _channel_dupes(state: GuildState) -> set[str]:
    return _dupes(c["name"] for c in state.all_non_category_channels())
