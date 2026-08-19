param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftwareinc',
    [string]$ExpectedTenantId = '',
    [string]$ExpectedSubscriptionId = '',
    [switch]$DeploySql,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$Template = Join-Path $PSScriptRoot 'base.bicep'
if (-not (Test-Path $Template)) { throw "Base infrastructure template not found: $Template" }
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI is required.' }

$accountJson = az account show --query '{subscriptionId:id,subscriptionName:name,tenantId:tenantId,user:user.name}' -o json 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($accountJson)) { throw 'Azure CLI is not authenticated.' }
$account = $accountJson | ConvertFrom-Json

if ($ExpectedTenantId -and $account.tenantId -ne $ExpectedTenantId) { throw "STOP: wrong Azure tenant." }
if ($ExpectedSubscriptionId -and $account.subscriptionId -ne $ExpectedSubscriptionId) { throw "STOP: wrong Azure subscription." }

$normalizedEnvironment = $Environment.ToLowerInvariant()
$deploymentName = "$COMPANY_NAME-$normalizedEnvironment-base-$(Get-Date -Format 'yyyyMMddHHmmss')"
$sqlAdminPassword = ''

if ($DeploySql) {
    $sqlAdminPassword = $env:WORKSLIP_SQL_ADMIN_PASSWORD
    if ([string]::IsNullOrWhiteSpace($sqlAdminPassword)) {
        if (-not $WhatIf) { throw 'Set WORKSLIP_SQL_ADMIN_PASSWORD before deploying SQL.' }
        $bytes = New-Object byte[] 36
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        $sqlAdminPassword = "Aa1!$([Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'))"
    }
}

$arguments = @(
    'deployment', 'sub',
    $(if ($WhatIf) { 'what-if' } else { 'create' }),
    '--location', $Location,
    '--name', $deploymentName,
    '--template-file', $Template,
    '--parameters',
    "companyName=$COMPANY_NAME",
    "environment=$Environment",
    "location=$Location",
    "deploySql=$($DeploySql.IsPresent.ToString().ToLowerInvariant())",
    "sqlAdminPassword=$sqlAdminPassword",
    '--only-show-errors'
)

& az @arguments
if ($LASTEXITCODE -ne 0) { throw 'Base infrastructure deployment failed.' }
