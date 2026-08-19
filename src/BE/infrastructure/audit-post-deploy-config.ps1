param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$COMPANY_NAME = 'mrsoftwareinc',
    [string]$ExpectedTenantId = '',
    [string]$ExpectedSubscriptionId = '',
    [switch]$ExpectPowerBiEnabled
)

$ErrorActionPreference = 'Stop'

$environmentName = $Environment.ToLowerInvariant()
$resourceGroup = "rg-$COMPANY_NAME-$environmentName"
$appConfigurationName = "appcs-$COMPANY_NAME-$environmentName"
$keyVaultName = "kv-$COMPANY_NAME-$environmentName"
$webAppName = "api-$COMPANY_NAME-$environmentName"
$storageAccountName = "st$($COMPANY_NAME.Replace('-', '').ToLowerInvariant())$environmentName"
if ($storageAccountName.Length -gt 24) { $storageAccountName = $storageAccountName.Substring(0, 24) }
$sqlServerName = "db-$COMPANY_NAME-$environmentName-server"
$sqlDatabaseName = "db-$COMPANY_NAME-$environmentName"
$expectedAppConfigEndpoint = "https://$appConfigurationName.azconfig.io"
$expectedVaultPrefix = "https://$keyVaultName.vault.azure.net/secrets/"

$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$Message) { $failures.Add($Message) }
function Get-AppConfigValue([string]$Key) {
    $value = az appconfig kv show --name $appConfigurationName --key $Key --auth-mode login --query value -o tsv 2>$null
    if ($LASTEXITCODE -ne 0) { Fail "Missing/unreadable App Config key: $Key"; return $null }
    return [string]$value
}
function Get-AppSetting([string]$Name) {
    $value = az webapp config appsettings list --resource-group $resourceGroup --name $webAppName --query "[?name=='$Name'].value | [0]" -o tsv 2>$null
    if ($LASTEXITCODE -ne 0) { Fail "Could not read App Service setting: $Name"; return $null }
    return [string]$value
}
function Assert-SecretEnabled([string]$Name) {
    $enabled = az keyvault secret show --vault-name $keyVaultName --name $Name --query attributes.enabled -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or $enabled -ne 'true') { Fail "Key Vault secret missing or disabled: $Name" }
}
function Assert-VersionlessVaultReference([string]$Key, [string]$SecretName) {
    $raw = Get-AppConfigValue $Key
    if ([string]::IsNullOrWhiteSpace($raw)) { return }
    $expected = "$expectedVaultPrefix$SecretName"
    if ($raw -notmatch [regex]::Escape($expected)) { Fail "$Key does not point to expected vault/secret"; return }
    if ($raw -match [regex]::Escape("$expected/")) { Fail "$Key is version-pinned instead of versionless" }
}

$accountJson = az account show --query '{tenantId:tenantId,subscriptionId:id}' -o json 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($accountJson)) { throw 'Azure CLI is not authenticated.' }
$account = $accountJson | ConvertFrom-Json
if ($ExpectedTenantId -and $account.tenantId -ne $ExpectedTenantId) { Fail "Wrong tenant: $($account.tenantId)" }
if ($ExpectedSubscriptionId -and $account.subscriptionId -ne $ExpectedSubscriptionId) { Fail "Wrong subscription: $($account.subscriptionId)" }

$actualEndpoint = Get-AppSetting 'Azure__AppConfiguration__Endpoint'
if ($actualEndpoint -ne $expectedAppConfigEndpoint) { Fail "App Service points to '$actualEndpoint' instead of '$expectedAppConfigEndpoint'" }

$tenantId = Get-AppConfigValue 'Azure:AdOAuth:TenantId'
if ($tenantId -ne $account.tenantId) { Fail 'App Config tenant ID does not match current Azure tenant' }

$storageName = Get-AppConfigValue 'Azure:DocumentFileStorage:StorageAccountName'
if ($storageName -ne $storageAccountName) { Fail "App Config storage account is '$storageName', expected '$storageAccountName'" }

$appConfigManagedIdentity = Get-AppConfigValue 'Azure:ManagedIdentity:ClientId'
$appServiceManagedIdentity = Get-AppSetting 'AZURE_CLIENT_ID'
if ([string]::IsNullOrWhiteSpace($appConfigManagedIdentity) -or $appConfigManagedIdentity -ne $appServiceManagedIdentity) {
    Fail 'Managed identity Client ID differs between App Service and App Configuration'
}

Assert-VersionlessVaultReference 'Jwt:SigningKey' 'Jwt--SigningKey'
Assert-VersionlessVaultReference 'Azure:Sql:ConnectionString' 'Azure--Sql--ConnectionString'
Assert-VersionlessVaultReference 'Azure:Acs:ConnectionString' 'Azure--Acs--ConnectionString'

Assert-SecretEnabled 'Jwt--SigningKey'
Assert-SecretEnabled 'Azure--Sql--AdminPassword'
Assert-SecretEnabled 'Azure--Sql--ConnectionString'
Assert-SecretEnabled 'Azure--Acs--ConnectionString'

$sqlConnection = az keyvault secret show --vault-name $keyVaultName --name 'Azure--Sql--ConnectionString' --query value -o tsv 2>$null
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($sqlConnection)) {
    if ($sqlConnection -notmatch [regex]::Escape("$sqlServerName.database.windows.net")) { Fail 'SQL connection secret points to an unexpected server' }
    if ($sqlConnection -notmatch [regex]::Escape("Initial Catalog=$sqlDatabaseName")) { Fail 'SQL connection secret points to an unexpected database' }
    if ($sqlConnection -match '(?i)Password=') { Fail 'SQL runtime connection unexpectedly contains a password' }
}

$powerBiEnabled = (Get-AppConfigValue 'PowerBiExport:Enabled').ToLowerInvariant()
$expectedPowerBi = if ($ExpectPowerBiEnabled) { 'true' } else { 'false' }
if ($powerBiEnabled -ne $expectedPowerBi) { Fail "Power BI enabled state is '$powerBiEnabled', expected '$expectedPowerBi'" }

if ($failures.Count -gt 0) {
    Write-Host 'POST-DEPLOY CONFIG AUDIT: FAILED' -ForegroundColor Red
    foreach ($failure in $failures) { Write-Host " - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host 'POST-DEPLOY CONFIG AUDIT: PASSED' -ForegroundColor Green
Write-Host "Tenant, App Config, Key Vault references, SQL target, managed identity and Power BI state are consistent for $COMPANY_NAME/$Environment." -ForegroundColor Green
