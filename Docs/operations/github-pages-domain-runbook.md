# Workslip GitHub Pages domain ownership

Status: Active cleanup runbook
Owner: Workslip maintainers
Source of truth: GitHub Pages settings, `site/_config.yml`, `.github/workflows/pages.yml`, and the company website repository
Review cadence: before each Pages/domain change

## Host ownership

| Host | Owner and purpose |
| --- | --- |
| `mrsoftware.dk` and `www.mrsoftware.dk` | Company homepage in `TBD-APS/mrsoftware-website`, served by Cloudflare Worker `mrsoftware` |
| `https://tbd-aps.github.io/Workslip/` | Planned fallback address for this Workslip Jekyll site after the legacy Pages custom domain is cleared |
| `app.mrsoftware.dk` | Workslip production application; retain its Azure hosting and security boundaries |
| `demo.mrsoftware.dk` | Separately approved demo environment |

The former instructions to point the company apex and www at this Pages site are superseded. The source no longer contains `site/CNAME`; `site.url` and `site.baseurl` now describe the project-site address. Source changes do not clear the remote Pages custom-domain setting or change DNS.

## Separately approved settings cleanup

1. Record the current Pages configuration, domain/DNS ownership and rollback evidence. The 26 October audit observed Pages custom domain `www.mrsoftware.dk`, while the source had claimed the apex.
2. After owner approval, clear only the Workslip repository's Pages custom domain. Keep GitHub Actions as the Pages publishing source. Preserve the company Worker's domains, DNS and Access settings.
3. Configure `github-pages` for `main` only. The deploy Action requires the exact current main SHA from one PR merged by Rasmus and a successful exact-main `CI Gate`. Preserve existing environment reviews.
4. Validate the next owner-merged release at the project-site address: strict Jekyll build, generated links, project base path, sitemap and canonical URLs.
5. Read-only verify the company apex/www, application and demo hostnames independently. A Pages build proves no application/API health.

Do not change Azure RBAC, OIDC subjects, CORS, Entra callbacks or the application's host as part of this Pages cleanup. Do not restore the obsolete company-domain claim to recover a defective Workslip static build.

## Rollback and evidence

Capture the prior Pages setting before mutation. A source regression is reverted through a new PR merged by Rasmus, followed by its deploy Action. Restoring a remote custom-domain claim requires a separate verified ownership decision; it is not an automatic rollback.

Completion requires the approved remote setting change, a successful exact-SHA Pages deployment, working project-site links/canonical metadata, and independent read-only checks that the company and application hosts still reach their respective services. Until those exist, report the source preparation and live cleanup separately.
