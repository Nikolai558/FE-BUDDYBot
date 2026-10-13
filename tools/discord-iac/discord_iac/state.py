"""Live server state: fetching it, snapshotting it, and answering questions about it."""

from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from . import permissions as P
from .api import DiscordClient, DiscordError

SNAPSHOT_VERSION = 1

CATEGORY = 4
CHANNEL_TYPES = {
    0: "text",
    2: "voice",
    5: "announcement",
    13: "stage",
    15: "forum",
    16: "media",
}
TYPE_IDS = {name: type_id for type_id, name in CHANNEL_TYPES.items()}
VOICE_LIKE = {2, 13}
TOPIC_TYPES = {0, 5, 15, 16}

OW_ROLE = 0
OW_MEMBER = 1


@dataclass(frozen=True)
class Overwrite:
    target_id: str
    kind: int  # OW_ROLE or OW_MEMBER
    allow: int
    deny: int


def fetch_snapshot(client: DiscordClient, guild_id: str) -> dict:
    """Read everything the tool needs from the API into a plain JSON-able dict."""
    me = client.get_current_user()
    guild = client.get_guild(guild_id)
    roles = client.get_roles(guild_id)
    channels = client.get_channels(guild_id)
    bot_member = client.get_member(guild_id, me["id"])
    try:
        counts = client.get_role_member_counts(guild_id)
    except DiscordError:
        counts = None

    members: dict[str, dict] = {}
    for ch in channels:
        for ow in ch.get("permission_overwrites", []):
            if int(ow["type"]) == OW_MEMBER and ow["id"] not in members:
                members[ow["id"]] = _member_label(client, guild_id, ow["id"])

    return {
        "version": SNAPSHOT_VERSION,
        "taken_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "guild": {"id": guild["id"], "name": guild["name"], "owner_id": guild["owner_id"]},
        "bot": {
            "user_id": me["id"],
            "username": me.get("username"),
            "role_ids": bot_member.get("roles", []),
        },
        "roles": roles,
        "role_member_counts": counts,
        "channels": channels,
        "members": members,
    }


def _member_label(client: DiscordClient, guild_id: str, user_id: str) -> dict:
    try:
        m = client.get_member(guild_id, user_id)
    except DiscordError as exc:
        if exc.status == 404:
            return {"name": None, "in_guild": False}
        raise
    user = m.get("user", {})
    name = m.get("nick") or user.get("global_name") or user.get("username")
    return {"name": name, "username": user.get("username"), "in_guild": True}


def write_snapshot(snapshot: dict, directory: Path) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    path = directory / f"{stamp}.json"
    n = 1
    while path.exists():
        path = directory / f"{stamp}-{n}.json"
        n += 1
    path.write_text(json.dumps(snapshot, indent=2, ensure_ascii=False), encoding="utf-8")
    return path


def read_snapshot(path: Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("version") != SNAPSHOT_VERSION:
        raise ValueError(f"{path}: unsupported snapshot version {data.get('version')!r}")
    return data


def _snow(obj: dict) -> tuple[int, int]:
    return int(obj.get("position", 0)), int(obj["id"])


class GuildState:
    """Read-only view over a snapshot dict."""

    def __init__(self, snapshot: dict):
        self.raw = snapshot
        self.guild_id: str = snapshot["guild"]["id"]
        self.guild_name: str = snapshot["guild"]["name"]
        self.owner_id: str = snapshot["guild"]["owner_id"]
        self.bot_user_id: str = snapshot["bot"]["user_id"]
        self.bot_role_ids: list[str] = list(snapshot["bot"]["role_ids"])
        self.members: dict[str, dict] = snapshot.get("members", {})
        self.member_counts: dict[str, int] | None = snapshot.get("role_member_counts")

        # Roles, highest first. Equal positions sort by id (docs); like the client
        # and discord.py, the older (lower id) role counts as higher.
        self.roles: list[dict] = sorted(
            snapshot["roles"], key=lambda r: (int(r.get("position", 0)), -int(r["id"])), reverse=True
        )
        self.role_by_id: dict[str, dict] = {r["id"]: r for r in self.roles}
        self._rank = {r["id"]: i for i, r in enumerate(reversed(self.roles))}

        channels = snapshot["channels"]
        self.channel_by_id: dict[str, dict] = {c["id"]: c for c in channels}
        self.categories: list[dict] = sorted((c for c in channels if c["type"] == CATEGORY), key=_snow)
        self.children: dict[str | None, list[dict]] = {}
        for c in channels:
            if c["type"] == CATEGORY:
                continue
            self.children.setdefault(c.get("parent_id"), []).append(c)
        for kids in self.children.values():
            kids.sort(key=lambda c: (c["type"] in VOICE_LIKE, *_snow(c)))
        for cat in self.categories:
            self.children.setdefault(cat["id"], [])

    # -- roles ----------------------------------------------------------------

    @property
    def everyone(self) -> dict:
        return self.role_by_id[self.guild_id]

    def is_everyone(self, role_id: str) -> bool:
        return role_id == self.guild_id

    def role_rank(self, role_id: str) -> int:
        """0 for @everyone, higher is higher in the list."""
        return self._rank[role_id]

    def role_perms(self, role_id: str) -> int:
        return int(self.role_by_id[role_id]["permissions"])

    @property
    def bot_top_role(self) -> dict | None:
        ranked = [r for r in self.bot_role_ids if r in self.role_by_id]
        if not ranked:
            return None
        return self.role_by_id[max(ranked, key=self.role_rank)]

    def bot_can_manage_role(self, role_id: str) -> bool:
        top = self.bot_top_role
        if top is None:
            return False
        return self.role_rank(role_id) < self.role_rank(top["id"])

    def is_bot_own_role(self, role: dict) -> bool:
        return role.get("tags", {}).get("bot_id") == self.bot_user_id

    def role_name_counts(self) -> dict[str, int]:
        counts: dict[str, int] = {}
        for r in self.roles:
            counts[r["name"]] = counts.get(r["name"], 0) + 1
        return counts

    def staff_roles(self) -> list[dict]:
        """Unmanaged roles holding any moderation/management permission.

        Excludes @everyone and roles the tool's bot holds (those are the bot's, not staff's)."""
        markers = 0
        for name in P.STAFF_MARKERS:
            markers |= P.bit(name)
        return [
            r for r in self.roles
            if not self.is_everyone(r["id"]) and not r.get("managed") and r["id"] not in self.bot_role_ids
            and int(r["permissions"]) & markers
        ]

    # -- permissions ------------------------------------------------------------

    def base_permissions(self, role_ids: list[str], user_id: str | None = None) -> int:
        """Server-level permissions for someone with these roles (docs' compute_base_permissions)."""
        if user_id is not None and user_id == self.owner_id:
            return P.ALL_KNOWN
        perms = self.role_perms(self.guild_id)
        for rid in role_ids:
            if rid in self.role_by_id:
                perms |= self.role_perms(rid)
        if perms & P.ADMINISTRATOR:
            return P.ALL_KNOWN
        return perms

    def channel_permissions(self, channel: dict, role_ids: list[str], user_id: str | None = None) -> int:
        """Effective permissions in a channel (docs' compute_overwrites)."""
        base = self.base_permissions(role_ids, user_id)
        if base == P.ALL_KNOWN:
            return base
        ows = overwrites_of(channel)
        perms = base
        everyone = ows.get(self.guild_id)
        if everyone:
            perms &= ~everyone.deny
            perms |= everyone.allow
        allow = deny = 0
        for rid in role_ids:
            ow = ows.get(rid)
            if ow and ow.kind == OW_ROLE:
                allow |= ow.allow
                deny |= ow.deny
        perms &= ~deny
        perms |= allow
        if user_id is not None:
            ow = ows.get(user_id)
            if ow and ow.kind == OW_MEMBER:
                perms &= ~ow.deny
                perms |= ow.allow
        return perms

    @property
    def bot_base_permissions(self) -> int:
        return self.base_permissions(self.bot_role_ids, self.bot_user_id)

    def bot_channel_permissions(self, channel: dict) -> int:
        return self.channel_permissions(channel, self.bot_role_ids, self.bot_user_id)

    # -- channels -------------------------------------------------------------

    def parent(self, channel: dict) -> dict | None:
        pid = channel.get("parent_id")
        return self.channel_by_id.get(pid) if pid else None

    def is_synced(self, channel: dict) -> bool | None:
        """True/False for channels in a category, None for uncategorized ones."""
        parent = self.parent(channel)
        if parent is None:
            return None
        return overwrites_of(channel) == overwrites_of(parent)

    def all_non_category_channels(self) -> list[dict]:
        out = list(self.children.get(None, []))
        for cat in self.categories:
            out.extend(self.children[cat["id"]])
        return out

    # -- labels -----------------------------------------------------------------

    def target_label(self, ow: Overwrite) -> str:
        if ow.kind == OW_ROLE:
            if self.is_everyone(ow.target_id):
                return "@everyone"
            role = self.role_by_id.get(ow.target_id)
            return role["name"] if role else f"(deleted role {ow.target_id})"
        info = self.members.get(ow.target_id) or {}
        name = info.get("name")
        if not info.get("in_guild", True):
            return f"member {ow.target_id} (no longer in server)"
        return f"member {name} ({ow.target_id})" if name else f"member {ow.target_id}"


def overwrites_of(channel: dict) -> dict[str, Overwrite]:
    return {
        ow["id"]: Overwrite(ow["id"], int(ow["type"]), int(ow["allow"]), int(ow["deny"]))
        for ow in channel.get("permission_overwrites", [])
    }


def channel_type_name(channel: dict) -> str:
    return CHANNEL_TYPES.get(channel["type"], f"type{channel['type']}")
