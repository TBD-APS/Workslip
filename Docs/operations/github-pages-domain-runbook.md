# GitHub Pages and custom domain runbook

Status: Planned
Owner: Workslip
Source of truth: GitHub repository settings, DNS provider configuration and `.github/workflows/pages.yml`
Review cadence: before each domain or Pages configuration change
Linear: WOR-168

## Decision

The public marketing site is built with Jekyll from `site/` and deployed through GitHub Actions to GitHub Pages. It remains separate from the React/Vite application under `src/FE`.

Target domain layout:

| Host | Purpose |
|---|---|
| `workslip.dk` | Canonical public marketing site |
| `www.workslip.dk` | Redirect to canonical host |
| `app.workslip.dk` | Production application |
| `demo.workslip.dk` | Isolated interactive demo |

No production DNS or GitHub Pages custom-domain setting is changed by WOR-168.

## Initial GitHub configuration

1. Merge the validated site PR.
2. In repository settings, open **Pages**.
3. Set the publishing source to **GitHub Actions**.
4. Create or verify the `github-pages` environment.
5. Restrict deployment to `main` and retain environment protection appropriate to production publishing.
6. Run the Pages workflow manually before adding the custom domain.
7. Verify the generated `github.io` site, navigation, mobile layout, 404 behavior and HTTPS.

## Domain verification

1. Confirm ownership of the selected domain at the DNS provider.
2. Add the GitHub-provided TXT verification record for the exact domain.
3. Wait for DNS propagation.
4. Verify the domain in the GitHub account settings.
5. Keep the verification TXT record after verification to reduce takeover risk.

Do not copy DNS values from an old guide. Use the values shown by GitHub when the change is performed.

## DNS rollout

1. Record the current DNS zone and TTL values before modification.
2. Lower relevant TTL values in advance when practical.
3. Configure the apex records using GitHub's current Pages addresses.
4. Configure `www` as a CNAME to the GitHub Pages hostname shown for the account.
5. Keep `app` and `demo` separate from Pages and pointed at their actual hosting providers.
6. Validate DNS from more than one resolver.
7. Add `workslip.dk` as the Pages custom domain.
8. Wait for GitHub's certificate provisioning.
9. Enable **Enforce HTTPS** only after the certificate is active.
10. Verify canonical redirect behavior between apex and `www`.

## Security requirements

* The repository must not expose production secrets in site content, build logs or Pages artifacts.
* The Jekyll site must not call internal APIs with privileged credentials.
* A future demo iframe must only target `demo.workslip.dk` and requires explicit CSP/`frame-ancestors` configuration.
* The demo must remain isolated from production data and integrations.
* Public feature claims must match currently deployed and validated behavior.

## Validation checklist

* Jekyll build passes with strict front matter.
* Required pages exist in `_site`.
* Navigation works with keyboard and visible focus.
* Mobile layout has no horizontal overflow.
* Sitemap and SEO metadata are generated.
* HTTPS is valid and mixed content is absent.
* Apex and `www` canonical behavior is correct.
* `app` and `demo` hosts still resolve to their intended platforms.
* No private repository data appears in generated HTML.

## Rollback

If the generated site is defective but DNS is healthy:

1. Revert the responsible PR.
2. Run the Pages deployment workflow.
3. Verify the prior artifact is restored.

If custom-domain activation causes an outage:

1. Remove the custom domain from GitHub Pages.
2. Restore the previous DNS records from the captured zone snapshot.
3. Verify the `github.io` fallback site.
4. Do not retry until DNS values, ownership verification and certificate state are understood.

If `app` or `demo` is affected, treat that as a separate hosting incident; do not repoint those hosts to GitHub Pages as a shortcut.

## Future extraction

The `site/` directory is intentionally self-contained. It may later move to a dedicated `workslip-site` repository. That move must preserve history where practical, update Pages settings and workflows, and document the new source-of-truth boundary.
