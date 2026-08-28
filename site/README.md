# Workslip public site

Status: Active foundation, mrsoftware.dk rollout  
Owner: Workslip  
Source of truth: this directory for public-site implementation; root repository documentation for architecture and operations  
Review cadence: each public-site release

## Local development

Requirements: Ruby 3.3, Bundler and Python 3.12 or newer.

```powershell
cd site
bundle config set path vendor/bundle
bundle install
bundle exec jekyll serve --livereload
```

Open `http://localhost:4000`.

`Gemfile.lock` is committed. Update dependencies intentionally with Bundler, commit the resulting lockfile and let the relevant repository validation verify that Gemfile and Gemfile.lock agree.

## Tenant branding (WOR-447)

Branding is data-driven. Shared templates stay fork-free.

### Data files

| File | Role |
|------|------|
| `_data/theme.yml` | Default Workslip branding (safe fallback) |
| `_data/companies/<id>.yml` | Optional per-company override |

### How to fill branding (anyone on the team)

1. Copy `_data/companies/example-acme.yml` to `_data/companies/<company-id>.yml`.
2. Set `display_name`, colors, optional contact email.
3. Add logo assets under `assets/brands/<company-id>/` (SVG preferred) and point `logo.primary` / `favicon` at them.
4. Preview with company override:

```powershell
cd site
bundle exec jekyll serve --livereload --config _config.yml --company example-acme
```

Jekyll does not natively take `--company`. Until a small config wrapper exists, set in `_config.yml` temporarily:

```yaml
company: example-acme
```

or pass via environment-driven config merge in CI. Missing/unknown company always falls back to `_data/theme.yml` (Workslip defaults). Never use another company's assets as fallback.

### Rules

- No per-customer branches or page forks.
- No arbitrary tenant CSS/JS in v1.
- Empty logo fields render the text brand name.
- Section flags under `sections:` can hide nav items without editing layout.

### Minimum assets when a real company goes live

- Logo primary (SVG or transparent PNG)
- Favicon (32×32 + 180×180 recommended)
- Brand colors (primary + accent at minimum)

Defaults already cover missing assets so the site never looks broken or leaks another tenant.

## Build and validation

From the repository root:

```powershell
cd site
bundle exec jekyll build --strict_front_matter
cd ..
python site/scripts/validate_output.py site/_site
```

The validator checks required pages, one H1 and one `main-content` landmark per generated HTML page, plus local links and assets.

Pull requests that only change `site/**` or `.github/workflows/pages.yml` use the dedicated **Static site fast lane**: strict Jekyll build plus generated-output/link validation. They do not run Workslip backend tests, frontend-app build/tests, Postman or app Playwright. Browser/screenshot checks are optional review evidence rather than a deployment gate. Pull-request runs never deploy GitHub Pages.

## Deployment

Pushes to `main` that change `site/**` or `.github/workflows/pages.yml` run `.github/workflows/pages.yml`. That workflow builds the Jekyll site with strict front matter, validates the generated output and deploys through the `github-pages` environment. It can also be started manually through `workflow_dispatch`, but production deployment is hard-gated to `main`.

The canonical marketing-site domain is `https://mrsoftware.dk`. GitHub Pages must use **GitHub Actions** as its source, and the repository Pages setting must use `mrsoftware.dk` as the custom domain. DNS, HTTPS and rollback steps are documented in `Docs/operations/github-pages-domain-runbook.md`.

The production application is hosted separately at `https://app.mrsoftware.dk`. The marketing site must not call the production API directly unless that origin is deliberately added to the API CORS configuration.

## Public-content rules

- Describe only functionality that is implemented and validated in the deployed application.
- Keep demo, security, privacy, terms and status claims explicit about incomplete rollout work.
- Do not add analytics, cookies or third-party embeds without a documented privacy decision.
- Do not expose production secrets, private repository data or internal operational details.

## Architecture boundary

This Jekyll site is static marketing and documentation content. It must not contain application business logic, production secrets, direct database access or duplicated React application behavior. The real application remains under `src/FE`.

`/demo/` is a deliberately limited, public product walkthrough: it may render fictional, bundled scenarios and client-only UI feedback so visitors can understand the flow without a login. It must not authenticate users, call an API, access production data, use application components or routes, persist data in browser storage, or claim to be an integrated product environment. A real integrated demo remains a separate deployment and needs its own security gates.
