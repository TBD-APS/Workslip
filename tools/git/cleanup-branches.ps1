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

function Get-GitHubRepositoryFromRemoteUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    if ($Url -match '^https?://github\.com/([^/]+)/([^/]+)/?$') {
        $owner = $Matches[1]
        $repository = $Matches[2] -replace '\.git$', ''
        return "$owner/$repository"
    }

    if ($Url -match '^git@github\.com:([^/]+)/(.+)$') {
        $owner = $Matches[1]
        $repository = $Matches[2] -replace '\.git$', ''
        return "$owner/$repository"
    }

    if ($Url -match '^ssh://git@github\.com/([^/]+)/([^/]+)/?$') {
        $owner = $Matches[1]
        $repository = $Matches[2] -replace '\.git$', ''
        return "$owner/$repository"
    }

    throw "Remote URL '$Url' is not a supported github.com repository URL. Refusing cleanup because open-PR protection cannot be verified."
}

$git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1

if ($null -eq $git) {
    throw 'git is required but was not found on PATH.'
}

$gh = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1

if ($null -eq $gh) {
    throw 'GitHub CLI (gh) is required so branches referenced by open pull requests can be protected. Install/authenticate gh before running cleanup.'
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

function Invoke-Gh {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & $gh.Source @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $details = ($output | Out-String).Trim()
        throw "gh $($Arguments -join ' ') failed. Refusing cleanup because open-PR protection could not be verified. $details"
    }

    return @($output)
}

function Get-OpenPullRequestBranches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    $jsonLines = Invoke-Gh -Arguments @(
        'pr', 'list',
        '--repo', $Repository,
        '--state', 'open',
        '--limit', '1000',
        '--json', 'headRefName,baseRefName'
    )

    $json = ($jsonLines -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw 'GitHub CLI returned no PR data. Refusing cleanup because open-PR protection could not be verified.'
    }

    try {
        $pullRequests = @($json | ConvertFrom-Json -ErrorAction Stop)
    }
    catch {
        throw "Could not parse GitHub PR data. Refusing cleanup. $($_.Exception.Message)"
    }

    return @(
        $pullRequests |
            ForEach-Object { @([string]$_.headRefName, [string]$_.baseRefName) } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
}

$insideWorkTree = (Invoke-Git -Arguments @('rev-parse', '--is-inside-work-tree') | Select-Object -First 1).Trim()
if ($insideWorkTree -ne 'true') {
    throw 'Run this script from inside a Git worktree.'
}

$remoteUrl = (Invoke-Git -Arguments @('remote', 'get-url', $Remote) | Select-Object -First 1).Trim()
$githubRepository = Get-GitHubRepositoryFromRemoteUrl -Url $remoteUrl
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

$openPullRequestBranches = @(Get-OpenPullRequestBranches -Repository $githubRepository)

$protectedBranches = @(
    $branches | Where-Object { Test-ProtectedBranch -Branch $_ }
)

$openPullRequestBranchesOnRemote = @(
    $branches |
        Where-Object {
            -not (Test-ProtectedBranch -Branch $_) -and
            $openPullRequestBranches -contains $_
        }
)

$branchesToDelete = @(
    $branches |
        Where-Object {
            -not (Test-ProtectedBranch -Branch $_) -and
            $openPullRequestBranches -notcontains $_
        }
)

Write-Host ''
Write-Host 'Workslip branch cleanup' -ForegroundColor Cyan
Write-Host "  Remote:     $Remote"
Write-Host "  Repository: $githubRepository"
Write-Host "  URL:        $remoteUrl"
Write-Host "  Mode:       $(if ($Execute) { 'EXECUTE' } else { 'DRY RUN' })"
Write-Host ''

Write-Host 'KEEP - protected branches:' -ForegroundColor Green
$protectedBranches | ForEach-Object { Write-Host "  KEEP   $_" }

Write-Host ''
Write-Host 'KEEP - branches referenced by open pull requests:' -ForegroundColor Green
if ($openPullRequestBranchesOnRemote.Count -eq 0) {
    Write-Host '  (none)'
} else {
    $openPullRequestBranchesOnRemote | ForEach-Object { Write-Host "  KEEP   $_" }
}

Write-Host ''
Write-Host 'DELETE - remote branches not protected by name or an open pull request:' -ForegroundColor Yellow
if ($branchesToDelete.Count -eq 0) {
    Write-Host '  (none)'
} else {
    $branchesToDelete | ForEach-Object { Write-Host "  DELETE $_" }
}

Write-Host ''
Write-Host "Protected by name:    $($protectedBranches.Count)"
Write-Host "Protected by open PR: $($openPullRequestBranchesOnRemote.Count)"
Write-Host "To delete:            $($branchesToDelete.Count)"

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

# Re-read PR state immediately before destructive work. If any deletion target
# becomes referenced by an open PR after the initial classification, abort and
# require a new dry-run/execute cycle.
$openPullRequestBranchesBeforeDelete = @(Get-OpenPullRequestBranches -Repository $githubRepository)
$newlyProtectedBranches = @(
    $branchesToDelete |
        Where-Object { $openPullRequestBranchesBeforeDelete -contains $_ }
)

if ($newlyProtectedBranches.Count -gt 0) {
    throw "Open PR state changed after classification. These branches are now protected: $($newlyProtectedBranches -join ', '). Re-run the cleanup."
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

$expectedPreservedBranches = @(
    (@($protectedBranches) + @($openPullRequestBranchesOnRemote)) |
        Sort-Object -Unique
)

$missingPreservedBranches = @(
    $expectedPreservedBranches |
        Where-Object { $remainingBranches -notcontains $_ }
)

if ($missingPreservedBranches.Count -gt 0) {
    throw "Post-check failed. Expected preserved branches are missing from '$Remote': $($missingPreservedBranches -join ', ')"
}

Write-Host ''
Write-Host "Cleanup complete. Deleted $($deletedBranches.Count) remote branches." -ForegroundColor Green
Write-Host 'main, release branches, and branches referenced by open pull requests were preserved.' -ForegroundColor Green
