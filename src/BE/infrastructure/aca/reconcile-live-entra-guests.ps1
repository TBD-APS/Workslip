param(
    [switch]$Apply,
    [string]$SqlServerFqdn = 'db-mrsoftwarev2-live-server.database.windows.net',
    [string]$SqlDatabase = 'db-mrsoftwarev2-live',
    [string]$AppConfigurationName = 'appcs-mrsoftwarev2-live',
    [string]$BaseUrl = 'https://app.mrsoftware.dk'
)

$ErrorActionPreference = 'Stop'
$graphRoot = 'https://graph.microsoft.com/v1.0'
$managedRoleValues = @('Superadmin', 'Admin', 'User', 'Auditor')

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ([string]::IsNullOrWhiteSpace($text)) { $text = 'Azure CLI returned no diagnostic output.' }
        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Output = $text }
}

function Invoke-GraphGet {
    param([Parameter(Mandatory = $true)][string]$Uri)
    $result = Invoke-AzureCli -Arguments @(
        'rest', '--method', 'GET', '--uri', $Uri,
        '--only-show-errors', '-o', 'json'
    )
    return $result.Output | ConvertFrom-Json
}

function Invoke-GraphPost {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][object]$Body
    )

    $temp = New-TemporaryFile
    try {
        $json = $Body | ConvertTo-Json -Depth 20
        [System.IO.File]::WriteAllText($temp.FullName, $json, [System.Text.UTF8Encoding]::new($false))
        $result = Invoke-AzureCli -Arguments @(
            'rest', '--method', 'POST', '--uri', $Uri,
            '--headers', 'Content-Type=application/json',
            '--body', "@$($temp.FullName)",
            '--only-show-errors', '-o', 'json'
        )
        if ([string]::IsNullOrWhiteSpace($result.Output)) { return $null }
        return $result.Output | ConvertFrom-Json
    }
    finally {
        Remove-Item $temp.FullName -Force -ErrorAction SilentlyContinue
    }
}

function Escape-ODataString {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("'", "''")
}

function Get-GuestUpnPrefix {
    param([Parameter(Mandatory = $true)][string]$Email)
    return $Email.Replace('@', '_') + '#EXT#'
}

function Get-CanonicalRole {
    param([Parameter(Mandatory = $true)][string]$Role)
    foreach ($candidate in $managedRoleValues) {
        if ([string]::Equals($candidate, $Role.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
            return $candidate
        }
    }
    return $null
}

function Find-EntraUser {
    param([Parameter(Mandatory = $true)][string]$Email)

    $escapedEmail = Escape-ODataString $Email
    $escapedGuestPrefix = Escape-ODataString (Get-GuestUpnPrefix $Email)
    $filter = "mail eq '$escapedEmail' or otherMails/any(m:m eq '$escapedEmail') or startswith(userPrincipalName,'$escapedGuestPrefix')"
    $uri = "$graphRoot/users?`$filter=$([uri]::EscapeDataString($filter))&`$select=id,userPrincipalName,mail,otherMails,displayName,userType&`$top=2"
    $response = Invoke-GraphGet $uri
    return @($response.value)
}

function New-GuestInvitation {
    param(
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $invitation = Invoke-GraphPost -Uri "$graphRoot/invitations" -Body ([ordered]@{
        invitedUserEmailAddress = $Email
        invitedUserDisplayName = $DisplayName
        inviteRedirectUrl = "$($BaseUrl.TrimEnd('/'))/login"
        sendInvitationMessage = $false
    })

    $id = [string]$invitation.invitedUser.id
    if ([string]::IsNullOrWhiteSpace($id)) {
        throw "Microsoft Graph created no invitedUser id for '$Email'."
    }
    return $id
}

function Ensure-AppRoleAssignment {
    param(
        [Parameter(Mandatory = $true)][string]$UserId,
        [Parameter(Mandatory = $true)][string]$RoleValue,
        [Parameter(Mandatory = $true)][string]$ServicePrincipalId,
        [Parameter(Mandatory = $true)][string]$AppRoleId
    )

    $assignments = Invoke-GraphGet "$graphRoot/users/$UserId/appRoleAssignments?`$select=id,appRoleId,resourceId"
    $alreadyAssigned = @($assignments.value) | Where-Object {
        [string]$_.resourceId -eq $ServicePrincipalId -and [string]$_.appRoleId -eq $AppRoleId
    } | Select-Object -First 1

    if ($null -ne $alreadyAssigned) {
        return $false
    }

    if (-not $Apply) {
        return $true
    }

    Invoke-GraphPost -Uri "$graphRoot/users/$UserId/appRoleAssignments" -Body ([ordered]@{
        principalId = $UserId
        resourceId = $ServicePrincipalId
        appRoleId = $AppRoleId
    }) | Out-Null

    Write-Host "Assigned Workslip role '$RoleValue' to Entra guest $UserId." -ForegroundColor DarkGray
    return $true
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required.'
}
if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'Invoke-Sqlcmd is required. Install the SqlServer PowerShell module first.'
}

$tenantId = (Invoke-AzureCli -Arguments @('account', 'show', '--query', 'tenantId', '-o', 'tsv')).Output.Trim()
if ([string]::IsNullOrWhiteSpace($tenantId)) { throw 'The signed-in Entra tenant could not be resolved.' }

$oauthClientId = (Invoke-AzureCli -Arguments @(
    'appconfig', 'kv', 'show',
    '--name', $AppConfigurationName,
    '--key', 'Azure:AdOAuth:ClientId',
    '--auth-mode', 'login',
    '--query', 'value',
    '--only-show-errors', '-o', 'tsv'
)).Output.Trim()
if (-not [Guid]::TryParse($oauthClientId, [ref]([Guid]::Empty))) {
    throw "App Configuration returned an invalid Azure:AdOAuth:ClientId: '$oauthClientId'."
}

$spFilter = [uri]::EscapeDataString("appId eq '$oauthClientId'")
$spResponse = Invoke-GraphGet "$graphRoot/servicePrincipals?`$filter=$spFilter&`$select=id,appId,displayName,appRoles&`$top=2"
$servicePrincipals = @($spResponse.value)
if ($servicePrincipals.Count -ne 1) {
    throw "Expected exactly one Workslip API service principal for appId '$oauthClientId'; found $($servicePrincipals.Count)."
}
$servicePrincipal = $servicePrincipals[0]
$servicePrincipalId = [string]$servicePrincipal.id

$roleIds = @{}
foreach ($roleValue in $managedRoleValues) {
    $role = @($servicePrincipal.appRoles) | Where-Object {
        [string]$_.value -eq $roleValue -and $_.isEnabled -eq $true -and @($_.allowedMemberTypes) -contains 'User'
    } | Select-Object -First 1
    if ($null -eq $role -or [string]::IsNullOrWhiteSpace([string]$role.id)) {
        throw "Workslip app role '$roleValue' is missing from service principal '$servicePrincipalId'."
    }
    $roleIds[$roleValue] = [string]$role.id
}

$sqlAccessToken = (Invoke-AzureCli -Arguments @(
    'account', 'get-access-token',
    '--resource', 'https://database.windows.net/',
    '--query', 'accessToken', '-o', 'tsv'
)).Output.Trim()
if ([string]::IsNullOrWhiteSpace($sqlAccessToken)) { throw 'Could not acquire an Azure SQL access token.' }

$users = @(Invoke-Sqlcmd `
    -ServerInstance $SqlServerFqdn `
    -Database $SqlDatabase `
    -AccessToken $sqlAccessToken `
    -Query @'
SET NOCOUNT ON;
SELECT
    CAST(Id AS nvarchar(36)) AS Id,
    ISNULL(Email, N'') AS Email,
    ISNULL(EntraEmail, N'') AS EntraEmail,
    ISNULL(EntraId, N'') AS EntraId,
    ISNULL(DisplayName, N'') AS DisplayName,
    ISNULL(Role, N'') AS Role
FROM Users
ORDER BY Email;
'@ `
    -QueryTimeout 120 `
    -AbortOnError `
    -ErrorAction Stop)

Write-Host "Tenant:   $tenantId" -ForegroundColor Cyan
Write-Host "Users:    $($users.Count)" -ForegroundColor Cyan
Write-Host "Mode:     $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })" -ForegroundColor $(if ($Apply) { 'Yellow' } else { 'Green' })
Write-Host ''

$resolved = @{}
$plan = New-Object System.Collections.Generic.List[object]
$blocked = New-Object System.Collections.Generic.List[object]

foreach ($user in $users) {
    $email = if (-not [string]::IsNullOrWhiteSpace([string]$user.EntraEmail)) {
        ([string]$user.EntraEmail).Trim().ToLowerInvariant()
    } else {
        ([string]$user.Email).Trim().ToLowerInvariant()
    }
    $canonicalRole = Get-CanonicalRole ([string]$user.Role)

    if ([string]::IsNullOrWhiteSpace($email)) {
        $entry = [pscustomobject]@{ User = $user; Email = $email; Status = 'NoEmail'; ResolvedId = $null; Role = $canonicalRole; RoleChange = $false }
        $blocked.Add($entry); $plan.Add($entry); continue
    }
    if ($null -eq $canonicalRole) {
        $entry = [pscustomobject]@{ User = $user; Email = $email; Status = 'InvalidRole'; ResolvedId = $null; Role = [string]$user.Role; RoleChange = $false }
        $blocked.Add($entry); $plan.Add($entry); continue
    }

    $matches = @(Find-EntraUser $email)
    if ($matches.Count -gt 1) {
        $entry = [pscustomobject]@{ User = $user; Email = $email; Status = 'Ambiguous'; ResolvedId = $null; Role = $canonicalRole; RoleChange = $false }
        $blocked.Add($entry); $plan.Add($entry); continue
    }

    $created = $false
    if ($matches.Count -eq 0) {
        if (-not $Apply) {
            $entry = [pscustomobject]@{ User = $user; Email = $email; Status = 'Invite'; ResolvedId = $null; Role = $canonicalRole; RoleChange = $true }
            $plan.Add($entry); continue
        }

        $displayName = if (-not [string]::IsNullOrWhiteSpace([string]$user.DisplayName)) { [string]$user.DisplayName } else { $email }
        $resolvedId = New-GuestInvitation -Email $email -DisplayName $displayName
        $created = $true
    } else {
        $resolvedId = [string]$matches[0].id
    }

    $parsedResolved = [Guid]::Empty
    if (-not [Guid]::TryParse($resolvedId, [ref]$parsedResolved)) {
        throw "Graph returned invalid object id '$resolvedId' for '$email'."
    }

    if ($resolved.ContainsKey($resolvedId)) {
        $entry = [pscustomobject]@{ User = $user; Email = $email; Status = 'Conflict'; ResolvedId = $resolvedId; Role = $canonicalRole; RoleChange = $false }
        $blocked.Add($entry); $plan.Add($entry); continue
    }
    $resolved[$resolvedId] = [string]$user.Id

    $roleChange = Ensure-AppRoleAssignment `
        -UserId $resolvedId `
        -RoleValue $canonicalRole `
        -ServicePrincipalId $servicePrincipalId `
        -AppRoleId $roleIds[$canonicalRole]

    $currentId = ([string]$user.EntraId).Trim()
    $status = if ($created) { 'Invited' } elseif ($currentId -eq $resolvedId) { 'Current' } else { 'Backfill' }
    $plan.Add([pscustomobject]@{
        User = $user
        Email = $email
        Status = $status
        ResolvedId = $resolvedId
        Role = $canonicalRole
        RoleChange = $roleChange
    })
}

foreach ($entry in $plan) {
    $roleNote = if ($entry.RoleChange) { ' + role' } else { '' }
    Write-Host ("{0,-12} {1,-42} {2}{3}" -f $entry.Status, $entry.Email, $entry.Role, $roleNote) -ForegroundColor $(
        if ($entry.Status -in @('Ambiguous', 'Conflict', 'NoEmail', 'InvalidRole')) { 'Red' }
        elseif ($entry.Status -in @('Invite', 'Invited', 'Backfill')) { 'Yellow' }
        else { 'DarkGray' }
    )
}

Write-Host ''
Write-Host "Blocked:  $($blocked.Count)" -ForegroundColor $(if ($blocked.Count) { 'Red' } else { 'DarkGray' })
Write-Host "Invites:  $(@($plan | Where-Object { $_.Status -in @('Invite', 'Invited') }).Count)" -ForegroundColor Yellow
Write-Host "Backfill: $(@($plan | Where-Object { $_.Status -eq 'Backfill' }).Count)" -ForegroundColor Yellow

if ($blocked.Count -gt 0) {
    throw 'B2B guest reconciliation is blocked. Resolve Ambiguous/Conflict/NoEmail/InvalidRole rows before applying database changes.'
}

if (-not $Apply) {
    Write-Host 'Dry run complete. No Entra or database changes were made.' -ForegroundColor Green
    return
}

$updates = @($plan | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.ResolvedId) -and ([string]$_.User.EntraId).Trim() -ne [string]$_.ResolvedId
})

if ($updates.Count -gt 0) {
    $statements = New-Object System.Collections.Generic.List[string]
    $statements.Add('SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;')
    foreach ($entry in $updates) {
        $workslipId = [Guid]::Empty
        $entraId = [Guid]::Empty
        if (-not [Guid]::TryParse([string]$entry.User.Id, [ref]$workslipId)) { throw 'Invalid Workslip user GUID in reconciliation plan.' }
        if (-not [Guid]::TryParse([string]$entry.ResolvedId, [ref]$entraId)) { throw 'Invalid Entra GUID in reconciliation plan.' }
        $statements.Add("UPDATE Users SET EntraId = N'$($entraId.ToString())' WHERE Id = '$($workslipId.ToString())';")
    }
    $statements.Add('COMMIT TRANSACTION;')

    Invoke-Sqlcmd `
        -ServerInstance $SqlServerFqdn `
        -Database $SqlDatabase `
        -AccessToken $sqlAccessToken `
        -Query ($statements -join [Environment]::NewLine) `
        -QueryTimeout 120 `
        -AbortOnError `
        -ErrorAction Stop | Out-Null
}

Write-Host "B2B reconciliation complete. Updated $($updates.Count) Workslip Entra object IDs." -ForegroundColor Green
