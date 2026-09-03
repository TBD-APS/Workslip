# Accounting integration boundary

Workslip owns operational job data. The connected accounting system remains the accounting source of truth for booked invoices, payments, VAT, journals, bank reconciliation and statutory accounting.

The accounting boundary is deliberately narrow:

- tenant-scoped provider selection and credentials come from configuration / secret-backed configuration;
- Workslip persists only external identifiers and synchronization state;
- customer synchronization is idempotent and links Workslip customer IDs to external customer numbers;
- approved Workslip jobs may create invoice drafts, but Workslip does not book or send invoices automatically;
- invoice status is read back into Workslip;
- supplier/accounting documents are read-only from the provider unless a later explicit accounting workflow is approved.

## e-conomic configuration

Production secrets must be supplied through the existing configuration chain (Azure App Configuration / Key Vault in production), never in source control or SQL.

```text
Integrations:Accounting:Organizations:<organization-guid>:Provider=economics
Integrations:Economic:AppSecretToken=<secret>
Integrations:Economic:Agreements:<organization-guid>:GrantToken=<secret>
Integrations:Economic:Defaults:CustomerGroupNumber=<number>
Integrations:Economic:Defaults:PaymentTermsNumber=<number>
Integrations:Economic:Defaults:VatZoneNumber=<number>
Integrations:Economic:Products:Hours=<product-number>
Integrations:Economic:Products:Material=<product-number>
Integrations:Economic:Products:Outlay=<product-number>
```

The global app-secret identifies the Workslip e-conomic app. The agreement grant token identifies the customer's e-conomic agreement.

## Guardrails

- No secret values in logs, telemetry, API responses or database rows.
- Draft creation is idempotent per Workslip job.
- Booking/sending invoices is intentionally outside this operational integration.
- Workslip never calculates or posts VAT/accounting entries itself.
- Material and outlay invoice lines are explicit monetary `JobBillableItems`; the worksheet `HasOutlay` flag is not treated as an amount.
