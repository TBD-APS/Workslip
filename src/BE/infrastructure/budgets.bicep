@description('Company prefix used across resource names, for example mrsoftware.')
param companyName string

@description('Environment this budget belongs to, for example prod.')
param environment string

@description('Action group that receives the threshold notifications. Reuses the API alert group so cost warnings reach the same superadmin mailboxes as health alerts.')
param actionGroupId string

@description('Monthly cost ceiling, expressed in the billing currency of the subscription — NOT necessarily DKK. Verify the subscription currency before trusting the number. The default leaves headroom above the ~534/month lean production baseline so the budget alarms on a runaway rather than on normal operation.')
@minValue(1)
param monthlyAmount int = 800

/*
  This budget and the Log Analytics daily ingestion cap in main.bicep are coupled,
  and nothing enforces the link, so it is written down here.

  The headroom between the ~534 baseline and this 800 ceiling is roughly 266, and
  the ingestion cap decides how much of it telemetry can claim. At 2 GB/day that
  is about 60 GB/month billable, against about 30 under the 1 GB cap it replaced:
  enough to stop silently discarding signal, and near half the headroom rather
  than all of it.

  Telemetry is not the only claim on that headroom. The always-warm replica in
  aca/app.bicep runs continuously and draws on the same budget, so the two
  increases have to be read together rather than each against the full 266.

  Before moving either number, check actual ingestion against the cap and actual
  spend against this amount. A cap is a ceiling rather than a forecast, and these
  are only a problem in combination.
*/

@description('First day of the month the budget starts measuring from. Azure requires the first of a month for a monthly budget. Defaulted here rather than computed inside the template because utcNow() is only valid in a parameter default.')
param startDate string = utcNow('yyyy-MM-01')

var normalizedEnvironment = toLower(environment)

/*
  Cost alerting is deliberately separate from the health alerts in monitoring.bicep.
  Those watch whether the API is up; this watches whether it is affordable. They share
  only the action group, so a change to one cannot silently reshape the other.

  Actual thresholds tell you what already happened. The forecasted one is the useful
  one day to day: it fires when Azure projects the month to end over budget, which is
  early enough to act on. A fresh subscription has no history to forecast from, so
  expect the forecast notification to stay quiet for the first billing period.
*/
resource monthlyCostBudget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: take('budget-${companyName}-${normalizedEnvironment}-monthly', 63)
  properties: {
    category: 'Cost'
    amount: monthlyAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      Actual_50_Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Actual'
        contactGroups: [
          actionGroupId
        ]
        contactEmails: []
        contactRoles: []
      }
      Actual_80_Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactGroups: [
          actionGroupId
        ]
        contactEmails: []
        contactRoles: []
      }
      Actual_100_Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactGroups: [
          actionGroupId
        ]
        contactEmails: []
        contactRoles: []
      }
      Forecasted_100_Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactGroups: [
          actionGroupId
        ]
        contactEmails: []
        contactRoles: []
      }
    }
  }
}

output BUDGET_ID string = monthlyCostBudget.id
output BUDGET_AMOUNT int = monthlyAmount
