#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FE_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${FE_ROOT}/../.." && pwd)"
API_PROJECT="${REPO_ROOT}/src/BE/WorkslipApi/Workslip.Api.csproj"
API_URL="http://127.0.0.1:5262"
APP_URL="http://127.0.0.1:5270"
SQL_CONTAINER="workslip-playwright-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"
BACKEND_LOG="${RUNNER_TEMP:-/tmp}/workslip-playwright-backend.log"
FRONTEND_LOG="${RUNNER_TEMP:-/tmp}/workslip-playwright-frontend.log"
SCENARIO_TIMEOUT_SECONDS="${WORKSLIP_PLAYWRIGHT_SCENARIO_TIMEOUT_SECONDS:-180}"
BACKEND_PID=""
FRONTEND_PID=""

cleanup() {
  if [[ -n "${FRONTEND_PID}" ]]; then
    kill "${FRONTEND_PID}" >/dev/null 2>&1 || true
    wait "${FRONTEND_PID}" >/dev/null 2>&1 || true
  fi
  if [[ -n "${BACKEND_PID}" ]]; then
    kill "${BACKEND_PID}" >/dev/null 2>&1 || true
    wait "${BACKEND_PID}" >/dev/null 2>&1 || true
  fi
  docker rm --force "${SQL_CONTAINER}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

for command in docker dotnet node npm curl openssl timeout; do
  if ! command -v "${command}" >/dev/null 2>&1; then
    echo "ERROR: Required command '${command}' is unavailable." >&2
    exit 70
  fi
done

if ! [[ "${SCENARIO_TIMEOUT_SECONDS}" =~ ^[1-9][0-9]*$ ]]; then
  echo "ERROR: WORKSLIP_PLAYWRIGHT_SCENARIO_TIMEOUT_SECONDS must be a positive integer." >&2
  exit 70
fi

run_scenario() {
  local label="$1"
  local script="$2"
  echo "[playwright] running ${label} (hard timeout ${SCENARIO_TIMEOUT_SECONDS}s)"
  set +e
  timeout --foreground --signal=TERM --kill-after=10s "${SCENARIO_TIMEOUT_SECONDS}s" node "${script}"
  local status=$?
  set -e
  if [[ "${status}" -eq 124 || "${status}" -eq 137 ]]; then
    echo "ERROR: Playwright scenario '${label}' exceeded ${SCENARIO_TIMEOUT_SECONDS}s and was terminated." >&2
    return 124
  fi
  if [[ "${status}" -ne 0 ]]; then
    echo "ERROR: Playwright scenario '${label}' failed with exit code ${status}." >&2
    return "${status}"
  fi
}

echo "[playwright] validating suite stability policy before expensive runtime setup"
cd "${FE_ROOT}"
node --test scripts/playwright-stability-policy.test.mjs
node scripts/playwright-stability-policy.mjs

sql_password="Workslip$(openssl rand -hex 16)!A1"
jwt_key="$(openssl rand -hex 32)"

if [[ "${GITHUB_ACTIONS:-false}" == "true" ]]; then
  echo "::add-mask::${sql_password}"
  echo "::add-mask::${jwt_key}"
fi

echo "Starting disposable SQL Server for authenticated Playwright."
docker run --detach \
  --name "${SQL_CONTAINER}" \
  --publish 1433:1433 \
  --env ACCEPT_EULA=Y \
  --env MSSQL_PID=Developer \
  --env "MSSQL_SA_PASSWORD=${sql_password}" \
  mcr.microsoft.com/mssql/server:2022-latest >/dev/null

sql_ready=false
for attempt in $(seq 1 60); do
  if docker exec "${SQL_CONTAINER}" bash -lc \
    "if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '${sql_password}' -C -Q 'SELECT 1'; else /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P '${sql_password}' -Q 'SELECT 1'; fi" \
    >/dev/null 2>&1; then
    sql_ready=true
    break
  fi
  sleep 2
done

if [[ "${sql_ready}" != "true" ]]; then
  docker logs "${SQL_CONTAINER}" >&2 || true
  echo "ERROR: Ephemeral SQL Server did not become ready." >&2
  exit 71
fi

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="${API_URL}"
export Azure__AppConfiguration__Endpoint=''
export Azure__Sql__ConnectionString="Server=localhost,1433;Initial Catalog=WorkslipPlaywright;User Id=sa;Password=${sql_password};Encrypt=false;TrustServerCertificate=true;MultipleActiveResultSets=true"
export Jwt__Issuer=workslip-playwright-local
export Jwt__Audience=workslip-playwright-local
export Jwt__SigningKey="${jwt_key}"
export Workslip__ApplyLocalMigrations=true
export Workslip__SeedDevelopmentData=true
export Workslip__SeedDevelopmentEntraIdentities=false

echo "Starting Development API against disposable database."
dotnet restore "${API_PROJECT}" --nologo
nohup dotnet run \
  --project "${API_PROJECT}" \
  --no-launch-profile \
  --no-restore \
  >"${BACKEND_LOG}" 2>&1 &
BACKEND_PID=$!

api_ready=false
for attempt in $(seq 1 120); do
  if curl --fail --silent "${API_URL}/health" >/dev/null; then
    api_ready=true
    break
  fi
  if ! kill -0 "${BACKEND_PID}" >/dev/null 2>&1; then
    cat "${BACKEND_LOG}" >&2 || true
    echo "ERROR: Workslip API exited before becoming healthy." >&2
    exit 72
  fi
  sleep 1
done

if [[ "${api_ready}" != "true" ]]; then
  cat "${BACKEND_LOG}" >&2 || true
  echo "ERROR: Workslip API did not become healthy." >&2
  exit 73
fi

echo "Installing frontend dependencies and Chromium runtime."
npm ci --prefer-offline --no-audit --no-fund
npm install \
  --prefix scripts \
  --no-save \
  --package-lock=false \
  --ignore-scripts \
  --no-audit \
  --no-fund \
  playwright@1.55.0
node scripts/node_modules/playwright/cli.js install --with-deps chromium

echo "Starting Vite on loopback with the repository's /api proxy."
nohup ./node_modules/.bin/vite \
  --host 127.0.0.1 \
  --port 5270 \
  --strictPort \
  >"${FRONTEND_LOG}" 2>&1 &
FRONTEND_PID=$!

app_ready=false
for attempt in $(seq 1 90); do
  if curl --fail --silent "${APP_URL}/login" >/dev/null; then
    app_ready=true
    break
  fi
  if ! kill -0 "${FRONTEND_PID}" >/dev/null 2>&1; then
    cat "${FRONTEND_LOG}" >&2 || true
    echo "ERROR: Vite exited before becoming ready." >&2
    exit 74
  fi
  sleep 1
done

if [[ "${app_ready}" != "true" ]]; then
  cat "${FRONTEND_LOG}" >&2 || true
  echo "ERROR: Vite did not become ready." >&2
  exit 75
fi

export WORKSLIP_PLAYWRIGHT_APP_URL="${APP_URL}"
export WORKSLIP_PLAYWRIGHT_API_URL="${API_URL}"
export WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL='admin@17v3ygzs.mailosaur.net'
export WORKSLIP_PLAYWRIGHT_USER_EMAIL='user@17v3ygzs.mailosaur.net'
export WORKSLIP_PLAYWRIGHT_AUDITOR_EMAIL='auditor@17v3ygzs.mailosaur.net'

export WORKSLIP_ALLOW_LOCAL_DEV_TOKEN=true
export WORKSLIP_LOCAL_APP_URL="${APP_URL}"
export WORKSLIP_LOCAL_API_URL="${API_URL}"
export WORKSLIP_SYNTHETIC_ADMIN_EMAIL="${WORKSLIP_PLAYWRIGHT_ADMIN_EMAIL}"
export WORKSLIP_SYNTHETIC_USER_EMAIL="${WORKSLIP_PLAYWRIGHT_USER_EMAIL}"

run_scenario 'authenticated smoke' scripts/playwright-ephemeral-smoke.mjs
run_scenario 'auth brand and login transition evidence' scripts/playwright-auth-brand.mjs
run_scenario 'PDF performance evidence' scripts/playwright-pdf-performance.mjs
run_scenario 'job image gallery evidence' scripts/playwright-job-images.mjs
run_scenario 'rare critical auth/role flows' scripts/playwright-critical-rare-flows.mjs
run_scenario 'critical job lifecycle flows' scripts/playwright-critical-job-lifecycle.mjs
run_scenario 'customer lifecycle evidence' scripts/playwright-customer-lifecycle.mjs
run_scenario 'worksheet integrity evidence' scripts/playwright-worksheet-integrity.mjs
run_scenario 'notification and people lifecycle evidence' scripts/playwright-notification-people-lifecycle.mjs
run_scenario 'duplicate assignment lifecycle evidence' scripts/playwright-duplicate-assignment-lifecycle.mjs
run_scenario 'shared state semantics evidence' scripts/playwright-shared-state-semantics.mjs
run_scenario 'overview status navigation evidence' scripts/playwright-overview-status-navigation.mjs
run_scenario 'WOR-542 Admin Overview + Timer isolation evidence' scripts/playwright-power-bi-admin-overview.mjs

echo "Authenticated ephemeral Playwright suite completed successfully."
