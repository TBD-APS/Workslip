
// ──────────────────────────────────────────────────────────────────────────────
// Document Intelligence
// ──────────────────────────────────────────────────────────────────────────────/
/*
resource documentIntelligence 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: documentIntelligenceName
  location: location
  kind: 'FormRecognizer'
  sku: { name: 'F0' }
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    restore: false
    customSubDomainName: documentIntelligenceName
    publicNetworkAccess: 'Enabled'
  }
}

resource diRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid('${documentIntelligence.id}${identity.id}${roles.cognitiveServicesUser}')
  scope: documentIntelligence
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesUser)
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Logic App Connections
// ──────────────────────────────────────────────────────────────────────────────

resource blobConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'azureblob'
  location: location
  properties: {
    displayName: 'azureblob'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'azureblob')
    }
  }
}

resource formRecognizerConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'formrecognizer'
  location: location
  properties: {
    displayName: 'formrecognizer'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'formrecognizer')
    }
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Logic App Workflow
// ──────────────────────────────────────────────────────────────────────────────

var workflowTemplate = loadTextContent('./logic-app/workflow.json')
var workflowDefinition = json(
  replace(workflowTemplate, '__STORAGE_ACCOUNT_NAME__', storageAccountName)
)

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: logicAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    state: 'Enabled'
    definition: workflowDefinition
    parameters: {
      '$connections': {
        value: {
          azureblob: {
            connectionId: blobConnection.id
            connectionName: 'azureblob'
            connectionProperties: {
              authentication: {
                type: 'ManagedServiceIdentity'
                identity: identity.id
              }
            }
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'azureblob')
          }
          formrecognizer: {
            connectionId: formRecognizerConnection.id
            connectionName: 'formrecognizer'
            connectionProperties: {
              authentication: {
                type: 'ManagedServiceIdentity'
                identity: identity.id
              }
            }
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'formrecognizer')
          }
        }
      }
    }
  }
}
*/
