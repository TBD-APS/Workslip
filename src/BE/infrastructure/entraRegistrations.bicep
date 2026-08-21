/*
  Entra application identifiers for this environment.

  These are resolved outside Bicep — deploy-entra.ps1 owns the registrations,
  because Microsoft Graph relationship updates are non-atomic and do not model
  well as declarative resources. This module exists only to validate the values
  and re-export them under stable names.

  They arrive as parameters rather than through loadJsonContent so that the
  template describes *a* Workslip environment rather than *this* one. A
  compile-time file load pins the template to whatever happens to be on disk
  next to it, which meant the deployment script had to write a handoff file,
  deploy, then restore the original — a mutation of the working tree as a side
  effect of deploying.
*/

@description('Environment these registrations belong to, for example prod. Used only to keep the failure message specific.')
param environment string

@description('Application (client) ID of the OAuth server registration.')
param oauthClientId string

@description('Directory object ID of the OAuth server registration.')
param oauthAppObjectId string

@description('Application (client) ID of the browser client registration.')
param clientAppId string

@description('Directory object ID of the browser client registration.')
param clientAppObjectId string

var allPresent = !empty(oauthClientId) && !empty(oauthAppObjectId) && !empty(clientAppId) && !empty(clientAppObjectId)

var validated = allPresent
  ? true
  : fail('Entra application identifiers were not supplied for environment "${environment}". Run deploy-entra.ps1, then deploy-infrastructure.ps1.')

output OAuthClientId string = validated ? oauthClientId : ''
output OAuthAppObjectId string = validated ? oauthAppObjectId : ''
// Existing callers use OAuthAppId as the API audience application/client ID.
output OAuthAppId string = validated ? oauthClientId : ''
output ClientAppId string = validated ? clientAppId : ''
output ClientAppObjectId string = validated ? clientAppObjectId : ''
