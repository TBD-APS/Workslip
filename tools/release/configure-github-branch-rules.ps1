[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Owner = 'rasm105k',
    [string]$Repository = 'Workslip-v2.0',
    [string]$ReleasePattern = 'release-*',
    [string]$FeaturePattern = 'rbj--*',
    [string[]]$RequiredStatusChecks = @('CI Gate', 'Feature change guard'),
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-GitHubCli {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) is required.'
    }

    & gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub CLI is not authenticated with repository Administration write permission.'
    }
}

function New-IntegrationRulesetPayload {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$IncludedRefs
    )

    $statusChecks = @(
        $RequiredStatusChecks |
            ForEach-Object { [ordered]@{ context = $_ } }
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
                    required_status_checks = $statusChecks
                    strict_required_status_checks_policy = $true
                }
            }
        )
    }
}

function New-FeatureRulesetPayload {
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
            [ordered]@{ type = 'non_fast_forward' }
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
        [Parameter(Mandatory)] [object]$Payload
    )

    $endpoint = "repos/$Owner/$Repository/rulesets"
    $rulesets = @(Invoke-GhJsonApi -Method GET -Endpoint $endpoint)
    $existing = $rulesets |
        Where-Object { $_.name -eq $Name -and $_.source_type -eq 'Repository' } |
        Select-Object -First 1

    if ($null -eq $existing) {
        if ($PSCmdlet.ShouldProcess("$Owner/$Repository", "Create active ruleset '$Name'")) {
            $created = Invoke-GhJsonApi -Method POST -Endpoint $endpoint -Body $Payload
            Write-Host "Created ruleset '$Name' (id $($created.id))."
        }
        return
    }

    if ($PSCmdlet.ShouldProcess("$Owner/$Repository", "Update active ruleset '$Name' (id $($existing.id))")) {
        $updated = Invoke-GhJsonApi -Method PUT -Endpoint "$endpoint/$($existing.id)" -Body $Payload
        Write-Host "Updated ruleset '$Name' (id $($updated.id))."
    }
}

function Assert-RepositoryRuleset {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [object]$Expected
    )

    $rulesets = @(Invoke-GhJsonApi -Method GET -Endpoint "repos/$Owner/$Repository/rulesets")
    $summary = $rulesets |
        Where-Object { $_.name -eq $Name -and $_.source_type -eq 'Repository' } |
        Select-Object -First 1

    if ($null -eq $summary) {
        throw "Required GitHub ruleset '$Name' is not present."
    }

    $actual = Invoke-GhJsonApi -Method GET -Endpoint "repos/$Owner/$Repository/rulesets/$($summary.id)"
    if ($actual.enforcement -ne 'active') {
        throw "GitHub ruleset '$Name' is not active."
    }

    $expectedBypassActorCount = @($Expected.bypass_actors).Count
    $actualBypassActorCount = @($actual.bypass_actors).Count
    if ($actualBypassActorCount -ne $expectedBypassActorCount) {
        throw "GitHub ruleset '$Name' has $actualBypassActorCount bypass actor(s); expected $expectedBypassActorCount."
    }

    $expectedRefs = @($Expected.conditions.ref_name.include | ForEach-Object { [string]$_ } | Sort-Object)
    $actualRefs = @($actual.conditions.ref_name.include | ForEach-Object { [string]$_ } | Sort-Object)
    if (Compare-Object -ReferenceObject $expectedRefs -DifferenceObject $actualRefs) {
        throw "GitHub ruleset '$Name' does not target the expected refs."
    }

    $expectedRuleTypes = @($Expected.rules | ForEach-Object { [string]$_.type } | Sort-Object)
    $actualRuleTypes = @($actual.rules | ForEach-Object { [string]$_.type } | Sort-Object)
    if (Compare-Object -ReferenceObject $expectedRuleTypes -DifferenceObject $actualRuleTypes) {
        throw "GitHub ruleset '$Name' does not contain the expected rule types."
    }

    $expectedPullRequestRule = $Expected.rules | Where-Object { $_.type -eq 'pull_request' } | Select-Object -First 1
    if ($null -ne $expectedPullRequestRule) {
        $actualPullRequestRule = $actual.rules | Where-Object { $_.type -eq 'pull_request' } | Select-Object -First 1
        if ($null -eq $actualPullRequestRule) {
            throw "GitHub ruleset '$Name' is missing pull-request protection."
        }

        $expectedMergeMethods = @($expectedPullRequestRule.parameters.allowed_merge_methods | ForEach-Object { [string]$_ } | Sort-Object)
        $actualMergeMethods = @($actualPullRequestRule.parameters.allowed_merge_methods | ForEach-Object { [string]$_ } | Sort-Object)
        if (Compare-Object -ReferenceObject $expectedMergeMethods -DifferenceObject $actualMergeMethods) {
            throw "GitHub ruleset '$Name' does not enforce the expected merge methods."
        }

        if ([int]$actualPullRequestRule.parameters.required_approving_review_count -ne [int]$expectedPullRequestRule.parameters.required_approving_review_count) {
            throw "GitHub ruleset '$Name' does not have the expected approving-review count."
        }
    }

    $expectedStatusRule = $Expected.rules | Where-Object { $_.type -eq 'required_status_checks' } | Select-Object -First 1
    if ($null -ne $expectedStatusRule) {
        $actualStatusRule = $actual.rules | Where-Object { $_.type -eq 'required_status_checks' } | Select-Object -First 1
        if ($null -eq $actualStatusRule) {
            throw "GitHub ruleset '$Name' is missing required status checks."
        }

        $expectedChecks = @($expectedStatusRule.parameters.required_status_checks | ForEach-Object { [string]$_.context } | Sort-Object)
        $actualChecks = @($actualStatusRule.parameters.required_status_checks | ForEach-Object { [string]$_.context } | Sort-Object)
        if (Compare-Object -ReferenceObject $expectedChecks -DifferenceObject $actualChecks) {
            throw "GitHub ruleset '$Name' does not require the expected status checks."
        }

        if ([bool]$actualStatusRule.parameters.strict_required_status_checks_policy -ne [bool]$expectedStatusRule.parameters.strict_required_status_checks_policy) {
            throw "GitHub ruleset '$Name' does not enforce the expected strict status-check policy."
        }
    }

    Write-Host "Verified GitHub ruleset '$Name' (id $($summary.id)); bypass actors=$actualBypassActorCount."
}

Assert-GitHubCli

$mainPayload = New-IntegrationRulesetPayload `
    -Name 'Workslip main protection' `
    -IncludedRefs @('refs/heads/main')
$releasePayload = New-IntegrationRulesetPayload `
    -Name 'Workslip release protection' `
    -IncludedRefs @("refs/heads/$ReleasePattern")
$featurePayload = New-FeatureRulesetPayload `
    -Name 'Workslip active feature protection' `
    -IncludedRefs @("refs/heads/$FeaturePattern")

if (-not $VerifyOnly) {
    Set-RepositoryRuleset -Name 'Workslip main protection' -Payload $mainPayload
    Set-RepositoryRuleset -Name 'Workslip release protection' -Payload $releasePayload
    Set-RepositoryRuleset -Name 'Workslip active feature protection' -Payload $featurePayload

    if ($WhatIfPreference) {
        Write-Host 'WhatIf complete. External GitHub ruleset state was not changed or claimed as verified.'
        return
    }
}

Assert-RepositoryRuleset -Name 'Workslip main protection' -Expected $mainPayload
Assert-RepositoryRuleset -Name 'Workslip release protection' -Expected $releasePayload
Assert-RepositoryRuleset -Name 'Workslip active feature protection' -Expected $featurePayload

$statusSummary = $RequiredStatusChecks -join ', '
Write-Host 'Branch rules reconciled and externally verified.'
Write-Host "main: PR + required checks [$statusSummary], squash only, no bypass actors, deletion and non-fast-forward blocked."
Write-Host "${ReleasePattern}: same integration protection."
Write-Host "${FeaturePattern}: no bypass actors, deletion and non-fast-forward updates blocked; ordinary fast-forward feature pushes remain allowed."
