targetScope = 'resourceGroup'

@description('Use centralus unless a required SKU is unavailable; eastus2 is the contract fallback.')
param location string = 'centralus'
@secure()
param postgresPassword string = ''
param apiClientId string = ''
param budgetContactEmail string = ''
param customDomain string = ''
@allowed([ 'Balanced_B0', 'Balanced_B1' ])
param redisSku string = 'Balanced_B0'
@allowed([ 'foundation', 'data', 'apps', 'edge' ])
param deploymentPhase string = 'foundation'
param apiImage string = ''
param webImage string = ''
param analyticsImage string = ''
param apiMinReplicas int = 1
param apiMaxReplicas int = 1
param webMinReplicas int = 1
param webMaxReplicas int = 1
param analyticsMinReplicas int = 0
param analyticsMaxReplicas int = 0
param enableScaleOutSettings bool = false

var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var publicHost = empty(customDomain) ? 'afd-shortener-stg-${suffix}.azurefd.net' : customDomain
var deployData = deploymentPhase == 'data' || deploymentPhase == 'apps' || deploymentPhase == 'edge'
var deployApps = deploymentPhase == 'apps' || deploymentPhase == 'edge'
var deployEdge = deploymentPhase == 'edge'

module network './modules/network.bicep' = { name: 'network', params: { location: location } }
module observability './modules/observability.bicep' = { name: 'observability', params: { location: location } }
module foundation './modules/foundation.bicep' = {
  name: 'foundation'
  params: {
    location: location
    suffix: suffix
    vnetId: network.outputs.vnetId
    privateSubnetId: network.outputs.privateSubnetId
    logWorkspaceId: observability.outputs.logWorkspaceId
  }
}
module data './modules/data.bicep' = if (deployData) {
  name: 'data'
  params: {
    location: location
    suffix: suffix
    vnetId: network.outputs.vnetId
    privateSubnetId: network.outputs.privateSubnetId
    logWorkspaceId: observability.outputs.logWorkspaceId
    vaultName: foundation.outputs.vaultName
    apiPrincipalId: foundation.outputs.apiIdentity.principalId
    webPrincipalId: foundation.outputs.webIdentity.principalId
    analyticsPrincipalId: foundation.outputs.analyticsIdentity.principalId
    postgresPassword: postgresPassword
    redisSku: redisSku
  }
}
module apps './modules/apps.bicep' = if (deployApps) {
  name: 'apps'
  params: {
    location: location
    acaSubnetId: network.outputs.acaSubnetId
    logWorkspaceCustomerId: observability.outputs.logWorkspaceCustomerId
    logWorkspaceSharedKey: observability.outputs.logWorkspaceSharedKey
    appInsightsConnectionString: observability.outputs.appInsightsConnectionString
    acrLoginServer: foundation.outputs.acrLoginServer
    apiIdentity: foundation.outputs.apiIdentity
    webIdentity: foundation.outputs.webIdentity
    analyticsIdentity: foundation.outputs.analyticsIdentity
    apiImage: apiImage
    webImage: webImage
    analyticsImage: analyticsImage
    apiClientId: apiClientId
    publicHost: publicHost
    pgAppSecretUrl: data!.outputs.pgAppSecretUrl
    pgMigrateSecretUrl: data!.outputs.pgMigrateSecretUrl
    redisSecretUrl: data!.outputs.redisSecretUrl
    eventhubsSecretUrl: data!.outputs.eventhubsSecretUrl
    eventhubsHost: data!.outputs.eventhubsHost
    storageBlobBaseUrl: data!.outputs.storageBlobBaseUrl
    apiKeyId: data!.outputs.apiKeyId
    webKeyId: data!.outputs.webKeyId
    apiMinReplicas: apiMinReplicas
    apiMaxReplicas: apiMaxReplicas
    webMinReplicas: webMinReplicas
    webMaxReplicas: webMaxReplicas
    analyticsMinReplicas: analyticsMinReplicas
    analyticsMaxReplicas: analyticsMaxReplicas
    enableScaleOutSettings: enableScaleOutSettings
  }
}
module edge './modules/edge.bicep' = if (deployApps && deployEdge) {
  name: 'edge'
  params: {
    suffix: suffix
    apiFqdn: apps!.outputs.apiFqdn
    webFqdn: apps!.outputs.webFqdn
    logWorkspaceId: observability.outputs.logWorkspaceId
    customDomain: customDomain
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = if (!empty(budgetContactEmail)) {
  name: 'budget-shortener-stg'
  properties: {
    category: 'Cost'
    amount: 600
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
output SERVICE_API_NAME string = deployApps ? 'ca-shortener-api-stg' : ''
output SERVICE_WEB_NAME string = deployApps ? 'ca-shortener-web-stg' : ''
output SERVICE_ANALYTICS_NAME string = deployApps ? 'ca-shortener-analytics-stg' : ''
output PUBLIC_HOST string = publicHost
output FRONT_DOOR_HOST string = deployApps && deployEdge ? edge!.outputs.endpointHost : ''
