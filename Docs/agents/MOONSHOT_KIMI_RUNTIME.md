# Moonshot / Kimi cloud runtime

**State:** Active

Moonshot/Kimi is the preferred cloud runtime for the Kimi agent role. It is a provider/runtime, not the role itself. Kimi remains a provider-neutral frontend/implementation role governed by the shared agent handbook and routing policy.

## Provider contract

Current Kimi Open Platform contract:

- API key: `MOONSHOT_API_KEY`
- default base URL: `https://api.moonshot.ai/v1`
- optional override: `MOONSHOT_BASE_URL`
- model discovery: `GET /models`
- chat: `POST /chat/completions`
- request/response format: OpenAI-compatible Chat Completions

Do not commit `MOONSHOT_API_KEY`. It belongs in the operator/server secret environment only.

## Model identity

Do not model the provider as just `Kimi` or `Moonshot`.

Control Center/provider metadata should retain at least:

```text
role = frontend / implementation / configured role
provider = moonshot
runtime = kimi-api
model = exact discovered model id
providerBase = configured provider identity, not secret-bearing URL metadata
observedAt = timestamp
```

Available model ids change over time. Discover them from `/models` and route based on declared capabilities instead of baking a permanent model name into core code.

## Routing policy

For material frontend work, Kimi involvement is mandatory under the shared handbook.

Initial runtime preference:

1. Moonshot/Kimi cloud runtime for material or complex frontend implementation/review where its required capabilities are available.
2. Ollama/Kimi local runtime for privacy-sensitive, repetitive or lower-risk work when an eligible local model satisfies the task capability/context requirements.
3. If neither runtime is available, the frontend task is `BLOCKED` unless the repository owner records a temporary exception to the mandatory-Kimi rule.

Provider selection does not grant additional data/tool permissions. The same sanitized Context/Policy boundary applies to cloud and local runtimes.

The Documentation Steward runs as a bounded role in the existing AI Delivery
State workflow; it does not add a separate Kimi execution runtime. Its runner
receives limited pull-request metadata and diffs, names its source paths in the
result, and can write only the technical Markdown scope defined in
[`DOCUMENTATION_STEWARD.md`](DOCUMENTATION_STEWARD.md).

## Verification

From a PowerShell shell with the key set only in the operator environment:

```powershell
$env:MOONSHOT_API_KEY = '<secret-from-Kimi-Open-Platform>'
.\tools\ai\moonshot-check.ps1
```

This lists current models without sending repository/customer context.

Run a synthetic chat smoke against a model id returned by discovery:

```powershell
.\tools\ai\moonshot-check.ps1 -Model '<current-model-id>' -SmokeChat
```

The smoke sends only synthetic text and must not be used as evidence that product context permissions are valid.

## Failure semantics

Normalize provider failures through the shared provider/control-plane contract when it lands:

- missing key => `BLOCKED` configuration
- HTTP 401 => `BLOCKED` authentication
- HTTP 429 => `WAITING`/provider throttled according to broker retry policy
- unavailable/timeout => `UNKNOWN` or `BLOCKED`, never healthy
- unknown/removed model => capability unavailable; rerun model discovery
- malformed provider response => provider failure, never synthetic success

Do not include provider error bodies in normalized activity if they can contain prompt/context details.

## Security and privacy

- API key is server/operator-side only.
- No key in frontend, repository, PRs, logs or Control Center records.
- No direct Workslip database/network credentials are exposed to the provider.
- No raw private transcript persistence by default.
- Send only task context already approved/sanitized by the platform context boundary.
- Provider/runtime choice must not change authorization or tenant access.

## Shared provider integration

WOR-576 owns the common AI-provider interface. This document and `moonshot-check.ps1` establish the operational contract now; the runtime adapter must plug into WOR-576 rather than creating a Moonshot-specific execution subsystem or provider-specific Control Center UI.
