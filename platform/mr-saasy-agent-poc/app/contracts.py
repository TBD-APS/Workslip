from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional


@dataclass
class ChangeRunInput:
    run_id: str
    goal: str
    max_attempts: int = 3


@dataclass
class GateFeedback:
    gate_id: str
    rule_id: str
    category: str
    explanation: str
    suggested_next_actions: list[str]
    retryable: bool


@dataclass
class DecisionInput:
    run_id: str
    attempt: int
    goal: str
    feedback: Optional[GateFeedback]
    inject_transient_failure: bool = False


@dataclass
class DecisionResult:
    patch_label: str
    rationale: str
    provider_activity_attempt: int
    candidate_source: str


@dataclass
class ToolInput:
    run_id: str
    attempt: int
    patch_label: str
    artificial_delay_seconds: float


@dataclass
class ToolResult:
    idempotency_key: str
    applied_this_invocation: bool
    applied_count: int
    invocation_count: int


@dataclass
class SandboxInput:
    run_id: str
    attempt: int
    source_code: str
    test_code: str


@dataclass
class SandboxEvidence:
    sandbox_id: str
    sandbox_name: str
    image: str
    exit_code: int
    output: str
    source_sha256: str
    test_sha256: str
    network_disabled: bool
    read_only_root: bool
    capabilities_dropped: bool
    no_new_privileges: bool
    memory_limit_bytes: int
    pids_limit: int
    tmpfs_workspace: bool
    bind_mount_count: int
    destroyed: bool


@dataclass
class SandboxResult:
    passed: bool
    evidence: SandboxEvidence


@dataclass
class GateInput:
    run_id: str
    attempt: int
    patch_label: str
    sandbox_passed: bool
    sandbox_exit_code: int
    sandbox_output: str


@dataclass
class GateResult:
    passed: bool
    feedback: Optional[GateFeedback] = None


@dataclass
class ApprovalSignal:
    actor: str
    approved: bool
    reason: str


@dataclass
class AttemptRecord:
    attempt: int
    patch_label: str
    provider_activity_attempt: int
    tool_idempotency_key: str
    tool_applied_count: int
    tool_invocation_count: int
    gate_passed: bool
    outcome: str
    feedback: Optional[GateFeedback] = None
    sandbox: Optional[SandboxEvidence] = None


@dataclass
class RunSnapshot:
    run_id: str = ""
    goal: str = ""
    state: str = "CREATED"
    current_attempt: int = 0
    last_feedback: Optional[GateFeedback] = None
    approval: Optional[ApprovalSignal] = None
    attempts: list[AttemptRecord] = field(default_factory=list)
