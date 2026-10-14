param contactEmail string
param postgresId string
param eventhubsId string

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-shortener-ops'
  location: 'global'
  properties: {
    groupShortName: 'Shortener'
    enabled: true
    emailReceivers: [ { name: 'budget-contact', emailAddress: contactEmail, useCommonAlertSchema: true } ]
  }
}

resource postgresCpu 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-shortener-postgres-cpu'
  location: 'global'
  properties: {
    description: 'PostgreSQL CPU above 80 percent for 10 minutes.'
    severity: 2
    enabled: true
    scopes: [ postgresId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT10M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [ { name: 'HighCpu', criterionType: 'StaticThresholdCriterion', metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers', metricName: 'cpu_percent', operator: 'GreaterThan', threshold: 80, timeAggregation: 'Average' } ]
    }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}

// B1ms has 35 user connections. Alert at 28 (80 percent of usable slots),
// before the server's reserved slots make a 40-connection alert unreachable.
resource postgresConnections 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-shortener-postgres-connections'
  location: 'global'
  properties: {
    description: 'PostgreSQL active connections above 28 for 10 minutes.'
    severity: 2
    enabled: true
    scopes: [ postgresId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT10M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [ { name: 'HighConnections', criterionType: 'StaticThresholdCriterion', metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers', metricName: 'active_connections', operator: 'GreaterThan', threshold: 28, timeAggregation: 'Maximum' } ]
    }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}

// Event Hubs Standard uses a fixed 1-TU ingress limit. These independent
// rules alert when either 10-minute average reaches 70 percent of its limit.
resource eventhubsBytes 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-shortener-eventhubs-ingress-bytes'
  location: 'global'
  properties: {
    description: 'Incoming bytes above 420,000,000 per 10 minutes (0.7 MB/s average).'
    severity: 2
    enabled: true
    scopes: [ eventhubsId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT10M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [ { name: 'HighIncomingBytes', criterionType: 'StaticThresholdCriterion', metricNamespace: 'Microsoft.EventHub/namespaces', metricName: 'IncomingBytes', operator: 'GreaterThan', threshold: 420000000, timeAggregation: 'Total' } ]
    }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}
resource eventhubsMessages 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-shortener-eventhubs-ingress-messages'
  location: 'global'
  properties: {
    description: 'Incoming messages above 420,000 per 10 minutes (700/s average).'
    severity: 2
    enabled: true
    scopes: [ eventhubsId ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT10M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [ { name: 'HighIncomingMessages', criterionType: 'StaticThresholdCriterion', metricNamespace: 'Microsoft.EventHub/namespaces', metricName: 'IncomingMessages', operator: 'GreaterThan', threshold: 420000, timeAggregation: 'Total' } ]
    }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}

output actionGroupId string = actionGroup.id
