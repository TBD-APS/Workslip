@description('Azure region for the isolated Workslip demo environment.')
param location string = resourceGroup().location

@description('Stable prefix used for all demo resources.')
param namePrefix string = 'workslip-demo'

@description('SQL administrator login retained only as the Azure SQL creation bootstrap. Runtime and normal migrations use Entra identities.')
param sqlAdminLogin string = 'workslipdemobootstrap'

@secure()
@description('Random bootstrap-only SQL administrator password. Never passed to the runtime container or migration runner.')
param sqlAdminPassword string

param githubOwner string = 'rasm105k'
param githubOwnerId string = '31623093'
param githubRepository string = 'Workslip-v2.0'
param githubRepositoryId string = '1245555609'
param githubEnvironment string = 'demo'

var suffix = uniqueString(subscription().id, resourceGroup().id)
var compactPrefix = replace(namePrefix, '-', '')
var logAnalyticsName = 'log-${namePrefix}'
var appInsightsName = 'appi-${namePrefix}'
var environmentName = 'cae-${namePrefix}'
var registryName = take('acr${compactPrefix}${suffix}', 50)
var storageName = take('st${compactPrefix}${suffix}', 24)
// Match the repository's canonical migration runner naming contract so demo uses
// the exact same reviewed migration mechanism as other Workslip environments.
var sqlServerName = 'db-mrsoftware-demo-server'
var databaseName = 'db-mrsoftware-demo'
var runtimeIdentityName = 'id-${namePrefix}'
var migrationIdentityName = 'id-mrsoftware-demo-migration'

var tags = {
  environment: 'demo'
  workload: 'workslip'
  dataClassification: 'synthetic-only'
  managedBy: 'bicep'
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
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

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: listKeys(logAnalytics.id, '2022-10-01').primarySharedKey
      }
    }
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    policies: {
      quarantinePolicy: {
        status: 'disabled'
      }
    }
  }
}

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: runtimeIdentityName
  location: location
  tags: tags
}

resource migrationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: migrationIdentityName
  location: location
  tags: tags
}

resource migrationFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: migrationIdentity
  name: 'github-demo'
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubOwner}@${githubOwnerId}/${githubRepository}@${githubRepositoryId}:environment:${githubEnvironment}'
  }
}

var acrPullRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, runtimeIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    principalId: runtimeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleId
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'documents'
  properties: {
    publicAccess: 'None'
  }
}

var blobContributorRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
)

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, runtimeIdentity.id, blobContributorRoleId)
  scope: storage
  properties: {
    principalId: runtimeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: blobContributorRoleId
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// The deployment-only migration identity is the Azure SQL Entra administrator.
// The ordinary Container App identity receives only contained database roles later
// from the migration workflow and never receives server-level administration.
resource sqlEntraAdministrator 'Microsoft.Sql/servers/administrators@2023-08-01-preview' = {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: migrationIdentity.name
    sid: migrationIdentity.properties.principalId
    tenantId: tenant().tenantId
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}

output containerAppsEnvironmentName string = containerAppsEnvironment.name
output containerRegistryName string = registry.name
output containerRegistryLoginServer string = registry.properties.loginServer
output runtimeIdentityName string = runtimeIdentity.name
output runtimeIdentityId string = runtimeIdentity.id
output runtimeIdentityPrincipalId string = runtimeIdentity.properties.principalId
output runtimeIdentityClientId string = runtimeIdentity.properties.clientId
output migrationIdentityName string = migrationIdentity.name
output migrationIdentityClientId string = migrationIdentity.properties.clientId
output storageAccountName string = storage.name
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = database.name
output applicationInsightsConnectionString string = appInsights.properties.ConnectionString
