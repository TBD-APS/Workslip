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
   "Microsoft.OperationalInsights", "Microsoft.Insights",
   "Microsoft.KeyVault", "Microsoft.AppConfiguration") | ForEach-Object {
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

# ─── deploy azure ressources ───────────────────────────────────────────
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


# ─── Add Graph roles to Managed Identity ───────────────────────────────────────────

Write-Host "Starting up azure Graph Role permissions on Managed Identity" 

$ManagedIdentityName = "id-$COMPANY_NAME-$ENVIRONMENT"
$ResourceGroupName = "rg-$COMPANY_NAME-$ENVIRONMENT"

$ManagedIdentityPrincipalId = az identity show `
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
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$ManagedIdentityPrincipalId/appRoleAssignments" `
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
    principalId = $ManagedIdentityPrincipalId
    resourceId  = $GraphSpId
    appRoleId   = $RoleId
    }

    $tempBodyFile = New-TemporaryFile
    $bodyObject | ConvertTo-Json -Depth 10 | Set-Content -Path $tempBodyFile -Encoding utf8

    az rest `
    --method POST `
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$ManagedIdentityPrincipalId/appRoleAssignments" `
    --headers "Content-Type=application/json" `
    --body "@$tempBodyFile"

    Remove-Item $tempBodyFile
}

# ─── OAuth app registration client secret ─────────────────────────────────────
Write-Host "Ensuring OAuth app registration client secret..." -ForegroundColor Cyan

$AppConfigurationNamePrefix = "appcs-$COMPANY_NAME-$($Environment.ToLowerInvariant())"
$AppConfigurationName = $AppConfigurationNamePrefix
if ($AppConfigurationName.Length -gt 50) {
    $AppConfigurationName = $AppConfigurationName.Substring(0, 50)
}

$ExactAppConfigurationName = az appconfig show `
  --name $AppConfigurationName `
  --resource-group $ResourceGroupName `
  --query name `
  -o tsv 2>$null

if ($ExactAppConfigurationName) {
    $AppConfigurationName = $ExactAppConfigurationName
} else {
    $MatchingAppConfigurations = @(az appconfig list `
      --resource-group $ResourceGroupName `
      --query "[?starts_with(name, '$AppConfigurationNamePrefix')].name" `
      -o json | ConvertFrom-Json)

    if ($MatchingAppConfigurations.Count -ne 1) {
        throw "Expected exactly one App Configuration store with prefix '$AppConfigurationNamePrefix' in $ResourceGroupName, found $($MatchingAppConfigurations.Count)."
    }

    $AppConfigurationName = $MatchingAppConfigurations[0]
}

$KeyVaultNameRaw = "kv-$COMPANY_NAME-$($Environment.ToLowerInvariant())"
if ($KeyVaultNameRaw.Length -gt 24) {
    $KeyVaultName = $KeyVaultNameRaw.Substring(0, 24)
} else {
    $KeyVaultName = $KeyVaultNameRaw
}

$OAuthClientId = az appconfig kv show `
  --name $AppConfigurationName `
  --key "Azure:AdOAuth:ClientId" `
  --query value `
  -o tsv

if (-not $OAuthClientId) {
    throw "Could not read Azure:AdOAuth:ClientId from App Configuration: $AppConfigurationName"
}

$OAuthApp = az ad app show `
  --id $OAuthClientId `
  --query "{id:id, appId:appId, displayName:displayName, passwordCredentials:passwordCredentials}" `
  -o json | ConvertFrom-Json

if (-not $OAuthApp -or -not $OAuthApp.id) {
    throw "OAuth app registration not found for client id: $OAuthClientId"
}

$OAuthClientSecretKey = "Azure:AdOAuth:ClientSecret"
$OAuthClientSecretName = "Azure--AdOAuth--ClientSecret"
$OAuthCredentialDisplayName = "workslip-deploy-$Environment-oauth-client-secret"
$OAuthSecretEndDateUtc = "2299-12-31T23:59:59Z"

$ExistingOAuthSecret = az keyvault secret show `
  --vault-name $KeyVaultName `
  --name $OAuthClientSecretName `
  --query id `
  -o tsv 2>$null

$ExistingOAuthCredential = $OAuthApp.passwordCredentials | Where-Object {
    $_.displayName -eq $OAuthCredentialDisplayName -and
    ([DateTime]$_.endDateTime).ToUniversalTime() -gt (Get-Date).ToUniversalTime().AddDays(30)
} | Select-Object -First 1

if ($ExistingOAuthCredential -and $ExistingOAuthSecret) {
    $OAuthSecretIdentifier = $ExistingOAuthSecret
    Write-Host "OAuth client secret already exists and Key Vault secret is present ✅"
} else {
    Write-Host "Creating OAuth client secret..."

    $OAuthClientSecret = az ad app credential reset `
      --id $OAuthApp.appId `
      --append `
      --display-name $OAuthCredentialDisplayName `
      --end-date $OAuthSecretEndDateUtc `
      --query password `
      -o tsv

    if (-not $OAuthClientSecret) {
        throw "Azure CLI did not return OAuth client secret."
    }

    $OAuthSecretIdentifier = az keyvault secret set `
      --vault-name $KeyVaultName `
      --name $OAuthClientSecretName `
      --value $OAuthClientSecret `
      --expires $OAuthSecretEndDateUtc `
      --query id `
      -o tsv

    $OAuthClientSecret = $null

    Write-Host "OAuth client secret created and stored in Key Vault ✅"
}

az appconfig kv set-keyvault `
  --name $AppConfigurationName `
  --key $OAuthClientSecretKey `
  --secret-identifier $OAuthSecretIdentifier `
  --yes `
  -o none

Write-Host "OAuth client secret reference stored in App Configuration ✅"
