"""Load and validate desired.yaml."""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

import yaml

from . import permissions as P
from .state import TYPE_IDS


class ConfigError(ValueError):
    pass


class _Unset:
    def __repr__(self) -> str:
        return "UNSET"

    def __bool__(self) -> bool:
        return False


UNSET = _Unset()  # "key left out": leave that setting alone


@dataclass
class OverwriteSpec:
    key: str  # as written: role name, "@everyone", "role:<id>", "member:<id>"
    allow: int
    deny: int


@dataclass
class RoleSpec:
    name: str
    id: str | None = None
    managed: bool = False
    color: object = UNSET  # int, None (no color) or UNSET
    hoist: object = UNSET
    mentionable: object = UNSET
    permissions: object = UNSET  # int or UNSET

    @property
    def is_everyone(self) -> bool:
        return self.name == "@everyone" and self.id is None


@dataclass
class ChannelSpec:
    name: str
    category: str | None  # category name as written, None for uncategorized
    id: str | None = None
    type: str | None = None
    topic: object = UNSET
    sync: bool = False
    overwrites: list[OverwriteSpec] | None = None  # None = leave alone


@dataclass
class CategorySpec:
    name: str
    id: str | None = None
    overwrites: list[OverwriteSpec] | None = None
    channels: list[ChannelSpec] = field(default_factory=list)


@dataclass
class Config:
    roles: list[RoleSpec] | None
    uncategorized: list[ChannelSpec] | None
    categories: list[CategorySpec] | None
    delete_roles: list[str]
    delete_categories: list[str]
    delete_channels: list[str]


ROLE_KEYS = {"name", "id", "managed", "color", "hoist", "mentionable", "permissions"}
CHANNEL_KEYS = {"name", "id", "type", "topic", "sync", "overwrites"}
CATEGORY_KEYS = {"name", "id", "overwrites", "channels"}
TOP_KEYS = {"roles", "uncategorized", "categories", "delete"}
DELETE_KEYS = {"roles", "categories", "channels"}


def load_config(path: Path) -> Config:
    if not path.exists():
        raise ConfigError(f"{path} not found (copy current.yaml to it and edit)")
    try:
        data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    except yaml.YAMLError as exc:
        raise ConfigError(f"{path}: invalid YAML: {exc}") from None
    return parse_config(data, str(path))


def parse_config(data: dict, where: str = "config") -> Config:
    _check_keys(data, TOP_KEYS, where)
    roles = None
    if "roles" in data:
        roles = [_role(r, f"{where}: roles[{i}]") for i, r in enumerate(_list(data["roles"], f"{where}: roles"))]
        names = [r.name for r in roles if r.id is None]
        dupes = {n for n in names if names.count(n) > 1}
        if dupes:
            raise ConfigError(f"{where}: roles listed twice: {', '.join(sorted(dupes))} (add `id:` to tell them apart)")
        everyone = [i for i, r in enumerate(roles) if r.is_everyone]
        if everyone and everyone[0] != len(roles) - 1:
            raise ConfigError(f"{where}: @everyone must be the last role")

    uncategorized = None
    if "uncategorized" in data:
        uncategorized = [
            _channel(c, None, f"{where}: uncategorized[{i}]")
            for i, c in enumerate(_list(data["uncategorized"], f"{where}: uncategorized"))
        ]
        for ch in uncategorized:
            if ch.sync:
                raise ConfigError(f"{where}: #{ch.name} has no category, so it can't `sync`")

    categories = None
    if "categories" in data:
        categories = []
        for i, c in enumerate(_list(data["categories"], f"{where}: categories")):
            loc = f"{where}: categories[{i}]"
            if not isinstance(c, dict):
                raise ConfigError(f"{loc}: expected a mapping")
            _check_keys(c, CATEGORY_KEYS, loc)
            name = _name(c, loc)
            cat = CategorySpec(name=name, id=_id(c, loc))
            if "overwrites" in c:
                cat.overwrites = _overwrites(c["overwrites"], f"{loc} ({name})")
            for j, ch in enumerate(_list(c.get("channels", []), f"{loc}.channels")):
                spec = _channel(ch, name, f"{loc}.channels[{j}]")
                if spec.sync and cat.overwrites is None:
                    raise ConfigError(f"{loc}: #{spec.name} syncs to {name}, which has no `overwrites:`")
                cat.channels.append(spec)
            categories.append(cat)

    delete = data.get("delete") or {}
    if not isinstance(delete, dict):
        raise ConfigError(f"{where}: delete: expected a mapping")
    _check_keys(delete, DELETE_KEYS, f"{where}: delete")
    return Config(
        roles=roles,
        uncategorized=uncategorized,
        categories=categories,
        delete_roles=[str(x) for x in _list(delete.get("roles") or [], f"{where}: delete.roles")],
        delete_categories=[str(x) for x in _list(delete.get("categories") or [], f"{where}: delete.categories")],
        delete_channels=[str(x) for x in _list(delete.get("channels") or [], f"{where}: delete.channels")],
    )


def _role(r, loc: str) -> RoleSpec:
    if not isinstance(r, dict):
        raise ConfigError(f"{loc}: expected a mapping")
    _check_keys(r, ROLE_KEYS, loc)
    name = _name(r, loc)
    spec = RoleSpec(name=name, id=_id(r, loc), managed=bool(r.get("managed", False)))
    loc = f"{loc} ({name})"
    if "color" in r:
        spec.color = _color(r["color"], loc)
    if "hoist" in r:
        spec.hoist = _bool(r["hoist"], f"{loc}: hoist")
    if "mentionable" in r:
        spec.mentionable = _bool(r["mentionable"], f"{loc}: mentionable")
    if "permissions" in r:
        spec.permissions = _perms(r["permissions"], f"{loc}: permissions")
    if spec.is_everyone:
        extra = [k for k in ("color", "hoist", "mentionable") if k in r]
        if extra:
            raise ConfigError(f"{loc}: only `permissions` can be set on @everyone (remove {', '.join(extra)})")
    return spec


def _channel(c, category: str | None, loc: str) -> ChannelSpec:
    if not isinstance(c, dict):
        raise ConfigError(f"{loc}: expected a mapping")
    _check_keys(c, CHANNEL_KEYS, loc)
    name = _name(c, loc)
    loc = f"{loc} (#{name})"
    spec = ChannelSpec(name=name, category=category, id=_id(c, loc))
    if "type" in c:
        if c["type"] not in TYPE_IDS:
            raise ConfigError(f"{loc}: type must be one of {', '.join(TYPE_IDS)}")
        spec.type = c["type"]
    if "topic" in c:
        if c["topic"] is not None and not isinstance(c["topic"], str):
            raise ConfigError(f"{loc}: topic must be text")
        spec.topic = c["topic"] or None
    spec.sync = _bool(c.get("sync", False), f"{loc}: sync")
    if "overwrites" in c:
        if spec.sync:
            raise ConfigError(f"{loc}: use either `sync: true` or `overwrites`, not both")
        spec.overwrites = _overwrites(c["overwrites"], loc)
    return spec


def _overwrites(value, loc: str) -> list[OverwriteSpec]:
    if value is None:
        value = {}
    if not isinstance(value, dict):
        raise ConfigError(f"{loc}: overwrites must be a mapping of role name -> allow/deny")
    out = []
    for key, body in value.items():
        key = str(key)
        if body is None:
            body = {}
        if not isinstance(body, dict):
            raise ConfigError(f"{loc}: overwrite {key!r} must have allow/deny lists")
        _check_keys(body, {"allow", "deny"}, f"{loc}: overwrite {key!r}")
        allow = _perms(body.get("allow"), f"{loc}: {key} allow")
        deny = _perms(body.get("deny"), f"{loc}: {key} deny")
        both = allow & deny
        if both:
            raise ConfigError(f"{loc}: {key} both allows and denies {', '.join(P.to_names(both))}")
        out.append(OverwriteSpec(key, allow, deny))
    return out


def _perms(value, loc: str) -> int:
    if value is None:
        return 0
    if not isinstance(value, list):
        raise ConfigError(f"{loc}: expected a list of permission names")
    try:
        return P.to_bits(value)
    except P.UnknownPermission as exc:
        raise ConfigError(f"{loc}: {exc}") from None


def _color(value, loc: str):
    if value is None:
        return None
    m = re.fullmatch(r"#?([0-9a-fA-F]{6})", str(value).strip())
    if not m:
        raise ConfigError(f"{loc}: color must look like \"#1f8b4c\" or be null")
    return int(m.group(1), 16)


def _bool(value, loc: str) -> bool:
    if not isinstance(value, bool):
        raise ConfigError(f"{loc}: expected true or false")
    return value


def _name(d: dict, loc: str) -> str:
    name = d.get("name")
    if not isinstance(name, str) or not name.strip():
        raise ConfigError(f"{loc}: missing `name`")
    return name


def _id(d: dict, loc: str) -> str | None:
    if "id" not in d:
        return None
    value = str(d["id"]).strip()
    if not value.isdigit():
        raise ConfigError(f"{loc}: id must be a numeric Discord id")
    return value


def _list(value, loc: str) -> list:
    if value is None:
        return []
    if not isinstance(value, list):
        raise ConfigError(f"{loc}: expected a list")
    return value


def _check_keys(d: dict, allowed: set[str], loc: str) -> None:
    unknown = set(d) - allowed
    if unknown:
        raise ConfigError(f"{loc}: unknown key(s) {', '.join(sorted(map(str, unknown)))}")
