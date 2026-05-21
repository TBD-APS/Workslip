param(
    [Parameter(Position=0)]
    [string]$Environment = "dev",
    [string]$Location = "westeurope",
    [string]$COMPANY_NAME = "rbj"
)

$RESOURCE_GROUP = "rg-$COMPANY_NAME-$Environment"
$INFRA_DIR = Split-Path -Parent $PSCommandPath
$TEMPLATE = Join-Path $INFRA_DIR "main.bicep"
$PARAMETERS = Join-Path $INFRA_DIR "parameters.$Environment.json"
$DEPLOY_NAME = "$COMPANY_NAME-$Environment-$(Get-Date -Format 'yyyyMMddHHmmss')"

# ─── checks ───────────────────────────────────────────
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI not found. Install from https://aka.ms/installazurecliwindows"
    exit 1
}
if (-not (Test-Path $TEMPLATE)) {
    Write-Error "Template not found at $TEMPLATE"
    exit 1
}


# ─── login ────────────────────────────────────────────
Write-Host "Checking Azure login…" -ForegroundColor Cyan
$account = az account show --query id -o tsv 2>$null
if (-not $account) {
    Write-Host "   Not logged in. Starting device login…"
    az login --use-device-code
    $account = az account show --query id -o tsv
}
Write-Host "Subscription: $account"

# ─── register providers ───────────────────────────────
Write-Host "Registering resource providers…" -ForegroundColor Cyan
@("Microsoft.Web", "Microsoft.Storage",
   "Microsoft.Logic", "Microsoft.OperationalInsights", "Microsoft.Insights",
   "Microsoft.KeyVault", "Microsoft.CognitiveServices") | ForEach-Object {
     $state = az provider show --namespace $_ --query registrationState -o tsv 2>$null
     if ($state -ne "Registered") {
         Write-Host "   Registering $_ …"
         az provider register --namespace $_ --wait
     } else {
         Write-Host "   $_ ✅"
     }
}


# ─── resource group ───────────────────────────────────
Write-Host "Ensuring resource group…" -ForegroundColor Cyan
$exists = az group exists --name $RESOURCE_GROUP -o tsv
if ($exists -eq "false") {
    az group create --name $RESOURCE_GROUP --location $Location
    Write-Host "   Created $RESOURCE_GROUP"
} else {
    Write-Host "   $RESOURCE_GROUP ✅"
}

# ─── deploy ───────────────────────────────────────────
Write-Host "Deploying Bicep template…" -ForegroundColor Cyan

if (Test-Path $PARAMETERS) {
    az deployment group create `
        --resource-group $RESOURCE_GROUP `
        --name $DEPLOY_NAME `
        --mode Incremental `
        --template-file $TEMPLATE `
        --parameters $PARAMETERS `
        --parameters companyName=$COMPANY_NAME
} else {
    az deployment group create `
        --mode Incremental `
        --resource-group $RESOURCE_GROUP `
        --name $DEPLOY_NAME `
        --template-file $TEMPLATE
}

Write-Host "Deployment complete: $DEPLOY_NAME" -ForegroundColor Green
Write-Host "Resource group: $RESOURCE_GROUP"
