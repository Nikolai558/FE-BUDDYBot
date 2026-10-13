"""Run a confirmed plan against the server, one phase at a time.

Each phase is re-planned from a fresh read of the server, so things created in
an earlier phase (new roles, categories) have real ids by the time a later
phase refers to them. Every change a phase wants to make must be one the user
already confirmed; anything else stops the run.
"""

from __future__ import annotations

import json
from collections import Counter
from collections.abc import Callable

from .api import DiscordClient, DiscordError
from .plan import NEW, PHASES, Plan, build_plan, render_plan
from .state import GuildState, fetch_snapshot

PlanFn = Callable[[GuildState], Plan]


class ApplyAborted(RuntimeError):
    pass


def execute(client: DiscordClient, guild_id: str, confirmed: Plan, plan_fn: PlanFn, reason_prefix: str) -> None:
    remaining = Counter(c.text for c in confirmed.changes)
    total = len(confirmed.changes)
    done = 0
    for phase, title in PHASES.items():
        if not any(c.phase == phase for c in confirmed.changes):
            continue
        state = GuildState(fetch_snapshot(client, guild_id))
        fresh = plan_fn(state)
        if not fresh.ok:
            raise ApplyAborted("the plan changed and now has errors or blockers:\n" + render_plan(fresh))
        print(f"{title}:")
        for change in (c for c in fresh.changes if c.phase == phase):
            if remaining[change.text] <= 0:
                raise ApplyAborted(f"the server changed since you confirmed; this wasn't in the plan: {change.text}")
            if NEW in json.dumps(change.body):
                raise ApplyAborted(f"internal error: {change.text} still refers to something not created yet")
            done += 1
            print(f"  [{done}/{total}] {change.text} ... ", end="", flush=True)
            try:
                client.request(change.method, change.path, json=change.body,
                               reason=f"{reason_prefix}: {change.text}")
            except DiscordError:
                print("FAILED")
                raise
            print("done")
            remaining[change.text] -= 1

    left = sum(remaining.values())
    if left:
        print(f"{left} confirmed change(s) were no longer needed when their turn came (someone may have made them already).")


def verify(client: DiscordClient, guild_id: str, plan_fn: PlanFn) -> Plan:
    return plan_fn(GuildState(fetch_snapshot(client, guild_id)))


def confirm(prompt: str) -> bool:
    try:
        answer = input(prompt)
    except EOFError:
        return False
    return answer.strip() == "yes"


__all__ = ["ApplyAborted", "build_plan", "confirm", "execute", "verify"]
