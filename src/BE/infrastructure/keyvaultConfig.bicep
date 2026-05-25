param keyVaultName string
param communicationServiceName string
param sqlConnectionString string
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

resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Azure--Sql--ConnectionString'
  properties: {
    value: sqlConnectionString
  }
}

var generatedSigningKey = base64(uniqueString(subscription().id, resourceGroup().id))
resource localJwtSigninKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Jwt--SigninKey'
  properties: {
    value: generatedSigningKey
  }
}

output acsConnectionStringSecretUri string = acsConnectionStringSecret.properties.secretUri
output sqlConnectionstring string = sqlConnectionStringSecret.properties.secretUri
output jwtSigninKey string = localJwtSigninKey.properties.secretUri
