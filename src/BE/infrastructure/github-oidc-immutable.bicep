targetScope = 'resourceGroup'

param companyName string = 'npteknik'
param environment string = 'prod'
param githubOwner string = 'rasm105k'
param githubOwnerId string = '31623093'
param githubRepository string = 'Workslip-v2.0'
param githubRepositoryId string = '1245555609'

var deploymentIdentityName = take('id-${companyName}-${toLower(environment)}-github', 128)
var immutableSubject = 'repo:${githubOwner}@${githubOwnerId}/${githubRepository}@${githubRepositoryId}:environment:${environment}'

resource githubDeploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: deploymentIdentityName
}

resource githubImmutableFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: githubDeploymentIdentity
  name: 'github-${toLower(environment)}-immutable'
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: immutableSubject
  }
}

output GITHUB_IMMUTABLE_FEDERATED_CREDENTIAL_SUBJECT string = githubImmutableFederatedCredential.properties.subject
