param companyName string = 'mrsoftwareinc'
param environment string = 'prod'
param location string = resourceGroup().location
@secure()
param sqlAdminPassword string

var normalizedEnvironment = toLower(environment)
var compactCompanyName = replace(toLower(companyName), '-', '')
var tags = {
  environment: environment
  project: companyName
  deploymentStage: 'base'
}

var storageAccountName = take('st${compactCompanyName}${normalizedEnvironment}', 24)
var appInsightsName = take('ai-${companyName}-${normalizedEnvironment}', 260)
var logAnalyticsName = take('logAnal-${companyName}-${normalizedEnvironment}', 63)
var webApiServerName = take('plan-${companyName}-${normalizedEnvironment}', 40)
var webApiName = take('api-${companyName}-${normalizedEnvironment}', 60)
var appConfigurationName = take('appcs-${companyName}-${normalizedEnvironment}', 50)
var keyVaultName = take('kv-${companyName}-${normalizedEnvironment}', 24)
var sqlServerName = take('db-${companyName}-${normalizedEnvironment}-server', 63)
var sqlDatabaseName = take('db-${companyName}-${normalizedEnvironment}', 128)

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    workspaceCapping: {
      dailyQuotaGb: 1
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
  properties: {
    serverFarmId: webApiServer.id
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
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
          name: 'Azure__AppConfiguration__Endpoint'
          value: appConfiguration.properties.endpoint
        }
        {
          name: 'Azure__ApplicationInsights__ConnectionString'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Azure__ApplicationInsights__WorkspaceId'
          value: logAnalyticsWorkspace.properties.customerId
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
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

resource appConfiguration 'Microsoft.AppConfiguration/configurationStores@2023-03-01' = {
  name: appConfigurationName
  location: location
  sku: {
    name: 'free'
  }
  tags: tags
  properties: {
    createMode: 'Default'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'standard'
      family: 'A'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    administratorLogin: 'rbj'
    administratorLoginPassword: sqlAdminPassword
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
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
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource uploadsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'uploads'
  properties: {
    publicAccess: 'None'
  }
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'documents'
  properties: {
    publicAccess: 'None'
  }
}

output RESOURCE_GROUP_NAME string = resourceGroup().name
output WEB_API_NAME string = webApi.name
output WEB_API_URL string = 'https://${webApi.properties.defaultHostName}'
output APP_CONFIGURATION_NAME string = appConfiguration.name
output APP_CONFIGURATION_ENDPOINT string = appConfiguration.properties.endpoint
output KEY_VAULT_NAME string = keyVault.name
output KEY_VAULT_URI string = keyVault.properties.vaultUri
output STORAGE_ACCOUNT_NAME string = storageAccount.name
output SQL_SERVER_NAME string = sqlServer.name
output SQL_DATABASE_NAME string = sqlDatabase.name
output APP_INSIGHTS_NAME string = appInsights.name
output LOG_ANALYTICS_NAME string = logAnalyticsWorkspace.name
