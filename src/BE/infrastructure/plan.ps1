<#
.SYNOPSIS
    Preview the full Workslip infrastructure deployment. Cannot change anything.

.DESCRIPTION
    Runs all four deployment phases in preview mode and reports what each would do:

      1. Microsoft Entra applications  — the Graph upserts that would be sent
      2. Azure infrastructure          — az deployment group what-if
      3. VAPID secret lifecycle        — whether the key would be created
      4. GitHub infrastructure OIDC    — subscription-level what-if

    This is deploy.ps1 -WhatIf with the mutating path removed rather than switched
    off, so a mistyped argument cannot turn a preview into a deployment. Prefer it
    over deploy.ps1 -WhatIf when previewing production.

    Phase 2 previews against the Entra registrations that exist right now. In a
    tenant where phase 1 has not run yet there are none, so it will report that it
    cannot resolve them. That is expected: run phase 1 for real first, then preview
    again.

.EXAMPLE
    ./plan.ps1 prod

.EXAMPLE
    ./plan.ps1 -Environment prod -COMPANY_NAME mrsoftware
#>
param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$PowerBiReaderPrincipalId = '',
    [string]$PowerBiReaderEmail = '',
    [switch]$EnablePowerBiExport,
    [switch]$EnableCustomEmailDomain,
    [string]$EntraStatePath = ''
)

$ErrorActionPreference = 'Stop'

$DeployScript = Join-Path $PSScriptRoot 'deploy.ps1'
if (-not (Test-Path $DeployScript)) {
    throw "Deployment script not found: $DeployScript"
}

# -WhatIf is passed as a literal here and is not exposed as a parameter of this
# script, so there is no argument a caller can supply that makes this run mutate.
& $DeployScript `
    -Environment $Environment `
    -Location $Location `
    -COMPANY_NAME $COMPANY_NAME `
    -GlobalAdminId $GlobalAdminId `
    -PowerBiReaderPrincipalId $PowerBiReaderPrincipalId `
    -PowerBiReaderEmail $PowerBiReaderEmail `
    -EnablePowerBiExport:$EnablePowerBiExport `
    -EnableCustomEmailDomain:$EnableCustomEmailDomain `
    -EntraStatePath $EntraStatePath `
    -WhatIf
