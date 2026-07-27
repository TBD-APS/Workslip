param(
    [Parameter(Position=0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$VercelToken = ''
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
$ExpectedVaultName = "kv-$COMPANY_NAME-$NormalizedEnvironment"
$LegacyVaultName = "kv-$COMPANY_NAME$NormalizedEnvironment"
$MalformedVaultName = "kv-$COMPANY_NAME"
$GraphRoot = 'https://graph.microsoft.com/v1.0'
$OAuthUniqueName = "workslip-oauth-server-$NormalizedEnvironment"
$ClientUniqueName = "workslip-client-$NormalizedEnvironment"
$ApiScopeId = 'c2e2bf46-f94d-4c3e-86d7-ca425e4c6e2a'

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

    # Windows PowerShell converts native stderr to PowerShell error records.
    # Keep those records non-terminating so callers can suppress expected lookup
    # failures and inspect $LASTEXITCODE themselves.
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

function Ensure-AzureLogin {
    $subscriptionId = az account show --query id -o tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($subscriptionId)) {
        return
    }

    Write-Host 'Azure login required. Starting device login...' -ForegroundColor Cyan
    az login --use-device-code -o none
    if ($LASTEXITCODE -ne 0) {
        throw 'Azure login failed.'
    }
}

function Get-GraphApplication {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UniqueName,
        [int]$MaxAttempts = 1,
        [switch]$AllowMissing
    )

    $uri = "$GraphRoot/applications(uniqueName='$UniqueName')?`$select=id,appId,displayName,appRoles"
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $json = az rest --method GET --uri $uri -o json 2>$null
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($json)) {
            try {
                $application = $json | ConvertFrom-Json
                if (-not [string]::IsNullOrWhiteSpace($application.id) -and
                    -not [string]::IsNullOrWhiteSpace($application.appId)) {
                    return $application
                }
            }
            catch {
                # Graph can briefly return incomplete content during replication.
            }
        }

        if ($attempt -lt $MaxAttempts) {
            Start-Sleep -Seconds 5
        }
    }

    if ($AllowMissing) {
        return $null
    }

    throw "Microsoft Graph did not expose application '$UniqueName' after $MaxAttempts attempts."
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
        $existingRole = $Application.appRoles |
            Where-Object { $_.value -eq $Value } |
            Select-Object -First 1

        if ($null -ne $existingRole -and -not [string]::IsNullOrWhiteSpace($existingRole.id)) {
            return [string]$existingRole.id
        }
    }

    return $FallbackId
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

        for ($attempt = 1; $attempt -le 18; $attempt++) {
            az rest `
                --method PATCH `
                --uri $Uri `
                --headers 'Content-Type=application/json' 'Prefer=create-if-missing' `
                --body "@$($tempFile.FullName)" `
                -o none 2>$null

            if ($LASTEXITCODE -eq 0) {
                return
            }

            if ($attempt -lt 18) {
                Write-Host "Retrying Microsoft Graph operation ($attempt/18): $Description" -ForegroundColor DarkGray
                Start-Sleep -Seconds 5
            }
        }

        throw "Microsoft Graph failed while $Description."
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
    for ($attempt = 1; $attempt -le 18; $attempt++) {
        $json = az rest --method GET --uri $uri -o json 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($json)) {
            try {
                $servicePrincipal = $json | ConvertFrom-Json
                if (-not [string]::IsNullOrWhiteSpace($servicePrincipal.id) -and
                    $servicePrincipal.appId -eq $AppId) {
                    return
                }
            }
            catch {
                # Retry incomplete Graph responses.
            }
        }

        if ($attempt -lt 18) {
            Start-Sleep -Seconds 5
        }
    }

    throw "Microsoft Graph did not expose $Description."
}

function Provision-EntraApplications {
    Write-Host 'Checking Microsoft Graph application access...' -ForegroundColor Cyan
    az rest --method GET --uri "$GraphRoot/applications?`$top=1" -o none 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Microsoft Graph application access failed. Sign in with an administrator that can manage app registrations.'
    }

    $existingOAuthApplication = Get-GraphApplication `
        -UniqueName $OAuthUniqueName `
        -MaxAttempts 3 `
        -AllowMissing

    $oauthBody = [ordered]@{
        displayName = "Oauth server $Environment"
        signInAudience = 'AzureADandPersonalMicrosoftAccount'
        publicClient = [ordered]@{
            redirectUris = @('nativepasskeydemo://auth')
        }
        appRoles = @(
            [ordered]@{
                id = (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'Superadmin' -FallbackId '560f8c3b-513a-4cc2-b8fe-e43294c327d6')
                allowedMemberTypes = @('User')
                displayName = 'Superadmin'
                description = 'Super administrator'
                value = 'Superadmin'
                isEnabled = $true
            },
            [ordered]@{
                id = (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'Admin' -FallbackId 'e89d1554-64fd-493d-a4ca-25c33bdc7327')
                allowedMemberTypes = @('User')
                displayName = 'Admin'
                description = 'Administrator'
                value = 'Admin'
                isEnabled = $true
            },
            [ordered]@{
                id = (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'User' -FallbackId 'ebc3ff93-e885-46d7-9733-7bc1cb2eb675')
                allowedMemberTypes = @('User')
                displayName = 'User'
                description = 'Standard user'
                value = 'User'
                isEnabled = $true
            },
            [ordered]@{
                id = (Get-ExistingRoleId -Application $existingOAuthApplication -Value 'Auditor' -FallbackId '663dfda9-0112-4654-93ca-f16d70d7109b')
                allowedMemberTypes = @('User')
                displayName = 'Auditor'
                description = 'External temporary user'
                value = 'Auditor'
                isEnabled = $true
            }
        )
        api = [ordered]@{
            requestedAccessTokenVersion = 2
            oauth2PermissionScopes = @(
                [ordered]@{
                    id = $ApiScopeId
                    adminConsentDescription = 'Access Workslip API as the signed-in user'
                    adminConsentDisplayName = 'Access Workslip API'
                    userConsentDescription = 'Access Workslip API on your behalf'
                    userConsentDisplayName = 'Access Workslip API'
                    value = 'access_as_user'
                    type = 'User'
                    isEnabled = $true
                }
            )
        }
    }

    Invoke-GraphUpsert `
        -Uri "$GraphRoot/applications(uniqueName='$OAuthUniqueName')" `
        -Body $oauthBody `
        -Description "upserting OAuth application '$OAuthUniqueName'"

    $oauthApplication = Get-GraphApplication -UniqueName $OAuthUniqueName -MaxAttempts 18

    $clientBody = [ordered]@{
        displayName = 'Workslip App'
        signInAudience = 'AzureADandPersonalMicrosoftAccount'
        api = [ordered]@{
            requestedAccessTokenVersion = 2
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
                        id = $ApiScopeId
                        type = 'Scope'
                    }
                )
            }
        )
    }

    Invoke-GraphUpsert `
        -Uri "$GraphRoot/applications(uniqueName='$ClientUniqueName')" `
        -Body $clientBody `
        -Description "upserting client application '$ClientUniqueName'"

    $clientApplication = Get-GraphApplication -UniqueName $ClientUniqueName -MaxAttempts 18

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

    Wait-GraphServicePrincipal `
        -AppId ([string]$oauthApplication.appId) `
        -Description "OAuth service principal '$($oauthApplication.appId)'"

    Wait-GraphServicePrincipal `
        -AppId ([string]$clientApplication.appId) `
        -Description "client service principal '$($clientApplication.appId)'"

    $provisionedValues = [ordered]@{
        environment = $NormalizedEnvironment
        oauthClientId = [string]$oauthApplication.appId
        oauthAppObjectId = [string]$oauthApplication.id
        clientAppId = [string]$clientApplication.appId
        clientAppObjectId = [string]$clientApplication.id
    }

    Write-Utf8JsonFile -Path $ProvisionedValuesPath -Value $provisionedValues

    Write-Host "OAuth application: $($oauthApplication.appId)" -ForegroundColor Green
    Write-Host "Client application: $($clientApplication.appId)" -ForegroundColor Green
    Write-Host 'Entra applications and service principals reconciled.' -ForegroundColor Green
}

try {
    Ensure-AzureLogin
    Provision-EntraApplications

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
