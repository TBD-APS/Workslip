---
id: product.capability.example
product: product
type: capability
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

# Capability name

## Purpose

What durable user or system outcome does this capability provide?

## Actors and user intent

Who uses it and what are they trying to achieve?

## Preconditions

What must already be true?

## Happy path

Describe the smallest complete successful flow.

## States and transitions

List meaningful states and allowed transitions. Avoid copying implementation-only states that have no durable meaning.

## Business rules and invariants

List rules that must remain true across implementations.

## Permissions and tenant boundary

State who may read/change what and where authorization is enforced.

## Failure and edge cases

Describe important expected failures, retries, conflicts and recovery behaviour.

## APIs, events and data touched

Link stable contracts and ownership; do not paste generated OpenAPI or schema dumps.

## UI locations

Where is the capability discoverable to users?

## Related capabilities and decisions

Link knowledge IDs, ADRs and related capability documents.

## Verification and tests

Name executable tests or runtime evidence that proves the important behaviour.

## Source and provenance

State the primary implementation/configuration authorities used to review this document.
