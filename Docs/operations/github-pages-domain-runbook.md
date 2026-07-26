# GitHub Pages and custom domain runbook

Status: Active rollout runbook; Pages and domain state need verification  
Owner: Workslip  
Source of truth: GitHub repository settings, DNS provider configuration and `.github/workflows/pages.yml`  
Review cadence: before each domain or Pages configuration change  
Linear: WOR-168, WOR-169

## Decision

The public marketing site is built with Jekyll from `site/` and deployed through GitHub Actions to GitHub Pages. It remains separate from the React/Vite application under `src/FE`.

Target domain layout:

| Host | Purpose |
|---|---|
| `workslip.dk` | Canonical public marketing site |
| `www.workslip.dk` | Redirect to canonical host |
| `app.workslip.dk` | Production application |
| `demo.workslip.dk` | Isolated interactive demo |

Repository changes do not prove that Pages settings, domain ownership or DNS have been configured. Those settings must be verified directly during rollout.

## Code readiness gate

Before changing repository settings or DNS, require all of the following:

- `site/Gemfile` contains one version per dependency.
- `site/Gemfile.lock` is committed and Bundler runs in frozen mode in CI.
- Jekyll builds with strict front matter.
- `site/scripts/validate_output.py` passes against the generated `_site` directory.
- Required outputs include home, features, demo, security, privacy, terms, status, changelog, 404, robots and sitemap.
- Public content contains no production secret, private operational detail or unverified product claim.
- The GitHub Pages deployment workflow uses only `contents: read`, `pages: write` and `id-token: write`.

## Initial GitHub configuration

These steps require repository-administrator access and must be recorded as completed in WOR-169 or a successor rollout issue.

1. Open **Settings → Pages**.
2. Set the publishing source to **GitHub Actions**.
3. Open **Settings → Environments → github-pages**.
4. Restrict deployment to `main` and configure appropriate production protection.
5. Run the Pages workflow manually before adding a custom domain.
6. Record the workflow run URL and deployed `github.io` URL.
7. Verify navigation, mobile layout, 404 behavior, robots, sitemap and HTTPS on the fallback URL.
8. Confirm that a failed deployment does not alter the currently served artifact.

Do not mark Pages as active based only on the presence of `.github/workflows/pages.yml`.

## Domain verification

1. Confirm ownership of the selected domain at the DNS provider.
2. Capture a zone export or screenshots of the current records and TTL values.
3. Add the GitHub-provided TXT verification record for the exact domain.
4. Wait for DNS propagation and verify from more than one resolver.
5. Verify the domain in the GitHub account settings.
6. Keep the verification TXT record after verification to reduce takeover risk.

Do not copy DNS values from an old guide. Use the values shown by GitHub when the change is performed.

## DNS rollout

1. Lower relevant TTL values in advance when practical.
2. Configure the apex records using GitHub's current Pages addresses.
3. Configure `www` as a CNAME to the GitHub Pages hostname shown for the account.
4. Keep `app` and `demo` separate from Pages and pointed at their actual hosting providers.
5. Validate DNS from more than one resolver.
6. Add `workslip.dk` as the Pages custom domain.
7. Wait for GitHub's certificate provisioning.
8. Enable **Enforce HTTPS** only after the certificate is active.
9. Verify canonical redirect behavior between apex and `www`.
10. Re-run smoke checks against both the canonical domain and the GitHub Pages fallback.

## Demo rollout boundary

The marketing page may link to the current application, but it must not embed the full production application.

A future iframe requires all of the following:

- an isolated `demo.workslip.dk` deployment with no production connectivity;
- explicit `frame-ancestors` and marketing-site `frame-src` restrictions;
- short-lived demo sessions and deterministic reset;
- no registration, billing, destructive settings or privileged integrations;
- Playwright coverage for persona, authorization, tenant isolation and accessibility flows.

Until WOR-125 through WOR-128 satisfy those gates, the demo page remains an explanatory entry page without an iframe.

## Security and privacy requirements

- The repository must not expose production secrets in site content, build logs or Pages artifacts.
- The Jekyll site must not call internal APIs with privileged credentials.
- Public feature claims must match currently deployed and validated behavior.
- Security, privacy and terms pages remain explicitly provisional until approved owners and contact channels exist.
- Analytics, cookies and third-party embeds require a documented privacy decision before activation.
- A public status page must use an authoritative automated source rather than a manually maintained green indicator.

## Validation checklist

- Jekyll build passes with strict front matter.
- Bundler runs with the committed lockfile in frozen mode.
- Generated output validation passes.
- Navigation works with keyboard and visible focus.
- Mobile layout has no horizontal overflow.
- Sitemap and SEO metadata are generated.
- `robots.txt` references the canonical sitemap.
- HTTPS is valid and mixed content is absent.
- Apex and `www` canonical behavior is correct.
- `app` and `demo` hosts still resolve to their intended platforms.
- No private repository data appears in generated HTML.

## Rollback

If the generated site is defective but DNS is healthy:

1. Revert the responsible PR.
2. Run the Pages deployment workflow.
3. Verify that the prior content is served.
4. Record the failed and recovery workflow runs.

If custom-domain activation causes an outage:

1. Remove the custom domain from GitHub Pages.
2. Restore the previous DNS records from the captured zone snapshot.
3. Verify the `github.io` fallback site.
4. Do not retry until DNS values, ownership verification and certificate state are understood.

If `app` or `demo` is affected, treat that as a separate hosting incident; do not repoint those hosts to GitHub Pages as a shortcut.

## Completion evidence

WOR-169 can complete the repository portion when the hardening PR is merged and CI is green. Full domain rollout requires separate evidence:

- GitHub Pages source screenshot or recorded setting;
- protected `github-pages` environment configuration;
- successful deployment workflow URL;
- verified fallback URL;
- domain ownership verification;
- DNS record snapshot;
- active certificate and enforced HTTPS;
- canonical redirect and smoke-test results.

## Future extraction

The `site/` directory is intentionally self-contained. It may later move to a dedicated `workslip-site` repository. That move must preserve history where practical, update Pages settings and workflows, and document the new source-of-truth boundary.
