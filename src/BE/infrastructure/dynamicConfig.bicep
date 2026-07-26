param appConfigurationName string
param managedIdentityClientId string
param appConfigurationEndpoint string
param azureAdOAuthClientId string
param clientAppId string
param oauthServerAppId string
param acsSenderAddress string
@secure()
param acsConnectionString string
param storageAccountName string
param applicationInsightsConnectionString string
param sqlConnectionString string
@secure()
param jwtSigninKey string

// ... (existing resources) ...

resource configClientAppId 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:AdOAuth:ClientAppId'
  properties: {
    value: clientAppId
  }
}

resource appConfiguration 'Microsoft.AppConfiguration/configurationStores@2023-03-01' existing = {
  name: appConfigurationName
}

resource configManagedIdentityClientId 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:ManagedIdentity:ClientId'
  properties: {
    value: managedIdentityClientId
  }
}

resource configAppConfigurationEndpoint 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:AppConfiguration:Endpoint'
  properties: {
    value: appConfigurationEndpoint
  }
}

resource configAdOAuthClientId 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:AdOAuth:ClientId'
  properties: {
    value: azureAdOAuthClientId
  }
}

resource configAdOAuthAudience 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:AdOAuth:Audience'
  properties: {
    value: 'api://${oauthServerAppId}'
  }
}


resource configOAuthServerAppId 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:GraphApp:OAuthAppId'
  properties: {
    value: oauthServerAppId
  }
}

resource configAcsSenderAddress 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:Acs:SenderAddress'
  properties: {
    value: acsSenderAddress
  }
}

resource configDocumentFileStorageAccountName 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:DocumentFileStorage:StorageAccountName'
  properties: {
    value: storageAccountName
  }
}

resource configApplicationInsightsConnectionString 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:ApplicationInsights:ConnectionString'
  properties: {
    value: applicationInsightsConnectionString
  }
}

//KEY VAULT REFERENCES

var keyVaultReferenceContentType = 'application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8'

resource acsConnectionStringSecret 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:Acs:ConnectionString'
  properties: {
    value: string({ uri: acsConnectionString })
    contentType: keyVaultReferenceContentType
  }
}

resource sqlConnectionStringValue 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:Sql:ConnectionString'
  properties: {
    value: sqlConnectionString
  }
}

resource JwtSigninKeySecret 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Jwt:SigningKey'
  properties: {
    value: string({ uri: jwtSigninKey })
    contentType: keyVaultReferenceContentType
  }
}
