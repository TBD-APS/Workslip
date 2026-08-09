#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-${WORKSLIP_INTEGRATION_BASE_URL:-}}"
if [ -z "$BASE_URL" ]; then
  echo "ERROR: Missing integration test base URL. Pass it as argv[1] or set WORKSLIP_INTEGRATION_BASE_URL." >&2
  exit 64
fi

AUTH_TOKEN="${WORKSLIP_AUTH_TOKEN:-}"
if [ -z "$AUTH_TOKEN" ]; then
  echo "ERROR: Missing WORKSLIP_AUTH_TOKEN. The full Postman suite requires a pre-issued bearer token for the isolated integration environment." >&2
  exit 66
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
RUN_COLLECTION="$(mktemp)"
trap 'rm -f "$RUN_COLLECTION"' EXIT

# /api/dev/* was intentionally removed from the application. Keep that legacy
# folder out of the executable success-path suite instead of silently accepting
# stale development-only expectations. Every other current/future top-level
# folder is executed automatically.
node - "$COLLECTION" "$RUN_COLLECTION" <<'NODE'
const fs = require('node:fs');

const [sourcePath, outputPath] = process.argv.slice(2);
const collection = JSON.parse(fs.readFileSync(sourcePath, 'utf8'));
const items = Array.isArray(collection.item) ? collection.item : [];
const devFolders = items.filter((item) => item?.name === 'Dev');

if (devFolders.length > 1) {
  throw new Error('Postman collection contains more than one top-level Dev folder.');
}

if (devFolders.length === 1) {
  const requests = Array.isArray(devFolders[0].item) ? devFolders[0].item : [];
  const unexpected = requests
    .map((item) => item?.request?.url?.raw ?? '')
    .filter((url) => !url.startsWith('{{baseUrl}}/api/dev/'));

  if (unexpected.length > 0) {
    throw new Error(`Dev folder contains non-/api/dev requests: ${unexpected.join(', ')}`);
  }
}

collection.item = items.filter((item) => item?.name !== 'Dev');
fs.writeFileSync(outputPath, JSON.stringify(collection));
NODE

args=(
  run "$RUN_COLLECTION"
  --environment "$ENVIRONMENT"
  --env-var "baseUrl=$BASE_URL"
  --env-var "authToken=$AUTH_TOKEN"
  --reporters cli
  --timeout-request 30000
  --bail
)

npx --yes newman "${args[@]}"
