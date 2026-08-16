from __future__ import annotations

import argparse
import asyncio
from dataclasses import asdict
import json
import os
from pathlib import Path
import time

from .connection import connect_temporal
from .contracts import ApprovalSignal, ChangeRunInput, RunSnapshot
from .workflow import ChangeRunWorkflow


def _print(value: object) -> None:
    if hasattr(value, "__dataclass_fields__"):
        value = asdict(value)
    print(json.dumps(value, indent=2, sort_keys=True))


def _state_path() -> Path:
    return Path(os.getenv("POC_STATE_FILE", "/state/tool-effects.json"))


def _read_effects() -> dict[str, dict[str, object]]:
    path = _state_path()
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


async def _snapshot(run_id: str) -> RunSnapshot:
    client = await connect_temporal()
    handle = client.get_workflow_handle(run_id)
    return await handle.query(ChangeRunWorkflow.snapshot)


async def command_ping(_: argparse.Namespace) -> None:
    await connect_temporal()
    _print({"temporal": "ready"})


async def command_start(args: argparse.Namespace) -> None:
    client = await connect_temporal()
    task_queue = os.getenv("TEMPORAL_TASK_QUEUE", "mr-saasy-agent-poc")
    await client.start_workflow(
        ChangeRunWorkflow.run,
        ChangeRunInput(run_id=args.run_id, goal=args.goal),
        id=args.run_id,
        task_queue=task_queue,
    )
    _print({"run_id": args.run_id, "state": "STARTED"})


async def command_status(args: argparse.Namespace) -> None:
    _print(await _snapshot(args.run_id))


async def command_wait_state(args: argparse.Namespace) -> None:
    deadline = time.monotonic() + args.timeout
    latest = None
    while time.monotonic() < deadline:
        latest = await _snapshot(args.run_id)
        if latest.state == args.state:
            _print(latest)
            return
        await asyncio.sleep(0.25)
    current = latest.state if latest is not None else "UNKNOWN"
    raise SystemExit(f"Timed out waiting for {args.state}; current state is {current}")


async def command_continue(args: argparse.Namespace) -> None:
    client = await connect_temporal()
    handle = client.get_workflow_handle(args.run_id)
    await handle.signal(ChangeRunWorkflow.continue_after_tool)
    _print({"run_id": args.run_id, "signal": "continue_after_tool"})


async def command_approve(args: argparse.Namespace) -> None:
    client = await connect_temporal()
    handle = client.get_workflow_handle(args.run_id)
    await handle.signal(
        ChangeRunWorkflow.approve,
        ApprovalSignal(actor=args.actor, approved=True, reason=args.reason),
    )
    _print({"run_id": args.run_id, "signal": "approve", "actor": args.actor})


async def command_result(args: argparse.Namespace) -> None:
    client = await connect_temporal()
    handle = client.get_workflow_handle(args.run_id)
    _print(await handle.result())


async def command_wait_effect(args: argparse.Namespace) -> None:
    key = f"{args.run_id}:attempt:{args.attempt}:apply-patch"
    deadline = time.monotonic() + args.timeout
    while time.monotonic() < deadline:
        effects = _read_effects()
        if key in effects and int(effects[key].get("applied_count", 0)) >= 1:
            _print(effects[key])
            return
        await asyncio.sleep(0.1)
    raise SystemExit(f"Timed out waiting for side effect {key}")


async def command_assert_effect(args: argparse.Namespace) -> None:
    key = f"{args.run_id}:attempt:{args.attempt}:apply-patch"
    record = _read_effects().get(key)
    if record is None:
        raise SystemExit(f"Missing side effect {key}")

    actual_applied = int(record.get("applied_count", 0))
    actual_invocations = int(record.get("invocation_count", 0))
    if actual_applied != args.applied_count:
        raise SystemExit(
            f"Expected applied_count={args.applied_count} for {key}; got {actual_applied}"
        )
    if actual_invocations < args.min_invocations:
        raise SystemExit(
            f"Expected at least {args.min_invocations} invocations for {key}; got {actual_invocations}"
        )
    _print(record)


def _attempt(snapshot: RunSnapshot, attempt: int):
    for record in snapshot.attempts:
        if record.attempt == attempt:
            return record
    raise SystemExit(f"Run {snapshot.run_id} has no recorded attempt {attempt}")


async def command_assert_sandbox(args: argparse.Namespace) -> None:
    snapshot = await _snapshot(args.run_id)
    record = _attempt(snapshot, args.attempt)
    sandbox = record.sandbox
    if sandbox is None:
        raise SystemExit(f"Attempt {args.attempt} has no sandbox evidence")

    expected_pass = args.expected == "pass"
    actual_pass = sandbox.exit_code == 0 and record.gate_passed
    if actual_pass != expected_pass:
        raise SystemExit(
            f"Attempt {args.attempt} expected sandbox {args.expected}; exit_code={sandbox.exit_code}, gate_passed={record.gate_passed}"
        )

    checks = {
        "network_disabled": sandbox.network_disabled,
        "read_only_root": sandbox.read_only_root,
        "capabilities_dropped": sandbox.capabilities_dropped,
        "no_new_privileges": sandbox.no_new_privileges,
        "tmpfs_workspace": sandbox.tmpfs_workspace,
        "destroyed": sandbox.destroyed,
        "memory_limit": sandbox.memory_limit_bytes > 0,
        "pids_limit": sandbox.pids_limit > 0,
        "no_bind_mounts": sandbox.bind_mount_count == 0,
    }
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        raise SystemExit(
            f"Sandbox isolation assertions failed for attempt {args.attempt}: " + ", ".join(failed)
        )

    if expected_pass and "OK" not in sandbox.output:
        raise SystemExit("Passing sandbox output did not contain unittest OK evidence")
    if not expected_pass and "FAILED" not in sandbox.output:
        raise SystemExit("Failing sandbox output did not contain unittest FAILED evidence")

    _print(record)


async def command_assert_sandbox_separation(args: argparse.Namespace) -> None:
    snapshot = await _snapshot(args.run_id)
    first = _attempt(snapshot, args.first_attempt)
    second = _attempt(snapshot, args.second_attempt)
    if first.sandbox is None or second.sandbox is None:
        raise SystemExit("Both attempts must contain sandbox evidence")
    if first.sandbox.sandbox_id == second.sandbox.sandbox_id:
        raise SystemExit("Sandbox container IDs were reused across logical attempts")
    if first.sandbox.source_sha256 == second.sandbox.source_sha256:
        raise SystemExit("Corrective attempt did not produce different source content")
    if first.sandbox.test_sha256 != second.sandbox.test_sha256:
        raise SystemExit("The POC weakened or changed the test between attempts")

    _print(
        {
            "first_sandbox_id": first.sandbox.sandbox_id,
            "second_sandbox_id": second.sandbox.sandbox_id,
            "source_changed": True,
            "test_held_constant": True,
        }
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="MR SAAS'y durable agent POC harness")
    sub = parser.add_subparsers(dest="command", required=True)

    ping = sub.add_parser("ping")
    ping.set_defaults(handler=command_ping)

    start = sub.add_parser("start")
    start.add_argument("--run-id", required=True)
    start.add_argument("--goal", default="Implement a tiny safe change")
    start.set_defaults(handler=command_start)

    status = sub.add_parser("status")
    status.add_argument("--run-id", required=True)
    status.set_defaults(handler=command_status)

    wait_state = sub.add_parser("wait-state")
    wait_state.add_argument("--run-id", required=True)
    wait_state.add_argument("--state", required=True)
    wait_state.add_argument("--timeout", type=float, default=30.0)
    wait_state.set_defaults(handler=command_wait_state)

    continue_parser = sub.add_parser("continue")
    continue_parser.add_argument("--run-id", required=True)
    continue_parser.set_defaults(handler=command_continue)

    approve = sub.add_parser("approve")
    approve.add_argument("--run-id", required=True)
    approve.add_argument("--actor", default="poc-human")
    approve.add_argument("--reason", default="POC evidence reviewed")
    approve.set_defaults(handler=command_approve)

    result = sub.add_parser("result")
    result.add_argument("--run-id", required=True)
    result.set_defaults(handler=command_result)

    wait_effect = sub.add_parser("wait-effect")
    wait_effect.add_argument("--run-id", required=True)
    wait_effect.add_argument("--attempt", type=int, required=True)
    wait_effect.add_argument("--timeout", type=float, default=30.0)
    wait_effect.set_defaults(handler=command_wait_effect)

    assert_effect = sub.add_parser("assert-effect")
    assert_effect.add_argument("--run-id", required=True)
    assert_effect.add_argument("--attempt", type=int, required=True)
    assert_effect.add_argument("--applied-count", type=int, default=1)
    assert_effect.add_argument("--min-invocations", type=int, default=1)
    assert_effect.set_defaults(handler=command_assert_effect)

    assert_sandbox = sub.add_parser("assert-sandbox")
    assert_sandbox.add_argument("--run-id", required=True)
    assert_sandbox.add_argument("--attempt", type=int, required=True)
    assert_sandbox.add_argument("--expected", choices=["pass", "fail"], required=True)
    assert_sandbox.set_defaults(handler=command_assert_sandbox)

    assert_separation = sub.add_parser("assert-sandbox-separation")
    assert_separation.add_argument("--run-id", required=True)
    assert_separation.add_argument("--first-attempt", type=int, default=1)
    assert_separation.add_argument("--second-attempt", type=int, default=2)
    assert_separation.set_defaults(handler=command_assert_sandbox_separation)

    return parser


async def main() -> None:
    args = build_parser().parse_args()
    await args.handler(args)


if __name__ == "__main__":
    asyncio.run(main())
