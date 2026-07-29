param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$StatePath = ''
)

$ErrorActionPreference = 'Stop'

$NormalizedEnvironment = $Environment.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $PSScriptRoot "entra.$NormalizedEnvironment.local.json"
}

$GraphRoot = 'https://graph.microsoft.com/v1.0'
$OAuthUniqueName = "workslip-oauth-server-$NormalizedEnvironment"
$ClientUniqueName = "workslip-client-$NormalizedEnvironment"
$ApiScopeId = 'c2e2bf46-f94d-4c3e-86d7-ca425e4c6e2a'
$ManagedRoleValues = @('Superadmin', 'Admin', 'User', 'Auditor')

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

    # az.cmd forwards arguments through cmd.exe. Alternate-key Graph URLs contain
    # parentheses and query strings that cmd.exe parses before Azure CLI receives
    # them. Invoke the Azure CLI Python entry point directly instead.
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
        [object]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 30
    [System.IO.File]::WriteAllText(
        $Path,
        $json,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Get-GraphApplication {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UniqueName,
        [int]$MaxAttempts = 1,
        [switch]$AllowMissing
    )

    $uri = "$GraphRoot/applications(uniqueName='$UniqueName')?`$select=id,appId,displayName,appRoles,api"
    $lastDiagnostic = ''

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $result = Invoke-AzureCli `
            -Arguments @(
                'rest',
                '--method', 'GET',
                '--uri', $uri,
                '--only-show-errors',
                '-o', 'json'
            ) `
            -AllowFailure

        if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)) {
            try {
                $application = $result.Output | ConvertFrom-Json
                if (-not [string]::IsNullOrWhiteSpace([string]$application.id) -and
                    -not [string]::IsNullOrWhiteSpace([string]$application.appId)) {
                    return $application
                }
            }
            catch {
                $lastDiagnostic = $_.Exception.Message
            }
        }
        else {
            $lastDiagnostic = $result.Output
            if ($AllowMissing -and
                $result.Output -match '(?i)(Request_ResourceNotFound|ResourceNotFound|404|does not exist|cannot find)') {
                return $null
            }
        }

        if ($attempt -lt $MaxAttempts) {
            Start-Sleep -Seconds 5
        }
    }

    if ($AllowMissing) {
        return $null
    }

    throw "Microsoft Graph did not expose application '$UniqueName' after $MaxAttempts attempts.`n$lastDiagnostic"
}

function Get-ExistingRoleId {
    param(
        [object]$Application,
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$FallbackId
    )

    if ($null -ne $Application -and $null -ne $Application.appRoles) {
        $existingRole = @($Application.appRoles) |
            Where-Object { $_.value -eq $Value } |
            Select-Object -First 1

        if ($null -ne $existingRole -and -not [string]::IsNullOrWhiteSpace([string]$existingRole.id)) {
            return [string]$existingRole.id
        }
    }

    return $FallbackId
}

function Get-UnmanagedExistingRoles {
    param([object]$Application)

    if ($null -eq $Application -or $null -eq $Application.appRoles) {
        return @()
    }

    return @(
        @($Application.appRoles) |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_.value) -and
                $ManagedRoleValues -notcontains [string]$_.value
            } |
            ForEach-Object {
                [ordered]@{
                    id = [string]$_.id
                    allowedMemberTypes = @($_.allowedMemberTypes)
                    displayName = [string]$_.displayName
                    description = [string]$_.description
                    value = [string]$_.value
                    isEnabled = [bool]$_.isEnabled
                }
            }
    )
}

function Get-ExistingScopeId {
    param(
        [object]$Application,
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$FallbackId
    )

    if ($null -ne $Application -and
        $null -ne $Application.api -and
        $null -ne $Application.api.oauth2PermissionScopes) {
        $existingScope = @($Application.api.oauth2PermissionScopes) |
            Where-Object { $_.value -eq $Value } |
            Select-Object -First 1

        if ($null -ne $existingScope -and -not [string]::IsNullOrWhiteSpace([string]$existingScope.id)) {
            return [string]$existingScope.id
        }
    }

    return $FallbackId
}

function Get-UnmanagedExistingScopes {
    param([object]$Application)

    if ($null -eq $Application -or
        $null -eq $Application.api -or
        $null -eq $Application.api.oauth2PermissionScopes) {
        return @()
    }

    return @(
        @($Application.api.oauth2PermissionScopes) |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_.value) -and
                $_.value -ne 'access_as_user'
            } |
            ForEach-Object {
                [ordered]@{
                    id = [string]$_.id
                    adminConsentDescription = [string]$_.adminConsentDescription
                    adminConsentDisplayName = [string]$_.adminConsentDisplayName
                    userConsentDescription = [string]$_.userConsentDescription
                    userConsentDisplayName = [string]$_.userConsentDisplayName
                    value = [string]$_.value
                    type = [string]$_.type
                    isEnabled = [bool]$_.isEnabled
                }
            }
    )
}

function Invoke-GraphUpsert {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [object]$Body,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $tempFile = New-TemporaryFile
    try {
        Write-Utf8JsonFile -Path $tempFile.FullName -Value $Body

        $result = Invoke-AzureCli `
            -Arguments @(
                'rest',
                '--method', 'PATCH',
                '--uri', $Uri,
                '--headers', 'Content-Type=application/json', 'Prefer=create-if-missing',
                '--body', "@$($tempFile.FullName)",
                '--only-show-errors',
                '-o', 'none'
            ) `
            -AllowFailure

        if ($result.ExitCode -ne 0) {
            $diagnostic = $result.Output
            if ([string]::IsNullOrWhiteSpace($diagnostic)) {
                $diagnostic = 'Microsoft Graph returned no diagnostic output.'
            }

            throw "Microsoft Graph failed while $Description.`n$diagnostic"
        }
    }
    finally {
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    }
}

function Wait-GraphServicePrincipal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppId,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $uri = "$GraphRoot/servicePrincipals(appId='$AppId')?`$select=id,appId,displayName"
    $lastDiagnostic = ''

    for ($attempt = 1; $attempt -le 18; $attempt++) {
        $result = Invoke-AzureCli `
            -Arguments @(
                'rest',
                '--method', 'GET',
                '--uri', $uri,
                '--only-show-errors',
                '-o', 'json'
            ) `
            -AllowFailure

        if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)) {
            try {
                $servicePrincipal = $result.Output | ConvertFrom-Json
                if (-not [string]::IsNullOrWhiteSpace([string]$servicePrincipal.id) -and
                    $servicePrincipal.appId -eq $AppId) {
                    return $servicePrincipal
                }
            }
            catch {
                $lastDiagnostic = $_.Exception.Message
            }
        }
        else {
            $lastDiagnostic = $result.Output
        }

        if ($attempt -lt 18) {
            Start-Sleep -Seconds 5
        }
    }

    throw "Microsoft Graph did not expose $Description.`n$lastDiagnostic"
}

function New-ManagedRole {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Id,
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    return [ordered]@{
        id = $Id
        allowedMemberTypes = @('User')
        displayName = $Value
        description = $Description
        value = $Value
        isEnabled = $true
    }
}

Initialize-AzureCliInvocation
$account = Ensure-AzureLogin

Write-Host 'Checking Microsoft Graph application access...' -ForegroundColor Cyan
$accessResult = Invoke-AzureCli `
    -Arguments @(
        'rest',
        '--method', 'GET',
        '--uri', "$GraphRoot/applications?`$top=1",
        '--only-show-errors',
        '-o', 'none'
    ) `
    -AllowFailure

if ($accessResult.ExitCode -ne 0) {
    throw "Microsoft Graph application access failed. Sign in with an administrator that can manage app registrations.`n$($accessResult.Output)"
}

$existingOAuthApplication = Get-GraphApplication `
    -UniqueName $OAuthUniqueName `
    -MaxAttempts 1 `
    -AllowMissing

$managedRoles = @(
    (New-ManagedRole `
        -Id (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'Superadmin' -FallbackId '560f8c3b-513a-4cc2-b8fe-e43294c327d6') `
        -Value 'Superadmin' `
        -Description 'Super administrator'),
    (New-ManagedRole `
        -Id (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'Admin' -FallbackId 'e89d1554-64fd-493d-a4ca-25c33bdc7327') `
        -Value 'Admin' `
        -Description 'Administrator'),
    (New-ManagedRole `
        -Id (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'User' -FallbackId 'ebc3ff93-e885-46d7-9733-7bc1cb2eb675') `
        -Value 'User' `
        -Description 'Standard user'),
    (New-ManagedRole `
        -Id (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'Auditor' -FallbackId '663dfda9-0112-4654-93ca-f16d70d7109b') `
        -Value 'Auditor' `
        -Description 'External temporary user')
)

$oauthRoles = @($managedRoles) + @(Get-UnmanagedExistingRoles -Application $existingOAuthApplication)
$apiScopeId = Get-ExistingScopeId `
    -Application $existingOAuthApplication `
    -Value 'access_as_user' `
    -FallbackId $ApiScopeId

$managedScope = [ordered]@{
    id = $apiScopeId
    adminConsentDescription = 'Access Workslip API as the signed-in user'
    adminConsentDisplayName = 'Access Workslip API'
    userConsentDescription = 'Access Workslip API on your behalf'
    userConsentDisplayName = 'Access Workslip API'
    value = 'access_as_user'
    type = 'User'
    isEnabled = $true
}
$oauthScopes = @($managedScope) + @(Get-UnmanagedExistingScopes -Application $existingOAuthApplication)

$oauthBody = [ordered]@{
    displayName = "Oauth server $Environment"
    # Workslip authenticates members and invited B2B guests in this tenant.
    # Single-tenant registration is required for the login_hint optional claim
    # used by promptless Microsoft logout.
    signInAudience = 'AzureADMyOrg'
    publicClient = [ordered]@{
        redirectUris = @('nativepasskeydemo://auth')
    }
    appRoles = $oauthRoles
    api = [ordered]@{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes = $oauthScopes
    }
}

Write-Host "Upserting OAuth application '$OAuthUniqueName'..." -ForegroundColor Cyan
Invoke-GraphUpsert `
    -Uri "$GraphRoot/applications(uniqueName='$OAuthUniqueName')" `
    -Body $oauthBody `
    -Description "upserting OAuth application '$OAuthUniqueName'"

$oauthApplication = Get-GraphApplication -UniqueName $OAuthUniqueName -MaxAttempts 18

$clientBody = [ordered]@{
    displayName = 'Workslip App'
    # Workslip authenticates members and invited B2B guests in this tenant.
    # Single-tenant registration is required for the login_hint optional claim
    # used by promptless Microsoft logout.
    signInAudience = 'AzureADMyOrg'
    api = [ordered]@{
        requestedAccessTokenVersion = 2
    }
    # The browser stores this opaque ID-token claim and sends it as logout_hint
    # so Microsoft can end the correct session without an account picker.
    optionalClaims = [ordered]@{
        idToken = @(
            [ordered]@{
                name = 'login_hint'
                source = $null
                essential = $false
                additionalProperties = @()
            }
        )
        accessToken = @()
        saml2Token = @()
    }
    spa = [ordered]@{
        redirectUris = @(
            'http://localhost:5270/login',
            'http://localhost:5270/invite/callback',
            'https://app.mrsoftware.dk/login',
            'https://app.mrsoftware.dk/invite/callback',
            'https://workslip-v2-0.vercel.app/login',
            'https://workslip-v2-0.vercel.app/invite/callback'
        )
    }
    web = [ordered]@{
        redirectUris = @('https://oauth.pstmn.io/v1/callback')
        implicitGrantSettings = [ordered]@{
            enableAccessTokenIssuance = $false
            enableIdTokenIssuance = $true
        }
    }
    requiredResourceAccess = @(
        [ordered]@{
            resourceAppId = '00000003-0000-0000-c000-000000000000'
            resourceAccess = @(
                [ordered]@{
                    id = 'e1fe6dd8-ba31-4d61-89e7-88639da4683d'
                    type = 'Scope'
                }
            )
        },
        [ordered]@{
            resourceAppId = [string]$oauthApplication.appId
            resourceAccess = @(
                [ordered]@{
                    id = $apiScopeId
                    type = 'Scope'
                }
            )
        }
    )
}

Write-Host "Upserting client application '$ClientUniqueName'..." -ForegroundColor Cyan
Invoke-GraphUpsert `
    -Uri "$GraphRoot/applications(uniqueName='$ClientUniqueName')" `
    -Body $clientBody `
    -Description "upserting client application '$ClientUniqueName'"

$clientApplication = Get-GraphApplication -UniqueName $ClientUniqueName -MaxAttempts 18

Write-Host 'Upserting service principals...' -ForegroundColor Cyan
Invoke-GraphUpsert `
    -Uri "$GraphRoot/servicePrincipals(appId='$($oauthApplication.appId)')" `
    -Body ([ordered]@{
        displayName = "Oauth server $Environment"
        tags = @('WindowsAzureActiveDirectoryIntegratedApp')
    }) `
    -Description "upserting OAuth service principal '$($oauthApplication.appId)'"

Invoke-GraphUpsert `
    -Uri "$GraphRoot/servicePrincipals(appId='$($clientApplication.appId)')" `
    -Body ([ordered]@{
        displayName = 'Workslip App'
        tags = @('WindowsAzureActiveDirectoryIntegratedApp')
    }) `
    -Description "upserting client service principal '$($clientApplication.appId)'"

$oauthServicePrincipal = Wait-GraphServicePrincipal `
    -AppId ([string]$oauthApplication.appId) `
    -Description "OAuth service principal '$($oauthApplication.appId)'"

$clientServicePrincipal = Wait-GraphServicePrincipal `
    -AppId ([string]$clientApplication.appId) `
    -Description "client service principal '$($clientApplication.appId)'"

$state = [ordered]@{
    environment = $NormalizedEnvironment
    tenantId = [string]$account.tenantId
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    oauthClientId = [string]$oauthApplication.appId
    oauthAppObjectId = [string]$oauthApplication.id
    oauthServicePrincipalObjectId = [string]$oauthServicePrincipal.id
    clientAppId = [string]$clientApplication.appId
    clientAppObjectId = [string]$clientApplication.id
    clientServicePrincipalObjectId = [string]$clientServicePrincipal.id
}

Write-Utf8JsonFile -Path $StatePath -Value $state

Write-Host "OAuth application client ID: $($oauthApplication.appId)" -ForegroundColor Green
Write-Host "Client application client ID: $($clientApplication.appId)" -ForegroundColor Green
Write-Host "Entra state written to: $StatePath" -ForegroundColor Green
