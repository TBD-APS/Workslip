param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$PowerBiReaderPrincipalId = '',
    [string]$PowerBiReaderEmail = '',
    [switch]$EnablePowerBiExport,
    [string]$EntraStatePath = ''
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

$gh = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $gh) {
    throw 'GitHub CLI is required because deploy.ps1 reconciles the GitHub infrastructure OIDC identity and environment variable. Install gh and retry.'
}

& $gh.Source auth status --hostname github.com 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated. Run gh auth login, then retry deploy.ps1.'
}

Write-Host 'Phase 1/4: reconciling Microsoft Entra applications...' -ForegroundColor Cyan
& $EntraScript `
    -Environment $Environment `
    -StatePath $EntraStatePath

Write-Host 'Phase 2/4: deploying Azure infrastructure...' -ForegroundColor Cyan
& $InfrastructureScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -PowerBiReaderPrincipalId $PowerBiReaderPrincipalId `
    -PowerBiReaderEmail $PowerBiReaderEmail `
    -EnablePowerBiExport:$EnablePowerBiExport `
    -EntraStatePath $EntraStatePath

Write-Host 'Phase 3/4: reconciling VAPID secret lifecycle...' -ForegroundColor Cyan
& $VapidSecretScript `
    -Environment $Environment `
    -CompanyName $COMPANY_NAME

Write-Host 'Phase 4/4: reconciling GitHub infrastructure OIDC identity...' -ForegroundColor Cyan
& $GitHubInfrastructureIdentityScript `
    -Environment $Environment `
    -Location $Location `
    -CompanyName $COMPANY_NAME `
    -GitHubEnvironment $Environment `
    -ConfigureGitHubEnvironment

Write-Host 'Full deployment completed.' -ForegroundColor Green
