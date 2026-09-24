param companyName string
param environment string
param tags object

@description('Mailboxes that receive operational alerts. Supplied by the deployment script from monitoring.config.json rather than loaded at compile time, so the template describes any environment rather than this one.')
@minLength(1)
param alertEmailAddressList array

var alertEmailAddresses = array(alertEmailAddressList)
var normalizedEnvironment = toLower(environment)
var actionGroupName = take('ag-${companyName}-${normalizedEnvironment}-api', 260)

resource apiAlertActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  location: 'global'
  tags: tags
  properties: {
    armRoleReceivers: []
    automationRunbookReceivers: []
    azureAppPushReceivers: []
    azureFunctionReceivers: []
    emailReceivers: [for (emailAddress, index) in alertEmailAddresses: {
      name: 'superadmin-${index + 1}'
      emailAddress: string(emailAddress)
      useCommonAlertSchema: true
    }]
    enabled: true
    eventHubReceivers: []
    groupShortName: take('api${normalizedEnvironment}', 12)
    itsmReceivers: []
    logicAppReceivers: []
    smsReceivers: []
    voiceReceivers: []
    webhookReceivers: []
  }
}

output ACTION_GROUP_ID string = apiAlertActionGroup.id