"""Minimal Discord REST v10 client. No gateway connection is ever opened."""

from __future__ import annotations

import os
import re
import sys
import time
from urllib.parse import quote

import httpx
from dotenv import load_dotenv

API_BASE = "https://discord.com/api/v10"
USER_AGENT = "DiscordBot (https://github.com/Nikolai558/FE-BUDDYBot, 1.0) discord-iac"
MAX_429_RETRIES = 6
MAX_5XX_RETRIES = 2


class ConfigError(RuntimeError):
    pass


class DiscordError(RuntimeError):
    def __init__(self, method: str, path: str, status: int, body: object):
        self.method = method
        self.path = path
        self.status = status
        self.body = body
        super().__init__(f"{method} {path} failed with HTTP {status}: {body}")


def load_settings(env_file: str | None = None) -> tuple[str, str]:
    """Return (token, guild_id) from the environment / .env file."""
    load_dotenv(env_file or ".env")
    token = os.environ.get("DISCORD_BOT_TOKEN", "").strip()
    guild_id = os.environ.get("DISCORD_GUILD_ID", "").strip()
    if not token:
        raise ConfigError("DISCORD_BOT_TOKEN is not set (put it in .env)")
    if not guild_id.isdigit():
        raise ConfigError("DISCORD_GUILD_ID is not set or is not a numeric ID (put it in .env)")
    return token, guild_id


def _route_key(method: str, path: str) -> str:
    # Discord buckets are per route plus the "major" parameter (guild/channel id).
    # Keep the first id after /guilds/ or /channels/, template the rest.
    major = re.match(r"^/(guilds|channels)/(\d+)", path)
    templated = re.sub(r"/\d+", "/:id", path)
    return f"{method} {templated} {major.group(2) if major else ''}"


class DiscordClient:
    def __init__(self, token: str, *, verbose: bool = False):
        # The token only ever lives in this header. It is never printed or logged.
        self._http = httpx.Client(
            base_url=API_BASE,
            headers={"Authorization": f"Bot {token}", "User-Agent": USER_AGENT},
            timeout=30.0,
        )
        self._verbose = verbose
        self._route_bucket: dict[str, str] = {}
        self._bucket_state: dict[str, tuple[int, float]] = {}  # bucket -> (remaining, reset_at monotonic)
        self._global_until = 0.0

    def close(self) -> None:
        self._http.close()

    def __enter__(self) -> "DiscordClient":
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    # -- core ---------------------------------------------------------------

    def request(self, method: str, path: str, *, json: object = None, reason: str | None = None,
                params: dict | None = None) -> object:
        method = method.upper()
        is_write = method != "GET"
        if is_write and not reason:
            raise ValueError(f"{method} {path}: every write needs an audit log reason")
        headers = {}
        if reason:
            # 1-512 URL-encoded UTF-8 characters, per the audit log docs.
            headers["X-Audit-Log-Reason"] = quote(reason[:512], safe=" ")

        key = _route_key(method, path)
        tries_429 = 0
        tries_5xx = 0
        while True:
            self._wait_for_bucket(key)
            try:
                resp = self._http.request(method, path, json=json, headers=headers, params=params)
            except httpx.TransportError as exc:
                if not is_write and tries_5xx < MAX_5XX_RETRIES:
                    tries_5xx += 1
                    time.sleep(1.0 * tries_5xx)
                    continue
                raise DiscordError(method, path, 0, f"network error: {exc.__class__.__name__}") from None

            self._record_headers(key, resp)

            if resp.status_code == 429:
                tries_429 += 1
                body = _safe_json(resp)
                retry_after = _retry_after(resp, body)
                if isinstance(body, dict) and body.get("global"):
                    self._global_until = time.monotonic() + retry_after
                scope = resp.headers.get("X-RateLimit-Scope", "?")
                self._wait_note(retry_after, f"rate limited on {method} {path} (scope {scope})")
                if tries_429 > MAX_429_RETRIES:
                    raise DiscordError(method, path, 429, body)
                time.sleep(retry_after)
                continue

            if resp.status_code >= 500 and not is_write and tries_5xx < MAX_5XX_RETRIES:
                tries_5xx += 1
                time.sleep(1.0 * tries_5xx)
                continue

            if resp.status_code >= 400:
                raise DiscordError(method, path, resp.status_code, _safe_json(resp))

            if resp.status_code == 204 or not resp.content:
                return None
            return resp.json()

    def _wait_for_bucket(self, key: str) -> None:
        now = time.monotonic()
        if self._global_until > now:
            time.sleep(self._global_until - now)
        bucket = self._route_bucket.get(key)
        if not bucket:
            return
        remaining, reset_at = self._bucket_state.get(bucket, (1, 0.0))
        now = time.monotonic()
        if remaining <= 0 and reset_at > now:
            wait = reset_at - now
            self._wait_note(wait, f"rate limit bucket for {key.split(' ')[1]} is used up")
            time.sleep(wait)

    def _record_headers(self, key: str, resp: httpx.Response) -> None:
        bucket = resp.headers.get("X-RateLimit-Bucket")
        if not bucket:
            return
        major = key.rsplit(" ", 1)[-1]
        bucket_key = f"{bucket}:{major}"
        self._route_bucket[key] = bucket_key
        try:
            remaining = int(resp.headers.get("X-RateLimit-Remaining", "1"))
            reset_after = float(resp.headers.get("X-RateLimit-Reset-After", "0"))
        except ValueError:
            return
        self._bucket_state[bucket_key] = (remaining, time.monotonic() + reset_after)

    def _wait_note(self, seconds: float, why: str) -> None:
        # Long waits are always shown, so a pause never looks like a hang.
        if seconds >= 1 or self._verbose:
            print(f"  (waiting {seconds:.0f}s for Discord's rate limit: {why})", file=sys.stderr, flush=True)

    # -- reads --------------------------------------------------------------

    def get_current_user(self) -> dict:
        return self.request("GET", "/users/@me")

    def get_guild(self, guild_id: str) -> dict:
        return self.request("GET", f"/guilds/{guild_id}")

    def get_roles(self, guild_id: str) -> list[dict]:
        return self.request("GET", f"/guilds/{guild_id}/roles")

    def get_role_member_counts(self, guild_id: str) -> dict[str, int]:
        return self.request("GET", f"/guilds/{guild_id}/roles/member-counts")

    def get_channels(self, guild_id: str) -> list[dict]:
        return self.request("GET", f"/guilds/{guild_id}/channels")

    def get_member(self, guild_id: str, user_id: str) -> dict:
        return self.request("GET", f"/guilds/{guild_id}/members/{user_id}")


def _safe_json(resp: httpx.Response) -> object:
    try:
        return resp.json()
    except ValueError:
        return resp.text[:500]


def _retry_after(resp: httpx.Response, body: object) -> float:
    if isinstance(body, dict) and "retry_after" in body:
        try:
            return float(body["retry_after"]) + 0.05
        except (TypeError, ValueError):
            pass
    try:
        return float(resp.headers.get("Retry-After", "1")) + 0.05
    except ValueError:
        return 1.0
