[CmdletBinding()]
param(
    [string]$Remote = 'origin',
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-ProtectedBranch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Branch
    )

    return $Branch -eq 'main' `
        -or $Branch -eq 'release' `
        -or $Branch.StartsWith('release/', [System.StringComparison]::Ordinal) `
        -or $Branch.StartsWith('release-', [System.StringComparison]::Ordinal)
}

$git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1

if ($null -eq $git) {
    throw 'git is required but was not found on PATH.'
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & $git.Source @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $details = ($output | Out-String).Trim()
        throw "git $($Arguments -join ' ') failed. $details"
    }

    return @($output)
}

$insideWorkTree = (Invoke-Git -Arguments @('rev-parse', '--is-inside-work-tree') | Select-Object -First 1).Trim()
if ($insideWorkTree -ne 'true') {
    throw 'Run this script from inside a Git worktree.'
}

$remoteUrl = (Invoke-Git -Arguments @('remote', 'get-url', $Remote) | Select-Object -First 1).Trim()
$currentBranchOutput = @(Invoke-Git -Arguments @('branch', '--show-current'))
$currentBranch = if ($currentBranchOutput.Count -gt 0) {
    ([string]$currentBranchOutput[0]).Trim()
} else {
    ''
}

$remoteHeads = Invoke-Git -Arguments @('ls-remote', '--heads', $Remote)
$branches = @(
    foreach ($line in $remoteHeads) {
        if ($line -match 'refs/heads/(.+)$') {
            $Matches[1]
        }
    }
) | Sort-Object -Unique

if ($branches -notcontains 'main') {
    throw "Remote '$Remote' does not contain main. Refusing branch cleanup for $remoteUrl."
}

$protectedBranches = @(
    $branches | Where-Object { Test-ProtectedBranch -Branch $_ }
)

$branchesToDelete = @(
    $branches | Where-Object { -not (Test-ProtectedBranch -Branch $_) }
)

Write-Host ''
Write-Host 'Workslip branch cleanup' -ForegroundColor Cyan
Write-Host "  Remote: $Remote"
Write-Host "  URL:    $remoteUrl"
Write-Host "  Mode:   $(if ($Execute) { 'EXECUTE' } else { 'DRY RUN' })"
Write-Host ''

Write-Host 'KEEP - protected branches:' -ForegroundColor Green
$protectedBranches | ForEach-Object { Write-Host "  KEEP   $_" }

Write-Host ''
Write-Host 'DELETE - all other remote branches:' -ForegroundColor Yellow
if ($branchesToDelete.Count -eq 0) {
    Write-Host '  (none)'
} else {
    $branchesToDelete | ForEach-Object { Write-Host "  DELETE $_" }
}

Write-Host ''
Write-Host "Protected: $($protectedBranches.Count)"
Write-Host "To delete: $($branchesToDelete.Count)"

if (-not $Execute) {
    Write-Host ''
    Write-Host 'DRY RUN ONLY: no remote branches were changed.' -ForegroundColor Cyan
    Write-Host 'Re-run with -Execute after reviewing the list.' -ForegroundColor Cyan
    return
}

if ([string]::IsNullOrWhiteSpace($currentBranch)) {
    throw 'Execute mode is not allowed from a detached HEAD. Check out main or a release branch first.'
}

if (-not (Test-ProtectedBranch -Branch $currentBranch)) {
    throw "Execute mode is only allowed while checked out on main or a release branch. Current branch: $currentBranch"
}

if ($branchesToDelete.Count -eq 0) {
    Write-Host ''
    Write-Host 'Nothing to delete.' -ForegroundColor Green
    return
}

Write-Host ''
Write-Host 'DESTRUCTIVE MODE: deleting remote branches...' -ForegroundColor Red

$failedBranches = [System.Collections.Generic.List[string]]::new()
$deletedBranches = [System.Collections.Generic.List[string]]::new()

foreach ($branch in $branchesToDelete) {
    $output = & $git.Source push $Remote --delete $branch 2>&1
    if ($LASTEXITCODE -ne 0) {
        $failedBranches.Add($branch)
        Write-Warning "Failed to delete '$branch': $(($output | Out-String).Trim())"
        continue
    }

    $deletedBranches.Add($branch)
    Write-Host "  DELETED $branch"
}

if ($failedBranches.Count -gt 0) {
    throw "Branch cleanup partially failed. Deleted $($deletedBranches.Count); failed $($failedBranches.Count): $($failedBranches -join ', ')"
}

$remainingHeads = Invoke-Git -Arguments @('ls-remote', '--heads', $Remote)
$remainingBranches = @(
    foreach ($line in $remainingHeads) {
        if ($line -match 'refs/heads/(.+)$') {
            $Matches[1]
        }
    }
) | Sort-Object -Unique

$unexpectedRemaining = @(
    $branchesToDelete | Where-Object { $remainingBranches -contains $_ }
)

if ($unexpectedRemaining.Count -gt 0) {
    throw "Post-check failed. These branches still exist on '$Remote': $($unexpectedRemaining -join ', ')"
}

Write-Host ''
Write-Host "Cleanup complete. Deleted $($deletedBranches.Count) remote branches." -ForegroundColor Green
Write-Host 'main and release branches were preserved.' -ForegroundColor Green
