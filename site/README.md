# Workslip public site

Status: Active foundation, pre-domain rollout  
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

`Gemfile.lock` is committed. Update dependencies intentionally with Bundler, commit the resulting lockfile and let CI verify that Gemfile and Gemfile.lock agree.

## Build and validation

From the repository root:

```powershell
cd site
bundle exec jekyll build --strict_front_matter
cd ..
python site/scripts/validate_output.py site/_site
```

The validator checks required pages, one H1 and one `main-content` landmark per generated HTML page, plus local links and assets.

## Deployment

Pull requests run `.github/workflows/site-validate.yml`. Merges to `main` that change `site/**` run `.github/workflows/pages.yml` and deploy through the protected `github-pages` environment.

GitHub Pages must be configured to use **GitHub Actions** as its source. Do not activate `workslip.dk` until ownership, DNS records, HTTPS and rollback have been verified according to `Docs/operations/github-pages-domain-runbook.md`.

## Public-content rules

- Describe only functionality that is implemented and validated in the deployed application.
- Keep demo, security, privacy, terms and status claims explicit about incomplete rollout work.
- Do not add analytics, cookies or third-party embeds without a documented privacy decision.
- Do not expose production secrets, private repository data or internal operational details.

## Architecture boundary

This Jekyll site is static marketing and documentation content. It must not contain application business logic, production secrets, direct database access or duplicated React application behavior. The real application remains under `src/FE`; the isolated demo is delivered separately.
