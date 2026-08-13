# Feature branch safety

**Status:** Active  
**Owner:** Workslip maintainers  
**Source of truth:** GitHub rulesets, `.github/workflows/feature-change-guard.yml`, and `tools/release/configure-github-branch-rules.ps1`  
**Review cadence:** On branch/ruleset/PR-guard changes

## Purpose

Active `rbj--*` feature work must not disappear because of an accidental remote ref delete or non-fast-forward reset. Large destructive product-code changes must also be visible as an explicit merge decision rather than passing as an ordinary refactor.

## Required GitHub rules

Run the repository ruleset reconciler with repository Administration write permission:

```powershell
pwsh ./tools/release/configure-github-branch-rules.ps1 -WhatIf
pwsh ./tools/release/configure-github-branch-rules.ps1
```

The intended repository rules are:

- `main`: pull request required, `CI Gate` and `Feature change guard` required, squash only, ref deletion and non-fast-forward updates blocked;
- `release-*`: same integration protection;
- `rbj--*`: ref deletion and non-fast-forward updates blocked while ordinary fast-forward development pushes remain allowed.

The script is configuration-as-code only until the GitHub ruleset state is actually applied and verified. Do not claim the external protection is active from a green repository CI run alone.

## High-risk product changes

`Feature change guard` compares the PR base and head. It focuses on product source under frontend feature/shared-component areas and backend endpoint/application/domain/infrastructure areas. Documentation-only cleanup does not count as product feature removal.

A PR is classified high risk when it crosses one of the maintained thresholds, including multiple deleted product-code files or a strongly deletion-dominant product-code diff. The workflow prints the affected files and counts.

A high-risk change is blocked unless both are true:

1. the PR has the `intentional-feature-removal` label;
2. another reviewer has submitted `APPROVED` on the current head SHA.

An approval of an older head is not sufficient after further pushes. Commit messages are never an approval mechanism.

## Feature ref cleanup

The `rbj--*` ruleset intentionally means the generic branch-cleanup script cannot remove protected feature refs. Cleanup of merged/abandoned feature refs is therefore an explicit repository-admin operation performed only after verifying the PR is no longer active and the retained commit is recoverable through merged history or another durable ref.

Do not weaken or disable the feature ruleset as part of routine cleanup.

## Incident evidence

On 2026-08-13 active draft PR #547 / WOR-335 lost its remote `rbj--335-image-uploads` ref and closed without merge. GitHub still retained head commit `e774255a5416a9d63d03523646bf3dc54eda518c`, allowing the exact branch to be recreated and the PR reopened without reconstructing code. WOR-436 owns the preventative controls introduced after that incident.
