// Complete only P1 resources blocked by the first Key Vault key deployment.
// Do not redeploy existing data resources or secrets with placeholder inputs.
targetScope = 'resourceGroup'

param location string = 'centralus'
param contactEmail string
param apiPrincipalId string
param webPrincipalId string

var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var resourceTags = {
  app: 'schwab-url-shortener'
  env: 'prod'
  owner: 'social@mediaraq.com'
  costCenter: 'assessment'
  managedBy: 'bicep'
}
var cryptoUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = { name: 'kv-shortener-${suffix}' }

resource apiKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection-api'
  tags: resourceTags
  properties: { kty: 'RSA', keySize: 2048, keyOps: [ 'encrypt', 'decrypt', 'wrapKey', 'unwrapKey' ] }
}
resource webKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection-web'
  tags: resourceTags
  properties: { kty: 'RSA', keySize: 2048, keyOps: [ 'encrypt', 'decrypt', 'wrapKey', 'unwrapKey' ] }
}

resource apiKeyRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiKey.id, 'api', cryptoUserRole)
  scope: apiKey
  properties: { principalId: apiPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: cryptoUserRole }
}
resource webKeyRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(webKey.id, 'web', cryptoUserRole)
  scope: webKey
  properties: { principalId: webPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: cryptoUserRole }
}

module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    contactEmail: contactEmail
    resourceTags: resourceTags
    postgresId: resourceId('Microsoft.DBforPostgreSQL/flexibleServers', 'pg-shortener-${suffix}')
    eventhubsId: resourceId('Microsoft.EventHub/namespaces', 'eh-shortener-${suffix}')
    logWorkspaceId: resourceId('Microsoft.OperationalInsights/workspaces', 'log-shortener')
  }
}
