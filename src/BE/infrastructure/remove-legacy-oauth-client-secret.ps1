param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$StatePath = ''
)

$ErrorActionPreference = 'Stop'
$NormalizedEnvironment = $Environment.ToLowerInvariant()
$LegacyCredentialDisplayName = "workslip-deploy-$NormalizedEnvironment-oauth-client-secret"

if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $PSScriptRoot "entra.$NormalizedEnvironment.local.json"
}

if (-not (Test-Path $StatePath)) {
    throw "Entra state file not found: $StatePath. Run deploy-entra.ps1 first."
}

try {
    $state = Get-Content -Path $StatePath -Raw | ConvertFrom-Json
}
catch {
    throw "Entra state file is not valid JSON: $StatePath`n$($_.Exception.Message)"
}

if ($state.environment -ne $NormalizedEnvironment) {
    throw "Entra state file is for environment '$($state.environment)', not '$NormalizedEnvironment'."
}

$oauthAppObjectId = [string]$state.oauthAppObjectId
if ([string]::IsNullOrWhiteSpace($oauthAppObjectId)) {
    throw "Entra state file is missing oauthAppObjectId: $StatePath"
}

$azureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop |
    Select-Object -First 1
if ($null -eq $azureCliCommand -or [string]::IsNullOrWhiteSpace($azureCliCommand.Source)) {
    throw 'Could not resolve Azure CLI.'
}

$credentialIds = @(
    & $azureCliCommand.Source ad app credential list `
        --id $oauthAppObjectId `
        --query "[?displayName=='$LegacyCredentialDisplayName'].keyId" `
        --only-show-errors `
        -o tsv
)

if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect OAuth application credentials for '$oauthAppObjectId'."
}

$credentialIds = @($credentialIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
if ($credentialIds.Count -eq 0) {
    Write-Host "Legacy OAuth client credential is absent: $LegacyCredentialDisplayName" -ForegroundColor DarkGray
    return
}

foreach ($credentialId in $credentialIds) {
    & $azureCliCommand.Source ad app credential delete `
        --id $oauthAppObjectId `
        --key-id ([string]$credentialId).Trim() `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "Could not delete legacy OAuth client credential '$credentialId'."
    }
}

Write-Host "Removed legacy OAuth client credential: $LegacyCredentialDisplayName" -ForegroundColor Green
