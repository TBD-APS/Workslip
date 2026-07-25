# Isolated full-stack validation

`Full Stack Validation` is a GitHub Actions workflow for exercising Workslip without using the production Azure deployment or database.

## What it starts

Each workflow run receives fresh, disposable resources:

1. SQL Server 2022 Developer container.
2. Workslip API running with `ASPNETCORE_ENVIRONMENT=Development`.
3. Development seed data and local JWT authentication.
4. Vite frontend configured to use the local API.
5. Headless Chrome controlled through Selenium.

All resources are destroyed when the GitHub-hosted runner finishes.

## Validation stages

The workflow:

- restores and builds the .NET API;
- installs and builds the frontend;
- waits for SQL Server and API health;
- creates a unique isolated organization;
- runs a generated CI-safe subset of the maintained Postman collection through Newman;
- starts the frontend and performs Selenium smoke tests for development login, customer listing and customer creation navigation;
- uploads API logs, frontend logs, Newman JUnit output, the generated CI collection, screenshots, page HTML and browser-console output.

## External-provider exclusions

The generated Newman collection excludes requests that require services unavailable inside the disposable stack:

- email one-time-code delivery and verification;
- Microsoft Entra login and enrollment;
- invitation delivery and invitation-token callbacks;
- browser push-subscription creation;
- external cache invalidation.

These endpoints remain in the maintained Postman collection. The workflow filters them only for the isolated run and prints every exclusion in its log.

## Separation from deployment

The workflow does not:

- call `azure/login`;
- use an Azure GitHub environment;
- read production SQL or integration secrets;
- deploy an API or frontend;
- invoke `.github/workflows/main_api-npteknik-prod.yml`.

It can run automatically for relevant pull requests or manually through **Actions → Full Stack Validation → Run workflow**.

## Test-data policy

The database is ephemeral and uses synthetic seed data plus a unique organization generated for the workflow run. Production customer data, uploaded customer workbooks and production credentials must not be added to this workflow or its artifacts.
