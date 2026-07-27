param(
    [string]$Environment = "prod",
    [string]$CompanyName = "npteknik",
    [string]$ApiHostname = "api.mrsoftware.dk"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI is required."
}

if (-not (Get-Command Resolve-DnsName -ErrorAction SilentlyContinue)) {
    throw "Resolve-DnsName is required to verify public DNS before binding the hostname."
}

$ResourceGroup = "rg-$CompanyName-$Environment"
$WebAppName = "api-$CompanyName-$Environment"
$PlanName = "plan-$CompanyName-$Environment"
$TxtHostname = "asuid.$ApiHostname"

$SubscriptionId = az account show --query id -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($SubscriptionId)) {
    throw "Azure CLI is not logged in. Run 'az login' first."
}

$WebAppJson = az webapp show `
    --resource-group $ResourceGroup `
    --name $WebAppName `
    -o json
if ($LASTEXITCODE -ne 0 -or -not $WebAppJson) {
    throw "Could not read Azure Web App '$WebAppName' in '$ResourceGroup'."
}
$WebApp = $WebAppJson | ConvertFrom-Json

$PlanJson = az appservice plan show `
    --resource-group $ResourceGroup `
    --name $PlanName `
    -o json
if ($LASTEXITCODE -ne 0 -or -not $PlanJson) {
    throw "Could not read App Service plan '$PlanName'."
}
$Plan = $PlanJson | ConvertFrom-Json

if ($Plan.sku.tier -eq "Free") {
    throw "Azure App Service Free F1 cannot use a custom domain. Run './deploy.ps1 -EnableApiCustomDomain' first and explicitly accept the paid Basic B1 tier."
}

$DefaultHostname = [string]$WebApp.defaultHostName
$VerificationId = [string]$WebApp.customDomainVerificationId
if ([string]::IsNullOrWhiteSpace($DefaultHostname) -or [string]::IsNullOrWhiteSpace($VerificationId)) {
    throw "Azure did not return the default hostname or custom-domain verification ID."
}

Write-Host "Required public DNS records:" -ForegroundColor Cyan
Write-Host "  CNAME api -> $DefaultHostname"
Write-Host "  TXT asuid.api -> $VerificationId"
Write-Host "Keep the CNAME as DNS only in Cloudflare until Azure validation and TLS binding are complete." -ForegroundColor Yellow

$CnameAnswer = Resolve-DnsName -Name $ApiHostname -Type CNAME -ErrorAction SilentlyContinue |
    Select-Object -First 1
$ResolvedCname = if ($CnameAnswer) { ([string]$CnameAnswer.NameHost).TrimEnd('.').ToLowerInvariant() } else { "" }
if ($ResolvedCname -ne $DefaultHostname.TrimEnd('.').ToLowerInvariant()) {
    throw "CNAME validation failed. '$ApiHostname' currently resolves to '$ResolvedCname'; expected '$DefaultHostname'."
}

$TxtValues = Resolve-DnsName -Name $TxtHostname -Type TXT -ErrorAction SilentlyContinue |
    ForEach-Object { $_.Strings -join "" }
if ($TxtValues -notcontains $VerificationId) {
    throw "TXT validation failed. '$TxtHostname' must contain Azure verification ID '$VerificationId'."
}

$HostnameBindingsJson = az webapp config hostname list `
    --resource-group $ResourceGroup `
    --webapp-name $WebAppName `
    -o json
if ($LASTEXITCODE -ne 0) {
    throw "Could not list hostname bindings for '$WebAppName'."
}
$HostnameBindings = $HostnameBindingsJson | ConvertFrom-Json
$HostnameExists = $HostnameBindings | Where-Object {
    $_.name -eq $ApiHostname -or $_.hostName -eq $ApiHostname
}

if (-not $HostnameExists) {
    az webapp config hostname add `
        --resource-group $ResourceGroup `
        --webapp-name $WebAppName `
        --hostname $ApiHostname `
        -o none
    if ($LASTEXITCODE -ne 0) {
        throw "Azure rejected the hostname binding for '$ApiHostname'."
    }
}

function Get-ApiManagedCertificate {
    $CertificatesJson = az webapp config ssl list `
        --resource-group $ResourceGroup `
        --name $WebAppName `
        -o json
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list App Service certificates."
    }

    return ($CertificatesJson | ConvertFrom-Json) |
        Where-Object { $_.hostNames -contains $ApiHostname } |
        Select-Object -First 1
}

$Certificate = Get-ApiManagedCertificate
if (-not $Certificate) {
    az webapp config ssl create `
        --resource-group $ResourceGroup `
        --name $WebAppName `
        --hostname $ApiHostname `
        --certificate-name $ApiHostname `
        -o none
    if ($LASTEXITCODE -ne 0) {
        throw "Could not request the App Service managed certificate for '$ApiHostname'."
    }

    for ($Attempt = 1; $Attempt -le 30; $Attempt++) {
        $Certificate = Get-ApiManagedCertificate
        if ($Certificate -and -not [string]::IsNullOrWhiteSpace([string]$Certificate.thumbprint)) {
            break
        }

        Write-Host "Waiting for managed certificate issuance ($Attempt/30)..." -ForegroundColor DarkGray
        Start-Sleep -Seconds 10
    }
}

$Thumbprint = [string]$Certificate.thumbprint
if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
    throw "The managed certificate for '$ApiHostname' was not issued within the expected time. Rerun this script after Azure completes certificate issuance."
}

az webapp config ssl bind `
    --resource-group $ResourceGroup `
    --name $WebAppName `
    --hostname $ApiHostname `
    --certificate-thumbprint $Thumbprint `
    --ssl-type SNI `
    -o none
if ($LASTEXITCODE -ne 0) {
    throw "Could not bind the managed certificate to '$ApiHostname'."
}

$HealthPassed = $false
for ($Attempt = 1; $Attempt -le 12; $Attempt++) {
    try {
        $Health = Invoke-RestMethod -Uri "https://$ApiHostname/health" -Method Get -TimeoutSec 30
        if ($Health.status -eq "ok") {
            $HealthPassed = $true
            break
        }
    }
    catch {
        Write-Host "Waiting for DNS/TLS propagation ($Attempt/12): $($_.Exception.Message)" -ForegroundColor DarkGray
    }

    Start-Sleep -Seconds 10
}

if (-not $HealthPassed) {
    throw "API health check did not succeed after the hostname and TLS binding were created."
}

Write-Host "API custom domain is active: https://$ApiHostname" -ForegroundColor Green
Write-Host "Health check passed: https://$ApiHostname/health" -ForegroundColor Green
