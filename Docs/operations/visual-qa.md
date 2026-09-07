# Visual QA

**Status:** Pilot  
**Owner:** Workslip maintainers

Visual QA adds deterministic screenshot evidence to the existing Playwright browser gate. It does not replace DOM assertions or product tests; it answers the narrower question: does an element that the DOM says is visible actually contribute visible pixels inside its expected bounds?

## Current proof of concept

The proof of concept was built against `#help-wizard` on desktop (1280px) and
mobile (390px). That element has since been removed with the help-wizard
feature, so the pilot needs a new subject element before it can be run again.
The technique is unchanged and is described here against the original subject:

1. Playwright records the element bounding box and viewport.
2. It captures a normal screenshot.
3. It captures a control screenshot with only the expected element forced to `opacity: 0`.
4. `tools/visual-qa` compares the same region with OpenCV/OpenCvSharp.
5. The check blocks when the element is clipped outside the viewport or hiding it produces no meaningful pixel delta.
6. An intentional negative fixture feeds identical screenshots to the analyzer and must be rejected, proving DOM presence alone cannot pass the gate.

Evidence is written under `${RUNNER_TEMP}/workslip-visual-qa` (or `WORKSLIP_VISUAL_QA_EVIDENCE_DIR`) as visible/control screenshots, metadata and analyzer JSON.

## Severity policy

The pilot only blocks high-confidence deterministic failures: viewport clipping and effectively zero visual contribution in a known element region. Reference-image drift, aesthetic differences and semantic visual judgements are not blocking signals in this phase; add them as warnings first and calibrate them before promotion to release gates.

## Runtime

The analyzer is a small .NET 8 console tool using OpenCvSharp. It can run directly from the existing Playwright CI job or from `tools/visual-qa/Dockerfile`. MCP is intentionally not a runtime dependency; an MCP adapter may expose this analyzer later after the detection contract is stable.
