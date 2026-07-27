param environment string

// deploy-safe.ps1 reconciles the applications and service principals through
// Microsoft Graph using the signed-in administrator, waits for concrete IDs,
// and writes this compile-time handoff file before invoking main.bicep.
var provisionedValues = loadJsonContent('./entra-provisioned.json')
var validatedValues = provisionedValues.environment == toLower(environment)
  ? provisionedValues
  : fail('Entra values were not provisioned for this environment. Run deploy-safe.ps1 instead of deploying main.bicep directly.')

output OAuthClientId string = validatedValues.oauthClientId
// Existing callers use OAuthAppId as the API audience application/client ID.
output OAuthAppId string = validatedValues.oauthClientId
output ClientAppId string = validatedValues.clientAppId
output ClientAppObjectId string = validatedValues.clientAppObjectId
