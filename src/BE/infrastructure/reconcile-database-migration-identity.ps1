param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,
    [string]$CompanyName = 'mrsoftware',
    [string]$GitHubOwner = 'rasm105k',
    [string]$GitHubOwnerId = '31623093',
    [string]$GitHubRepository = 'Workslip-v2.0',
    [string]$GitHubRepositoryId = '1245555609'
)

$ErrorActionPreference = 'Stop'

$normalizedEnvironment = $Environment.ToLowerInvariant()
$resourceGroup = "rg-$CompanyName-$normalizedEnvironment"
$sqlServerName = "db-$CompanyName-$normalizedEnvironment-server"
$sqlServerFqdn = "$sqlServerName.database.windows.net"
$sqlDatabaseName = "db-$CompanyName-$normalizedEnvironment"
$keyVaultName = "kv-$CompanyName-$normalizedEnvironment"
$migrationIdentityName = "id-$CompanyName-$normalizedEnvironment-migration"
$githubDeploymentIdentityName = "id-$CompanyName-$normalizedEnvironment-github"
$sqlAdminPasswordSecretName = 'Azure--Sql--AdminPassword'
$sqlSecurityManagerRoleId = '056cd41c-7e88-42e1-933e-88ba6a50c9c3'
$readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
$firewallRuleName = 'AllowDatabaseMigrationProvisioning'
$federatedCredentialName = "github-$normalizedEnvironment"
$federatedCredentialSubject = "repo:$GitHubOwner@$GitHubOwnerId/$GitHubRepository@$GitHubRepositoryId`:environment:$normalizedEnvironment"

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $text = ($output -join [Environment]::NewLine).Trim()

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ([string]::IsNullOrWhiteSpace($text)) {
            $text = 'Azure CLI returned no diagnostic output.'
        }

        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Ensure-RoleAssignment {
    param(
        [Parameter(Mandatory = $true)][string]$PrincipalId,
        [Parameter(Mandatory = $true)][string]$Scope,
        [Parameter(Mandatory = $true)][string]$RoleDefinitionId,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $existing = Invoke-AzureCli -Arguments @(
        'role', 'assignment', 'list',
        '--assignee-object-id', $PrincipalId,
        '--scope', $Scope,
        '--role', $RoleDefinitionId,
        '--query', '[0].id',
        '--only-show-errors',
        '--output', 'tsv'
    )

    if (-not [string]::IsNullOrWhiteSpace($existing.Output)) {
        Write-Host "Role assignment already present: $Description" -ForegroundColor DarkGray
        return
    }

    Invoke-AzureCli -Arguments @(
        'role', 'assignment', 'create',
        '--assignee-object-id', $PrincipalId,
        '--assignee-principal-type', 'ServicePrincipal',
        '--role', $RoleDefinitionId,
        '--scope', $Scope,
        '--only-show-errors',
        '--output', 'none'
    ) | Out-Null

    Write-Host "Role assignment created: $Description" -ForegroundColor Green
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required.'
}

$sqlcmdCommand = Get-Command sqlcmd -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $sqlcmdCommand -or [string]::IsNullOrWhiteSpace($sqlcmdCommand.Source)) {
    throw 'sqlcmd is required to provision the database migration identity.'
}

$subscriptionId = (Invoke-AzureCli -Arguments @(
    'account', 'show', '--query', 'id', '--output', 'tsv'
)).Output.Trim()
if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
    throw 'Azure CLI did not return a subscription ID.'
}

$migrationIdentity = Invoke-AzureCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroup,
    '--name', $migrationIdentityName,
    '--only-show-errors',
    '--output', 'json'
) -AllowFailure

if ($migrationIdentity.ExitCode -ne 0) {
    Write-Host "Creating deployment-only migration identity '$migrationIdentityName'." -ForegroundColor Cyan
    Invoke-AzureCli -Arguments @(
        'identity', 'create',
        '--resource-group', $resourceGroup,
        '--name', $migrationIdentityName,
        '--only-show-errors',
        '--output', 'none'
    ) | Out-Null
}

$migrationIdentityJson = (Invoke-AzureCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroup,
    '--name', $migrationIdentityName,
    '--only-show-errors',
    '--output', 'json'
)).Output | ConvertFrom-Json

$migrationClientId = [string]$migrationIdentityJson.clientId
$migrationPrincipalId = [string]$migrationIdentityJson.principalId
$migrationIdentityResourceId = [string]$migrationIdentityJson.id
if ([string]::IsNullOrWhiteSpace($migrationClientId) -or
    [string]::IsNullOrWhiteSpace($migrationPrincipalId) -or
    [string]::IsNullOrWhiteSpace($migrationIdentityResourceId)) {
    throw "Migration identity '$migrationIdentityName' did not return clientId/principalId/id."
}

$federatedBodyFile = New-TemporaryFile
try {
    $body = [ordered]@{
        properties = [ordered]@{
            issuer = 'https://token.actions.githubusercontent.com'
            subject = $federatedCredentialSubject
            audiences = @('api://AzureADTokenExchange')
        }
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText(
        $federatedBodyFile.FullName,
        $body,
        [System.Text.UTF8Encoding]::new($false))

    $federatedUri = "https://management.azure.com$migrationIdentityResourceId/federatedIdentityCredentials/$federatedCredentialName`?api-version=2024-11-30"
    Invoke-AzureCli -Arguments @(
        'rest',
        '--method', 'PUT',
        '--url', $federatedUri,
        '--headers', 'Content-Type=application/json',
        '--body', "@$($federatedBodyFile.FullName)",
        '--only-show-errors',
        '--output', 'none'
    ) | Out-Null
}
finally {
    Remove-Item $federatedBodyFile.FullName -Force -ErrorAction SilentlyContinue
}

$sqlServerResourceId = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Sql/servers/$sqlServerName"
Ensure-RoleAssignment `
    -PrincipalId $migrationPrincipalId `
    -Scope $sqlServerResourceId `
    -RoleDefinitionId $sqlSecurityManagerRoleId `
    -Description 'migration identity -> SQL Security Manager on production SQL server'

$githubDeploymentPrincipalId = (Invoke-AzureCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroup,
    '--name', $githubDeploymentIdentityName,
    '--query', 'principalId',
    '--only-show-errors',
    '--output', 'tsv'
)).Output.Trim()
if ([string]::IsNullOrWhiteSpace($githubDeploymentPrincipalId)) {
    throw "GitHub deployment identity '$githubDeploymentIdentityName' was not found."
}

Ensure-RoleAssignment `
    -PrincipalId $githubDeploymentPrincipalId `
    -Scope $migrationIdentityResourceId `
    -RoleDefinitionId $readerRoleId `
    -Description 'GitHub deployment identity -> Reader on migration identity'

$provisioningIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($provisioningIp, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Could not resolve a valid public IPv4 address. Received '$provisioningIp'."
}

$sqlAdminPassword = $null
$previousSqlCmdPassword = $env:SQLCMDPASSWORD
$firewallRuleCreated = $false
$sqlFile = New-TemporaryFile

try {
    Invoke-AzureCli -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $resourceGroup,
        '--server', $sqlServerName,
        '--name', $firewallRuleName,
        '--start-ip-address', $provisioningIp,
        '--end-ip-address', $provisioningIp,
        '--only-show-errors',
        '--output', 'none'
    ) | Out-Null
    $firewallRuleCreated = $true

    $sqlAdminPassword = (Invoke-AzureCli -Arguments @(
        'keyvault', 'secret', 'show',
        '--vault-name', $keyVaultName,
        '--name', $sqlAdminPasswordSecretName,
        '--query', 'value',
        '--only-show-errors',
        '--output', 'tsv'
    )).Output
    if ([string]::IsNullOrWhiteSpace($sqlAdminPassword)) {
        throw "Key Vault secret '$sqlAdminPasswordSecretName' was empty."
    }

    $parsedClientId = [Guid]::Empty
    if (-not [Guid]::TryParse($migrationClientId, [ref]$parsedClientId)) {
        throw "Migration identity returned an invalid client ID: '$migrationClientId'."
    }
    $sid = '0x' + (($parsedClientId.ToByteArray() | ForEach-Object { $_.ToString('X2') }) -join '')

    $sql = @"
DECLARE @userName sysname = N'$migrationIdentityName';
DECLARE @expectedSid varbinary(16) = $sid;
DECLARE @currentSid varbinary(85) = (
    SELECT sid FROM sys.database_principals WHERE name = @userName
);
DECLARE @sql nvarchar(max);

IF @currentSid IS NOT NULL AND @currentSid <> @expectedSid
BEGIN
    IF IS_ROLEMEMBER(N'db_ddladmin', @userName) = 1
    BEGIN
        SET @sql = N'ALTER ROLE db_ddladmin DROP MEMBER ' + QUOTENAME(@userName) + N';';
        EXEC sp_executesql @sql;
    END;
    IF IS_ROLEMEMBER(N'db_datareader', @userName) = 1
    BEGIN
        SET @sql = N'ALTER ROLE db_datareader DROP MEMBER ' + QUOTENAME(@userName) + N';';
        EXEC sp_executesql @sql;
    END;
    IF IS_ROLEMEMBER(N'db_datawriter', @userName) = 1
    BEGIN
        SET @sql = N'ALTER ROLE db_datawriter DROP MEMBER ' + QUOTENAME(@userName) + N';';
        EXEC sp_executesql @sql;
    END;
    SET @sql = N'DROP USER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @userName)
BEGIN
    SET @sql = N'CREATE USER ' + QUOTENAME(@userName) + N' WITH SID = $sid, TYPE = E;';
    EXEC sp_executesql @sql;
END;

IF IS_ROLEMEMBER(N'db_ddladmin', @userName) <> 1
BEGIN
    SET @sql = N'ALTER ROLE db_ddladmin ADD MEMBER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;
IF IS_ROLEMEMBER(N'db_datareader', @userName) <> 1
BEGIN
    SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;
IF IS_ROLEMEMBER(N'db_datawriter', @userName) <> 1
BEGIN
    SET @sql = N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;
"@

    [System.IO.File]::WriteAllText(
        $sqlFile.FullName,
        $sql,
        [System.Text.UTF8Encoding]::new($false))

    $env:SQLCMDPASSWORD = $sqlAdminPassword
    & $sqlcmdCommand.Source `
        -S $sqlServerFqdn `
        -d $sqlDatabaseName `
        -U rbj `
        -b `
        -l 30 `
        -N `
        -i $sqlFile.FullName
    if ($LASTEXITCODE -ne 0) {
        throw 'Migration identity SQL role provisioning failed.'
    }
}
finally {
    $env:SQLCMDPASSWORD = $previousSqlCmdPassword
    $sqlAdminPassword = $null
    Remove-Item $sqlFile.FullName -Force -ErrorAction SilentlyContinue

    if ($firewallRuleCreated) {
        $deleteResult = Invoke-AzureCli -Arguments @(
            'sql', 'server', 'firewall-rule', 'delete',
            '--resource-group', $resourceGroup,
            '--server', $sqlServerName,
            '--name', $firewallRuleName,
            '--only-show-errors',
            '--output', 'none'
        ) -AllowFailure
        if ($deleteResult.ExitCode -ne 0) {
            throw "Could not remove temporary migration provisioning firewall rule '$firewallRuleName'.`n$($deleteResult.Output)"
        }
    }
}

Write-Host "Database migration identity reconciled: $migrationIdentityName" -ForegroundColor Green
Write-Host "Migration client ID: $migrationClientId" -ForegroundColor Green
