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

// Service principals are upserted by appId after both applications exist. This
// phase reads appId through existing Graph resources instead of relying on the
// partial response returned by an application upsert.
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
// runtime audience is api://{appId}. deploy.ps1 can resolve the application by
// this ID when managing credentials.
output OAuthClientId string = OAuthServerApp.appId
output OAuthAppId string = OAuthServerApp.appId
output ClientAppId string = WorkslipClientApp.appId
output ClientAppObjectId string = WorkslipClientApp.id
