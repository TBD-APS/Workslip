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

resource OAuthServerServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: OAuthServerApp.appId
  tags: [
    'WindowsAzureActiveDirectoryIntegratedApp'
  ]
}

output OAuthClientId string = OAuthServerApp.appId
output OAuthAppId string = OAuthServerApp.id
