# Ollama local runtime

**State:** Draft  
**Tracking:** WOR-593  
**Role:** Local AI provider/runtime for MR SAAS'y agents

Ollama is treated as a **runtime/provider host**, not as one agent identity. Every model used through Ollama must be registered by concrete model/tag and, when available, digest/version metadata. Routing authority comes from the shared agent/provider policy; running locally does not grant security, release or production authority.

## Security boundary

Default local endpoint:

```text
http://127.0.0.1:11434
```

Keep the runtime loopback-only unless a separate reviewed change explicitly introduces a network boundary. Do not expose Ollama publicly as part of local development.

Provider choice must not expand data permissions. Ollama receives only context already approved/sanitized by the agent Context/Policy boundary. Do not provide Workslip database credentials, generic database/network tools, production secrets or raw private transcripts by default.

## Install/start Ollama

Install Ollama using the supported installer for the developer machine, then ensure the local service is running. The repository deliberately does not silently install system software or download arbitrary models.

After installation, verify the runtime from repository root:

```powershell
.\tools\ai\ollama-check.ps1
```

The check calls the supported Ollama local API and reports runtime version plus installed models. The official API exposes runtime version and local model discovery under `/api/version` and `/api/tags`; chat requests use `/api/chat`.

To use another approved local endpoint for the current shell:

```powershell
$env:OLLAMA_BASE_URL = 'http://127.0.0.1:11434'
.\tools\ai\ollama-check.ps1
```

## Model selection

Do not route against an ambiguous name such as `Ollama`. Select a concrete installed model:

```powershell
.\tools\ai\ollama-check.ps1 -Model '<model:tag>'
```

Run a synthetic deterministic smoke request only after choosing the exact model:

```powershell
.\tools\ai\ollama-check.ps1 -Model '<model:tag>' -SmokeChat
```

The smoke request asks the model for a trivial safe response and does not read repository/customer/product data.

## Initial workload policy

Good initial local workloads:

- classification/routing assistance;
- checkpoint and evidence summarization from already-approved context;
- repetitive text/code transformations with deterministic validation;
- low-risk repository analysis where the relevant files are explicitly supplied;
- local experimentation/benchmarking before a model receives a formal agent role.

Not default authority:

- production release/merge approval;
- security sign-off;
- tenant-isolation decisions;
- destructive operations;
- unrestricted repository or product-data access.

## Provider registration target

WOR-593 will connect this runtime behind the shared provider contract once that contract is available from WOR-576/WOR-563. A registration should carry at least:

```text
providerRuntime = ollama
runtimeVersion
baseUrlClass = loopback-local
model
modelDigest
agentRole
capabilities[]
dataClassification
benchmark/evidence level
loaded handbook/source revision
```

Control Center must distinguish runtime availability from model capability. An available Ollama service with no suitable/registered model is not evidence that a requested agent role is available.

## Failure semantics

- Runtime unreachable: `BLOCKED`/`UNKNOWN`, never healthy.
- Model missing: capability unavailable; do not silently substitute another model.
- Timeout/cancelled request: normalized provider failure with no prompt/context leakage.
- Malformed response: fail closed and preserve provider evidence only.

## Current implementation boundary

The repository currently includes the local runtime check and operating contract. The provider adapter itself remains pending the shared AI provider contract tracked by WOR-576; do not create a second Ollama-specific execution abstraction in Workslip while that contract is being established.
