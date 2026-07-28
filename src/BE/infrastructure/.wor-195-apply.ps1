$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$New
    )

    $content = [System.IO.File]::ReadAllText($Path)
    if (-not $content.Contains($Old)) {
        throw "Expected text not found in $Path`n--- expected ---`n$Old"
    }

    $updated = $content.Replace($Old, $New)
    [System.IO.File]::WriteAllText($Path, $updated, [System.Text.UTF8Encoding]::new($false))
}

$main = 'src/BE/infrastructure/main.bicep'
$keyVault = 'src/BE/infrastructure/keyvaultConfig.bicep'
$entra = 'src/BE/infrastructure/entraRegistrations.bicep'
$infraReadme = 'src/BE/infrastructure/README.md'
$acsReadme = 'Docs/acs-email-setup.md'

Replace-Exact $main "param logicAppName string             = 'la-`${companyName}-`${toLower(environment)}'`n" ''
Replace-Exact $main "param documentIntelligenceName string = 'di-`${companyName}-`${toLower(environment)}'`n" ''
Replace-Exact $main "@description('Verified customer-managed ACS email domain used by every deployment.')" "@description('Verified customer-managed ACS email domain used by production deployments.')"
Replace-Exact $main "  cognitiveServicesUser:   'a97b65f3-24c7-4388-baec-2e87135dc908'`n" ''
Replace-Exact $main "  UserAuthenticationMethodReadWriteAll: '50483e42-d915-4231-9639-7fdb7fd190e5'`n" ''

Replace-Exact $main @'
var appInsightsConnectionString = appInsights.properties.ConnectionString
var appInsightsInstrumentationKey = appInsights.properties.InstrumentationKey
var sqlAdminGroupMailNickname = take(replace(sqlAdminGroupName, '-', ''), 64)
'@ @'
var appInsightsConnectionString = appInsights.properties.ConnectionString
var appInsightsInstrumentationKey = appInsights.properties.InstrumentationKey
var sqlAdminGroupMailNickname = take(replace(sqlAdminGroupName, '-', ''), 64)
var isProduction = toLower(environment) == 'prod'
var acsSenderAddress = isProduction
  ? '${customEmailSenderUsername}@${customEmailDomainName}'
  : 'DoNotReply@${azureManagedEmailDomain.properties.mailFromSenderDomain}'
'@

Replace-Exact $main "    acsSenderAddress: '`${customEmailSenderUsername}@`${customEmailDomainName}'" '    acsSenderAddress: acsSenderAddress'

Replace-Exact $main @'
module keyVaultConfigs './keyvaultConfig.bicep' = {
  name: 'key-vault-secrets'
  params: {
    keyVaultName: keyVault.name
    communicationServiceName: communicationService.name
    sqlConnectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=db-${companyName}-${environment};User ID=rbj;Password=${sqlAdminPassword}; TrustServerCertificate=False;'
  }
}
'@ @'
module keyVaultConfigs './keyvaultConfig.bicep' = {
  name: 'key-vault-secrets'
  params: {
    keyVaultName: keyVault.name
    communicationServiceName: communicationService.name
  }
}
'@

Replace-Exact $main @'
    linkedDomains: [
      azureManagedEmailDomain.id
      customEmailDomain.id
    ]
'@ @'
    linkedDomains: isProduction
      ? [
          azureManagedEmailDomain.id
          customEmailDomain.id
        ]
      : [
          azureManagedEmailDomain.id
        ]
'@

Replace-Exact $main @'
// Production DNS verification is complete. The custom domain and sender are
// unconditional parts of every infrastructure deployment.
resource customEmailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
'@ @'
// Production DNS verification is complete. Non-production environments use the
// Azure-managed domain and do not depend on production DNS ownership.
resource customEmailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = if (isProduction) {
'@
Replace-Exact $main "resource customEmailSender 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-04-01' = {" "resource customEmailSender 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-04-01' = if (isProduction) {"

Replace-Exact $main "output LOGIC_APP_NAME string                   = logicAppName`n" ''
Replace-Exact $main "output DOCUMENT_INTELLIGENCE_NAME string       = documentIntelligenceName`n" ''
Replace-Exact $main 'output AZURE_AD_OAUTH_APP_OBJECT_ID string      = EntraAppRegistrations.outputs.OAuthClientId' 'output AZURE_AD_OAUTH_APP_OBJECT_ID string      = EntraAppRegistrations.outputs.OAuthAppObjectId'
Replace-Exact $main @'
output ACS_CUSTOM_EMAIL_DOMAIN_ID string        = customEmailDomain.id
output ACS_CUSTOM_EMAIL_DOMAIN_ACTIVE bool      = true
output ACS_SENDER_ADDRESS string                = '${customEmailSenderUsername}@${customEmailDomainName}'
'@ @'
output ACS_CUSTOM_EMAIL_DOMAIN_ID string        = isProduction ? customEmailDomain.id : ''
output ACS_CUSTOM_EMAIL_DOMAIN_ACTIVE bool      = isProduction
output ACS_SENDER_ADDRESS string                = acsSenderAddress
'@

Replace-Exact $keyVault @'
@secure()
@description('Retained for main.bicep compatibility. Runtime SQL uses managed identity and this value is not persisted by this module.')
param sqlConnectionString string

'@ ''

Replace-Exact $entra @'
output OAuthClientId string = validatedValues.oauthClientId
// Existing callers use OAuthAppId as the API audience application/client ID.
'@ @'
output OAuthClientId string = validatedValues.oauthClientId
output OAuthAppObjectId string = validatedValues.oauthAppObjectId
// Existing callers use OAuthAppId as the API audience application/client ID.
'@

Replace-Exact $infraReadme '| `deploy.ps1` | Reconcile Entra, remove the obsolete deployment-created OAuth credential and deploy Azure infrastructure. |' '| `deploy.ps1` | Reconcile Entra and deploy Azure infrastructure. |'
Replace-Exact $infraReadme @'
The sequence is:

1. `deploy-entra.ps1` reconciles the two Entra applications and service principals.
2. The internal credential-cleanup step removes the exact obsolete `workslip-deploy-{environment}-oauth-client-secret` credential when present.
3. `deploy-infrastructure.ps1` deploys and reconciles Azure resources.
'@ @'
The sequence is:

1. `deploy-entra.ps1` reconciles the two Entra applications and service principals.
2. `deploy-infrastructure.ps1` deploys and reconciles Azure resources.
'@
Replace-Exact $infraReadme '`grant-web-api-sql-access.ps1` and files under `internal/` are called by the supported entry points. They are not standalone deployment commands and must not be referenced as startup scripts in automation or operator documentation.' '`grant-web-api-sql-access.ps1` is called by `deploy-infrastructure.ps1`. It is an implementation helper, not a standalone operator command.'
Replace-Exact $infraReadme @'
The verified `mrsoftware.dk` ACS email domain and `noreply@mrsoftware.dk` sender are selected by every supported infrastructure deployment. There is no operator activation parameter.

The Azure-managed domain remains linked only as an emergency rollback resource; the supported deployment path always provisions the custom sender and writes `noreply@mrsoftware.dk` to App Configuration.
'@ @'
Production selects the verified `mrsoftware.dk` ACS email domain and `noreply@mrsoftware.dk` sender. Non-production environments use their Azure-managed domain and generated `DoNotReply@<domain>.azurecomm.net` sender. There is no operator activation parameter; the environment determines the sender.

The Azure-managed domain remains linked in production as an emergency rollback resource. Non-production deployments do not provision or link the production custom domain.
'@
Replace-Exact $infraReadme '6. `Azure:Acs:SenderAddress` is `noreply@mrsoftware.dk` and the ACS domain verification states remain successful.' '6. In production, `Azure:Acs:SenderAddress` is `noreply@mrsoftware.dk` and the ACS domain verification states remain successful; non-production uses its Azure-managed sender.'

Replace-Exact $acsReadme @'
Every supported Azure infrastructure deployment provisions and configures:

- the Communication Services resource `acs-<company>-<environment>`;
- the Email Communication Service `email-<company>-<environment>`;
- the verified customer-managed domain `mrsoftware.dk`;
- the `noreply` sender;
- the custom-domain link on Communication Services;
- `Azure:Acs:SenderAddress = noreply@mrsoftware.dk` in Azure App Configuration.

There is no operator activation parameter. The supported deployment scripts always select the custom domain and sender.

The Azure-managed domain remains linked as an emergency rollback resource, but it is not selected by the normal deployment path.
'@ @'
Every supported Azure infrastructure deployment provisions the Communication Services resource, Email Communication Service, Azure-managed domain and `DoNotReply` sender.

Production additionally provisions and links the verified `mrsoftware.dk` domain, provisions the `noreply` sender and writes `Azure:Acs:SenderAddress = noreply@mrsoftware.dk` to Azure App Configuration. The Azure-managed domain remains linked as an emergency rollback resource.

Non-production environments do not provision or link `mrsoftware.dk`; they write the generated Azure-managed `DoNotReply@<domain>.azurecomm.net` address instead.

There is no operator activation parameter. Sender selection is derived from `environment == prod`.
'@
Replace-Exact $acsReadme @'
Bicep owns `Azure:Acs:SenderAddress`; manual App Configuration edits are overwritten by the next deployment. The required value is:

```text
noreply@mrsoftware.dk
```
'@ @'
Bicep owns `Azure:Acs:SenderAddress`; manual App Configuration edits are overwritten by the next deployment. Production uses `noreply@mrsoftware.dk`; non-production uses its generated Azure-managed sender address.
'@
Replace-Exact $acsReadme 'A broken DNS verification state is a production configuration fault, not a deployment mode. Repair the DNS records and re-run deployment. Do not reintroduce an operator activation toggle.' 'A broken production DNS verification state is a production configuration fault. Repair the DNS records and re-run deployment. Non-production environments are unaffected because they use Azure-managed domains. Do not reintroduce an operator activation toggle.'
