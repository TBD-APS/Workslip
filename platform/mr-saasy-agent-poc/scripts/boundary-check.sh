#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "[sandbox-boundary] FAIL: $*" >&2
  exit 1
}

[[ -f app/sandbox.py ]] || fail "app/sandbox.py is missing"
grep -q 'class SandboxRunner(Protocol)' app/sandbox.py \
  || fail "neutral SandboxRunner protocol is missing"
grep -q 'class DockerPocSandboxRunner' app/sandbox.py \
  || fail "Docker POC adapter is missing"
grep -q 'class SandboxExecutionPolicy' app/contracts.py \
  || fail "neutral sandbox execution policy is missing"
grep -q 'policy=_SANDBOX_POLICY' app/workflow.py \
  || fail "workflow is not passing an explicit neutral sandbox policy"

for forbidden in SANDBOX_BROKER_URL urllib_request urllib_error sandbox-broker docker; do
  if grep -q "$forbidden" app/activities.py; then
    fail "Temporal activity leaked runtime-specific detail: $forbidden"
  fi
done

grep -q 'create_sandbox_runner' app/activities.py \
  || fail "Temporal activity is not resolving the sandbox through the runner boundary"
grep -q 'network_access' app/sandbox.py \
  || fail "adapter is not mapping the neutral network policy"
grep -q 'policy.network_access=none' sandbox_broker/server.py \
  || fail "POC adapter is not fail-closed for unsupported network policy"

if grep -q '/var/run/docker.sock' app/*.py; then
  fail "agent runtime code must never reference the Docker socket"
fi

echo "[sandbox-boundary] PASS: workflow/activity are runtime-agnostic; Docker remains behind the POC adapter"
