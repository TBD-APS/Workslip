# CI quality gates

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** `.github/workflows/`, repository rulesets and current successful runs  
**Review cadence:** When workflows, release environments or required checks change

## Principle

A workflow should exist only when it provides an actionable signal or performs a required deployment task. Do not keep duplicated, placeholder or routinely ignored automation.

Workflow files describe intended automation; successful runs and target-environment checks provide execution evidence.

## Current boundaries

### Release validation

`.github/workflows/release-validation.yml` runs for pushes to `release/**` and is the full-code release validation boundary.

It currently covers:

- backend Release build, backend tests and C# CodeQL;
- frontend lint, Vitest, production build and JavaScript/TypeScript CodeQL;
- release-environment policy plus Playwright/Postman source checks;
- a final `Release gate` that succeeds only when the required jobs succeed.

The repository intentionally has no broad pull-request validation workflow. Issue-scoped/local validation remains required before merge.

### Backend production deployment

`.github/workflows/main_api-mrsoftware-prod.yml` builds and deploys the API and performs its health verification. Deployment success is not a substitute for authentication, database or critical-flow smoke when those paths changed.

### Frontend production deployment

Vercel production deployment policy is defined from the frontend project/configuration. Repository workflow documentation should not duplicate Vercel dashboard state that cannot be proven from the repository.

### Documentation checks

`python tools/docs/check_docs.py` is the local documentation drift check. It is deliberately not a broad automatic PR workflow; reviewers run it when documentation or documentation-owning sources change.

## Required-check changes

When adding, renaming or removing a required check:

1. update the workflow and repository ruleset together;
2. prove the new check can succeed on its intended branch/event;
3. prove a controlled failure blocks or reports as intended;
4. remove stale required-check names;
5. document the owner and remediation path when the check is non-obvious.

A YAML change alone does not prove repository ruleset configuration.

## Security

Use GitHub OIDC for Azure deployment. Do not introduce publish profiles, long-lived Azure credentials or privileged repository-writing automation without a concrete requirement and reviewed least-privilege design.
