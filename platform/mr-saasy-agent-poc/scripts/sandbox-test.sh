#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE=(docker compose -f "$ROOT/docker-compose.yml")
RUN_ID="mr-saasy-sandbox-$(date +%s)-${RANDOM}"

show_diagnostics() {
  echo "[sandbox-poc] FAILURE diagnostics"
  "${COMPOSE[@]}" ps -a || true
  "${COMPOSE[@]}" logs --no-color temporal sandbox-broker worker || true
  docker ps -a --filter "label=mr-saasy.sandbox-run=${RUN_ID}" || true
}

cleanup() {
  docker ps -aq --filter "label=mr-saasy.sandbox-run=${RUN_ID}" \
    | xargs -r docker rm -f >/dev/null 2>&1 || true
  "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
}

trap show_diagnostics ERR
trap cleanup EXIT

run_cli() {
  "${COMPOSE[@]}" run --rm worker python -m app.cli "$@"
}

assert_boundary() {
  local worker_id
  local broker_id
  worker_id="$("${COMPOSE[@]}" ps -q worker)"
  broker_id="$("${COMPOSE[@]}" ps -q sandbox-broker)"

  if docker inspect "$worker_id" --format '{{json .Mounts}}' | grep -q '/var/run/docker.sock'; then
    echo "ERROR: worker must not own the Docker socket" >&2
    exit 91
  fi
  if ! docker inspect "$broker_id" --format '{{json .Mounts}}' | grep -q '/var/run/docker.sock'; then
    echo "ERROR: expected Docker socket only on sandbox broker" >&2
    exit 92
  fi
}

assert_no_sandbox_leak() {
  local leaked
  leaked="$(docker ps -a \
    --filter "label=mr-saasy.sandbox-run=${RUN_ID}" \
    --format '{{.ID}}')"
  if [[ -n "$leaked" ]]; then
    echo "ERROR: sandbox containers survived the disposable execution boundary: $leaked" >&2
    exit 93
  fi
}

echo "[sandbox-poc] Build services"
"${COMPOSE[@]}" build worker sandbox-broker

echo "[sandbox-poc] Start Temporal, broker and worker"
"${COMPOSE[@]}" up -d temporal sandbox-broker
run_cli ping
"${COMPOSE[@]}" up -d worker
assert_boundary

echo "[sandbox-poc] Start logical run $RUN_ID"
run_cli start \
  --run-id "$RUN_ID" \
  --goal "Fix a tiny implementation using isolated test feedback"

run_cli wait-state --run-id "$RUN_ID" --state PAUSED_AFTER_TOOL --timeout 30
run_cli continue --run-id "$RUN_ID"
run_cli wait-state --run-id "$RUN_ID" --state WAITING_APPROVAL --timeout 45

echo "[sandbox-poc] Verify real failing test -> corrected fresh sandbox"
run_cli assert-sandbox --run-id "$RUN_ID" --attempt 1 --expected fail
run_cli assert-sandbox --run-id "$RUN_ID" --attempt 2 --expected pass
run_cli assert-sandbox-separation --run-id "$RUN_ID"
assert_no_sandbox_leak

echo "[sandbox-poc] Preserve evidence outside destroyed sandboxes"
run_cli status --run-id "$RUN_ID"

run_cli approve \
  --run-id "$RUN_ID" \
  --actor "sandbox-poc-harness" \
  --reason "Two isolated disposable sandbox attempts and durable evidence verified"
run_cli wait-state --run-id "$RUN_ID" --state COMPLETED --timeout 30
run_cli result --run-id "$RUN_ID"

echo "[sandbox-poc] PASS: isolated code sandbox feedback survived container destruction"
