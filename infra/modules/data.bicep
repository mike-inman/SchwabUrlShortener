param location string
param suffix string
param vnetId string
param privateSubnetId string
param logWorkspaceId string
param vaultName string
param apiPrincipalId string
param webPrincipalId string
param analyticsPrincipalId string
@secure()
param postgresPassword string
@allowed([ 'Balanced_B0', 'Balanced_B1' ])
param redisSku string = 'Balanced_B0'

var postgresName = 'pg-shortener-stg-${suffix}'
var redisName = 'redis-shortener-stg-${suffix}'
var eventhubsName = 'eh-shortener-stg-${suffix}'
var storageName = 'stshortenerstg${suffix}'
var secretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var cryptoUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
var blobContributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = { name: vaultName }

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresName
  location: location
  sku: { name: 'Standard_D2ds_v5', tier: 'GeneralPurpose' }
  properties: {
    version: '17'
    administratorLogin: 'shortener'
    administratorLoginPassword: postgresPassword
    storage: { storageSizeGB: 64 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    network: { publicNetworkAccess: 'Disabled' }
  }
}
resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'shortener'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}
resource pgbouncerEnabled 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'pgbouncer.enabled'
  properties: { value: 'true', source: 'user-override' }
}
resource pgbouncerMode 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'pgbouncer.pool_mode'
  properties: { value: 'transaction', source: 'user-override' }
  dependsOn: [ pgbouncerEnabled ]
}

resource redis 'Microsoft.Cache/redisEnterprise@2025-07-01' = {
  name: redisName
  location: location
  sku: { name: redisSku }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    highAvailability: 'Enabled'
    encryption: {}
  }
}
resource redisDb 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' = {
  parent: redis
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    clusteringPolicy: 'OSSCluster'
    evictionPolicy: 'VolatileLRU'
    port: 10000
    accessKeysAuthentication: 'Enabled'
  }
}

resource eventhubs 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: eventhubsName
  location: location
  sku: { name: 'Standard', tier: 'Standard', capacity: 1 }
  properties: {
    isAutoInflateEnabled: true
    maximumThroughputUnits: 4
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
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

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    publicNetworkAccess: 'Disabled'
    minimumTlsVersion: 'TLS1_2'
  }
}
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}
resource protectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'dataprotection'
  properties: { publicAccess: 'None' }
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

var pgBase = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=shortener;Username=shortener;Password=${postgresPassword};SSL Mode=Require'
resource pgAppSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'pg-app-connection'
  properties: { value: '${pgBase};Port=6432;No Reset On Close=true' }
  dependsOn: [ pgbouncerMode ]
}
resource pgMigrateSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'pg-migrate-connection'
  properties: { value: '${pgBase};Port=5432' }
}
resource redisSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'redis-connection'
  properties: { value: '${redis.properties.hostName}:10000,password=${redisDb.listKeys().primaryKey},ssl=True,abortConnect=False' }
}
resource eventhubsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'eventhubs-connection'
  properties: { value: eventhubsRule.listKeys().primaryConnectionString }
}

var pgSecretPrincipals = [ apiPrincipalId, analyticsPrincipalId ]
var redisSecretPrincipals = [ apiPrincipalId, webPrincipalId, analyticsPrincipalId ]
resource pgAppRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in pgSecretPrincipals: {
  name: guid(pgAppSecret.id, principal, secretsUserRole)
  scope: pgAppSecret
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}]
resource redisRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in redisSecretPrincipals: {
  name: guid(redisSecret.id, principal, secretsUserRole)
  scope: redisSecret
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}]
resource eventhubsRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in [ apiPrincipalId, analyticsPrincipalId ]: {
  name: guid(eventhubsSecret.id, principal, secretsUserRole)
  scope: eventhubsSecret
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}]
resource migrateRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(pgMigrateSecret.id, apiPrincipalId, secretsUserRole)
  scope: pgMigrateSecret
  properties: { principalId: apiPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: secretsUserRole }
}
resource apiKeyRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiKey.id, apiPrincipalId, cryptoUserRole)
  scope: apiKey
  properties: { principalId: apiPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: cryptoUserRole }
}
resource webKeyRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(webKey.id, webPrincipalId, cryptoUserRole)
  scope: webKey
  properties: { principalId: webPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: cryptoUserRole }
}
resource blobWrite 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in [ apiPrincipalId, webPrincipalId ]: {
  name: guid(protectionContainer.id, principal, blobContributorRole)
  scope: protectionContainer
  properties: { principalId: principal, principalType: 'ServicePrincipal', roleDefinitionId: blobContributorRole }
}]

var endpointDefinitions = [
  { name: 'postgres', resourceId: postgres.id, group: 'postgresqlServer', zone: 'privatelink.postgres.database.azure.com' }
  { name: 'redis', resourceId: redis.id, group: 'redisEnterprise', zone: 'privatelink.redis.azure.net' }
  { name: 'eventhubs', resourceId: eventhubs.id, group: 'namespace', zone: 'privatelink.servicebus.windows.net' }
  { name: 'blob', resourceId: storage.id, group: 'blob', zone: 'privatelink.blob.${environment().suffixes.storage}' }
]
resource zones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for item in endpointDefinitions: {
  name: item.zone
  location: 'global'
}]
resource links 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (item, i) in endpointDefinitions: {
  parent: zones[i]
  name: 'vnet-shortener-stg'
  location: 'global'
  properties: { registrationEnabled: false, virtualNetwork: { id: vnetId } }
}]
resource endpoints 'Microsoft.Network/privateEndpoints@2024-05-01' = [for item in endpointDefinitions: {
  name: 'pe-shortener-${item.name}-stg'
  location: location
  properties: {
    subnet: { id: privateSubnetId }
    privateLinkServiceConnections: [
      { name: item.name, properties: { privateLinkServiceId: item.resourceId, groupIds: [ item.group ] } }
    ]
  }
}]
resource zoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = [for (item, i) in endpointDefinitions: {
  parent: endpoints[i]
  name: 'default'
  properties: { privateDnsZoneConfigs: [ { name: item.name, properties: { privateDnsZoneId: zones[i].id } } ] }
}]

resource pgDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: postgres
  properties: { workspaceId: logWorkspaceId, logs: [ { categoryGroup: 'allLogs', enabled: true } ], metrics: [ { category: 'AllMetrics', enabled: true } ] }
}
resource redisDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: redis
  properties: { workspaceId: logWorkspaceId, logs: [ { categoryGroup: 'allLogs', enabled: true } ], metrics: [ { category: 'AllMetrics', enabled: true } ] }
}
resource eventhubsDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: eventhubs
  properties: { workspaceId: logWorkspaceId, logs: [ { categoryGroup: 'allLogs', enabled: true } ], metrics: [ { category: 'AllMetrics', enabled: true } ] }
}

output postgresHost string = postgres.properties.fullyQualifiedDomainName
output redisHost string = redis.properties.hostName
output eventhubsHost string = '${eventhubs.name}.servicebus.windows.net'
output storageName string = storage.name
output storageBlobBaseUrl string = 'https://${storage.name}.blob.${environment().suffixes.storage}'
output pgAppSecretUrl string = '${vault.properties.vaultUri}secrets/${pgAppSecret.name}'
output pgMigrateSecretUrl string = '${vault.properties.vaultUri}secrets/${pgMigrateSecret.name}'
output redisSecretUrl string = '${vault.properties.vaultUri}secrets/${redisSecret.name}'
output eventhubsSecretUrl string = '${vault.properties.vaultUri}secrets/${eventhubsSecret.name}'
output apiKeyId string = apiKey.properties.keyUriWithVersion
output webKeyId string = webKey.properties.keyUriWithVersion
