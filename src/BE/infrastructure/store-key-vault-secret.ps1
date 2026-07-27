param(
    [Parameter(Mandatory=$true)]
    [string]$VaultName,

    [Parameter(Mandatory=$true)]
    [string]$SecretName,

    [Parameter(Mandatory=$true)]
    [AllowEmptyString()]
    [string]$SecretValue,

    [string]$Expires = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SecretValue)) {
    throw "Secret value for '$SecretName' was empty."
}

$tempFile = New-TemporaryFile

try {
    [System.IO.File]::WriteAllText($tempFile.FullName, $SecretValue, [System.Text.UTF8Encoding]::new($false))

    $arguments = @(
        "keyvault", "secret", "set",
        "--vault-name", $VaultName,
        "--name", $SecretName,
        "--file", $tempFile.FullName,
        "--encoding", "utf-8",
        "--query", "id",
        "-o", "tsv"
    )

    if (-not [string]::IsNullOrWhiteSpace($Expires)) {
        $arguments += @("--expires", $Expires)
    }

    $secretIdentifier = & az @arguments

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($secretIdentifier)) {
        throw "Could not store secret '$SecretName' in Key Vault '$VaultName'."
    }

    return $secretIdentifier
}
finally {
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    $SecretValue = $null
}
