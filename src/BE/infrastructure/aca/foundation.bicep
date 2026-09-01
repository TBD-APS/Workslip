extension microsoftGraphV1

@description('Azure region for the Workslip live-app serverless runway. Mirrors the live region.')
param location string = resourceGroup().location

@description('Stable prefix used for all live-app resources.')
param namePrefix string = 'workslip-live-app'

@description('Existing Log Analytics workspace that already receives live telemetry.')
param logAnalyticsWorkspaceId string

@description('Customer ID of the existing Log Analytics workspace.')
param logAnalyticsWorkspaceCustomerId string

@description('Existing production Key Vault that backs App Configuration secret references and scopes the shared data resource group.')
param keyVaultId string

param keyVaultName string
param appConfigurationName string
param storageAccountName string

param githubOwner string = 'rasm105k'
param githubOwnerId string = '31623093'
param githubRepository string = 'Workslip-v2.0'
param githubRepositoryId string = '1245555609'
param githubEnvironment string = 'live'

var suffix = uniqueString(subscription().id)
var compactPrefix = replace(namePrefix, '-', '')
var registryName = take('acr${compactPrefix}${suffix}', 50)
var managedEnvironmentName = 'cae-${namePrefix}'
var runtimeIdentityName = 'id-${namePrefix}'
var ciIdentityName = 'id-${namePrefix}-ci'

var dataSubscriptionId = split(keyVaultId, '/')[2]
var dataResourceGroupName = split(keyVaultId, '/')[4]

var tags = {
  environment: githubEnvironment
  workload: 'workslip'
  managedBy: 'bicep'
}

var workspaceKeys = listKeys(logAnalyticsWorkspaceId, '2023-09-01')

var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var acrPushRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
var contributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')

// Match the legacy API runtime identity exactly. Workslip intentionally stays
// single-tenant and provisions customer accounts as Entra B2B guests.
var graphRoleIds = {
  userReadWriteAll: '741f803b-c850-494e-b5df-cde7c675a1ca'
  userInviteAll: '09850681-111b-4a89-9bed-3f2cae46d706'
  applicationReadAll: '9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30'
  appRoleAssignmentReadWriteAll: '06b708a9-e830-4db3-a914-8e69da51d44f'
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
    // Basic ACR does not support IP or virtual-network rules. Image access is
    // still protected by Entra RBAC; move to Premium before adding network rules.
    publicNetworkAccess: 'Enabled'
  }
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: managedEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspaceCustomerId
        sharedKey: workspaceKeys.primarySharedKey
      }
    }
  }
}

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: runtimeIdentityName
  location: location
  tags: tags
}

resource microsoftGraphServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: '00000003-0000-0000-c000-000000000000'
}

resource graphUserReadWriteAllForRuntimeIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: graphRoleIds.userReadWriteAll
  principalId: runtimeIdentity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource graphUserInviteAllForRuntimeIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: graphRoleIds.userInviteAll
  principalId: runtimeIdentity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource graphApplicationReadAllForRuntimeIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: graphRoleIds.applicationReadAll
  principalId: runtimeIdentity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource graphAppRoleAssignmentReadWriteAllForRuntimeIdentity 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: graphRoleIds.appRoleAssignmentReadWriteAll
  principalId: runtimeIdentity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource runtimeFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: runtimeIdentity
  name: 'github'
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubOwner}@${githubOwnerId}/${githubRepository}@${githubRepositoryId}:environment:${githubEnvironment}'
  }
}

resource ciIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: ciIdentityName
  location: location
  tags: tags
}

resource ciFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: ciIdentity
  name: 'github'
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubOwner}@${githubOwnerId}/${githubRepository}@${githubRepositoryId}:environment:${githubEnvironment}'
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, runtimeIdentity.id, acrPullRoleDefinitionId)
  scope: registry
  properties: {
    roleDefinitionId: acrPullRoleDefinitionId
    principalId: runtimeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource acrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, ciIdentity.id, acrPushRoleDefinitionId)
  scope: registry
  properties: {
    roleDefinitionId: acrPushRoleDefinitionId
    principalId: ciIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource ciContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, ciIdentity.id, contributorRoleDefinitionId)
  properties: {
    roleDefinitionId: contributorRoleDefinitionId
    principalId: ciIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

module runtimeDataAccess 'runtimeDataAccess.bicep' = {
  name: 'runtime-data-access'
  scope: resourceGroup(dataSubscriptionId, dataResourceGroupName)
  params: {
    runtimePrincipalId: runtimeIdentity.properties.principalId
    keyVaultName: keyVaultName
    appConfigurationName: appConfigurationName
    storageAccountName: storageAccountName
  }
}

output managedEnvironmentName string = managedEnvironment.name
output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output runtimeIdentityName string = runtimeIdentity.name
output runtimeIdentityId string = runtimeIdentity.id
output runtimeIdentityPrincipalId string = runtimeIdentity.properties.principalId
output runtimeIdentityClientId string = runtimeIdentity.properties.clientId
output ciIdentityName string = ciIdentity.name
output ciIdentityClientId string = ciIdentity.properties.clientId
