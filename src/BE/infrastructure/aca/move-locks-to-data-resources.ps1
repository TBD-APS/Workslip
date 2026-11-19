#Requires -Version 7
<#
.SYNOPSIS
Protects the data, not the whole subscription: puts a CanNotDelete lock on every SQL database,
storage account and key vault, then removes CanNotDelete locks on the subscription and on
resource groups.

.DESCRIPTION
A CanNotDelete lock on a subscription or resource group is inherited by everything below it,
including the temporary SQL firewall rule that run-database-migrations.ps1 adds and must delete
again. With such a lock, a live deploy stops after the schema migration and before the new app
revision (see Docs/operations/live-resource-locks.md).

Without -Apply the script only prints what it would do. With -Apply it:
1. creates a CanNotDelete lock on each SQL database (except master), storage account and key
   vault that has no CanNotDelete or ReadOnly lock of its own;
2. re-reads the locks and stops unless every data resource is now locked;
3. deletes CanNotDelete locks whose scope is the subscription or a resource group;
4. deletes leftover 'AllowDatabaseMigration-*' firewall rules from failed migration runs.
ReadOnly locks are reported and never changed.

Run it as a user who may manage locks (Owner or User Access Administrator).

.EXAMPLE
./move-locks-to-data-resources.ps1            # dry run
./move-locks-to-data-resources.ps1 -Apply     # make the changes
#>
param(
    [switch]$Apply,
    [string]$SubscriptionId,
    [string]$LockName = 'protect-data'
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $output = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) {
        throw "az $($Arguments -join ' ') failed.`n$($output -join [Environment]::NewLine)"
    }
    ($output -join [Environment]::NewLine).Trim()
}

function Get-LockScope([string]$LockId) {
    ($LockId -split '/providers/Microsoft.Authorization/locks/')[0]
}

if ($SubscriptionId) {
    Invoke-Az @('account', 'set', '--subscription', $SubscriptionId) | Out-Null
}
$subscription = Invoke-Az @('account', 'show', '--query', '{id:id, name:name}', '--output', 'json') | ConvertFrom-Json
$subscriptionScope = "/subscriptions/$($subscription.id)"
Write-Host "Subscription: $($subscription.name) ($($subscription.id))"
Write-Host ($Apply ? 'Mode: APPLY' : 'Mode: dry run (add -Apply to make the changes)')

$dataResources = @()
foreach ($type in @('Microsoft.Sql/servers/databases', 'Microsoft.Storage/storageAccounts', 'Microsoft.KeyVault/vaults')) {
    $found = Invoke-Az @('resource', 'list', '--resource-type', $type, '--query', '[].{id:id, name:name, type:type}', '--output', 'json') | ConvertFrom-Json
    $dataResources += @($found | Where-Object { -not ($_.type -eq 'Microsoft.Sql/servers/databases' -and $_.name -like '*/master') })
}

function Get-Locks { @(Invoke-Az @('lock', 'list', '--output', 'json') | ConvertFrom-Json) }
$locks = Get-Locks

function Test-OwnLock($Resource, $Locks) {
    @($Locks | Where-Object {
        (Get-LockScope $_.id) -ieq $Resource.id -and $_.level -in @('CanNotDelete', 'ReadOnly')
    }).Count -gt 0
}

$toLock = @($dataResources | Where-Object { -not (Test-OwnLock $_ $locks) })
$broadLocks = @($locks | Where-Object {
    $scope = Get-LockScope $_.id
    $isSubscription = $scope -ieq $subscriptionScope
    $isResourceGroup = $scope -match '^/subscriptions/[^/]+/resourceGroups/[^/]+$'
    ($isSubscription -or $isResourceGroup)
})
$toRemove = @($broadLocks | Where-Object level -eq 'CanNotDelete')
$readOnlyBroad = @($broadLocks | Where-Object level -eq 'ReadOnly')

Write-Host "`nData resources: $($dataResources.Count), already locked: $($dataResources.Count - $toLock.Count)"
$toLock | ForEach-Object { Write-Host "  lock      $($_.type)  $($_.name)" }
Write-Host "`nSubscription and resource-group locks to remove: $($toRemove.Count)"
$toRemove | ForEach-Object { Write-Host "  unlock    $($_.level) '$($_.name)' on $(Get-LockScope $_.id)" }
$readOnlyBroad | ForEach-Object { Write-Warning "ReadOnly lock '$($_.name)' on $(Get-LockScope $_.id) is left alone; it also blocks deploys." }

$sqlServers = @(Invoke-Az @('sql', 'server', 'list', '--query', '[].{name:name, resourceGroup:resourceGroup}', '--output', 'json') | ConvertFrom-Json)
$staleRules = @()
foreach ($server in $sqlServers) {
    $names = @(Invoke-Az @('sql', 'server', 'firewall-rule', 'list', '--resource-group', $server.resourceGroup, '--server', $server.name,
        '--query', "[?starts_with(name, 'AllowDatabaseMigration-')].name", '--output', 'json') | ConvertFrom-Json)
    $staleRules += @($names | ForEach-Object { [pscustomobject]@{ Server = $server.name; ResourceGroup = $server.resourceGroup; Name = $_ } })
}
Write-Host "`nLeftover migration firewall rules: $($staleRules.Count)"
$staleRules | ForEach-Object { Write-Host "  delete    $($_.Name) on $($_.Server)" }

if (-not $Apply) {
    Write-Host "`nDry run only. Nothing changed."
    return
}

foreach ($resource in $toLock) {
    Invoke-Az @('lock', 'create', '--name', $LockName, '--lock-type', 'CanNotDelete', '--resource', $resource.id,
        '--notes', 'Protects the data. Subscription and resource-group locks were moved here so deploys can clean up temporary firewall rules.') | Out-Null
    Write-Host "Locked $($resource.name)"
}

$locks = Get-Locks
$unprotected = @($dataResources | Where-Object { -not (Test-OwnLock $_ $locks) })
if ($unprotected.Count -gt 0) {
    throw "Stopped before removing any lock: these data resources are still unlocked: $(($unprotected | ForEach-Object name) -join ', ')"
}

foreach ($lock in $toRemove) {
    Invoke-Az @('lock', 'delete', '--ids', $lock.id) | Out-Null
    Write-Host "Removed $($lock.level) '$($lock.name)' on $(Get-LockScope $lock.id)"
}

foreach ($rule in $staleRules) {
    Invoke-Az @('sql', 'server', 'firewall-rule', 'delete', '--resource-group', $rule.ResourceGroup, '--server', $rule.Server,
        '--name', $rule.Name, '--only-show-errors', '--output', 'none') | Out-Null
    Write-Host "Deleted firewall rule $($rule.Name) on $($rule.Server)"
}

Write-Host "`nDone. Data resources are locked; subscription and resource-group CanNotDelete locks are gone."
