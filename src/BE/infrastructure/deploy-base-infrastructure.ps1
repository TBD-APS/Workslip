param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftwareinc',
    [string]$ExpectedTenantId = '',
    [string]$ExpectedSubscriptionId = '',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$Template = Join-Path $PSScriptRoot 'base.bicep'
if (-not (Test-Path $Template)) {
    throw "Base infrastructure template not found: $Template"
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required.'
}

$accountJson = az account show --query '{subscriptionId:id,subscriptionName:name,tenantId:tenantId,user:user.name}' -o json 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($accountJson)) {
    throw 'Azure CLI is not authenticated. Run az login and select the intended tenant/subscription first.'
}

$account = $accountJson | ConvertFrom-Json

if (-not [string]::IsNullOrWhiteSpace($ExpectedTenantId) -and $account.tenantId -ne $ExpectedTenantId) {
    throw "STOP: wrong Azure tenant. Expected '$ExpectedTenantId', current '$($account.tenantId)'."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedSubscriptionId) -and $account.subscriptionId -ne $ExpectedSubscriptionId) {
    throw "STOP: wrong Azure subscription. Expected '$ExpectedSubscriptionId', current '$($account.subscriptionId)'."
}

$normalizedEnvironment = $Environment.ToLowerInvariant()
$resourceGroupName = "rg-$COMPANY_NAME-$normalizedEnvironment"
$deploymentName = "$COMPANY_NAME-$normalizedEnvironment-base-$(Get-Date -Format 'yyyyMMddHHmmss')"

# The base deployment creates an empty SQL database. The transient SQL admin
# password is only needed by ARM to create/update the SQL logical server in this
# isolated stage. The full deployment later reconciles the production SQL secret.
$bytes = New-Object byte[] 36
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($bytes)
}
finally {
    $rng.Dispose()
}
$sqlAdminPassword = "Aa1!$([Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'))"

Write-Host ''
Write-Host 'Workslip base infrastructure deployment' -ForegroundColor Cyan
Write-Host "Tenant:       $($account.tenantId)"
Write-Host "Subscription: $($account.subscriptionName) ($($account.subscriptionId))"
Write-Host "User:         $($account.user)"
Write-Host "Resource grp: $resourceGroupName"
Write-Host "Company:      $COMPANY_NAME"
Write-Host "Environment:  $Environment"
Write-Host "Mode:         $(if ($WhatIf) { 'WHAT-IF (read-only)' } else { 'DEPLOY' })"
Write-Host ''
Write-Host 'Excluded by design: Entra apps, Microsoft Graph, managed identities/RBAC, GitHub OIDC, Power BI, ACS/email, runtime secrets and SQL identity provisioning.' -ForegroundColor Yellow
Write-Host ''

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
    "sqlAdminPassword=$sqlAdminPassword",
    '--only-show-errors'
)

& az @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Base infrastructure $(if ($WhatIf) { 'what-if' } else { 'deployment' }) failed."
}

if ($WhatIf) {
    Write-Host ''
    Write-Host 'Base infrastructure what-if completed. No Azure resources were changed.' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host 'Base infrastructure deployment completed.' -ForegroundColor Green
Write-Host "Resource group: $resourceGroupName"
Write-Host 'No Entra, GitHub or identity cutover has been performed.' -ForegroundColor Green
