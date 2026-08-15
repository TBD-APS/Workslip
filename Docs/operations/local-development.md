# Local development

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** root `dev.ps1`, `tools/dev/start.ps1`, tracked Development configuration, backend startup safety and frontend package scripts  
**Review cadence:** On local bootstrap, runtime configuration or prerequisite changes

## Canonical Windows path

A fresh Windows developer machine must use the repository root bootstrap as the normal full-stack entry point:

```powershell
.\dev.ps1
```

Do not reconstruct missing local settings from chat history, copy production configuration, enable remote SQL, or route synthetic test users through Entra just to make local development start.

The supported first-run contract is:

1. verify .NET 10, Node.js 24, npm and SQL Server LocalDB;
2. create/start only the standard local `MSSQLLocalDB` instance when needed;
3. restore backend dependencies and run `npm ci`;
4. generate the frontend API client from the backend contract in the current working tree before the backend starts;
5. start the backend in `Development` with synthetic database seeding enabled and a process-local ephemeral LocalJwt signing key;
6. wait for `http://localhost:5262/health`;
7. prove `POST /api/dev/token` followed by authenticated `GET /api/auth/me` succeeds for the synthetic Admin user;
8. start Vite on `http://127.0.0.1:5270` using the already generated local contract;
9. open the frontend unless `-NoBrowser` was requested.

Backend and frontend logs are written under the operating-system temporary directory at `workslip-dev-logs`, not inside the repository.

## Phone testing on the local network

Use the maintained mobile mode instead of changing `vite.config.ts`, exposing the backend directly, or creating an ad-hoc public tunnel:

```powershell
.\dev.ps1 -Mobile
```

`-Mobile` keeps the normal backend on `http://localhost:5262`, but starts Vite with a LAN bind (`0.0.0.0`) and prints one or more detected phone URLs such as:

```text
Phone:    http://192.168.1.42:5270
```

Open the printed URL in Safari/Chrome on a phone connected to the same trusted Wi-Fi/LAN. Browser API requests continue through Vite's same-origin `/api` proxy to the localhost backend, so SQL Server LocalDB and the backend port are not exposed to the network.

If Windows Firewall prompts for Node.js, allow **Private networks only**. The bootstrap does not create or widen firewall rules automatically. Avoid mobile mode on public/untrusted networks because the Vite development server, frontend source and synthetic development session are reachable by devices that can connect to that LAN endpoint.

If the phone cannot connect:

1. verify the phone and PC are on the same Wi-Fi/LAN;
2. retry with VPN disconnected if it has taken over the default network route;
3. confirm Windows Firewall allows Node.js on the active Private network;
4. check whether the access point has client/AP isolation enabled;
5. rerun `.\dev.ps1 -Mobile -CheckOnly` to see the currently detected LAN URL(s).

The LAN URL uses plain HTTP. That is appropriate for responsive layout, touch interaction and normal authenticated application flows, but mobile browsers do not treat an ordinary LAN HTTP origin as a secure context. Service-worker/PWA installation, push and other HTTPS-only browser capabilities therefore require an approved HTTPS test/staging environment rather than a public quick tunnel to the local development server.

## Useful modes

```powershell
# Non-mutating prerequisite/port check.
.\dev.ps1 -CheckOnly

# Validate prerequisites and show the current phone URL without starting anything.
.\dev.ps1 -Mobile -CheckOnly

# Reuse already restored/installed dependencies.
.\dev.ps1 -SkipInstall

# Start without opening a browser on the PC.
.\dev.ps1 -NoBrowser

# Start full stack and expose only Vite to the trusted LAN for phone testing.
.\dev.ps1 -Mobile
```

`-CheckOnly` must not create a LocalDB instance, install dependencies, seed data or start application processes. If the LocalDB instance is missing it reports that the normal bootstrap must create it.

## Local-only safety boundary

Normal Development startup is local-only:

- the launch profiles must not set `ALLOW_REMOTE_SQL`;
- backend SQL isolation must accept only a provably local target and defaults fresh Windows machines to `MSSQLLocalDB`;
- synthetic dev users authenticate through `/api/dev/token` and LocalJwt, not Entra;
- the bootstrap generates a LocalJwt signing key only in the process environment for the started backend and restores the parent shell afterwards;
- Azure App Configuration, Entra identity provisioning, ACS email and production credentials are not prerequisites for the normal local login/read path;
- `-Mobile` broadens only the Vite listener to the LAN; the backend and LocalDB remain loopback/local-only behind the Vite API proxy.

The explicit platform Superadmin bootstrap remains a separate operator workflow described in the backend README. Do not broaden its remote-SQL exception into normal local development.

## Definition of working local development

A local setup is not considered working merely because restore/build succeeds. The bootstrap must reach all of these runtime checkpoints:

```text
LocalDB available
Backend READY /health
Development seed complete
/api/dev/token -> token
/api/auth/me -> authenticated synthetic user
Frontend READY on 127.0.0.1:5270
```

For `-Mobile`, the same checkpoints apply plus at least one detected LAN phone URL. The bootstrap verifies frontend readiness over loopback; physical-device reachability still depends on the active LAN and Windows firewall and must be checked from the phone when mobile validation is required.

If one of those fails on a clean supported Windows machine, treat the bootstrap/setup as a Workslip defect rather than asking the developer to invent machine-specific configuration.

## Manual component commands

Backend-only and frontend-only commands remain documented in their scoped READMEs for debugging and focused development. They are advanced component paths, not a replacement for the canonical fresh-machine full-stack bootstrap.
