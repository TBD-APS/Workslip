param companyName string = ''
param environment string = ''
param globalAdminId string = ''
param location string = resourceGroup().location
param storageAccountName string       = take('st${companyName}${toLower(environment)}', 24)
param logicAppName string             = 'la-${companyName}-${toLower(environment)}'
param appInsightsName string          = 'ai-${companyName}-${toLower(environment)}'
param logAnalyticsName string          = 'logAnal-${companyName}-${toLower(environment)}'
param appConfigurationName string     = take('appcs-${companyName}-${toLower(environment)}', 50)
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
}

var tags = {
  environment: environment
  project: companyName
}

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

    managedIdentityClientId: identity.properties.clientId
    appConfigurationEndpoint: 'https://${appConfiguration.name}.azconfig.io'
    
    azureAdOAuthClientId: '67882e61-7227-4c8a-bc55-dd36b2c59005'
    workslipServerAppId: '4b36d921-1eaa-4d29-a1ba-412f07eaefe6'
    graphAppClientId: '6af642a7-7877-4668-84d3-53dc52a9c796'
    graphAppUserDomain: 'rasmusvm6hotmail.onmicrosoft.com'
    graphClientSecretKeyvault: keyVaultConfigs.outputs.graphAppClientSecretUri

    acsConnectionString: keyVaultConfigs.outputs.acsConnectionStringSecretUri
    acsSenderAddress:  emailDomain.properties.mailFromSenderDomain
    acsEndpoint: 'https://${communicationService.properties.hostName}'
    
    storageAccountName: storageAccount.name
    applicationInsightsConnectionString: appInsights.properties.ConnectionString
  }
}


//Other apps can read directly from app config (azure functions, web api osv..)
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
    graphAppClientSecret: 'gFB8Q~Qc_FKeKWrfqnGjOqLqTu1Ds6rcZ8dyvbK5'
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
    // VIGTIGT 2: Data-lokationen skal være 'europe' i præcis dette felt
    dataLocation: 'europe' 
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


// ──────────────────────────────────────────────────────────────────────────────
// Outputs
// ──────────────────────────────────────────────────────────────────────────────

output STORAGE_ACCOUNT_NAME string             = storageAccount.name
output LOGIC_APP_NAME string                   = logicAppName
output MANAGED_IDENTITY_CLIENT_ID string       = identity.properties.clientId
output MANAGED_IDENTITY_PRINCIPAL_ID string    = identity.properties.principalId
output APP_INSIGHTS_CONNECTION_STRING string   = appInsights.properties.ConnectionString
output KEY_VAULT_URI string                    = keyVault.properties.vaultUri
//output DOCUMENT_INTELLIGENCE_ENDPOINT string   = documentIntelligence.properties.endpoint
output DOCUMENT_INTELLIGENCE_NAME string       = documentIntelligenceName
output AZURE_APP_CONFIG_ENDPOINT string         = appConfiguration.properties.endpoint
output ACS_ENDPOINT string                     = 'https://${communicationService.properties.hostName}'
output ACS_SENDER_ADDRESS string               = 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}'
