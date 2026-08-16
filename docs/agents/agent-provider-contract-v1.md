# Agent Provider Contract v1

## Purpose

Define the provider-neutral foundation for MR SAAS'y AI workers.

Kimi is the first provider, but no agent workflow should depend directly on a single model vendor.

## Agent Run

Every execution should expose:

- run id
- provider
- agent name
- trigger
- status
- timestamps
- result summary
- evidence references

Statuses:

- Queued
- Running
- Completed
- Failed
- Cancelled

## Provider Contract

Providers must implement a common execution boundary:

- execute agent task
- report status
- expose capabilities
- return structured evidence

## Evidence Rules

Agent output must be traceable and include:

- source trigger
- timestamp
- confidence
- findings
- recommendations

Never store:

- API keys
- credentials
- unnecessary customer data

## Initial Provider

Kimi API is the first implementation. Future providers must be replaceable without changing Control Center workflows.
