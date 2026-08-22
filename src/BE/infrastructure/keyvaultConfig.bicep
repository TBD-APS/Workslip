param keyVaultName string
param communicationServiceName string

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-03-31' existing = {
  name: communicationServiceName
}

resource acsConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Azure--Acs--ConnectionString'
  properties: {
    value: communicationService.listKeys().primaryConnectionString
  }
}

var acsConnectionStringSecretUri = 'https://${keyVaultName}.vault.azure.net/secrets/Azure--Acs--ConnectionString'
var sqlConnectionStringSecretUri = 'https://${keyVaultName}.vault.azure.net/secrets/Azure--Sql--ConnectionString'
var jwtSigningKeySecretUri = 'https://${keyVaultName}.vault.azure.net/secrets/Jwt--SigningKey'

output acsConnectionStringSecretUri string = acsConnectionStringSecretUri
output sqlConnectionstring string = sqlConnectionStringSecretUri
output jwtSigninKey string = jwtSigningKeySecretUri
