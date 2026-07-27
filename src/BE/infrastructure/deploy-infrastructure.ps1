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
$DeployScript = Join-Path $PSScriptRoot 'deploy.ps1'
$ProvisionedValuesPath = Join-Path $PSScriptRoot 'entra-provisioned.json'

if (-not (Test-Path $DeployScript)) {
    throw "Deployment script not found: $DeployScript"
}

$AzureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop | Select-Object -First 1
$AzureCli = $AzureCliCommand.Source
if ([string]::IsNullOrWhiteSpace($AzureCli)) {
    throw 'Could not resolve a single Azure CLI executable path.'
}

$NormalizedEnvironment = $Environment.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($EntraStatePath)) {
    $EntraStatePath = Join-Path $PSScriptRoot "entra.$NormalizedEnvironment.local.json"
}

if (-not (Test-Path $EntraStatePath)) {
    throw "Entra state file not found: $EntraStatePath`nRun '.\deploy-entra.ps1 $Environment' once before deploying infrastructure."
}

try {
    $EntraState = Get-Content -Path $EntraStatePath -Raw | ConvertFrom-Json
}
catch {
    throw "Entra state file is not valid JSON: $EntraStatePath`n$($_.Exception.Message)"
}

if ($EntraState.environment -ne $NormalizedEnvironment) {
    throw "Entra state file is for environment '$($EntraState.environment)', not '$NormalizedEnvironment'."
}

$RequiredStateProperties = @(
    'oauthClientId',
    'oauthAppObjectId',
    'clientAppId',
    'clientAppObjectId'
)

foreach ($propertyName in $RequiredStateProperties) {
    if ([string]::IsNullOrWhiteSpace([string]$EntraState.$propertyName)) {
        throw "Entra state file is missing '$propertyName': $EntraStatePath"
    }
}

$ExpectedVaultName = "kv-$COMPANY_NAME-$NormalizedEnvironment"
$LegacyVaultName = "kv-$COMPANY_NAME$NormalizedEnvironment"
$MalformedVaultName = "kv-$COMPANY_NAME"

$OriginalProvisionedValues = $null
$ProvisionedValuesExisted = Test-Path $ProvisionedValuesPath
if ($ProvisionedValuesExisted) {
    $OriginalProvisionedValues = [System.IO.File]::ReadAllText($ProvisionedValuesPath)
}

function Invoke-AzureCliRaw {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[string]]$CliArguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $AzureCli @CliArguments
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function global:az {
    $cliArguments = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in $args) {
        $value = [string]$argument
        if ($value -eq $LegacyVaultName -or $value -eq $MalformedVaultName) {
            $value = $ExpectedVaultName
        }
        $cliArguments.Add($value)
    }

    $isSecretSet = $cliArguments.Count -ge 3 -and
        $cliArguments[0] -eq 'keyvault' -and
        $cliArguments[1] -eq 'secret' -and
        $cliArguments[2] -eq 'set'
    $valueIndex = $cliArguments.IndexOf('--value')

    if (-not $isSecretSet -or $valueIndex -lt 0) {
        Invoke-AzureCliRaw -CliArguments $cliArguments
        return
    }

    if ($valueIndex + 1 -ge $cliArguments.Count) {
        throw 'Azure CLI Key Vault secret command contained --value without a value.'
    }

    $secretValue = $cliArguments[$valueIndex + 1]
    if ([string]::IsNullOrWhiteSpace($secretValue)) {
        throw 'Refusing to store an empty Key Vault secret.'
    }

    $tempFile = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText(
            $tempFile.FullName,
            $secretValue,
            [System.Text.UTF8Encoding]::new($false)
        )

        $cliArguments.RemoveAt($valueIndex + 1)
        $cliArguments.RemoveAt($valueIndex)
        $cliArguments.Insert($valueIndex, '--file')
        $cliArguments.Insert($valueIndex + 1, $tempFile.FullName)
        $cliArguments.Insert($valueIndex + 2, '--encoding')
        $cliArguments.Insert($valueIndex + 3, 'utf-8')

        Invoke-AzureCliRaw -CliArguments $cliArguments
    }
    finally {
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        $secretValue = $null
    }
}

function Ensure-AzureLogin {
    $tenantId = az account show --query tenantId -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tenantId)) {
        Write-Host 'Azure login required. Starting device login...' -ForegroundColor Cyan
        az login --use-device-code -o none
        if ($LASTEXITCODE -ne 0) {
            throw 'Azure login failed.'
        }

        $tenantId = az account show --query tenantId -o tsv
    }

    return [string]$tenantId
}

function Write-Utf8JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $json = $Value | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        $Path,
        $json,
        [System.Text.UTF8Encoding]::new($false)
    )
}

try {
    $CurrentTenantId = Ensure-AzureLogin
    if (-not [string]::IsNullOrWhiteSpace([string]$EntraState.tenantId) -and
        $EntraState.tenantId -ne $CurrentTenantId) {
        throw "Entra state belongs to tenant '$($EntraState.tenantId)', but Azure CLI is signed into '$CurrentTenantId'."
    }

    $handoff = [ordered]@{
        environment = $NormalizedEnvironment
        oauthClientId = [string]$EntraState.oauthClientId
        oauthAppObjectId = [string]$EntraState.oauthAppObjectId
        clientAppId = [string]$EntraState.clientAppId
        clientAppObjectId = [string]$EntraState.clientAppObjectId
    }

    Write-Utf8JsonFile -Path $ProvisionedValuesPath -Value $handoff

    Write-Host "Using Entra state: $EntraStatePath" -ForegroundColor DarkGray
    & $DeployScript `
        -Environment $Environment `
        -Location $Location `
        -COMPANY_NAME $COMPANY_NAME `
        -GlobalAdminId $GlobalAdminId `
        -VercelToken $VercelToken
}
finally {
    if ($ProvisionedValuesExisted) {
        [System.IO.File]::WriteAllText(
            $ProvisionedValuesPath,
            $OriginalProvisionedValues,
            [System.Text.UTF8Encoding]::new($false)
        )
    }
    else {
        Remove-Item $ProvisionedValuesPath -Force -ErrorAction SilentlyContinue
    }

    Remove-Item Function:\global:az -ErrorAction SilentlyContinue
    Remove-Item Function:\Invoke-AzureCliRaw -ErrorAction SilentlyContinue
}
