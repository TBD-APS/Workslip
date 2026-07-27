from pathlib import Path

MAIN_PATH = Path("src/BE/infrastructure/main.bicep")
DOC_PATH = Path("Docs/acs-email-setup.md")
WORKFLOW_PATH = Path(".github/workflows/wor-187-apply.yml")
SCRIPT_PATH = Path(__file__)


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one {label} match, found {count}.")
    return source.replace(old, new, 1)


main = MAIN_PATH.read_text(encoding="utf-8")

main = replace_once(
    main,
    """param communicationServiceName string = take('acs-${companyName}-${toLower(environment)}', 64)
param emailServiceName string         = take('email-${companyName}-${toLower(environment)}', 64)
param githubOwner string            = 'rasm105k'
""",
    """param communicationServiceName string = take('acs-${companyName}-${toLower(environment)}', 64)
param emailServiceName string         = take('email-${companyName}-${toLower(environment)}', 64)
@description('Customer-managed ACS email domain. Keep activation disabled until Domain, SPF, DKIM and DKIM2 are verified.')
param customEmailDomainName string = 'mrsoftware.dk'
@description('Sender username used on the verified customer-managed email domain.')
param customEmailSenderUsername string = 'noreply'
@description('Links the customer-managed domain and switches Azure:Acs:SenderAddress to it. Enable only after DNS verification succeeds.')
param activateCustomEmailDomain bool = false
param githubOwner string            = 'rasm105k'
""",
    "email-domain parameter block",
)

main = replace_once(
    main,
    """    acsConnectionString: keyVaultConfigs.outputs.acsConnectionStringSecretUri
    acsSenderAddress:  '${senderUsername.properties.username}@${emailDomain.properties.fromSenderDomain}'
""",
    """    acsConnectionString: keyVaultConfigs.outputs.acsConnectionStringSecretUri
    acsSenderAddress: activateCustomEmailDomain
      ? '${customEmailSenderUsername}@${customEmailDomainName}'
      : '${azureManagedSenderUsername.properties.username}@${azureManagedEmailDomain.properties.fromSenderDomain}'
""",
    "ACS sender App Configuration block",
)

main = replace_once(
    main,
    """    linkedDomains: [
      emailDomain.id
    ]
""",
    """    linkedDomains: activateCustomEmailDomain
      ? [
          azureManagedEmailDomain.id
          customEmailDomain.id
        ]
      : [
          azureManagedEmailDomain.id
        ]
""",
    "Communication Services linked-domain block",
)

main = replace_once(
    main,
    """resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  name: 'AzureManagedDomain'
  parent: emailService
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
  }
}

resource senderUsername 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-03-31' = {
  parent: emailDomain
  name: 'DoNotReply'
  properties: {
    displayName: 'Workslip'
    username: 'DoNotReply'
  }
}
""",
    """// Keep the Azure-managed domain linked as a rollback sender during rollout.
resource azureManagedEmailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  name: 'AzureManagedDomain'
  parent: emailService
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
  }
}

resource azureManagedSenderUsername 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-03-31' = {
  parent: azureManagedEmailDomain
  name: 'DoNotReply'
  properties: {
    displayName: 'Workslip'
    username: 'DoNotReply'
  }
}

// The domain is provisioned before activation so Azure can expose its unique DNS
// verification records. Linking and sender creation remain gated until all four
// verification states are successful.
resource customEmailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  name: customEmailDomainName
  parent: emailService
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'CustomerManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource customEmailSender 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-04-01' = if (activateCustomEmailDomain) {
  parent: customEmailDomain
  name: customEmailSenderUsername
  properties: {
    displayName: 'Workslip'
    username: customEmailSenderUsername
  }
}
""",
    "email-domain resources",
)

main = replace_once(
    main,
    """output ACS_ENDPOINT string                     = 'https://${communicationService.properties.hostName}'
output ACS_SENDER_ADDRESS string               = 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
""",
    """output ACS_ENDPOINT string                     = 'https://${communicationService.properties.hostName}'
output ACS_CUSTOM_EMAIL_DOMAIN_ID string        = customEmailDomain.id
output ACS_CUSTOM_EMAIL_DOMAIN_ACTIVE bool      = activateCustomEmailDomain
output ACS_SENDER_ADDRESS string                = activateCustomEmailDomain
  ? '${customEmailSenderUsername}@${customEmailDomainName}'
  : '${azureManagedSenderUsername.properties.username}@${azureManagedEmailDomain.properties.mailFromSenderDomain}'
""",
    "ACS outputs",
)

required_fragments = [
    "param activateCustomEmailDomain bool = false",
    "domainManagement: 'CustomerManaged'",
    "resource customEmailSender 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-04-01' = if (activateCustomEmailDomain)",
    "linkedDomains: activateCustomEmailDomain",
    "acsSenderAddress: activateCustomEmailDomain",
    "output ACS_CUSTOM_EMAIL_DOMAIN_ACTIVE bool",
]
for fragment in required_fragments:
    if fragment not in main:
        raise RuntimeError(f"Missing required Bicep fragment: {fragment}")

if "resource emailDomain " in main or "resource senderUsername " in main:
    raise RuntimeError("Stale ACS resource symbols remain after patching.")

MAIN_PATH.write_text(main, encoding="utf-8")

DOC_PATH.write_text(
    """# ACS Email Setup

**Status:** Maintained
**Owner:** Workslip
**Source of truth:** `src/BE/infrastructure/main.bicep` and `Workslip.Infrastructure/AcsEmailService.cs`
**Review cadence:** Review whenever the ACS email domain, DNS provider, sender address, or invitation flow changes.

## Purpose

Workslip sends invitations and one-time login codes through Azure Communication Services (ACS) Email. Production should send as:

```text
Workslip <noreply@mrsoftware.dk>
```

ACS provides outbound delivery only. This setup does not require a mailbox, mail hosting, MX records, or WHOIS add-ons.

## Provisioned resources

`main.bicep` provisions:

- the Communication Services resource `acs-<company>-<environment>`;
- the Email Communication Service `email-<company>-<environment>`;
- an Azure-managed email domain and `DoNotReply` sender for rollback;
- the customer-managed domain `mrsoftware.dk`;
- the `noreply` sender after the custom domain is activated;
- `Azure:Acs:SenderAddress` in Azure App Configuration.

The custom-domain rollout is deliberately staged through:

```bicep
param activateCustomEmailDomain bool = false
```

With activation disabled, Bicep creates the customer-managed domain but keeps the Azure-managed domain linked and active. With activation enabled, Bicep also creates `noreply`, links `mrsoftware.dk`, and writes `noreply@mrsoftware.dk` to App Configuration.

## Phase 1: provision the domain

Deploy `main.bicep` while `activateCustomEmailDomain` remains `false`. The deployment output `ACS_CUSTOM_EMAIL_DOMAIN_ID` identifies the domain resource.

For the production defaults used by this repository:

```text
Resource group: rg-mrsoftware-prod
Email service: email-mrsoftware-prod
Domain: mrsoftware.dk
```

The Azure CLI `communication` extension requires Azure CLI 2.67 or newer. Install or update it before using the commands below.

Inspect the DNS records generated by Azure:

```powershell
az communication email domain show `
  --resource-group rg-mrsoftware-prod `
  --email-service-name email-mrsoftware-prod `
  --domain-name mrsoftware.dk `
  --query properties.verificationRecords `
  --output json
```

Add the returned records to the authoritative DNS provider exactly as Azure reports them. The required checks are:

- `Domain`: ownership TXT record;
- `SPF`: sender-policy TXT record;
- `DKIM`: first CNAME record;
- `DKIM2`: second CNAME record.

Do not add MX records for this sender-only setup. If the DNS provider automatically appends `mrsoftware.dk`, enter only the host/name portion shown for that DNS zone.

## Phase 2: verify DNS

Initiate each verification separately:

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

Check the current states:

```powershell
az communication email domain show `
  --resource-group rg-mrsoftware-prod `
  --email-service-name email-mrsoftware-prod `
  --domain-name mrsoftware.dk `
  --query properties.verificationStates `
  --output table
```

Do not activate the custom sender until `Domain`, `SPF`, `DKIM`, and `DKIM2` all show a successful verified state in Azure.

## Phase 3: activate the sender

After all verification gates pass, change the production value to:

```bicep
param activateCustomEmailDomain bool = true
```

Then deploy the same Bicep template again. The deployment must result in:

```text
ACS_CUSTOM_EMAIL_DOMAIN_ACTIVE = true
ACS_SENDER_ADDRESS = noreply@mrsoftware.dk
```

The Azure-managed domain remains linked as a rollback sender, but `Azure:Acs:SenderAddress` selects the custom sender.

Do not merge a change that activates the custom domain before Azure has verified all four DNS checks. A deployment can otherwise fail while updating the Communication Services domain link or sender username.

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

Bicep owns `Azure:Acs:SenderAddress`; do not treat a manual App Configuration edit as permanent. The active value should be:

```text
noreply@mrsoftware.dk
```

The invitation base URL must continue to produce links under:

```text
https://app.mrsoftware.dk/invite/
```

## Smoke test

After activation:

1. Send an invitation through the production Workslip UI to an external mailbox.
2. Confirm the visible sender is `Workslip <noreply@mrsoftware.dk>`.
3. Confirm the invitation link starts with `https://app.mrsoftware.dk/invite/`.
4. Complete the invitation flow and verify authenticated API access.
5. Request a one-time login code and confirm it uses the same sender.
6. Check ACS delivery status and Application Insights for failures or bounces.

## Rollback

Set `activateCustomEmailDomain` back to `false` and redeploy. This restores the Azure-managed sender in App Configuration and removes the custom domain from the active linked-domain set without deleting the verified custom domain.

Do not delete `mrsoftware.dk` or its DNS records as part of a normal rollback.

## Troubleshooting

| Symptom | Likely cause | Correction |
|---|---|---|
| Domain verification remains pending | DNS record is missing, duplicated, or entered with the full zone twice | Compare the authoritative DNS response with `properties.verificationRecords` |
| SPF fails | Existing SPF policy conflicts or multiple SPF TXT records exist | Merge authorized senders into one SPF policy rather than publishing multiple SPF records |
| DKIM or DKIM2 fails | CNAME host or target was copied incorrectly | Recreate the exact selector record returned by Azure |
| `Invalid sender address` | Custom domain is not linked, sender was not created, or App Configuration still contains the old value | Confirm the Bicep activation output and `Azure:Acs:SenderAddress` |
| Invitation arrives but link is wrong | `Azure:Acs:InviteBaseUrl` is stale | Set it to the production `app.mrsoftware.dk` invitation route and redeploy configuration |
""",
    encoding="utf-8",
)

WORKFLOW_PATH.unlink()
SCRIPT_PATH.unlink()
