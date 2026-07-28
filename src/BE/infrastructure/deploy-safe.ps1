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

$EntraScript = Join-Path $PSScriptRoot 'deploy-entra.ps1'
$CredentialCleanupScript = Join-Path $PSScriptRoot 'remove-legacy-oauth-client-secret.ps1'
$InfrastructureScript = Join-Path $PSScriptRoot 'deploy-infrastructure.ps1'

foreach ($scriptPath in @($EntraScript, $CredentialCleanupScript, $InfrastructureScript)) {
    if (-not (Test-Path $scriptPath)) {
        throw "Deployment script not found: $scriptPath"
    }
}

& $EntraScript `
    -Environment $Environment `
    -StatePath $EntraStatePath

& $CredentialCleanupScript `
    -Environment $Environment `
    -StatePath $EntraStatePath

& $InfrastructureScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -ActivateCustomEmailDomain $ActivateCustomEmailDomain `
    -EntraStatePath $EntraStatePath
