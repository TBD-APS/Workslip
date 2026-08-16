param(
    [string]$BaseUrl = $(if ($env:MOONSHOT_BASE_URL) { $env:MOONSHOT_BASE_URL } else { 'https://api.moonshot.ai/v1' }),
    [string]$Model,
    [switch]$SmokeChat,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message, [int]$ExitCode) {
    if ($Json) {
        $err = @{ provider = 'moonshot'; runtime = 'kimi-api'; status = 'error'; error = $Message } | ConvertTo-Json -Compress
        [Console]::Error.WriteLine($err)
    } else {
        Write-Error $Message
    }
    exit $ExitCode
}

if (-not $env:MOONSHOT_API_KEY) {
    Fail 'MOONSHOT_API_KEY is not set. Create/manage the key in Kimi Open Platform and expose it only in the current operator/server environment.' 10
}

$base = $BaseUrl.TrimEnd('/')
$headers = @{
    Authorization = "Bearer $($env:MOONSHOT_API_KEY)"
    'Content-Type' = 'application/json'
}

if (-not $Json) {
    Write-Host "Moonshot base: $base"
}

try {
    $modelsResponse = Invoke-RestMethod -Method Get -Uri "$base/models" -Headers $headers -TimeoutSec 30
} catch {
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    if ($status -eq 401) { Fail 'Moonshot authentication failed. Verify MOONSHOT_API_KEY.' 11 }
    if ($status -eq 429) { Fail 'Moonshot rate limit reached while listing models.' 12 }
    Fail "Moonshot model discovery failed: $($_.Exception.Message)" 13
}

$models = @($modelsResponse.data)
if ($models.Count -eq 0) {
    Fail 'Moonshot returned no models. Treat provider capability as UNKNOWN until discovery succeeds.' 14
}

$selectedModel = if ($Model) { $Model } else { $null }

if (-not $Json) {
    Write-Host "Available models ($($models.Count)):"
    $models | ForEach-Object {
        $context = if ($_.context_length) { " context=$($_.context_length)" } else { '' }
        $reasoning = if ($null -ne $_.supports_reasoning) { " reasoning=$($_.supports_reasoning)" } else { '' }
        Write-Host " - $($_.id)$context$reasoning"
    }
}

$smokeRequested = $SmokeChat.IsPresent
$smokeResult = $null

if ($smokeRequested) {
    if (-not $selectedModel) {
        Fail 'Pass -Model <current-model-id> when using -SmokeChat. Model ids are discovered at runtime; do not hardcode a stale default in this script.' 20
    }

    if (-not ($models.id -contains $selectedModel)) {
        Fail "Selected model '$selectedModel' was not returned by /models." 21
    }

    $body = @{
        model = $selectedModel
        messages = @(
            @{ role = 'system'; content = 'You are executing a synthetic provider health check. Return only SAFE_OK.' },
            @{ role = 'user'; content = 'Reply SAFE_OK.' }
        )
        stream = $false
    } | ConvertTo-Json -Depth 8

    try {
        $response = Invoke-RestMethod -Method Post -Uri "$base/chat/completions" -Headers $headers -Body $body -TimeoutSec 60
    } catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status -eq 401) { Fail 'Moonshot authentication failed during chat smoke.' 22 }
        if ($status -eq 429) { Fail 'Moonshot rate limit reached during chat smoke.' 23 }
        Fail "Moonshot chat smoke failed: $($_.Exception.Message)" 24
    }

    $smokeResult = $response.choices[0].message.content
    if (-not $smokeResult) {
        Fail 'Moonshot returned no assistant content for the synthetic smoke request.' 25
    }

    if (-not $Json) {
        Write-Host "Smoke model: $selectedModel"
        Write-Host "Smoke result: $smokeResult"
        Write-Host 'Moonshot/Kimi provider check completed.'
    }
} else {
    if (-not $Json) {
        Write-Host 'Moonshot/Kimi provider check completed.'
    }
}

if ($Json) {
    $output = @{
        provider = 'moonshot'
        runtime = 'kimi-api'
        status = 'ok'
        baseUrl = $base
        modelCount = $models.Count
        models = @($models.id)
        selectedModel = $selectedModel
        smokeRequested = $smokeRequested
        smokeResult = $smokeResult
    } | ConvertTo-Json -Compress
    Write-Output $output
}

exit 0
