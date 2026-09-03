#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

LOCAL_URL="${WORKSLIP_LOCAL_URL:-http://127.0.0.1:5270}"
API_URL="${WORKSLIP_API_URL:-http://127.0.0.1:5262}"
SEQ_URL="${WORKSLIP_SEQ_URL:-http://127.0.0.1:5341}"
COMPOSE=(docker compose)

log() {
  printf '[workslip demo] %s\n' "$*"
}

fail() {
  printf '[workslip demo] ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command '$1' was not found."
}

ensure_docker() {
  require_command docker

  if [[ "$(uname -s)" == "Darwin" ]]; then
    require_command open

    if open -Ra OrbStack >/dev/null 2>&1; then
      # Non-destructive: only open OrbStack and use its Docker context. Never
      # reset the engine or remove containers/images/volumes automatically.
      open -gja OrbStack >/dev/null 2>&1 || true

      local attempt=0
      until docker context inspect orbstack >/dev/null 2>&1; do
        attempt=$((attempt + 1))
        if (( attempt >= 30 )); then
          fail "OrbStack opened, but Docker context 'orbstack' did not become available."
        fi
        sleep 1
      done

      if [[ "$(docker context show 2>/dev/null || true)" != "orbstack" ]]; then
        log "Switching Docker context to orbstack"
        docker context use orbstack >/dev/null
      fi
    else
      log "OrbStack is not installed; using the current Docker context instead."
    fi
  fi

  local attempt=0
  until docker info >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    if (( attempt >= 60 )); then
      fail "Docker engine is not responding."
    fi
    sleep 1
  done
}

validate_compose() {
  log "Validating Compose configuration"
  "${COMPOSE[@]}" config --quiet
}

wait_for_url() {
  local name="$1"
  local url="$2"
  local attempts="${3:-120}"
  local attempt=0

  require_command curl
  until curl --fail --silent --show-error "$url" >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    if (( attempt >= attempts )); then
      printf '\n' >&2
      "${COMPOSE[@]}" ps >&2 || true
      "${COMPOSE[@]}" logs --tail 120 api fe >&2 || true
      fail "$name did not become reachable at $url."
    fi
    sleep 2
  done
}

emit_urls() {
  printf '\n'
  printf 'WORKSLIP_URL=%s\n' "$LOCAL_URL"
  printf 'WORKSLIP_API_URL=%s\n' "$API_URL"
  printf 'WORKSLIP_SEQ_URL=%s\n' "$SEQ_URL"
  printf '\n'
}

start_demo() {
  ensure_docker
  validate_compose

  log "Starting Workslip full local stack"
  "${COMPOSE[@]}" up -d --wait --quiet-pull --progress plain

  log "Waiting for API"
  wait_for_url "Workslip API" "$API_URL" 120

  log "Waiting for frontend"
  wait_for_url "Workslip frontend" "$LOCAL_URL" 120

  log "Workslip is ready: $LOCAL_URL"
  emit_urls
}

stop_demo() {
  ensure_docker
  log "Stopping Workslip (persistent volumes are preserved)"
  "${COMPOSE[@]}" down
}

show_status() {
  ensure_docker
  "${COMPOSE[@]}" ps

  if command -v curl >/dev/null 2>&1 && curl --fail --silent "$LOCAL_URL" >/dev/null 2>&1; then
    log "Frontend: healthy ($LOCAL_URL)"
  else
    log "Frontend: unavailable ($LOCAL_URL)"
  fi

  if command -v curl >/dev/null 2>&1 && curl --fail --silent "$API_URL" >/dev/null 2>&1; then
    log "API: reachable ($API_URL)"
  else
    log "API: unavailable or root route is not HTTP 2xx ($API_URL)"
  fi
}

show_logs() {
  ensure_docker
  "${COMPOSE[@]}" logs --tail 200 -f api fe db seq
}

usage() {
  cat <<'EOF'
Usage: bash scripts/demo.sh <command>

Commands:
  up       Start the Workslip full local stack
  down     Stop the stack without deleting persistent volumes
  status   Show Compose and local endpoint status
  logs     Follow API, frontend, SQL Server and Seq logs

On macOS, OrbStack is opened automatically when installed and Docker is switched
non-destructively to the `orbstack` context. Other platforms use the current
Docker context.
EOF
}

case "${1:-}" in
  up)
    start_demo
    ;;
  down)
    stop_demo
    ;;
  status)
    show_status
    ;;
  logs)
    show_logs
    ;;
  *)
    usage
    exit 2
    ;;
esac
