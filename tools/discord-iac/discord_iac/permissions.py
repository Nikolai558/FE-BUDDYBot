"""Discord permission flags, by name.

Source: discord/discord-api-docs, developers/topics/permissions.mdx,
"Bitwise Permission Flags" table, checked 2026-10-12. Bit 47 is missing from
that table, but Discord still sets it in default @everyone permissions (it was
most likely the retired Clyde AI flag), so it shows as unknown_bit_47.
If Discord adds a flag, add it here; any bit the map doesn't know is shown as
`unknown_bit_<n>` so it is never silently dropped.
"""

from __future__ import annotations

PERMISSION_BITS: dict[str, int] = {
    "create_instant_invite": 0,
    "kick_members": 1,
    "ban_members": 2,
    "administrator": 3,
    "manage_channels": 4,
    "manage_guild": 5,
    "add_reactions": 6,
    "view_audit_log": 7,
    "priority_speaker": 8,
    "stream": 9,
    "view_channel": 10,
    "send_messages": 11,
    "send_tts_messages": 12,
    "manage_messages": 13,
    "embed_links": 14,
    "attach_files": 15,
    "read_message_history": 16,
    "mention_everyone": 17,
    "use_external_emojis": 18,
    "view_guild_insights": 19,
    "connect": 20,
    "speak": 21,
    "mute_members": 22,
    "deafen_members": 23,
    "move_members": 24,
    "use_vad": 25,
    "change_nickname": 26,
    "manage_nicknames": 27,
    "manage_roles": 28,
    "manage_webhooks": 29,
    "manage_guild_expressions": 30,
    "use_application_commands": 31,
    "request_to_speak": 32,
    "manage_events": 33,
    "manage_threads": 34,
    "create_public_threads": 35,
    "create_private_threads": 36,
    "use_external_stickers": 37,
    "send_messages_in_threads": 38,
    "use_embedded_activities": 39,
    "moderate_members": 40,
    "view_creator_monetization_analytics": 41,
    "use_soundboard": 42,
    "create_guild_expressions": 43,
    "create_events": 44,
    "use_external_sounds": 45,
    "send_voice_messages": 46,
    "set_voice_channel_status": 48,
    "send_polls": 49,
    "use_external_apps": 50,
    "pin_messages": 51,
    "bypass_slowmode": 52,
}

# Names the Discord client shows, where they differ enough to cause confusion.
CLIENT_NAMES: dict[str, str] = {
    "manage_roles": "Manage Roles / Manage Permissions (in channels)",
    "use_vad": "Use Voice Activity",
    "moderate_members": "Timeout Members",
    "manage_guild": "Manage Server",
    "manage_guild_expressions": "Manage Expressions",
    "create_guild_expressions": "Create Expressions",
}

_BIT_TO_NAME = {bit: name for name, bit in PERMISSION_BITS.items()}
_UNKNOWN_PREFIX = "unknown_bit_"

ALL_KNOWN = 0
for _bit in PERMISSION_BITS.values():
    ALL_KNOWN |= 1 << _bit

ADMINISTRATOR = 1 << PERMISSION_BITS["administrator"]
VIEW_CHANNEL = 1 << PERMISSION_BITS["view_channel"]
MANAGE_ROLES = 1 << PERMISSION_BITS["manage_roles"]
MANAGE_CHANNELS = 1 << PERMISSION_BITS["manage_channels"]

# Server-level permissions that mark a role as staff for the audit's checks.
STAFF_MARKERS = (
    "administrator",
    "manage_guild",
    "manage_roles",
    "manage_channels",
    "manage_messages",
    "manage_threads",
    "kick_members",
    "ban_members",
    "moderate_members",
)


class UnknownPermission(ValueError):
    pass


def bit(name: str) -> int:
    return 1 << PERMISSION_BITS[name]


def to_names(value: int | str) -> list[str]:
    """Bitfield (int or the API's string form) to a list of names in bit order."""
    value = int(value)
    names = []
    n = 0
    while value >> n:
        if (value >> n) & 1:
            names.append(_BIT_TO_NAME.get(n, f"{_UNKNOWN_PREFIX}{n}"))
        n += 1
    return names


def to_bits(names: list[str] | None) -> int:
    """List of names to a bitfield. Raises UnknownPermission on a typo."""
    value = 0
    for name in names or []:
        key = str(name).strip().lower()
        if key in PERMISSION_BITS:
            value |= 1 << PERMISSION_BITS[key]
        elif key.startswith(_UNKNOWN_PREFIX) and key[len(_UNKNOWN_PREFIX):].isdigit():
            value |= 1 << int(key[len(_UNKNOWN_PREFIX):])
        else:
            raise UnknownPermission(f"unknown permission name {name!r}")
    return value


def has(value: int, name: str) -> bool:
    return bool(value & bit(name))
