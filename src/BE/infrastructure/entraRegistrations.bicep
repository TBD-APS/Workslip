param environment string
// Retained for compatibility with main.bicep. Application ownership is not
// mutated through Graph Bicep because relationship updates are non-atomic.
param globalAdminId string

module applications './entraApplications.bicep' = {
  name: 'entra-applications'
  params: {
    environment: environment
  }
}

module clientAccess './entraClientAccess.bicep' = {
  name: 'entra-client-access'
  params: {
    environment: environment
  }
  dependsOn: [
    applications
  ]
}

module finalize './entraFinalize.bicep' = {
  name: 'entra-finalize'
  params: {
    environment: environment
  }
  dependsOn: [
    clientAccess
  ]
}

output OAuthClientId string = finalize.outputs.OAuthClientId
output OAuthAppId string = finalize.outputs.OAuthAppId
output ClientAppId string = finalize.outputs.ClientAppId
output ClientAppObjectId string = finalize.outputs.ClientAppObjectId
