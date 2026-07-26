# Application subdomain cutover runbook

Status: Active rollout runbook; cutover not yet completed  
Owner: Workslip  
Source of truth: Vercel domain settings, DNS provider, `src/BE/infrastructure`, API configuration and frontend auth code  
Review cadence: before each production-domain or authentication-origin change  
Linear: WOR-180

## Target topology

| Host | Purpose | Platform |
|---|---|---|
| `www.workslip.dk` | Public marketing site | GitHub Pages |
| `app.workslip.dk` | Production application | Vercel |
| `workslip-v2-0.vercel.app` | Temporary rollback origin | Vercel |

The application and marketing site remain separate deployments even though both are sourced from the same GitHub repository.

## Code changes in WOR-180

The cutover PR:

- makes `https://app.workslip.dk` the application and invitation base URL;
- allows the new origin in API CORS configuration;
- retains the current Vercel origin temporarily for rollback;
- adds Entra SPA callbacks for `/login` and `/invite/callback` on the new origin;
- updates marketing-site links to the new application host.

The PR must remain draft until the pre-cutover checks below are complete.

## Pre-cutover checks

1. Add `app.workslip.dk` to the existing Vercel project that deploys `src/FE`.
2. Add the exact DNS record shown by Vercel for the `app` host.
3. Wait until Vercel reports the domain as verified and an HTTPS certificate is active.
4. Confirm that `https://app.workslip.dk` serves the current production frontend before changing public links.
5. In the production Vercel environment, inspect these optional variables:
   - `VITE_AZURE_AD_LOGIN_REDIRECT_URI`
   - `VITE_AZURE_AD_REDIRECT_URI`
6. If they are set, change them to:
   - `https://app.workslip.dk/login`
   - `https://app.workslip.dk/invite/callback`
7. Redeploy the production frontend after changing environment variables.
8. Add the new Entra redirect URIs and API CORS origin additively before the public cutover, then record the configuration evidence in WOR-180.

If the redirect environment variables are unset, the frontend derives callbacks from `window.location.origin`; the corresponding Entra redirect URIs are still required.

## Cutover sequence

1. Confirm the Vercel domain, DNS and TLS checks above are green.
2. Confirm both new Entra SPA redirect URIs exist.
3. Confirm the API accepts `https://app.workslip.dk` in `Cors:AllowedOrigins`.
4. Mark the WOR-180 PR ready only after the three checks above are recorded.
5. Squash-merge the PR.
6. Deploy the infrastructure changes so Entra and Azure App Configuration match the merged source.
7. Deploy/restart the API if required for refreshed configuration.
8. Let the GitHub Pages workflow publish the updated marketing links.
9. Run the smoke tests below.

Do not remove the old Vercel origin during the initial cutover. It is the rollback path until the new host has completed an agreed soak period.

## Smoke tests

Test in a clean browser session:

- `https://www.workslip.dk` loads the marketing site over HTTPS;
- the primary Workslip link opens `https://app.workslip.dk/app`;
- unauthenticated routing reaches the login flow;
- Microsoft login returns to `https://app.workslip.dk/login`;
- an invitation link uses `https://app.workslip.dk/invite`;
- invite enrollment returns to `https://app.workslip.dk/invite/callback`;
- authenticated API calls do not fail CORS preflight;
- logout and reauthentication work;
- service worker/PWA assets load without mixed-content errors;
- the old Vercel origin remains available for rollback but is not linked publicly.

Record URLs, timestamps and any relevant workflow/deployment identifiers in WOR-180.

## Rollback

If the new host fails before public links are switched:

1. Leave the PR as draft.
2. Restore the prior Vercel environment variables if they were changed.
3. Keep the additive Entra and CORS entries; they are harmless while unused.

If the new host fails after merge:

1. Revert the WOR-180 PR.
2. Restore `Azure:Domain:BaseUrl` and `Azure:Acs:InviteBaseUrl` to the Vercel origin through the normal infrastructure deployment.
3. Redeploy the API and marketing site.
4. Verify login, invitations and API calls through `https://workslip-v2-0.vercel.app`.
5. Do not repoint `www.workslip.dk` to the application as a shortcut.

## Post-cutover cleanup

After the soak period:

- remove the old Vercel origin from production CORS;
- remove the old Vercel Entra redirect URIs;
- remove obsolete redirect environment variables where dynamic origin is sufficient;
- verify all emails, documentation and public links use `app.workslip.dk`;
- keep Vercel's platform URL available only if required operationally, not as a public product URL.
