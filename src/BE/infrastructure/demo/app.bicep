param location string = resourceGroup().location
param namePrefix string = 'workslip-demo'
param containerAppsEnvironmentName string
param containerRegistryName string
param runtimeIdentityName string
param storageAccountName string
param frontendImage string
param apiImage string
param applicationInsightsConnectionString string
param demoAdminEmail string = 'admin@17v3ygzs.mailosaur.net'

@secure()
param sqlConnectionString string

@secure()
param jwtSigningKey string

var appName = 'ca-${namePrefix}'
var tags = {
  environment: 'demo'
  workload: 'workslip'
  dataClassification: 'synthetic-only'
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

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource demoApp 'Microsoft.App/containerApps@2024-03-01' = {
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
      secrets: [
        {
          name: 'sql-connection'
          value: sqlConnectionString
        }
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
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
              value: 'Demo'
            }
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '5262'
            }
            {
              name: 'DemoMode__Enabled'
              value: 'true'
            }
            {
              name: 'DemoMode__AdminEmail'
              value: demoAdminEmail
            }
            {
              name: 'Azure__ManagedIdentity__ClientId'
              value: runtimeIdentity.properties.clientId
            }
            {
              name: 'Azure__DocumentFileStorage__StorageAccountName'
              value: storage.name
            }
            {
              name: 'Azure__ApplicationInsights__ConnectionString'
              value: applicationInsightsConnectionString
            }
            {
              name: 'Azure__Sql__ConnectionString'
              secretRef: 'sql-connection'
            }
            {
              name: 'Jwt__Issuer'
              value: 'workslip-demo'
            }
            {
              name: 'Jwt__Audience'
              value: 'workslip-demo'
            }
            {
              name: 'Jwt__SigningKey'
              secretRef: 'jwt-signing-key'
            }
            {
              name: 'Workslip__SeedDevelopmentData'
              value: 'false'
            }
            {
              name: 'ReleaseTesting__Enabled'
              value: 'false'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
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

output containerAppName string = demoApp.name
output containerAppFqdn string = demoApp.properties.configuration.ingress.fqdn
output demoUrl string = 'https://${demoApp.properties.configuration.ingress.fqdn}'
