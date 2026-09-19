---
id: product.workflow.example
product: product
type: workflow
status: draft
owner: team/name
visibility: internal
audience: [agent, developer, operator]
capabilities: [example]
last_reviewed: 2026-09-03
code_refs: []
api_refs: []
linear_refs: []
adr_refs: []
---

# Workflow name

## Purpose

What outcome does this workflow produce, and why does it exist?

## Actors and user intent

List the actors and the intent that starts the workflow.

## Preconditions

List required state, permissions and dependencies.

## Happy path

Describe the normal sequence in user/domain terms.

## States and transitions

| From | Action/condition | To | Guard/owner |
|---|---|---|---|
| | | | |

## Business rules and invariants

List rules that must remain true across UI/API implementations.

## Permissions and tenant boundary

State authorization, visibility and tenant isolation expectations.

## Failure and edge cases

Describe rejection, retry, partial completion, concurrency and recovery where relevant.

## APIs, events and data touched

Reference stable API/event/data contracts and the owning component.

## UI locations

List the user-visible entry points.

## Related capabilities and decisions

Link durable knowledge IDs and ADRs.

## Verification and tests

List tests and observable evidence that prove the workflow.

## Source and provenance

State which code/config/tests were reviewed and when.
