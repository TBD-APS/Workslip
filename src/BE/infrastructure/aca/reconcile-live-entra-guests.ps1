param(
    [switch]$Apply,
    [string]$SqlServerFqdn = 'db-mrsoftwarev2-live-server.database.windows.net',
    [string]$SqlDatabase = 'db-mrsoftwarev2-live',
    [string]$AppConfigurationEndpoint = 'https://appcs-mrsoftwarev2-live.azconfig.io',
    [string]$BaseUrl = 'https://app.mrsoftware.dk'
)

$ErrorActionPreference = 'Stop'
$graphRoot = 'https://graph.microsoft.com/v1.0'
$managedRoles = @('Superadmin', 'Admin', 'User', 'Auditor')

function Invoke-Az {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $output = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) {
        $text = ($output -join [Environment]::NewLine).Trim()
        throw "Azure CLI failed.`n$text"
    }
    return ($output -join [Environment]::NewLine).Trim()
}

function Get-Graph {
    param([Parameter(Mandatory = $true)][string]$Uri)
    return (Invoke-Az @('rest', '--method', 'GET', '--uri', $Uri, '--only-show-errors', '-o', 'json')) | ConvertFrom-Json
}

function Post-Graph {
    param([Parameter(Mandatory = $true)][string]$Uri, [Parameter(Mandatory = $true)][object]$Body)
    $temp = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText(
            $temp.FullName,
            ($Body | ConvertTo-Json -Depth 20),
            [System.Text.UTF8Encoding]::new($false))
        $response = Invoke-Az @(
            'rest', '--method', 'POST', '--uri', $Uri,
            '--headers', 'Content-Type=application/json',
            '--body', "@$($temp.FullName)", '--only-show-errors', '-o', 'json')
        if ([string]::IsNullOrWhiteSpace($response)) { return $null }
        return $response | ConvertFrom-Json
    }
    finally {
        Remove-Item $temp.FullName -Force -ErrorAction SilentlyContinue
    }
}

function Normalize-Role {
    param([string]$Role)
    foreach ($candidate in $managedRoles) {
        if ([string]::Equals($candidate, $Role.Trim(), [StringComparison]::OrdinalIgnoreCase)) { return $candidate }
    }
    return $null
}

function Find-DirectoryUser {
    param([Parameter(Mandatory = $true)][string]$Email)
    $escapedEmail = $Email.Replace("'", "''")
    $guestPrefix = $Email.Replace('@', '_').Replace("'", "''") + '#EXT#'
    $filter = "mail eq '$escapedEmail' or otherMails/any(m:m eq '$escapedEmail') or startswith(userPrincipalName,'$guestPrefix')"
    $uri = "$graphRoot/users?`$filter=$([uri]::EscapeDataString($filter))&`$select=id,userPrincipalName,mail,otherMails,displayName,userType&`$top=2"
    return @((Get-Graph $uri).value)
}

function New-DirectoryGuest {
    param([Parameter(Mandatory = $true)][string]$Email, [Parameter(Mandatory = $true)][string]$DisplayName)
    $invitation = Post-Graph "$graphRoot/invitations" ([ordered]@{
        invitedUserEmailAddress = $Email
        invitedUserDisplayName = $DisplayName
        inviteRedirectUrl = "$($BaseUrl.TrimEnd('/'))/login"
        sendInvitationMessage = $false
    })
    $id = [string]$invitation.invitedUser.id
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($id, [ref]$parsed)) { throw "Graph returned no valid invited user id for '$Email'." }
    return $parsed.ToString()
}

function Test-AppRoleAssignment {
    param([string]$UserId, [string]$ServicePrincipalId, [string]$AppRoleId)
    $response = Get-Graph "$graphRoot/users/$UserId/appRoleAssignments?`$select=id,appRoleId,resourceId"
    return $null -ne (@($response.value) | Where-Object {
        [string]$_.resourceId -eq $ServicePrincipalId -and [string]$_.appRoleId -eq $AppRoleId
    } | Select-Object -First 1)
}

function Add-AppRoleAssignment {
    param([string]$UserId, [string]$ServicePrincipalId, [string]$AppRoleId)
    Post-Graph "$graphRoot/users/$UserId/appRoleAssignments" ([ordered]@{
        principalId = $UserId
        resourceId = $ServicePrincipalId
        appRoleId = $AppRoleId
    }) | Out-Null
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI is required.' }
if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) { throw 'SqlServer PowerShell module is required.' }

$tenantId = (Invoke-Az @('account', 'show', '--query', 'tenantId', '-o', 'tsv')).Trim()
$oauthClientId = (Invoke-Az @(
    'appconfig', 'kv', 'show', '--endpoint', $AppConfigurationEndpoint,
    '--key', 'Azure:AdOAuth:ClientId', '--auth-mode', 'login',
    '--query', 'value', '--only-show-errors', '-o', 'tsv')).Trim()
$parsedClientId = [Guid]::Empty
if (-not [Guid]::TryParse($oauthClientId, [ref]$parsedClientId)) {
    throw "Azure:AdOAuth:ClientId is invalid: '$oauthClientId'."
}

$spFilter = [uri]::EscapeDataString("appId eq '$oauthClientId'")
$servicePrincipals = @((Get-Graph "$graphRoot/servicePrincipals?`$filter=$spFilter&`$select=id,appId,displayName,appRoles&`$top=2").value)
if ($servicePrincipals.Count -ne 1) { throw "Expected one Workslip API service principal; found $($servicePrincipals.Count)." }
$servicePrincipal = $servicePrincipals[0]
$servicePrincipalId = [string]$servicePrincipal.id

$roleIds = @{}
foreach ($roleValue in $managedRoles) {
    $role = @($servicePrincipal.appRoles) | Where-Object {
        [string]$_.value -eq $roleValue -and $_.isEnabled -eq $true -and @($_.allowedMemberTypes) -contains 'User'
    } | Select-Object -First 1
    if ($null -eq $role) { throw "Workslip app role '$roleValue' is missing in the current tenant." }
    $roleIds[$roleValue] = [string]$role.id
}

$sqlToken = (Invoke-Az @(
    'account', 'get-access-token', '--resource', 'https://database.windows.net/',
    '--query', 'accessToken', '-o', 'tsv')).Trim()

$rows = @(Invoke-Sqlcmd -ServerInstance $SqlServerFqdn -Database $SqlDatabase -AccessToken $sqlToken -Query @'
SET NOCOUNT ON;
SELECT CAST(Id AS nvarchar(36)) AS Id,
       ISNULL(Email, N'') AS Email,
       ISNULL(EntraEmail, N'') AS EntraEmail,
       ISNULL(EntraId, N'') AS EntraId,
       ISNULL(DisplayName, N'') AS DisplayName,
       ISNULL(Role, N'') AS Role
FROM Users
ORDER BY Email;
'@ -QueryTimeout 120 -AbortOnError -ErrorAction Stop)

$plan = New-Object System.Collections.Generic.List[object]
$blocked = New-Object System.Collections.Generic.List[string]
$emailOwners = @{}

foreach ($row in $rows) {
    $email = if (-not [string]::IsNullOrWhiteSpace([string]$row.EntraEmail)) {
        ([string]$row.EntraEmail).Trim().ToLowerInvariant()
    } else {
        ([string]$row.Email).Trim().ToLowerInvariant()
    }
    $role = Normalize-Role ([string]$row.Role)

    if ([string]::IsNullOrWhiteSpace($email)) { $blocked.Add("NoEmail: $($row.Id)"); continue }
    if ($null -eq $role) { $blocked.Add("InvalidRole '$($row.Role)': $email"); continue }
    if ($emailOwners.ContainsKey($email)) { $blocked.Add("DuplicateEmail: $email"); continue }
    $emailOwners[$email] = [string]$row.Id

    $matches = @(Find-DirectoryUser $email)
    if ($matches.Count -gt 1) { $blocked.Add("Ambiguous: $email"); continue }

    $resolvedId = if ($matches.Count -eq 1) { [string]$matches[0].id } else { $null }
    if ($null -ne $resolvedId) {
        $parsedResolved = [Guid]::Empty
        if (-not [Guid]::TryParse($resolvedId, [ref]$parsedResolved)) { $blocked.Add("InvalidGraphId: $email"); continue }
        $resolvedId = $parsedResolved.ToString()
    }

    $plan.Add([pscustomobject]@{
        Row = $row
        Email = $email
        Role = $role
        ResolvedId = $resolvedId
        NeedsInvite = [string]::IsNullOrWhiteSpace($resolvedId)
        NeedsRole = $false
    })
}

$resolvedOwners = @{}
foreach ($entry in $plan | Where-Object { -not $_.NeedsInvite }) {
    if ($resolvedOwners.ContainsKey($entry.ResolvedId)) { $blocked.Add("Conflict: $($entry.Email) shares $($entry.ResolvedId)") }
    else { $resolvedOwners[$entry.ResolvedId] = $entry.Email }
}

if ($blocked.Count -gt 0) {
    $blocked | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw 'B2B reconciliation is blocked; no changes were made.'
}

foreach ($entry in $plan | Where-Object { -not $_.NeedsInvite }) {
    $entry.NeedsRole = -not (Test-AppRoleAssignment $entry.ResolvedId $servicePrincipalId $roleIds[$entry.Role])
}

Write-Host "Tenant: $tenantId" -ForegroundColor Cyan
Write-Host "Mode:   $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })" -ForegroundColor $(if ($Apply) { 'Yellow' } else { 'Green' })
foreach ($entry in $plan) {
    $state = if ($entry.NeedsInvite) { 'Invite' } elseif (([string]$entry.Row.EntraId).Trim() -ne $entry.ResolvedId) { 'Backfill' } else { 'Current' }
    $roleState = if ($entry.NeedsInvite -or $entry.NeedsRole) { ' + role' } else { '' }
    Write-Host ("{0,-10} {1,-42} {2}{3}" -f $state, $entry.Email, $entry.Role, $roleState)
}

if (-not $Apply) {
    Write-Host 'Dry run complete. No guest, role or database changes were made.' -ForegroundColor Green
    return
}

foreach ($entry in $plan) {
    if ($entry.NeedsInvite) {
        $displayName = if (-not [string]::IsNullOrWhiteSpace([string]$entry.Row.DisplayName)) { [string]$entry.Row.DisplayName } else { $entry.Email }
        $entry.ResolvedId = New-DirectoryGuest $entry.Email $displayName
        $entry.NeedsRole = $true
    }
    if ($entry.NeedsRole) {
        Add-AppRoleAssignment $entry.ResolvedId $servicePrincipalId $roleIds[$entry.Role]
    }
}

$finalOwners = @{}
foreach ($entry in $plan) {
    if ($finalOwners.ContainsKey($entry.ResolvedId)) { throw "Resolved Entra conflict after invitation: $($entry.ResolvedId). Database was not changed." }
    $finalOwners[$entry.ResolvedId] = $entry.Email
}

$updates = @($plan | Where-Object { ([string]$_.Row.EntraId).Trim() -ne $_.ResolvedId })
if ($updates.Count -gt 0) {
    $sql = New-Object System.Collections.Generic.List[string]
    $sql.Add('SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;')
    foreach ($entry in $updates) {
        $workslipId = [Guid]::Empty
        $entraId = [Guid]::Empty
        if (-not [Guid]::TryParse([string]$entry.Row.Id, [ref]$workslipId)) { throw 'Invalid Workslip user id.' }
        if (-not [Guid]::TryParse([string]$entry.ResolvedId, [ref]$entraId)) { throw 'Invalid Entra user id.' }
        $sql.Add("UPDATE Users SET EntraId = N'$($entraId.ToString())' WHERE Id = '$($workslipId.ToString())';")
    }
    $sql.Add('COMMIT TRANSACTION;')
    Invoke-Sqlcmd -ServerInstance $SqlServerFqdn -Database $SqlDatabase -AccessToken $sqlToken -Query ($sql -join [Environment]::NewLine) -QueryTimeout 120 -AbortOnError -ErrorAction Stop | Out-Null
}

Write-Host "B2B guest reconciliation complete. Users=$($plan.Count), EntraId updates=$($updates.Count)." -ForegroundColor Green
