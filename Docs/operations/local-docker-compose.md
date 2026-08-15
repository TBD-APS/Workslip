# Local full stack with Docker Compose

`docker compose up` from the repository root starts the whole local stack in dev mode:

| Service | URL / endpoint | Notes |
| --- | --- | --- |
| Frontend | http://localhost:5270 | Vite dev server, hot reload |
| API | http://localhost:5262 | `dotnet watch`, hot reload |
| SQL Server | `localhost,1433` | sa / `WorkslipLocal123!` (matches `appsettings.Local.json`) |
| Seq | http://localhost:5341 | structured log viewer |

## How it fits together

- `db` runs SQL Server 2022 with a persistent `sql-data` volume.
- `api` starts after `db` is healthy and reuses the existing Development startup path: on a fresh database it creates the schema from the EF model, baselines the migration ledger, seeds synthetic development data, and applies any pending `src/BE/infrastructure/database/migrations/*.sql` — the same behavior as running the backend natively against a fresh local SQL.
- The startup's local-SQL safety guard only trusts provably-local hosts (`localhost`, `127.0.0.1`, …). The compose service host `db` is opted in explicitly via `WORKSLIP_ADDITIONAL_LOCAL_SQL_HOSTS=db`; the guard stays closed everywhere that variable is not set.
- API source is volume-mounted at the repo-mirrored path `/src/BE/WorkslipApi`; `bin`/`obj` are shadowed with container-local volumes so host and container build artifacts never mix.
- `fe` volume-mounts `src/FE` with a container-native `node_modules` volume (esbuild/rollup binaries are OS-specific). The browser calls the API directly on `http://localhost:5262` via `VITE_API_BASE_URL`; backend CORS already allows the 5270 origin.

## Everyday commands

```bash
docker compose up            # start everything (first run: image pulls + npm ci + restore)
docker compose up -d db seq  # only infrastructure, run api/fe natively as before
docker compose down          # stop; data volumes survive
docker compose down -v       # stop and wipe DB/node_modules/nuget volumes
```

The native workflow (`./dev.ps1`, backend and frontend on the host) keeps working unchanged — it expects SQL on `localhost,1433`, which `docker compose up -d db` provides.

## Migrating from the manual containers

Older setups ran hand-started `workslip-sql`/`workslip-seq` containers on the same ports. Stop and remove those once before the first `docker compose up`:

```bash
docker rm -f workslip-sql workslip-seq
```

The compose `db` starts empty; the API seeds it on first startup. Data from the old manual SQL container is not carried over.

## Known limitations

- First `up` is slow: SQL image pull, full `npm ci`, full NuGet restore.
- `dotnet watch` and Vite use polling file watchers in containers; large rebuilds are slower than native.
- The SA password is a local-only credential and is intentionally committed; never reuse it outside compose.
- On Apple Silicon the SQL Server image runs under amd64 emulation (Rosetta); expect slower DB startup.
