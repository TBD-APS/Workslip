from __future__ import annotations

import asyncio
import fcntl
import json
import os
from pathlib import Path

from temporalio import activity
from temporalio.exceptions import ApplicationError

from .contracts import (
    DecisionInput,
    DecisionResult,
    GateFeedback,
    GateInput,
    GateResult,
    ToolInput,
    ToolResult,
)


def _state_path() -> Path:
    return Path(os.getenv("POC_STATE_FILE", "/state/tool-effects.json"))


def _read_state(path: Path) -> dict[str, dict[str, object]]:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def _write_state(path: Path, state: dict[str, dict[str, object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(state, indent=2, sort_keys=True), encoding="utf-8")
    os.replace(temporary, path)


@activity.defn
async def decide_attempt(request: DecisionInput) -> DecisionResult:
    # POC provider failure: first invocation of the first logical attempt fails.
    # Temporal retries only this activity, not the whole workflow.
    provider_activity_attempt = activity.info().attempt
    if request.inject_transient_failure and provider_activity_attempt == 1:
        raise ApplicationError(
            "simulated provider timeout",
            type="ProviderTimeout",
        )

    if request.feedback is None:
        return DecisionResult(
            patch_label="BROKEN",
            rationale="Initial agent attempt intentionally violates the deterministic gate.",
            provider_activity_attempt=provider_activity_attempt,
        )

    return DecisionResult(
        patch_label="FIXED",
        rationale=(
            "Agent consumed structured gate feedback and selected the corrected action: "
            + request.feedback.explanation
        ),
        provider_activity_attempt=provider_activity_attempt,
    )


@activity.defn
async def perform_tool(request: ToolInput) -> ToolResult:
    """Apply one idempotent side effect and then keep the activity alive briefly.

    The destructive POC kills the worker after the state file is committed but before
    this activity returns. Temporal therefore retries the activity, and this function
    must observe the existing idempotency key instead of duplicating the effect.
    """

    path = _state_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    lock_path = path.with_suffix(".lock")
    idempotency_key = f"{request.run_id}:attempt:{request.attempt}:apply-patch"

    with lock_path.open("a+", encoding="utf-8") as lock_file:
        fcntl.flock(lock_file.fileno(), fcntl.LOCK_EX)
        state = _read_state(path)
        record = state.setdefault(
            idempotency_key,
            {
                "run_id": request.run_id,
                "attempt": request.attempt,
                "patch_label": request.patch_label,
                "applied_count": 0,
                "invocation_count": 0,
            },
        )
        record["invocation_count"] = int(record["invocation_count"]) + 1
        applied_this_invocation = int(record["applied_count"]) == 0
        if applied_this_invocation:
            record["applied_count"] = 1
        _write_state(path, state)
        fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)

    # On the first application we deliberately leave a failure window after the
    # external side effect is durable but before Temporal receives activity success.
    # A retry skips the long delay so recovery is quick.
    if applied_this_invocation and request.artificial_delay_seconds > 0:
        remaining = request.artificial_delay_seconds
        while remaining > 0:
            activity.heartbeat({"idempotency_key": idempotency_key})
            interval = min(0.25, remaining)
            await asyncio.sleep(interval)
            remaining -= interval

    with lock_path.open("a+", encoding="utf-8") as lock_file:
        fcntl.flock(lock_file.fileno(), fcntl.LOCK_SH)
        final_record = _read_state(path)[idempotency_key]
        fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)

    return ToolResult(
        idempotency_key=idempotency_key,
        applied_this_invocation=applied_this_invocation,
        applied_count=int(final_record["applied_count"]),
        invocation_count=int(final_record["invocation_count"]),
    )


@activity.defn
async def run_gate(request: GateInput) -> GateResult:
    if request.patch_label == "FIXED":
        return GateResult(passed=True)

    return GateResult(
        passed=False,
        feedback=GateFeedback(
            gate_id="poc-quality-gate",
            rule_id="requires-corrected-change",
            category="quality",
            explanation="The proposed change still carries the BROKEN marker.",
            suggested_next_actions=[
                "Consume this feedback on the next attempt.",
                "Produce the FIXED marker before re-running the gate.",
            ],
            retryable=True,
        ),
    )
