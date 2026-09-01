param(
    [Parameter(Mandatory = $true)]
    [Guid]$SourceTenantId,

    [Parameter(Mandatory = $true)]
    [Guid]$SourceWorkslipClientId,

    [Parameter(Mandatory = $true)]
    [Guid]$TargetTenantId,

    [Guid]$TargetWorkslipClientId = [Guid]::Empty,
    [string]$TargetOAuthUniqueName = 'workslip-oauth-server-live',
    [string]$BaseUrl = 'https://app.mrsoftware.dk',
    [string]$SqlResourceGroup = 'rg-mrsoftwarev2-live',
    [string]$SqlServerName = 'db-mrsoftwarev2-live-server',
    [string]$SqlDatabaseName = 'db-mrsoftwarev2-live',
    [string]$OutputPath = '',
    [switch]$IncludeMembers,
    [switch]$Apply,
    [switch]$UpdateSql,
    [string]$Confirmation = ''
)

$ErrorActionPreference = 'Stop'
$graphRoot = 'https://graph.microsoft.com/v1.0'
$managedRoleValues = @('Superadmin', 'Admin', 'User', 'Auditor')

if ($SourceTenantId -eq $TargetTenantId) {
    throw 'SourceTenantId and TargetTenantId must be different tenants.'
}

if ($Apply -and $Confirmation -ne 'MIGRATE_B2B') {
    throw 'Apply mode requires -Confirmation MIGRATE_B2B.'
}

if ($UpdateSql -and -not $Apply) {
    throw '-UpdateSql is only allowed together with -Apply.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PWD 'workslip-entra-b2b-mapping.json'
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
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

function Get-GraphToken {
    param([Parameter(Mandatory = $true)][Guid]$TenantId)

    $result = Invoke-AzureCli -Arguments @(
        'account', 'get-access-token',
        '--tenant', $TenantId.ToString(),
        '--resource', 'https://graph.microsoft.com/',
        '--query', 'accessToken',
        '--output', 'tsv'
    ) -AllowFailure

    if ($result.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.Output)) {
        throw @"
Could not acquire a Microsoft Graph token for tenant $TenantId.
Sign in to that tenant first, for example:
  az login --tenant $TenantId
Then make sure the signed-in administrator can read users/app-role assignments there.
$($result.Output)
"@
    }

    return $result.Output.Trim()
}

function Invoke-Graph {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][ValidateSet('GET', 'POST')][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body = $null
    )

    $args = @(
        'rest',
        '--method', $Method,
        '--uri', $Uri,
        '--headers', "Authorization=Bearer $Token",
        '--only-show-errors',
        '--output', 'json'
    )

    $tempFile = $null
    try {
        if ($null -ne $Body) {
            $tempFile = New-TemporaryFile
            $json = $Body | ConvertTo-Json -Depth 30
            [System.IO.File]::WriteAllText(
                $tempFile.FullName,
                $json,
                [System.Text.UTF8Encoding]::new($false))
            $args += @(
                '--headers', 'Content-Type=application/json',
                '--body', "@$($tempFile.FullName)"
            )
        }

        $result = Invoke-AzureCli -Arguments $args
        if ([string]::IsNullOrWhiteSpace($result.Output)) {
            return $null
        }

        return $result.Output | ConvertFrom-Json
    }
    finally {
        if ($null -ne $tempFile) {
            Remove-Item $tempFile.FullName -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-GraphCollection {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$Uri
    )

    $items = New-Object System.Collections.Generic.List[object]
    $next = $Uri

    while (-not [string]::IsNullOrWhiteSpace($next)) {
        $page = Invoke-Graph -Token $Token -Method GET -Uri $next
        foreach ($item in @($page.value)) {
            $items.Add($item)
        }
        $next = [string]$page.'@odata.nextLink'
    }

    return @($items)
}

function Escape-ODataString {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("'", "''")
}

function Get-GuestUpnPrefix {
    param([Parameter(Mandatory = $true)][string]$Email)
    return $Email.Replace('@', '_') + '#EXT#'
}

function Get-EmailFromGuestUpn {
    param([string]$UserPrincipalName)

    if ([string]::IsNullOrWhiteSpace($UserPrincipalName)) {
        return $null
    }

    $marker = '#EXT#@'
    $markerIndex = $UserPrincipalName.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase)
    if ($markerIndex -le 0) {
        if ($UserPrincipalName -match '^[^@\s]+@[^@\s]+$') {
            return $UserPrincipalName.ToLowerInvariant()
        }
        return $null
    }

    $alias = $UserPrincipalName.Substring(0, $markerIndex)
    $separatorIndex = $alias.LastIndexOf('_')
    if ($separatorIndex -le 0 -or $separatorIndex -eq $alias.Length - 1) {
        return $null
    }

    $localPart = $alias.Substring(0, $separatorIndex)
    $domainPart = $alias.Substring($separatorIndex + 1)
    if (-not $domainPart.Contains('.')) {
        return $null
    }

    return "$localPart@$domainPart".ToLowerInvariant()
}

function Resolve-ExternalEmail {
    param([Parameter(Mandatory = $true)][object]$User)

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace([string]$User.mail)) {
        $candidates.Add(([string]$User.mail).Trim().ToLowerInvariant())
    }
    foreach ($mail in @($User.otherMails)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$mail)) {
            $candidates.Add(([string]$mail).Trim().ToLowerInvariant())
        }
    }

    $fromUpn = Get-EmailFromGuestUpn ([string]$User.userPrincipalName)
    if (-not [string]::IsNullOrWhiteSpace($fromUpn)) {
        $candidates.Add($fromUpn)
    }

    return @($candidates | Where-Object { $_ -match '^[^@\s]+@[^@\s]+$' } | Select-Object -Unique | Select-Object -First 1)[0]
}

function Get-ServicePrincipalByAppId {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][Guid]$AppId,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $filter = [uri]::EscapeDataString("appId eq '$($AppId.ToString())'")
    $response = Invoke-Graph -Token $Token -Method GET -Uri "$graphRoot/servicePrincipals?`$filter=$filter&`$select=id,appId,displayName,appRoles&`$top=2"
    $matches = @($response.value)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one $Description service principal for appId '$AppId'; found $($matches.Count)."
    }
    return $matches[0]
}

function Resolve-TargetWorkslipClientId {
    param([Parameter(Mandatory = $true)][string]$Token)

    if ($TargetWorkslipClientId -ne [Guid]::Empty) {
        return $TargetWorkslipClientId
    }

    $escapedName = Escape-ODataString $TargetOAuthUniqueName
    $application = Invoke-Graph -Token $Token -Method GET -Uri "$graphRoot/applications(uniqueName='$escapedName')?`$select=id,appId,displayName"
    $resolved = [Guid]::Empty
    if ($null -eq $application -or -not [Guid]::TryParse([string]$application.appId, [ref]$resolved)) {
        throw "Could not resolve target Workslip application '$TargetOAuthUniqueName'. Pass -TargetWorkslipClientId explicitly."
    }
    return $resolved
}

function Get-RoleMap {
    param([Parameter(Mandatory = $true)][object]$ServicePrincipal)

    $map = @{}
    foreach ($role in @($ServicePrincipal.appRoles)) {
        $value = [string]$role.value
        if ($managedRoleValues -contains $value -and $role.isEnabled -eq $true -and @($role.allowedMemberTypes) -contains 'User') {
            $map[[string]$role.id] = $value
        }
    }
    return $map
}

function Find-TargetUser {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$Email
    )

    $escapedEmail = Escape-ODataString $Email
    $guestPrefix = Escape-ODataString (Get-GuestUpnPrefix $Email)
    $filter = "mail eq '$escapedEmail' or otherMails/any(m:m eq '$escapedEmail') or userPrincipalName eq '$escapedEmail' or startswith(userPrincipalName,'$guestPrefix')"
    $uri = "$graphRoot/users?`$filter=$([uri]::EscapeDataString($filter))&`$select=id,displayName,userPrincipalName,mail,otherMails,userType&`$top=2"
    $response = Invoke-Graph -Token $Token -Method GET -Uri $uri
    return @($response.value)
}

function New-TargetGuest {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$Email,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $invitation = Invoke-Graph -Token $Token -Method POST -Uri "$graphRoot/invitations" -Body ([ordered]@{
        invitedUserEmailAddress = $Email
        invitedUserDisplayName = $DisplayName
        inviteRedirectUrl = "$($BaseUrl.TrimEnd('/'))/login"
        sendInvitationMessage = $false
    })

    $id = [string]$invitation.invitedUser.id
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($id, [ref]$parsed)) {
        throw "Graph invitation did not return a valid invited user ID for '$Email'."
    }
    return $parsed.ToString()
}

function Ensure-TargetRoleAssignment {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$UserId,
        [Parameter(Mandatory = $true)][string]$ServicePrincipalId,
        [Parameter(Mandatory = $true)][string]$RoleId,
        [Parameter(Mandatory = $true)][string]$RoleValue
    )

    $assignments = Get-GraphCollection -Token $Token -Uri "$graphRoot/users/$UserId/appRoleAssignments?`$select=id,appRoleId,resourceId&`$top=999"
    $exists = @($assignments) | Where-Object {
        [string]$_.resourceId -eq $ServicePrincipalId -and [string]$_.appRoleId -eq $RoleId
    } | Select-Object -First 1

    if ($null -ne $exists) {
        return $false
    }

    if ($Apply) {
        Invoke-Graph -Token $Token -Method POST -Uri "$graphRoot/users/$UserId/appRoleAssignments" -Body ([ordered]@{
            principalId = $UserId
            resourceId = $ServicePrincipalId
            appRoleId = $RoleId
        }) | Out-Null
        Write-Host "Assigned role '$RoleValue' to $UserId." -ForegroundColor DarkGray
    }

    return $true
}

function Update-WorkslipSqlMappings {
    param(
        [Parameter(Mandatory = $true)][array]$Mappings,
        [Parameter(Mandatory = $true)][Guid]$TenantId
    )

    if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
        throw 'Invoke-Sqlcmd is required for -UpdateSql. Install-Module SqlServer first.'
    }

    $sqlToken = (Invoke-AzureCli -Arguments @(
        'account', 'get-access-token',
        '--tenant', $TenantId.ToString(),
        '--resource', 'https://database.windows.net/',
        '--query', 'accessToken',
        '--output', 'tsv'
    )).Output.Trim()

    if ([string]::IsNullOrWhiteSpace($sqlToken)) {
        throw 'Could not acquire an Azure SQL access token for the target tenant.'
    }

    $runnerIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
    $parsedIp = $null
    if (-not [System.Net.IPAddress]::TryParse($runnerIp, [ref]$parsedIp) -or
        $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "Could not resolve a valid public IPv4 address. Received '$runnerIp'."
    }

    $firewallRuleName = "AllowB2BMigration-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $firewallCreated = $false

    try {
        Invoke-AzureCli -Arguments @(
            'sql', 'server', 'firewall-rule', 'create',
            '--resource-group', $SqlResourceGroup,
            '--server', $SqlServerName,
            '--name', $firewallRuleName,
            '--start-ip-address', $runnerIp,
            '--end-ip-address', $runnerIp,
            '--only-show-errors',
            '--output', 'none'
        ) | Out-Null
        $firewallCreated = $true

        $statements = New-Object System.Collections.Generic.List[string]
        $statements.Add('SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;')

        foreach ($mapping in $Mappings) {
            $newId = [Guid]::Empty
            if (-not [Guid]::TryParse([string]$mapping.TargetObjectId, [ref]$newId)) {
                throw "Invalid target Entra object ID in mapping for '$($mapping.Email)'."
            }
            $email = ([string]$mapping.Email).Replace("'", "''")
            $entraId = $newId.ToString()
            $statements.Add(@"
IF (SELECT COUNT(*) FROM dbo.Users WHERE LOWER(Email) = LOWER(N'$email') OR LOWER(EntraEmail) = LOWER(N'$email')) <> 1
    THROW 51030, 'Expected exactly one Workslip user for B2B mapping.', 1;
UPDATE dbo.Users
SET EntraId = N'$entraId', UpdatedAt = SYSUTCDATETIME()
WHERE LOWER(Email) = LOWER(N'$email') OR LOWER(EntraEmail) = LOWER(N'$email');
"@)
        }

        $statements.Add('COMMIT TRANSACTION;')

        Invoke-Sqlcmd `
            -ServerInstance "$SqlServerName.database.windows.net" `
            -Database $SqlDatabaseName `
            -AccessToken $sqlToken `
            -Query ($statements -join [Environment]::NewLine) `
            -QueryTimeout 120 `
            -AbortOnError `
            -ErrorAction Stop | Out-Null
    }
    finally {
        if ($firewallCreated) {
            $delete = Invoke-AzureCli -Arguments @(
                'sql', 'server', 'firewall-rule', 'delete',
                '--resource-group', $SqlResourceGroup,
                '--server', $SqlServerName,
                '--name', $firewallRuleName,
                '--only-show-errors',
                '--output', 'none'
            ) -AllowFailure

            if ($delete.ExitCode -ne 0) {
                throw "B2B migration completed but temporary SQL firewall cleanup failed for '$firewallRuleName'.`n$($delete.Output)"
            }
        }
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required.'
}

Write-Host 'Acquiring tenant-scoped Microsoft Graph tokens...' -ForegroundColor Cyan
$sourceToken = Get-GraphToken $SourceTenantId
$targetToken = Get-GraphToken $TargetTenantId

$targetClientId = Resolve-TargetWorkslipClientId -Token $targetToken
$sourceSp = Get-ServicePrincipalByAppId -Token $sourceToken -AppId $SourceWorkslipClientId -Description 'source Workslip'
$targetSp = Get-ServicePrincipalByAppId -Token $targetToken -AppId $targetClientId -Description 'target Workslip'

$sourceRoleMap = Get-RoleMap $sourceSp
$targetRoleMapByValue = @{}
foreach ($role in @($targetSp.appRoles)) {
    if ($managedRoleValues -contains [string]$role.value -and $role.isEnabled -eq $true -and @($role.allowedMemberTypes) -contains 'User') {
        $targetRoleMapByValue[[string]$role.value] = [string]$role.id
    }
}

foreach ($requiredRole in $managedRoleValues) {
    if (-not $targetRoleMapByValue.ContainsKey($requiredRole)) {
        throw "Target Workslip service principal is missing app role '$requiredRole'."
    }
}

Write-Host 'Reading Workslip app-role assignments from source tenant...' -ForegroundColor Cyan
$sourceAssignments = Get-GraphCollection -Token $sourceToken -Uri "$graphRoot/servicePrincipals/$($sourceSp.id)/appRoleAssignedTo?`$select=principalId,principalDisplayName,principalType,appRoleId&`$top=999"
$sourceAssignments = @($sourceAssignments | Where-Object {
    [string]$_.principalType -eq 'User' -and $sourceRoleMap.ContainsKey([string]$_.appRoleId)
})

$plan = New-Object System.Collections.Generic.List[object]
$blocked = New-Object System.Collections.Generic.List[object]
$seenTargetIds = @{}

foreach ($assignment in $sourceAssignments) {
    $sourceUser = Invoke-Graph -Token $sourceToken -Method GET -Uri "$graphRoot/users/$($assignment.principalId)?`$select=id,displayName,userPrincipalName,mail,otherMails,userType"
    $roleValue = [string]$sourceRoleMap[[string]$assignment.appRoleId]

    if ([string]$sourceUser.userType -ne 'Guest' -and -not $IncludeMembers) {
        $plan.Add([pscustomobject]@{
            Status = 'SkippedMember'; Email = Resolve-ExternalEmail $sourceUser; Role = $roleValue
            SourceObjectId = [string]$sourceUser.id; TargetObjectId = $null; RoleChange = $false
        })
        continue
    }

    $email = Resolve-ExternalEmail $sourceUser
    if ([string]::IsNullOrWhiteSpace($email)) {
        $entry = [pscustomobject]@{
            Status = 'NoEmail'; Email = ''; Role = $roleValue
            SourceObjectId = [string]$sourceUser.id; TargetObjectId = $null; RoleChange = $false
        }
        $blocked.Add($entry); $plan.Add($entry); continue
    }

    $matches = @(Find-TargetUser -Token $targetToken -Email $email)
    if ($matches.Count -gt 1) {
        $entry = [pscustomobject]@{
            Status = 'Ambiguous'; Email = $email; Role = $roleValue
            SourceObjectId = [string]$sourceUser.id; TargetObjectId = $null; RoleChange = $false
        }
        $blocked.Add($entry); $plan.Add($entry); continue
    }

    $created = $false
    if ($matches.Count -eq 0) {
        if ($Apply) {
            $displayName = if (-not [string]::IsNullOrWhiteSpace([string]$sourceUser.displayName)) { [string]$sourceUser.displayName } else { $email }
            $targetObjectId = New-TargetGuest -Token $targetToken -Email $email -DisplayName $displayName
            $created = $true
        }
        else {
            $plan.Add([pscustomobject]@{
                Status = 'WouldInvite'; Email = $email; Role = $roleValue
                SourceObjectId = [string]$sourceUser.id; TargetObjectId = $null; RoleChange = $true
            })
            continue
        }
    }
    else {
        $targetObjectId = [string]$matches[0].id
    }

    if ($seenTargetIds.ContainsKey($targetObjectId)) {
        $entry = [pscustomobject]@{
            Status = 'Conflict'; Email = $email; Role = $roleValue
            SourceObjectId = [string]$sourceUser.id; TargetObjectId = $targetObjectId; RoleChange = $false
        }
        $blocked.Add($entry); $plan.Add($entry); continue
    }
    $seenTargetIds[$targetObjectId] = $email

    $roleChange = Ensure-TargetRoleAssignment `
        -Token $targetToken `
        -UserId $targetObjectId `
        -ServicePrincipalId ([string]$targetSp.id) `
        -RoleId ([string]$targetRoleMapByValue[$roleValue]) `
        -RoleValue $roleValue

    $plan.Add([pscustomobject]@{
        Status = if ($created) { 'Invited' } else { 'Existing' }
        Email = $email
        Role = $roleValue
        SourceObjectId = [string]$sourceUser.id
        TargetObjectId = $targetObjectId
        RoleChange = $roleChange
    })
}

foreach ($entry in $plan) {
    $roleSuffix = if ($entry.RoleChange) { ' + role' } else { '' }
    $colour = if ($entry.Status -in @('NoEmail', 'Ambiguous', 'Conflict')) {
        'Red'
    } elseif ($entry.Status -in @('WouldInvite', 'Invited')) {
        'Yellow'
    } else {
        'DarkGray'
    }
    Write-Host ("{0,-14} {1,-42} {2}{3}" -f $entry.Status, $entry.Email, $entry.Role, $roleSuffix) -ForegroundColor $colour
}

$mapping = @($plan | Where-Object {
    $_.Status -in @('Invited', 'Existing') -and -not [string]::IsNullOrWhiteSpace([string]$_.TargetObjectId)
})

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$mapping | ConvertTo-Json -Depth 10 | Set-Content -Path $OutputPath -Encoding utf8

Write-Host ''
Write-Host "Source assignments: $($sourceAssignments.Count)" -ForegroundColor Cyan
Write-Host "Migratable mapping: $($mapping.Count)" -ForegroundColor Cyan
Write-Host "Blocked:            $($blocked.Count)" -ForegroundColor $(if ($blocked.Count -gt 0) { 'Red' } else { 'DarkGray' })
Write-Host "Mapping file:       $OutputPath" -ForegroundColor DarkGray

if ($blocked.Count -gt 0) {
    throw 'Cross-tenant B2B migration is blocked. Resolve NoEmail/Ambiguous/Conflict rows before applying SQL mappings.'
}

if (-not $Apply) {
    Write-Host 'Preview complete. No target users, app-role assignments or SQL rows were changed.' -ForegroundColor Green
    Write-Host 'Re-run with -Apply -Confirmation MIGRATE_B2B when the plan is correct.' -ForegroundColor Green
    return
}

if ($UpdateSql) {
    Update-WorkslipSqlMappings -Mappings $mapping -TenantId $TargetTenantId
    Write-Host "SQL mapping updated for $($mapping.Count) Workslip users." -ForegroundColor Green
}
else {
    Write-Host 'Target Entra guests/app roles reconciled. SQL was not changed because -UpdateSql was not supplied.' -ForegroundColor Yellow
}

Write-Host 'Cross-tenant Workslip B2B migration completed.' -ForegroundColor Green
