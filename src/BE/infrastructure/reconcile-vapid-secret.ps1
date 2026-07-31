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
$SecretName = 'Vapid--PrivateKey'
$ConfigurationKey = 'Vapid:PrivateKey'
$P256ObjectIdentifier = '1.2.840.10045.3.1.7'

function Initialize-AzureCliInvocation {
    $azureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop |
        Select-Object -First 1

    $script:AzureCliExecutable = $azureCliCommand.Source
    $script:AzureCliPrefix = @()

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return
    }

    $pythonCandidate = [System.IO.Path]::GetFullPath(
        (Join-Path (Split-Path -Parent $azureCliCommand.Source) '..\python.exe')
    )
    if (-not (Test-Path $pythonCandidate)) {
        throw "Azure CLI Python runtime not found: $pythonCandidate"
    }

    $script:AzureCliExecutable = $pythonCandidate
    $script:AzureCliPrefix = @('-IBm', 'azure.cli')
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(
            & $script:AzureCliExecutable @($script:AzureCliPrefix + $Arguments) 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Output = $text }
}

function New-VapidPrivateKey {
    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    if ($null -eq $ecdsa) {
        throw 'The current .NET runtime could not create an ECDSA provider.'
    }

    $parameters = $null
    try {
        $curve = [System.Security.Cryptography.ECCurve]::CreateFromValue($P256ObjectIdentifier)
        $ecdsa.GenerateKey($curve)
        $parameters = $ecdsa.ExportParameters($true)
        if ($null -eq $parameters.D -or $parameters.D.Length -ne 32) {
            throw 'Unable to generate a 32-byte P-256 VAPID private key.'
        }

        return [Convert]::ToBase64String($parameters.D).
            TrimEnd('=').
            Replace('+', '-').
            Replace('/', '_')
    }
    finally {
        if ($null -ne $parameters -and $null -ne $parameters.D) {
            [Array]::Clear($parameters.D, 0, $parameters.D.Length)
        }
        $ecdsa.Dispose()
    }
}

if ($ValidateOnly) {
    $validationKey = New-VapidPrivateKey
    if ($validationKey.Length -ne 43) {
        throw 'Generated VAPID private key had an unexpected encoded length.'
    }
    Write-Host 'VAPID P-256 generation passed.' -ForegroundColor Green
    return
}

Initialize-AzureCliInvocation

$secretState = Invoke-AzureCli `
    -Arguments @(
        'keyvault', 'secret', 'show',
        '--vault-name', $KeyVaultName,
        '--name', $SecretName,
        '--query', 'attributes.enabled',
        '--only-show-errors',
        '-o', 'tsv'
    ) `
    -AllowFailure

$secretExists = $secretState.ExitCode -eq 0 -and $secretState.Output -eq 'true'
if (-not $secretExists -and
    $secretState.ExitCode -ne 0 -and
    $secretState.Output -notmatch '(?i)(SecretNotFound|VaultNotFound|ResourceNotFound|not found|does not exist|404)') {
    throw "Could not inspect VAPID private-key secret.`n$($secretState.Output)"
}

if (-not $secretExists) {
    $privateKey = New-VapidPrivateKey
    $tempFile = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText(
            $tempFile.FullName,
            $privateKey,
            [System.Text.UTF8Encoding]::new($false)
        )

        Invoke-AzureCli -Arguments @(
            'keyvault', 'secret', 'set',
            '--vault-name', $KeyVaultName,
            '--name', $SecretName,
            '--file', $tempFile.FullName,
            '--encoding', 'utf-8',
            '--only-show-errors',
            '-o', 'none'
        ) | Out-Null
    }
    finally {
        Remove-Item $tempFile.FullName -Force -ErrorAction SilentlyContinue
        $privateKey = $null
    }

    Write-Host 'Generated and stored the missing VAPID private key.' -ForegroundColor Yellow
}
else {
    Write-Host 'Existing VAPID private key preserved.' -ForegroundColor DarkGray
}

$secretIdentifier = "https://$KeyVaultName.vault.azure.net/secrets/$SecretName"
Invoke-AzureCli -Arguments @(
    'appconfig', 'kv', 'set-keyvault',
    '--name', $AppConfigurationName,
    '--key', $ConfigurationKey,
    '--secret-identifier', $secretIdentifier,
    '--auth-mode', 'login',
    '--yes',
    '--only-show-errors',
    '-o', 'none'
) | Out-Null

Invoke-AzureCli -Arguments @(
    'webapp', 'restart',
    '--resource-group', $ResourceGroup,
    '--name', $WebApiName,
    '--only-show-errors',
    '-o', 'none'
) | Out-Null

Write-Host "VAPID private-key configuration reconciled for $Environment." -ForegroundColor Green
