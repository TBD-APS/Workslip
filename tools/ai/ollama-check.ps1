param(
    [string]$BaseUrl = $(if ($env:OLLAMA_BASE_URL) { $env:OLLAMA_BASE_URL } else { 'http://127.0.0.1:11434' }),
    [string]$Model,
    [switch]$SmokeChat
)

$ErrorActionPreference = 'Stop'

function Invoke-OllamaJson {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('GET','POST')][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body
    )

    $uri = ($BaseUrl.TrimEnd('/') + $Path)
    $params = @{
        Uri = $uri
        Method = $Method
        TimeoutSec = 10
        Headers = @{ Accept = 'application/json' }
    }

    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }

    Invoke-RestMethod @params
}

Write-Host "Ollama runtime check"
Write-Host "Base URL: $BaseUrl"

try {
    $version = Invoke-OllamaJson -Method GET -Path '/api/version'
    Write-Host ("Runtime:   reachable (version {0})" -f $version.version)
} catch {
    Write-Error "Ollama is not reachable at $BaseUrl. Start the local Ollama service or set OLLAMA_BASE_URL to the intended loopback endpoint. $($_.Exception.Message)"
    exit 2
}

try {
    $tags = Invoke-OllamaJson -Method GET -Path '/api/tags'
} catch {
    Write-Error "Ollama is reachable, but model discovery failed. $($_.Exception.Message)"
    exit 3
}

$models = @($tags.models)
if ($models.Count -eq 0) {
    Write-Warning 'Ollama is running, but no local models are installed.'
} else {
    Write-Host "Models:"
    foreach ($entry in $models | Sort-Object name) {
        $digest = if ($entry.digest) { $entry.digest.Substring(0, [Math]::Min(12, $entry.digest.Length)) } else { 'unknown' }
        $parameters = if ($entry.details.parameter_size) { $entry.details.parameter_size } else { '?' }
        $quantization = if ($entry.details.quantization_level) { $entry.details.quantization_level } else { '?' }
        Write-Host ("  - {0} [{1}, {2}] digest={3}" -f $entry.name, $parameters, $quantization, $digest)
    }
}

if ($Model) {
    $match = $models | Where-Object { $_.name -eq $Model -or $_.model -eq $Model } | Select-Object -First 1
    if (-not $match) {
        Write-Error "Requested model '$Model' is not installed locally."
        exit 4
    }

    Write-Host "Selected:  $($match.name)"
}

if ($SmokeChat) {
    if (-not $Model) {
        Write-Error '-SmokeChat requires -Model <exact-local-model-name>.'
        exit 5
    }

    try {
        $response = Invoke-OllamaJson -Method POST -Path '/api/chat' -Body @{
            model = $Model
            stream = $false
            messages = @(
                @{ role = 'system'; content = 'Return only the word OK.' },
                @{ role = 'user'; content = 'Health check.' }
            )
            options = @{ temperature = 0 }
        }

        if (-not $response.done) {
            Write-Error 'Ollama returned an incomplete non-streaming chat response.'
            exit 6
        }

        Write-Host ("Smoke:     success; prompt_eval_count={0}; eval_count={1}; total_duration_ns={2}" -f $response.prompt_eval_count, $response.eval_count, $response.total_duration)
    } catch {
        Write-Error "Synthetic Ollama chat failed. $($_.Exception.Message)"
        exit 7
    }
}

Write-Host 'Status:    READY'
