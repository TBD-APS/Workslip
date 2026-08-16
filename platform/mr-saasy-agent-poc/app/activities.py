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
    SandboxInput,
    SandboxResult,
    ToolInput,
    ToolResult,
)
from .sandbox import SandboxRunner, SandboxRunnerError, create_sandbox_runner


_BROKEN_SOURCE = """def add(left: int, right: int) -> int:
    return left - right
"""

_FIXED_SOURCE = """def add(left: int, right: int) -> int:
    return left + right
"""

_SANDBOX_RUNNER: SandboxRunner | None = None


def _state_path() -> Path:
    return Path(os.getenv("POC_STATE_FILE", "/state/tool-effects.json"))


def _sandbox_runner() -> SandboxRunner:
    global _SANDBOX_RUNNER
    if _SANDBOX_RUNNER is None:
        _SANDBOX_RUNNER = create_sandbox_runner()
    return _SANDBOX_RUNNER


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
    provider_activity_attempt = activity.info().attempt
    if request.inject_transient_failure and provider_activity_attempt == 1:
        raise ApplicationError("simulated provider timeout", type="ProviderTimeout")

    if request.feedback is None:
        return DecisionResult(
            patch_label="BROKEN",
            rationale="Initial agent attempt intentionally introduces a real failing implementation.",
            provider_activity_attempt=provider_activity_attempt,
            candidate_source=_BROKEN_SOURCE,
        )

    return DecisionResult(
        patch_label="FIXED",
        rationale=(
            "Agent consumed structured sandbox/test feedback and selected the corrected action: "
            + request.feedback.explanation
        ),
        provider_activity_attempt=provider_activity_attempt,
        candidate_source=_FIXED_SOURCE,
    )


@activity.defn
async def perform_tool(request: ToolInput) -> ToolResult:
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
async def execute_sandbox(request: SandboxInput) -> SandboxResult:
    try:
        return await _sandbox_runner().run(request)
    except SandboxRunnerError as exc:
        raise ApplicationError(
            str(exc),
            type=exc.error_type,
            non_retryable=not exc.retryable,
        ) from exc
    except RuntimeError as exc:
        raise ApplicationError(
            str(exc),
            type="SandboxRunnerConfigurationError",
            non_retryable=True,
        ) from exc


@activity.defn
async def run_gate(request: GateInput) -> GateResult:
    if request.sandbox_passed:
        return GateResult(passed=True)

    output = request.sandbox_output.strip()
    if len(output) > 1200:
        output = output[-1200:]

    return GateResult(
        passed=False,
        feedback=GateFeedback(
            gate_id="sandbox-unit-test-gate",
            rule_id="candidate-must-pass-isolated-tests",
            category="quality",
            explanation=(
                f"Disposable sandbox tests failed with exit code {request.sandbox_exit_code}. Output: {output}"
            ),
            suggested_next_actions=[
                "Use the isolated test failure as input for the next attempt.",
                "Correct the implementation without weakening or deleting the test.",
                "Re-run the same test in a fresh sandbox container.",
            ],
            retryable=True,
        ),
    )
