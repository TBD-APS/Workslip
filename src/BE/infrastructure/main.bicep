param companyName string = ''
param environment string = ''
param globalAdminId string = ''
param location string = resourceGroup().location
param secureAdminName string = 'rbjadmin'
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

// ── Role definition IDs ───────────────────────────────────────────────────────
// Centralised here so they're easy to audit and update.
var roles = {
  cognitiveServicesUser:   'a97b65f3-24c7-4388-baec-2e87135dc908'
  storageBlobContributor:  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  appConfigurationDataReader: '516239f1-63e1-4d78-a4de-a74fb236a071'
  keyVaultAdministrator: '00482a5a-887f-4fb3-b363-3b7fe8e74483'
  keyVaultSecretsUserRole: '4633458b-17de-408a-b874-0445c86b69e6'
  appConfigurationDataOwnerRole: '5ae67dd6-50cb-40e7-96ff-dc2bfa4b606b'


  UserReadWriteAll: '741f1ec0-4c47-4952-b971-50c2d3d7d31f'
  ApplicationReadAll: '9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30'
  AppRoleAssignmentReadWriteAll: '06b03e2b-286b-4043-9a0b-116a43319a53'
  UserAuthenticationMethodReadWriteAll: '48db3110-388d-4be9-b467-36e2f11ffc8f'
}

var tags = {
  environment: environment
  project: companyName
}

var appInsightsConnectionString = appInsights.properties.ConnectionString
var appInsightsInstrumentationKey = appInsights.properties.InstrumentationKey

// ──────────────────────────────────────────────────────────────────────────────
// User-Assigned Managed Identity
// One identity, shared by all resources. All RBAC is granted to this identity.
// ──────────────────────────────────────────────────────────────────────────────

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
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
      use32BitWorkerProcess: false
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
        {
          name: 'KEY_VAULT_URL'
          value: keyVault.properties.vaultUri
        }
      ]
    }
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
    oauthServerAppId: EntraAppRegistrations.outputs.OAuthAppId

    acsConnectionString: keyVaultConfigs.outputs.acsConnectionStringSecretUri
    acsSenderAddress:  '${senderUsername.properties.username}@${emailDomain.properties.fromSenderDomain}'
    acsEndpoint: 'https://${communicationService.properties.hostName}'

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
    sqlConnectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=db-${companyName}-${environment}; TrustServerCertificate=False; Authentication="Active Directory Default";'
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


resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'db-${companyName}-server'
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: secureAdminName
      sid: globalAdminId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true // <-- DETTE DEAKTIVERER SQL PASSWORDS PERMANENT
    }
    version: '12.0'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'db-${companyName}-${environment}'
  location: location
  sku: {
    name: 'GP_S_Gen5_1' // General Purpose, Serverless, Gen5 (Kræves for Free Offer)
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1 // 1 vCore
  }

  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368 // 32 GB (Det maksimale tilladte for den gratis aftale)

    // Vi vælger Serverless, så den pauser automatisk når du ikke bruger den (sparer på de gratis sekunder)
    requestedBackupStorageRedundancy: 'Local'
  }
}

var developerIp = '83.93.49.174'
resource firewallAllowAzureIPs 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: developerIp
    endIpAddress: developerIp
  }
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
output APP_INSIGHTS_CONNECTION_STRING string   = appInsights.properties.ConnectionString
output KEY_VAULT_URI string                    = keyVault.properties.vaultUri
output DOCUMENT_INTELLIGENCE_NAME string       = documentIntelligenceName
output AZURE_APP_CONFIG_ENDPOINT string         = appConfiguration.properties.endpoint
output AZURE_AD_OAUTH_APP_OBJECT_ID string      = EntraAppRegistrations.outputs.OAuthClientId
output AZURE_AD_OAUTH_APP_CLIENT_ID string      = EntraAppRegistrations.outputs.OAuthAppId
output ACS_ENDPOINT string                     = 'https://${communicationService.properties.hostName}'
output ACS_SENDER_ADDRESS string               = 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
