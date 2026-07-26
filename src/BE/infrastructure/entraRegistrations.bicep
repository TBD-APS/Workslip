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
      id: guid('Superadmin', environment)
      allowedMemberTypes: [
        'User'
      ]
      displayName: 'Superadmin'
      description: 'Super administrator'
      value: 'Superadmin'
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
    {
      id: guid('Auditor', environment)
      allowedMemberTypes: [
        'User'
      ]
      displayName: 'Auditor'
      description: 'External temporary user'
      value: 'Auditor'
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
  displayName: 'Workslip App'
  signInAudience: 'AzureADandPersonalMicrosoftAccount'
  api:{
    requestedAccessTokenVersion: 2
  }
  spa: {
    redirectUris: [
      'http://localhost:5270/login'
      'http://localhost:5270/invite/callback'
      'https://app.workslip.dk/login'
      'https://app.workslip.dk/invite/callback'
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
          id: 'e1fe6dd8-ba31-4d61-89e7-88639da4683d' // User.Read
          type: 'Scope'
        }
      ]
    }
    {
      resourceAppId: OAuthServerApp.appId // Workslip API
      resourceAccess: [
        {
          id: apiScopeId
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
