@description('Company prefix used across resource names, for example mrsoftware.')
param companyName string

@description('Environment this observability configuration belongs to, for example prod.')
param environment string

@description('Log Analytics workspace that receives the diagnostic streams.')
param logAnalyticsWorkspaceId string

@description('Action group that receives the alerts. Reuses the API alert group so every operational signal lands in one place.')
param actionGroupId string

@description('Name of the Azure SQL logical server.')
param sqlServerName string

@description('Name of the Azure SQL database.')
param sqlDatabaseName string

@description('Name of the storage account holding job images and document attachments.')
param storageAccountName string

@description('Name of the Azure Communication Services resource used for invitation and one-time-code email.')
param communicationServiceName string

param tags object

var normalizedEnvironment = toLower(environment)

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' existing = {
  name: '${sqlServerName}/${sqlDatabaseName}'
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName

  resource blobService 'blobServices' existing = {
    name: 'default'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' existing = {
  name: communicationServiceName
}

/*
  Diagnostic streams.

  The workspace is capped at 1 GB/day. That cap makes log selection more important,
  not less: a high-volume stream does not just cost money, it crowds out the signals
  that matter once the cap is hit. Reads against the blob container are by far the
  noisiest thing this system produces — every job image view is one — and they carry
  almost no diagnostic value, so they are deliberately excluded while writes and
  deletes are kept.
*/

resource sqlDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-to-log-analytics'
  scope: sqlDatabase
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'Basic'
        enabled: true
      }
    ]
  }
}

resource blobDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-to-log-analytics'
  scope: storageAccount::blobService
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'StorageWrite'
        enabled: true
      }
      {
        category: 'StorageDelete'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'Transaction'
        enabled: true
      }
    ]
  }
}

/*
  Invitations and one-time codes go out through this resource. When email stops being
  delivered, onboarding fails silently: nothing errors, the mail simply never arrives.
  Routing the operational logs here is the prerequisite for both alerting on that and
  for reporting on delivery rates later. Volume is low.
*/
resource communicationDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-to-log-analytics'
  scope: communicationService
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

/*
  Alerts. The database is Basic tier — five DTU and a two gigabyte ceiling — so both
  limits are reachable under ordinary growth rather than only under abuse.
*/

resource sqlDtuAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: take('alert-${companyName}-${normalizedEnvironment}-sql-dtu', 260)
  location: 'global'
  tags: tags
  properties: {
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          dimensions: []
          metricName: 'dtu_consumption_percent'
          metricNamespace: 'Microsoft.Sql/servers/databases'
          name: 'SqlDtuSaturation'
          operator: 'GreaterThan'
          skipMetricValidation: false
          threshold: 80
          timeAggregation: 'Average'
        }
      ]
    }
    // Fifteen minutes rather than five: on five DTU a single report render spikes the
    // gauge, and a shorter window would alert on normal use.
    description: 'Warning: average SQL DTU consumption exceeded 80 percent over fifteen minutes. The database is Basic tier, so this is close to the ceiling.'
    enabled: true
    evaluationFrequency: 'PT5M'
    scopes: [
      sqlDatabase.id
    ]
    severity: 2
    windowSize: 'PT15M'
  }
}

resource sqlStorageAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: take('alert-${companyName}-${normalizedEnvironment}-sql-storage', 260)
  location: 'global'
  tags: tags
  properties: {
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          dimensions: []
          metricName: 'storage_percent'
          metricNamespace: 'Microsoft.Sql/servers/databases'
          name: 'SqlStorageSaturation'
          operator: 'GreaterThan'
          skipMetricValidation: false
          threshold: 80
          timeAggregation: 'Average'
        }
      ]
    }
    // Database size moves slowly, so an hour-long window costs nothing in warning time
    // and removes the noise a short window would produce.
    description: 'Warning: SQL database storage exceeded 80 percent of the Basic tier two gigabyte limit. Writes fail once it is full.'
    enabled: true
    evaluationFrequency: 'PT15M'
    scopes: [
      sqlDatabase.id
    ]
    severity: 2
    windowSize: 'PT1H'
  }
}

/*
  Job images and document attachments live in blob storage. When the account degrades,
  uploads and downloads fail while the API itself stays up and healthy, so none of the
  existing API alerts notice.
*/
resource blobAvailabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: take('alert-${companyName}-${normalizedEnvironment}-storage-availability', 260)
  location: 'global'
  tags: tags
  properties: {
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          dimensions: []
          metricName: 'Availability'
          metricNamespace: 'Microsoft.Storage/storageAccounts'
          name: 'StorageAvailability'
          operator: 'LessThan'
          skipMetricValidation: false
          threshold: 99
          timeAggregation: 'Average'
        }
      ]
    }
    description: 'Error: storage account availability dropped below 99 percent. Image and document upload or download is failing even if the API is healthy.'
    enabled: true
    evaluationFrequency: 'PT5M'
    scopes: [
      storageAccount.id
    ]
    severity: 1
    windowSize: 'PT5M'
  }
}

output SQL_DIAGNOSTICS_ID string = sqlDiagnostics.id
output BLOB_DIAGNOSTICS_ID string = blobDiagnostics.id
output COMMUNICATION_DIAGNOSTICS_ID string = communicationDiagnostics.id
