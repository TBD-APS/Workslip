targetScope = 'resourceGroup'

@description('Deployment environment name')
param environment string = 'dev'

@description('Azure region for Sassy resources')
param location string = resourceGroup().location

param namePrefix string = 'sassy'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-${environment}-identity'
  location: location
}

resource appConfig 'Microsoft.AppConfiguration/configurationStores@2023-03-01' = {
  name: '${namePrefix}-${environment}-config'
  location: location
  sku: {
    name: 'standard'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${namePrefix}-${environment}-kv'
  location: location
  properties: {
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    sku: {
      family: 'A'
      name: 'standard'
    }
  }
}

output managedIdentityId string = identity.id
output appConfigurationId string = appConfig.id
output keyVaultId string = keyVault.id
