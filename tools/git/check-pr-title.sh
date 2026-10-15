#!/usr/bin/env bash
# Public-repository equivalent of the MR SAAS'y PR title convention.
# Workslip cannot call workflows hosted in the private mr-saassy repository.
set -euo pipefail

TITLE="${1:-}"
BODY_FILE="${2:-}"
TITLE_RE='^\[(Fix|Feature|Improvement|Chore|Docs|Infra|Test|Security)\] .+$'

if [[ ! "${TITLE}" =~ ${TITLE_RE} ]]; then
  echo "::error::PR title must use [Fix], [Feature], [Improvement], [Chore], [Docs], [Infra], [Test] or [Security], followed by a subject." >&2
  echo "Received: ${TITLE:-<empty>}" >&2
  exit 1
fi

if [[ -n "${BODY_FILE}" ]]; then
  if [[ ! -f "${BODY_FILE}" ]]; then
    echo "::error::PR body file not found: ${BODY_FILE}" >&2
    exit 1
  fi
  if ! grep -qE '[^[:space:]]' "${BODY_FILE}"; then
    echo "::warning::PR body is empty. Explain why the change is necessary." >&2
  fi
fi

if (( ${#TITLE} < 20 )); then
  echo "::warning::PR title is short; describe the actual change." >&2
fi

echo "PR title convention passed: ${TITLE}"
