param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$PowerBiReaderPrincipalId = '',
    [string]$PowerBiReaderEmail = '',
    [switch]$EnablePowerBiExport,
    [string]$EntraStatePath = '',
    # Preview all four phases without changing anything. Each phase reports what it
    # would do and returns; nothing is written to Entra, Azure, Key Vault, SQL or
    # GitHub. Use plan.ps1 when the run must not be able to mutate at all.
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$EntraScript = Join-Path $PSScriptRoot 'deploy-entra.ps1'
$InfrastructureScript = Join-Path $PSScriptRoot 'deploy-infrastructure.ps1'
$VapidSecretScript = Join-Path $PSScriptRoot 'reconcile-vapid-secret.ps1'
$GitHubInfrastructureIdentityScript = Join-Path $PSScriptRoot 'bootstrap-github-infrastructure-identity.ps1'

foreach ($scriptPath in @(
    $EntraScript,
    $InfrastructureScript,
    $VapidSecretScript,
    $GitHubInfrastructureIdentityScript
)) {
    if (-not (Test-Path $scriptPath)) {
        throw "Deployment script not found: $scriptPath"
    }
}

# A preview never writes the GitHub environment variable, so it does not need the
# GitHub CLI. Requiring it would block previewing from a machine that only has Azure.
if (-not $WhatIf) {
    $gh = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $gh) {
        throw 'GitHub CLI is required because deploy.ps1 reconciles the GitHub infrastructure OIDC identity and environment variable. Install gh and retry.'
    }

    & $gh.Source auth status --hostname github.com 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub CLI is not authenticated. Run gh auth login, then retry deploy.ps1.'
    }
}

$phaseVerb = if ($WhatIf) { 'previewing' } else { 'reconciling' }

if ($WhatIf) {
    Write-Host ''
    Write-Host 'PREVIEW — no phase will change anything.' -ForegroundColor Yellow
    Write-Host ''
}

Write-Host "Phase 1/4: $phaseVerb Microsoft Entra applications..." -ForegroundColor Cyan
& $EntraScript `
    -Environment $Environment `
    -StatePath $EntraStatePath `
    -WhatIf:$WhatIf

Write-Host "Phase 2/4: $(if ($WhatIf) { 'previewing' } else { 'deploying' }) Azure infrastructure..." -ForegroundColor Cyan
& $InfrastructureScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -PowerBiReaderPrincipalId $PowerBiReaderPrincipalId `
    -PowerBiReaderEmail $PowerBiReaderEmail `
    -EnablePowerBiExport:$EnablePowerBiExport `
    -EntraStatePath $EntraStatePath `
    -WhatIf:$WhatIf

Write-Host "Phase 3/4: $phaseVerb VAPID secret lifecycle..." -ForegroundColor Cyan
& $VapidSecretScript `
    -Environment $Environment `
    -CompanyName $COMPANY_NAME `
    -WhatIf:$WhatIf

Write-Host "Phase 4/4: $phaseVerb GitHub infrastructure OIDC identity..." -ForegroundColor Cyan
& $GitHubInfrastructureIdentityScript `
    -Environment $Environment `
    -Location $Location `
    -CompanyName $COMPANY_NAME `
    -GitHubEnvironment $Environment `
    -ConfigureGitHubEnvironment:(-not $WhatIf) `
    -WhatIf:$WhatIf

if ($WhatIf) {
    Write-Host ''
    Write-Host 'Preview complete across all four phases. Nothing was changed.' -ForegroundColor Green
    Write-Host 'Phase 2 previewed against the Entra registrations that exist now, so its diff will' -ForegroundColor DarkGray
    Write-Host 'differ once phase 1 has actually run in a tenant that has none yet.' -ForegroundColor DarkGray
    return
}

Write-Host 'Full deployment completed.' -ForegroundColor Green
