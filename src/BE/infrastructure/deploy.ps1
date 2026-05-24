param(
    [Parameter(Position=0)]
    [string]$Environment = "dev",
    [string]$Location = "westeurope",
    [string]$COMPANY_NAME = "npteknik1",
    [string]$GlobalAdminId = "141e797e-ee4a-41fd-9778-5430ed0a712e"
)

$RESOURCE_GROUP = "rg-$COMPANY_NAME-$Environment"
$INFRA_DIR = Split-Path -Parent $PSCommandPath
$TEMPLATE = Join-Path $INFRA_DIR "main.bicep"
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

az deployment group create `
   --resource-group $RESOURCE_GROUP `
   --name $DEPLOY_NAME `
   --mode Incremental `
   --template-file $TEMPLATE `
   --parameters companyName=$COMPANY_NAME `
   --parameters environment=$Environment `
   --parameters globalAdminId=$GlobalAdminId `

Write-Host "Deployment complete: $DEPLOY_NAME" "Resource group: $RESOURCE_GROUP" -ForegroundColor Green
Write-Host "Starting up azure Graph Role permissions on Managed Identity" 

$ManagedIdentityName = "id-$COMPANY_NAME-$ENVIRONMENT"
$ResourceGroupName = "rg-$COMPANY_NAME-$ENVIRONMENT"

$MiPrincipalId = az identity show `
  --name $ManagedIdentityName `
  --resource-group $ResourceGroupName `
  --query principalId `
  -o tsv

$GraphSp = az ad sp show `
  --id "00000003-0000-0000-c000-000000000000" `
  | ConvertFrom-Json

$GraphSpId = $GraphSp.id

$ExistingAssignments = az rest `
  --method GET `
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$MiPrincipalId/appRoleAssignments" `
  | ConvertFrom-Json

$Roles = @(
  "User.ReadWrite.All",
  "Application.Read.All",
  "AppRoleAssignment.ReadWrite.All",
  "UserAuthenticationMethod.ReadWrite.All"
)

foreach ($Role in $Roles) {
  $RoleId = ($GraphSp.appRoles | Where-Object { $_.value -eq $Role }).id

  if (-not $RoleId) {
    throw "Could not find Microsoft Graph app role: $Role"
  }

  $AlreadyAssigned = $ExistingAssignments.value | Where-Object {
    $_.resourceId -eq $GraphSpId -and $_.appRoleId -eq $RoleId
  }

  if ($AlreadyAssigned) {
    Write-Host "Already assigned: $Role"
    continue
  }

  Write-Host "Assigning: $Role"

    $bodyObject = @{
    principalId = $MiPrincipalId
    resourceId  = $GraphSpId
    appRoleId   = $RoleId
    }

    $tempBodyFile = New-TemporaryFile
    $bodyObject | ConvertTo-Json -Depth 10 | Set-Content -Path $tempBodyFile -Encoding utf8

    az rest `
    --method POST `
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$MiPrincipalId/appRoleAssignments" `
    --headers "Content-Type=application/json" `
    --body "@$tempBodyFile"

    Remove-Item $tempBodyFile
}