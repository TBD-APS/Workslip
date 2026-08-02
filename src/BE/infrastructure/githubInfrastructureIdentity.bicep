targetScope = 'resourceGroup'

param companyName string
param environment string
param location string = resourceGroup().location
param githubOwner string
param githubOwnerId string
param githubRepository string
param githubRepositoryId string
param githubEnvironment string

var normalizedEnvironment = toLower(environment)
var identityName = take('id-${companyName}-${normalizedEnvironment}-infra-github', 128)
var keyVaultName = take('kv-${companyName}-${normalizedEnvironment}', 24)
var appConfigurationName = take('appcs-${companyName}-${normalizedEnvironment}', 50)

var roleDefinitionIds = {
  contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  roleBasedAccessControlAdministrator: 'f58310d9-a9f6-439a-9e8d-f62e7b41a168'
  keyVaultAdministrator: '00482a5a-887f-4fb3-b363-3b7fe8e74483'
  appConfigurationDataOwner: '5ae67dd6-50cb-40e7-96ff-dc2bfa4b606b'
}

var tags = {
  environment: normalizedEnvironment
  project: companyName
  purpose: 'github-infrastructure-deployment'
}

resource infrastructureIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
}

resource githubFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: infrastructureIdentity
  name: 'github-${normalizedEnvironment}'
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubOwner}@${githubOwnerId}/${githubRepository}@${githubRepositoryId}:environment:${githubEnvironment}'
  }
}

resource resourceGroupContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, infrastructureIdentity.id, roleDefinitionIds.contributor)
  scope: resourceGroup()
  properties: {
    principalId: infrastructureIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionIds.contributor)
  }
}

resource resourceGroupRbacAdministrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, infrastructureIdentity.id, roleDefinitionIds.roleBasedAccessControlAdministrator)
  scope: resourceGroup()
  properties: {
    principalId: infrastructureIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionIds.roleBasedAccessControlAdministrator)
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource keyVaultAdministrator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, infrastructureIdentity.id, roleDefinitionIds.keyVaultAdministrator)
  scope: keyVault
  properties: {
    principalId: infrastructureIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionIds.keyVaultAdministrator)
  }
}

resource appConfiguration 'Microsoft.AppConfiguration/configurationStores@2023-03-01' existing = {
  name: appConfigurationName
}

resource appConfigurationDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appConfiguration.id, infrastructureIdentity.id, roleDefinitionIds.appConfigurationDataOwner)
  scope: appConfiguration
  properties: {
    principalId: infrastructureIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionIds.appConfigurationDataOwner)
  }
}

output IDENTITY_NAME string = infrastructureIdentity.name
output CLIENT_ID string = infrastructureIdentity.properties.clientId
output PRINCIPAL_ID string = infrastructureIdentity.properties.principalId
output FEDERATED_CREDENTIAL_SUBJECT string = githubFederatedCredential.properties.subject
