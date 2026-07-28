param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [Nullable[bool]]$ActivateCustomEmailDomain = $null,
    [string]$EntraStatePath = ''
)

$ErrorActionPreference = 'Stop'
$SafeDeployScript = Join-Path $PSScriptRoot 'deploy-safe.ps1'

if (-not (Test-Path $SafeDeployScript)) {
    throw "Deployment orchestrator not found: $SafeDeployScript"
}

Write-Warning 'deploy.ps1 is a compatibility entry point. Use deploy-entra.ps1 and deploy-infrastructure.ps1 directly, or deploy-safe.ps1 for both phases.'

& $SafeDeployScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -ActivateCustomEmailDomain $ActivateCustomEmailDomain `
    -EntraStatePath $EntraStatePath
