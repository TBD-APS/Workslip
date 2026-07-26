[CmdletBinding()]
param(
    [string]$Environment = "prod",
    [string]$CompanyName = "npteknik",
    [string]$ResourceGroupName = "",
    [string]$ApiUrl = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-AzJson {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$Arguments
    )

    $Output = & az @Arguments --only-show-errors --output json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')`n$($Output -join [Environment]::NewLine)"
    }

    return ($Output -join [Environment]::NewLine) | ConvertFrom-Json
}

function Invoke-AzText {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$Arguments
    )

    $Output = & az @Arguments --only-show-errors --output tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')`n$($Output -join [Environment]::NewLine)"
    }

    return ($Output -join [Environment]::NewLine).Trim()
}

function Assert-SetEqual {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$Expected,
        [Parameter(Mandatory=$true)]
        [string[]]$Actual,
        [Parameter(Mandatory=$true)]
        [string]$Description
    )

    $ExpectedSet = @($Expected | Sort-Object -Unique)
    $ActualSet = @($Actual | Sort-Object -Unique)
    $Difference = @(Compare-Object -ReferenceObject $ExpectedSet -DifferenceObject $ActualSet)

    if ($Difference.Count -gt 0) {
        $Details = $Difference |
            ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" } |
            Out-String
        throw "$Description does not match.`n$Details"
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI is required."
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd 18 is required for the denied-access probe."
}

if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
    $ResourceGroupName = "rg-$CompanyName-$Environment"
}

$WebApiName = "api-$CompanyName-$Environment"
$SqlServerName = "db-$CompanyName-$Environment-server"
$SqlDatabaseName = "db-$CompanyName-$Environment"

Invoke-AzJson -Arguments @("account", "show") | Out-Null

$SqlServer = Invoke-AzJson -Arguments @(
    "sql", "server", "show",
    "--resource-group", $ResourceGroupName,
    "--name", $SqlServerName
)

if ($SqlServer.publicNetworkAccess -ne "Enabled") {
    throw "Expected Azure SQL publicNetworkAccess=Enabled for the App Service IP-allowlist design, got '$($SqlServer.publicNetworkAccess)'."
}

if ($SqlServer.minimalTlsVersion -ne "1.2") {
    throw "Expected Azure SQL minimum TLS 1.2, got '$($SqlServer.minimalTlsVersion)'."
}

$AdOnlyAuthentication = Invoke-AzJson -Arguments @(
    "sql", "server", "ad-only-auth", "get",
    "--resource-group", $ResourceGroupName,
    "--name", $SqlServerName
)
$AdOnlyProperty = $AdOnlyAuthentication.PSObject.Properties["azureADOnlyAuthentication"]
$AdOnlyEnabled = if ($null -ne $AdOnlyProperty) {
    $AdOnlyAuthentication.azureADOnlyAuthentication
} else {
    $AdOnlyAuthentication.properties.azureADOnlyAuthentication
}

if ($AdOnlyEnabled -ne $true) {
    throw "Microsoft Entra-only authentication is not enabled on '$SqlServerName'."
}

$PossibleOutboundIpCsv = Invoke-AzText -Arguments @(
    "webapp", "show",
    "--resource-group", $ResourceGroupName,
    "--name", $WebApiName,
    "--query", "possibleOutboundIpAddresses"
)
$ExpectedOutboundIps = @(
    $PossibleOutboundIpCsv.Split(",", [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ }
)

if ($ExpectedOutboundIps.Count -eq 0) {
    throw "App Service '$WebApiName' returned no possible outbound IP addresses."
}

$FirewallRules = @(
    Invoke-AzJson -Arguments @(
        "sql", "server", "firewall-rule", "list",
        "--resource-group", $ResourceGroupName,
        "--server", $SqlServerName
    )
)

$UnexpectedRules = @(
    $FirewallRules |
        Where-Object {
            $_.name -notmatch "^AllowWebApiOutbound[0-9]+$" -or
            $_.startIpAddress -ne $_.endIpAddress -or
            $_.startIpAddress -eq "0.0.0.0"
        }
)

if ($UnexpectedRules.Count -gt 0) {
    $UnexpectedSummary = $UnexpectedRules |
        ForEach-Object { "$($_.name): $($_.startIpAddress)-$($_.endIpAddress)" } |
        Out-String
    throw "Unexpected Azure SQL firewall rules found:`n$UnexpectedSummary"
}

$ActualAllowedIps = @($FirewallRules | ForEach-Object { $_.startIpAddress })
Assert-SetEqual `
    -Expected $ExpectedOutboundIps `
    -Actual $ActualAllowedIps `
    -Description "Azure SQL firewall IPs and App Service possible outbound IPs"

if ([string]::IsNullOrWhiteSpace($ApiUrl)) {
    $DefaultHostName = Invoke-AzText -Arguments @(
        "webapp", "show",
        "--resource-group", $ResourceGroupName,
        "--name", $WebApiName,
        "--query", "defaultHostName"
    )
    $ApiUrl = "https://$DefaultHostName"
}

$ReadinessUri = "$($ApiUrl.TrimEnd('/'))/health/ready"
$ReadinessResponse = $null

for ($Attempt = 1; $Attempt -le 18; $Attempt++) {
    try {
        $CandidateResponse = Invoke-WebRequest `
            -Uri $ReadinessUri `
            -Method Get `
            -TimeoutSec 30 `
            -SkipHttpErrorCheck

        if ($CandidateResponse.StatusCode -eq 200) {
            $CandidatePayload = $CandidateResponse.Content | ConvertFrom-Json
            if ($CandidatePayload.status -eq "ready") {
                $ReadinessResponse = $CandidateResponse
                break
            }
        }

        Write-Warning "API readiness attempt $Attempt returned HTTP $($CandidateResponse.StatusCode)."
    }
    catch {
        Write-Warning "API readiness attempt $Attempt failed: $($_.Exception.Message)"
    }

    if ($Attempt -lt 18) {
        Start-Sleep -Seconds 10
    }
}

if ($null -eq $ReadinessResponse) {
    throw "API database readiness did not report status=ready within three minutes: $ReadinessUri"
}

$SqlToken = Invoke-AzText -Arguments @(
    "account", "get-access-token",
    "--resource", "https://database.windows.net/",
    "--query", "accessToken"
)
$TokenFile = [IO.Path]::GetTempFileName()

try {
    [IO.File]::WriteAllBytes($TokenFile, [Text.Encoding]::Unicode.GetBytes($SqlToken))

    $SqlCmdOutput = & sqlcmd `
        -S "tcp:$($SqlServer.fullyQualifiedDomainName),1433" `
        -d $SqlDatabaseName `
        -G `
        -P $TokenFile `
        -Q "SELECT 1;" `
        -b `
        -l 15 2>&1 | Out-String
    $SqlCmdExitCode = $LASTEXITCODE

    if ($SqlCmdExitCode -eq 0) {
        throw "Denied-access probe unexpectedly connected to Azure SQL from the external runner."
    }

    if ($SqlCmdOutput -notmatch "40615|is not allowed to access the server") {
        throw "Denied-access probe failed for an inconclusive reason instead of the Azure SQL firewall:`n$SqlCmdOutput"
    }
}
finally {
    Remove-Item $TokenFile -Force -ErrorAction SilentlyContinue
    $SqlToken = $null
}

Write-Host "Azure SQL security validation passed:" -ForegroundColor Green
Write-Host "  - Microsoft Entra-only authentication enabled"
Write-Host "  - firewall exactly matches App Service possible outbound IPs"
Write-Host "  - API runtime identity reached the database through /health/ready"
Write-Host "  - external deployment runner was rejected by the SQL firewall"
