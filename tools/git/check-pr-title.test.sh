#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHECK="${ROOT}/check-pr-title.sh"
BODY="$(mktemp)"
trap 'rm -f "$BODY"' EXIT
printf 'This change fixes a broken integration.\n' > "$BODY"

for kind in Bug Fix Feature Improvement Docs Refactor Infra Security Release Hotfix Chore Test; do
  bash "$CHECK" "[${kind}] Workslip integration fix" "$BODY" >/dev/null
done
for invalid in 'fix: integration' '[Bugfix] Integration' '[bug] Integration' '[Fix]' 'WOR-815: Integration' '' '[Feature] '; do
  if bash "$CHECK" "$invalid" "$BODY" >/dev/null 2>&1; then
    echo "Invalid PR title unexpectedly passed: '$invalid'" >&2
    exit 1
  fi
done
: > "$BODY"
bash "$CHECK" '[Fix] Workslip integration fix' "$BODY" >/dev/null
if bash "$CHECK" '[Fix] Workslip integration fix' "${BODY}.missing" >/dev/null 2>&1; then
  echo 'Missing body file unexpectedly passed' >&2
  exit 1
fi
echo 'PR title convention self-tests passed.'
