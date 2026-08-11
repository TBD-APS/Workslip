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

## Build and validation

From the repository root:

```powershell
cd site
bundle exec jekyll build --strict_front_matter
cd ..
python site/scripts/validate_output.py site/_site
```

The validator checks required pages, one H1 and one `main-content` landmark per generated HTML page, plus local links and assets.

There is currently no dedicated pull-request workflow that builds the Jekyll site. The normal documentation checker covers the maintained Markdown surface, while a site-affecting change still requires the local Jekyll build/output validation above until equivalent PR automation is introduced.

## Deployment

Pushes to `main` that change `site/**` or `.github/workflows/pages.yml` run `.github/workflows/pages.yml`. That workflow builds the Jekyll site with strict front matter, validates the generated output and deploys through the `github-pages` environment. It can also be started manually through `workflow_dispatch`.

The canonical marketing-site domain is `https://mrsoftware.dk`. GitHub Pages must use **GitHub Actions** as its source, and the repository Pages setting must use `mrsoftware.dk` as the custom domain. DNS, HTTPS and rollback steps are documented in `Docs/operations/github-pages-domain-runbook.md`.

The production application is hosted separately at `https://app.mrsoftware.dk`. The marketing site must not call the production API directly unless that origin is deliberately added to the API CORS configuration.

## Public-content rules

- Describe only functionality that is implemented and validated in the deployed application.
- Keep demo, security, privacy, terms and status claims explicit about incomplete rollout work.
- Do not add analytics, cookies or third-party embeds without a documented privacy decision.
- Do not expose production secrets, private repository data or internal operational details.

## Architecture boundary

This Jekyll site is static marketing and documentation content. It must not contain application business logic, production secrets, direct database access or duplicated React application behavior. The real application remains under `src/FE`; the isolated demo is delivered separately.
