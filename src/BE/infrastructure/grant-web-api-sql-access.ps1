param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,
    [Parameter(Mandatory = $true)]
    [string]$CompanyName,
    [Parameter(Mandatory = $true)]
    [string]$SqlAdminPassword
)

$ErrorActionPreference = 'Stop'
$InternalScript = Join-Path (Join-Path $PSScriptRoot 'internal') 'grant-web-api-sql-access.ps1'

if (-not (Test-Path $InternalScript)) {
    throw "Internal SQL provisioning script not found: $InternalScript"
}

& $InternalScript `
    -Environment $Environment `
    -CompanyName $CompanyName `
    -SqlAdminPassword $SqlAdminPassword
