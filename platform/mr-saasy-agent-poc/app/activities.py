from __future__ import annotations

import asyncio
import fcntl
import json
import os
from pathlib import Path
from urllib import error as urllib_error
from urllib import request as urllib_request

from temporalio import activity
from temporalio.exceptions import ApplicationError

from .contracts import (
    DecisionInput,
    DecisionResult,
    GateFeedback,
    GateInput,
    GateResult,
    SandboxEvidence,
    SandboxInput,
    SandboxResult,
    ToolInput,
    ToolResult,
)


_BROKEN_SOURCE = """def add(left: int, right: int) -> int:
    return left - right
"""

_FIXED_SOURCE = """def add(left: int, right: int) -> int:
    return left + right
"""


def _state_path() -> Path:
    return Path(os.getenv("POC_STATE_FILE", "/state/tool-effects.json"))


def _sandbox_broker_url() -> str:
    return os.getenv("SANDBOX_BROKER_URL", "http://sandbox-broker:8080").rstrip("/")


def _read_state(path: Path) -> dict[str, dict[str, object]]:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def _write_state(path: Path, state: dict[str, dict[str, object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(state, indent=2, sort_keys=True), encoding="utf-8")
    os.replace(temporary, path)


def _post_json(url: str, payload: dict[str, object]) -> dict[str, object]:
    body = json.dumps(payload).encode("utf-8")
    request = urllib_request.Request(
        url,
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib_request.urlopen(request, timeout=25) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib_error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise ApplicationError(
            f"sandbox broker rejected request: HTTP {exc.code}: {detail}",
            type="SandboxBrokerRejected",
            non_retryable=400 <= exc.code < 500,
        ) from exc
    except Exception as exc:
        raise ApplicationError(
            f"sandbox broker unavailable: {exc}",
            type="SandboxBrokerUnavailable",
        ) from exc


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
    payload = {
        "run_id": request.run_id,
        "attempt": request.attempt,
        "source_code": request.source_code,
        "test_code": request.test_code,
    }
    raw = await asyncio.to_thread(_post_json, f"{_sandbox_broker_url()}/v1/run", payload)

    evidence_raw = raw.get("evidence")
    if not isinstance(evidence_raw, dict):
        raise ApplicationError(
            "sandbox broker returned no structured evidence",
            type="SandboxEvidenceMissing",
            non_retryable=True,
        )

    evidence = SandboxEvidence(
        sandbox_id=str(evidence_raw["sandbox_id"]),
        sandbox_name=str(evidence_raw["sandbox_name"]),
        image=str(evidence_raw["image"]),
        exit_code=int(evidence_raw["exit_code"]),
        output=str(evidence_raw.get("output", "")),
        source_sha256=str(evidence_raw["source_sha256"]),
        test_sha256=str(evidence_raw["test_sha256"]),
        network_disabled=bool(evidence_raw["network_disabled"]),
        read_only_root=bool(evidence_raw["read_only_root"]),
        capabilities_dropped=bool(evidence_raw["capabilities_dropped"]),
        no_new_privileges=bool(evidence_raw["no_new_privileges"]),
        memory_limit_bytes=int(evidence_raw["memory_limit_bytes"]),
        pids_limit=int(evidence_raw["pids_limit"]),
        tmpfs_workspace=bool(evidence_raw["tmpfs_workspace"]),
        bind_mount_count=int(evidence_raw["bind_mount_count"]),
        destroyed=bool(evidence_raw["destroyed"]),
    )
    return SandboxResult(passed=bool(raw.get("passed", False)), evidence=evidence)


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
