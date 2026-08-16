# MR SAAS'y durable agent loop POC

Tracking: WOR-617

This directory is deliberately a **throwaway-capable proof of concept**. It exists to prove or falsify one architectural assumption before the full Change Intelligence Engine is built:

> Can a project-agnostic agent run survive worker loss, retry failed provider/tool steps safely, consume structured quality-gate feedback, pause for human approval and resume without duplicating side effects?

It is not a production service and it does not depend on Workslip domain code, Laravel, GitHub, a real LLM provider, OPA or customer data.

## POC shape

```text
Temporal dev service
        |
        v
containerized Python worker
        |
        +--> fake provider activity (first call transiently fails)
        +--> idempotent tool activity (writes one durable side effect)
        +--> deterministic quality gate
        |
        v
structured feedback -> corrected second attempt
        |
        v
WAITING_APPROVAL -> signal -> COMPLETED
```

For this first POC the Temporal dev container remains alive while only the worker container is destroyed and recreated. That isolates the question we actually need to answer first: does durable workflow history let a **replaceable worker** recover correctly? Temporal-service failover/persistence is deliberately a later proof and should be tested against Temporal Cloud rather than inferred from a local SQLite mount.

The synthetic external tool state is stored in a Docker volume so it survives worker replacement and can prove idempotency across retries.

## What the destructive test proves

`scripts/destructive-test.sh` intentionally kills the worker **after the tool has committed its side effect but before the activity can acknowledge completion to Temporal**.

After restart, Temporal retries the unfinished activity. The tool receives the same idempotency key and must not apply the side effect a second time.

The test then proves:

1. the workflow survives worker-container loss;
2. the first tool side effect is applied exactly once even though the activity is invoked again;
3. a simulated provider timeout is retried by Temporal in isolation;
4. the first deterministic quality gate fails with machine-readable feedback;
5. the second attempt consumes that feedback and passes;
6. the run pauses in `WAITING_APPROVAL` without active work;
7. a later approval signal resumes the same logical run;
8. the final overview retains both attempts and the gate feedback.

## Run locally

Prerequisite: Docker with Compose v2.

```bash
cd platform/mr-saasy-agent-poc
./scripts/destructive-test.sh
```

Temporal UI is exposed at `http://localhost:8233` while the test is running.

## Boundaries

The neutral models are in `app/contracts.py`. Temporal-specific orchestration stays in `app/workflow.py`, `app/worker.py` and `app/connection.py`.

The POC intentionally keeps the integration path tiny. Laravel remains outside the execution kernel and can later expose project integrations/tools without owning durable workflow state.

## Exit criteria

Do not promote this directory to a permanent platform service just because the happy path works. WOR-617 only succeeds when the destructive container-restart test is green and the resulting Temporal event history is understandable.

If the POC succeeds, use its evidence to decide the ADR for WOR-616. If it fails or the operational model feels brittle, replace the runtime before building more platform surface.
