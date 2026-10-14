param location string
param suffix string
param acaSubnetId string
param postgresSubnetId string
param postgresDnsZoneId string
param logWorkspaceId string
param vaultName string
param apiPrincipalId string
param webPrincipalId string
param analyticsPrincipalId string
param migratePrincipalId string
param redisPrincipalId string
param operatorPublicIp string
@secure()
param postgresPassword string
@secure()
param redisPassword string
@secure()
param webClientSecret string

var ipRules = empty(operatorPublicIp) ? [] : [ { value: operatorPublicIp, action: 'Allow' } ]
var secretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var cryptoUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
var blobContributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = { name: vaultName }

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'pg-shortener-${suffix}'
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '17'
    administratorLogin: 'shortener'
    administratorLoginPassword: postgresPassword
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    network: {
      delegatedSubnetResourceId: postgresSubnetId
      privateDnsZoneArmResourceId: postgresDnsZoneId
      publicNetworkAccess: 'Disabled'
    }
  }
}
resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'shortener'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

resource eventhubs 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: 'eh-shortener-${suffix}'
  location: location
  sku: { name: 'Standard', tier: 'Standard', capacity: 1 }
  properties: {
    isAutoInflateEnabled: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}
resource eventhubsNetwork 'Microsoft.EventHub/namespaces/networkRuleSets@2024-01-01' = {
  parent: eventhubs
  name: 'default'
  properties: {
    defaultAction: 'Deny'
    publicNetworkAccess: 'Enabled'
    trustedServiceAccessEnabled: false
    virtualNetworkRules: [ { subnet: { id: acaSubnetId }, ignoreMissingVnetServiceEndpoint: false } ]
    ipRules: empty(operatorPublicIp) ? [] : [ { ipMask: operatorPublicIp, action: 'Allow' } ]
  }
}
resource hub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  parent: eventhubs
  name: 'managed-clicks-v2'
  properties: { partitionCount: 32, messageRetentionInDays: 7 }
}
resource consumerGroup 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2024-01-01' = {
  parent: hub
  name: 'shortener-analytics-v1'
}
resource eventhubsRule 'Microsoft.EventHub/namespaces/authorizationRules@2024-01-01' = {
  parent: eventhubs
  name: 'shortener-apps'
  properties: { rights: [ 'Send', 'Listen' ] }
}

resource protectionStorage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stshortenerdp${suffix}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    publicNetworkAccess: 'Enabled'
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      virtualNetworkRules: [ { id: acaSubnetId, action: 'Allow' } ]
      ipRules: ipRules
    }
  }
}
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: protectionStorage
  name: 'default'
}
resource protectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'dataprotection'
  properties: { publicAccess: 'None' }
}

// Container Apps Azure Files mounts require an account key. Keep this account
// separate from the shared-key-disabled Data Protection storage account.
resource redisStorage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stshortenerrd${suffix}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: true
    allowBlobPublicAccess: false
    publicNetworkAccess: 'Enabled'
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      virtualNetworkRules: [ { id: acaSubnetId, action: 'Allow' } ]
      ipRules: ipRules
    }
  }
}
resource redisFiles 'Microsoft.Storage/storageAccounts/fileServices@2024-01-01' = {
  parent: redisStorage
  name: 'default'
}
resource redisShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2024-01-01' = {
  parent: redisFiles
  name: 'redis-data'
  properties: { shareQuota: 1, enabledProtocols: 'SMB' }
}

resource apiKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection-api'
  properties: { kty: 'RSA', keySize: 2048, keyOps: [ 'encrypt', 'decrypt', 'wrapKey', 'unwrapKey' ] }
}
resource webKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection-web'
  properties: { kty: 'RSA', keySize: 2048, keyOps: [ 'encrypt', 'decrypt', 'wrapKey', 'unwrapKey' ] }
}

var pgBase = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=shortener;Username=shortener;Password=${postgresPassword};SSL Mode=Require'
resource pgAppSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'pg-app-connection'
  properties: { value: '${pgBase};Maximum Pool Size=2' }
}
resource pgMigrateSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'pg-migrate-connection'
  properties: { value: pgBase }
}
resource redisPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'redis-password'
  properties: { value: redisPassword }
}
resource redisConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'redis-connection'
  properties: { value: 'ca-shortener-redis:6379,password=${redisPassword},abortConnect=False' }
}
resource eventhubsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'eventhubs-connection'
  properties: { value: eventhubsRule.listKeys().primaryConnectionString }
}
resource webClientSecretKv 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'web-client-secret'
  properties: { value: webClientSecret }
}

resource pgAppRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (principal, i) in [ apiPrincipalId, analyticsPrincipalId ]: {
  name: guid(pgAppSecret.id, 'reader-${i}', secretsUserRole)
  scope: pgAppSecret
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}]
resource pgMigrateRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(pgMigrateSecret.id, 'migrate', secretsUserRole)
  scope: pgMigrateSecret
  properties: { principalId: migratePrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}
resource redisRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (principal, i) in [ apiPrincipalId, webPrincipalId, analyticsPrincipalId ]: {
  name: guid(redisConnectionSecret.id, 'reader-${i}', secretsUserRole)
  scope: redisConnectionSecret
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}]
resource redisPasswordRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(redisPasswordSecret.id, 'redis', secretsUserRole)
  scope: redisPasswordSecret
  properties: { principalId: redisPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}
resource eventhubsRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (principal, i) in [ apiPrincipalId, analyticsPrincipalId ]: {
  name: guid(eventhubsSecret.id, 'reader-${i}', secretsUserRole)
  scope: eventhubsSecret
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}]
resource webClientSecretRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(webClientSecretKv.id, 'web', secretsUserRole)
  scope: webClientSecretKv
  properties: { principalId: webPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
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
resource blobWrite 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (principal, i) in [ apiPrincipalId, webPrincipalId ]: {
  name: guid(protectionContainer.id, 'writer-${i}', blobContributorRole)
  scope: protectionContainer
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: blobContributorRole }
}]

resource pgDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: postgres
  properties: { workspaceId: logWorkspaceId, logs: [ { categoryGroup: 'allLogs', enabled: true } ], metrics: [ { category: 'AllMetrics', enabled: true } ] }
}
resource eventhubsDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: eventhubs
  properties: { workspaceId: logWorkspaceId, logs: [ { categoryGroup: 'allLogs', enabled: true } ], metrics: [ { category: 'AllMetrics', enabled: true } ] }
}

output postgresId string = postgres.id
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output eventhubsId string = eventhubs.id
output eventhubsHost string = '${eventhubs.name}.servicebus.windows.net'
output protectionStorageName string = protectionStorage.name
output redisStorageName string = redisStorage.name
output pgAppSecretUrl string = '${vault.properties.vaultUri}secrets/${pgAppSecret.name}'
output pgMigrateSecretUrl string = '${vault.properties.vaultUri}secrets/${pgMigrateSecret.name}'
output redisSecretUrl string = '${vault.properties.vaultUri}secrets/${redisConnectionSecret.name}'
output eventhubsSecretUrl string = '${vault.properties.vaultUri}secrets/${eventhubsSecret.name}'
output apiKeyId string = apiKey.properties.keyUriWithVersion
output webKeyId string = webKey.properties.keyUriWithVersion
