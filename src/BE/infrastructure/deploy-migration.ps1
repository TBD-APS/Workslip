param(
    [Parameter(Mandatory = $true)][string]$CompanyName,
    [Parameter(Mandatory = $true)][string]$Environment,
    [Parameter(Mandatory = $true)][string]$Location,
    [Parameter(Mandatory = $true)][string]$ExpectedTenantId,
    [Parameter(Mandatory = $true)][string]$ExpectedSubscriptionId,
    [switch]$DeploySql,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$account = az account show --query '{tenantId:tenantId,subscriptionId:id}' -o json | ConvertFrom-Json
if ($account.tenantId -ne $ExpectedTenantId) { throw "Wrong tenant. Expected $ExpectedTenantId, got $($account.tenantId)." }
if ($account.subscriptionId -ne $ExpectedSubscriptionId) { throw "Wrong subscription. Expected $ExpectedSubscriptionId, got $($account.subscriptionId)." }

$envName = $Environment.ToLowerInvariant()
$resourceGroup = "rg-$CompanyName-$envName"
$template = Join-Path $PSScriptRoot 'migration.bicep'

Write-Host "Company:      $CompanyName"
Write-Host "Environment:  $envName"
Write-Host "Location:     $Location"
Write-Host "ResourceGroup:$resourceGroup"
Write-Host "Deploy SQL:   $($DeploySql.IsPresent)"
Write-Host "Mode:         $(if ($WhatIf) { 'WHAT-IF' } else { 'DEPLOY' })"

if (-not $WhatIf) {
    az group create --name $resourceGroup --location $Location --only-show-errors -o none
    if ($LASTEXITCODE -ne 0) { throw 'Resource group creation failed.' }
}
else {
    $rgExists = az group exists --name $resourceGroup | ConvertFrom-Json
    if (-not $rgExists) {
        Write-Host "Resource group '$resourceGroup' does not exist yet. Creating it is the only step omitted from what-if." -ForegroundColor Yellow
        exit 0
    }
}

$sqlPassword = ''
if ($DeploySql) {
    $sqlPassword = $env:WORKSLIP_SQL_ADMIN_PASSWORD
    if ([string]::IsNullOrWhiteSpace($sqlPassword)) {
        throw 'Set WORKSLIP_SQL_ADMIN_PASSWORD before using -DeploySql.'
    }
}

$action = if ($WhatIf) { 'what-if' } else { 'create' }
az deployment group $action `
    --resource-group $resourceGroup `
    --name "$CompanyName-$envName-migration" `
    --template-file $template `
    --parameters companyName=$CompanyName environment=$envName location=$Location deploySql=$($DeploySql.IsPresent.ToString().ToLowerInvariant()) sqlAdminPassword=$sqlPassword `
    --only-show-errors

if ($LASTEXITCODE -ne 0) { throw 'Migration deployment failed.' }
