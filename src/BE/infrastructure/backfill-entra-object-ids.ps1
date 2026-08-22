<#
.SYNOPSIS
    Reconcile Users.EntraId against the directory of the signed-in Entra tenant.

.DESCRIPTION
    Entra object IDs are per-tenant. After a tenant migration every EntraId in the
    Workslip database points at an object that no longer exists, because recreating
    a user in another tenant always mints a new GUID.

    Sign-in survives this: EfUserRepository.GetByExternalIdentityAsync falls back to
    matching EntraEmail and Email when the object ID does not match. What does not
    survive is everything keyed on the object ID —

      * IUserEntraService.DeleteUserAsync(entraUserId) — offboarding calls Graph with
        a dead ID, so the directory account is never removed
      * ISuperadminEntraService.RevokeSuperadminAsync(entraUserId) — same, for
        superadmin revocation
      * IsEntraIdentityReferencedAsync — the guard against two Workslip users sharing
        one directory identity compares against dead IDs

    This script resolves each user against the directory using the same filter as
    UserEntraService.FindExistingEntraUserAsync and writes the current object ID back.

    It reports without changing anything unless -Apply is passed.

.EXAMPLE
    ./backfill-entra-object-ids.ps1 -Environment prod
    Dry run. Prints what would change and exits without touching the database.

.EXAMPLE
    ./backfill-entra-object-ids.ps1 -Environment prod -Apply
    Writes the resolved object IDs.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,
    [string]$CompanyName = 'mrsoftware',
    [string]$EntraDefaultDomain = '',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

$normalizedEnvironment = $Environment.ToLowerInvariant()
$resourceGroup = "rg-$CompanyName-$normalizedEnvironment"
$sqlServerName = "db-$CompanyName-$normalizedEnvironment-server"
$sqlServerFqdn = "$sqlServerName.database.windows.net"
$sqlDatabaseName = "db-$CompanyName-$normalizedEnvironment"
$keyVaultName = "kv-$CompanyName-$normalizedEnvironment"
$sqlAdminPasswordSecretName = 'Azure--Sql--AdminPassword'
$sqlAdminLogin = 'rbj'
$firewallRuleName = 'AllowEntraObjectIdBackfill'
$graphRoot = 'https://graph.microsoft.com/v1.0'

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

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

# Mirrors UserEntraService.BuildMailNickname.
function Get-MailNickname {
    param([Parameter(Mandatory = $true)][string]$Email)
    return $Email.Split('@')[0].Replace('.', '').Replace('-', '')
}

# Mirrors UserEntraService.BuildGuestUserPrincipalNamePrefix.
function Get-GuestUpnPrefix {
    param([Parameter(Mandatory = $true)][string]$Email)
    return $Email.Replace('@', '_') + '#EXT#'
}

# Mirrors UserEntraService.EscapeODataString.
function ConvertTo-EscapedODataString {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    return $Value.Replace("'", "''")
}

function ConvertTo-SqlGuidLiteral {
    <#
        Graph object IDs and Workslip user IDs are GUIDs. Rejecting anything that is
        not one keeps the generated UPDATE free of interpolated free text.
    #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($Value, [ref]$parsed)) {
        throw "Refusing to build SQL from a non-GUID value: '$Value'."
    }

    return $parsed.ToString()
}

function Resolve-EntraDefaultDomain {
    $result = Invoke-AzureCli `
        -Arguments @(
            'rest',
            '--method', 'GET',
            '--uri', "$graphRoot/organization?`$select=verifiedDomains",
            '--query', 'value[0].verifiedDomains[?isDefault].name | [0]',
            '--only-show-errors',
            '-o', 'tsv'
        ) `
        -AllowFailure

    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Output)) {
        throw @"
Could not resolve the tenant's default verified domain from Microsoft Graph.
The signed-in identity needs Organization.Read.All or Directory.Read.All, or you
can bypass the lookup with -EntraDefaultDomain <domain>.
$($result.Output)
"@
    }

    return $result.Output.Trim()
}

<#
    Same filter as UserEntraService.FindExistingEntraUserAsync, so the script and the
    application agree on which directory account belongs to a Workslip user. Top is 2
    rather than 1: the application only needs any match, but a backfill must refuse to
    guess when a mail address resolves to more than one directory object.
#>
function Find-EntraUser {
    param(
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$DefaultDomain
    )

    $escapedEmail = ConvertTo-EscapedODataString -Value $Email
    $escapedUpn = ConvertTo-EscapedODataString -Value "$(Get-MailNickname -Email $Email)@$DefaultDomain"
    $escapedGuestPrefix = ConvertTo-EscapedODataString -Value (Get-GuestUpnPrefix -Email $Email)

    $filter = "mail eq '$escapedEmail' or otherMails/any(m:m eq '$escapedEmail') or userPrincipalName eq '$escapedUpn' or startswith(userPrincipalName,'$escapedGuestPrefix')"
    $uri = "$graphRoot/users?`$filter=$([uri]::EscapeDataString($filter))&`$select=id,userPrincipalName,mail&`$top=2"

    $result = Invoke-AzureCli `
        -Arguments @('rest', '--method', 'GET', '--uri', $uri, '--only-show-errors', '-o', 'json') `
        -AllowFailure

    if ($result.ExitCode -ne 0) {
        throw "Graph lookup failed for '$Email'.`n$($result.Output)"
    }

    $matches = @(($result.Output | ConvertFrom-Json).value)
    return [pscustomobject]@{
        Count = $matches.Count
        ObjectId = if ($matches.Count -ge 1) { [string]$matches[0].id } else { $null }
        UserPrincipalName = if ($matches.Count -ge 1) { [string]$matches[0].userPrincipalName } else { $null }
    }
}

function Invoke-SqlQuery {
    param(
        [Parameter(Mandatory = $true)][string]$Query,
        [Parameter(Mandatory = $true)][string]$SqlAdminPassword
    )

    $sqlFile = New-TemporaryFile
    $outFile = New-TemporaryFile
    $previousPassword = $env:SQLCMDPASSWORD

    try {
        [System.IO.File]::WriteAllText(
            $sqlFile.FullName,
            $Query,
            [System.Text.UTF8Encoding]::new($false))

        $env:SQLCMDPASSWORD = $SqlAdminPassword
        & $script:SqlCmdPath `
            -S $sqlServerFqdn `
            -d $sqlDatabaseName `
            -U $sqlAdminLogin `
            -b -l 30 -N `
            -s '|' -W -h -1 `
            -i $sqlFile.FullName `
            -o $outFile.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "sqlcmd failed with exit code $LASTEXITCODE.`n$([System.IO.File]::ReadAllText($outFile.FullName))"
        }

        return @(
            [System.IO.File]::ReadAllLines($outFile.FullName) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Where-Object { $_ -notmatch '^\(\d+ rows affected\)$' }
        )
    }
    finally {
        $env:SQLCMDPASSWORD = $previousPassword
        Remove-Item $sqlFile.FullName -Force -ErrorAction SilentlyContinue
        Remove-Item $outFile.FullName -Force -ErrorAction SilentlyContinue
    }
}

$sqlcmdCommand = Get-Command sqlcmd -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $sqlcmdCommand -or [string]::IsNullOrWhiteSpace($sqlcmdCommand.Source)) {
    throw 'sqlcmd is required to read and update Workslip users.'
}
$script:SqlCmdPath = $sqlcmdCommand.Source

$defaultDomain = if (-not [string]::IsNullOrWhiteSpace($EntraDefaultDomain)) {
    $EntraDefaultDomain.Trim()
} else {
    Resolve-EntraDefaultDomain
}

$tenantId = (Invoke-AzureCli -Arguments @('account', 'show', '--query', 'tenantId', '-o', 'tsv')).Output.Trim()

Write-Host ''
Write-Host "Tenant:      $tenantId" -ForegroundColor Cyan
Write-Host "Domain:      $defaultDomain" -ForegroundColor Cyan
Write-Host "Database:    $sqlDatabaseName" -ForegroundColor Cyan
Write-Host "Mode:        $(if ($Apply) { 'APPLY — the database will be updated' } else { 'DRY RUN — nothing will be written' })" -ForegroundColor $(if ($Apply) { 'Yellow' } else { 'Green' })
Write-Host ''

$provisioningIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($provisioningIp, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Could not resolve a valid public IPv4 address. Received '$provisioningIp'."
}

$sqlAdminPassword = $null
$firewallRuleCreated = $false

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

    $rows = Invoke-SqlQuery -SqlAdminPassword $sqlAdminPassword -Query @'
SET NOCOUNT ON;
SELECT
    CAST(Id AS nvarchar(36)),
    ISNULL(Email, N''),
    ISNULL(EntraEmail, N''),
    ISNULL(EntraId, N'')
FROM Users
ORDER BY Email;
'@

    $users = foreach ($row in $rows) {
        $fields = $row -split '\|'
        if ($fields.Count -lt 4) { continue }

        [pscustomobject]@{
            Id = $fields[0].Trim()
            Email = $fields[1].Trim()
            EntraEmail = $fields[2].Trim()
            EntraId = $fields[3].Trim()
        }
    }

    Write-Host "Read $(@($users).Count) users." -ForegroundColor DarkGray
    Write-Host ''

    $plan = foreach ($user in $users) {
        # EntraEmail is the address the directory actually knows the user by, so
        # prefer it and fall back to the Workslip login address.
        $lookupEmail = if (-not [string]::IsNullOrWhiteSpace($user.EntraEmail)) {
            $user.EntraEmail
        } else {
            $user.Email
        }

        if ([string]::IsNullOrWhiteSpace($lookupEmail)) {
            [pscustomobject]@{
                User = $user; Status = 'NoEmail'; ResolvedId = $null; Detail = 'No Email or EntraEmail to look up'
            }
            continue
        }

        $found = Find-EntraUser -Email $lookupEmail -DefaultDomain $defaultDomain

        if ($found.Count -eq 0) {
            [pscustomobject]@{
                User = $user; Status = 'Missing'; ResolvedId = $null; Detail = "No directory user matches $lookupEmail"
            }
        }
        elseif ($found.Count -gt 1) {
            [pscustomobject]@{
                User = $user; Status = 'Ambiguous'; ResolvedId = $null; Detail = "$lookupEmail matches more than one directory user"
            }
        }
        elseif ($found.ObjectId -eq $user.EntraId) {
            [pscustomobject]@{
                User = $user; Status = 'Current'; ResolvedId = $found.ObjectId; Detail = $found.UserPrincipalName
            }
        }
        else {
            [pscustomobject]@{
                User = $user; Status = 'Backfill'; ResolvedId = $found.ObjectId; Detail = $found.UserPrincipalName
            }
        }
    }

    # IsEntraIdentityReferencedAsync exists to stop two Workslip users sharing one
    # directory identity. Enforce the same rule here rather than writing a duplicate.
    $claimed = @{}
    foreach ($entry in $plan | Where-Object { $_.ResolvedId }) {
        if ($claimed.ContainsKey($entry.ResolvedId)) {
            $entry.Status = 'Conflict'
            $entry.Detail = "Object ID already resolved for $($claimed[$entry.ResolvedId])"
        }
        else {
            $claimed[$entry.ResolvedId] = $entry.User.Email
        }
    }

    foreach ($entry in $plan | Sort-Object { $_.Status }, { $_.User.Email }) {
        $colour = switch ($entry.Status) {
            'Current'   { 'DarkGray' }
            'Backfill'  { 'Yellow' }
            'Missing'   { 'Red' }
            'Ambiguous' { 'Red' }
            'Conflict'  { 'Red' }
            default     { 'Red' }
        }

        Write-Host ("{0,-10} {1,-40} {2}" -f $entry.Status, $entry.User.Email, $entry.Detail) -ForegroundColor $colour
    }

    $backfill = @($plan | Where-Object { $_.Status -eq 'Backfill' })
    $blocked = @($plan | Where-Object { $_.Status -in @('Missing', 'Ambiguous', 'Conflict', 'NoEmail') })

    Write-Host ''
    Write-Host "Already current: $(@($plan | Where-Object { $_.Status -eq 'Current' }).Count)" -ForegroundColor DarkGray
    Write-Host "To backfill:     $($backfill.Count)" -ForegroundColor Yellow
    Write-Host "Needs attention: $($blocked.Count)" -ForegroundColor $(if ($blocked.Count -gt 0) { 'Red' } else { 'DarkGray' })
    Write-Host ''

    if ($blocked.Count -gt 0) {
        Write-Host 'Users listed as Missing have no directory account in this tenant. They can still sign in only if' -ForegroundColor Red
        Write-Host 'an account with a matching address is created; until then Graph offboarding cannot reach them.' -ForegroundColor Red
        Write-Host ''
    }

    if (-not $Apply) {
        Write-Host 'Dry run complete. Nothing was written. Re-run with -Apply to write the backfill.' -ForegroundColor Green
        return
    }

    if ($backfill.Count -eq 0) {
        Write-Host 'Nothing to write.' -ForegroundColor Green
        return
    }

    $statements = foreach ($entry in $backfill) {
        $objectId = ConvertTo-SqlGuidLiteral -Value $entry.ResolvedId
        $userId = ConvertTo-SqlGuidLiteral -Value $entry.User.Id
        "UPDATE Users SET EntraId = N'$objectId' WHERE Id = '$userId';"
    }

    # Single transaction: a partial backfill would leave the table in a state where
    # some rows point at the new tenant and some at the old one.
    $updateScript = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
$($statements -join [Environment]::NewLine)
COMMIT TRANSACTION;
"@

    Invoke-SqlQuery -SqlAdminPassword $sqlAdminPassword -Query $updateScript | Out-Null
    Write-Host "Backfilled $($backfill.Count) users." -ForegroundColor Green
}
finally {
    $sqlAdminPassword = $null

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
            throw "Could not remove temporary backfill firewall rule '$firewallRuleName'.`n$($deleteResult.Output)"
        }
    }
}
