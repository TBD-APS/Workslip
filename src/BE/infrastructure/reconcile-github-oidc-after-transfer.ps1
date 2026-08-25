param(
    [string]$Repository = '',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required.'
}
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is required.'
}

if ([string]::IsNullOrWhiteSpace($Repository)) {
    $Repository = (& gh repo view --json nameWithOwner --jq '.nameWithOwner').Trim()
}
if ($Repository -notmatch '^[^/]+/[^/]+$') {
    throw "Repository must be owner/name. Received '$Repository'."
}

$repoJson = & gh api "repos/$Repository"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoJson)) {
    throw "Could not resolve GitHub repository metadata for '$Repository'."
}
$repo = $repoJson | ConvertFrom-Json
$owner = [string]$repo.owner.login
$ownerId = [string]$repo.owner.id
$repositoryName = [string]$repo.name
$repositoryId = [string]$repo.id

foreach ($value in @($owner, $ownerId, $repositoryName, $repositoryId)) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw 'GitHub repository metadata returned an empty owner/repository identity value.'
    }
}

$targets = @(
    [pscustomobject]@{ Company = 'mrsoftware'; Environment = 'prod' },
    [pscustomobject]@{ Company = 'mrsoftwarev2'; Environment = 'live' }
)

function Set-FederatedCredential {
    param(
        [Parameter(Mandatory = $true)][string]$ResourceGroup,
        [Parameter(Mandatory = $true)][string]$IdentityName,
        [Parameter(Mandatory = $true)][string]$CredentialName,
        [Parameter(Mandatory = $true)][string]$Subject
    )

    $identityId = (& az identity show `
        --resource-group $ResourceGroup `
        --name $IdentityName `
        --query id -o tsv).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($identityId)) {
        throw "Azure managed identity '$IdentityName' was not found in '$ResourceGroup'."
    }

    Write-Host "$IdentityName -> $Subject" -ForegroundColor Cyan
    if ($WhatIf) {
        return
    }

    $bodyFile = New-TemporaryFile
    try {
        $body = [ordered]@{
            properties = [ordered]@{
                issuer = 'https://token.actions.githubusercontent.com'
                subject = $Subject
                audiences = @('api://AzureADTokenExchange')
            }
        } | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($bodyFile.FullName, $body, [System.Text.UTF8Encoding]::new($false))

        $uri = "https://management.azure.com$identityId/federatedIdentityCredentials/$CredentialName`?api-version=2024-11-30"
        & az rest `
            --method PUT `
            --url $uri `
            --headers 'Content-Type=application/json' `
            --body "@$($bodyFile.FullName)" `
            --only-show-errors `
            --output none
        if ($LASTEXITCODE -ne 0) {
            throw "Could not reconcile federated credential '$CredentialName' on '$IdentityName'."
        }
    }
    finally {
        Remove-Item $bodyFile.FullName -Force -ErrorAction SilentlyContinue
    }
}

foreach ($target in $targets) {
    $environment = $target.Environment
    $resourceGroup = "rg-$($target.Company)-$environment"
    $subject = "repo:$owner@$ownerId/$repositoryName@$repositoryId`:environment:$environment"

    Set-FederatedCredential `
        -ResourceGroup $resourceGroup `
        -IdentityName "id-$($target.Company)-$environment-github" `
        -CredentialName "github-$environment" `
        -Subject $subject

    Set-FederatedCredential `
        -ResourceGroup $resourceGroup `
        -IdentityName "id-$($target.Company)-$environment-migration" `
        -CredentialName "github-$environment" `
        -Subject $subject
}

Write-Host "Resolved GitHub owner: $owner ($ownerId)" -ForegroundColor Green
Write-Host "Repository identity: $repositoryName ($repositoryId)" -ForegroundColor Green
if ($WhatIf) {
    Write-Host 'WHAT-IF complete. No Azure federated credential was changed.' -ForegroundColor Green
}
else {
    Write-Host 'GitHub OIDC trust reconciled for deployment and migration identities.' -ForegroundColor Green
}
