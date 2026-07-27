param(
    [Parameter(Position=0)]
    [string]$Environment = 'prod',
    [string]$Location = 'westeurope',
    [string]$COMPANY_NAME = 'mrsoftware',
    [string]$GlobalAdminId = '9ea4bcd3-bf90-4249-93e0-f45070d140f7',
    [string]$VercelToken = ''
)

$ErrorActionPreference = 'Stop'
$DeployScript = Join-Path $PSScriptRoot 'deploy.ps1'
if (-not (Test-Path $DeployScript)) {
    throw "Deployment script not found: $DeployScript"
}

$AzureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop | Select-Object -First 1
$AzureCli = $AzureCliCommand.Source
if ([string]::IsNullOrWhiteSpace($AzureCli)) {
    throw 'Could not resolve a single Azure CLI executable path.'
}

$ExpectedVaultName = "kv-$COMPANY_NAME-$($Environment.ToLowerInvariant())"
$LegacyVaultName = "kv-$COMPANY_NAME$($Environment.ToLowerInvariant())"
$MalformedVaultName = "kv-$COMPANY_NAME"

function Invoke-AzureCliRaw {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[string]]$CliArguments
    )

    # Windows PowerShell converts native stderr to PowerShell error records.
    # Keep those records non-terminating so callers can intentionally suppress
    # expected lookup failures and inspect $LASTEXITCODE themselves.
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $AzureCli @CliArguments
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function global:az {
    $cliArguments = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in $args) {
        $value = [string]$argument
        if ($value -eq $LegacyVaultName -or $value -eq $MalformedVaultName) {
            $value = $ExpectedVaultName
        }
        $cliArguments.Add($value)
    }

    $isSecretSet = $cliArguments.Count -ge 3 -and
        $cliArguments[0] -eq 'keyvault' -and
        $cliArguments[1] -eq 'secret' -and
        $cliArguments[2] -eq 'set'
    $valueIndex = $cliArguments.IndexOf('--value')

    if (-not $isSecretSet -or $valueIndex -lt 0) {
        Invoke-AzureCliRaw -CliArguments $cliArguments
        return
    }

    if ($valueIndex + 1 -ge $cliArguments.Count) {
        throw 'Azure CLI Key Vault secret command contained --value without a value.'
    }

    $secretValue = $cliArguments[$valueIndex + 1]
    if ([string]::IsNullOrWhiteSpace($secretValue)) {
        throw 'Refusing to store an empty Key Vault secret.'
    }

    $tempFile = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText(
            $tempFile.FullName,
            $secretValue,
            [System.Text.UTF8Encoding]::new($false)
        )

        $cliArguments.RemoveAt($valueIndex + 1)
        $cliArguments.RemoveAt($valueIndex)
        $cliArguments.Insert($valueIndex, '--file')
        $cliArguments.Insert($valueIndex + 1, $tempFile.FullName)
        $cliArguments.Insert($valueIndex + 2, '--encoding')
        $cliArguments.Insert($valueIndex + 3, 'utf-8')

        Invoke-AzureCliRaw -CliArguments $cliArguments
    }
    finally {
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        $secretValue = $null
    }
}

try {
    & $DeployScript `
        -Environment $Environment `
        -Location $Location `
        -COMPANY_NAME $COMPANY_NAME `
        -GlobalAdminId $GlobalAdminId `
        -VercelToken $VercelToken
}
finally {
    Remove-Item Function:\global:az -ErrorAction SilentlyContinue
    Remove-Item Function:\Invoke-AzureCliRaw -ErrorAction SilentlyContinue
}
