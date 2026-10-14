targetScope = 'resourceGroup'

param location string = 'centralus'
@secure()
param postgresPassword string
@secure()
param redisPassword string
@secure()
param webClientSecret string
param budgetContactEmail string
param operatorPublicIp string = ''
@allowed([ 'p1' ])
param deploymentPhase string = 'p1'

var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var resourceTags = {
  app: 'schwab-url-shortener'
  env: 'prod'
  owner: 'social@mediaraq.com'
  costCenter: 'assessment'
  managedBy: 'bicep'
}

module network './modules/network.bicep' = {
  name: 'network'
  params: { location: location, resourceTags: resourceTags }
}
module observability './modules/observability.bicep' = {
  name: 'observability'
  params: { location: location, resourceTags: resourceTags }
}
module foundation './modules/foundation.bicep' = {
  name: 'foundation'
  params: {
    location: location
    resourceTags: resourceTags
    suffix: suffix
    acaSubnetId: network.outputs.acaSubnetId
    operatorPublicIp: operatorPublicIp
    logWorkspaceId: observability.outputs.logWorkspaceId
  }
}
module data './modules/data.bicep' = {
  name: 'data'
  params: {
    location: location
    resourceTags: resourceTags
    suffix: suffix
    acaSubnetId: network.outputs.acaSubnetId
    postgresSubnetId: network.outputs.postgresSubnetId
    postgresDnsZoneId: network.outputs.postgresDnsZoneId
    logWorkspaceId: observability.outputs.logWorkspaceId
    vaultName: foundation.outputs.vaultName
    apiPrincipalId: foundation.outputs.apiIdentity.principalId
    webPrincipalId: foundation.outputs.webIdentity.principalId
    analyticsPrincipalId: foundation.outputs.analyticsIdentity.principalId
    migratePrincipalId: foundation.outputs.migrateIdentity.principalId
    redisPrincipalId: foundation.outputs.redisIdentity.principalId
    operatorPublicIp: operatorPublicIp
    postgresPassword: postgresPassword
    redisPassword: redisPassword
    webClientSecret: webClientSecret
  }
}
module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    contactEmail: budgetContactEmail
    resourceTags: resourceTags
    postgresId: data.outputs.postgresId
    eventhubsId: data.outputs.eventhubsId
    logWorkspaceId: observability.outputs.logWorkspaceId
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'budget-shortener'
  properties: {
    category: 'Cost'
    amount: 100
    timeGrain: 'Monthly'
    timePeriod: { startDate: '2026-10-01T00:00:00Z' }
    notifications: {
      actual50: { enabled: true, operator: 'GreaterThan', threshold: 50, contactEmails: [ budgetContactEmail ] }
      actual80: { enabled: true, operator: 'GreaterThan', threshold: 80, contactEmails: [ budgetContactEmail ] }
      actual100: { enabled: true, operator: 'GreaterThan', threshold: 100, contactEmails: [ budgetContactEmail ] }
    }
  }
}

output AZURE_CONTAINER_REGISTRY_NAME string = foundation.outputs.acrName
output POSTGRES_HOST string = data.outputs.postgresHost
output EVENTHUBS_HOST string = data.outputs.eventhubsHost
output REDIS_STORAGE_ACCOUNT string = data.outputs.redisStorageName
output MONITOR_ACTION_GROUP_ID string = monitoring.outputs.actionGroupId
output DEPLOYMENT_PHASE string = deploymentPhase
