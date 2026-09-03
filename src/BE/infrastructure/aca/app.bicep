@description('Azure region for the Workslip live-app serverless runway.')
param location string = resourceGroup().location

@description('Stable prefix used for all live-app resources.')
param namePrefix string = 'workslip-live-app'

param containerAppsEnvironmentName string
param containerRegistryName string
param runtimeIdentityName string

@description('Immutable frontend image including tag, e.g. <acr>.azurecr.io/workslip-live-app-frontend:<sha>.')
param frontendImage string

@description('Immutable api image including tag, e.g. <acr>.azurecr.io/workslip-live-app-api:<sha>.')
param apiImage string

@description('Existing production App Configuration endpoint.')
param appConfigEndpoint string

@description('Existing production Application Insights connection string.')
param applicationInsightsConnectionString string

@description('Existing production SQL server that carries the live database.')
param sqlServerName string
param sqlServerFqdn string
param sqlDatabaseName string

@description('Existing production document-file storage account.')
param storageAccountName string

param apiEnvironment string = 'Production'

var appName = 'ca-${namePrefix}'
var tags = {
  environment: 'live'
  workload: 'workslip'
  managedBy: 'bicep'
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: runtimeIdentityName
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' existing = {
  name: sqlServerName
}

// Consumption Container Apps do not have a stable outbound IP unless a VNet/NAT
// boundary is added. Network reachability therefore uses Azure SQL's Azure-services
// firewall rule while authentication remains fail-closed to the dedicated managed
// identity contained database user. The boundary tradeoff is tracked in WOR-806.
resource allowAzureRuntime 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureLiveAppRuntime'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${runtimeIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource liveApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${runtimeIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'Auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: runtimeIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'frontend'
          image: frontendImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/login'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
        {
          name: 'api'
          image: apiImage
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: apiEnvironment
            }
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '5262'
            }
            {
              name: 'Azure__AppConfiguration__Endpoint'
              value: appConfigEndpoint
            }
            {
              name: 'Azure__ApplicationInsights__ConnectionString'
              value: applicationInsightsConnectionString
            }
            {
              name: 'Azure__ManagedIdentity__ClientId'
              value: runtimeIdentity.properties.clientId
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: runtimeIdentity.properties.clientId
            }
            {
              name: 'Azure__Sql__ConnectionString'
              value: sqlConnectionString
            }
            {
              name: 'Azure__DocumentFileStorage__StorageAccountName'
              value: storageAccountName
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 5262
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        // Keep one replica warm. This app serves the customer-facing production
        // domain, and both containers share a replica: scaling to zero means the
        // next visitor after an idle period waits for the .NET API to boot, open
        // its SQL pool and acquire a managed-identity token before the first
        // response. The App Service this replaces runs on F1 Free, where Always On
        // cannot be enabled at all, so cold starts were unavoidable there; a warm
        // replica here is the first chance to remove them rather than inherit them.
        minReplicas: 1
        maxReplicas: 4
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output containerAppName string = liveApp.name
output containerAppFqdn string = liveApp.properties.configuration.ingress.fqdn
output liveUrl string = 'https://${liveApp.properties.configuration.ingress.fqdn}'