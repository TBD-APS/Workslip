# ADR 0012: Domain-field interactions are an explicit central UI policy

- Status: Accepted
- Date: 2026-08-20
- Tracking: WOR-724
- Related: WOR-266, WOR-386, WOR-725, WOR-726, WOR-727

## Context

Workslip displays the same business values in many surfaces: customer lists and details, People, job cards, overview pages, assignment flows and mobile variants. Interaction behaviour implemented locally causes drift: a phone number can be copyable on one page but not another, one e-mail can open the mail client while another only copies, and changing the product decision later requires hunting through many components.

Address copy already proved the value of shared behaviour. A copy-only policy is still too narrow for the long-term product requirement: the same semantic value may support several user actions. A phone number should be able to expose both **Kopiér** and **Ring op**; an e-mail address should expose **Kopiér** and **Send mail**. Future fields may add Maps or other actions without page-specific interaction logic.

The product requirement is therefore broader than clipboard behaviour: changing what a semantic field can do must remain cheap and predictable as the UI grows.

## Decision

1. Workslip owns one canonical frontend **domain-field interaction policy registry**.
2. Every policy-controlled semantic field has an explicit `copyable: true | false` decision and an explicit `actions` list. Absence is not the long-term way to express a deliberate product decision.
3. Supported actions are centrally typed (currently `copy`, `call`, `email`, `maps`) and may be extended when the product adds another reusable interaction class.
4. The registry also owns the field label and normalization used by interactions.
5. Shared UI renders policy-controlled values through the common policy-aware `DomainValue` primitive (`CopyableValue` remains only a migration-compatible name).
6. `DomainValue` interprets the policy consistently:
   - zero actions -> ordinary non-interactive text;
   - one `copy` action -> direct one-click/tap copy;
   - multiple actions -> activating the value opens one compact action chooser generated from the registry.
7. Phone fields use `actions: ['copy', 'call']`; the chooser offers **Kopiér** and **Ring op** using a platform-native `tel:` link.
8. E-mail fields use `actions: ['copy', 'email']`; the chooser offers **Kopiér** and **Send mail** using a platform-native `mailto:` link.
9. Future reusable actions such as Maps must be added to the central policy/action renderer rather than hand-coded on each page. Existing specialist components may remain during migration, but shared low-level behaviour must not fork.
10. The same semantic field must use the same policy key everywhere it is rendered in read-only product UI. Page-local `navigator.clipboard`, `tel:`, `mailto:`, Maps handlers or local `isCopyable`/action booleans are not allowed for policy-controlled values.
11. A policy change should normally be made in **one place**: change the registry entry. Existing policy-aware renderings must inherit the new behaviour without page-specific edits.
12. Editable form controls are not automatically wrapped as read-only domain values; editing semantics remain primary unless a separate UX decision explicitly adds actions.

## Policy shape

Conceptually:

```ts
'customer.name': {
  copyable: true,
  actions: ['copy'],
}

'customer.phone': {
  copyable: true,
  actions: ['copy', 'call'],
}

'customer.email': {
  copyable: true,
  actions: ['copy', 'email'],
}

'user.role': {
  copyable: false,
  actions: [],
}
```

`copyable` remains explicit because it is a product decision that should be auditable at a glance. Tests enforce that it stays consistent with whether `copy` appears in `actions`.

## Maintainability contract

The policy exists specifically so interaction decisions stay easy to change over time.

When changing a field interaction:

1. update the single field entry in `src/FE/src/lib/copyableFields.ts`;
2. do not add conditional copy/call/mail logic to individual pages;
3. if a rendering does not react to the central change, migrate that rendering to `DomainValue` instead of special-casing it;
4. keep semantic keys stable (`customer.phone`, `user.email`, `job.reportNumber`, etc.);
5. add a new reusable action type centrally before exposing that action on any page;
6. extend unit/browser coverage when introducing a new interaction class.

A reviewer should be able to answer both **“what is copyable in Workslip?”** and **“what actions can I perform on this field?”** by reading the registry rather than searching the application.

## UX contract

### Single-action fields

A field with only `copy` remains fast: click/tap copies immediately. Enter/Space works for keyboard users and success/error feedback reflects the actual clipboard result.

### Multi-action fields

A field with more than one action opens a compact chooser on click/tap. Examples:

- phone: `Kopiér` / `Ring op`;
- e-mail: `Kopiér` / `Send mail`.

The chooser must:

- work on desktop and touch/mobile;
- expose large enough touch targets;
- be keyboard accessible;
- close after a successful copy, Escape or outside interaction;
- use platform-native URI schemes where appropriate (`tel:`, `mailto:`);
- stop propagation so opening/using it inside a clickable card does not navigate the card.

### Non-interactive fields

A field with no actions is ordinary text with no copy cursor, action icon, button role or side effect. Parent row/card behaviour remains unchanged.

## Consequences

### Positive

- copy/call/mail behaviour becomes a deliberate product policy instead of scattered UI behaviour;
- one semantic field has one interaction contract across customer, People, job and future views;
- adding another reusable action does not require redesigning every page;
- desktop and mobile stay aligned while still using native device capabilities;
- explicit empty action lists document deliberate non-interactive decisions.

### Costs

- policy-controlled read-only renderings must use `DomainValue` for the one-place update guarantee to hold;
- the registry must be extended when new semantic field families or reusable action types are introduced;
- multi-action fields require a small shared action-menu UI and corresponding browser coverage;
- legacy specialist actions may need gradual migration to the policy system.

## Guardrails

- No new page-local clipboard implementation for domain values.
- No page-local `tel:` or `mailto:` behaviour for policy-controlled fields.
- No duplicated interaction booleans in feature components.
- No weakening card/navigation behaviour to make field actions easier.
- Stable test hooks may be added where browser evidence requires them, but hooks must not become the policy source of truth.
- Regression coverage must include direct-copy, multi-action and explicit no-action cases.

## References

- `src/FE/src/lib/copyableFields.ts`
- `src/FE/src/components/CopyableValue.tsx`
- Linear: WOR-724, WOR-725, WOR-726, WOR-727
- Historical address work: WOR-266, WOR-386
