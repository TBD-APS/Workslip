param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    # Entra object IDs are per-tenant. This default belongs to the original
    # production tenant; in any other tenant it resolves to nothing. Leave it
    # alone here — the script verifies it exists in the signed-in tenant and
    # falls back to the deploying principal when it does not.
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    # Enable only after Domain, SPF, DKIM and DKIM2 are verified for this exact
    # Email Communication Services domain. Foundation deploys keep the
    # Azure-managed sender linked so an unverified custom domain cannot block IaC.
    [switch]$EnableCustomEmailDomain,
    # Default verified domain of the signed-in tenant. Resolved from Microsoft
    # Graph when omitted, so a fresh tenant needs no hand-set value.
    [string]$EntraDefaultDomain = '',
    [string]$PowerBiReaderPrincipalId = '',
    [string]$PowerBiReaderEmail = '',
    [switch]$EnablePowerBiExport,
    # Monthly cost budget in the subscription's billing currency. Threshold
    # notifications go to the existing API alert action group.
    [int]$BudgetMonthlyAmount = 800,
    # Escape hatch for a deploying identity without Microsoft.Consumption write
    # permission. Turning this off removes cost alerting entirely.
    [bool]$BudgetEnabled = $true,
    [string]$EntraStatePath = '',
    # Resolve everything, run the ARM deployment as what-if, and stop. No resource
    # group, secret, App Configuration, SQL or Graph write is performed.
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$InfrastructureRoot = $PSScriptRoot
$NormalizedEnvironment = $Environment.ToLowerInvariant()
$ResourceGroup = "rg-$COMPANY_NAME-$NormalizedEnvironment"
$AppConfigurationName = "appcs-$COMPANY_NAME-$NormalizedEnvironment"
$KeyVaultName = "kv-$COMPANY_NAME-$NormalizedEnvironment"
$SqlServerName = "db-$COMPANY_NAME-$NormalizedEnvironment-server"
$SqlDatabaseName = "db-$COMPANY_NAME-$NormalizedEnvironment"
$Template = Join-Path $InfrastructureRoot 'main.bicep'
$SqlAccessScript = Join-Path $PSScriptRoot 'grant-web-api-sql-access.ps1'
$MonitoringConfigPath = Join-Path $InfrastructureRoot 'monitoring.config.json'
$GraphRoot = 'https://graph.microsoft.com/v1.0'
$OAuthUniqueName = "workslip-oauth-server-$NormalizedEnvironment"
$ClientUniqueName = "workslip-client-$NormalizedEnvironment"
$SqlAdminPasswordSecretName = 'Azure--Sql--AdminPassword'
$JwtSigningKeySecretName = 'Jwt--SigningKey'
$SqlConnectionSecretName = 'Azure--Sql--ConnectionString'
$LegacyOAuthClientSecretName = 'Azure--AdOAuth--ClientSecret'

if ([string]::IsNullOrWhiteSpace($EntraStatePath)) {
    $EntraStatePath = Join-Path $InfrastructureRoot "entra.$NormalizedEnvironment.local.json"
}

if (-not (Test-Path $Template)) {
    throw "Bicep template not found: $Template"
}

if (-not (Test-Path $SqlAccessScript)) {
    throw "SQL access provisioning script not found: $SqlAccessScript"
}

# A preview never runs the SQL access script, so it does not need sqlcmd. Requiring
# it would block previewing from a machine that only has the Azure CLI.
if (-not $WhatIf -and -not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required to provision the API managed identity in Azure SQL.'
}

if ($EnablePowerBiExport -and
    ([string]::IsNullOrWhiteSpace($PowerBiReaderPrincipalId) -or
     [string]::IsNullOrWhiteSpace($PowerBiReaderEmail))) {
    throw 'Power BI export activation requires both PowerBiReaderPrincipalId and PowerBiReaderEmail.'
}

function Initialize-AzureCliInvocation {
    $azureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop |
        Select-Object -First 1

    if ($null -eq $azureCliCommand -or [string]::IsNullOrWhiteSpace($azureCliCommand.Source)) {
        throw 'Could not resolve Azure CLI.'
    }

    $script:AzureCliExecutable = $azureCliCommand.Source
    $script:AzureCliPrefix = @()

    $runningOnWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    if (-not $runningOnWindows) {
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

    # A fresh deployment has no Key Vault yet.
    # Check the ARM resource before calling the Key Vault data plane.
    $vaultResult = Invoke-AzureCli `
        -Arguments @(
            'keyvault', 'show',
            '--resource-group', $ResourceGroup,
            '--name', $KeyVaultName,
            '--query', 'id',
            '--only-show-errors',
            '-o', 'tsv'
        ) `
        -AllowFailure

    if ($vaultResult.ExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace($vaultResult.Output)) {
        return $null
    }

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

function Resolve-EntraDefaultDomain {
    <#
        UserEntraService builds userPrincipalName as "<mailNickname>@<domain>" when
        it searches for an existing directory user. An empty or foreign domain makes
        that filter match nothing, so the service creates a duplicate account instead
        of reusing the real one. The value is tenant-bound and must therefore come
        from the tenant being deployed into, never from a checked-in constant.
    #>
    $result = Invoke-AzureCli `
        -Arguments @(
            'rest',
            '--method', 'GET',
            '--uri', "$GraphRoot/organization?`$select=verifiedDomains",
            '--query', 'value[0].verifiedDomains[?isDefault].name | [0]',
            '--only-show-errors',
            '-o', 'tsv'
        ) `
        -AllowFailure

    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Output)) {
        throw @"
Could not resolve the tenant's default verified domain from Microsoft Graph.
The deploying identity needs Organization.Read.All or Directory.Read.All, or you
can bypass the lookup with -EntraDefaultDomain <domain>.
$($result.Output)
"@
    }

    return $result.Output.Trim()
}

function Test-GraphObjectExists {
    param([Parameter(Mandatory = $true)][string]$ObjectId)

    $result = Invoke-AzureCli `
        -Arguments @(
            'rest',
            '--method', 'GET',
            '--uri', "$GraphRoot/directoryObjects/$ObjectId",
            '--only-show-errors',
            '-o', 'none'
        ) `
        -AllowFailure

    return $result.ExitCode -eq 0
}

function Resolve-GraphPrincipalType {
    param([Parameter(Mandatory = $true)][string]$ObjectId)

    $result = Invoke-AzureCli `
        -Arguments @(
            'rest',
            '--method', 'GET',
            '--uri', "$GraphRoot/directoryObjects/$ObjectId",
            '--query', '"@odata.type"',
            '--only-show-errors',
            '-o', 'tsv'
        )

    switch ($result.Output.Trim()) {
        '#microsoft.graph.user' { return 'User' }
        '#microsoft.graph.servicePrincipal' { return 'ServicePrincipal' }
        default { throw "Directory object '$ObjectId' has unsupported type '$($result.Output.Trim())'." }
    }
}

function Resolve-DeployingPrincipalObjectId {
    # Interactive/device login: a real user is signed in.
    $userResult = Invoke-AzureCli `
        -Arguments @('ad', 'signed-in-user', 'show', '--query', 'id', '--only-show-errors', '-o', 'tsv') `
        -AllowFailure

    if ($userResult.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($userResult.Output)) {
        return $userResult.Output.Trim()
    }

    # CI/OIDC: a service principal is signed in, so resolve it via its app ID.
    $appIdResult = Invoke-AzureCli `
        -Arguments @('account', 'show', '--query', 'user.name', '--only-show-errors', '-o', 'tsv') `
        -AllowFailure

    if ($appIdResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($appIdResult.Output)) {
        return $null
    }

    $spResult = Invoke-AzureCli `
        -Arguments @(
            'ad', 'sp', 'show',
            '--id', $appIdResult.Output.Trim(),
            '--query', 'id',
            '--only-show-errors',
            '-o', 'tsv'
        ) `
        -AllowFailure

    if ($spResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($spResult.Output)) {
        return $null
    }

    return $spResult.Output.Trim()
}

function Resolve-GlobalAdminId {
    <#
        globalAdminId receives App Configuration Data Owner and Key Vault
        Administrator, and is added to the SQL admin group. A stale ID from another
        tenant does not fail fast: the role assignments are accepted and the run
        only dies much later, inside Add-GraphGroupMember's 30-attempt wait. Verify
        it up front instead.
    #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$RequestedGlobalAdminId)

    if (-not [string]::IsNullOrWhiteSpace($RequestedGlobalAdminId)) {
        if (Test-GraphObjectExists -ObjectId $RequestedGlobalAdminId) {
            return $RequestedGlobalAdminId
        }

        Write-Host "Global administrator '$RequestedGlobalAdminId' does not exist in this tenant. Falling back to the deploying principal." -ForegroundColor Yellow
    }

    $resolved = Resolve-DeployingPrincipalObjectId
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        throw @"
Could not determine a global administrator object ID for this tenant.
'$RequestedGlobalAdminId' was not found and the deploying principal could not be
resolved. Pass -GlobalAdminId <objectId> for an identity that exists in the
signed-in tenant.
"@
    }

    if (-not (Test-GraphObjectExists -ObjectId $resolved)) {
        throw "Resolved deploying principal '$resolved' is not readable in this tenant. Pass -GlobalAdminId explicitly."
    }

    Write-Host "Using deploying principal as global administrator: $resolved" -ForegroundColor Cyan
    return $resolved
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
        # Required by the cost budget in budgets.bicep. Without it the first
        # deployment into a fresh subscription fails on an unregistered provider.
        'Microsoft.Consumption'
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

    # Resolve every tenant-bound value before the first mutation, so deploying
    # into a fresh tenant fails immediately and legibly rather than halfway
    # through provisioning.
    $resolvedEntraDefaultDomain = if (-not [string]::IsNullOrWhiteSpace($EntraDefaultDomain)) {
        $EntraDefaultDomain.Trim()
    } else {
        Resolve-EntraDefaultDomain
    }
    Write-Host "Entra default domain: $resolvedEntraDefaultDomain" -ForegroundColor Cyan

    $resolvedGlobalAdminId = Resolve-GlobalAdminId -RequestedGlobalAdminId $GlobalAdminId
    $resolvedGlobalAdminPrincipalType = Resolve-GraphPrincipalType -ObjectId $resolvedGlobalAdminId
    Write-Host "Resolved infrastructure administrator principal type: $resolvedGlobalAdminPrincipalType" -ForegroundColor Cyan

    # Operator configuration, not template content. Read here and passed as a
    # parameter so main.bicep stays independent of what sits next to it on disk.
    $alertEmailAddresses = @(
        (Get-Content -Path $MonitoringConfigPath -Raw | ConvertFrom-Json).alertEmailAddresses
    )
    if ($alertEmailAddresses.Count -eq 0) {
        throw "No alertEmailAddresses configured in $MonitoringConfigPath. Operational alerts would have no recipient."
    }

    $groupExists = Invoke-AzureCli `
        -Arguments @('group', 'exists', '--name', $ResourceGroup, '-o', 'tsv')
    if ($groupExists.Output -ne 'true') {
        if ($WhatIf) {
            # what-if runs against a resource group, so there is nothing to compare
            # against until it exists. Say so rather than reporting an empty diff.
            throw @"
Resource group '$ResourceGroup' does not exist, so there is nothing to preview against.
Run without -WhatIf to create it, or create the group first:
  az group create --name $ResourceGroup --location $Location
"@
        }

        Invoke-AzureCli -Arguments @('group', 'create', '--name', $ResourceGroup, '--location', $Location, '-o', 'none') | Out-Null
    }

    if (Test-Path $EntraStatePath) {
        $entraState = Read-EntraState -Path $EntraStatePath
    }
    else {
        $entraState = Resolve-ExistingEntraState -TenantId ([string]$account.tenantId)
        if ($WhatIf) {
            Write-Host "Resolved Entra state in memory; cache file not written: $EntraStatePath" -ForegroundColor DarkGray
        }
        else {
            Write-Utf8JsonFile -Path $EntraStatePath -Value $entraState
            Write-Host "Cached read-only Entra state: $EntraStatePath" -ForegroundColor Green
        }
    }

    Assert-EntraState -State $entraState
    if (-not [string]::IsNullOrWhiteSpace([string]$entraState.tenantId) -and
        $entraState.tenantId -ne [string]$account.tenantId) {
        throw "Entra state belongs to tenant '$($entraState.tenantId)', but Azure CLI is signed into '$($account.tenantId)'."
    }


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

    $deploymentParameters = [ordered]@{
        '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
        contentVersion = '1.0.0.0'
        parameters = [ordered]@{
            companyName = @{ value = $COMPANY_NAME }
            environment = @{ value = $Environment }
            globalAdminId = @{ value = $resolvedGlobalAdminId }
            globalAdminPrincipalType = @{ value = $resolvedGlobalAdminPrincipalType }
            customEmailDomainEnabled = @{ value = [bool]$EnableCustomEmailDomain }
            entraDefaultDomain = @{ value = $resolvedEntraDefaultDomain }
            powerBiReaderPrincipalId = @{ value = $PowerBiReaderPrincipalId }
            powerBiReaderEmail = @{ value = $PowerBiReaderEmail }
            powerBiExportEnabled = @{ value = [bool]$EnablePowerBiExport }
            budgetMonthlyAmount = @{ value = $BudgetMonthlyAmount }
            budgetEnabled = @{ value = $BudgetEnabled }
            oauthClientId = @{ value = [string]$entraState.oauthClientId }
            oauthAppObjectId = @{ value = [string]$entraState.oauthAppObjectId }
            clientAppId = @{ value = [string]$entraState.clientAppId }
            clientAppObjectId = @{ value = [string]$entraState.clientAppObjectId }
            alertEmailAddressList = @{ value = $alertEmailAddresses }
            location = @{ value = $Location }
            sqlAdminPassword = @{ value = $sqlAdminPassword }
        }
    }
    Write-Utf8JsonFile -Path $parameterFile.FullName -Value $deploymentParameters

    $deploymentName = "$COMPANY_NAME-$NormalizedEnvironment-$(Get-Date -Format 'yyyyMMddHHmmss')"

    if ($WhatIf) {
        Write-Host "Previewing Azure infrastructure: $deploymentName" -ForegroundColor Cyan
        Invoke-AzureCli -Arguments @(
            'deployment', 'group', 'what-if',
            '--resource-group', $ResourceGroup,
            '--name', $deploymentName,
            '--mode', 'Incremental',
            '--template-file', $Template,
            '--parameters', "@$($parameterFile.FullName)",
            '--only-show-errors',
            '-o', 'table'
        ) | ForEach-Object { Write-Host $_.Output }

        # Everything past this point consumes deployment outputs and writes secrets,
        # App Configuration references, SQL principals and Graph group membership.
        # what-if produces no outputs, so stop here rather than half-run the phase.
        Write-Host ''
        Write-Host 'Not written in preview: Key Vault secrets, App Configuration references, SQL access, SQL admin group membership.' -ForegroundColor DarkGray
        Write-Host 'Preview complete. No Azure resource was changed.' -ForegroundColor Green
        return
    }

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
    Add-GraphGroupMember -GroupId $sqlAdminGroupId -MemberId $resolvedGlobalAdminId -Description 'global administrator'

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

    $sqlAdminPassword = $null
    $requestedJwtSigningKey = $null
    $existingJwtSigningKey = $null
    $managedIdentitySqlConnectionString = $null
}
