extension microsoftGraphV1
param environment string
param globalAdminId string

resource ServerApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'Oauth-server-${environment}'
  displayName: 'Oauth server ${environment}'
  signInAudience: 'AzureADMyOrg'
  publicClient: {
    redirectUris: [
      'nativepasskeydemo://auth'
      'http://localhost'
    ]
  }
  owners: {relationships: [
    globalAdminId
  ]}
  appRoles: [
    {
      id: guid('SuperAdmin')
      allowedMemberTypes: [
        'User'
      ]
      displayName: 'SuperAdmin'
      description: 'Super administrator'
      value: 'SuperAdmin'
      isEnabled: true
    }
    {
      id: guid('Admin')
      allowedMemberTypes: [
        'User'
      ]
      displayName: 'Admin'
      description: 'Administrator'
      value: 'Admin'
      isEnabled: true
    }
    {
      id: guid('User')
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

resource ServerServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: ServerApp.appId
  
}


output workslipServerId string = ServerApp.id
