param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$EntraStatePath = ''
)

$ErrorActionPreference = 'Stop'

$EntraScript = Join-Path $PSScriptRoot 'deploy-entra.ps1'
$InfrastructureScript = Join-Path $PSScriptRoot 'deploy-infrastructure.ps1'

foreach ($scriptPath in @($EntraScript, $InfrastructureScript)) {
    if (-not (Test-Path $scriptPath)) {
        throw "Deployment script not found: $scriptPath"
    }
}

Write-Host 'Phase 1/2: reconciling Microsoft Entra applications...' -ForegroundColor Cyan
& $EntraScript `
    -Environment $Environment `
    -StatePath $EntraStatePath

Write-Host 'Phase 2/2: deploying Azure infrastructure...' -ForegroundColor Cyan
& $InfrastructureScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -EntraStatePath $EntraStatePath

Write-Host 'Full deployment completed.' -ForegroundColor Green
