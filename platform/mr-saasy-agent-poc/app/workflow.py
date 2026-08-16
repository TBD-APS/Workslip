from __future__ import annotations

from datetime import timedelta

from temporalio import workflow
from temporalio.common import RetryPolicy

with workflow.unsafe.imports_passed_through():
    from .activities import decide_attempt, execute_sandbox, perform_tool, run_gate
    from .contracts import (
        ApprovalSignal,
        AttemptRecord,
        ChangeRunInput,
        DecisionInput,
        GateFeedback,
        GateInput,
        RunSnapshot,
        SandboxInput,
        ToolInput,
    )


_ACTIVITY_RETRY = RetryPolicy(
    initial_interval=timedelta(seconds=1),
    backoff_coefficient=1.0,
    maximum_interval=timedelta(seconds=1),
    maximum_attempts=3,
)

_SANDBOX_TEST = """import unittest

from calc import add


class AddTests(unittest.TestCase):
    def test_adds_positive_numbers(self) -> None:
        self.assertEqual(add(2, 3), 5)

    def test_adds_negative_number(self) -> None:
        self.assertEqual(add(7, -2), 5)


if __name__ == "__main__":
    unittest.main()
"""


@workflow.defn
class ChangeRunWorkflow:
    def __init__(self) -> None:
        self._snapshot = RunSnapshot()
        self._continue_after_tool = False
        self._approval: ApprovalSignal | None = None

    @workflow.run
    async def run(self, request: ChangeRunInput) -> RunSnapshot:
        self._snapshot.run_id = request.run_id
        self._snapshot.goal = request.goal
        feedback: GateFeedback | None = None

        for attempt in range(1, request.max_attempts + 1):
            self._snapshot.current_attempt = attempt
            self._snapshot.state = "DECIDING"

            decision = await workflow.execute_activity(
                decide_attempt,
                DecisionInput(
                    run_id=request.run_id,
                    attempt=attempt,
                    goal=request.goal,
                    feedback=feedback,
                    inject_transient_failure=attempt == 1,
                ),
                start_to_close_timeout=timedelta(seconds=10),
                retry_policy=_ACTIVITY_RETRY,
            )

            self._snapshot.state = "EXECUTING_TOOL"
            tool_result = await workflow.execute_activity(
                perform_tool,
                ToolInput(
                    run_id=request.run_id,
                    attempt=attempt,
                    patch_label=decision.patch_label,
                    artificial_delay_seconds=8.0 if attempt == 1 else 0.0,
                ),
                start_to_close_timeout=timedelta(seconds=15),
                heartbeat_timeout=timedelta(seconds=2),
                retry_policy=_ACTIVITY_RETRY,
            )

            if attempt == 1:
                self._snapshot.state = "PAUSED_AFTER_TOOL"
                await workflow.wait_condition(lambda: self._continue_after_tool)

            self._snapshot.state = "EXECUTING_SANDBOX"
            sandbox_result = await workflow.execute_activity(
                execute_sandbox,
                SandboxInput(
                    run_id=request.run_id,
                    attempt=attempt,
                    source_code=decision.candidate_source,
                    test_code=_SANDBOX_TEST,
                ),
                start_to_close_timeout=timedelta(seconds=30),
                retry_policy=_ACTIVITY_RETRY,
            )

            self._snapshot.state = "RUNNING_GATE"
            gate_result = await workflow.execute_activity(
                run_gate,
                GateInput(
                    run_id=request.run_id,
                    attempt=attempt,
                    patch_label=decision.patch_label,
                    sandbox_passed=sandbox_result.passed,
                    sandbox_exit_code=sandbox_result.evidence.exit_code,
                    sandbox_output=sandbox_result.evidence.output,
                ),
                start_to_close_timeout=timedelta(seconds=5),
                retry_policy=_ACTIVITY_RETRY,
            )

            if not gate_result.passed:
                feedback = gate_result.feedback
                self._snapshot.last_feedback = feedback
                self._snapshot.attempts.append(
                    AttemptRecord(
                        attempt=attempt,
                        patch_label=decision.patch_label,
                        provider_activity_attempt=decision.provider_activity_attempt,
                        tool_idempotency_key=tool_result.idempotency_key,
                        tool_applied_count=tool_result.applied_count,
                        tool_invocation_count=tool_result.invocation_count,
                        gate_passed=False,
                        outcome="BLOCKED",
                        feedback=feedback,
                        sandbox=sandbox_result.evidence,
                    )
                )
                self._snapshot.state = "RETRYING"
                continue

            self._snapshot.attempts.append(
                AttemptRecord(
                    attempt=attempt,
                    patch_label=decision.patch_label,
                    provider_activity_attempt=decision.provider_activity_attempt,
                    tool_idempotency_key=tool_result.idempotency_key,
                    tool_applied_count=tool_result.applied_count,
                    tool_invocation_count=tool_result.invocation_count,
                    gate_passed=True,
                    outcome="WAITING_APPROVAL",
                    sandbox=sandbox_result.evidence,
                )
            )
            self._snapshot.state = "WAITING_APPROVAL"
            await workflow.wait_condition(lambda: self._approval is not None)
            self._snapshot.approval = self._approval

            if self._approval is not None and self._approval.approved:
                self._snapshot.attempts[-1].outcome = "APPROVED"
                self._snapshot.state = "COMPLETED"
                return self._snapshot

            self._snapshot.attempts[-1].outcome = "REJECTED_BY_HUMAN"
            self._snapshot.state = "BLOCKED"
            return self._snapshot

        self._snapshot.state = "BLOCKED"
        return self._snapshot

    @workflow.signal
    def continue_after_tool(self) -> None:
        self._continue_after_tool = True

    @workflow.signal
    def approve(self, approval: ApprovalSignal) -> None:
        self._approval = approval

    @workflow.query
    def snapshot(self) -> RunSnapshot:
        return self._snapshot
