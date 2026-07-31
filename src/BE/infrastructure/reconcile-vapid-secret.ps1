param(
    [Parameter(Position = 0)]
    [string]$Environment = 'prod',
    [string]$CompanyName = 'mrsoftware',
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

$NormalizedEnvironment = $Environment.ToLowerInvariant()
$ResourceGroup = "rg-$CompanyName-$NormalizedEnvironment"
$AppConfigurationName = "appcs-$CompanyName-$NormalizedEnvironment"
$KeyVaultName = "kv-$CompanyName-$NormalizedEnvironment"
$WebApiName = "api-$CompanyName-$NormalizedEnvironment"
$VapidPrivateKeySecretName = 'Vapid--PrivateKey'
$VapidPrivateKeyConfigurationKey = 'Vapid:PrivateKey'
$LegacyVapidPublicKeyConfigurationKey = 'Vapid:PublicKey'

function Initialize-AzureCliInvocation {
    $azureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop |
        Select-Object -First 1

    if ($null -eq $azureCliCommand -or [string]::IsNullOrWhiteSpace($azureCliCommand.Source)) {
        throw 'Could not resolve Azure CLI.'
    }

    $script:AzureCliExecutable = $azureCliCommand.Source
    $script:AzureCliPrefix = @()

    $runningOnWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    if (-not $runningOnWindows) {
        return
    }

    $azureCliDirectory = Split-Path -Parent $azureCliCommand.Source
    $pythonCandidate = [System.IO.Path]::GetFullPath(
        (Join-Path $azureCliDirectory '..\python.exe')
    )

    if (-not (Test-Path $pythonCandidate)) {
        throw "Azure CLI Python runtime not found: $pythonCandidate"
    }

    $script:AzureCliExecutable = $pythonCandidate
    $script:AzureCliPrefix = @('-IBm', 'azure.cli')
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $commandArguments = @($script:AzureCliPrefix) + @($Arguments)
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        $output = @(
            & $script:AzureCliExecutable @commandArguments 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ([string]::IsNullOrWhiteSpace($text)) {
            $text = 'Azure CLI returned no diagnostic output.'
        }

        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function ConvertTo-Base64Url {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function ConvertFrom-Base64Url {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw 'The VAPID private key is empty.'
    }

    $normalized = $Value.Trim().Replace('-', '+').Replace('_', '/')
    $normalized = $normalized.PadRight(
        $normalized.Length + ((4 - ($normalized.Length % 4)) % 4),
        '='
    )

    try {
        return [Convert]::FromBase64String($normalized)
    }
    catch [FormatException] {
        throw 'The VAPID private key is not valid base64url.'
    }
}

function Assert-VapidPrivateKey {
    param([Parameter(Mandatory = $true)][string]$Value)

    $privateKeyBytes = ConvertFrom-Base64Url -Value $Value
    if ($privateKeyBytes.Length -ne 32) {
        throw 'The VAPID private key must be a 32-byte P-256 private scalar.'
    }

    $parameters = [System.Security.Cryptography.ECParameters]::new()
    $parameters.Curve = [System.Security.Cryptography.ECCurve]::NamedCurves.nistP256
    $parameters.D = $privateKeyBytes

    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $ecdsa.ImportParameters($parameters)
        $publicParameters = $ecdsa.ExportParameters($false)
        if ($null -eq $publicParameters.Q.X -or $null -eq $publicParameters.Q.Y) {
            throw 'The VAPID private key could not derive a P-256 public key.'
        }
    }
    catch [System.Security.Cryptography.CryptographicException] {
        throw 'The VAPID private key is not a valid P-256 private scalar.'
    }
    finally {
        $ecdsa.Dispose()
        [Array]::Clear($privateKeyBytes, 0, $privateKeyBytes.Length)
    }
}

function New-VapidPrivateKey {
    $ecdsa = [System.Security.Cryptography.ECDsa]::Create(
        [System.Security.Cryptography.ECCurve]::NamedCurves.nistP256
    )
    $parameters = $null

    try {
        $parameters = $ecdsa.ExportParameters($true)
        if ($null -eq $parameters.D -or $parameters.D.Length -ne 32) {
            throw 'Unable to generate a 32-byte P-256 VAPID private key.'
        }

        return ConvertTo-Base64Url -Bytes $parameters.D
    }
    finally {
        if ($null -ne $parameters -and $null -ne $parameters.D) {
            [Array]::Clear($parameters.D, 0, $parameters.D.Length)
        }
        $ecdsa.Dispose()
    }
}

function Get-KeyVaultSecretValue {
    $result = Invoke-AzureCli `
        -Arguments @(
            'keyvault', 'secret', 'show',
            '--vault-name', $KeyVaultName,
            '--name', $VapidPrivateKeySecretName,
            '--query', 'value',
            '--only-show-errors',
            '-o', 'tsv'
        ) `
        -AllowFailure

    if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)) {
        return $result.Output.Trim()
    }

    if ($result.Output -match '(?i)(SecretNotFound|VaultNotFound|ResourceNotFound|not found|does not exist|404)') {
        return $null
    }

    if ($result.ExitCode -ne 0) {
        throw "Could not read VAPID private-key secret metadata.`n$($result.Output)"
    }

    return $null
}

function Set-KeyVaultSecretFromMemory {
    param([Parameter(Mandatory = $true)][string]$SecretValue)

    $tempFile = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText(
            $tempFile.FullName,
            $SecretValue,
            [System.Text.UTF8Encoding]::new($false)
        )

        Invoke-AzureCli `
            -Arguments @(
                'keyvault', 'secret', 'set',
                '--vault-name', $KeyVaultName,
                '--name', $VapidPrivateKeySecretName,
                '--file', $tempFile.FullName,
                '--encoding', 'utf-8',
                '--only-show-errors',
                '-o', 'none'
            ) | Out-Null
    }
    finally {
        Remove-Item $tempFile.FullName -Force -ErrorAction SilentlyContinue
    }
}

function Set-AppConfigurationKeyVaultReference {
    $secretIdentifier = "https://$KeyVaultName.vault.azure.net/secrets/$VapidPrivateKeySecretName"

    Invoke-AzureCli `
        -Arguments @(
            'appconfig', 'kv', 'set-keyvault',
            '--name', $AppConfigurationName,
            '--key', $VapidPrivateKeyConfigurationKey,
            '--secret-identifier', $secretIdentifier,
            '--auth-mode', 'login',
            '--yes',
            '--only-show-errors',
            '-o', 'none'
        ) | Out-Null
}

function Remove-LegacyPublicKeyConfiguration {
    $result = Invoke-AzureCli `
        -Arguments @(
            'appconfig', 'kv', 'delete',
            '--name', $AppConfigurationName,
            '--key', $LegacyVapidPublicKeyConfigurationKey,
            '--auth-mode', 'login',
            '--yes',
            '--only-show-errors',
            '-o', 'none'
        ) `
        -AllowFailure

    if ($result.ExitCode -ne 0 -and
        $result.Output -notmatch '(?i)(KeyNotFound|ResourceNotFound|not found|does not exist|404)') {
        throw "Could not remove legacy VAPID public-key configuration.`n$($result.Output)"
    }
}

function Restart-WebApiAndWaitForHealth {
    Invoke-AzureCli `
        -Arguments @(
            'webapp', 'restart',
            '--resource-group', $ResourceGroup,
            '--name', $WebApiName,
            '--only-show-errors',
            '-o', 'none'
        ) | Out-Null

    $hostResult = Invoke-AzureCli `
        -Arguments @(
            'webapp', 'show',
            '--resource-group', $ResourceGroup,
            '--name', $WebApiName,
            '--query', 'defaultHostName',
            '--only-show-errors',
            '-o', 'tsv'
        )

    $hostName = $hostResult.Output.Trim()
    if ([string]::IsNullOrWhiteSpace($hostName)) {
        throw "Azure did not return a hostname for $WebApiName."
    }

    $healthUrl = "https://$hostName/health"
    for ($attempt = 1; $attempt -le 15; $attempt++) {
        try {
            $response = Invoke-WebRequest `
                -Uri $healthUrl `
                -Method Get `
                -TimeoutSec 10 `
                -UseBasicParsing

            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                Write-Host "API health check passed after VAPID reconciliation: $healthUrl" -ForegroundColor Green
                return
            }
        }
        catch {
            if ($attempt -eq 15) {
                throw "API did not become healthy after VAPID reconciliation: $healthUrl`n$($_.Exception.Message)"
            }
        }

        Start-Sleep -Seconds 10
    }

    throw "API did not become healthy after VAPID reconciliation: $healthUrl"
}

if ($ValidateOnly) {
    $validationKey = $null
    try {
        $validationKey = New-VapidPrivateKey
        Assert-VapidPrivateKey -Value $validationKey
        Write-Host 'VAPID P-256 generation and validation passed.' -ForegroundColor Green
        return
    }
    finally {
        $validationKey = $null
    }
}

Initialize-AzureCliInvocation

$existingPrivateKey = $null
$requestedPrivateKey = $null
$effectivePrivateKey = $null

try {
    $existingPrivateKey = Get-KeyVaultSecretValue
    $requestedPrivateKey = if (-not [string]::IsNullOrWhiteSpace($env:WORKSLIP_VAPID_PRIVATE_KEY)) {
        $env:WORKSLIP_VAPID_PRIVATE_KEY.Trim()
    } else {
        $null
    }

    if ($null -ne $requestedPrivateKey) {
        Assert-VapidPrivateKey -Value $requestedPrivateKey
        $effectivePrivateKey = $requestedPrivateKey
        Set-KeyVaultSecretFromMemory -SecretValue $effectivePrivateKey
        Write-Host 'VAPID private key rotated from WORKSLIP_VAPID_PRIVATE_KEY.' -ForegroundColor Yellow
    }
    elseif ($null -ne $existingPrivateKey) {
        Assert-VapidPrivateKey -Value $existingPrivateKey
        $effectivePrivateKey = $existingPrivateKey
        Write-Host 'Existing VAPID private key preserved.' -ForegroundColor DarkGray
    }
    else {
        $effectivePrivateKey = New-VapidPrivateKey
        Assert-VapidPrivateKey -Value $effectivePrivateKey
        Set-KeyVaultSecretFromMemory -SecretValue $effectivePrivateKey
        Write-Host 'Generated and stored a new VAPID private key.' -ForegroundColor Yellow
    }

    Set-AppConfigurationKeyVaultReference
    Remove-LegacyPublicKeyConfiguration
    Restart-WebApiAndWaitForHealth

    Write-Host "VAPID secret lifecycle reconciled for $Environment." -ForegroundColor Green
}
finally {
    $existingPrivateKey = $null
    $requestedPrivateKey = $null
    $effectivePrivateKey = $null
}
