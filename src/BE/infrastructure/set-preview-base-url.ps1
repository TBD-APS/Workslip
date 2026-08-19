param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$COMPANY_NAME = 'mrsoftwareinc',
    [string]$PublicBaseUrl = '',
    [string]$ExpectedTenantId = '',
    [string]$ExpectedSubscriptionId = ''
)

$ErrorActionPreference = 'Stop'

$environmentName = $Environment.ToLowerInvariant()
$resourceGroup = "rg-$COMPANY_NAME-$environmentName"
$appConfigurationName = "appcs-$COMPANY_NAME-$environmentName"
$webAppName = "api-$COMPANY_NAME-$environmentName"

$account = az account show --query '{tenantId:tenantId,subscriptionId:id}' -o json 2>$null | ConvertFrom-Json
if ($null -eq $account) {
    throw 'Azure CLI is not authenticated.'
}
if ($ExpectedTenantId -and $account.tenantId -ne $ExpectedTenantId) {
    throw "STOP: wrong tenant. Expected '$ExpectedTenantId', current '$($account.tenantId)'."
}
if ($ExpectedSubscriptionId -and $account.subscriptionId -ne $ExpectedSubscriptionId) {
    throw "STOP: wrong subscription. Expected '$ExpectedSubscriptionId', current '$($account.subscriptionId)'."
}

if ([string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
    $defaultHostName = az webapp show `
        --resource-group $resourceGroup `
        --name $webAppName `
        --query defaultHostName `
        -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($defaultHostName)) {
        throw "Could not resolve Azure default hostname for '$webAppName'."
    }
    $PublicBaseUrl = "https://$defaultHostName"
}

$PublicBaseUrl = $PublicBaseUrl.TrimEnd('/')

$values = [ordered]@{
    'Azure:Domain:BaseUrl' = $PublicBaseUrl
    'Cors:AllowedOrigins:0' = $PublicBaseUrl
    'Azure:Acs:InviteBaseUrl' = "$PublicBaseUrl/invite"
}

Write-Host "Setting migration preview base URL to $PublicBaseUrl" -ForegroundColor Cyan
foreach ($entry in $values.GetEnumerator()) {
    az appconfig kv set `
        --name $appConfigurationName `
        --key $entry.Key `
        --value $entry.Value `
        --auth-mode login `
        --yes `
        --only-show-errors `
        -o none
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set App Configuration key '$($entry.Key)'."
    }
}

Write-Host 'Preview URL configuration updated.' -ForegroundColor Green
Write-Host 'Run the normal full deployment again at final DNS cutover to restore the production app.mrsoftware.dk defaults.' -ForegroundColor Yellow
