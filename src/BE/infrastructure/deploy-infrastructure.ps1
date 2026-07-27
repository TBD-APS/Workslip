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

$AppConfigurationName = "appcs-$COMPANY_NAME-$NormalizedEnvironment"
$OAuthUniqueName = "workslip-oauth-server-$NormalizedEnvironment"
$ClientUniqueName = "workslip-client-$NormalizedEnvironment"
$GraphRoot = 'https://graph.microsoft.com/v1.0'

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

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        $Path,
        $json,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Invoke-AzureCliCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(
            & $AzureCli @Arguments 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine).Trim()
    }
}

function Get-AppConfigurationValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    $result = Invoke-AzureCliCapture -Arguments @(
        'appconfig', 'kv', 'show',
        '--name', $AppConfigurationName,
        '--key', $Key,
        '--auth-mode', 'login',
        '--query', 'value',
        '--only-show-errors',
        '-o', 'tsv'
    )

    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }

    return $result.Output.Trim()
}

function Get-AzureAdApplicationById {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApplicationId
    )

    $result = Invoke-AzureCliCapture -Arguments @(
        'ad', 'app', 'show',
        '--id', $ApplicationId,
        '--query', '{id:id,appId:appId,displayName:displayName}',
        '--only-show-errors',
        '-o', 'json'
    )

    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }

    try {
        $application = $result.Output | ConvertFrom-Json
    }
    catch {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace([string]$application.id) -or
        [string]::IsNullOrWhiteSpace([string]$application.appId)) {
        return $null
    }

    return $application
}

function Get-GraphApplicationByUniqueName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UniqueName
    )

    $uri = "$GraphRoot/applications(uniqueName='$UniqueName')?`$select=id,appId,displayName"
    $result = Invoke-AzureCliCapture -Arguments @(
        'rest',
        '--method', 'GET',
        '--uri', $uri,
        '--only-show-errors',
        '-o', 'json'
    )

    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }

    try {
        $application = $result.Output | ConvertFrom-Json
    }
    catch {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace([string]$application.id) -or
        [string]::IsNullOrWhiteSpace([string]$application.appId)) {
        return $null
    }

    return $application
}

function New-EntraState {
    param(
        [Parameter(Mandatory = $true)]
        [object]$OAuthApplication,
        [Parameter(Mandatory = $true)]
        [object]$ClientApplication,
        [Parameter(Mandatory = $true)]
        [string]$TenantId,
        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    return [ordered]@{
        environment = $NormalizedEnvironment
        tenantId = $TenantId
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        source = $Source
        oauthClientId = [string]$OAuthApplication.appId
        oauthAppObjectId = [string]$OAuthApplication.id
        clientAppId = [string]$ClientApplication.appId
        clientAppObjectId = [string]$ClientApplication.id
    }
}

function Resolve-ExistingEntraState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TenantId
    )

    Write-Host 'Entra state file not found. Discovering existing registrations without modifying Entra...' -ForegroundColor Cyan

    $oauthClientId = Get-AppConfigurationValue -Key 'Azure:AdOAuth:ClientId'
    $clientAppId = Get-AppConfigurationValue -Key 'Azure:AdOAuth:ClientAppId'

    if (-not [string]::IsNullOrWhiteSpace($oauthClientId) -and
        -not [string]::IsNullOrWhiteSpace($clientAppId)) {
        $oauthApplication = Get-AzureAdApplicationById -ApplicationId $oauthClientId
        $clientApplication = Get-AzureAdApplicationById -ApplicationId $clientAppId

        if ($null -ne $oauthApplication -and $null -ne $clientApplication) {
            Write-Host "Resolved Entra IDs from App Configuration '$AppConfigurationName'." -ForegroundColor DarkGray
            return New-EntraState `
                -OAuthApplication $oauthApplication `
                -ClientApplication $clientApplication `
                -TenantId $TenantId `
                -Source 'app-configuration'
        }
    }

    $oauthApplication = Get-GraphApplicationByUniqueName -UniqueName $OAuthUniqueName
    $clientApplication = Get-GraphApplicationByUniqueName -UniqueName $ClientUniqueName

    if ($null -ne $oauthApplication -and $null -ne $clientApplication) {
        Write-Host 'Resolved Entra IDs from stable Microsoft Graph unique names.' -ForegroundColor DarkGray
        return New-EntraState `
            -OAuthApplication $oauthApplication `
            -ClientApplication $clientApplication `
            -TenantId $TenantId `
            -Source 'graph-unique-name'
    }

    throw @"
Could not resolve the existing OAuth and client app registrations without modifying Entra.
Checked App Configuration '$AppConfigurationName' and stable Graph names '$OAuthUniqueName' / '$ClientUniqueName'.
Run '.\deploy-entra.ps1 $Environment' to create or reconcile the registrations, then retry infrastructure deployment.
"@
}

function Read-EntraState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        return Get-Content -Path $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Entra state file is not valid JSON: $Path`n$($_.Exception.Message)"
    }
}

function Assert-EntraState {
    param(
        [Parameter(Mandatory = $true)]
        [object]$State
    )

    if ($State.environment -ne $NormalizedEnvironment) {
        throw "Entra state file is for environment '$($State.environment)', not '$NormalizedEnvironment'."
    }

    $requiredStateProperties = @(
        'oauthClientId',
        'oauthAppObjectId',
        'clientAppId',
        'clientAppObjectId'
    )

    foreach ($propertyName in $requiredStateProperties) {
        if ([string]::IsNullOrWhiteSpace([string]$State.$propertyName)) {
            throw "Entra state file is missing '$propertyName': $EntraStatePath"
        }
    }
}

try {
    $CurrentTenantId = Ensure-AzureLogin

    if (Test-Path $EntraStatePath) {
        $EntraState = Read-EntraState -Path $EntraStatePath
    }
    else {
        $EntraState = Resolve-ExistingEntraState -TenantId $CurrentTenantId
        Write-Utf8JsonFile -Path $EntraStatePath -Value $EntraState
        Write-Host "Cached read-only Entra state: $EntraStatePath" -ForegroundColor Green
    }

    Assert-EntraState -State $EntraState

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
