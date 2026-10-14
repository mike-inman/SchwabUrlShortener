param location string
param resourceTags object
param suffix string
param acaSubnetId string
param operatorPublicIp string
param logWorkspaceId string

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: 'acrshortener${suffix}'
  location: location
  tags: resourceTags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false, anonymousPullEnabled: false }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-shortener-${suffix}'
  location: location
  tags: resourceTags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Deny'
      virtualNetworkRules: [ { id: acaSubnetId } ]
      ipRules: empty(operatorPublicIp) ? [] : [ { value: operatorPublicIp } ]
    }
  }
}

resource apiId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = { name: 'id-shortener-api', location: location, tags: resourceTags }
resource webId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = { name: 'id-shortener-web', location: location, tags: resourceTags }
resource analyticsId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = { name: 'id-shortener-analytics', location: location, tags: resourceTags }
resource migrateId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = { name: 'id-shortener-migrate', location: location, tags: resourceTags }
// Redis needs its own identity to read only the raw password secret at P2.
resource redisId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = { name: 'id-shortener-redis', location: location, tags: resourceTags }

var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
resource pullApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, apiId.id, acrPullRole)
  scope: acr
  properties: { principalId: apiId.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: acrPullRole }
}
resource pullWeb 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, webId.id, acrPullRole)
  scope: acr
  properties: { principalId: webId.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: acrPullRole }
}
resource pullAnalytics 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, analyticsId.id, acrPullRole)
  scope: acr
  properties: { principalId: analyticsId.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: acrPullRole }
}
resource pullMigrate 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, migrateId.id, acrPullRole)
  scope: acr
  properties: { principalId: migrateId.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: acrPullRole }
}

resource vaultDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: vault
  properties: {
    workspaceId: logWorkspaceId
    logs: [ { categoryGroup: 'allLogs', enabled: true } ]
    metrics: [ { category: 'AllMetrics', enabled: true } ]
  }
}

output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
output apiIdentity object = { id: apiId.id, clientId: apiId.properties.clientId, principalId: apiId.properties.principalId }
output webIdentity object = { id: webId.id, clientId: webId.properties.clientId, principalId: webId.properties.principalId }
output analyticsIdentity object = { id: analyticsId.id, clientId: analyticsId.properties.clientId, principalId: analyticsId.properties.principalId }
output migrateIdentity object = { id: migrateId.id, clientId: migrateId.properties.clientId, principalId: migrateId.properties.principalId }
output redisIdentity object = { id: redisId.id, clientId: redisId.properties.clientId, principalId: redisId.properties.principalId }
