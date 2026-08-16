#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE=(docker compose -f "$ROOT/docker-compose.yml")
RUN_ID="mr-saasy-poc-$(date +%s)-${RANDOM}"

show_diagnostics() {
  echo "[poc] FAILURE diagnostics"
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

assert_container_boundaries() {
  local worker_id
  local broker_id
  worker_id="$("${COMPOSE[@]}" ps -q worker)"
  broker_id="$("${COMPOSE[@]}" ps -q sandbox-broker)"

  if docker inspect "$worker_id" --format '{{json .Mounts}}' | grep -q '/var/run/docker.sock'; then
    echo "ERROR: agent worker unexpectedly has Docker socket access" >&2
    exit 81
  fi

  if ! docker inspect "$broker_id" --format '{{json .Mounts}}' | grep -q '/var/run/docker.sock'; then
    echo "ERROR: sandbox broker is missing its explicit Docker socket boundary" >&2
    exit 82
  fi
}

assert_no_live_sandboxes() {
  local leaked
  leaked="$(docker ps -a \
    --filter "label=mr-saasy.sandbox-run=${RUN_ID}" \
    --format '{{.ID}}')"
  if [[ -n "$leaked" ]]; then
    echo "ERROR: disposable sandbox container leaked after execution: $leaked" >&2
    exit 83
  fi
}

echo "[poc] Build worker and restricted sandbox broker images"
"${COMPOSE[@]}" build worker sandbox-broker

echo "[poc] Start local Temporal dev service + sandbox broker"
"${COMPOSE[@]}" up -d temporal sandbox-broker
run_cli ping

echo "[poc] Start worker container"
"${COMPOSE[@]}" up -d worker
assert_container_boundaries

echo "[poc] Start logical change run: $RUN_ID"
run_cli start --run-id "$RUN_ID" --goal "Prove crash-safe agent correction loop with isolated disposable code execution"

echo "[poc] Wait until attempt 1 has committed its external side effect"
run_cli wait-effect --run-id "$RUN_ID" --attempt 1 --timeout 30

echo "[poc] KILL worker before the activity can acknowledge success"
"${COMPOSE[@]}" kill -s KILL worker
"${COMPOSE[@]}" rm -f worker

echo "[poc] Create a fresh worker container against the same Temporal history"
"${COMPOSE[@]}" up -d worker
assert_container_boundaries

run_cli wait-state --run-id "$RUN_ID" --state PAUSED_AFTER_TOOL --timeout 30
run_cli assert-effect \
  --run-id "$RUN_ID" \
  --attempt 1 \
  --applied-count 1 \
  --min-invocations 2

echo "[poc] Continue into disposable sandbox validation"
run_cli continue --run-id "$RUN_ID"

run_cli wait-state --run-id "$RUN_ID" --state WAITING_APPROVAL --timeout 45
run_cli assert-effect --run-id "$RUN_ID" --attempt 1 --applied-count 1 --min-invocations 2
run_cli assert-effect --run-id "$RUN_ID" --attempt 2 --applied-count 1 --min-invocations 1
run_cli assert-sandbox --run-id "$RUN_ID" --attempt 1 --expected fail
run_cli assert-sandbox --run-id "$RUN_ID" --attempt 2 --expected pass
run_cli assert-sandbox-separation --run-id "$RUN_ID"
assert_no_live_sandboxes

echo "[poc] Overview before human approval"
run_cli status --run-id "$RUN_ID"

echo "[poc] Resume the same run with a human approval signal"
run_cli approve \
  --run-id "$RUN_ID" \
  --actor "poc-harness" \
  --reason "Crash recovery, sandbox isolation, corrective retry and idempotency were observed"
run_cli wait-state --run-id "$RUN_ID" --state COMPLETED --timeout 30

echo "[poc] Final run overview"
run_cli result --run-id "$RUN_ID"

echo "[poc] PASS: durable agent loop survived worker replacement and disposable sandbox execution"
