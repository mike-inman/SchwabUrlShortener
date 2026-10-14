param location string
param resourceTags object

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-shortener'
  location: location
  tags: resourceTags
  properties: {
    retentionInDays: 30
    sku: { name: 'PerGB2018' }
    workspaceCapping: { dailyQuotaGb: 1 }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-shortener'
  location: location
  tags: resourceTags
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
