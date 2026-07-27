param environment string
// Retained for compatibility with main.bicep. Application ownership is managed
// outside Bicep because Microsoft Graph relationship updates are non-atomic.

// deploy-entra.ps1 writes persistent environment-specific local state.
// deploy-infrastructure.ps1 validates that state and writes this temporary
// compile-time handoff before invoking main.bicep.
var provisionedValues = loadJsonContent('./entra-provisioned.json')
var validatedValues = provisionedValues.environment == toLower(environment)
  ? provisionedValues
  : fail('Entra values were not loaded for this environment. Run deploy-entra.ps1, then deploy-infrastructure.ps1.')

output OAuthClientId string = validatedValues.oauthClientId
// Existing callers use OAuthAppId as the API audience application/client ID.
output OAuthAppId string = validatedValues.oauthClientId
output ClientAppId string = validatedValues.clientAppId
output ClientAppObjectId string = validatedValues.clientAppObjectId
