param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$EntraStatePath = ''
)

$ErrorActionPreference = 'Stop'
$InternalScriptDirectory = Join-Path $PSScriptRoot 'internal'
$InfrastructureScript = Join-Path $InternalScriptDirectory 'deploy-infrastructure-core.ps1'

if (-not (Test-Path $InfrastructureScript)) {
    throw "Internal infrastructure deployment script not found: $InfrastructureScript"
}

& $InfrastructureScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -EntraStatePath $EntraStatePath
