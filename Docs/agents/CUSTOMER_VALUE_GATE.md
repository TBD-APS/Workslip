# Customer Value Gate

**Status:** Active  
**Owner:** Workslip product and engineering leads  
**Source of truth:** customer evidence, current product behaviour, Linear scope/status, repository implementation and validated business metrics  
**Review cadence:** Quarterly, and whenever pricing, ICP or product strategy materially changes

This gate is the default product-triage method for new Workslip feature requests, product improvements and material scope changes. It exists to maximize **customer value and durable profit per unit of engineering effort** without trading away security, tenant isolation, data integrity, maintainability or product coherence.

The gate is not a feature-voting system and not a substitute for judgment. A score is a decision aid. Verified security, privacy, data-integrity and correctness obligations can override commercial priority.

## Core rule

Always separate these four things before discussing implementation:

1. **Customer problem** — what costly, risky, slow or frustrating situation exists today?
2. **Requested solution** — what did the customer or stakeholder ask us to build?
3. **Smallest valuable solution** — what is the smallest change that removes most of the pain?
4. **Evidence** — what proves the problem is real, frequent and worth solving now?

A customer can be exactly right about the pain and wrong about the implementation. Preserve the problem; challenge the proposed solution.

## When to apply

Apply this gate before implementation when any of these are true:

- a new Linear issue proposes a customer-facing feature or workflow change;
- an existing issue grows materially in scope;
- a customer asks for a specific implementation rather than describing only a problem;
- a roadmap item consumes meaningful engineering capacity;
- a technically attractive idea lacks direct customer evidence;
- multiple possible solutions could address the same pain.

Do not delay urgent security, tenant-isolation, data-loss, compliance or production-correctness fixes to complete commercial scoring. For those issues, the gate may still help size the safest smallest correction, but the safety obligation comes first.

## Step 1 — reconstruct the customer reality

Write the problem without naming the requested feature.

Good:

> Admin spends 60–90 minutes every Friday chasing missing timesheets across several employees, and delayed hours postpone invoice preparation.

Weak:

> Customer wants reminders.

Identify:

- **Actor:** who experiences the problem?
- **Payer:** who ultimately pays for the problem?
- **Trigger:** when does it happen?
- **Frequency:** how often?
- **Current workaround:** what do they do today?
- **Consequence:** what happens if nothing changes?
- **Economic path:** does the pain consume time, salary, capacity, revenue, cashflow, error budget or management attention?

Treat workarounds as evidence. Repeated use of Excel, paper, WhatsApp, phone calls, duplicate entry, manual checking or a dedicated admin routine often indicates stronger latent demand than a feature request with no observed behaviour behind it.

## Step 2 — classify demand

Choose one primary demand type:

| Demand | Meaning | Default response |
|---|---|---|
| **Explicit demand** | Customer clearly asks for an outcome or capability | Evaluate value and solution fit |
| **Observed pain** | Customer describes a recurring problem but not the solution | Strong discovery signal |
| **Latent demand** | Customer has normalized a costly workaround and may not ask for software | Look for a product insight / fast experiment |
| **Speculative demand** | Internal idea with little behavioural evidence | Validate before substantial build |

Do not assume that lack of a feature request means lack of demand. Customers often adapt to bad workflows and stop describing them as problems.

## Step 3 — quantify the pain before the feature

Use ranges when exact numbers are unavailable. Prefer directional economics over fake precision.

Useful approximations:

```text
Monthly time cost ≈ occurrences/month × minutes/occurrence × loaded hourly cost / 60

Monthly error/risk cost ≈ incidents/month × expected cost/incident

Cashflow drag ≈ value waiting for approval/invoice × delay days × business sensitivity

Capacity value ≈ hours released × value of the work those hours can replace
```

Also ask:

- Does this move completed field work closer to an approved invoice basis?
- Does this allow the same office team to support more field employees?
- Does this reduce avoidable rework or customer callbacks?
- Would a buyer understand the value during a demo in under five minutes?
- Could this support pricing power, retention or expansion?

When possible, capture the baseline before implementation so value can later be measured rather than asserted.

## Step 4 — value model

Score each dimension from **0–5** using evidence, not optimism.

### Benefit dimensions

| Dimension | 0 | 3 | 5 |
|---|---|---|---|
| **Revenue / willingness to pay** | No credible economic effect | Helps justify product value | Directly creates/protects meaningful revenue or premium willingness to pay |
| **Cashflow** | No effect | Removes some approval/invoice delay | Materially accelerates finished work → invoice/payment |
| **Time saved** | Negligible | Repeated minutes saved | Repeated hours of avoidable work removed |
| **Labor / capacity leverage** | No resource effect | Some admin capacity recovered | Same team can support materially more work/customers |
| **Risk / error reduction** | Cosmetic only | Prevents recurring mistakes/rework | Prevents costly loss, compliance/documentation failure or customer harm |
| **Frequency** | Rare | Weekly/monthly | Daily / every job / every employee |
| **Reach** | One edge-case customer | Meaningful segment | Core ICP / most customers |
| **Workaround strength** | No current workaround | Informal workaround | Paid/manual recurring workaround with clear friction |
| **Strategic compounding** | Isolated feature | Strengthens an existing workflow | Deepens Workslip's core data/workflow moat or unlocks multiple future capabilities |
| **Retention / habit** | No recurring use | Useful recurring action | Makes Workslip part of a critical daily/weekly operating loop |

### Cost / friction dimensions

Score from **1–5**:

| Dimension | 1 | 3 | 5 |
|---|---|---|---|
| **Implementation effort** | Tiny isolated change | Multi-layer feature | Large subsystem / migration / major integration |
| **Maintenance burden** | Reuses existing model | Some ongoing special cases | New permanent subsystem/integration burden |
| **Adoption friction** | Natural extension of current behaviour | Requires learning/process adjustment | Requires major customer behaviour/process change |
| **Architecture / operational risk** | Existing patterns, low blast radius | Moderate new state or coupling | High concurrency/data/security/integration risk |

### Evidence confidence

Use one confidence level:

- **0.25 — weak:** internal intuition, hypothetical request, no observed behaviour.
- **0.50 — plausible:** one credible customer/problem report or indirect product evidence.
- **0.75 — strong:** repeated customer evidence, clear workaround, usage/support data or verified operational pattern.
- **1.00 — very strong:** repeated measured behaviour, multiple customers/segments, actual willingness-to-pay or production data.

### Priority signal

A rough signal may be calculated as:

```text
Benefit = weighted sum of benefit dimensions
Cost = implementation effort + maintenance burden + adoption friction + architecture/operational risk
Priority signal = Benefit × confidence / Cost
```

Use additional weight on **revenue/willingness to pay** and **cashflow** when the current strategic goal is monetization, and on **risk/error reduction** when production safety is the dominant constraint.

The number never makes the decision by itself. It exists to expose assumptions and make comparisons less arbitrary.

## Step 5 — identify the value mechanism

Every recommended feature should have at least one explicit value mechanism:

- **Make money:** supports revenue, premium pricing, expansion or conversion.
- **Get paid faster:** reduces delay from work completion to invoice/payment.
- **Save paid time:** removes manual administration or duplicate work.
- **Use fewer resources:** allows the same people to handle more jobs/customers.
- **Avoid costly mistakes:** prevents rework, missed documentation, disputes or unsafe states.
- **Reduce management attention:** removes recurring chasing, checking and coordination.
- **Increase adoption/retention:** creates a repeated operational habit that is painful to lose.

If the only value mechanism is “looks nicer” or “would be cool,” classify it as a vitamin unless there is evidence that visual trust, usability or delight materially affects conversion, task completion or retention.

## Step 6 — challenge the requested solution

Before accepting implementation scope, generate at least two alternatives when the requested solution is non-trivial:

1. the requested solution;
2. the smallest existing-pattern solution;
3. optionally, a no-build/process/integration solution.

Prefer the option that captures most of the value with the least irreversible complexity.

Ask in order:

1. Can existing Workslip data already answer this?
2. Can an existing UI component or workflow be extended?
3. Can a direct action solve the pain without a new subsystem?
4. Can the feature be default-off or scoped to the affected role?
5. Can we test the value before building generalized infrastructure?
6. Is the requested abstraction needed by multiple proven use cases now?

Avoid generic workflow engines, plugin systems, template engines, event platforms or new dependencies merely because they could support future requests. Generalize only when repeated evidence makes the common structure real.

## Step 7 — smallest valuable slice

A smallest valuable slice must:

- solve a real end-to-end customer outcome;
- be safe enough for the relevant data/authorization boundary;
- avoid half-built platform abstractions;
- produce measurable behaviour;
- be reversible or easy to extend when evidence is still developing.

It is not simply “the smallest amount of code.” It is the smallest complete change a customer can actually value.

Examples:

- reminder feature → start with one high-value missing-hours reminder and direct action, not a generic rule builder;
- job costing → approved hours × preserved billable rate → admin-visible PDF basis, not full accounting;
- inventory → validate job-material capture/reconciliation before warehouse optimization and forecasting;
- route planning → generate a multi-stop map route from existing job addresses before building a routing engine.

## Step 8 — success metric before build

Pick one primary observable result. Examples:

- minutes of admin work per weekly timesheet cycle;
- median finished-job → approved-job time;
- median approved-job → invoice-basis time;
- percentage of jobs approved without office rework;
- percentage of recurring jobs created via copy action;
- number of manual follow-up calls/messages avoided;
- demo → activated-company conversion;
- 30-day feature reuse among eligible users;
- percentage of customers that would object if the feature disappeared.

Avoid vanity metrics such as clicks when the business outcome is time, money or completed work.

## Step 9 — recommendation

Choose exactly one:

### DO NOW

Use when the problem is proven enough, the value is high relative to cost, and the smallest valuable slice is clear.

Required output:

- value mechanism;
- evidence/confidence;
- smallest valuable slice;
- success metric;
- key technical guardrails.

### VALIDATE FIRST

Use when potential value is high but evidence or solution fit is weak.

Prefer low-cost validation such as:

- observe the current workflow;
- review support/history data;
- interview 3–5 relevant customers about the last time the problem occurred;
- prototype the interaction;
- manually deliver the outcome before automating it;
- expose a narrow version to design partners;
- test willingness-to-pay or package positioning.

Do not ask only “would you use this?” Ask what happened last time, how they solved it, how long it took, what failed and what it cost.

### DEFER

Use when the problem is real but current ROI, timing, reach or dependency order is poor. State what evidence or milestone would cause reconsideration.

### REJECT / REFRAME

Use when the proposed solution is overbuilt, duplicates existing capability, optimizes an edge case at high cost or has no credible customer-value mechanism. Preserve the valid problem and propose a better framing when possible.

## Mandatory safety gate

No commercial score can override these boundaries:

- tenant isolation and authorization;
- personal-data minimization and approved processing boundaries;
- transaction/data-integrity requirements;
- security and secret handling;
- safe database/migration behaviour;
- idempotency/concurrency where side effects matter;
- production release and validation gates;
- applicable compliance review.

If a high-value idea requires weakening one of these, the recommendation is **REJECT / REFRAME** until a safe design exists.

## Linear comment format

For product triage, leave a compact comment in this structure when the decision is material:

```markdown
## Customer Value Gate — DO NOW | VALIDATE FIRST | DEFER | REJECT / REFRAME

**Customer problem:** <problem without feature wording>
**Demand:** Explicit | Observed pain | Latent | Speculative
**Who pays today:** <time / salary / errors / cashflow / revenue / management attention>
**Evidence / confidence:** <evidence>, confidence <0.25 / 0.50 / 0.75 / 1.00>

**Value drivers:** <2–4 strongest drivers>
**Main cost/risk:** <effort, adoption, maintenance, architecture>
**Smallest valuable slice:** <end-to-end slice>
**Success metric:** <one primary observable result>

**Decision rationale:** <why this recommendation now>
**Guardrails:** <security/tenant/data/architecture boundaries>
```

Do not spam Linear with the full scoring worksheet when a short rationale is sufficient. Keep detailed discovery evidence in the issue description, customer need or linked document when it is durable and useful.

## Fast triage mode

For a small incoming request, answer these seven questions first:

1. What problem is the customer actually paying for today?
2. How often does it happen?
3. What workaround proves the pain exists?
4. Does this affect money, paid time, risk or cashflow?
5. How much of the ICP likely shares it?
6. What is the smallest end-to-end fix using existing Workslip patterns?
7. What would we measure to know it worked?

If answers 1–4 are weak, default to **VALIDATE FIRST** or **DEFER**, not immediate implementation.

## Deep-dive mode for strategic bets

For large roadmap items or new product areas, add:

- customer segment/persona split;
- alternative solutions and competitors/workarounds;
- likely pricing/packaging impact;
- onboarding/support burden;
- operational and data model implications;
- dependency graph and opportunity cost;
- expansion path if the first slice succeeds;
- explicit kill criteria if evidence does not materialize.

Large projects should earn the right to generalize through successful smaller slices.

## Workslip calibration examples

### Job costing / billable hourly rate

**Recommendation:** DO NOW when customer workflow is already hours → approval → invoice basis.

Why: direct money/cashflow path, existing data, strong demo value and limited initial scope. Start with admin-controlled billable rate, preserved historical rate and reproducible PDF total. Do not expand directly into payroll, VAT, invoicing and bookkeeping.

### Actionable reminders / Inbox

**Recommendation:** DO NOW or narrow VALIDATE FIRST depending on evidence for the specific reminder.

Why: repeated management chasing is paid time and a strong latent-demand workaround. Start with the highest-frequency painful event, direct action and grouping. Do not begin with a generic customer-authored automation engine.

### Copy job

**Recommendation:** DO NOW when recurring jobs are common.

Why: low effort, high frequency and obvious admin-time reduction. Copy relevant source data only; never copy status, approvals, timer state or historical execution data.

### Route planning

**Recommendation:** typically DO NOW as a small integration when many field employees have several jobs per day.

Why: recurring daily convenience and travel-time value with low implementation effort if built on existing addresses/map links. Avoid building proprietary route optimization before usage proves it.

### Inventory / stock management

**Recommendation:** VALIDATE FIRST and slice aggressively.

Why: potentially high retention and operational value, but large data-model, transaction and process complexity. First prove which material/stock problem customers actually pay to remove.

### Cosmetic UI polish

**Recommendation:** usually DEFER unless evidence shows usability, trust, task-completion or conversion impact.

A visual change becomes economically meaningful when it removes errors, reduces training/support, improves completion or materially increases sales conversion.

## Anti-patterns

Reject these reasoning shortcuts:

- “A customer asked for it, therefore we should build it.”
- “Competitors have it, therefore we need it.”
- “It is easy to code, therefore it is valuable.”
- “Nobody asked for it, therefore there is no demand.”
- “We can make it generic now for future flexibility.”
- “The score is high, therefore security/architecture constraints do not matter.”
- “Five customers said yes to a hypothetical question, therefore willingness-to-pay is proven.”
- “Usage proves value” when the feature is mandatory or has no alternative.

## Default product stance

Workslip should preferentially build features that move customers through this chain with less friction:

```text
assigned work
→ field execution
→ time/material/documentation
→ submission
→ approval
→ invoice basis / business follow-up
```

Features outside that chain can still be valuable, but they need stronger evidence or a clear strategic reason.

The objective is not to build fewer features. It is to **learn faster, ship smaller complete value, and spend engineering capacity where customers feel the economic difference**.
