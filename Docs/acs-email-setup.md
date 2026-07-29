# ACS Email Setup

**Status:** Maintained  
**Owner:** Workslip  
**Source of truth:** `src/BE/infrastructure/main.bicep` and `Workslip.Infrastructure/AcsEmailService.cs`  
**Review cadence:** Review whenever the ACS email domain, DNS provider, sender address, or invitation flow changes.

## Purpose

Workslip sends invitations and one-time login codes through Azure Communication Services Email. Production sends as:

```text
Workslip <noreply@mrsoftware.dk>
```

ACS provides outbound delivery only. This setup does not require a mailbox, mail hosting, MX records, or WHOIS add-ons.

## Deployment model

Every supported Azure infrastructure deployment provisions the Communication Services resource, Email Communication Service, Azure-managed domain and `DoNotReply` sender.

Production additionally provisions and links the verified `mrsoftware.dk` domain, provisions the `noreply` sender and writes `Azure:Acs:SenderAddress = noreply@mrsoftware.dk` to Azure App Configuration. The Azure-managed domain remains linked as an emergency rollback resource.

Non-production environments do not provision or link `mrsoftware.dk`; they write the generated Azure-managed `DoNotReply@<domain>.azurecomm.net` address instead.

There is no operator activation parameter. Sender selection is derived from `environment == prod`.

For the production defaults:

```text
Resource group: rg-mrsoftware-prod
Email service: email-mrsoftware-prod
Domain: mrsoftware.dk
Sender: noreply@mrsoftware.dk
```

## DNS verification maintenance

The authoritative DNS zone must continue to satisfy all four Azure verification checks:

- `Domain`: ownership TXT record;
- `SPF`: sender-policy TXT record;
- `DKIM`: first CNAME record;
- `DKIM2`: second CNAME record.

Do not add MX records for this sender-only setup. If the DNS provider automatically appends `mrsoftware.dk`, enter only the host/name portion shown for that DNS zone.

The Azure CLI `communication` extension requires Azure CLI 2.67 or newer. Inspect the current verification records and states with:

```powershell
az communication email domain show `
  --resource-group rg-mrsoftware-prod `
  --email-service-name email-mrsoftware-prod `
  --domain-name mrsoftware.dk `
  --query "{records:properties.verificationRecords,states:properties.verificationStates}" `
  --output json
```

When DNS records are intentionally changed, initiate verification for each type:

```powershell
$verificationTypes = @('Domain', 'SPF', 'DKIM', 'DKIM2')

foreach ($verificationType in $verificationTypes) {
  az communication email domain initiate-verification `
    --resource-group rg-mrsoftware-prod `
    --email-service-name email-mrsoftware-prod `
    --domain-name mrsoftware.dk `
    --verification-type $verificationType

  if ($LASTEXITCODE -ne 0) {
    throw "ACS verification could not be started for $verificationType."
  }
}
```

## Application configuration

`AcsEmailService` reads:

```text
Azure:Acs:ConnectionString
Azure:Acs:SenderAddress
Azure:Acs:InviteBaseUrl
Azure:Acs:PLainHeaderText
Azure:Acs:PlainInviteText
Azure:Acs:HtmlInviteText
```

Bicep owns `Azure:Acs:SenderAddress`; manual App Configuration edits are overwritten by the next deployment. Production uses `noreply@mrsoftware.dk`; non-production uses its generated Azure-managed sender address.

The invitation base URL must continue to produce links under:

```text
https://app.mrsoftware.dk/invite/
```

## Post-deployment smoke test

1. Confirm Domain, SPF, DKIM and DKIM2 remain verified in Azure.
2. Send an invitation through the production Workslip UI to an external mailbox.
3. Confirm the visible sender is `Workslip <noreply@mrsoftware.dk>`.
4. Confirm the invitation link starts with `https://app.mrsoftware.dk/invite/`.
5. Complete the invitation flow and verify authenticated API access.
6. Request a one-time login code and confirm it uses the same sender.
7. Check ACS delivery status and Application Insights for failures or bounces.

## Failure and rollback policy

A broken production DNS verification state is a production configuration fault. Repair the DNS records and re-run deployment. Non-production environments are unaffected because they use Azure-managed domains. Do not reintroduce an operator activation toggle.

An emergency switch to the Azure-managed sender requires a dedicated reviewed infrastructure change and matching documentation update.

## Troubleshooting

| Symptom | Likely cause | Correction |
|---|---|---|
| Domain verification is not successful | DNS record is missing, duplicated, or entered with the full zone twice | Compare authoritative DNS with `properties.verificationRecords` |
| SPF fails | Existing SPF policy conflicts or multiple SPF TXT records exist | Merge authorized senders into one SPF policy rather than publishing multiple SPF records |
| DKIM or DKIM2 fails | CNAME host or target was copied incorrectly | Recreate the exact selector record returned by Azure |
| `Invalid sender address` | Custom domain is not linked, sender was not created, or App Configuration is stale | Re-run infrastructure deployment and verify `Azure:Acs:SenderAddress` |
| Invitation arrives but link is wrong | `Azure:Acs:InviteBaseUrl` is stale | Set it to the production `app.mrsoftware.dk` invitation route and redeploy configuration |
