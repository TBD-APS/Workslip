param companyName string
param environment string
param location string
param appInsightsResourceId string
param webApiResourceId string
param healthEndpointUrl string
param tags object

var monitoringConfig = loadJsonContent('./monitoring.config.json')
var alertEmailAddresses = array(monitoringConfig.alertEmailAddresses)
var normalizedEnvironment = toLower(environment)
var actionGroupName = take('ag-${companyName}-${normalizedEnvironment}-api', 260)
var availabilityTestName = take('availability-${companyName}-${normalizedEnvironment}-api-health', 260)

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

resource apiHealthAvailabilityTest 'Microsoft.Insights/webTests@2022-06-15' = {
  name: availabilityTestName
  location: location
  kind: 'standard'
  tags: union(tags, {
    'hidden-link:${appInsightsResourceId}': 'Resource'
  })
  properties: {
    Description: 'Checks the public Workslip API health endpoint from five Azure regions.'
    Enabled: true
    Frequency: 300
    Kind: 'standard'
    Locations: [
      {
        Id: 'emea-nl-ams-azr'
      }
      {
        Id: 'emea-gb-db3-azr'
      }
      {
        Id: 'emea-fr-pra-edge'
      }
      {
        Id: 'emea-ru-msa-edge'
      }
      {
        Id: 'us-ca-sjc-azr'
      }
    ]
    Name: availabilityTestName
    Request: {
      FollowRedirects: false
      Headers: []
      HttpVerb: 'GET'
      ParseDependentRequests: false
      RequestUrl: healthEndpointUrl
    }
    RetryEnabled: true
    SyntheticMonitorId: availabilityTestName
    Timeout: 30
    ValidationRules: {
      ExpectedHttpStatusCode: 200
      IgnoreHttpStatusCode: false
      SSLCertRemainingLifetimeCheck: 14
      SSLCheck: true
    }
  }
}

resource apiAvailabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: take('alert-${companyName}-${normalizedEnvironment}-api-down', 260)
  location: 'global'
  tags: tags
  properties: {
    actions: [
      {
        actionGroupId: apiAlertActionGroup.id
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      componentId: appInsightsResourceId
      failedLocationCount: 3
      webTestId: apiHealthAvailabilityTest.id
    }
    description: 'Critical: the API health endpoint failed from at least three of five test locations.'
    enabled: true
    evaluationFrequency: 'PT1M'
    scopes: [
      apiHealthAvailabilityTest.id
      appInsightsResourceId
    ]
    severity: 0
    windowSize: 'PT5M'
  }
}

resource apiHttp5xxAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: take('alert-${companyName}-${normalizedEnvironment}-api-http5xx', 260)
  location: 'global'
  tags: tags
  properties: {
    actions: [
      {
        actionGroupId: apiAlertActionGroup.id
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          dimensions: []
          metricName: 'Http5xx'
          metricNamespace: 'Microsoft.Web/sites'
          name: 'Http5xxErrors'
          operator: 'GreaterThan'
          skipMetricValidation: false
          threshold: 0
          timeAggregation: 'Total'
        }
      ]
    }
    description: 'High: one or more HTTP 5xx responses were emitted by the API within five minutes.'
    enabled: true
    evaluationFrequency: 'PT1M'
    scopes: [
      webApiResourceId
    ]
    severity: 1
    targetResourceRegion: location
    targetResourceType: 'Microsoft.Web/sites'
    windowSize: 'PT5M'
  }
}

resource apiSlowResponseAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: take('alert-${companyName}-${normalizedEnvironment}-api-slow', 260)
  location: 'global'
  tags: tags
  properties: {
    actions: [
      {
        actionGroupId: apiAlertActionGroup.id
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          dimensions: []
          metricName: 'HttpResponseTime'
          metricNamespace: 'Microsoft.Web/sites'
          name: 'SlowApiResponses'
          operator: 'GreaterThan'
          skipMetricValidation: false
          threshold: 5
          timeAggregation: 'Average'
        }
      ]
    }
    description: 'Warning: average API response time exceeded five seconds during a five-minute window.'
    enabled: true
    evaluationFrequency: 'PT1M'
    scopes: [
      webApiResourceId
    ]
    severity: 2
    targetResourceRegion: location
    targetResourceType: 'Microsoft.Web/sites'
    windowSize: 'PT5M'
  }
}

output ACTION_GROUP_ID string = apiAlertActionGroup.id
output AVAILABILITY_TEST_ID string = apiHealthAvailabilityTest.id
