#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-${WORKSLIP_INTEGRATION_BASE_URL:-}}"
if [ -z "$BASE_URL" ]; then
  echo "ERROR: Missing integration test base URL. Pass it as argv[1] or set WORKSLIP_INTEGRATION_BASE_URL." >&2
  exit 64
fi
case "$BASE_URL" in
  http://localhost:*|https://localhost:*|http://127.0.0.1:*|https://127.0.0.1:*|*test*|*staging*|*stage*) ;;
  *)
    if [ "${ALLOW_PRODUCTION_INTEGRATION_TESTS:-false}" != "true" ]; then
      echo "ERROR: Base URL must look like localhost/test/staging. Refusing possible production target: $BASE_URL" >&2
      exit 65
    fi
    ;;
esac

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_COLLECTION="$SCRIPT_DIR/postman_collection.json"
COLLECTION="$(mktemp "${TMPDIR:-/tmp}/workslip-postman-collection.XXXXXX.json")"
ENVIRONMENT="$SCRIPT_DIR/workslip.integration.postman_environment.json"

cleanup() {
  rm -f "$COLLECTION"
}
trap cleanup EXIT

# Prepare a temporary execution copy. The bootstrap bearer token is seeded into
# collection scope (not Newman environment scope) so the Development-only token
# request can deliberately switch to the tenant identity used by the remaining
# workflow. The preparation also completes the canonical job fixture with the
# minimum worksheet required by the authoritative submit-ready rules.
node "$SCRIPT_DIR/prepare-integration-collection.mjs" "$SOURCE_COLLECTION" "$COLLECTION"

args=(
  run "$COLLECTION"
  --environment "$ENVIRONMENT"
  --env-var "baseUrl=$BASE_URL"
  --reporters cli
  --timeout-request 30000
  --bail
)

npx --yes newman "${args[@]}"
