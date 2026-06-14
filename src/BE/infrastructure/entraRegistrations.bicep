extension microsoftGraphV1

param environment string
param globalAdminId string

var uniqueSuffix = substring(uniqueString(subscription().id, resourceGroup().id, environment), 0, 5)

// Fast defineret GUID til API-scopet, så det ikke ændrer sig på tværs af miljøer
var apiScopeId = 'c2e2bf46-f94d-4c3e-86d7-ca425e4c6e2a'

resource OAuthServerApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'Oauth-server-${environment}-${uniqueSuffix}'
  displayName: 'Oauth server ${environment}'
  signInAudience: 'AzureADandPersonalMicrosoftAccount'
  
  publicClient: {
    redirectUris: [
      'nativepasskeydemo://auth'
    ]
  }
  owners: {
    relationships: [
      globalAdminId
    ]
  }

  appRoles: [
    {
      id: guid('SuperAdmin', environment)
      allowedMemberTypes: [
        'User'
      ]
      displayName: 'SuperAdmin'
      description: 'Super administrator'
      value: 'SuperAdmin'
      isEnabled: true
    }
    {
      id: guid('Admin', environment)
      allowedMemberTypes: [
        'User'
      ]
      displayName: 'Admin'
      description: 'Administrator'
      value: 'Admin'
      isEnabled: true
    }
    {
      id: guid('User', environment)
      allowedMemberTypes: [
        'User'
      ]
      displayName: 'User'
      description: 'Standard user'
      value: 'User'
      isEnabled: true
    }
  ]

  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: apiScopeId
        adminConsentDescription: 'Access Workslip API as the signed-in user'
        adminConsentDisplayName: 'Access Workslip API'
        userConsentDescription: 'Access Workslip API on your behalf'
        userConsentDisplayName: 'Access Workslip API'
        value: 'access_as_user'
        type: 'User'
        isEnabled: true
      }
    ]
  }
}

resource WorkslipClientApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'Workslip-client-${environment}-${uniqueSuffix}'
  displayName: 'Workslip Client ${environment}'
  signInAudience: 'AzureADMyOrg'
  spa: {
    redirectUris: [
      'http://localhost:5173/login'
      'http://localhost:5173/invite/callback'
      'https://workslip-v2-0.vercel.app/login'
      'https://workslip-v2-0.vercel.app/invite/callback'
      'https://webapp-delta-sand-62.vercel.app/login'
      'https://webapp-delta-sand-62.vercel.app/invite/callback'
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
  owners: {
    relationships: [
      globalAdminId
    ]
  }

  requiredResourceAccess: [
    {
      resourceAppId: '00000003-0000-0000-c000-000000000000' // Microsoft Graph
      resourceAccess: [
        {
          id: 'e1fe6dd8-ba31-4d61-89e7-886398468305' // User.Read
          type: 'Scope'
        }
      ]
    }
    {
      resourceAppId: OAuthServerApp.appId // Workslip API
      resourceAccess: [
        {
          id: apiScopeId // Matcher nu direkte den faste GUID fra serveren
          type: 'Scope'
        }
      ]
    }
  ]
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

output OAuthClientId string = OAuthServerApp.appId
output OAuthAppId string = OAuthServerApp.id
output ClientAppId string = WorkslipClientApp.appId
output ClientAppObjectId string = WorkslipClientApp.id