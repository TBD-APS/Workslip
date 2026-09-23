# Activation KPI scoreboard

**Status:** Active  
**Owner:** Product / customer delivery  
**Linear:** WOR-651  
**Metric definition version:** `2026-09-23.v1`

## Purpose

The activation scoreboard is the canonical weekly product-value read model for Workslip. It combines authoritative Workslip domain facts with product telemetry only when telemetry exists. Telemetry is diagnostic input; it is never the source of truth for activation, authorization, job completion or compliance.

The Superadmin API surface is:

`GET /api/superadmin/analytics/activation-scoreboard?days=90&scope=customer`

The endpoint runs against one deployed Workslip environment at a time and never accepts an environment identifier from the caller.

## Activation definition

An **Activated Company** is an organization that has produced at least one **Completed & Compliant Job**.

For the current Workslip lifecycle, a job becomes compliant enough to enter review only after `JobValidationService.ValidateSubmitReady` succeeds. A reviewer can then transition the job from `InReview` to `Approved`. Therefore the canonical Completed & Compliant value event is the first transition to `Approved`.

The timestamp is resolved from the first `Approved` audit transition. For legacy jobs that are currently `Approved` but predate audit history, `JobReportRow.UpdatedAt` is used only as a timestamp fallback. A later reopen/re-approval never moves the original activation timestamp.

**Time to First Completed & Compliant Job** = first Completed & Compliant timestamp − organization creation timestamp.

Signup, login, onboarding clicks and demo completion do not activate a company.

## Versioned metric definitions

| Metric | Precise definition | Authoritative source | Environment/scope |
|---|---|---|---|
| Activated Companies | Organizations with at least one first Approved value event | Organizations + JobReports + JobEvents | Current environment; selected customer/demo scope |
| Weekly Active Companies | Organizations with a job lifecycle event, new job or worksheet activity in the trailing 7 days | JobEvents + JobReports + Worksheets | Current environment; selected scope |
| Weekly Active Users | Distinct actors with a lifecycle event or worksheet activity in the trailing 7 days | JobEvents + Worksheets | Current environment; selected scope |
| Completed & Compliant Jobs | Jobs whose first Approved value event falls inside the selected window | JobEvents; legacy current-Approved fallback | Current environment; selected scope |
| Compliance Completion Rate | Jobs submitted in the selected window that have reached a first Approved value event ÷ jobs submitted in that window | JobReports + JobEvents | Current environment; selected scope |
| Time to First Completed & Compliant Job | Organization creation → first Approved value event; median and p75 are reported | Organizations + JobReports + JobEvents | Current environment; selected scope |
| Repeat-value rate | Activated organizations with first-value events in at least two distinct ISO weeks ÷ activated organizations | JobEvents; legacy current-Approved fallback | Current environment; selected scope |
| Funnel conversion | Company counts for organization → first customer → first job → first Completed & Compliant Job | Organizations + Customers + JobReports + JobEvents | Current environment; selected scope |
| Funnel stage time | Median and p75 duration for organization→customer, customer→job and job→first Completed & Compliant Job | Same canonical domain sources | Current environment; selected scope |

## Telemetry-owned stages

`demo_started`, `demo_value_flow_completed`, `first_login_completed`, discovery/friction and drop-off explanations belong to WOR-736's canonical product telemetry capability. The scoreboard reports those stages as unavailable until WOR-736 supplies them; it does not create a second event model.

## Customer versus demo scope

The reserved Workslip platform organization is always excluded. Demo organizations are configured through `Analytics:DemoOrganizationIds`.

- `scope=customer` excludes configured demo organization IDs.
- `scope=demo` includes only configured demo organization IDs and returns a conflict response when none are configured.
- There is intentionally no mixed `all` scope.

This keeps customer and demo results separated without introducing a second organization classification model.

## Weekly review

Use the same metric definition version when comparing weeks. If a semantic definition changes, update `ActivationMetricSemantics.DefinitionVersion` and this document in the same change. Historical numbers must not be silently reinterpreted.

Commercial metrics such as demos, won/lost opportunities, MRR, demo-to-paid conversion and lead source remain owned by their commercial source systems. Power BI may combine those sources with this scoreboard, but Workslip telemetry must not become a CRM.
