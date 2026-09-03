---
id: workslip.product.index
product: workslip
type: product
status: active
owner: product/workslip
visibility: internal
audience: [agent, developer, operator]
last_reviewed: 2026-09-03
code_refs: []
api_refs: []
linear_refs: [WOR-748]
adr_refs: []
---

# Workslip product knowledge

This directory is the canonical product-language layer for Workslip knowledge. It should explain stable product meaning that agents otherwise have to reconstruct from UI labels, code and tickets.

Prefer small documents for:

- product overview and boundaries;
- terminology and aliases;
- roles and permission intent;
- module/capability map;
- durable business concepts.

Runtime behaviour remains authoritative in current code/config/tests. Product documents explain the meaning and connect it to capabilities, workflows and implementation evidence.
