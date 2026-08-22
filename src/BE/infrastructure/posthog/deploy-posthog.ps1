param(
    [string]$CompanyName = 'mrsoftwarev2',
    [string]$Environment = 'live',
    [string]$Location = 'swedencentral',
    [string]$ResourceGroup = '',
    [string]$VmSize = 'Standard_D4as_v5',
    [int]$DataDiskSizeGb = 128,
    [string]$AdminUsername = 'workslip',
    [string]$SshPublicKeyPath = '',
    [string]$DnsLabel = '',
    [string]$ExpectedTenantId = '',
    [string]$ExpectedSubscriptionId = '',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
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

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required.'
}
if (-not (Get-Command ssh-keygen -ErrorAction SilentlyContinue)) {
    throw 'ssh-keygen is required.'
}

$normalizedEnvironment = $Environment.ToLowerInvariant()
$normalizedCompany = $CompanyName.ToLowerInvariant()

if ([string]::IsNullOrWhiteSpace($ResourceGroup)) {
    $ResourceGroup = "rg-$normalizedCompany-analytics-$normalizedEnvironment"
}

$vmName = "vm-$normalizedCompany-posthog-$normalizedEnvironment"
if ([string]::IsNullOrWhiteSpace($DnsLabel)) {
    $DnsLabel = "posthog-$normalizedCompany-$normalizedEnvironment"
}
$DnsLabel = ($DnsLabel.ToLowerInvariant() -replace '[^a-z0-9-]', '-').Trim('-')
if ($DnsLabel.Length -gt 63) {
    throw 'DnsLabel must be 63 characters or fewer after normalization.'
}

$template = Join-Path $PSScriptRoot 'posthog.bicep'
if (-not (Test-Path $template)) {
    throw "Bicep template not found: $template"
}

$account = (Invoke-Az -Arguments @(
    'account', 'show',
    '--query', '{subscriptionId:id,tenantId:tenantId,name:name}',
    '--output', 'json'
)).Output | ConvertFrom-Json

if (-not [string]::IsNullOrWhiteSpace($ExpectedTenantId) -and $account.tenantId -ne $ExpectedTenantId) {
    throw "Wrong tenant. Expected $ExpectedTenantId, got $($account.tenantId)."
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedSubscriptionId) -and $account.subscriptionId -ne $ExpectedSubscriptionId) {
    throw "Wrong subscription. Expected $ExpectedSubscriptionId, got $($account.subscriptionId)."
}

if ([string]::IsNullOrWhiteSpace($SshPublicKeyPath)) {
    $SshPublicKeyPath = Join-Path $HOME '.ssh/id_ed25519.pub'
}

if (-not (Test-Path $SshPublicKeyPath)) {
    $privateKeyPath = $SshPublicKeyPath -replace '\.pub$', ''
    $keyDirectory = Split-Path -Parent $privateKeyPath
    if (-not (Test-Path $keyDirectory)) {
        New-Item -ItemType Directory -Path $keyDirectory -Force | Out-Null
    }

    Write-Host "Generating SSH key at $privateKeyPath" -ForegroundColor Cyan
    & ssh-keygen -t ed25519 -f $privateKeyPath -N '' -C 'workslip-posthog'
    if ($LASTEXITCODE -ne 0) {
        throw 'ssh-keygen failed.'
    }
}

$sshPublicKey = (Get-Content -Raw $SshPublicKeyPath).Trim()
if ([string]::IsNullOrWhiteSpace($sshPublicKey)) {
    throw "SSH public key is empty: $SshPublicKeyPath"
}

$operatorIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($operatorIp, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Could not resolve a valid public IPv4 address. Received '$operatorIp'."
}
$adminSourceCidr = "$operatorIp/32"

Write-Host "Subscription:  $($account.name) ($($account.subscriptionId))"
Write-Host "ResourceGroup: $ResourceGroup"
Write-Host "Location:      $Location"
Write-Host "VM:            $vmName ($VmSize)"
Write-Host "Docker disk:   ${DataDiskSizeGb} GiB"
Write-Host "SSH source:    $adminSourceCidr"
Write-Host "DNS label:     $DnsLabel"
Write-Host "Mode:          $(if ($WhatIf) { 'WHAT-IF' } else { 'DEPLOY' })"

$groupExists = ((Invoke-Az -Arguments @('group', 'exists', '--name', $ResourceGroup, '--output', 'tsv')).Output).Trim()
if ($groupExists -ne 'true') {
    if ($WhatIf) {
        Write-Host 'Resource group does not exist. Running static Bicep build only; group what-if needs an existing group.' -ForegroundColor Yellow
        Invoke-Az -Arguments @('bicep', 'build', '--file', $template, '--stdout') | Out-Null
        return
    }

    Invoke-Az -Arguments @(
        'group', 'create',
        '--name', $ResourceGroup,
        '--location', $Location,
        '--only-show-errors',
        '--output', 'none'
    ) | Out-Null
}

$deploymentArgs = @(
    '--resource-group', $ResourceGroup,
    '--template-file', $template,
    '--parameters',
    "location=$Location",
    "vmName=$vmName",
    "adminUsername=$AdminUsername",
    "sshPublicKey=$sshPublicKey",
    "adminSourceCidr=$adminSourceCidr",
    "dnsLabel=$DnsLabel",
    "vmSize=$VmSize",
    "dataDiskSizeGb=$DataDiskSizeGb",
    '--only-show-errors',
    '--output', 'json'
)

if ($WhatIf) {
    $whatIf = Invoke-Az -Arguments (@('deployment', 'group', 'what-if') + $deploymentArgs)
    Write-Host $whatIf.Output
    return
}

$deploymentName = "posthog-$normalizedEnvironment-$(Get-Date -Format 'yyyyMMddHHmmss')"
$result = Invoke-Az -Arguments (@(
    'deployment', 'group', 'create',
    '--name', $deploymentName
) + $deploymentArgs)

$deployment = $result.Output | ConvertFrom-Json
$fqdn = [string]$deployment.properties.outputs.fqdn.value
$publicIp = [string]$deployment.properties.outputs.publicIpAddress.value

if ([string]::IsNullOrWhiteSpace($fqdn)) {
    throw 'Azure deployment did not return the PostHog FQDN.'
}

Write-Host ''
Write-Host 'PostHog host infrastructure deployed.' -ForegroundColor Green
Write-Host "FQDN:      $fqdn"
Write-Host "Public IP: $publicIp"
Write-Host "VM:        $vmName"
Write-Host "SSH:       ssh $AdminUsername@$fqdn"
Write-Host "Data:      dedicated ${DataDiskSizeGb} GiB Standard SSD mounted at /var/lib/docker after cloud-init"
Write-Host ''
Write-Host 'Next: SSH to the VM and run the official PostHog self-host installer from https://posthog.com/docs/self-host.' -ForegroundColor Cyan
