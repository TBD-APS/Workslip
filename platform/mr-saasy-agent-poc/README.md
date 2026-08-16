# MR SAAS'y durable agent + disposable sandbox POC

Tracking: WOR-617, WOR-619

This directory is deliberately a **throwaway-capable proof of concept**. It exists to prove or falsify architecture before the full Change Intelligence Engine is built.

The two stacked questions are:

1. can a project-agnostic agent run survive worker loss and resume safely; and
2. can code execution happen in a fresh, tightly constrained container whose destruction does not destroy the run evidence?

It is not a production service and it does not depend on Workslip domain code, Laravel, GitHub, a real LLM provider, OPA or customer data.

## POC shape

```text
Temporal dev service
        |
        v
containerized Python worker
(NO Docker socket)
        |
        +--> fake provider activity (first call transiently fails)
        +--> idempotent tool activity
        |
        v
restricted sandbox broker
(the only POC service with Docker socket)
        |
        v
fresh disposable Python sandbox
  - network disabled
  - read-only root filesystem
  - all Linux capabilities dropped
  - no-new-privileges
  - memory + PID limits
  - tmpfs-only workspace
        |
        v
real unittest output
        |
        v
structured gate feedback -> corrected second attempt
        |
        v
WAITING_APPROVAL -> signal -> COMPLETED
```

The **worker never receives `/var/run/docker.sock`**. The separate broker is intentionally the narrow privileged boundary. It does not accept arbitrary images, Docker flags, mounts or commands; it accepts only a small source/test payload and runs the fixed POC harness in the configured sandbox image.

That broker is still highly privileged because a Docker socket is effectively host-level container control. It is acceptable only as a POC boundary. A production design must replace or harden this with a purpose-built sandbox runtime/broker and tenant isolation.

## WOR-617: destructive worker replacement

For the first POC the Temporal dev container remains alive while only the worker container is destroyed and recreated. That isolates the first question: does durable workflow history let a **replaceable worker** recover correctly?

The synthetic external tool state is stored in a Docker volume so it survives worker replacement and can prove idempotency across retries.

`scripts/destructive-test.sh` intentionally kills the worker **after the tool has committed its side effect but before the activity can acknowledge completion to Temporal**.

After restart, Temporal retries the unfinished activity. The tool receives the same idempotency key and must not apply the side effect a second time.

The test proves:

1. the workflow survives worker-container loss;
2. the first tool side effect is applied exactly once even though the activity is invoked again;
3. a simulated provider timeout is retried by Temporal in isolation;
4. worker replacement does not grant the worker Docker-daemon access;
5. execution can continue into the WOR-619 sandbox proof;
6. human approval resumes the same logical run.

Temporal-service failover/persistence remains a later proof and should be tested against Temporal Cloud rather than inferred from a local SQLite mount.

## WOR-619: disposable sandbox correction loop

The next slice uses a real tiny Python implementation and real `unittest` execution.

Attempt 1 proposes:

```python
def add(left: int, right: int) -> int:
    return left - right
```

The fixed test suite expects addition. A fresh sandbox therefore exits non-zero and returns its isolated test output as structured evidence.

The deterministic gate turns that evidence into `GateFeedback`. Attempt 2 receives the feedback and proposes:

```python
def add(left: int, right: int) -> int:
    return left + right
```

The second attempt runs the **same tests in a different fresh sandbox container** and must pass.

Every sandbox is removed immediately after execution. The Temporal run retains:

- sandbox container ID/name;
- source and test hashes;
- exit code and bounded test output;
- evidence that network was disabled;
- evidence that the root filesystem was read-only;
- capability/no-new-privileges evidence;
- memory/PID limit evidence;
- tmpfs workspace evidence;
- confirmation that the sandbox container was destroyed.

The harness also verifies that the test hash remains constant while the source hash changes. The agent is not allowed to “fix” the failure by weakening the test.

## Run locally

Prerequisite: Docker with Compose v2.

Run the combined destructive proof:

```bash
cd platform/mr-saasy-agent-poc
./scripts/destructive-test.sh
```

Run the focused sandbox proof:

```bash
cd platform/mr-saasy-agent-poc
./scripts/sandbox-test.sh
```

Temporal UI is exposed at `http://localhost:8233` while a test is running.

## Boundaries

The neutral models are in `app/contracts.py`. Temporal-specific orchestration stays in `app/workflow.py`, `app/worker.py` and `app/connection.py`.

`sandbox_broker/server.py` is explicitly infrastructure-specific and privileged. It must not leak into the neutral ChangeRun/Attempt/GateFeedback contracts.

The sandbox receives no Workslip database, customer storage, product credentials or Docker socket. Its network mode is `none`, and its only writable paths are temporary in-memory filesystems.

Laravel remains outside the execution kernel and can later expose project integrations/tools without owning durable workflow state.

## Exit criteria

A green sandbox POC means only that the **boundary is promising enough to continue evaluating**.

It does not prove:

- safe production execution of untrusted customer code;
- multi-tenant host isolation;
- Docker-daemon compromise containment;
- Temporal Cloud failover;
- secret injection;
- network allowlisting;
- Git checkout/write-back;
- rolling worker-version compatibility.

Do not promote this directory to a permanent platform service solely because CI is green. Use the evidence to decide the WOR-616 ADR and the future execution-isolation boundary.
