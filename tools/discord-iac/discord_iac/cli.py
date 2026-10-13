"""Command line entry point."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .api import ConfigError, DiscordClient, DiscordError, load_settings
from .apply import ApplyAborted, confirm, execute, verify
from .audit import find_problems, render_problems, render_report
from .config import load_config
from .export import export_yaml
from .plan import Plan, build_plan, render_plan, resolve_config, resolve_snapshot
from .state import GuildState, fetch_snapshot, read_snapshot, write_snapshot

ROOT = Path(__file__).resolve().parent.parent


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except AttributeError:
            pass

    parser = argparse.ArgumentParser(prog="discord-iac", description="Audit and manage a Discord server from YAML.")
    parser.add_argument("--env", type=Path, default=ROOT / ".env", help="env file with DISCORD_BOT_TOKEN and DISCORD_GUILD_ID")
    parser.add_argument("-v", "--verbose", action="store_true", help="show rate-limit waits")
    sub = parser.add_subparsers(dest="command", required=True)

    p_audit = sub.add_parser("audit", help="report on the live server, snapshot it, export current.yaml")
    p_audit.add_argument("--from-snapshot", type=Path, help="audit a saved snapshot instead of the live server")
    p_audit.add_argument("--export", type=Path, default=ROOT / "current.yaml", help="where to write the YAML export")

    p_plan = sub.add_parser("plan", help="diff desired.yaml against the live server (no changes)")
    p_plan.add_argument("config", nargs="?", type=Path, default=ROOT / "desired.yaml")
    p_plan.add_argument("--from-snapshot", type=Path, help="plan against a saved snapshot instead of the live server")
    p_apply = sub.add_parser("apply", help="plan, snapshot, confirm, then make the changes")
    p_apply.add_argument("config", nargs="?", type=Path, default=ROOT / "desired.yaml")
    p_restore = sub.add_parser("restore", help="restore role permissions and overwrites from a snapshot")
    p_restore.add_argument("snapshot", type=Path)

    args = parser.parse_args(argv)
    try:
        if args.command == "audit":
            return cmd_audit(args)
        if args.command == "plan":
            return cmd_plan(args)
        if args.command == "apply":
            return cmd_apply(args)
        return cmd_restore(args)
    except (ConfigError, OSError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    except DiscordError as exc:
        print(f"Discord API error: {exc}", file=sys.stderr)
        return 1
    except ApplyAborted as exc:
        print(f"stopped: {exc}", file=sys.stderr)
        return 1


def cmd_audit(args) -> int:
    snapshot_path: Path | None = None
    if args.from_snapshot:
        snapshot = read_snapshot(args.from_snapshot)
    else:
        token, guild_id = load_settings(str(args.env))
        with DiscordClient(token, verbose=args.verbose) as client:
            snapshot = fetch_snapshot(client, guild_id)
        snapshot_path = write_snapshot(snapshot, ROOT / "snapshots")

    state = GuildState(snapshot)
    report = render_report(state) + "\n\n" + render_problems(find_problems(state)) + "\n"
    print(report)

    (ROOT / "audit-report.txt").write_text(report, encoding="utf-8")
    args.export.write_text(export_yaml(state), encoding="utf-8")
    if snapshot_path:
        print(f"Snapshot: {snapshot_path}")
    print(f"Report:   {ROOT / 'audit-report.txt'}")
    print(f"Export:   {args.export}")
    return 0


def cmd_plan(args) -> int:
    config = load_config(args.config)
    if args.from_snapshot:
        state = GuildState(read_snapshot(args.from_snapshot))
    else:
        token, guild_id = load_settings(str(args.env))
        with DiscordClient(token, verbose=args.verbose) as client:
            state = GuildState(fetch_snapshot(client, guild_id))
    plan = build_plan(state, resolve_config(state, config))
    print(render_plan(plan))
    return 0 if plan.ok else 3


def cmd_apply(args) -> int:
    config = load_config(args.config)

    def plan_fn(state: GuildState) -> Plan:
        return build_plan(state, resolve_config(state, config))

    return _run(args, plan_fn, f"discord-iac apply {args.config.name}")


def cmd_restore(args) -> int:
    snapshot = read_snapshot(args.snapshot)

    def plan_fn(state: GuildState) -> Plan:
        return build_plan(state, resolve_snapshot(state, snapshot))

    print(f"Restoring role permissions and channel overwrites from {args.snapshot.name} "
          f"(taken {snapshot['taken_at']}). Names, order and deleted things are not restored.\n")
    return _run(args, plan_fn, f"discord-iac restore {args.snapshot.stem}")


def _run(args, plan_fn, reason_prefix: str) -> int:
    """Shared by apply and restore: plan, snapshot, confirm, execute, verify."""
    token, guild_id = load_settings(str(args.env))
    with DiscordClient(token, verbose=args.verbose) as client:
        snapshot = fetch_snapshot(client, guild_id)
        plan = plan_fn(GuildState(snapshot))
        print(render_plan(plan))
        if not plan.ok:
            print("\nNothing was changed. Fix the problems above and try again.")
            return 3
        if not plan.changes:
            return 0
        path = write_snapshot(snapshot, ROOT / "snapshots")
        print(f"\nSnapshot of the server before any change: {path}")
        print(f"To undo role permission and overwrite changes later: python -m discord_iac restore {path.relative_to(ROOT)}")
        if not confirm(f"\nType yes to make these {len(plan.changes)} change(s): "):
            print("Cancelled. Nothing was changed.")
            return 1
        print()
        try:
            execute(client, guild_id, plan, plan_fn, reason_prefix)
        except (DiscordError, ApplyAborted):
            print(f"\nStopped at the first error. Changes before it were made; the snapshot above has the "
                  f"state from before.", file=sys.stderr)
            raise
        after = verify(client, guild_id, plan_fn)
        if after.changes:
            print(f"\nDone, but {len(after.changes)} difference(s) remain. Run `plan` to see them.")
            return 1
        print("\nDone. The server now matches.")
        return 0
