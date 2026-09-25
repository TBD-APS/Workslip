# Power BI value analytics

**Status:** Active  
**Owner:** Product / customer delivery  
**Metric definition version:** `2026-09-23.v1`

## Purpose

`Workslip Value Analytics` is the Power BI model for the 90-day value review. It deliberately separates product facts owned by Workslip from commercial and customer-value facts owned by external systems.

The product-data contract is:

`GET /api/superadmin/analytics/value-model?days=365&scope=customer`

The endpoint is Superadmin-only. It runs against one deployed Workslip environment at a time, excludes the reserved platform organization, and uses the same customer/demo scoping as the activation scoreboard.

## Canonical product facts

The endpoint returns the Power BI-shaped collections `dimOrganization`, `dimEmployee`, `factJobs` and `factControlPointResults`. They are built from Workslip organizations, users, jobs, job lifecycle audit events and KLS control-point state.

`Completed & Compliant` uses the canonical activation semantics defined by the product: the first transition to `Approved`. A later reopen or reapproval does not move the first value event. Legacy currently-approved jobs without historic audit transitions use the documented legacy timestamp fallback from the activation metric semantics.

The API never manufactures commercial or survey facts. Fields for evidence/photos that do not yet have an authoritative analytics source remain null rather than being inferred.

## KPI source matrix

| KPI | Definition / input | Authoritative source | Current availability |
|---|---|---|---|
| Completed & Compliant Jobs | Jobs with a first canonical `Approved` value event | Workslip job lifecycle + audit history | Available |
| Compliance Completion Rate | Completed & Compliant jobs relative to submitted/completed population used by the report | Workslip value-model | Available |
| Missing Documentation | Non-irrelevant KLS categories with no checked control point | Workslip installation/category/control-point state | Available |
| Rework Rate | Jobs with `Rejected`/`Reopened` lifecycle correction events relative to submitted jobs | Workslip audit history | Available |
| Admin Hours Saved | Before/after admin-hours delta | Monthly design-partner value survey | Requires measured baseline/after data |
| Audit Prep Hours Saved | Before/after audit-preparation-hours delta | Monthly design-partner value survey | Requires measured baseline/after data |
| Estimated Customer Value DKK | Measured hours saved × customer-specific estimated hourly cost | Monthly design-partner value survey | Requires measured baseline/after data |
| Value / MRR Multiple | Estimated customer value ÷ MRR | Value survey + billing | Requires both external sources |
| MRR | Recurring revenue for active subscriptions | Billing/subscription source | External source required |
| Paying Companies | Distinct organizations with active paid subscription | Billing/subscription source | External source required |
| Premium Attach Rate | Paying organizations with premium module ÷ paying organizations | Billing/subscription source | External source required |
| Demo → Paid | Won opportunities ÷ held demos | CRM / sales pipeline | External source required |
| Direct/Cold vs Partner/TEKNIQ | Acquisition channel on opportunity/account | CRM / sales pipeline | External source required |
| NPS / PMF signal | Monthly customer-value survey responses | Design-partner survey | Requires measured survey data |
| Activation / retention | First value event, repeat-value weeks and weekly activity | Workslip activation scoreboard | Available |

## Power BI model contract

The semantic model keeps these dimensions and facts stable so data sources can change without redesigning KPI measures:

- Dimensions: `DimDate`, `DimOrganization`, `DimEmployee`, `DimLeadSource`.
- Product facts: `FactJobs`, `FactControlPointResults`.
- External facts: `FactSubscriptions`, `FactSalesPipeline`, `FactCustomerValueSurvey`.

The API-backed product partitions use a shared Power Query source:

```powerquery
Json.Document(
    Web.Contents(
        ApiBaseUrl,
        [
            RelativePath = "api/superadmin/analytics/value-model",
            Query = [days = Text.From(HistoryDays), scope = AnalyticsScope],
            Headers = [Accept = "application/json"]
        ]
    )
)
```

The Power BI project parameters are `ApiBaseUrl`, `HistoryDays` and `AnalyticsScope`. Credentials, bearer tokens, SQL credentials and publish-to-web tokens must not be checked into the PBIP project.

## External-fact boundary

`FactSubscriptions`, `FactSalesPipeline` and `FactCustomerValueSurvey` must stay empty until their authoritative sources are connected. Synthetic MRR, NPS, pipeline, time-saved or before/after values are not acceptable substitutes because they would make test traction indistinguishable from real evidence.

The model is therefore structurally ready for MRR, paid-company, attach-rate, demo-to-paid, acquisition-channel and customer-value measures, while those measures remain data-dependent until the real billing, CRM and survey rows exist.

## Baseline and customer-value evidence

A design-partner baseline must be captured before or at the start of the measurement period and must include, at minimum, the organization, measurement date, admin hours, audit-preparation hours and the estimated hourly cost used for value calculations. Follow-up measurements must use the same unit and customer context.

The 90-day review requirement of three customers with measured before/after value is an evidence requirement, not a software default. It is only satisfied when three real customer measurements exist in the customer-value source. The reporting layer must not auto-fill or infer them.

## Report surfaces

The API-ready PBIP uses five report pages:

- Executive Value
- Customer Value
- Product Adoption
- Quality & Compliance
- Growth

Organization relationships allow aggregate and customer-specific views without separate KPI models. `DimLeadSource` separates Direct/Cold and partner acquisition; conversion/MRR/retention comparisons become populated when CRM and billing facts are connected.

## Validation boundary

Repository CI validates the Workslip API, authorization and documentation contract. Static PBIP validation verifies project JSON, report/semantic-model path binding, pages, visuals, relationships, DAX measure references, API partitions and absence of synthetic commercial/customer-value facts.

Opening the project in Power BI Desktop and performing a live refresh requires Power BI Desktop plus a deployed Workslip API and valid Superadmin authentication; that runtime step cannot be proven by the Linux CI runner.
