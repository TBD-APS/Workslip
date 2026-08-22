param companyName string
param environment string
param location string
param deploySql bool = false
@secure()
param sqlAdminPassword string = ''

var compactCompany = replace(toLower(companyName), '-', '')
var storageAccountName = take('st${compactCompany}${environment}', 24)
var appInsightsName = 'ai-${companyName}-${environment}'
var logAnalyticsName = 'logAnal-${companyName}-${environment}'
var appServicePlanName = 'plan-${companyName}-${environment}'
var webApiName = 'api-${companyName}-${environment}'
var appConfigName = take('appcs-${companyName}-${environment}', 50)
var keyVaultName = take('kv-${companyName}-${environment}', 24)
var sqlServerName = take('db-${companyName}-${environment}-server', 63)
var sqlDatabaseName = take('db-${companyName}-${environment}', 128)
var tags = {
  environment: environment
  project: companyName
  deploymentStage: 'migration'
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    workspaceCapping: { dailyQuotaGb: 1 }
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
    WorkspaceResourceId: logAnalytics.id
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  properties: {}
}

resource appConfig 'Microsoft.AppConfiguration/configurationStores@2023-03-01' = {
  name: appConfigName
  location: location
  tags: tags
  sku: { name: 'free' }
  properties: {
    createMode: 'Default'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource webApi 'Microsoft.Web/sites@2023-12-01' = {
  name: webApiName
  location: location
  kind: 'app'
  tags: tags
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
      netFrameworkVersion: 'v10.0'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Azure__AppConfiguration__Endpoint', value: appConfig.properties.endpoint }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
    }
  }
}

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

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
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
  parent: storage
  name: 'default'
}

resource uploads 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'uploads'
  properties: { publicAccess: 'None' }
}

resource documents 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'documents'
  properties: { publicAccess: 'None' }
}

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = if (deploySql) {
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

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = if (deploySql) {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: { name: 'Basic', tier: 'Basic' }
  properties: { requestedBackupStorageRedundancy: 'Local' }
}

output webApiUrl string = 'https://${webApi.properties.defaultHostName}'
output appConfigName string = appConfig.name
output keyVaultName string = keyVault.name
output storageAccountName string = storage.name
output sqlServerName string = deploySql ? sqlServer.name : ''
