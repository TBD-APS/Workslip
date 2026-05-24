param appConfigurationName string
param managedIdentityClientId string
param appConfigurationEndpoint string
param azureAdOAuthClientId string
param workslipServerAppId string
param graphAppClientId string
param graphAppUserDomain string
param acsEndpoint string
param acsSenderAddress string
@secure()
param acsConnectionString string
param storageAccountName string
param applicationInsightsConnectionString string
param sqlConnectionString string
@secure()
param graphClientSecretKeyvault string

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
    value: 'api://${workslipServerAppId}'
  }
}

resource configGraphAppTenantId 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:GraphApp:TenantId'
  properties: {
    value: tenant().tenantId
  }
}

resource configGraphAppClientId 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:GraphApp:ClientId'
  properties: {
    value: graphAppClientId
  }
}

resource configGraphAppDefaultUserDomain 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:GraphApp:DefaultUserDomain'
  properties: {
    value: graphAppUserDomain
  }
}

resource configGraphAppWorkslipServerAppId 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:GraphApp:WorkslipServerAppId'
  properties: {
    value: workslipServerAppId
  }
}

resource configAcsEndpoint 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:Acs:Endpoint'
  properties: {
    value: acsEndpoint
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

resource configGraphAppClientSecret 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:GraphApp:ClientSecret'
  properties: {
    value: graphClientSecretKeyvault
    contentType: keyVaultReferenceContentType
  }
}

resource acsConnectionStringSecret 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:Acs:Connectionstring'
  properties: {
    value: acsConnectionString
    contentType: keyVaultReferenceContentType
  }
}

resource SqlConnectionString 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = {
  parent: appConfiguration
  name: 'Azure:Sql:Connectionstring'
  properties: {
    value: sqlConnectionString
    contentType: keyVaultReferenceContentType
  }
}
