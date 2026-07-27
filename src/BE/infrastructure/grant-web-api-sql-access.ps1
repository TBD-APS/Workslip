param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,
    [Parameter(Mandatory = $true)]
    [string]$CompanyName,
    [Parameter(Mandatory = $true)]
    [string]$SqlAdminPassword
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required to provision SQL access.'
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required to provision SQL access. Install Microsoft sqlcmd, then rerun deploy.ps1.'
}

$resourceGroup = "rg-$CompanyName-$Environment"
$sqlServerName = "db-$CompanyName-$Environment-server"
$sqlServerFqdn = "$sqlServerName.database.windows.net"
$sqlDatabaseName = "db-$CompanyName-$Environment"
$identityName = "id-$CompanyName-$Environment"
$firewallRuleName = 'AllowSqlProvisioningScript'
$sqlFile = New-TemporaryFile

try {
    $clientId = az identity show `
        --resource-group $resourceGroup `
        --name $identityName `
        --query clientId `
        --output tsv

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($clientId)) {
        throw "Could not resolve client ID for managed identity '$identityName'."
    }

    $sid = '0x' + (([Guid]::Parse($clientId).ToByteArray() | ForEach-Object { $_.ToString('X2') }) -join '')
    $provisioningIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()

    if ($provisioningIp -notmatch '^\d{1,3}(\.\d{1,3}){3}$') {
        throw "Could not resolve a valid public IPv4 address. Received '$provisioningIp'."
    }

    az sql server firewall-rule create `
        --resource-group $resourceGroup `
        --server $sqlServerName `
        --name $firewallRuleName `
        --start-ip-address $provisioningIp `
        --end-ip-address $provisioningIp `
        --output none

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the temporary SQL provisioning firewall rule.'
    }

    @"
DECLARE @userName sysname = N'$identityName';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @userName)
BEGIN
    DECLARE @createUserSql nvarchar(max) = N'CREATE USER ' + QUOTENAME(@userName) + N' WITH SID = $sid, TYPE = E;';
    EXEC sp_executesql @createUserSql;
END;

IF IS_ROLEMEMBER(N'db_datareader', @userName) <> 1
    EXEC(N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@userName));

IF IS_ROLEMEMBER(N'db_datawriter', @userName) <> 1
    EXEC(N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@userName));

IF IS_ROLEMEMBER(N'db_ddladmin', @userName) <> 1
    EXEC(N'ALTER ROLE db_ddladmin ADD MEMBER ' + QUOTENAME(@userName));
"@ | Set-Content -Path $sqlFile -Encoding utf8

    $env:SQLCMDPASSWORD = $SqlAdminPassword
    & sqlcmd `
        -S $sqlServerFqdn `
        -d $sqlDatabaseName `
        -U rbj `
        -b `
        -l 30 `
        -N `
        -i $sqlFile

    if ($LASTEXITCODE -ne 0) {
        throw 'SQL access provisioning failed.'
    }

    Write-Host "SQL access provisioned for managed identity '$identityName'." -ForegroundColor Green
}
finally {
    $env:SQLCMDPASSWORD = $null
    Remove-Item $sqlFile -ErrorAction SilentlyContinue

    az sql server firewall-rule delete `
        --resource-group $resourceGroup `
        --server $sqlServerName `
        --name $firewallRuleName `
        --output none 2>$null
}
