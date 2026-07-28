# Isolated full-stack validation

**Status:** Historical  
**Owner:** Workslip repository owner  
**Source of truth:** Git history and WOR-188  
**Review cadence:** None unless an isolated integration gate is reintroduced

The former `Full Stack Validation` GitHub Actions workflow started disposable SQL Server, API and frontend processes, then ran Postman and Selenium checks. It was removed under WOR-188 because it was expensive and ran for routine application pull requests regardless of the actual risk.

This document no longer describes active automation. There is no manually runnable workflow with that name.

Reusable principles retained from the removed workflow:

- integration tests must use isolated synthetic data;
- production Azure, SQL and credentials must not be used;
- external-provider flows require separate controlled smoke tests;
- useful failure artifacts include API logs, browser console output and screenshots.

Current validation commands are documented in the root, frontend and backend READMEs. The maintained Postman runner can be executed deliberately against localhost or a dedicated test/staging environment.
