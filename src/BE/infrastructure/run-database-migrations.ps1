param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,
    [string]$CompanyName = 'mrsoftware',
    [string]$MigrationsPath = (Join-Path $PSScriptRoot 'database\migrations'),
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

$normalizedEnvironment = $Environment.ToLowerInvariant()
$resourceGroup = "rg-$CompanyName-$normalizedEnvironment"
$sqlServerName = "db-$CompanyName-$normalizedEnvironment-server"
$sqlServerFqdn = "$sqlServerName.database.windows.net"
$sqlDatabaseName = "db-$CompanyName-$normalizedEnvironment"
$firewallRuleName = "AllowDatabaseMigration-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
$migrationLockName = 'Workslip.SchemaMigrations'

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

function ConvertTo-SqlLiteral {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("'", "''")
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([Convert]::ToHexString($sha256.ComputeHash($Bytes))).ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-MigrationChecksumInfo {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    # Git may materialize text files with LF or CRLF depending on runner/platform.
    # Migration identity is therefore based on UTF-8 SQL with canonical LF newlines.
    $normalizedSql = $Sql.Replace("`r`n", "`n").Replace("`r", "`n")
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $canonicalChecksum = Get-Sha256Hex -Bytes $utf8NoBom.GetBytes($normalizedSql)
    $rawChecksum = (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $crlfChecksum = Get-Sha256Hex -Bytes $utf8NoBom.GetBytes($normalizedSql.Replace("`n", "`r`n"))

    $legacyChecksums = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$legacyChecksums.Add($rawChecksum)
    [void]$legacyChecksums.Add($crlfChecksum)
    [void]$legacyChecksums.Remove($canonicalChecksum)

    [pscustomobject]@{
        CanonicalSha256 = $canonicalChecksum
        LegacySha256 = @($legacyChecksums)
    }
}

if (-not (Test-Path $MigrationsPath)) {
    throw "Migration directory not found: $MigrationsPath"
}

$migrationFiles = @(Get-ChildItem -Path $MigrationsPath -File -Filter '*.sql' | Sort-Object Name)
$seenMigrationIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$migrations = @()

foreach ($file in $migrationFiles) {
    if ($file.BaseName -notmatch '^\d{8}_\d{4}_[a-z0-9][a-z0-9_-]*$') {
        throw "Invalid migration filename '$($file.Name)'. Expected YYYYMMDD_HHMM_slug.sql."
    }
    if (-not $seenMigrationIds.Add($file.BaseName)) {
        throw "Duplicate migration ID '$($file.BaseName)'."
    }

    $sql = Get-Content -Path $file.FullName -Raw
    if ([string]::IsNullOrWhiteSpace($sql)) {
        throw "Migration '$($file.Name)' is empty."
    }
    if ($sql -match '(?im)^\s*GO\s*(?:--.*)?$') {
        throw "Migration '$($file.Name)' contains a GO batch separator. Workslip migrations must be one transaction-safe T-SQL batch."
    }

    $checksumInfo = Get-MigrationChecksumInfo -Path $file.FullName -Sql $sql
    $migrations += [pscustomobject]@{
        Id = $file.BaseName
        Sql = $sql
        Sha256 = $checksumInfo.CanonicalSha256
        LegacySha256 = $checksumInfo.LegacySha256
    }
}

Write-Host "Validated $($migrations.Count) versioned database migration file(s)."
foreach ($migration in $migrations) {
    Write-Host "  $($migration.Id)  $($migration.Sha256)" -ForegroundColor DarkGray
    foreach ($legacyChecksum in $migration.LegacySha256) {
        Write-Host "    compatible legacy line-ending checksum: $legacyChecksum" -ForegroundColor DarkGray
    }
}

if ($ValidateOnly) {
    return
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required to execute database migrations.'
}
if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'Invoke-Sqlcmd is required. Install the Microsoft SqlServer PowerShell module before running migrations.'
}

$account = Invoke-AzureCli -Arguments @('account', 'show', '--query', '{id:id,tenantId:tenantId}', '--output', 'json')
if ([string]::IsNullOrWhiteSpace($account.Output)) {
    throw 'Azure CLI is not authenticated.'
}

$runnerIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($runnerIp, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Could not resolve a valid public IPv4 address. Received '$runnerIp'."
}

$firewallRuleCreated = $false
$accessToken = $null
try {
    Invoke-AzureCli -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $resourceGroup,
        '--server', $sqlServerName,
        '--name', $firewallRuleName,
        '--start-ip-address', $runnerIp,
        '--end-ip-address', $runnerIp,
        '--only-show-errors',
        '--output', 'none'
    ) | Out-Null
    $firewallRuleCreated = $true

    $accessToken = (Invoke-AzureCli -Arguments @(
        'account', 'get-access-token',
        '--resource', 'https://database.windows.net/',
        '--query', 'accessToken',
        '--output', 'tsv'
    )).Output.Trim()
    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw 'Azure CLI returned an empty Azure SQL access token.'
    }

    $bootstrapSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = N'$migrationLockName',
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 60000;
IF @lockResult < 0
    THROW 51000, 'Could not acquire the Workslip schema migration lock.', 1;

IF OBJECT_ID(N'dbo.WorkslipSchemaMigrations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkslipSchemaMigrations
    (
        MigrationId nvarchar(200) NOT NULL,
        Sha256 char(64) NOT NULL,
        AppliedAt datetimeoffset NOT NULL CONSTRAINT DF_WorkslipSchemaMigrations_AppliedAt DEFAULT sysutcdatetime(),
        AppliedBy nvarchar(200) NULL,
        CONSTRAINT PK_WorkslipSchemaMigrations PRIMARY KEY (MigrationId)
    );
END;
COMMIT TRANSACTION;
"@

    Invoke-Sqlcmd `
        -ServerInstance $sqlServerFqdn `
        -Database $sqlDatabaseName `
        -AccessToken $accessToken `
        -Query $bootstrapSql `
        -QueryTimeout 120 `
        -AbortOnError `
        -ErrorAction Stop | Out-Null

    foreach ($migration in $migrations) {
        $migrationId = ConvertTo-SqlLiteral $migration.Id
        $checksum = ConvertTo-SqlLiteral $migration.Sha256
        $appliedByValue = if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID)) { 'manual' } else { $env:GITHUB_RUN_ID }
        $appliedBy = ConvertTo-SqlLiteral $appliedByValue

        $existing = @(Invoke-Sqlcmd `
            -ServerInstance $sqlServerFqdn `
            -Database $sqlDatabaseName `
            -AccessToken $accessToken `
            -Query "SELECT Sha256 FROM dbo.WorkslipSchemaMigrations WHERE MigrationId = N'$migrationId';" `
            -QueryTimeout 30 `
            -AbortOnError `
            -ErrorAction Stop)

        if ($existing.Count -gt 0) {
            $existingChecksum = ([string]$existing[0].Sha256).Trim().ToLowerInvariant()
            if ($existingChecksum -eq $migration.Sha256) {
                Write-Host "Migration already applied: $($migration.Id)" -ForegroundColor DarkGray
                continue
            }

            $legacyChecksumMatch = @($migration.LegacySha256 | Where-Object { $_ -eq $existingChecksum } | Select-Object -First 1)
            if ($legacyChecksumMatch.Count -eq 0) {
                throw "Applied migration '$($migration.Id)' has checksum '$existingChecksum', but the repository contains '$($migration.Sha256)'. Applied migrations are immutable."
            }

            $legacyChecksum = ConvertTo-SqlLiteral $existingChecksum
            $reconcileSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock
        @Resource = N'$migrationLockName',
        @LockMode = 'Exclusive',
        @LockOwner = 'Transaction',
        @LockTimeout = 60000;
    IF @lockResult < 0
        THROW 51000, 'Could not acquire the Workslip schema migration lock.', 1;

    DECLARE @storedChecksum char(64) =
        (SELECT Sha256 FROM dbo.WorkslipSchemaMigrations WITH (UPDLOCK, HOLDLOCK) WHERE MigrationId = N'$migrationId');

    IF @storedChecksum IS NULL OR @storedChecksum <> '$legacyChecksum'
        THROW 51002, 'Migration checksum changed while reconciling a legacy line-ending checksum.', 1;

    UPDATE dbo.WorkslipSchemaMigrations
    SET Sha256 = '$checksum'
    WHERE MigrationId = N'$migrationId'
      AND Sha256 = '$legacyChecksum';

    IF @@ROWCOUNT <> 1
        THROW 51003, 'Legacy migration checksum reconciliation did not update exactly one history row.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@

            Invoke-Sqlcmd `
                -ServerInstance $sqlServerFqdn `
                -Database $sqlDatabaseName `
                -AccessToken $accessToken `
                -Query $reconcileSql `
                -QueryTimeout 120 `
                -AbortOnError `
                -ErrorAction Stop | Out-Null

            Write-Host "Reconciled legacy line-ending checksum without re-running migration SQL: $($migration.Id)" -ForegroundColor Yellow
            continue
        }

        Write-Host "Applying migration: $($migration.Id)" -ForegroundColor Cyan
        $applySql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @lockResult int;
    EXEC @lockResult = sys.sp_getapplock
        @Resource = N'$migrationLockName',
        @LockMode = 'Exclusive',
        @LockOwner = 'Transaction',
        @LockTimeout = 60000;
    IF @lockResult < 0
        THROW 51000, 'Could not acquire the Workslip schema migration lock.', 1;

    DECLARE @existingChecksum char(64) =
        (SELECT Sha256 FROM dbo.WorkslipSchemaMigrations WITH (UPDLOCK, HOLDLOCK) WHERE MigrationId = N'$migrationId');

    IF @existingChecksum IS NOT NULL AND @existingChecksum <> '$checksum'
        THROW 51001, 'An applied Workslip migration has been modified.', 1;

    IF @existingChecksum IS NULL
    BEGIN
$($migration.Sql)

        INSERT INTO dbo.WorkslipSchemaMigrations (MigrationId, Sha256, AppliedBy)
        VALUES (N'$migrationId', '$checksum', N'$appliedBy');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@

        Invoke-Sqlcmd `
            -ServerInstance $sqlServerFqdn `
            -Database $sqlDatabaseName `
            -AccessToken $accessToken `
            -Query $applySql `
            -QueryTimeout 600 `
            -AbortOnError `
            -ErrorAction Stop | Out-Null

        Write-Host "Applied migration: $($migration.Id)" -ForegroundColor Green
    }
}
finally {
    $accessToken = $null
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
            throw "Database migration finished, but temporary firewall cleanup failed for '$firewallRuleName'.`n$($deleteResult.Output)"
        }
    }
}

Write-Host 'Database migration step completed successfully.' -ForegroundColor Green
