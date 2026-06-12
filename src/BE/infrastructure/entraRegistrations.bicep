extension microsoftGraphV1

param environment string
param globalAdminId string

var uniqueSuffix = substring(uniqueString(subscription().id, resourceGroup().id, environment), 0, 5)

resource OAuthServerApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'Oauth-server-${environment}-${uniqueSuffix}'
  displayName: 'Oauth server ${environment}'
  signInAudience: 'AzureADMyOrg'
  publicClient: {
    redirectUris: [
      'nativepasskeydemo://auth'
    ]
  }
  owners: {relationships: [
    globalAdminId
  ]}

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
        id: guid('access_as_user')
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
  publicClient: {
    redirectUris: [
      'http://localhost:5173/callback'
      'https://workslip-v2-0.vercel.app/callback'
      'https://oauth.pstmn.io/v1/callback'
    ]
  }
  owners: {relationships: [
    globalAdminId
  ]}
  
  // Implicit grant for SPA
  web: {
    implicitGrantSettings: {
      enableAccessTokenIssuance: false
      enableIdTokenIssuance: true
    }
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
          id: guid('access_as_user') // Must match the ID defined in OAuthServerApp
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
