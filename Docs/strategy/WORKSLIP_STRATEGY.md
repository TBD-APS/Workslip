# Workslip strategy

**Status:** Active  
**Owner:** Workslip leadership  
**Last reviewed:** 2026-08-15  
**Review cadence:** Every 2–3 weeks, using actual customer and delivery data

This is the maintained repository reference for Workslip's current product, market and execution strategy. It gives implementation agents the business context required to make prioritization and scope decisions without turning chat history or a presentation into hidden source of truth.

It does **not** replace technical source of truth. Current code, configuration, tests, schema and accepted ADRs remain authoritative for implemented behaviour. Linear remains authoritative for issue status, priority and ownership.

## North star

**Workslip should be the simplest workflow and documentation layer for field-service businesses — not a new ERP.**

The product should connect the field with the administrative systems customers already use and make jobs, time, documentation, approvals, auditability and job economics easier to operate with less manual coordination.

## Strategic operating model

The next stage is controlled scaling rather than broad feature expansion.

1. **Stabilize** — make browser/mobile evidence, bug burn-down and delivery hygiene credible enough that “done” means done.
2. **Simplify** — reduce the backend and frontend boundaries that make otherwise small changes expensive or risky.
3. **Prove value** — use a narrow ICP and design partners to demonstrate measurable customer value and repeatable adoption.
4. **Scale what repeats** — extract modules, platform contracts, integrations and operational foundations only where repeated product demand justifies them.

This is not a rewrite strategy. Prefer small, complete slices on top of the existing product.

### Current strategic constraint

Do not open a new broad feature front while customer-visible or QA-critical work is still open without clear evidence, ownership or a documented exception. Fewer simultaneous strategic tracks are preferable to more partially completed work.

## Initial ICP

The current go-to-market hypothesis is deliberately narrow:

- Danish installation and service companies;
- roughly **5–50 field employees**;
- electricians / electrical installation and service;
- plumbing, HVAC and ventilation;
- fire, security and other documentation-heavy field trades;
- an owner or operations manager is still close to day-to-day administration.

The core problem is that jobs, hours, documentation, approval and audit information are spread across too many systems, spreadsheets, messages or people's memory.

This ICP is a **strategic hypothesis**, not a verified hard market boundary. Update it when real conversion, adoption and retention evidence supports a change.

### Not a priority yet

Avoid optimizing the roadmap for:

- large enterprise customers with long procurement programs;
- companies primarily looking for a complete ERP, payroll or accounting replacement;
- many verticals at the same time;
- customer-specific solutions that require code forks.

### Positioning

Workslip is the **workflow and documentation layer between the field and the systems the customer already uses**.

## Buyer map

| Role | Strategic role | Primary value message |
|---|---|---|
| Owner / operations manager | Primary buyer | Get control of jobs without adding administration: overview, fewer errors, faster approval and clearer job economics. |
| Admin / KLS responsible | Champion | Documentation and audit material are ready when needed: traceability, audit scope, PDFs and history. |
| Installer / technician | Daily user | Fewer taps, mobile-first execution and fast registration so the user can move to the next job. |
| Auditor | Trust role | Read only the relevant material with clear scope, history and minimal noise. |

## 90-day execution frame

### Days 0–30 — stabilize and close open risk

Outcome: lower WIP, fewer regression paths and a credible definition of done.

- make relevant browser/mobile evidence mandatory for UI delivery;
- burn down critical bugs and leave no P0/P1 production issue without an owner and next action;
- finish customer-visible UI tracks with real browser evidence;
- reconcile Linear and GitHub so merged work, validation gaps and remaining actions agree;
- continue splitting the highest-risk backend boundaries without creating abstraction for its own sake.

### Days 31–60 — simplify structure

Outcome: reduce change-cost where dependency evidence shows the most friction.

- reduce Jobs coupling toward a target of **5 or lower**;
- remove most frontend cross-feature imports and establish explicit feature contracts;
- continue reducing global CSS ownership and oversized feature components where it materially lowers regression risk;
- keep one clear schema-evolution path with tenant integrity and drift detection as defaults.

### Days 61–90 — prove market and prepare repeatable scaling

Outcome: convert technical maturity into repeatable customer evidence.

- work with **2–3 strong design partners** from the same ICP around the same core flow;
- productize onboarding from invitation to first job and first approval within one day;
- measure and document ROI in admin time, approval speed, audit readiness and error reduction;
- scale platform/module work only where the same requirement repeats across customers or products.

## Execution anchors

Linear is the authority for live status. Before acting, inspect the current issue and repository state rather than assuming the status recorded on this review date.

Strategically important execution areas include:

- browser/mobile QA enforcement — `WOR-548`;
- frontend feature contracts — `WOR-546`;
- continued shell/global CSS reduction — `WOR-475`;
- Overview and UI follow-up — `WOR-503` / `WOR-502`;
- Power BI / embedded analytics — `WOR-451` / `WOR-542`;
- critical-flow bug burn-down — `WOR-445`;
- secrets/environment foundation — `WOR-495` and related security work;
- Jobs/domain simplification — continue from the delivered `WOR-545` boundary work based on current dependency evidence rather than reopening completed work by default.

## Ownership model

Use this as the default responsibility split; verify current Linear assignment before delegating issue-specific work.

| Owner | Primary responsibility |
|---|---|
| **Rasmus** | Technical lead and architecture; backend/domain boundaries; QA/release gates; security/secrets foundation; final architecture decisions. |
| **Abdi** | Product and customer delivery; Overview/Farvelab follow-up; reporting/analytics; customer workflow improvements; design-partner feedback loop. |
| **Mathias** | Product polish and adoption; low-risk UX improvements; gamification/delight where tied to adoption; guides/onboarding; pilot feedback execution. |
| **Linear agent** | Repeatable delivery automation; bug triage; issue/PR hygiene; bounded implementation tasks; evidence/status reconciliation. |

Operating rule: **Rasmus owns boundaries and risk. Abdi and Mathias own customer-visible progress. The agent scales repeatable work, not product or architecture decisions.**

## Go-to-market motion

Start founder-led, narrow and measurable rather than marketing broadly.

Monthly steering funnel:

1. identify about **30 target companies** with the same ICP, geography and visible fit;
2. reach **10 qualified problem conversations** around administration, documentation, audit and hours;
3. run about **5 live demos** using the buyer's actual flow: job → field → approval → PDF/overview;
4. start about **3 thirty-day pilots** with 5–10 users and measure time-to-first-value plus weekly usage;
5. aim to convert **2 paying customers** when Workslip demonstrably replaces manual work.

Channel order:

1. existing network and warm introductions;
2. targeted phone plus LinkedIn/email outreach;
3. local trade communities;
4. reference cases once credible evidence exists.

These funnel numbers are steering targets, not validated conversion rates. Adjust them from actual data.

## Strategy metrics

Use a small operating dashboard that connects technical health, product value and market proof.

| Area | 90-day steering target |
|---|---|
| Backend architecture | Jobs coupling ≤ 5 |
| QA | 100% of relevant UI PRs carry required browser/mobile evidence |
| Product reliability | ≤ 3 open P0/P1 critical bugs |
| Pilot adoption | > 70% weekly active field users |
| Onboarding | First job in < 1 day for a new customer |
| Commercial proof | 2+ paying customers from the focused motion |

Run a **30-minute weekly operating review** around metrics, blockers and the next seven days. Avoid status reporting that does not change a decision or next action.

## How agents should use this document

Before cross-functional planning, broad feature prioritization, market-facing work or architecture work with product trade-offs:

1. read this file;
2. inspect current code/ADRs for technical truth;
3. inspect relevant Linear issues for current status and ownership;
4. identify whether the requested work strengthens the current strategy or opens an unplanned parallel track;
5. surface conflicts explicitly instead of silently optimizing for the latest chat request.

Important strategy changes made in chat must be reflected here or in a successor maintained strategy document, with Linear used for execution tracking.

## References

- Primary strategy deck: https://docs.google.com/presentation/d/1CZc9pLNV1MuD5bLrMbduYk6c8JYPDdt8o1YbYh0KUM8
- Central Linear strategy reference: https://linear.app/workslip/document/workslip-status-strategi-og-eksekveringsplan-15082026-c403634ea19b
- Alignment issue: `WOR-559`
- Repository-wide agent rules: [`../../AGENTS.md`](../../AGENTS.md)
- Documentation truth model: [`../README.md`](../README.md)
