# GitHub Pages and custom domain runbook

Status: Active rollout runbook  
Owner: Workslip  
Source of truth: GitHub repository settings, DNS provider configuration, `site/CNAME`, `site/_config.yml` and `.github/workflows/pages.yml`  
Review cadence: before each domain or Pages configuration change  
Linear: WOR-168, WOR-169, WOR-172

## Decision

The public marketing site is built with Jekyll from `site/` and deployed through GitHub Actions to GitHub Pages. It remains separate from the React/Vite application under `src/FE`.

Approved domain layout:

| Host | Purpose |
|---|---|
| `mrsoftware.dk` | Canonical public marketing site |
| `www.mrsoftware.dk` | Redirect to the canonical marketing site |
| `app.mrsoftware.dk` | Production Workslip application |
| `demo.mrsoftware.dk` | Isolated interactive demo, only after demo security gates |

The product may continue to be named Workslip while the company and public platform domain use MR Software.

## Repository configuration

The repository must contain the following matching configuration:

- `site/CNAME` contains `mrsoftware.dk`.
- `site.url` in `site/_config.yml` is `https://mrsoftware.dk`.
- the API CORS policy allows `https://app.mrsoftware.dk`.
- no active production configuration references `workslip.dk`.

The GitHub Pages fallback remains available through the repository Pages URL, but generated canonical metadata must point to `https://mrsoftware.dk` after the custom-domain rollout.

## GitHub configuration

1. Open **Settings → Pages**.
2. Set the publishing source to **GitHub Actions**.
3. Configure `mrsoftware.dk` as the custom domain.
4. Open **Settings → Environments → github-pages**.
5. Restrict deployment to `main` and configure appropriate production protection.
6. Keep the domain-verification TXT record after verification.
7. Enable **Enforce HTTPS** only after GitHub has provisioned a valid certificate.

## DNS rollout

1. Capture the current DNS records and TTL values before modification.
2. Configure the apex records for `mrsoftware.dk` using GitHub's current Pages addresses.
3. Configure `www.mrsoftware.dk` as a CNAME to the GitHub Pages hostname shown for the account.
4. Point `app.mrsoftware.dk` to the production Vercel deployment.
5. Keep `demo.mrsoftware.dk` separate from production hosting and data.
6. Validate DNS from more than one resolver.
7. Verify HTTPS and canonical redirects for both apex and `www`.
8. Verify that `app.mrsoftware.dk` still reaches the application and can call the API without CORS errors.

Do not copy IP addresses or verification values from an old guide. Use the values currently shown by GitHub and the active DNS provider.

## Application and API boundary

The marketing site and production application are separate deployments:

- `mrsoftware.dk` serves static marketing content.
- `app.mrsoftware.dk` serves the React/Vite application.
- the API permits the production application origin through the `Frontend` CORS policy.
- the marketing site must not receive production API access merely because it shares the parent domain.
- the demo must not use production data, credentials or integrations.

Any OAuth redirect URI, logout URI, CORS origin, CSP source or external-service callback that previously referenced a temporary Vercel hostname must be reviewed before domain cutover. Temporary hostnames may remain only where they are intentionally retained as rollback paths.

## Validation checklist

- Jekyll builds with strict front matter.
- Generated-output validation passes.
- `site/CNAME` and `site.url` agree on `mrsoftware.dk`.
- sitemap, feed, canonical and social metadata use `https://mrsoftware.dk`.
- navigation works with keyboard and visible focus.
- mobile layout has no horizontal overflow.
- HTTPS is valid and mixed content is absent.
- `www.mrsoftware.dk` redirects to `mrsoftware.dk`.
- `app.mrsoftware.dk` resolves to the production frontend.
- authenticated and unauthenticated API calls from `app.mrsoftware.dk` do not fail CORS checks.
- `demo.mrsoftware.dk` remains isolated from production.
- no active configuration references `workslip.dk`.
- no private repository data or secrets appear in generated HTML.

## Rollback

If the generated site is defective but DNS is healthy:

1. Revert the responsible PR.
2. run the Pages deployment workflow;
3. verify that the prior content is served.

If custom-domain activation causes an outage:

1. Remove the custom domain from GitHub Pages.
2. Restore the previous DNS records from the captured snapshot.
3. restore `site.url` and remove or update `site/CNAME` so generated metadata matches the fallback host;
4. verify the GitHub Pages fallback site;
5. do not retry until DNS ownership and certificate state are understood.

If `app.mrsoftware.dk` is affected, treat it as a separate hosting incident. Do not repoint the application host to GitHub Pages.

## Completion evidence

The rollout is complete only when the following evidence exists:

- successful Pages deployment workflow;
- verified domain ownership;
- recorded DNS configuration;
- active certificate and enforced HTTPS;
- canonical apex and `www` redirect behavior;
- successful production application smoke test;
- successful API call from `app.mrsoftware.dk` without CORS errors;
- OAuth redirect and logout URIs verified against the deployed domain layout.
