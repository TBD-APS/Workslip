#!/usr/bin/env bash
# Read-only branch hygiene audit. No branch deletion, ref updates or API writes.
set -euo pipefail
: "${GH_TOKEN:?GH_TOKEN is required}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"

mkdir -p artifacts/github
REPORT='artifacts/github/branch-hygiene.tsv'
printf 'branch\treason\n' > "$REPORT"

open_json=$(gh pr list --repo "$GITHUB_REPOSITORY" --state open --limit 500 --json headRefName --jq '.[].headRefName')
merged_json=$(gh pr list --repo "$GITHUB_REPOSITORY" --state merged --limit 500 --json headRefName,isCrossRepository --jq '.[] | select(.isCrossRepository == false) | .headRefName' | sort -u)
mapfile -t open_heads <<< "$open_json"
mapfile -t merged_heads <<< "$merged_json"

is_protected() {
  case "$1" in
    main|production|release-*|release/*|safety/*|backup/*|rbj--*) return 0 ;;
  esac
  return 1
}
is_open_head() {
  local head
  for head in "${open_heads[@]}"; do
    [[ "$head" == "$1" ]] && return 0
  done
  return 1
}

candidates=0
for branch in "${merged_heads[@]}"; do
  [[ -n "$branch" ]] || continue
  if is_protected "$branch" || is_open_head "$branch"; then continue; fi
  if gh api --silent "repos/${GITHUB_REPOSITORY}/git/ref/heads/${branch}" >/dev/null 2>&1; then
    printf '%s\tmerged PR head; no open PR uses branch\n' "$branch" >> "$REPORT"
    candidates=$((candidates + 1))
  fi
done

# Conservative content audit: report old branches only when every changed file
# matches main. False negatives are preferable to unsafe cleanup suggestions.
min_age_days=2
now=$(date +%s)
while IFS= read -r ref; do
  branch="${ref#origin/}"
  [[ "$branch" != "$ref" && "$branch" != HEAD ]] || continue
  if is_protected "$branch" || is_open_head "$branch"; then continue; fi
  if grep -Fq -- "$(printf '%s\t' "$branch")" "$REPORT"; then continue; fi
  timestamp=$(git log -1 --format=%ct "$ref" 2>/dev/null || true)
  [[ "$timestamp" =~ ^[0-9]+$ ]] || continue
  age_days=$(( (now - timestamp) / 86400 ))
  (( age_days >= min_age_days )) || continue
  base=$(git merge-base origin/main "$ref" 2>/dev/null || true)
  [[ -n "$base" ]] || continue
  mapfile -d '' -t changed_files < <(git diff --name-only -z "$base" "$ref")
  (( ${#changed_files[@]} > 0 )) || continue
  if git diff --quiet origin/main "$ref" -- "${changed_files[@]}"; then
    printf '%s\tchanged files match main; %s days old\n' "$branch" "$age_days" >> "$REPORT"
    candidates=$((candidates + 1))
  fi
done < <(git for-each-ref --format='%(refname:short)' refs/remotes/origin)

{
  echo '## Repository branch hygiene (read-only)'
  echo
  echo "Candidate branches: **${candidates}**"
  echo
  echo 'This report does not delete or modify branches. Protected and open PR branches are excluded.'
  if (( candidates > 0 )); then
    echo
    echo '~~~text'
    cat "$REPORT"
    echo '~~~'
  fi
} >> "${GITHUB_STEP_SUMMARY:-/dev/stdout}"
