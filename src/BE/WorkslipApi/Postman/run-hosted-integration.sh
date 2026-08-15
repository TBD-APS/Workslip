#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../../.." && pwd)"
API_PROJECT="${REPO_ROOT}/src/BE/WorkslipApi/Workslip.Api.csproj"
API_URL="http://127.0.0.1:5262"
SQL_CONTAINER="workslip-postman-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"
BACKEND_LOG="${RUNNER_TEMP:-/tmp}/workslip-postman-backend.log"
BACKEND_PID=""

cleanup() {
  if [[ -n "${BACKEND_PID}" ]]; then
    kill "${BACKEND_PID}" >/dev/null 2>&1 || true
    wait "${BACKEND_PID}" >/dev/null 2>&1 || true
  fi
  docker rm --force "${SQL_CONTAINER}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

for command in docker dotnet node npx curl openssl; do
  if ! command -v "${command}" >/dev/null 2>&1; then
    echo "ERROR: Required command '${command}' is unavailable." >&2
    exit 70
  fi
done

sql_password="Workslip$(openssl rand -hex 16)!A1"
jwt_key="$(openssl rand -hex 32)"

if [[ "${GITHUB_ACTIONS:-false}" == "true" ]]; then
  echo "::add-mask::${sql_password}"
  echo "::add-mask::${jwt_key}"
fi

echo "Starting ephemeral SQL Server container ${SQL_CONTAINER}."
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
export Azure__Sql__ConnectionString="Server=localhost,1433;Initial Catalog=WorkslipPostman;User Id=sa;Password=${sql_password};Encrypt=false;TrustServerCertificate=true;MultipleActiveResultSets=true"
export Jwt__Issuer=workslip-actions-local
export Jwt__Audience=workslip-actions-local
export Jwt__SigningKey="${jwt_key}"
export Workslip__ApplyLocalMigrations=true
export Workslip__SeedDevelopmentData=true
export Workslip__SeedDevelopmentEntraIdentities=false

echo "Restoring and starting Workslip API in Development against the ephemeral database."
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
    echo "ERROR: Workslip API exited before /health became ready." >&2
    exit 72
  fi
  sleep 1
done

if [[ "${api_ready}" != "true" ]]; then
  cat "${BACKEND_LOG}" >&2 || true
  echo "ERROR: Workslip API did not become healthy." >&2
  exit 73
fi

token_response="$(curl --fail --silent --show-error \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"email":"admin@17v3ygzs.mailosaur.net"}' \
  "${API_URL}/api/dev/token")"

auth_token="$(node -e '
  const response = JSON.parse(process.argv[1]);
  if (typeof response.token !== "string" || response.token.length === 0) process.exit(1);
  process.stdout.write(response.token);
' "${token_response}")"

if [[ -z "${auth_token}" ]]; then
  echo "ERROR: Development token endpoint returned no bearer token." >&2
  exit 74
fi

if [[ "${GITHUB_ACTIONS:-false}" == "true" ]]; then
  echo "::add-mask::${auth_token}"
fi

echo "Running Postman collection with Newman against the isolated local API."
WORKSLIP_INTEGRATION_BASE_URL="${API_URL}" \
WORKSLIP_AUTH_TOKEN="${auth_token}" \
  "${SCRIPT_DIR}/run-integration-tests.sh"

echo "Postman integration suite completed successfully."
