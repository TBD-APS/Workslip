param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$VercelToken = '',
    [string]$EntraStatePath = ''
)

$ErrorActionPreference = 'Stop'

$EntraScript = Join-Path $PSScriptRoot 'deploy-entra.ps1'
$InfrastructureScript = Join-Path $PSScriptRoot 'deploy-infrastructure.ps1'

if (-not (Test-Path $EntraScript)) {
    throw "Entra deployment script not found: $EntraScript"
}

if (-not (Test-Path $InfrastructureScript)) {
    throw "Infrastructure deployment script not found: $InfrastructureScript"
}

& $EntraScript `
    -Environment $Environment `
    -StatePath $EntraStatePath

& $InfrastructureScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -VercelToken $VercelToken `
    -EntraStatePath $EntraStatePath
