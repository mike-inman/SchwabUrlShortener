// 2026-10-14: focused Track G access deployment after the secret exists.
targetScope = 'resourceGroup'

var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var secretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: 'kv-shortener-${suffix}'
}

resource visitorHashSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' existing = {
  parent: vault
  name: 'visitor-hash-secret'
}

resource apiId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-shortener-api'
}

resource apiVisitorHashRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(visitorHashSecret.id, 'api', secretsUserRole)
  scope: visitorHashSecret
  properties: {
    principalId: apiId.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: secretsUserRole
  }
}
