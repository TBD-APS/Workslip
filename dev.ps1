$forwardArgs = @($args | Where-Object { $_ -ne '-Main' })
$switchToMain = $args -contains '-Main'
$repoRoot = $PSScriptRoot

if ($switchToMain) {
    $git = Get-Command git -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $git) {
        throw 'git is required to use -Main.'
    }

    $status = @(& $git.Source -C $repoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the Git worktree before switching to main.'
    }

    if ($status.Count -gt 0) {
        throw "Cannot switch to main because the worktree has local changes. Commit, stash, or discard them explicitly first.`n$($status -join [Environment]::NewLine)"
    }

    Write-Host '[....] Fetching origin/main' -ForegroundColor Cyan
    & $git.Source -C $repoRoot fetch origin main
    if ($LASTEXITCODE -ne 0) {
        throw 'git fetch origin main failed.'
    }

    Write-Host '[....] Switching to main' -ForegroundColor Cyan
    & $git.Source -C $repoRoot switch main
    if ($LASTEXITCODE -ne 0) {
        throw 'git switch main failed.'
    }

    Write-Host '[....] Fast-forwarding main from origin/main' -ForegroundColor Cyan
    & $git.Source -C $repoRoot merge --ff-only origin/main
    if ($LASTEXITCODE -ne 0) {
        throw 'Local main cannot be fast-forwarded to origin/main. Resolve the branch state explicitly before retrying.'
    }

    Write-Host '[OK] Running from current main' -ForegroundColor Green
}

& (Join-Path $PSScriptRoot 'tools/dev/start.ps1') @forwardArgs
