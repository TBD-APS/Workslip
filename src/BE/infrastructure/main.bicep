extension microsoftGraphV1

param companyName string = ''
param environment string = ''
param globalAdminId string = ''
param location string = resourceGroup().location
param storageAccountName string       = take('st${companyName}${toLower(environment)}', 24)
param logicAppName string             = 'la-${companyName}-${toLower(environment)}'
param appInsightsName string          = 'ai-${companyName}-${toLower(environment)}'
param logAnalyticsName string          = 'logAnal-${companyName}-${toLower(environment)}'
param webApiServerName string          = take('plan-${companyName}-${toLower(environment)}', 40)
param webApiName string                = take('api-${companyName}-${toLower(environment)}', 60)
param appConfigurationName string     = take('appcs-${companyName}-${toLower(environment)}', 50)
@allowed([
  'Default'
  'Recover'
])
param appConfigurationCreateMode string = 'Default'
param identityName string             = 'id-${companyName}-${toLower(environment)}'
param keyVaultName string             = take('kv-${companyName}-${toLower(environment)}', 24)
param documentIntelligenceName string = 'di-${companyName}-${toLower(environment)}'
param communicationServiceName string = take('acs-${companyName}-${toLower(environment)}', 64)
param emailServiceName string         = take('email-${companyName}-${toLower(environment)}', 64)
param githubRepository string         = 'rasm105k/Workslip-v2.0'
param githubEnvironment string        = environment
param sqlAdminGroupName string        = 'sql${companyName}${toLower(environment)}group'
param provisionWebApiSqlAccess bool   = false

// ── SQL admin password ────────────────────────────────────────────────────────
// SECURITY: was previously hardcoded as 'Num64bqe!' in this file. Moved to
// a @secure() parameter so it does not get baked into compiled main.json or
// show up in deployment history. The legacy password is still in git history
// from prior commits — rotate it manually in the Azure portal before reusing
// this template on a real environment.
@secure()
param sqlAdminPassword string
@secure()
param vercelToken string

// ── Role definition IDs ───────────────────────────────────────────────────────
// Centralised here so they're easy to audit and update.
var roles = {
  cognitiveServicesUser:   'a97b65f3-24c7-4388-baec-2e87135dc908'
  storageBlobContributor:  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  appConfigurationDataReader: '516239f1-63e1-4d78-a4de-a74fb236a071'
  keyVaultAdministrator: '00482a5a-887f-4fb3-b363-3b7fe8e74483'
  keyVaultSecretsUserRole: '4633458b-17de-408a-b874-0445c86b69e6'
  appConfigurationDataOwnerRole: '5ae67dd6-50cb-40e7-96ff-dc2bfa4b606b'
  websiteContributor: 'de139f84-1756-47ae-9be6-808fbbe84772'
  sqlSecurityManager: '056cd41c-7e88-42e1-933e-88ba6a50c9c3'
  
  UserReadWriteAll: '741f803b-c850-494e-b5df-cde7c675a1ca'
  UserInviteAll: '09850681-111b-4a89-9bed-3f2cae46d706'
  ApplicationReadAll: '9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30'
  AppRoleAssignmentReadWriteAll: '06b708a9-e830-4db3-a914-8e69da51d44f'
  UserAuthenticationMethodReadWriteAll: '50483e42-d915-4231-9639-7fdb7fd190e5'
}

var tags = {
  environment: environment
  project: companyName
}

var appInsightsConnectionString = appInsights.properties.ConnectionString
var appInsightsInstrumentationKey = appInsights.properties.InstrumentationKey
var sqlAdminGroupMailNickname = take(replace(sqlAdminGroupName, '-', ''), 64)

// ──────────────────────────────────────────────────────────────────────────────
// User-Assigned Managed Identity
// One identity, shared by all resources. All RBAC is granted to this identity.
// ──────────────────────────────────────────────────────────────────────────────

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
}

resource githubFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: identity
  name: 'github-${toLower(environment)}'
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:environment:${githubEnvironment}'
  }
}

resource microsoftGraphServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: '00000003-0000-0000-c000-000000000000'
}

resource graphUserReadWriteAllForApiIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: roles.UserReadWriteAll
  principalId: identity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource graphUserInviteAllForApiIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: roles.UserInviteAll
  principalId: identity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource graphApplicationReadAllForApiIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: roles.ApplicationReadAll
  principalId: identity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource graphAppRoleAssignmentReadWriteAllForApiIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: roles.AppRoleAssignmentReadWriteAll
  principalId: identity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

// ──────────────────────────────────────────────────────────────────────────────
// Monitoring
// ──────────────────────────────────────────────────────────────────────────────

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    workspaceCapping: {
      dailyQuotaGb: 1 // <-- Mindst mulige loft i Azure. Langt under de 5 GB gratis om måneden.
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Web API hosting
// Free App Service tier with shared user-assigned managed identity.
// The API reads App Configuration + Key Vault references through that identity.
// ──────────────────────────────────────────────────────────────────────────────

resource webApiServer 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: webApiServerName
  location: location
  tags: tags
  sku: {
    name: 'F1'
    tier: 'Free'
    capacity: 1
  }
  properties: {}
}

resource webApi 'Microsoft.Web/sites@2023-12-01' = {
  name: webApiName
  location: location
  kind: 'app'
  tags: union(tags, {
    'hidden-link:${appInsights.id}': 'Resource'
  })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: webApiServer.id
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
    keyVaultReferenceIdentity: identity.id
    siteConfig: {
      alwaysOn: false
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
      netFrameworkVersion: 'v10.0'
      use32BitWorkerProcess: true
      metadata: [
        {
          name: 'CURRENT_STACK'
          value: 'dotnet'
        }
      ]
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: identity.properties.clientId
        }
        {
          name: 'Azure__ManagedIdentity__ClientId'
          value: identity.properties.clientId
        }
        {
          name: 'Azure__AppConfiguration__Endpoint'
          value: appConfiguration.properties.endpoint
        }
        {
          name: 'Azure__ApplicationInsights__ConnectionString'
          value: appInsightsConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsightsInstrumentationKey
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'XDT_MicrosoftApplicationInsights_Mode'
          value: 'recommended'
        }
      ]
    }
  }
}

resource webApiDeploymentRoleForGithubIdentity 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(webApi.id, identity.id, roles.websiteContributor)
  scope: webApi
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.websiteContributor)
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Azure App Configuration
// Workloads read non-secret configuration here with managed identity. Secret values
// should be Key Vault references, resolved through the same identity.
// ──────────────────────────────────────────────────────────────────────────────

resource appConfiguration 'Microsoft.AppConfiguration/configurationStores@2023-03-01' = {
  name: appConfigurationName
  location: location
  sku: {
    name: 'free'
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  tags: tags
  properties: {
    createMode: appConfigurationCreateMode
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

module staticConfig './staticConfig.bicep' = {
  name: 'static-config-values'
  params: {
    appConfigurationName: appConfiguration.name
    vercelToken: vercelToken
  }
}

module dynamicAppConfigValues './dynamicConfig.bicep' = {
  name: 'app-config-values'
  params: {
    appConfigurationName: appConfiguration.name

    jwtSigninKey: keyVaultConfigs.outputs.jwtSigninKey
    managedIdentityClientId: identity.properties.clientId
    appConfigurationEndpoint: 'https://${appConfiguration.name}.azconfig.io'

    azureAdOAuthClientId: EntraAppRegistrations.outputs.OAuthClientId
    clientAppId: EntraAppRegistrations.outputs.ClientAppId
    oauthServerAppId: EntraAppRegistrations.outputs.OAuthAppId

    acsConnectionString: keyVaultConfigs.outputs.acsConnectionStringSecretUri
    acsSenderAddress:  '${senderUsername.properties.username}@${emailDomain.properties.fromSenderDomain}'

    storageAccountName: storageAccount.name
    applicationInsightsConnectionString: appInsights.properties.ConnectionString

    sqlConnectionString: keyVaultConfigs.outputs.sqlConnectionstring
  }
}

//Added so other apps can read directly from app config (azure functions, web api osv..)
resource appConfigurationRoleIdentity 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid('${appConfiguration.id}${identity.id}${roles.appConfigurationDataReader}')
  scope: appConfiguration
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.appConfigurationDataReader)
  }
}

//App configuration can read key vault refs from the keyvault directly
resource keyVaultSecretsUserForApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, roles.keyVaultSecretsUserRole)
  scope: keyVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roles.keyVaultSecretsUserRole
    )
  }
}

//I as admin have full control over app config
resource appConfigurationDataOwnerForAdmin 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appConfiguration.id, globalAdminId, roles.appConfigurationDataOwnerRole)
  scope: appConfiguration
  properties: {
    principalId: globalAdminId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roles.appConfigurationDataOwnerRole
    )
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Key Vault
// RBAC-mode only (no access policies). Identity gets Secrets User.
// ──────────────────────────────────────────────────────────────────────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: { name: 'standard', family: 'A' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: true
  }
}

module keyVaultConfigs './keyvaultConfig.bicep' = {
  name: 'key-vault-secrets'
  params: {
    keyVaultName: keyVault.name
    communicationServiceName: communicationService.name
    sqlConnectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=db-${companyName}-${environment};User ID=rbj;Password=${sqlAdminPassword}; TrustServerCertificate=False;'
  }
}

//I as admin have full control over keyvault
resource keyVaultSecretsOfficerForAdmin 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, globalAdminId, roles.keyVaultAdministrator)
  scope: keyVault
  properties: {
    principalId: globalAdminId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roles.keyVaultAdministrator
    )
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Entra App Registrations
// OAuth setup and passkey validation.
// ──────────────────────────────────────────────────────────────────────────────

module EntraAppRegistrations './entraRegistrations.bicep' = {
  name: 'entraApps'
  params: {
    environment: environment
    globalAdminId: globalAdminId
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// SQL Server + database
// Data and stuff
// ──────────────────────────────────────────────────────────────────────────────

resource sqlAdminGroup 'Microsoft.Graph/groups@v1.0' = {
  uniqueName: sqlAdminGroupName
  displayName: sqlAdminGroupName
  description: 'Azure SQL administrators for ${environment}, and deployment automation.'
  mailEnabled: false
  mailNickname: sqlAdminGroupMailNickname
  securityEnabled: true
  owners: {
    relationships: [
      globalAdminId
    ]
  }
}

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'db-${companyName}-${environment}-server'
  location: location
  properties: {
    version: '12.0'
    administratorLogin: 'rbj'
    administratorLoginPassword: sqlAdminPassword
    // The F1 App Service cannot use VNet integration. Restrict the public
    // endpoint to the App Service outbound IP allowlist managed below.
    publicNetworkAccess: 'Enabled'
    administrators:{
      administratorType: 'ActiveDirectory'
      login: sqlAdminGroupName
      sid: sqlAdminGroup.id
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: false
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'db-${companyName}-${environment}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource sqlFirewallManagerForIdentity 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sqlServer.id, identity.id, roles.sqlSecurityManager)
  scope: sqlServer
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.sqlSecurityManager)
  }
}

resource syncWebApiSqlFirewallRules 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'sync-web-api-sql-firewall-${toLower(environment)}'
  location: location
  kind: 'AzureCLI'
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    azCliVersion: '2.61.0'
    cleanupPreference: 'OnSuccess'
    retentionInterval: 'P1D'
    timeout: 'PT30M'
    forceUpdateTag: identity.id
    environmentVariables: [
      {
        name: 'RESOURCE_GROUP'
        value: resourceGroup().name
      }
      {
        name: 'SQL_SERVER_NAME'
        value: sqlServer.name
      }
      {
        name: 'OUTBOUND_IPS'
        value: webApi.properties.possibleOutboundIpAddresses
      }
    ]
    scriptContent: '''
set -euo pipefail

# Cap the time we spend in any single az call so a stuck control-plane
# response can't eat the whole deployment-script timeout.
export AZ_HTTP_TIMEOUT=60

az_with_retry() {
  local attempt=1
  local max_attempts=4

  while true; do
    if az "$@"; then
      return 0
    fi

    if [ "$attempt" -ge "$max_attempts" ]; then
      return 1
    fi

    sleep $((attempt * 5))
    attempt=$((attempt + 1))
  done
}

# Remove the managed App Service rules and the two legacy broad-access rules.
# Other deliberately configured firewall rules are left untouched.
delete_existing() {
  local raw
  raw=$(az_with_retry sql server firewall-rule list \
    --resource-group "$RESOURCE_GROUP" \
    --server "$SQL_SERVER_NAME" \
    --query "[?starts_with(name, 'AllowWebApiOutbound') || name == 'AllowAzureServices' || name == 'AllowDeveloperIP'].name" \
    --output tsv 2>/dev/null) || raw=""

  if [ -z "$raw" ]; then
    return 0
  fi

  local rule
  while IFS= read -r rule; do
    case "$rule" in
      AllowWebApiOutbound*|AllowAzureServices|AllowDeveloperIP)
az_with_retry sql server firewall-rule delete \
  --resource-group "$RESOURCE_GROUP" \
  --server "$SQL_SERVER_NAME" \
  --name "$rule" --output none ;;
      *)
        echo "skipping unexpected firewall-rule value: $rule" >&2 ;;
    esac
  done <<< "$raw"
}

create_rule() {
  local index="$1"
  local ip="$2"

  if ! [[ "$ip" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "skipping non-IPv4 value: $ip" >&2
    return 0
  fi

  az_with_retry sql server firewall-rule create \
    --resource-group "$RESOURCE_GROUP" \
    --server "$SQL_SERVER_NAME" \
    --name "AllowWebApiOutbound${index}" \
    --start-ip-address "$ip" \
    --end-ip-address "$ip" \
    --output none
}

export -f create_rule
export RESOURCE_GROUP SQL_SERVER_NAME

valid_ips=()
IFS=',' read -ra candidate_ips <<< "$OUTBOUND_IPS"
for ip in "${candidate_ips[@]}"; do
  trimmed_ip=$(echo "$ip" | xargs)
  if [[ "$trimmed_ip" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    valid_ips+=("$trimmed_ip")
  elif [ -n "$trimmed_ip" ]; then
    echo "skipping non-IPv4 value: $trimmed_ip" >&2
  fi
done

if [ "${#valid_ips[@]}" -eq 0 ]; then
  echo "App Service returned no valid outbound IP addresses; existing SQL firewall rules were not changed." >&2
  exit 1
fi

delete_existing

index=0
for ip in "${valid_ips[@]}"; do
  create_rule "$index" "$ip" &
  index=$((index + 1))

  # Cap concurrency so we don't hammer the SQL control plane with
  # dozens of simultaneous writes.
  if (( index % 8 == 0 )); then
    wait
  fi
done
wait
'''
  }
  dependsOn: [
    sqlFirewallManagerForIdentity
  ]
}

resource grantWebApiSqlAccess 'Microsoft.Resources/deploymentScripts@2023-08-01' = if (provisionWebApiSqlAccess) {
  name: 'grant-web-api-sql-access-${toLower(environment)}'
  location: location
  kind: 'AzureCLI'
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    azCliVersion: '2.61.0'
    cleanupPreference: 'OnSuccess'
    retentionInterval: 'P1D'
    timeout: 'PT20M'
    forceUpdateTag: identity.properties.clientId
    environmentVariables: [
      {
        name: 'RESOURCE_GROUP'
        value: resourceGroup().name
      }
      {
        name: 'SQL_SERVER_NAME'
        value: sqlServer.name
      }
      {
        name: 'SQL_SERVER_FQDN'
        value: sqlServer.properties.fullyQualifiedDomainName
      }
      {
        name: 'SQL_DATABASE_NAME'
        value: sqlDatabase.name
      }
      {
        name: 'WEB_API_SQL_USER_NAME'
        value: identity.name
      }
      {
        name: 'WEB_API_CLIENT_ID'
        value: identity.properties.clientId
      }
    ]
    scriptContent: '''
set -euo pipefail

install_sqlcmd() {
  if command -v sqlcmd >/dev/null 2>&1; then
    return 0
  fi

  apt-get update
  apt-get install -y curl gnupg apt-transport-https ca-certificates unixodbc
  curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb
  dpkg -i /tmp/packages-microsoft-prod.deb
  apt-get update
  ACCEPT_EULA=Y apt-get install -y mssql-tools18
  export PATH="$PATH:/opt/mssql-tools18/bin"
}

sql_user_sid=$(python3 - <<'PY'
import os
import uuid

print('0x' + uuid.UUID(os.environ['WEB_API_CLIENT_ID']).bytes_le.hex().upper())
PY
)

install_sqlcmd
export PATH="$PATH:/opt/mssql-tools18/bin"

az_with_retry() {
  local attempt=1
  local max_attempts=12

  while true; do
    if az "$@"; then
      return 0
    fi

    if [ "$attempt" -ge "$max_attempts" ]; then
      return 1
    fi

    sleep $((attempt * 10))
    attempt=$((attempt + 1))
  done
}

provisioning_ip=$(curl -fsSL https://api.ipify.org)
az_with_retry sql server firewall-rule create \
  --resource-group "$RESOURCE_GROUP" \
  --server "$SQL_SERVER_NAME" \
  --name "AllowSqlProvisioningScript" \
  --start-ip-address "$provisioning_ip" \
  --end-ip-address "$provisioning_ip" \
  --output none

cleanup() {
  az sql server firewall-rule delete --resource-group "$RESOURCE_GROUP" --server "$SQL_SERVER_NAME" --name "AllowSqlProvisioningScript" --output none || true
  rm -f /tmp/sqltoken /tmp/grant-web-api-access.sql
}
trap cleanup EXIT

cat > /tmp/grant-web-api-access.sql <<SQL
DECLARE @userName sysname = N'${WEB_API_SQL_USER_NAME}';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @userName)
BEGIN
  DECLARE @createUserSql nvarchar(max) = N'CREATE USER ' + QUOTENAME(@userName) + N' WITH SID = ${sql_user_sid}, TYPE = E;';
  EXEC sp_executesql @createUserSql;
END;

IF IS_ROLEMEMBER(N'db_datareader', @userName) <> 1
BEGIN
  EXEC(N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@userName));
END;

IF IS_ROLEMEMBER(N'db_datawriter', @userName) <> 1
BEGIN
  EXEC(N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@userName));
END;

IF IS_ROLEMEMBER(N'db_ddladmin', @userName) <> 1
BEGIN
  EXEC(N'ALTER ROLE db_ddladmin ADD MEMBER ' + QUOTENAME(@userName));
END;
SQL

run_sql_with_retry() {
  local attempt=1
  local max_attempts=12

  while true; do
    sql_token_resource="https://${SQL_SERVER_FQDN#*.}/"
    az account get-access-token --resource "$sql_token_resource" --query accessToken --output tsv | tr -d '\n' | iconv -f ascii -t UTF-16LE > /tmp/sqltoken

    if sqlcmd -S "$SQL_SERVER_FQDN" -d "$SQL_DATABASE_NAME" -G -P /tmp/sqltoken -b -l 30 -i /tmp/grant-web-api-access.sql; then
      return 0
    fi

    if [ "$attempt" -ge "$max_attempts" ]; then
      return 1
    fi

    sleep $((attempt * 10))
    attempt=$((attempt + 1))
  done
}

run_sql_with_retry
'''
  }
  dependsOn: [
    sqlFirewallManagerForIdentity
  ]
}

// ──────────────────────────────────────────────────────────
// Storage Account
// Used for document storage and workflow assets.
// Identity needs Blob contributor access for managed identity uploads.
// ──────────────────────────────────────────────────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  tags: tags
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource uploadsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'uploads'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'documents'
}

resource storageRoleBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid('${storageAccount.id}${identity.id}${roles.storageBlobContributor}')
  scope: storageAccount
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobContributor)
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Azure Communication Services
// Used for sending invite emails to new users via the ACS Email SDK.
// Authenticated through the shared user-assigned managed identity.
// ──────────────────────────────────────────────────────────────────────────────

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    dataLocation: 'europe'
    linkedDomains: [
      emailDomain.id
    ]
  }
}

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'europe'
  }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  name: 'AzureManagedDomain'
  parent: emailService
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
  }
}

resource senderUsername 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-03-31' = {
  parent: emailDomain
  name: 'DoNotReply'
  properties: {
    displayName: 'Workslip'
    username: 'DoNotReply'
  }
}
// ──────────────────────────────────────────────────────────────────────────────
// Outputs
// ──────────────────────────────────────────────────────────────────────────────

output STORAGE_ACCOUNT_NAME string             = storageAccount.name
output LOGIC_APP_NAME string                   = logicAppName
output WEB_API_NAME string                     = webApi.name
output WEB_API_DEFAULT_HOSTNAME string         = webApi.properties.defaultHostName
output WEB_API_URL string                      = 'https://${webApi.properties.defaultHostName}'
output WEB_API_SERVER_NAME string              = webApiServer.name
output MANAGED_IDENTITY_CLIENT_ID string       = identity.properties.clientId
output MANAGED_IDENTITY_PRINCIPAL_ID string    = identity.properties.principalId
output SQL_ADMIN_GROUP_ID string               = sqlAdminGroup.id
output GITHUB_FEDERATED_CREDENTIAL_SUBJECT string = githubFederatedCredential.properties.subject
output APP_INSIGHTS_CONNECTION_STRING string   = appInsights.properties.ConnectionString
output KEY_VAULT_URI string                    = keyVault.properties.vaultUri
output DOCUMENT_INTELLIGENCE_NAME string       = documentIntelligenceName
output AZURE_APP_CONFIG_ENDPOINT string         = appConfiguration.properties.endpoint
output AZURE_AD_OAUTH_APP_OBJECT_ID string      = EntraAppRegistrations.outputs.OAuthClientId
output AZURE_AD_OAUTH_APP_CLIENT_ID string      = EntraAppRegistrations.outputs.OAuthAppId
output ACS_ENDPOINT string                     = 'https://${communicationService.properties.hostName}'
output ACS_SENDER_ADDRESS string               = 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
