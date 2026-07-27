extension microsoftGraphV1

param environment string

var oauthServerUniqueName = 'workslip-oauth-server-${toLower(environment)}'
var workslipClientUniqueName = 'workslip-client-${toLower(environment)}'
var apiScopeId = 'c2e2bf46-f94d-4c3e-86d7-ca425e4c6e2a'

resource OAuthServerApp 'Microsoft.Graph/applications@v1.0' existing = {
  uniqueName: oauthServerUniqueName
}

resource WorkslipClientApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: workslipClientUniqueName
  displayName: 'Workslip App'
  signInAudience: 'AzureADandPersonalMicrosoftAccount'
  api: {
    requestedAccessTokenVersion: 2
  }
  spa: {
    redirectUris: [
      'http://localhost:5270/login'
      'http://localhost:5270/invite/callback'
      'https://app.mrsoftware.dk/login'
      'https://app.mrsoftware.dk/invite/callback'
      'https://workslip-v2-0.vercel.app/login'
      'https://workslip-v2-0.vercel.app/invite/callback'
    ]
  }
  web: {
    redirectUris: [
      'https://oauth.pstmn.io/v1/callback'
    ]
    implicitGrantSettings: {
      enableAccessTokenIssuance: false
      enableIdTokenIssuance: true
    }
  }
  requiredResourceAccess: [
    {
      resourceAppId: '00000003-0000-0000-c000-000000000000'
      resourceAccess: [
        {
          id: 'e1fe6dd8-ba31-4d61-89e7-88639da4683d'
          type: 'Scope'
        }
      ]
    }
    {
      resourceAppId: OAuthServerApp.appId
      resourceAccess: [
        {
          id: apiScopeId
          type: 'Scope'
        }
      ]
    }
  ]
}
