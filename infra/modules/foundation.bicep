param location string
param suffix string
param vnetId string
param privateSubnetId string
param logWorkspaceId string

var acrName = 'acrshortenerstg${suffix}'
var vaultName = 'kv-shortener-stg-${suffix}'

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: { name: 'Standard' }
  properties: { adminUserEnabled: false }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Disabled'
  }
}

resource apiId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-shortener-api-stg'
  location: location
}
resource webId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-shortener-web-stg'
  location: location
}
resource analyticsId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-shortener-analytics-stg'
  location: location
}

var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var identityPrincipals = [ apiId.properties.principalId, webId.properties.principalId, analyticsId.properties.principalId ]
var identityIds = [ apiId.id, webId.id, analyticsId.id ]

resource pull 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for i in range(0, 3): {
  name: guid(acr.id, identityIds[i], acrPullRole)
  scope: acr
  properties: {
    principalId: identityPrincipals[i]
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRole
  }
}]

resource vaultZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
}
resource vaultLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: vaultZone
  name: 'vnet-shortener-stg'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnetId }
  }
}
resource vaultEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: 'pe-shortener-vault-stg'
  location: location
  properties: {
    subnet: { id: privateSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'vault'
        properties: {
          privateLinkServiceId: vault.id
          groupIds: [ 'vault' ]
        }
      }
    ]
  }
}
resource vaultDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: vaultEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'vault', properties: { privateDnsZoneId: vaultZone.id } }
    ]
  }
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
output vaultId string = vault.id
output vaultUri string = vault.properties.vaultUri
output apiIdentity object = { id: apiId.id, clientId: apiId.properties.clientId, principalId: apiId.properties.principalId }
output webIdentity object = { id: webId.id, clientId: webId.properties.clientId, principalId: webId.properties.principalId }
output analyticsIdentity object = { id: analyticsId.id, clientId: analyticsId.properties.clientId, principalId: analyticsId.properties.principalId }
