# Browser evidence extension to validation rules

This file is an implementation pointer for `Docs/agents/VALIDATION.md`, not a competing validation policy.

The active machine-readable PR handoff contract for browser/mobile evidence is documented in [`browser-evidence-pr-contract.md`](browser-evidence-pr-contract.md) and enforced by `tools/release/validate-pr-browser-evidence.mjs` through `.github/workflows/feature-change-guard.yml`.

The governing policy remains `Docs/agents/VALIDATION.md`: use Playwright only for critical changed browser risks, use synthetic/non-production data, and keep a PR blocked/draft when required Playwright evidence is unavailable unless an explicit exception is approved.
