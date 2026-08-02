targetScope = 'subscription'

param companyName string = 'mrsoftware'
param environment string = 'prod'
param location string = 'westeurope'
param githubOwner string = 'rasm105k'
param githubOwnerId string = '31623093'
param githubRepository string = 'Workslip-v2.0'
param githubRepositoryId string = '1245555609'
param githubEnvironment string = environment

var normalizedEnvironment = toLower(environment)
var resourceGroupName = 'rg-${companyName}-${normalizedEnvironment}'
var identityName = take('id-${companyName}-${normalizedEnvironment}-infra-github', 128)
var identityResourceId = resourceId(subscription().subscriptionId, resourceGroupName, 'Microsoft.ManagedIdentity/userAssignedIdentities', identityName)
var providerRegistrationRoleName = 'Workslip Resource Provider Registration'
var providerRegistrationRoleId = guid(subscription().id, providerRegistrationRoleName)

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: resourceGroupName
}

module infrastructureIdentity './githubInfrastructureIdentity.bicep' = {
  name: 'github-infrastructure-identity-${normalizedEnvironment}'
  scope: resourceGroup
  params: {
    companyName: companyName
    environment: normalizedEnvironment
    location: location
    githubOwner: githubOwner
    githubOwnerId: githubOwnerId
    githubRepository: githubRepository
    githubRepositoryId: githubRepositoryId
    githubEnvironment: githubEnvironment
  }
}

resource providerRegistrationRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: providerRegistrationRoleId
  properties: {
    roleName: providerRegistrationRoleName
    description: 'Allows the Workslip GitHub infrastructure identity to read and register required Azure resource providers.'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.Resources/subscriptions/providers/read'
          'Microsoft.Resources/subscriptions/providers/register/action'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
    assignableScopes: [
      subscription().id
    ]
  }
}

resource providerRegistrationRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, identityResourceId, providerRegistrationRole.id)
  properties: {
    principalId: infrastructureIdentity.outputs.PRINCIPAL_ID
    principalType: 'ServicePrincipal'
    roleDefinitionId: providerRegistrationRole.id
  }
}

output IDENTITY_NAME string = infrastructureIdentity.outputs.IDENTITY_NAME
output CLIENT_ID string = infrastructureIdentity.outputs.CLIENT_ID
output PRINCIPAL_ID string = infrastructureIdentity.outputs.PRINCIPAL_ID
output FEDERATED_CREDENTIAL_SUBJECT string = infrastructureIdentity.outputs.FEDERATED_CREDENTIAL_SUBJECT
output PROVIDER_REGISTRATION_ROLE_ID string = providerRegistrationRole.id
