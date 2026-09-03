# Accounting integration boundary

Workslip owns operational job data. The connected accounting system remains the accounting source of truth for booked invoices, payments, VAT, journals, bank reconciliation and statutory accounting.

The accounting boundary is deliberately narrow. The Workslip UI may surface operational accounting actions, but accounting finalization remains in the connected provider:

- tenant-scoped provider selection is activated automatically after a successful provider connection;
- Workslip persists external identifiers, synchronization state and encrypted provider grants only;
- customer synchronization is idempotent and links Workslip customer IDs to external customer numbers;
- approved Workslip jobs may create invoice drafts, but Workslip does not book or send invoices automatically;
- invoice status is read back into Workslip;
- supplier/accounting documents are read-only from the provider unless a later explicit accounting workflow is approved.

## e-conomic configuration

The MR Software e-conomic app is configured once globally. Production secrets are supplied through the existing Azure App Configuration / Key Vault configuration chain and never committed to source control.

```text
Integrations:Economic:AppSecretToken=<secret>
Integrations:Economic:InstallationUrl=<installation-url-from-e-conomic-developer-portal>
Integrations:Economic:TokenEncryptionKey=<recommended-dedicated-secret>
```

In the e-conomic developer portal, the app redirect URL must point to:

```text
https://app.mrsoftware.dk/api/accounting/economic/callback
```

The `TokenEncryptionKey` is recommended so AppSecret rotation is independent from stored customer grants. For backwards-compatible deployments, Workslip derives the encryption key from `AppSecretToken` when a dedicated key is not present; establish the dedicated key before rotating the app secret.

### Customer connection flow

1. An authenticated Workslip admin opens **Administrativt → Integrationer** and selects **Forbind e-conomic**.
2. Workslip creates a cryptographically random, ten-minute connection correlation. Only its SHA-256 hash is persisted; the browser receives the correlation in an HttpOnly, SameSite=Lax cookie scoped to the callback path.
3. The browser is redirected to the official e-conomic installation URL.
4. e-conomic asks the accounting user to grant the MR Software app access and redirects the browser to the Workslip callback with `token=<AgreementGrantToken>`.
5. Workslip consumes the one-time correlation, verifies the grant against e-conomic `/self`, encrypts the grant with AES-GCM and tenant-bound associated data, and stores only ciphertext in `EconomicConnections`.
6. The browser returns to `/app/settings` with a green connected state. The integration engine now selects e-conomic automatically for that organization.

The AgreementGrantToken is never returned to React, logs, telemetry or API responses. It exists as plaintext only transiently inside the backend callback and provider request pipeline.

### Defaults

Customer group, payment terms and VAT zone may still be pinned globally when a specific accounting setup requires it:

```text
Integrations:Economic:Defaults:CustomerGroupNumber=<number>
Integrations:Economic:Defaults:PaymentTermsNumber=<number>
Integrations:Economic:Defaults:VatZoneNumber=<number>
Integrations:Economic:Defaults:Currency=DKK
```

When these values are absent, Workslip resolves a usable existing e-conomic customer group, payment term and domestic VAT zone at the time a new customer is pushed. Workslip does not create or alter the chart of accounts during onboarding.

Product mappings are optional:

```text
Integrations:Economic:Products:Hours=<product-number>
Integrations:Economic:Products:Material=<product-number>
Integrations:Economic:Products:Outlay=<product-number>
```

When a mapping exists, Workslip uses e-conomic's invoice-line template for that product. Without a mapping, Workslip creates a free invoice line containing description, quantity and unit net price, so connecting the integration does not require Workslip-specific products to be created first.

Legacy deployments may still provide `Integrations:Economic:Agreements:<organization-guid>:GrantToken` or an explicit accounting provider setting; the UI connection flow is preferred for customer-managed connections.

## Guardrails

- No plaintext provider secret values in logs, telemetry, API responses or database rows.
- Connection callbacks are one-time and expire after ten minutes.
- A callback cannot choose its organization from a query parameter; tenant identity comes from server-side correlation state.
- Draft creation is idempotent per Workslip job.
- Booking/sending invoices is intentionally outside this operational integration.
- Workslip never calculates or posts VAT/accounting entries itself.
- Material and outlay invoice lines are explicit monetary `JobBillableItems`; the worksheet `HasOutlay` flag is not treated as an amount.
