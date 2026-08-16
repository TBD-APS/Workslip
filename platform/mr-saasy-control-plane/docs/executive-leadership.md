# AI executive leadership v0.1

The executive layer is a provider-neutral governance projection over the shared `Provider -> Model -> Role -> Policy -> Run` primitives.

## Authority hierarchy

```text
Founder / Chair (human)
        |
        v
chief_executive
        |
        +-- chief_operating_officer
        +-- chief_technology_officer
        +-- chief_product_officer
        +-- chief_marketing_growth
        +-- chief_finance_commercial
                 |
                 v
       department/orchestration roles
```

Founder/Chair is deliberately **not** an `AgentRole`. The human authority owns vision, capital/risk tolerance and all high-impact or irreversible approvals.

Executive roles remain model-independent. `config/agent-routing.php` assigns provider/model aliases and fallbacks; concrete model IDs remain environment/configuration.

## Delegation

`ExecutiveHierarchy` declares direct-report relationships. The CEO delegates to functional executives rather than bypassing them to individual implementation workers. Functional executives can delegate only to their declared department roles.

This is an authorization/ownership signal, not permission to bypass the Context/Policy Gateway, Gate 0 or repository delivery rules.

## Decision classes

Executive agents may produce recommendations for reversible work such as:

- backlog prioritization;
- experiment design;
- resource-allocation proposals;
- campaign hypotheses;
- technical sequencing;
- reversible operational changes within an existing policy boundary.

The following classes are always normalized as `requires_founder_approval`:

- pricing changes;
- contract terms;
- legal commitments;
- material spend;
- employment commitments;
- material public statements;
- equity/ownership;
- destructive production actions;
- irreversible commercial commitments;
- governance-policy changes.

V0.1 does not grant autonomous executive write authority. Routing bindings for executive roles set `execute_write=false` and `approve=false`.

## Self-escalation prohibition

No executive agent may modify its own:

- permissions;
- budget limit;
- governance policy.

`ExecutiveSelfAuthorityPolicy` encodes this as a fail-closed invariant. A change to those control surfaces requires a higher authority outside the agent being governed.

## Provenance

Material recommendations retain:

- executive role;
- run ID / agent ID;
- provider + concrete model;
- timestamps;
- decision class;
- affected references;
- evidence references.

This is the data shape the Executive Command Center can later project. Raw provider transcripts are not required for the executive read model.

## Scope boundary

This layer does not:

- execute financial transactions;
- sign contracts;
- hire/fire;
- merge/deploy production changes;
- change pricing;
- publish material public statements;
- change its own governance;
- grant providers new product-data access.

Those remain separate human-gated or future explicitly governed capabilities.
