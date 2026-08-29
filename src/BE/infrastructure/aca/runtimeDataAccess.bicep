@description('Runtime identity that executes the serverless Workslip app.')
param runtimePrincipalId string

@description('Existing production Key Vault that backs App Configuration secret references.')
param keyVaultName string

@description('Existing production App Configuration store that owns runtime configuration.')
param appConfigurationName string

@description('Existing production document-file storage account.')
param storageAccountName string

var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var appConfigurationDataReaderRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0282681b-8069-42b8-b4af-1c9b6845f2e4')
var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource appConfiguration 'Microsoft.AppConfiguration/configurationStores@2023-03-01' existing = {
  name: appConfigurationName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, runtimePrincipalId, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalId: runtimePrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource appConfigurationDataReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appConfiguration.id, runtimePrincipalId, appConfigurationDataReaderRoleDefinitionId)
  scope: appConfiguration
  properties: {
    roleDefinitionId: appConfigurationDataReaderRoleDefinitionId
    principalId: runtimePrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource storageBlobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, runtimePrincipalId, storageBlobDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
    principalId: runtimePrincipalId
    principalType: 'ServicePrincipal'
  }
}