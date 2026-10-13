param location string

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-shortener-stg'
  location: location
  properties: { retentionInDays: 30, sku: { name: 'PerGB2018' } }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-shortener-stg'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

output logWorkspaceId string = logs.id
output logWorkspaceCustomerId string = logs.properties.customerId
@secure()
output logWorkspaceSharedKey string = logs.listKeys().primarySharedKey
output appInsightsConnectionString string = insights.properties.ConnectionString
