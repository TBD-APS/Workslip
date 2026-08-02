param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$CompanyName = 'mrsoftware',
    [string]$GitHubOwner = 'rasm105k',
    [string]$GitHubOwnerId = '31623093',
    [string]$GitHubRepository = 'Workslip-v2.0',
    [string]$GitHubRepositoryId = '1245555609',
    [string]$GitHubEnvironment = '',
    [switch]$ConfigureGitHubEnvironment,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$normalizedEnvironment = $Environment.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($GitHubEnvironment)) {
    $GitHubEnvironment = $normalizedEnvironment
}

$resourceGroup = "rg-$CompanyName-$normalizedEnvironment"
$keyVaultName = "kv-$CompanyName-$normalizedEnvironment"
$appConfigurationName = "appcs-$CompanyName-$normalizedEnvironment"
$template = Join-Path $PSScriptRoot 'githubInfrastructureBootstrap.bicep'
$graphRoot = 'https://graph.microsoft.com/v1.0'
$graphAppId = '00000003-0000-0000-c000-000000000000'
$requiredGraphRoles = @(
    'Directory.Read.All',
    'Group.ReadWrite.All',
    'AppRoleAssignment.ReadWrite.All'
)

if (-not (Test-Path $template)) {
    throw "Bootstrap Bicep template not found: $template"
}

function Initialize-AzureCli {
    $az = Get-Command az -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $script:AzExecutable = $az.Source
    $script:AzPrefix = @()

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return
    }

    $python = [System.IO.Path]::GetFullPath(
        (Join-Path (Split-Path -Parent $az.Source) '..\python.exe')
    )
    if (-not (Test-Path $python)) {
        throw "Azure CLI Python runtime not found: $python"
    }

    $script:AzExecutable = $python
    $script:AzPrefix = @('-IBm', 'azure.cli')
}

function Invoke-Az {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $commandArguments = @($script:AzPrefix) + @($Arguments)
        $output = @(
            & $script:AzExecutable @commandArguments 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ([string]::IsNullOrWhiteSpace($text)) {
            $text = 'Azure CLI returned no diagnostic output.'
        }
        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Output = $text }
}

function Ensure-AzureLogin {
    $account = Invoke-Az -Arguments @(
        'account', 'show',
        '--query', '{subscriptionId:id,tenantId:tenantId,user:user.name}',
        '--output', 'json'
    ) -AllowFailure

    if ($account.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($account.Output)) {
        Write-Host 'Azure login required. Starting device login...' -ForegroundColor Cyan
        Invoke-Az -Arguments @('login', '--use-device-code', '--output', 'none') | Out-Null
        $account = Invoke-Az -Arguments @(
            'account', 'show',
            '--query', '{subscriptionId:id,tenantId:tenantId,user:user.name}',
            '--output', 'json'
        )
    }

    return $account.Output | ConvertFrom-Json
}

function Assert-ExistingResource {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $result = Invoke-Az -Arguments ($Arguments + @('--query', 'id', '--output', 'tsv')) -AllowFailure
    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Output)) {
        throw "$Description was not found or is not readable. Complete the initial administrator infrastructure deployment first."
    }
}

function Write-JsonTempFile {
    param([Parameter(Mandatory = $true)][object]$Value)

    $file = New-TemporaryFile
    $json = $Value | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText(
        $file.FullName,
        $json,
        [System.Text.UTF8Encoding]::new($false)
    )
    return $file
}

function Wait-ForDirectoryObject {
    param([Parameter(Mandatory = $true)][string]$ObjectId)

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $result = Invoke-Az -Arguments @(
            'rest', '--method', 'GET',
            '--uri', "$graphRoot/directoryObjects/$ObjectId",
            '--output', 'none'
        ) -AllowFailure

        if ($result.ExitCode -eq 0) {
            return
        }
        Start-Sleep -Seconds ([Math]::Min($attempt * 5, 30))
    }

    throw "Timed out waiting for managed identity '$ObjectId' in Microsoft Graph."
}

function Ensure-GraphRoles {
    param([Parameter(Mandatory = $true)][string]$PrincipalId)

    Wait-ForDirectoryObject -ObjectId $PrincipalId
    $graphSp = (Invoke-Az -Arguments @(
        'ad', 'sp', 'show', '--id', $graphAppId,
        '--query', '{id:id,appRoles:appRoles}', '--output', 'json'
    )).Output | ConvertFrom-Json

    $assignmentsUri = "$graphRoot/servicePrincipals/$PrincipalId/appRoleAssignments"
    $assignmentResult = Invoke-Az -Arguments @(
        'rest', '--method', 'GET', '--uri', $assignmentsUri, '--output', 'json'
    )
    $assignments = @(($assignmentResult.Output | ConvertFrom-Json).value)

    foreach ($roleValue in $requiredGraphRoles) {
        $role = $graphSp.appRoles |
            Where-Object {
                $_.value -eq $roleValue -and
                $_.isEnabled -eq $true -and
                $_.allowedMemberTypes -contains 'Application'
            } |
            Select-Object -First 1

        if ($null -eq $role) {
            throw "Microsoft Graph application role '$roleValue' was not found."
        }

        $existing = $assignments | Where-Object {
            [string]$_.resourceId -eq [string]$graphSp.id -and
            [string]$_.appRoleId -eq [string]$role.id
        }
        if ($existing) {
            Write-Host "Microsoft Graph role already assigned: $roleValue" -ForegroundColor DarkGray
            continue
        }

        $bodyFile = Write-JsonTempFile -Value ([ordered]@{
            principalId = $PrincipalId
            resourceId = [string]$graphSp.id
            appRoleId = [string]$role.id
        })
        try {
            Invoke-Az -Arguments @(
                'rest', '--method', 'POST', '--uri', $assignmentsUri,
                '--headers', 'Content-Type=application/json',
                '--body', "@$($bodyFile.FullName)", '--output', 'none'
            ) | Out-Null
        }
        finally {
            Remove-Item $bodyFile.FullName -Force -ErrorAction SilentlyContinue
        }

        Write-Host "Assigned Microsoft Graph role: $roleValue" -ForegroundColor Green
    }
}

function Set-GitHubClientIdVariable {
    param([Parameter(Mandatory = $true)][string]$ClientId)

    $gh = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $gh) {
        throw 'GitHub CLI is required for -ConfigureGitHubEnvironment. Install gh or add the variable manually.'
    }

    & $gh.Source auth status --hostname github.com 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub CLI is not authenticated. Run gh auth login, then retry.'
    }

    & $gh.Source variable set AZURE_INFRA_CLIENT_ID `
        --env $GitHubEnvironment `
        --body $ClientId `
        --repo "$GitHubOwner/$GitHubRepository"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not set the GitHub environment variable AZURE_INFRA_CLIENT_ID.'
    }
}

Initialize-AzureCli
$account = Ensure-AzureLogin

Assert-ExistingResource -Description "Resource group '$resourceGroup'" -Arguments @(
    'group', 'show', '--name', $resourceGroup
)
Assert-ExistingResource -Description "Key Vault '$keyVaultName'" -Arguments @(
    'keyvault', 'show', '--name', $keyVaultName
)
Assert-ExistingResource -Description "App Configuration '$appConfigurationName'" -Arguments @(
    'appconfig', 'show', '--name', $appConfigurationName
)

$deploymentName = "$CompanyName-$normalizedEnvironment-github-infra-bootstrap"
$deploymentCommand = if ($WhatIf) { 'what-if' } else { 'create' }
$deployment = Invoke-Az -Arguments @(
    'deployment', 'sub', $deploymentCommand,
    '--location', $Location,
    '--name', $deploymentName,
    '--template-file', $template,
    '--parameters',
    "companyName=$CompanyName",
    "environment=$normalizedEnvironment",
    "location=$Location",
    "githubOwner=$GitHubOwner",
    "githubOwnerId=$GitHubOwnerId",
    "githubRepository=$GitHubRepository",
    "githubRepositoryId=$GitHubRepositoryId",
    "githubEnvironment=$GitHubEnvironment",
    '--only-show-errors', '--output', 'json'
)

if ($WhatIf) {
    Write-Host $deployment.Output
    Write-Host 'WHAT-IF complete. No Azure, Microsoft Graph or GitHub mutation was performed.' -ForegroundColor Green
    return
}

$outputs = ($deployment.Output | ConvertFrom-Json).properties.outputs
$clientId = [string]$outputs.CLIENT_ID.value
$principalId = [string]$outputs.PRINCIPAL_ID.value
$identityName = [string]$outputs.IDENTITY_NAME.value
$subject = [string]$outputs.FEDERATED_CREDENTIAL_SUBJECT.value

foreach ($output in @($clientId, $principalId, $identityName, $subject)) {
    if ([string]::IsNullOrWhiteSpace($output)) {
        throw 'The bootstrap deployment returned an empty required output.'
    }
}

Ensure-GraphRoles -PrincipalId $principalId

if ($ConfigureGitHubEnvironment) {
    Set-GitHubClientIdVariable -ClientId $clientId
    Write-Host "Configured AZURE_INFRA_CLIENT_ID in GitHub environment '$GitHubEnvironment'." -ForegroundColor Green
}
else {
    Write-Host ''
    Write-Host 'Add this non-secret GitHub environment variable:' -ForegroundColor Yellow
    Write-Host "  Environment: $GitHubEnvironment"
    Write-Host '  Variable: AZURE_INFRA_CLIENT_ID'
    Write-Host "  Value: $clientId"
    Write-Host 'Or rerun with -ConfigureGitHubEnvironment after gh auth login.'
}

Write-Host "Infrastructure OIDC identity: $identityName" -ForegroundColor Green
Write-Host "Federated credential subject: $subject" -ForegroundColor Green
Write-Host "Bootstrap completed in subscription $($account.subscriptionId)." -ForegroundColor Green
