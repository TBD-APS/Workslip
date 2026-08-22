# New tenant — what Azure cannot do for you

**Status:** Active
**Owner:** Workslip repository owner
**Source of truth:** `src/BE/infrastructure/main.bicep`, `Docs/acs-email-setup.md`, the deployment scripts
**Review cadence:** whenever a new tenant, subscription, sending domain or GitHub environment is established

Everything in `main.bicep` is provisioned by `deploy.ps1`. This document covers the rest — the steps that need a person, a DNS provider, or a privileged consent, and that will silently produce a broken environment if skipped.

Ordered by how much damage skipping it does.

---

## 1. ACS email domain verification

**This is the one that breaks email, and it breaks it quietly.**

`main.bicep` provisions the `mrsoftware.dk` domain with `domainManagement: 'CustomerManaged'`. Azure creates the domain resource; it cannot verify it. Until DNS is published and verification passes, the domain exists and sends nothing.

The comment in `main.bicep` reading *"Production DNS verification is complete"* describes the **old** tenant. It is not true of a new one.

Four records must be published at the authoritative DNS zone:

| Type | Record |
|---|---|
| Domain | ownership TXT |
| SPF | sender-policy TXT |
| DKIM | first CNAME |
| DKIM2 | second CNAME |

**The values are different in the new tenant.** Verification tokens and DKIM selectors are issued per ACS resource, so the records currently in DNS belong to the old tenant's ACS and will not satisfy the new one. Read the new values after deployment:

```powershell
az communication email domain show `
  --resource-group rg-mrsoftware-prod `
  --email-service-name email-mrsoftware-prod `
  --domain-name mrsoftware.dk `
  --query "{records:properties.verificationRecords,states:properties.verificationStates}" `
  --output json
```

Publish them, then initiate verification for each type:

```powershell
foreach ($type in @('Domain','SPF','DKIM','DKIM2')) {
  az communication email domain initiate-verification `
    --resource-group rg-mrsoftware-prod `
    --email-service-name email-mrsoftware-prod `
    --domain-name mrsoftware.dk `
    --verification-type $type
}
```

Do not add MX records — this is a sender-only setup. If the DNS provider appends the zone automatically, enter only the host portion.

**Cutover consequence:** invitations and one-time login codes stop working between the moment traffic moves and the moment verification passes. DNS propagation is not instant. Decide deliberately whether to publish the new records ahead of cutover so both sets coexist, or accept an email outage window and tell people.

`Docs/acs-email-setup.md` holds the full detail.

---

## 2. Entra: privileged consent for the API identity

`main.bicep` assigns four Microsoft Graph application roles to the API managed identity:

- `User.ReadWrite.All`
- `User.Invite.All`
- `Application.Read.All`
- `AppRoleAssignment.ReadWrite.All`

Assigning Graph app roles is itself privileged. The identity running the first deployment must be a Global Administrator, or hold Privileged Role Administrator plus the equivalent Graph permissions. A plain Contributor on the subscription will fail here, and the failure arrives partway through the deployment rather than at the start.

The deploying user also needs `Organization.Read.All` or `Directory.Read.All` for the tenant default-domain lookup. Without it, pass `-EntraDefaultDomain <domain>` and the deployment proceeds.

---

## 3. Entra: custom domain, if you want it

The tenant's default domain is `<something>.onmicrosoft.com`. If users should have `@mrsoftware.dk` UPNs, that domain has to be added and verified in Entra as well — a separate verification from the ACS one above, with its own TXT record.

This is not required for the platform to work. `Azure:AdOAuth:Domain` resolves to whatever the tenant default is, and `UserEntraService` builds usernames against it. But it changes how every new account looks, so decide before creating users, not after.

---

## 4. Frontend domain and hosting

Not in Azure IaC at all, and easy to forget because it is not in this repository's deployment path.

`staticConfig.bicep` hard-codes the frontend origin:

```
Azure:Domain:BaseUrl   https://app.mrsoftware.dk
Cors:AllowedOrigins:0  https://app.mrsoftware.dk
Cors:AllowedOrigins:1  https://workslip-v2-0.vercel.app
Azure:Acs:InviteBaseUrl https://app.mrsoftware.dk/invite
```

- `app.mrsoftware.dk` DNS must point at the frontend host
- The Vercel project, its environment configuration and cache-purge credentials sit outside the Azure boundary, as the infrastructure README states

The API keeps its default `api-<company>-<env>.azurewebsites.net` hostname. No custom domain or certificate binding is provisioned for it. If that should change, it is new infrastructure work, not a manual step.

---

## 5. GitHub environment

Three values are tenant- or subscription-bound and must be updated by hand before CI can deploy:

| Name | Kind | Set by |
|---|---|---|
| `AZURE_TENANT_ID` | secret | you |
| `AZURE_SUBSCRIPTION_ID` | secret | you |
| `AZURE_CLIENT_ID` | secret | you |
| `AZURE_INFRA_CLIENT_ID` | variable | `deploy.ps1` phase 4, via `gh` |

The first deployment cannot run from CI. `infrastructure-production-reconcile.yml` authenticates with `AZURE_INFRA_CLIENT_ID`, and that identity is created by the deployment itself. Run locally first, then let CI take over.

The remaining repository secrets — `ANTHROPIC_API_KEY`, `KIMI_API_KEY`, `LINEAR_ACCESS_KEY`, `OPENAI_API_KEY`, `OLLAMA_API_KEY` — are not tenant-bound and need no action.

---

## 6. Subscription and billing

- The tenant and subscription themselves are created outside IaC
- **Confirm the billing currency.** `-BudgetMonthlyAmount` is expressed in it, and the default of 800 assumes DKK. A subscription billing in USD makes that budget roughly seven times looser than intended
- A break-glass administrator account for the new tenant, separate from daily-use accounts

---

## 7. Verification that needs a human

Provisioned automatically, but only a person can confirm it works:

- **Alert delivery.** Use Azure Monitor's *Test action group*. The recipient in `monitoring.config.json` is a personal address; confirm it is still right for this tenant and that mail is not filtered as spam
- **Cost budget.** `az consumption budget list` after deployment. The forecasted threshold stays quiet through the first billing period — a fresh subscription has no history to project from
- **User offboarding.** Delete a test user through Workslip and confirm the directory account disappears. Sign-in works via email fallback whether or not `EntraId` is correct, so this is the only check that proves the backfill did anything

---

## Do not do these by hand

They are automated, and doing them manually creates drift:

| Looks manual | Actually handled by |
|---|---|
| Resource provider registration, including `Microsoft.Consumption` | `deploy-infrastructure.ps1` |
| Global administrator object ID | resolved from the signed-in principal when the configured one is absent |
| Tenant default domain | resolved from Microsoft Graph |
| SQL administrator group and its membership | `main.bicep` plus `deploy-infrastructure.ps1` |
| SQL admin password, JWT signing key, VAPID key | generated and stored in Key Vault |
| `Azure:AdOAuth:TenantId` and `ClientId` | derived during deployment |
| GitHub OIDC federated credential | `main.bicep` |
