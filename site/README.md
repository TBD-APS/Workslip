# Workslip public site

Status: Planned foundation
Owner: Workslip
Source of truth: this directory for public-site implementation; root repository documentation for architecture and operations
Review cadence: each public-site release

## Local development

Requirements: Ruby 3.3 and Bundler.

```powershell
cd site
bundle install
bundle exec jekyll serve --livereload
```

Open `http://localhost:4000`.

## Build

```powershell
cd site
bundle exec jekyll build --strict_front_matter
```

## Deployment

Pull requests run `.github/workflows/site-validate.yml`. Merges to `main` that change `site/**` run `.github/workflows/pages.yml` and deploy through the protected `github-pages` environment.

GitHub Pages must be configured to use **GitHub Actions** as its source. Do not activate `workslip.dk` until ownership, DNS records, HTTPS and rollback have been verified according to `Docs/operations/github-pages-domain-runbook.md`.

## Architecture boundary

This Jekyll site is static marketing and documentation content. It must not contain application business logic, production secrets, direct database access or duplicated React application behavior. The real application remains under `src/FE`; the isolated demo is delivered separately.
