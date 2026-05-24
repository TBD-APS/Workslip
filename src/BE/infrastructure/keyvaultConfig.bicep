@secure()
param graphAppClientSecret string
param keyVaultName string
param communicationServiceName string

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-03-31' existing = {
  name: communicationServiceName
}

resource graphAppClientSecretSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Azure--GraphApp--ClientSecret'
  properties: {
    value: graphAppClientSecret
  }
}

resource acsConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Azure--Acs--ConnectionString'
  properties: {
    value: communicationService.listKeys().primaryConnectionString
  }
}


output graphAppClientSecretUri string = graphAppClientSecretSecret.properties.secretUri
output acsConnectionStringSecretUri string = acsConnectionStringSecret.properties.secretUri
