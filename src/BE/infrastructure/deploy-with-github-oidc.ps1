param(
    [Parameter(Position=0)]
    [string]$Environment = "prod",
    [string]$Location = "westeurope",
    [string]$COMPANY_NAME = "npteknik",
    [string]$GlobalAdminId = "9ea4bcd3-bf90-4249-93e0-f45070d140f7",
    [string]$VercelToken = ""
)

$ErrorActionPreference = "Stop"
$InfrastructureDirectory = Split-Path -Parent $PSCommandPath
$BaseDeploymentScript = Join-Path $InfrastructureDirectory "deploy.ps1"
$OidcTemplate = Join-Path $InfrastructureDirectory "github-oidc-immutable.bicep"
$ResourceGroup = "rg-$COMPANY_NAME-$Environment"
$OidcDeploymentName = "$COMPANY_NAME-$Environment-github-oidc-$(Get-Date -Format 'yyyyMMddHHmmss')"

if (-not (Test-Path $BaseDeploymentScript)) {
    throw "Base deployment script not found at $BaseDeploymentScript"
}

if (-not (Test-Path $OidcTemplate)) {
    throw "GitHub OIDC template not found at $OidcTemplate"
}

& $BaseDeploymentScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -VercelToken $VercelToken

if (-not $?) {
    throw "Base infrastructure deployment failed."
}

Write-Host "Deploying immutable GitHub OIDC credential..." -ForegroundColor Cyan
$OidcDeploymentJson = az deployment group create `
    --resource-group $ResourceGroup `
    --name $OidcDeploymentName `
    --mode Incremental `
    --template-file $OidcTemplate `
    --parameters companyName=$COMPANY_NAME `
    --parameters environment=$Environment `
    -o json

if ($LASTEXITCODE -ne 0 -or -not $OidcDeploymentJson) {
    throw "Immutable GitHub OIDC credential deployment failed."
}

$OidcDeployment = $OidcDeploymentJson | ConvertFrom-Json
$Subject = $OidcDeployment.properties.outputs.GITHUB_IMMUTABLE_FEDERATED_CREDENTIAL_SUBJECT.value

if ([string]::IsNullOrWhiteSpace($Subject)) {
    throw "OIDC deployment output subject was empty."
}

Write-Host "GitHub OIDC subject: $Subject" -ForegroundColor Green
Write-Host "Infrastructure and GitHub OIDC deployment complete." -ForegroundColor Green
