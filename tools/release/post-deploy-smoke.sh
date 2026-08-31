#!/usr/bin/env bash
set -Eeuo pipefail

base_url="${1:-}"
if [[ -z "${base_url}" || ! "${base_url}" =~ ^https:// ]]; then
  echo 'usage: post-deploy-smoke.sh https://host' >&2
  exit 64
fi

base_url="${base_url%/}"

wait_for_status() {
  local url="$1"
  local expected="$2"
  local attempts="${3:-15}"
  for attempt in $(seq 1 "${attempts}"); do
    status="$(curl --silent --show-error --connect-timeout 10 --max-time 10 --output /dev/null --write-out '%{http_code}' "${url}" || true)"
    if [[ "${status}" == "${expected}" ]]; then
      return 0
    fi
    [[ "${attempt}" -lt "${attempts}" ]] && sleep 10
  done
  echo "Expected ${url} to return ${expected}; last status was ${status:-none}." >&2
  return 1
}

wait_for_status "${base_url}/" 200 15
wait_for_status "${base_url}/health" 200 15
wait_for_status "${base_url}/api/auth/me" 401 3

echo "[post-deploy] smoke passed for ${base_url}"
