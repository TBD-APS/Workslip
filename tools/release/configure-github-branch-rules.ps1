[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Owner = 'rasm105k',
    [string]$Repository = 'Workslip-v2.0',
    [string]$ReleasePattern = 'release-*',
    [string]$RequiredStatusCheck = 'CI Gate'
)

$ErrorActionPreference = 'Stop'

function Assert-GitHubCli {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) is required. Install it and run gh auth login first.'
    }

    & gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub CLI is not authenticated. Run gh auth login with an account that has Administration: write for the repository.'
    }
}

function New-BranchRulesetPayload {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$IncludedRefs
    )

    return [ordered]@{
        name = $Name
        target = 'branch'
        enforcement = 'active'
        bypass_actors = @()
        conditions = [ordered]@{
            ref_name = [ordered]@{
                include = $IncludedRefs
                exclude = @()
            }
        }
        rules = @(
            [ordered]@{ type = 'deletion' },
            [ordered]@{ type = 'non_fast_forward' },
            [ordered]@{
                type = 'pull_request'
                parameters = [ordered]@{
                    allowed_merge_methods = @('squash')
                    dismiss_stale_reviews_on_push = $false
                    require_code_owner_review = $false
                    require_last_push_approval = $false
                    required_approving_review_count = 0
                    required_review_thread_resolution = $false
                }
            },
            [ordered]@{
                type = 'required_status_checks'
                parameters = [ordered]@{
                    do_not_enforce_on_create = $true
                    required_status_checks = @(
                        [ordered]@{ context = $RequiredStatusCheck }
                    )
                    strict_required_status_checks_policy = $true
                }
            }
        )
    }
}

function Invoke-GhJsonApi {
    param(
        [Parameter(Mandatory)] [ValidateSet('GET', 'POST', 'PUT')] [string]$Method,
        [Parameter(Mandatory)] [string]$Endpoint,
        [object]$Body
    )

    if ($null -eq $Body) {
        $raw = & gh api --method $Method $Endpoint
    }
    else {
        $json = $Body | ConvertTo-Json -Depth 20 -Compress
        $raw = $json | & gh api --method $Method $Endpoint --input -
    }

    if ($LASTEXITCODE -ne 0) {
        throw "GitHub API call failed: $Method $Endpoint"
    }

    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }

    return $raw | ConvertFrom-Json
}

function Set-RepositoryRuleset {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$IncludedRefs
    )

    $endpoint = "repos/$Owner/$Repository/rulesets"
    $rulesets = @(Invoke-GhJsonApi -Method GET -Endpoint $endpoint)
    $existing = $rulesets | Where-Object { $_.name -eq $Name -and $_.source_type -eq 'Repository' } | Select-Object -First 1
    $payload = New-BranchRulesetPayload -Name $Name -IncludedRefs $IncludedRefs

    if ($null -eq $existing) {
        if ($PSCmdlet.ShouldProcess("$Owner/$Repository", "Create active ruleset '$Name'")) {
            $created = Invoke-GhJsonApi -Method POST -Endpoint $endpoint -Body $payload
            Write-Host "Created ruleset '$Name' (id $($created.id))."
        }
        return
    }

    if ($PSCmdlet.ShouldProcess("$Owner/$Repository", "Update active ruleset '$Name' (id $($existing.id))")) {
        $updated = Invoke-GhJsonApi -Method PUT -Endpoint "$endpoint/$($existing.id)" -Body $payload
        Write-Host "Updated ruleset '$Name' (id $($updated.id))."
    }
}

Assert-GitHubCli

Set-RepositoryRuleset -Name 'Workslip main protection' -IncludedRefs @('refs/heads/main')
Set-RepositoryRuleset -Name 'Workslip release protection' -IncludedRefs @("refs/heads/$ReleasePattern")

Write-Host 'Branch rules configured.'
Write-Host "main: PR required, '$RequiredStatusCheck' required, squash only, deletion/force-push blocked, no bypass actors."
Write-Host "$ReleasePattern: same protection for release integration branches."
