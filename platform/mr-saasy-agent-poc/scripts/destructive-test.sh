#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE=(docker compose -f "$ROOT/docker-compose.yml")
RUN_ID="mr-saasy-poc-$(date +%s)-${RANDOM}"

show_diagnostics() {
  echo "[poc] FAILURE diagnostics"
  "${COMPOSE[@]}" ps -a || true
  "${COMPOSE[@]}" logs --no-color temporal worker || true
}

cleanup() {
  "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
}

trap show_diagnostics ERR
trap cleanup EXIT

run_cli() {
  "${COMPOSE[@]}" run --rm worker python -m app.cli "$@"
}

echo "[poc] Build worker image"
"${COMPOSE[@]}" build worker

echo "[poc] Start local Temporal dev service"
"${COMPOSE[@]}" up -d temporal
run_cli ping

echo "[poc] Start worker container"
"${COMPOSE[@]}" up -d worker

echo "[poc] Start logical change run: $RUN_ID"
run_cli start --run-id "$RUN_ID" --goal "Prove crash-safe agent correction loop"

echo "[poc] Wait until attempt 1 has committed its external side effect"
run_cli wait-effect --run-id "$RUN_ID" --attempt 1 --timeout 30

echo "[poc] KILL worker before the activity can acknowledge success"
"${COMPOSE[@]}" kill -s KILL worker
"${COMPOSE[@]}" rm -f worker

echo "[poc] Create a fresh worker container against the same Temporal history"
"${COMPOSE[@]}" up -d worker

# The lost activity heartbeat forces Temporal to retry. The idempotency key must
# turn that retry into a no-op instead of a duplicate side effect.
run_cli wait-state --run-id "$RUN_ID" --state PAUSED_AFTER_TOOL --timeout 30
run_cli assert-effect \
  --run-id "$RUN_ID" \
  --attempt 1 \
  --applied-count 1 \
  --min-invocations 2

echo "[poc] Let attempt 1 hit the deterministic gate and feed failure back"
run_cli continue --run-id "$RUN_ID"

# Attempt 2 consumes the structured feedback, applies one new idempotent action,
# passes the gate and then suspends for human approval.
run_cli wait-state --run-id "$RUN_ID" --state WAITING_APPROVAL --timeout 30
run_cli assert-effect --run-id "$RUN_ID" --attempt 1 --applied-count 1 --min-invocations 2
run_cli assert-effect --run-id "$RUN_ID" --attempt 2 --applied-count 1 --min-invocations 1

echo "[poc] Overview before human approval"
run_cli status --run-id "$RUN_ID"

echo "[poc] Resume the same run with a human approval signal"
run_cli approve \
  --run-id "$RUN_ID" \
  --actor "poc-harness" \
  --reason "Crash recovery, idempotency and corrected retry were observed"
run_cli wait-state --run-id "$RUN_ID" --state COMPLETED --timeout 30

echo "[poc] Final run overview"
run_cli result --run-id "$RUN_ID"

echo "[poc] PASS: durable agent loop survived destructive container restart"
