# Documentation release gate

**State:** Maintained  
**Owner:** Release owner  
**Review:** Every production release  
**Linear:** WOR-148

A release is not documentation-ready because files merely exist. The artifacts must match the code and deployment being released.

## Required checks

- [ ] `CI Gate` is green for the pull request being merged.
- [ ] `python tools/docs/check_docs.py` is green when maintained documentation changed.
- [ ] Every included PR selected exactly one documentation decision.
- [ ] No expired documentation waiver is included.
- [ ] Changed API routes, auth, errors or contracts are reflected in OpenAPI, Postman and `Docs/api`.
- [ ] Changed architecture, trust boundary, persistence or dataflow has an updated architecture page or ADR.
- [ ] Changed deployment/configuration behaviour has an updated runbook.
- [ ] Changed user-visible flow has an updated role guide or release note.
- [ ] Known limitations state what is implemented, partial and not implemented.
- [ ] No secret values, tokens or personal data were added to documentation.

## Impact matrix

| Change | Minimum documentation evidence |
|---|---|
| API route/model/error/permission | API catalog/contract decision and Postman example/assertion |
| Authentication or role hierarchy | API auth section plus architecture/trust-boundary review |
| Database schema or transaction boundary | Migration/runbook update and ADR when the decision changes |
| Azure resource, secret or configuration source | Deployment/configuration runbook |
| PWA caching, update or offline behaviour | Architecture/offline decision and user limitation text |
| Critical user journey | Role guide and release note/known limitation |
| CI/release process | This release gate and affected workflow documentation |

## Stop conditions

Stop the release when:

- documentation describes planned behaviour as deployed;
- runtime OpenAPI and endpoint source disagree;
- an authorization or tenant boundary changed without negative verification;
- a destructive schema change lacks recovery/roll-forward guidance;
- a required runbook owner is unknown; or
- a waiver is incomplete or expired.

## Release record

For meaningful tagged releases, record:

- release identifier and commit SHA;
- reviewer;
- changed documentation artifacts;
- Postman/OpenAPI verification result;
- active waivers and follow-up issues; and
- known limitations.

The release record may live in a GitHub release or Linear release item, but it must link to the exact code revision. Routine production deployment is driven by an explicitly merged, CI-approved `main` revision rather than by a separate release branch.
