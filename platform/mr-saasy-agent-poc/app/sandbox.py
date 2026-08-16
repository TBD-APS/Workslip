from __future__ import annotations

import asyncio
import json
import os
from typing import Protocol
from urllib import error as urllib_error
from urllib import request as urllib_request

from .contracts import SandboxEvidence, SandboxInput, SandboxResult


class SandboxRunner(Protocol):
    async def run(self, request: SandboxInput) -> SandboxResult:
        ...


class SandboxRunnerError(RuntimeError):
    def __init__(self, message: str, *, error_type: str, retryable: bool) -> None:
        super().__init__(message)
        self.error_type = error_type
        self.retryable = retryable


class DockerPocSandboxRunner:
    """POC adapter for the privileged Docker sandbox broker.

    The agent/Temporal layer depends only on SandboxRunner. This adapter is the
    single place that knows the broker protocol and can be replaced later by a
    hardened runtime without changing workflow code.
    """

    def __init__(self, base_url: str | None = None) -> None:
        self.base_url = (
            base_url
            or os.getenv("SANDBOX_BROKER_URL", "http://sandbox-broker:8080")
        ).rstrip("/")

    async def run(self, request: SandboxInput) -> SandboxResult:
        payload: dict[str, object] = {
            "run_id": request.run_id,
            "attempt": request.attempt,
            "source_code": request.source_code,
            "test_code": request.test_code,
            "policy": {
                "timeout_seconds": request.policy.timeout_seconds,
                "memory_limit_bytes": request.policy.memory_limit_bytes,
                "pids_limit": request.policy.pids_limit,
                "network_access": request.policy.network_access,
                "cpu_millicores": request.policy.cpu_millicores,
                "workspace_limit_bytes": request.policy.workspace_limit_bytes,
            },
        }
        raw = await asyncio.to_thread(
            self._post_json,
            f"{self.base_url}/v1/run",
            payload,
            request.policy.timeout_seconds + 5,
        )
        return self._map_result(raw)

    @staticmethod
    def _post_json(
        url: str,
        payload: dict[str, object],
        timeout_seconds: int,
    ) -> dict[str, object]:
        body = json.dumps(payload).encode("utf-8")
        request = urllib_request.Request(
            url,
            data=body,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib_request.urlopen(request, timeout=timeout_seconds) as response:
                value = json.loads(response.read().decode("utf-8"))
                if not isinstance(value, dict):
                    raise SandboxRunnerError(
                        "sandbox broker returned a non-object response",
                        error_type="SandboxEvidenceMissing",
                        retryable=False,
                    )
                return value
        except urllib_error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise SandboxRunnerError(
                f"sandbox broker rejected request: HTTP {exc.code}: {detail}",
                error_type="SandboxBrokerRejected",
                retryable=not (400 <= exc.code < 500),
            ) from exc
        except SandboxRunnerError:
            raise
        except Exception as exc:
            raise SandboxRunnerError(
                f"sandbox broker unavailable: {exc}",
                error_type="SandboxBrokerUnavailable",
                retryable=True,
            ) from exc

    @staticmethod
    def _map_result(raw: dict[str, object]) -> SandboxResult:
        evidence_raw = raw.get("evidence")
        if not isinstance(evidence_raw, dict):
            raise SandboxRunnerError(
                "sandbox runtime returned no structured evidence",
                error_type="SandboxEvidenceMissing",
                retryable=False,
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


def create_sandbox_runner() -> SandboxRunner:
    runner = os.getenv("SANDBOX_RUNNER", "docker-poc").strip().lower()
    if runner == "docker-poc":
        return DockerPocSandboxRunner()
    raise RuntimeError(f"unsupported SANDBOX_RUNNER: {runner}")
