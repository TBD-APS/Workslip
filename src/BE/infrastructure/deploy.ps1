param(
    [Parameter(Position=0)]
    [string]$Environment = "prod",
    [string]$Location = "westeurope",
    [string]$COMPANY_NAME = "npteknik",
    [string]$GlobalAdminId = "9ea4bcd3-bf90-4249-93e0-f45070d140f7",
    [string]$VercelToken = "",
    [switch]$RemoveLegacyGitHubDeploymentAccess
)

# ── SQL admin password ────────────────────────────────────────────────────────
# Source of truth for the SQL admin password is the Key Vault secret
# 'Azure--Sql--AdminPassword'. The first deployment creates the secret with a
# randomly generated strong password; subsequent deployments read it back so
# the password never changes after first set.
#
# Override with $env:WORKSLIP_SQL_ADMIN_PASSWORD if you need a deterministic
# password (e.g. for an existing Azure SQL Server you are re-deploying into
# without access to the Key Vault).
$SQL_ADMIN_PWD_SECRET='Azure-...word'

function New-RandomSqlPassword {
    $rand = New-Object System.Random
    $alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
    $chars = @()
    for ($i = 0; $i -lt 24; $i++) {
        $chars += $alphabet[$rand.Next(0, $alphabet.Length)]
    }
    return -join $chars
}

if ($env:WORKSLIP_SQL_ADMIN_PASSWORD) {
    $SqlAdminPassword = $env:WORKSLIP_SQL_ADMIN_PASSWORD
} else {
    # Try to read existing secret from Key Vault. The vault is created as part
    # of the main deployment below, so the first run will fail this lookup and
    # fall through to generating a new password.
    $keyVaultName = "kv-${COMPANY_NAME}${Environment.ToLowerInvariant()}"
    $existingPwd = az keyvault secret show --name $SQL_ADMIN_PWD_SECRET --vault-name $keyVaultName --query "value" -o tsv 2>$null
    if ($existingPwd) {
        $SqlAdminPassword = $existingPwd
        Write-Host "Reusing existing SQL admin password from Key Vault secret '$SQL_ADMIN_PWD_SECRET'." -ForegroundColor DarkGray
    } else {
        $SqlAdminPassword = New-RandomSqlPassword
        Write-Host "Generated new SQL admin password (24 chars)." -ForegroundColor DarkGray
        # Flag for post-deploy block to persist the password once the main
        # deployment has created the Key Vault.
        $script:STORE_SQL_PWD_AFTER_DEPLOY = $true
    }
}

$ErrorActionPreference = "Stop"

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
   "Microsoft.KeyVault", "Microsoft.AppConfiguration",
   "Microsoft.Sql", "Microsoft.ManagedIdentity",
   "Microsoft.Communication", "Microsoft.Resources") | ForEach-Object {
     $state = az provider show --namespace $_ --query registrationState -o tsv 2>$null
     if ($state -ne "Registered") {
         Write-Host "   Registering $_ …"
         az provider register --namespace $_ --wait
     } else {
         Write-Host "   $_ ✅"
     }
}

Write-Host "Checking Microsoft Graph deployment access…" -ForegroundColor Cyan
az rest --method GET --uri "https://graph.microsoft.com/v1.0/groups?`$top=1" -o none 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Microsoft Graph access check failed. This template creates Microsoft Graph resources during Bicep deployment; sign in with an account that can manage groups and app registrations in this tenant."
}
Write-Host "   Microsoft Graph access ✅"

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
function Invoke-BicepDeployment {
    param(
        [Parameter(Mandatory=$true)] [string]$DeploymentName,
        [Parameter(Mandatory=$true)] [bool]$ProvisionWebApiSqlAccess,
        [Parameter(Mandatory=$true)] [string]$SqlAdminPassword
    )

    $ProvisionWebApiSqlAccessValue = $ProvisionWebApiSqlAccess.ToString().ToLowerInvariant()

    Write-Host "Deploying Bicep template: $DeploymentName" -ForegroundColor Cyan

    $DeploymentJson = az deployment group create `
       --resource-group $RESOURCE_GROUP `
       --name $DeploymentName `
       --mode Incremental `
       --template-file $TEMPLATE `
       --parameters companyName=$COMPANY_NAME `
       --parameters environment=$Environment `
       --parameters globalAdminId=$GlobalAdminId `
       --parameters provisionWebApiSqlAccess=$ProvisionWebApiSqlAccessValue `
       --parameters sqlAdminPassword="$SqlAdminPassword" `
       --parameters vercelToken="$VercelToken" `
       -o json

    if ($LASTEXITCODE -ne 0 -or -not $DeploymentJson) {
        throw "Azure deployment failed: $DeploymentName"
    }

    return $DeploymentJson | ConvertFrom-Json
}

function Wait-GraphDirectoryObject {
    param(
        [Parameter(Mandatory=$true)] [string]$ObjectId,
        [Parameter(Mandatory=$true)] [string]$Description
    )

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        az rest --method GET --uri "https://graph.microsoft.com/v1.0/directoryObjects/$ObjectId" -o none 2>$null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Start-Sleep -Seconds ([Math]::Min($attempt * 5, 30))
    }

    throw "Timed out waiting for Microsoft Graph object '$Description' ($ObjectId)."
}

function Add-GraphGroupMember {
    param(
        [Parameter(Mandatory=$true)] [string]$GroupId,
        [Parameter(Mandatory=$true)] [string]$MemberId,
        [Parameter(Mandatory=$true)] [string]$Description
    )

    Wait-GraphDirectoryObject -ObjectId $GroupId -Description "SQL admin group"
    Wait-GraphDirectoryObject -ObjectId $MemberId -Description $Description

    
    #$Body = @{ '@odata.id' = "https://graph.microsoft.com/v1.0/directoryObjects/$MemberId" } | ConvertTo-Json -Compress
    
    $BodyObject = @{
        '@odata.id' = "https://graph.microsoft.com/v1.0/directoryObjects/$MemberId"
    }
    $TempBodyFile = New-TemporaryFile
    
    $BodyObject |
            ConvertTo-Json -Depth 10 -Compress |
            Set-Content -Path $TempBodyFile -Encoding utf8

    $AddMemberOutput = az rest `
          --method POST `
          --uri "https://graph.microsoft.com/v1.0/groups/$GroupId/members/`$ref" `
          --headers "Content-Type=application/json" `
          --body "@$TempBodyFile" `
          -o none 2>&1

    if ($LASTEXITCODE -eq 0) {
        Remove-Item $TempBodyFile -ErrorAction SilentlyContinue
        Write-Host "Added SQL admin group member: $Description"
        return
    }

    if (($AddMemberOutput | Out-String) -match "already exist") {
        Write-Host "SQL admin group member already exists: $Description"
        return
    }

    throw "Could not add SQL admin group member '$Description' ($MemberId): $AddMemberOutput"
}

function Remove-LegacyGitHubDeploymentAccess {
    param(
        [Parameter(Mandatory=$true)] [string]$RuntimeIdentityName,
        [Parameter(Mandatory=$true)] [string]$RuntimeIdentityPrincipalId,
        [Parameter(Mandatory=$true)] [string]$WebApiResourceId
    )

    $LegacyFederatedCredentialName = "github-$($Environment.ToLowerInvariant())"
    Write-Host "Removing legacy GitHub access from the API runtime identity…" -ForegroundColor Cyan

    $LegacyFederatedCredential = az identity federated-credential list `
        --resource-group $RESOURCE_GROUP `
        --identity-name $RuntimeIdentityName `
        --query "[?name == '$LegacyFederatedCredentialName'].name" `
        -o tsv

    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect federated credentials on runtime identity '$RuntimeIdentityName'."
    }

    if (-not [string]::IsNullOrWhiteSpace($LegacyFederatedCredential)) {
        az identity federated-credential delete `
            --resource-group $RESOURCE_GROUP `
            --identity-name $RuntimeIdentityName `
            --name $LegacyFederatedCredentialName `
            --yes `
            -o none

        if ($LASTEXITCODE -ne 0) {
            throw "Could not remove legacy federated credential '$LegacyFederatedCredentialName' from runtime identity '$RuntimeIdentityName'."
        }
    }

    $LegacyRoleAssignmentIds = az role assignment list `
        --assignee-object-id $RuntimeIdentityPrincipalId `
        --scope $WebApiResourceId `
        --role "Website Contributor" `
        --fill-principal-name false `
        --query "[].id" `
        -o tsv

    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect legacy Website Contributor assignments for runtime identity '$RuntimeIdentityName'."
    }

    foreach ($RoleAssignmentId in @($LegacyRoleAssignmentIds)) {
        if ([string]::IsNullOrWhiteSpace($RoleAssignmentId)) {
            continue
        }

        az role assignment delete --ids $RoleAssignmentId -o none
        if ($LASTEXITCODE -ne 0) {
            throw "Could not remove legacy Website Contributor assignment '$RoleAssignmentId' from runtime identity '$RuntimeIdentityName'."
        }
    }

    Write-Host "   Runtime identity has no GitHub federation or Web App deployment role." -ForegroundColor Green
}

$DeploymentResult = Invoke-BicepDeployment -DeploymentName $DEPLOY_NAME -ProvisionWebApiSqlAccess $false -SqlAdminPassword $SqlAdminPassword
$DeploymentOutputs = $DeploymentResult.properties.outputs

$SqlAdminGroupId = $DeploymentOutputs.SQL_ADMIN_GROUP_ID.value
$DeploymentIdentityPrincipalId = $DeploymentOutputs.MANAGED_IDENTITY_PRINCIPAL_ID.value

if ([string]::IsNullOrWhiteSpace($SqlAdminGroupId)) {
    throw "Deployment output SQL_ADMIN_GROUP_ID was empty."
}

if ([string]::IsNullOrWhiteSpace($DeploymentIdentityPrincipalId)) {
    throw "Deployment output MANAGED_IDENTITY_PRINCIPAL_ID was empty."
}

Write-Host "Ensuring SQL admin group membership…" -ForegroundColor Cyan
Add-GraphGroupMember -GroupId $SqlAdminGroupId -MemberId $GlobalAdminId -Description "global administrator"
Add-GraphGroupMember -GroupId $SqlAdminGroupId -MemberId $DeploymentIdentityPrincipalId -Description "deployment managed identity"

$SqlAccessDeploymentName = "$DEPLOY_NAME-sql"
$DeploymentResult = Invoke-BicepDeployment -DeploymentName $SqlAccessDeploymentName -ProvisionWebApiSqlAccess $true -SqlAdminPassword $SqlAdminPassword
$DeploymentOutputs = $DeploymentResult.properties.outputs

$GitHubDeploymentClientId = $DeploymentOutputs.GITHUB_DEPLOYMENT_CLIENT_ID.value
if ([string]::IsNullOrWhiteSpace($GitHubDeploymentClientId)) {
    throw "Deployment output GITHUB_DEPLOYMENT_CLIENT_ID was empty."
}

Write-Host "GitHub OIDC deployment client ID: $GitHubDeploymentClientId" -ForegroundColor Green

if ($RemoveLegacyGitHubDeploymentAccess) {
    Remove-LegacyGitHubDeploymentAccess `
        -RuntimeIdentityName $DeploymentOutputs.MANAGED_IDENTITY_NAME.value `
        -RuntimeIdentityPrincipalId $DeploymentOutputs.MANAGED_IDENTITY_PRINCIPAL_ID.value `
        -WebApiResourceId $DeploymentOutputs.WEB_API_RESOURCE_ID.value
} else {
    Write-Warning "Legacy GitHub access remains on the API runtime identity. Update AZURE_CLIENT_ID in the GitHub environment, verify a manual OIDC deployment, then rerun with -RemoveLegacyGitHubDeploymentAccess."
}

Write-Host "Deployment complete: $SqlAccessDeploymentName" "Resource group: $RESOURCE_GROUP" -ForegroundColor Green

# If we generated a new password this run (no existing Key Vault secret),
# store it now so subsequent deploys reuse it instead of generating a new one
# that would break the existing SQL Server login.
if ($script:STORE_SQL_PWD_AFTER_DEPLOY) {
    Write-Host "Storing SQL admin password in Key Vault secret '$SQL_ADMIN_PWD_SECRET' for future deploys..." -ForegroundColor Cyan
    az keyvault secret set `
        --vault-name $keyVaultName `
        --name $SQL_ADMIN_PWD_SECRET `
        --value $SqlAdminPassword `
        -o none
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to store SQL admin password in Key Vault. Next deployment will generate a new password and may break the existing SQL Server login. Store it manually with: az keyvault secret set --vault-name $keyVaultName --name $SQL_ADMIN_PWD_SECRET --value '<current-password>'"
    } else {
        Write-Host "   Stored." -ForegroundColor Green
    }
    # Wipe from in-memory so it doesn't linger past the script (defence in depth).
    $SqlAdminPassword = $null
}


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

function Get-ResourceNameFromEndpoint {
    param([string]$Endpoint)

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        return $null
    }

    return ([System.Uri]$Endpoint).Host.Split('.')[0]
}

$AppConfigurationName = Get-ResourceNameFromEndpoint $DeploymentOutputs.AZURE_APP_CONFIG_ENDPOINT.value
$KeyVaultName = Get-ResourceNameFromEndpoint $DeploymentOutputs.KEY_VAULT_URI.value

if ([string]::IsNullOrWhiteSpace($AppConfigurationName)) {
    throw "Could not resolve App Configuration name from deployment output AZURE_APP_CONFIG_ENDPOINT."
}

if ([string]::IsNullOrWhiteSpace($KeyVaultName)) {
    throw "Could not resolve Key Vault name from deployment output KEY_VAULT_URI."
}

$OAuthAppObjectId = $DeploymentOutputs.AZURE_AD_OAUTH_APP_OBJECT_ID.value

if ([string]::IsNullOrWhiteSpace($OAuthAppObjectId)) {
    $OAuthAppObjectId = az appconfig kv show `
      --name $AppConfigurationName `
      --key "Azure:AdOAuth:ClientId" `
      --auth-mode login `
      --query value `
      -o tsv

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($OAuthAppObjectId)) {
        throw "Could not resolve OAuth app registration object id from deployment output or App Configuration: $AppConfigurationName"
    }
}

$OAuthApp = az ad app show `
  --id $OAuthAppObjectId `
  --query "{id:id, appId:appId, displayName:displayName, passwordCredentials:passwordCredentials}" `
  -o json | ConvertFrom-Json

if (-not $OAuthApp -or [string]::IsNullOrWhiteSpace($OAuthApp.id)) {
    throw "OAuth app registration not found for object id: $OAuthAppObjectId"
}

$OAuthClientSecretKey = "Azure:AdOAuth:ClientSecret"
$OAuthClientSecretName = "Azure--AdOAuth--ClientSecret"
$OAuthCredentialDisplayName = "workslip-deploy-$Environment-oauth-client-secret"
$OAuthSecretEndDateUtc = "2299-12-31T23:59:59Z"

$PreviousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$ExistingOAuthSecret = az keyvault secret show `
  --vault-name $KeyVaultName `
  --name $OAuthClientSecretName `
  --query id `
  -o tsv 2>$null
$SecretLookupExitCode = $LASTEXITCODE
$ErrorActionPreference = $PreviousErrorActionPreference

if ($SecretLookupExitCode -ne 0) {
    $ExistingOAuthSecret = $null
}

$ExistingOAuthCredential = $OAuthApp.passwordCredentials | Where-Object {
    $_.displayName -eq $OAuthCredentialDisplayName -and
    ([DateTime]$_.endDateTime).ToUniversalTime() -gt (Get-Date).ToUniversalTime().AddDays(30)
} | Select-Object -First 1

function New-OAuthClientSecretInKeyVault {
    param(
        [Parameter(Mandatory=$true)] [string]$Reason
    )

    Write-Host $Reason

    $OAuthClientSecret = az ad app credential reset `
      --id $OAuthApp.id `
      --append `
      --display-name $OAuthCredentialDisplayName `
      --end-date $OAuthSecretEndDateUtc `
      --query password `
      -o tsv

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($OAuthClientSecret)) {
        throw "Azure CLI did not return OAuth client secret."
    }

    try {
        $OAuthSecretIdentifier = az keyvault secret set `
          --vault-name $KeyVaultName `
          --name $OAuthClientSecretName `
          --value $OAuthClientSecret `
          --expires $OAuthSecretEndDateUtc `
          --query id `
          -o tsv

        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($OAuthSecretIdentifier)) {
            throw "Could not store OAuth client secret in Key Vault: $KeyVaultName"
        }

        return $OAuthSecretIdentifier
    }
    finally {
        $OAuthClientSecret = $null
    }
}

if ($ExistingOAuthCredential -and $ExistingOAuthSecret) {
    $OAuthSecretIdentifier = $ExistingOAuthSecret
    Write-Host "OAuth client secret already exists and Key Vault secret is present ✅"
} elseif ($ExistingOAuthCredential -and -not $ExistingOAuthSecret) {
    Write-Host "OAuth app credential exists, but Key Vault secret '$OAuthClientSecretName' is missing in '$KeyVaultName'. Existing credential values cannot be read back. Rotating OAuth client secret..."
    $OAuthSecretIdentifier = New-OAuthClientSecretInKeyVault -Reason "Creating replacement OAuth client secret and storing it in Key Vault..."
    Write-Host "OAuth client secret rotated and stored in Key Vault ✅"
} else {
    $OAuthSecretIdentifier = New-OAuthClientSecretInKeyVault -Reason "No matching OAuth app credential found. Creating OAuth client secret..."
    Write-Host "OAuth client secret created and stored in Key Vault ✅"
}

az appconfig kv set-keyvault `
  --name $AppConfigurationName `
  --key $OAuthClientSecretKey `
  --secret-identifier $OAuthSecretIdentifier `
  --auth-mode login `
  --yes `
  -o none

if ($LASTEXITCODE -ne 0) {
    throw "Could not store OAuth client secret Key Vault reference in App Configuration: $AppConfigurationName"
}

Write-Host "OAuth client secret reference stored in App Configuration ✅"
