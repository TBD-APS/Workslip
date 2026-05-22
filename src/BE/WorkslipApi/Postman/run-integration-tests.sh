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
COLLECTION="$SCRIPT_DIR/postman_collection.json"
ENVIRONMENT="$SCRIPT_DIR/workslip.integration.postman_environment.json"

npx --yes newman run "$COLLECTION"   --environment "$ENVIRONMENT"   --env-var "baseUrl=$BASE_URL"   --reporters cli   --timeout-request 30000   --bail
