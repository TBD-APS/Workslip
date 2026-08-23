# Agent role routing v0.1

Role identity, provider identity and concrete model identity are separate.

The application core knows only the provider-neutral routing vocabulary under `app/AI/Application/Routing`. Provider/model preferences live in `config/agent-routing.php`; concrete model IDs come from environment/configuration.

## Assign or change a model

To replace the model used for an existing alias, change only the relevant environment value, for example:

```text
KIMI_CODE_MODEL=<current-coding-model-id>
KIMI_FLAGSHIP_MODEL=<current-flagship-model-id>
OPENAI_FRONTIER_MODEL=<configured-model-id>
ANTHROPIC_FRONTIER_MODEL=<configured-model-id>
XAI_GROWTH_MODEL=<configured-model-id>
```

No `AgentRole`, routing class or Control Center domain contract changes are required.

Provider/model identifiers are provenance and must be recorded on each run. Do not put API keys in this config or in run evidence.

## Add a new model target

1. Add a model alias under `models` in `config/agent-routing.php`.
2. Declare provider label, model ID from environment/config, capabilities and tool capabilities.
3. Point an existing role's `primary` or `fallback` alias at it.
4. Run the unit tests and Gate 0 architecture workflow.
5. Do not add provider-specific branches to `RoleRegistry` or `RoutingConfiguration`.

A declared alias with no concrete model configured is unavailable. This is intentional: routing either uses an eligible configured fallback or fails closed.

## Capability and tool validation

A target is eligible only when it supplies every capability and every tool required by the role. Unknown capability/tool strings are rejected during configuration loading; missing requirements never silently degrade.

Examples:

- `implementation_standard` requires coding + repository access;
- `independent_pr_reviewer` requires reasoning/coding + PR read access;
- content/market roles can require web research without teaching core anything about xAI/Grok payloads.

## Documentation Steward

`documentation_steward` is a bounded implementation role for keeping technical
repository documentation aligned with a successful pull request. Its primary
target is Kimi and its fallback is the configured OpenAI frontier target.

The role requires `repository_read`, `pull_request_read` and
`documentation_write`. The last capability is not general repository write
access: a runtime must independently restrict it to the allowed documentation
paths and reject every non-documentation, governance and customer-facing write.
The role cannot approve or merge a pull request.

## Separation of duties

Run provenance records role, agent ID, provider, model, timestamps and evidence references.

The implementation run cannot use the same agent ID or same provider+model as the **sole approving review**. A separate review signal does not replace CI or the configured human merge gate.

## Human-gated actions

The following remain human-approved regardless of model/provider routing:

- public content publishing;
- pricing changes;
- contract changes;
- legal commitments;
- irreversible commercial commitments;
- governance changes.

Routing policy cannot expand its own authorization or remove these gates.
