targetScope = 'subscription'

param companyName string = 'mrsoftwareinc'
param environment string = 'prod'
param location string = 'westeurope'
@secure()
param sqlAdminPassword string

var normalizedEnvironment = toLower(environment)
var resourceGroupName = 'rg-${companyName}-${normalizedEnvironment}'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: {
    environment: environment
    project: companyName
    deploymentStage: 'base'
  }
}

module baseResources './base.resources.bicep' = {
  name: 'base-resources-${normalizedEnvironment}'
  scope: resourceGroup
  params: {
    companyName: companyName
    environment: environment
    location: location
    sqlAdminPassword: sqlAdminPassword
  }
}

output RESOURCE_GROUP_NAME string = resourceGroup.name
output WEB_API_NAME string = baseResources.outputs.WEB_API_NAME
output WEB_API_URL string = baseResources.outputs.WEB_API_URL
output APP_CONFIGURATION_NAME string = baseResources.outputs.APP_CONFIGURATION_NAME
output APP_CONFIGURATION_ENDPOINT string = baseResources.outputs.APP_CONFIGURATION_ENDPOINT
output KEY_VAULT_NAME string = baseResources.outputs.KEY_VAULT_NAME
output KEY_VAULT_URI string = baseResources.outputs.KEY_VAULT_URI
output STORAGE_ACCOUNT_NAME string = baseResources.outputs.STORAGE_ACCOUNT_NAME
output SQL_SERVER_NAME string = baseResources.outputs.SQL_SERVER_NAME
output SQL_DATABASE_NAME string = baseResources.outputs.SQL_DATABASE_NAME
output APP_INSIGHTS_NAME string = baseResources.outputs.APP_INSIGHTS_NAME
output LOG_ANALYTICS_NAME string = baseResources.outputs.LOG_ANALYTICS_NAME
