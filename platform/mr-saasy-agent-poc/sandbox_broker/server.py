from __future__ import annotations

import base64
from hashlib import sha256
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import re
from typing import Any
from uuid import uuid4

import docker
from docker.errors import NotFound


HOST = os.getenv("SANDBOX_BROKER_HOST", "0.0.0.0")
PORT = int(os.getenv("SANDBOX_BROKER_PORT", "8080"))
SANDBOX_IMAGE = os.getenv("SANDBOX_IMAGE", "python:3.12-slim")
MAX_SOURCE_BYTES = 64 * 1024
MAX_OUTPUT_BYTES = 8 * 1024

_SANDBOX_RUNNER = r"""
import base64
import os
from pathlib import Path
import subprocess
import sys

workspace = Path("/workspace")
workspace.mkdir(parents=True, exist_ok=True)

source = base64.b64decode(os.environ["SOURCE_CODE_B64"]).decode("utf-8")
tests = base64.b64decode(os.environ["TEST_CODE_B64"]).decode("utf-8")

(workspace / "calc.py").write_text(source, encoding="utf-8")
(workspace / "test_calc.py").write_text(tests, encoding="utf-8")

completed = subprocess.run(
    [sys.executable, "-m", "unittest", "-v"],
    cwd=workspace,
    text=True,
    stdout=subprocess.PIPE,
    stderr=subprocess.STDOUT,
    check=False,
)
print(completed.stdout, end="")
raise SystemExit(completed.returncode)
"""


def _slug(value: str) -> str:
    value = re.sub(r"[^a-zA-Z0-9_.-]+", "-", value).strip("-.")
    return (value or "run")[:32]


def _json_bytes(payload: dict[str, Any]) -> bytes:
    return json.dumps(payload, sort_keys=True).encode("utf-8")


def _bounded_int(
    policy: dict[str, object],
    key: str,
    *,
    default: int,
    minimum: int,
    maximum: int,
) -> int:
    value = int(policy.get(key, default))
    if value < minimum or value > maximum:
        raise ValueError(f"policy.{key} must be between {minimum} and {maximum}")
    return value


class SandboxBroker:
    def __init__(self) -> None:
        self.client = docker.from_env()

    def health(self) -> dict[str, object]:
        self.client.ping()
        return {"status": "ok", "docker": "reachable", "sandbox_image": SANDBOX_IMAGE}

    def run(self, payload: dict[str, object]) -> dict[str, object]:
        run_id = str(payload.get("run_id", "")).strip()
        attempt = int(payload.get("attempt", 0))
        source_code = str(payload.get("source_code", ""))
        test_code = str(payload.get("test_code", ""))
        policy_raw = payload.get("policy") or {}
        if not isinstance(policy_raw, dict):
            raise ValueError("policy must be an object")
        policy: dict[str, object] = policy_raw

        if not run_id or attempt < 1:
            raise ValueError("run_id and positive attempt are required")
        if not source_code or not test_code:
            raise ValueError("source_code and test_code are required")

        source_bytes = source_code.encode("utf-8")
        test_bytes = test_code.encode("utf-8")
        if len(source_bytes) > MAX_SOURCE_BYTES or len(test_bytes) > MAX_SOURCE_BYTES:
            raise ValueError("source_code/test_code exceed the POC payload limit")

        timeout_seconds = _bounded_int(
            policy,
            "timeout_seconds",
            default=20,
            minimum=1,
            maximum=60,
        )
        memory_limit_bytes = _bounded_int(
            policy,
            "memory_limit_bytes",
            default=128 * 1024 * 1024,
            minimum=32 * 1024 * 1024,
            maximum=512 * 1024 * 1024,
        )
        pids_limit = _bounded_int(
            policy,
            "pids_limit",
            default=64,
            minimum=16,
            maximum=256,
        )
        cpu_millicores = _bounded_int(
            policy,
            "cpu_millicores",
            default=500,
            minimum=100,
            maximum=2000,
        )
        workspace_limit_bytes = _bounded_int(
            policy,
            "workspace_limit_bytes",
            default=16 * 1024 * 1024,
            minimum=1024 * 1024,
            maximum=64 * 1024 * 1024,
        )
        network_access = str(policy.get("network_access", "none")).strip().lower()
        if network_access != "none":
            raise ValueError("POC Docker adapter only supports policy.network_access=none")

        sandbox_name = f"mr-saasy-sbx-{_slug(run_id)}-{attempt}-{uuid4().hex[:8]}"
        environment = {
            "SOURCE_CODE_B64": base64.b64encode(source_bytes).decode("ascii"),
            "TEST_CODE_B64": base64.b64encode(test_bytes).decode("ascii"),
        }

        container = None
        sandbox_id = ""
        exit_code = -1
        output = ""
        host_config: dict[str, object] = {}
        mounts: list[dict[str, object]] = []
        destroyed = False

        try:
            container = self.client.containers.run(
                SANDBOX_IMAGE,
                command=["python", "-c", _SANDBOX_RUNNER],
                name=sandbox_name,
                detach=True,
                auto_remove=False,
                environment=environment,
                labels={
                    "mr-saasy.sandbox": "true",
                    "mr-saasy.sandbox-run": run_id,
                    "mr-saasy.sandbox-attempt": str(attempt),
                },
                network_mode="none",
                read_only=True,
                user="65534:65534",
                cap_drop=["ALL"],
                security_opt=["no-new-privileges:true"],
                mem_limit=memory_limit_bytes,
                nano_cpus=cpu_millicores * 1_000_000,
                pids_limit=pids_limit,
                tmpfs={
                    "/workspace": (
                        "rw,nosuid,nodev,"
                        f"size={workspace_limit_bytes},uid=65534,gid=65534,mode=0700"
                    ),
                    "/tmp": (
                        "rw,nosuid,nodev,"
                        f"size={workspace_limit_bytes},uid=65534,gid=65534,mode=0700"
                    ),
                },
            )
            sandbox_id = container.id
            wait_result = container.wait(timeout=timeout_seconds)
            exit_code = int(wait_result.get("StatusCode", -1))
            output = container.logs(stdout=True, stderr=True).decode("utf-8", errors="replace")
            if len(output.encode("utf-8")) > MAX_OUTPUT_BYTES:
                output = output[-MAX_OUTPUT_BYTES:]

            container.reload()
            host_config = dict(container.attrs.get("HostConfig") or {})
            mounts = list(container.attrs.get("Mounts") or [])
        finally:
            if container is not None:
                sandbox_id = sandbox_id or container.id
                try:
                    container.remove(force=True)
                finally:
                    try:
                        self.client.containers.get(sandbox_id)
                    except NotFound:
                        destroyed = True

        cap_drop = [str(value).upper() for value in (host_config.get("CapDrop") or [])]
        security_opt = [str(value).lower() for value in (host_config.get("SecurityOpt") or [])]
        network_mode = str(host_config.get("NetworkMode") or "")
        tmpfs = host_config.get("Tmpfs") or {}
        bind_mount_count = sum(1 for mount in mounts if str(mount.get("Type")) == "bind")

        evidence = {
            "sandbox_id": sandbox_id,
            "sandbox_name": sandbox_name,
            "image": SANDBOX_IMAGE,
            "exit_code": exit_code,
            "output": output,
            "source_sha256": sha256(source_bytes).hexdigest(),
            "test_sha256": sha256(test_bytes).hexdigest(),
            "network_disabled": network_mode == "none",
            "read_only_root": bool(host_config.get("ReadonlyRootfs")),
            "capabilities_dropped": "ALL" in cap_drop,
            "no_new_privileges": any(value.startswith("no-new-privileges") for value in security_opt),
            "memory_limit_bytes": int(host_config.get("Memory") or 0),
            "pids_limit": int(host_config.get("PidsLimit") or 0),
            "tmpfs_workspace": "/workspace" in tmpfs,
            "bind_mount_count": bind_mount_count,
            "destroyed": destroyed,
        }
        return {"passed": exit_code == 0, "evidence": evidence}


BROKER = SandboxBroker()


class Handler(BaseHTTPRequestHandler):
    server_version = "MRSAASySandboxBrokerPOC/0.2"

    def log_message(self, format: str, *args: object) -> None:
        print(json.dumps({"client": self.client_address[0], "message": format % args}), flush=True)

    def _write(self, status: int, payload: dict[str, object]) -> None:
        body = _json_bytes(payload)
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        if self.path != "/health":
            self._write(HTTPStatus.NOT_FOUND, {"error": "not_found"})
            return
        try:
            self._write(HTTPStatus.OK, BROKER.health())
        except Exception as exc:
            self._write(HTTPStatus.SERVICE_UNAVAILABLE, {"status": "error", "error": type(exc).__name__})

    def do_POST(self) -> None:
        if self.path != "/v1/run":
            self._write(HTTPStatus.NOT_FOUND, {"error": "not_found"})
            return
        try:
            content_length = int(self.headers.get("Content-Length", "0"))
            if content_length <= 0 or content_length > 256 * 1024:
                raise ValueError("invalid request size")
            payload = json.loads(self.rfile.read(content_length).decode("utf-8"))
            if not isinstance(payload, dict):
                raise ValueError("JSON object required")
            self._write(HTTPStatus.OK, BROKER.run(payload))
        except ValueError as exc:
            self._write(HTTPStatus.BAD_REQUEST, {"error": str(exc)})
        except Exception as exc:
            self._write(
                HTTPStatus.INTERNAL_SERVER_ERROR,
                {"error": "sandbox_execution_failed", "type": type(exc).__name__, "detail": str(exc)[:500]},
            )


def main() -> None:
    BROKER.health()
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print(json.dumps({"event": "sandbox_broker_ready", "host": HOST, "port": PORT, "image": SANDBOX_IMAGE}), flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
