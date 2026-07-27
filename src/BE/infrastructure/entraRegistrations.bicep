extension microsoftGraphV1

param environment string
// Retained for compatibility with main.bicep. Application ownership is not
// mutated because Graph relationship updates are non-atomic.
param globalAdminId string

// Deployment scripts only rerun when their resource definition changes. A
// timestamp default forces reconciliation on every parent deployment.
param forceUpdateTag string = utcNow()

var normalizedEnvironment = toLower(environment)
var oauthServerUniqueName = 'workslip-oauth-server-${normalizedEnvironment}'
var workslipClientUniqueName = 'workslip-client-${normalizedEnvironment}'
var apiScopeId = 'c2e2bf46-f94d-4c3e-86d7-ca425e4c6e2a'
var graphApplicationReadWriteAllRoleId = '1bfefb4e-e0b5-418b-a88f-73c46d2cc8e9'
var provisionerIdentityName = take('id-workslip-entra-provisioner-${normalizedEnvironment}', 128)

resource provisionerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: provisionerIdentityName
  location: resourceGroup().location
  tags: {
    environment: environment
    purpose: 'Entra application provisioning'
  }
}

resource microsoftGraphServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: '00000003-0000-0000-c000-000000000000'
}

// The identity is attached only to the ephemeral deployment-script container.
// Application.ReadWrite.All is required to adopt and update registrations that
// predate this provisioner as well as to create their service principals.
resource graphApplicationReadWriteAllForProvisioner 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  appRoleId: graphApplicationReadWriteAllRoleId
  principalId: provisionerIdentity.properties.principalId
  resourceId: microsoftGraphServicePrincipal.id
}

resource provisionEntraApplications 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'provision-entra-apps-${normalizedEnvironment}'
  location: resourceGroup().location
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${provisionerIdentity.id}': {}
    }
  }
  properties: {
    azCliVersion: '2.61.0'
    cleanupPreference: 'OnSuccess'
    retentionInterval: 'P1D'
    timeout: 'PT30M'
    forceUpdateTag: forceUpdateTag
    environmentVariables: [
      {
        name: 'ENVIRONMENT_NAME'
        value: environment
      }
      {
        name: 'OAUTH_UNIQUE_NAME'
        value: oauthServerUniqueName
      }
      {
        name: 'CLIENT_UNIQUE_NAME'
        value: workslipClientUniqueName
      }
      {
        name: 'API_SCOPE_ID'
        value: apiScopeId
      }
      {
        name: 'SUPERADMIN_ROLE_ID'
        value: guid('Superadmin', environment)
      }
      {
        name: 'ADMIN_ROLE_ID'
        value: guid('Admin', environment)
      }
      {
        name: 'USER_ROLE_ID'
        value: guid('User', environment)
      }
      {
        name: 'AUDITOR_ROLE_ID'
        value: guid('Auditor', environment)
      }
    ]
    scriptContent: '''
set -euo pipefail

GRAPH_ROOT='https://graph.microsoft.com/v1.0'
MAX_ATTEMPTS=36

retry_graph_patch() {
  local uri="$1"
  local body_file="$2"
  local description="$3"

  for attempt in $(seq 1 "$MAX_ATTEMPTS"); do
    if az rest \
      --method PATCH \
      --uri "$uri" \
      --headers 'Content-Type=application/json' 'Prefer=create-if-missing' \
      --body "@$body_file" \
      --output none 2>/tmp/graph-error; then
      return 0
    fi

    echo "Attempt $attempt/$MAX_ATTEMPTS failed while $description." >&2
    cat /tmp/graph-error >&2 || true
    sleep 10
  done

  echo "Microsoft Graph did not complete: $description." >&2
  return 1
}

retry_graph_get() {
  local uri="$1"
  local description="$2"

  for attempt in $(seq 1 "$MAX_ATTEMPTS"); do
    local result
    if result=$(az rest \
      --method GET \
      --uri "$uri" \
      --output json 2>/tmp/graph-error); then
      if [ -n "$(jq -r '.id // empty' <<< "$result")" ] && \
         [ -n "$(jq -r '.appId // empty' <<< "$result")" ]; then
        printf '%s' "$result"
        return 0
      fi
    fi

    echo "Attempt $attempt/$MAX_ATTEMPTS waiting for $description." >&2
    cat /tmp/graph-error >&2 || true
    sleep 10
  done

  echo "Microsoft Graph did not expose $description." >&2
  return 1
}

jq -n \
  --arg displayName "Oauth server $ENVIRONMENT_NAME" \
  --arg apiScopeId "$API_SCOPE_ID" \
  --arg superadminRoleId "$SUPERADMIN_ROLE_ID" \
  --arg adminRoleId "$ADMIN_ROLE_ID" \
  --arg userRoleId "$USER_ROLE_ID" \
  --arg auditorRoleId "$AUDITOR_ROLE_ID" \
  '{
    displayName: $displayName,
    signInAudience: "AzureADandPersonalMicrosoftAccount",
    publicClient: {
      redirectUris: ["nativepasskeydemo://auth"]
    },
    appRoles: [
      {
        id: $superadminRoleId,
        allowedMemberTypes: ["User"],
        displayName: "Superadmin",
        description: "Super administrator",
        value: "Superadmin",
        isEnabled: true
      },
      {
        id: $adminRoleId,
        allowedMemberTypes: ["User"],
        displayName: "Admin",
        description: "Administrator",
        value: "Admin",
        isEnabled: true
      },
      {
        id: $userRoleId,
        allowedMemberTypes: ["User"],
        displayName: "User",
        description: "Standard user",
        value: "User",
        isEnabled: true
      },
      {
        id: $auditorRoleId,
        allowedMemberTypes: ["User"],
        displayName: "Auditor",
        description: "External temporary user",
        value: "Auditor",
        isEnabled: true
      }
    ],
    api: {
      requestedAccessTokenVersion: 2,
      oauth2PermissionScopes: [
        {
          id: $apiScopeId,
          adminConsentDescription: "Access Workslip API as the signed-in user",
          adminConsentDisplayName: "Access Workslip API",
          userConsentDescription: "Access Workslip API on your behalf",
          userConsentDisplayName: "Access Workslip API",
          value: "access_as_user",
          type: "User",
          isEnabled: true
        }
      ]
    }
  }' > /tmp/oauth-application.json

retry_graph_patch \
  "$GRAPH_ROOT/applications(uniqueName='$OAUTH_UNIQUE_NAME')" \
  /tmp/oauth-application.json \
  "upserting OAuth application $OAUTH_UNIQUE_NAME"

oauth_application=$(retry_graph_get \
  "$GRAPH_ROOT/applications(uniqueName='$OAUTH_UNIQUE_NAME')?\$select=id,appId,displayName" \
  "OAuth application $OAUTH_UNIQUE_NAME")
oauth_object_id=$(jq -r '.id' <<< "$oauth_application")
oauth_app_id=$(jq -r '.appId' <<< "$oauth_application")

jq -n \
  --arg oauthAppId "$oauth_app_id" \
  --arg apiScopeId "$API_SCOPE_ID" \
  '{
    displayName: "Workslip App",
    signInAudience: "AzureADandPersonalMicrosoftAccount",
    api: {
      requestedAccessTokenVersion: 2
    },
    spa: {
      redirectUris: [
        "http://localhost:5270/login",
        "http://localhost:5270/invite/callback",
        "https://app.mrsoftware.dk/login",
        "https://app.mrsoftware.dk/invite/callback",
        "https://workslip-v2-0.vercel.app/login",
        "https://workslip-v2-0.vercel.app/invite/callback"
      ]
    },
    web: {
      redirectUris: ["https://oauth.pstmn.io/v1/callback"],
      implicitGrantSettings: {
        enableAccessTokenIssuance: false,
        enableIdTokenIssuance: true
      }
    },
    requiredResourceAccess: [
      {
        resourceAppId: "00000003-0000-0000-c000-000000000000",
        resourceAccess: [
          {
            id: "e1fe6dd8-ba31-4d61-89e7-88639da4683d",
            type: "Scope"
          }
        ]
      },
      {
        resourceAppId: $oauthAppId,
        resourceAccess: [
          {
            id: $apiScopeId,
            type: "Scope"
          }
        ]
      }
    ]
  }' > /tmp/client-application.json

retry_graph_patch \
  "$GRAPH_ROOT/applications(uniqueName='$CLIENT_UNIQUE_NAME')" \
  /tmp/client-application.json \
  "upserting client application $CLIENT_UNIQUE_NAME"

client_application=$(retry_graph_get \
  "$GRAPH_ROOT/applications(uniqueName='$CLIENT_UNIQUE_NAME')?\$select=id,appId,displayName" \
  "client application $CLIENT_UNIQUE_NAME")
client_object_id=$(jq -r '.id' <<< "$client_application")
client_app_id=$(jq -r '.appId' <<< "$client_application")

jq -n \
  --arg displayName "Oauth server $ENVIRONMENT_NAME" \
  '{
    displayName: $displayName,
    tags: ["WindowsAzureActiveDirectoryIntegratedApp"]
  }' > /tmp/oauth-service-principal.json

retry_graph_patch \
  "$GRAPH_ROOT/servicePrincipals(appId='$oauth_app_id')" \
  /tmp/oauth-service-principal.json \
  "upserting OAuth service principal $oauth_app_id"

jq -n \
  '{
    displayName: "Workslip App",
    tags: ["WindowsAzureActiveDirectoryIntegratedApp"]
  }' > /tmp/client-service-principal.json

retry_graph_patch \
  "$GRAPH_ROOT/servicePrincipals(appId='$client_app_id')" \
  /tmp/client-service-principal.json \
  "upserting client service principal $client_app_id"

retry_graph_get \
  "$GRAPH_ROOT/servicePrincipals(appId='$oauth_app_id')?\$select=id,appId,displayName" \
  "OAuth service principal $oauth_app_id" >/dev/null
retry_graph_get \
  "$GRAPH_ROOT/servicePrincipals(appId='$client_app_id')?\$select=id,appId,displayName" \
  "client service principal $client_app_id" >/dev/null

jq -n \
  --arg oauthClientId "$oauth_app_id" \
  --arg oauthAppObjectId "$oauth_object_id" \
  --arg clientAppId "$client_app_id" \
  --arg clientAppObjectId "$client_object_id" \
  '{
    oauthClientId: $oauthClientId,
    oauthAppObjectId: $oauthAppObjectId,
    clientAppId: $clientAppId,
    clientAppObjectId: $clientAppObjectId
  }' > "$AZ_SCRIPTS_OUTPUT_PATH"
'''
  }
  dependsOn: [
    graphApplicationReadWriteAllForProvisioner
  ]
}

// Existing callers use OAuthAppId as the API audience application/client ID.
// The object ID is retained in the deployment-script output for diagnostics and
// future callers, while the current deployment output remains backward compatible.
output OAuthClientId string = provisionEntraApplications.properties.outputs.oauthClientId
output OAuthAppId string = provisionEntraApplications.properties.outputs.oauthClientId
output ClientAppId string = provisionEntraApplications.properties.outputs.clientAppId
output ClientAppObjectId string = provisionEntraApplications.properties.outputs.clientAppObjectId
