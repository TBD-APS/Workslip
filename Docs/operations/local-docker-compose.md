# Local full stack with Docker Compose

`docker compose up` from the repository root starts the whole local stack in dev mode:

| Service | URL / endpoint | Notes |
| --- | --- | --- |
| Frontend | http://localhost:5270 | Vite dev server, hot reload |
| API | http://localhost:5262 | `dotnet watch`, hot reload |
| SQL Server | `localhost,1433` | sa / `WorkslipLocal123!` (matches `appsettings.Local.json`) |
| Redis | `localhost:6379` | `HybridCache` L2; ephemeral, no password, no persistence |
| Seq | http://localhost:5341 | structured log viewer |

## Cleaner startup

On the **first** start Compose waits ~1 minute for SQL Server to report healthy before it creates the API (which `depends_on` the db healthcheck). During that wait some terminals redraw the progress line repeatedly, printing many identical `[+] up 6/7... Created` lines. This is expected waiting, not an error — the stack finishes once the db is healthy (subsequent starts are faster because the SQL volume is already initialized).

To avoid the redraw noise, use the `make` wrappers, which run with plain, line-based progress and wait until the stack is ready:

```bash
make up        # start; clean output; waits until ready
make down      # stop (keeps data)
make down-hard # stop and wipe the local DB volume
make logs      # follow logs
```

Equivalent raw command: `docker compose up -d --wait --quiet-pull --progress plain`.

## How it fits together

- `db` runs SQL Server 2022 with a persistent `sql-data` volume.
- `redis` runs Redis 8 with persistence switched off (`--save "" --appendonly no`) and no named volume, so `docker compose down` leaves nothing behind. The `api` service sets `Azure__Redis__ConnectionString=redis:6379`, which turns on `HybridCache`'s shared second level locally. Delete that one line from `docker-compose.yml` to run the API the way it runs in an environment with no Redis configured; the container can stay up, it is the configuration key that decides. See [Cache diagnostics](CACHE_DIAGNOSTICS.md) for what changes when it is on.
- `api` starts after `db` is healthy and reuses the existing Development startup path: on a fresh database it creates the schema from the EF model, baselines the migration ledger, seeds synthetic development data, and applies any pending `src/BE/infrastructure/database/migrations/*.sql` — the same behavior as running the backend natively against a fresh local SQL.
- The startup's local-SQL safety guard only trusts provably-local hosts (`localhost`, `127.0.0.1`, …). The compose service host `db` is opted in explicitly via `WORKSLIP_ADDITIONAL_LOCAL_SQL_HOSTS=db`; the guard stays closed everywhere that variable is not set.
- API source is volume-mounted at the repo-mirrored path `/src/BE/WorkslipApi`; `bin`/`obj` are shadowed with container-local volumes so host and container build artifacts never mix.
- `fe` volume-mounts `src/FE` with a container-native `node_modules` volume (esbuild/rollup binaries are OS-specific).
- Frontend API calls are same-origin (`/api`). Vite proxies them to `http://api:5262` inside the Compose network. This is important for phone testing: `localhost` in Safari on an iPhone would otherwise mean the iPhone itself, not the development Mac.

## Canonical dev command

`dev.ps1` is platform-aware:

- Windows keeps using the native LocalDB/bootstrap flow in `tools/dev/start.ps1`.
- macOS/Linux uses the Docker Compose full stack in `tools/dev/start-docker.ps1`.

Run the current checked-out branch:

```powershell
./dev.ps1 -NoBrowser
```

Run the current checked-out branch and expose the frontend for phone testing:

```powershell
./dev.ps1 -Mobile -NoBrowser
```

On macOS/Linux the script validates Docker/Compose, starts `db`, `api`, `fe` and `seq`, waits for API + frontend readiness, prints the current Git branch/SHA, resolves the machine's LAN IPv4 address and prints a phone URL such as:

```text
Phone: http://192.168.1.42:5270/app/overblik
```

The phone and development machine must be on the same trusted Wi-Fi/LAN. The API is not exposed to the phone as a separate origin; Safari calls `/api` on the frontend origin and Vite proxies the request inside Docker.

`-CheckOnly` validates Docker/Compose and LAN address resolution without changing containers:

```powershell
./dev.ps1 -Mobile -CheckOnly
```

`-Main` retains its explicit behavior on all platforms: it refuses dirty worktrees, switches to `main`, fast-forwards from `origin/main`, and then starts the platform-specific dev stack. Omit `-Main` when you want to test the branch currently checked out.

## Everyday Docker commands

```bash
docker compose up            # start everything (first run: image pulls + npm ci + restore)
docker compose up -d db redis seq  # only infrastructure, run api/fe natively as before
docker compose down          # stop; data volumes survive
docker compose down -v       # stop and wipe DB/node_modules/nuget volumes
```

The Windows native workflow still expects SQL on `localhost,1433`, which `docker compose up -d db` provides.

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
- The local Redis has no password and is published on `localhost:6379`. That is acceptable only because it is bound to the development machine and holds nothing that is not rebuildable from the local database. Do not point a local API at a shared or remote Redis, and do not copy this service definition into anything that is deployed.
- The Windows native path (`tools/dev/start.ps1`, LocalDB) does not start Redis, so a native Windows run is L1-only unless `docker compose up -d redis` is started alongside it and `Azure__Redis__ConnectionString=localhost:6379` is set for the backend process.
- On Apple Silicon the SQL Server image runs under amd64 emulation (Rosetta); expect slower DB startup.
- Local phone access can still be blocked by host firewall/VPN/network isolation. The bootstrap checks the LAN URL from the host and prints a focused warning if localhost works but the LAN URL does not.
