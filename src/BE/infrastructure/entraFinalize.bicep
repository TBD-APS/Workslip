extension microsoftGraphV1

param environment string

var oauthServerUniqueName = 'workslip-oauth-server-${toLower(environment)}'
var workslipClientUniqueName = 'workslip-client-${toLower(environment)}'

resource OAuthServerApp 'Microsoft.Graph/applications@v1.0' existing = {
  uniqueName: oauthServerUniqueName
}

resource WorkslipClientApp 'Microsoft.Graph/applications@v1.0' existing = {
  uniqueName: workslipClientUniqueName
}

resource OAuthServerServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: OAuthServerApp.appId
  tags: [
    'WindowsAzureActiveDirectoryIntegratedApp'
  ]
}

resource WorkslipClientServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: WorkslipClientApp.appId
  tags: [
    'WindowsAzureActiveDirectoryIntegratedApp'
  ]
}

// OAuthClientId and OAuthAppId are both the application/client ID because the
// runtime audience is api://{appId}. The application object ID is not required
// by Bicep consumers; deploy.ps1 can resolve the app by client ID.
output OAuthClientId string = OAuthServerApp.appId
output OAuthAppId string = OAuthServerApp.appId
output ClientAppId string = WorkslipClientApp.appId
output ClientAppObjectId string = WorkslipClientApp.id
