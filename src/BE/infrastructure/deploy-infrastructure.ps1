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

$NormalizedEnvironment = $Environment.ToLowerInvariant()
$ResourceGroup = "rg-$COMPANY_NAME-$NormalizedEnvironment"
$AppConfigurationName = "appcs-$COMPANY_NAME-$NormalizedEnvironment"
$KeyVaultName = "kv-$COMPANY_NAME-$NormalizedEnvironment"
$SqlServerName = "db-$COMPANY_NAME-$NormalizedEnvironment-server"
$SqlDatabaseName = "db-$COMPANY_NAME-$NormalizedEnvironment"
$Template = Join-Path $PSScriptRoot 'main.bicep'
$SqlAccessScript = Join-Path $PSScriptRoot 'grant-web-api-sql-access.ps1'
$ProvisionedValuesPath = Join-Path $PSScriptRoot 'entra-provisioned.json'
$GraphRoot = 'https://graph.microsoft.com/v1.0'
$OAuthUniqueName = "workslip-oauth-server-$NormalizedEnvironment"
$ClientUniqueName = "workslip-client-$NormalizedEnvironment"
$SqlAdminPasswordSecretName = 'Azure--Sql--AdminPassword'
$JwtSigningKeySecretName = 'Jwt--SigningKey'
$SqlConnectionSecretName = 'Azure--Sql--ConnectionString'
$LegacyOAuthClientSecretName = 'Azure--AdOAuth--ClientSecret'

if ([string]::IsNullOrWhiteSpace($EntraStatePath)) {
    $EntraStatePath = Join-Path $PSScriptRoot "entra.$NormalizedEnvironment.local.json"
}

if (-not (Test-Path $Template)) {
    throw "Bicep template not found: $Template"
}

if (-not (Test-Path $SqlAccessScript)) {
    throw "SQL access provisioning script not found: $SqlAccessScript"
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required to provision the API managed identity in Azure SQL.'
}

$OriginalProvisionedValues = $null
$ProvisionedValuesExisted = Test-Path $ProvisionedValuesPath
if ($ProvisionedValuesExisted) {
    $OriginalProvisionedValues = [System.IO.File]::ReadAllText($ProvisionedValuesPath)
}

function Initialize-AzureCliInvocation {
    $azureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop |
        Select-Object -First 1

    if ($null -eq $azureCliCommand -or [string]::IsNullOrWhiteSpace($azureCliCommand.Source)) {
        throw 'Could not resolve Azure CLI.'
    }

    $script:AzureCliExecutable = $azureCliCommand.Source
    $script:AzureCliPrefix = @()

    $isWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    if (-not $isWindows) {
        return
    }

    $azureCliDirectory = Split-Path -Parent $azureCliCommand.Source
    $pythonCandidate = [System.IO.Path]::GetFullPath(
        (Join-Path $azureCliDirectory '..\python.exe')
    )

    if (-not (Test-Path $pythonCandidate)) {
        throw "Azure CLI Python runtime not found: $pythonCandidate"
    }

    $script:AzureCliExecutable = $pythonCandidate
    $script:AzureCliPrefix = @('-IBm', 'azure.cli')
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $commandArguments = @($script:AzureCliPrefix) + @($Arguments)
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        $output = @(
            & $script:AzureCliExecutable @commandArguments 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ([string]::IsNullOrWhiteSpace($text)) {
            $text = 'Azure CLI returned no diagnostic output.'
        }

        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Ensure-AzureLogin {
    $accountResult = Invoke-AzureCli `
        -Arguments @(
            'account', 'show',
            '--query', '{subscriptionId:id,tenantId:tenantId}',
            '-o', 'json'
        ) `
        -AllowFailure

    if ($accountResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($accountResult.Output)) {
        Write-Host 'Azure login required. Starting device login...' -ForegroundColor Cyan
        Invoke-AzureCli -Arguments @('login', '--use-device-code', '-o', 'none') | Out-Null
        $accountResult = Invoke-AzureCli `
            -Arguments @(
                'account', 'show',
                '--query', '{subscriptionId:id,tenantId:tenantId}',
                '-o', 'json'
            )
    }

    return $accountResult.Output | ConvertFrom-Json
}

function Write-Utf8JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Value,
        [int]$Depth = 30
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth $Depth
    [System.IO.File]::WriteAllText(
        $Path,
        $json,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function New-CryptographicSecret {
    param([int]$ByteLength = 64)

    $bytes = New-Object byte[] $ByteLength
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-SqlAdminPassword {
    return "Aa1!$(New-CryptographicSecret -ByteLength 30)"
}

function Get-AppConfigurationValue {
    param([Parameter(Mandatory = $true)][string]$Key)

    $result = Invoke-AzureCli `
        -Arguments @(
            'appconfig', 'kv', 'show',
            '--name', $AppConfigurationName,
            '--key', $Key,
            '--auth-mode', 'login',
            '--query', 'value',
            '--only-show-errors',
            '-o', 'tsv'
        ) `
        -AllowFailure

    if ($result.ExitCode -ne 0) {
        if ($result.Output -match '(?i)(KeyNotFound|ResourceNotFound|not found|does not exist|404)') {
            return $null
        }

        throw "Could not read App Configuration key '$Key'.`n$($result.Output)"
    }

    if ([string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }

    return $result.Output.Trim()
}

function Get-KeyVaultSecretValue {
    param([Parameter(Mandatory = $true)][string]$SecretName)

    $result = Invoke-AzureCli `
        -Arguments @(
            'keyvault', 'secret', 'show',
            '--vault-name', $KeyVaultName,
            '--name', $SecretName,
            '--query', 'value',
            '--only-show-errors',
            '-o', 'tsv'
        ) `
        -AllowFailure

    if ($result.ExitCode -ne 0) {
        if ($result.Output -match '(?i)(SecretNotFound|VaultNotFound|ResourceNotFound|not found|does not exist|404)') {
            return $null
        }

        throw "Could not read Key Vault secret '$SecretName'.`n$($result.Output)"
    }

    if ([string]::IsNullOrWhiteSpace($result.Output)) {
        return $null
    }

    return $result.Output
}

function Set-KeyVaultSecretFromMemory {
    param(
        [Parameter(Mandatory = $true)][string]$SecretName,
        [Parameter(Mandatory = $true)][string]$SecretValue
    )

    if ([string]::IsNullOrWhiteSpace($SecretValue)) {
        throw "Refusing to write an empty Key Vault secret '$SecretName'."
    }

    $tempFile = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText(
            $tempFile.FullName,
            $SecretValue,
            [System.Text.UTF8Encoding]::new($false)
        )

        for ($attempt = 1; $attempt -le 12; $attempt++) {
            $result = Invoke-AzureCli `
                -Arguments @(
                    'keyvault', 'secret', 'set',
                    '--vault-name', $KeyVaultName,
                    '--name', $SecretName,
                    '--file', $tempFile.FullName,
                    '--encoding', 'utf-8',
                    '--only-show-errors',
                    '-o', 'none'
                ) `
                -AllowFailure

            if ($result.ExitCode -eq 0) {
                return
            }

            if ($attempt -lt 12) {
                Start-Sleep -Seconds 10
            }
        }

        throw "Could not store Key Vault secret '$SecretName' after waiting for RBAC propagation.`n$($result.Output)"
    }
    finally {
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    }
}

function Set-AppConfigurationKeyVaultReference {
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$SecretName
    )

    $secretIdentifier = "https://$KeyVaultName.vault.azure.net/secrets/$SecretName"

    for ($attempt = 1; $attempt -le 12; $attempt++) {
        $result = Invoke-AzureCli `
            -Arguments @(
                'appconfig', 'kv', 'set-keyvault',
                '--name', $AppConfigurationName,
                '--key', $Key,
                '--secret-identifier', $secretIdentifier,
                '--auth-mode', 'login',
                '--yes',
                '--only-show-errors',
                '-o', 'none'
            ) `
            -AllowFailure

        if ($result.ExitCode -eq 0) {
            return
        }

        if ($attempt -lt 12) {
            Start-Sleep -Seconds 10
        }
    }

    throw "Could not set Key Vault reference '$Key' after waiting for RBAC propagation.`n$($result.Output)"
}

function Remove-AppConfigurationKeyIfPresent {
    param([Parameter(Mandatory = $true)][string]$Key)

    $result = Invoke-AzureCli `
        -Arguments @(
            'appconfig', 'kv', 'delete',
            '--name', $AppConfigurationName,
            '--key', $Key,
            '--auth-mode', 'login',
            '--yes',
            '--only-show-errors',
            '-o', 'none'
        ) `
        -AllowFailure

    if ($result.ExitCode -ne 0 -and $result.Output -notmatch '(?i)(KeyNotFound|ResourceNotFound|not found|does not exist|404)') {
        throw "Could not remove obsolete App Configuration key '$Key'.`n$($result.Output)"
    }
}

function Disable-KeyVaultSecretIfPresent {
    param([Parameter(Mandatory = $true)][string]$SecretName)

    $result = Invoke-AzureCli `
        -Arguments @(
            'keyvault', 'secret', 'set-attributes',
            '--vault-name', $KeyVaultName,
            '--name', $SecretName,
            '--enabled', 'false',
            '--only-show-errors',
            '-o', 'none'
        ) `
        -AllowFailure

    if ($result.ExitCode -ne 0 -and $result.Output -notmatch '(?i)(SecretNotFound|VaultNotFound|ResourceNotFound|not found|does not exist|404)') {
        throw "Could not disable obsolete Key Vault secret '$SecretName'.`n$($result.Output)"
    }
}

function Get-AzureAdApplicationById {
    param([Parameter(Mandatory = $true)][string]$ApplicationId)

    $result = Invoke-AzureCli `
        -Arguments @(
            'ad', 'app', 'show',
            '--id', $ApplicationId,
            '--query', '{id:id,appId:appId,displayName:displayName}',
            '--only-show-errors',
            '-o', 'json'
        ) `
        -AllowFailure

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
    param([Parameter(Mandatory = $true)][string]$UniqueName)

    $uri = "$GraphRoot/applications(uniqueName='$UniqueName')?`$select=id,appId,displayName"
    $result = Invoke-AzureCli `
        -Arguments @(
            'rest',
            '--method', 'GET',
            '--uri', $uri,
            '--only-show-errors',
            '-o', 'json'
        ) `
        -AllowFailure

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
        [Parameter(Mandatory = $true)][object]$OAuthApplication,
        [Parameter(Mandatory = $true)][object]$ClientApplication,
        [Parameter(Mandatory = $true)][string]$TenantId,
        [Parameter(Mandatory = $true)][string]$Source
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
    param([Parameter(Mandatory = $true)][string]$TenantId)

    Write-Host 'Entra state file not found. Discovering existing registrations without modifying Entra...' -ForegroundColor Cyan

    $oauthClientId = Get-AppConfigurationValue -Key 'Azure:AdOAuth:ClientId'
    $clientAppId = Get-AppConfigurationValue -Key 'Azure:AdOAuth:ClientAppId'

    if (-not [string]::IsNullOrWhiteSpace($oauthClientId) -and
        -not [string]::IsNullOrWhiteSpace($clientAppId)) {
        $oauthApplication = Get-AzureAdApplicationById -ApplicationId $oauthClientId
        $clientApplication = Get-AzureAdApplicationById -ApplicationId $clientAppId

        if ($null -ne $oauthApplication -and $null -ne $clientApplication) {
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
        return New-EntraState `
            -OAuthApplication $oauthApplication `
            -ClientApplication $clientApplication `
            -TenantId $TenantId `
            -Source 'graph-unique-name'
    }

    throw @"
Could not resolve the existing OAuth and client app registrations without modifying Entra.
Run '.\deploy-entra.ps1 $Environment' to reconcile the registrations, then retry infrastructure deployment.
"@
}

function Read-EntraState {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        return Get-Content -Path $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Entra state file is not valid JSON: $Path`n$($_.Exception.Message)"
    }
}

function Assert-EntraState {
    param([Parameter(Mandatory = $true)][object]$State)

    if ($State.environment -ne $NormalizedEnvironment) {
        throw "Entra state file is for environment '$($State.environment)', not '$NormalizedEnvironment'."
    }

    foreach ($propertyName in @('oauthClientId', 'oauthAppObjectId', 'clientAppId', 'clientAppObjectId')) {
        if ([string]::IsNullOrWhiteSpace([string]$State.$propertyName)) {
            throw "Entra state file is missing '$propertyName': $EntraStatePath"
        }
    }
}

function Ensure-ResourceProviders {
    foreach ($provider in @(
        'Microsoft.Web',
        'Microsoft.Storage',
        'Microsoft.OperationalInsights',
        'Microsoft.Insights',
        'Microsoft.KeyVault',
        'Microsoft.AppConfiguration',
        'Microsoft.Sql',
        'Microsoft.ManagedIdentity',
        'Microsoft.Communication',
        'Microsoft.Resources'
    )) {
        $state = Invoke-AzureCli `
            -Arguments @('provider', 'show', '--namespace', $provider, '--query', 'registrationState', '-o', 'tsv') `
            -AllowFailure

        if ($state.ExitCode -ne 0 -or $state.Output -ne 'Registered') {
            Write-Host "Registering resource provider $provider..." -ForegroundColor DarkGray
            Invoke-AzureCli -Arguments @('provider', 'register', '--namespace', $provider, '--wait', '-o', 'none') | Out-Null
        }
    }
}

function Wait-GraphDirectoryObject {
    param(
        [Parameter(Mandatory = $true)][string]$ObjectId,
        [Parameter(Mandatory = $true)][string]$Description
    )

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $result = Invoke-AzureCli `
            -Arguments @('rest', '--method', 'GET', '--uri', "$GraphRoot/directoryObjects/$ObjectId", '-o', 'none') `
            -AllowFailure

        if ($result.ExitCode -eq 0) {
            return
        }

        if ($attempt -lt 30) {
            Start-Sleep -Seconds ([Math]::Min($attempt * 5, 30))
        }
    }

    throw "Timed out waiting for Microsoft Graph object '$Description' ($ObjectId)."
}

function Add-GraphGroupMember {
    param(
        [Parameter(Mandatory = $true)][string]$GroupId,
        [Parameter(Mandatory = $true)][string]$MemberId,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Wait-GraphDirectoryObject -ObjectId $GroupId -Description 'SQL admin group'
    Wait-GraphDirectoryObject -ObjectId $MemberId -Description $Description

    $membersResult = Invoke-AzureCli `
        -Arguments @(
            'rest',
            '--method', 'GET',
            '--uri', "$GraphRoot/groups/$GroupId/members?`$select=id",
            '--query', "value[?id=='$MemberId'].id | [0]",
            '-o', 'tsv'
        )

    if ($membersResult.Output -eq $MemberId) {
        Write-Host "SQL admin group member already exists: $Description" -ForegroundColor DarkGray
        return
    }

    $bodyFile = New-TemporaryFile
    try {
        Write-Utf8JsonFile -Path $bodyFile.FullName -Value ([ordered]@{
            '@odata.id' = "$GraphRoot/directoryObjects/$MemberId"
        })

        Invoke-AzureCli -Arguments @(
            'rest',
            '--method', 'POST',
            '--uri', "$GraphRoot/groups/$GroupId/members/`$ref",
            '--headers', 'Content-Type=application/json',
            '--body', "@$($bodyFile.FullName)",
            '-o', 'none'
        ) | Out-Null
    }
    finally {
        Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
    }
}

Initialize-AzureCliInvocation
$parameterFile = New-TemporaryFile

try {
    $account = Ensure-AzureLogin
    Ensure-ResourceProviders

    $groupExists = Invoke-AzureCli `
        -Arguments @('group', 'exists', '--name', $ResourceGroup, '-o', 'tsv')
    if ($groupExists.Output -ne 'true') {
        Invoke-AzureCli -Arguments @('group', 'create', '--name', $ResourceGroup, '--location', $Location, '-o', 'none') | Out-Null
    }

    if (Test-Path $EntraStatePath) {
        $entraState = Read-EntraState -Path $EntraStatePath
    }
    else {
        $entraState = Resolve-ExistingEntraState -TenantId ([string]$account.tenantId)
        Write-Utf8JsonFile -Path $EntraStatePath -Value $entraState
        Write-Host "Cached read-only Entra state: $EntraStatePath" -ForegroundColor Green
    }

    Assert-EntraState -State $entraState
    if (-not [string]::IsNullOrWhiteSpace([string]$entraState.tenantId) -and
        $entraState.tenantId -ne [string]$account.tenantId) {
        throw "Entra state belongs to tenant '$($entraState.tenantId)', but Azure CLI is signed into '$($account.tenantId)'."
    }

    $handoff = [ordered]@{
        environment = $NormalizedEnvironment
        oauthClientId = [string]$entraState.oauthClientId
        oauthAppObjectId = [string]$entraState.oauthAppObjectId
        clientAppId = [string]$entraState.clientAppId
        clientAppObjectId = [string]$entraState.clientAppObjectId
    }
    Write-Utf8JsonFile -Path $ProvisionedValuesPath -Value $handoff

    $existingSqlAdminPassword = Get-KeyVaultSecretValue -SecretName $SqlAdminPasswordSecretName
    $sqlAdminPassword = if (-not [string]::IsNullOrWhiteSpace($env:WORKSLIP_SQL_ADMIN_PASSWORD)) {
        $env:WORKSLIP_SQL_ADMIN_PASSWORD
    } elseif (-not [string]::IsNullOrWhiteSpace($existingSqlAdminPassword)) {
        $existingSqlAdminPassword
    } else {
        New-SqlAdminPassword
    }
    $mustStoreSqlAdminPassword = [string]::IsNullOrWhiteSpace($existingSqlAdminPassword) -or
        -not [string]::IsNullOrWhiteSpace($env:WORKSLIP_SQL_ADMIN_PASSWORD)

    $existingJwtSigningKey = Get-KeyVaultSecretValue -SecretName $JwtSigningKeySecretName
    $requestedJwtSigningKey = if (-not [string]::IsNullOrWhiteSpace($env:WORKSLIP_JWT_SIGNING_KEY)) {
        $env:WORKSLIP_JWT_SIGNING_KEY
    } elseif ([string]::IsNullOrWhiteSpace($existingJwtSigningKey) -or $existingJwtSigningKey.Length -lt 64) {
        New-CryptographicSecret -ByteLength 64
    } else {
        $null
    }

    if (-not [string]::IsNullOrWhiteSpace($requestedJwtSigningKey) -and $requestedJwtSigningKey.Length -lt 64) {
        throw 'WORKSLIP_JWT_SIGNING_KEY must contain at least 64 characters.'
    }

    $existingSender = Get-AppConfigurationValue -Key 'Azure:Acs:SenderAddress'
    $activateEmailDomain = if ($null -ne $ActivateCustomEmailDomain) {
        [bool]$ActivateCustomEmailDomain
    } else {
        $existingSender -eq 'noreply@mrsoftware.dk'
    }

    $deploymentParameters = [ordered]@{
        '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
        contentVersion = '1.0.0.0'
        parameters = [ordered]@{
            companyName = @{ value = $COMPANY_NAME }
            environment = @{ value = $Environment }
            globalAdminId = @{ value = $GlobalAdminId }
            location = @{ value = $Location }
            sqlAdminPassword = @{ value = $sqlAdminPassword }
            vercelToken = @{ value = '' }
            activateCustomEmailDomain = @{ value = $activateEmailDomain }
        }
    }
    Write-Utf8JsonFile -Path $parameterFile.FullName -Value $deploymentParameters

    $deploymentName = "$COMPANY_NAME-$NormalizedEnvironment-$(Get-Date -Format 'yyyyMMddHHmmss')"
    Write-Host "Deploying Azure infrastructure: $deploymentName" -ForegroundColor Cyan
    $deploymentResult = Invoke-AzureCli -Arguments @(
        'deployment', 'group', 'create',
        '--resource-group', $ResourceGroup,
        '--name', $deploymentName,
        '--mode', 'Incremental',
        '--template-file', $Template,
        '--parameters', "@$($parameterFile.FullName)",
        '--only-show-errors',
        '-o', 'json'
    )
    $deployment = $deploymentResult.Output | ConvertFrom-Json
    $outputs = $deployment.properties.outputs

    if ($mustStoreSqlAdminPassword) {
        Set-KeyVaultSecretFromMemory -SecretName $SqlAdminPasswordSecretName -SecretValue $sqlAdminPassword
    }

    if (-not [string]::IsNullOrWhiteSpace($requestedJwtSigningKey)) {
        Set-KeyVaultSecretFromMemory -SecretName $JwtSigningKeySecretName -SecretValue $requestedJwtSigningKey
        Write-Host 'JWT signing key created or rotated. Existing local JWTs are invalidated.' -ForegroundColor Yellow
    }

    Set-AppConfigurationKeyVaultReference -Key 'Jwt:SigningKey' -SecretName $JwtSigningKeySecretName

    $managedIdentityClientId = [string]$outputs.MANAGED_IDENTITY_CLIENT_ID.value
    if ([string]::IsNullOrWhiteSpace($managedIdentityClientId)) {
        throw 'Deployment output MANAGED_IDENTITY_CLIENT_ID was empty.'
    }

    $managedIdentitySqlConnectionString = "Server=tcp:$SqlServerName.database.windows.net,1433;Initial Catalog=$SqlDatabaseName;Authentication=Active Directory Managed Identity;User Id=$managedIdentityClientId;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    Set-KeyVaultSecretFromMemory -SecretName $SqlConnectionSecretName -SecretValue $managedIdentitySqlConnectionString
    Set-AppConfigurationKeyVaultReference -Key 'Azure:Sql:ConnectionString' -SecretName $SqlConnectionSecretName

    Remove-AppConfigurationKeyIfPresent -Key 'Azure:AdOAuth:ClientSecret'
    Disable-KeyVaultSecretIfPresent -SecretName $LegacyOAuthClientSecretName

    $sqlAdminGroupId = [string]$outputs.SQL_ADMIN_GROUP_ID.value
    if ([string]::IsNullOrWhiteSpace($sqlAdminGroupId)) {
        throw 'Deployment output SQL_ADMIN_GROUP_ID was empty.'
    }
    Add-GraphGroupMember -GroupId $sqlAdminGroupId -MemberId $GlobalAdminId -Description 'global administrator'

    & $SqlAccessScript `
        -Environment $Environment `
        -CompanyName $COMPANY_NAME `
        -SqlAdminPassword $sqlAdminPassword
    if (-not $?) {
        throw 'Managed identity SQL access provisioning failed.'
    }

    $githubDeploymentClientId = [string]$outputs.GITHUB_DEPLOYMENT_CLIENT_ID.value
    Write-Host "GitHub OIDC deployment client ID: $githubDeploymentClientId" -ForegroundColor Green
    Write-Host "API managed identity client ID: $managedIdentityClientId" -ForegroundColor Green
    Write-Host "Infrastructure deployment complete: $deploymentName" -ForegroundColor Green
}
finally {
    Remove-Item $parameterFile.FullName -Force -ErrorAction SilentlyContinue

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

    $sqlAdminPassword = $null
    $requestedJwtSigningKey = $null
    $existingJwtSigningKey = $null
    $managedIdentitySqlConnectionString = $null
}
