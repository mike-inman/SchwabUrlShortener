// 2026-10-13 P2 only. Existing P1 resources are references and are not reapplied.
targetScope = 'resourceGroup'

param location string = 'centralus'
param apiImage string
param webImage string
param analyticsImage string
param redisImage string
param apiClientId string
param webClientId string
param enableScaleOutSettings bool = true
param customDomain string = 'ziplio.io'

var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var tags = {
  app: 'schwab-url-shortener'
  env: 'prod'
  owner: 'social@mediaraq.com'
  costCenter: 'assessment'
  managedBy: 'bicep'
}
var vaultName = 'kv-shortener-${suffix}'
var acrName = 'acrshortener${suffix}'
var dpStorageName = 'stshortenerdp${suffix}'
var redisStorageName = 'stshortenerrd${suffix}'
var eventhubsName = 'eh-shortener-${suffix}'
var routeName = 'routeshortener'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' existing = { name: 'vnet-shortener' }
resource acaSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = { parent: vnet, name: 'snet-shortener-aca' }
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = { name: 'log-shortener' }
resource insights 'Microsoft.Insights/components@2020-02-02' existing = { name: 'appi-shortener' }
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = { name: vaultName }
resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = { name: acrName }
resource dpStorage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = { name: dpStorageName }
resource redisStorage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = { name: redisStorageName }
resource apiId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: 'id-shortener-api' }
resource webId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: 'id-shortener-web' }
resource analyticsId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: 'id-shortener-analytics' }
resource migrateId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: 'id-shortener-migrate' }
resource redisId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: 'id-shortener-redis' }

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-shortener'
  location: location
  tags: tags
  properties: {
    vnetConfiguration: { infrastructureSubnetId: acaSubnet.id, internal: false }
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: { customerId: logs.properties.customerId, sharedKey: logs.listKeys().primarySharedKey }
    }
    workloadProfiles: [ { name: 'Consumption', workloadProfileType: 'Consumption' } ]
  }
}

// Container Apps Azure Files storage requires this account's shared key.
resource redisFiles 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: environment
  name: 'redis-data'
  properties: {
    azureFile: {
      accountName: redisStorage.name
      accountKey: redisStorage.listKeys().keys[0].value
      shareName: 'redis-data'
      accessMode: 'ReadWrite'
    }
  }
}

var routeHost = '${routeName}.${environment.properties.defaultDomain}'
var publicHost = empty(customDomain) ? routeHost : customDomain
var vaultBase = 'https://${vault.name}.vault.azure.net/secrets/'
var dpBlobBase = 'https://${dpStorage.name}.blob.core.windows.net/dataprotection/'
var dpKeyBase = 'https://${vault.name}.vault.azure.net/keys/'
var commonIdentityEnv = 'Customer'
var appInsightsEnv = [ { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString } ]

resource redis 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-shortener-redis'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${redisId.id}': {} } }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 6379, transport: 'tcp' }
      secrets: [ { name: 'redis-password', keyVaultUrl: '${vaultBase}redis-password', identity: redisId.id } ]
    }
    template: {
      containers: [ {
        name: 'redis'
        image: redisImage
        command: [ '/bin/sh', '-c' ]
        args: [ 'exec redis-server --requirepass "$REDIS_PASSWORD" --appendonly yes --appendfsync everysec --dir /data' ]
        env: [
          { name: 'AZURE_CLIENT_ID', value: redisId.properties.clientId }
          { name: 'REDIS_PASSWORD', secretRef: 'redis-password' }
        ]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        volumeMounts: [ { volumeName: 'redis-data', mountPath: '/data' } ]
        probes: [ { type: 'Liveness', tcpSocket: { port: 6379 }, initialDelaySeconds: 10, periodSeconds: 15 } ]
      } ]
      volumes: [ { name: 'redis-data', storageType: 'AzureFile', storageName: redisFiles.name } ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// ACA ingress reaches containers from its reserved 100.100.0.0/17 range;
// keep the VNet range for in-environment callers (Azure custom VNet documentation).
var apiScaleEnv = enableScaleOutSettings ? [
  { name: 'DataProtection__Mode', value: 'AzureBlob' }
  { name: 'DataProtection__ApplicationName', value: 'Shortener.Api' }
  { name: 'DataProtection__BlobUri', value: '${dpBlobBase}api-keys.xml' }
  { name: 'DataProtection__KeyVaultKeyId', value: '${dpKeyBase}dataprotection-api' }
  { name: 'ForwardedHeaders__KnownNetworks', value: '10.60.0.0/23,100.100.0.0/17' }
  { name: 'ForwardedHeaders__ForwardLimit', value: '1' }
  { name: 'LiveUpdates__Mode', value: 'Redis' }
  { name: 'LiveUpdates__Channel', value: 'shortener:links:v1' }
  { name: 'LiveUpdates__DebounceMilliseconds', value: '1000' }
] : []
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-shortener-api'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${apiId.id}': {} } }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'http', allowInsecure: true }
      registries: [ { server: registry.properties.loginServer, identity: apiId.id } ]
      secrets: [
        { name: 'pg-app-connection', keyVaultUrl: '${vaultBase}pg-app-connection', identity: apiId.id }
        { name: 'redis-connection', keyVaultUrl: '${vaultBase}redis-connection', identity: apiId.id }
      ]
    }
    template: {
      containers: [ {
        name: 'api'
        image: apiImage
        env: concat([
          { name: 'AZURE_CLIENT_ID', value: apiId.properties.clientId }
          { name: 'ConnectionStrings__ShortenerDb', secretRef: 'pg-app-connection' }
          { name: 'ConnectionStrings__Redis', secretRef: 'redis-connection' }
          { name: 'Identity__Mode', value: commonIdentityEnv }
          { name: 'AzureAd__Instance', value: 'https://schwaburlshortener.ciamlogin.com/' }
          { name: 'AzureAd__TenantId', value: '6adc36e9-dfe5-469f-8aed-6ef83abcdd19' }
          { name: 'AzureAd__ClientId', value: apiClientId }
          { name: 'AllowedHosts', value: 'ca-shortener-api.${environment.properties.defaultDomain};ca-shortener-api;${routeHost};${publicHost};www.ziplio.io' }
          { name: 'PublicLinks__BaseUrl', value: 'https://${publicHost}/' }
        ], apiScaleEnv, appInsightsEnv)
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        probes: [
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, httpHeaders: [ { name: 'Host', value: 'ca-shortener-api.${environment.properties.defaultDomain}' } ] }, initialDelaySeconds: 10, periodSeconds: 15 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, httpHeaders: [ { name: 'Host', value: 'ca-shortener-api.${environment.properties.defaultDomain}' } ] }, initialDelaySeconds: 15, periodSeconds: 15 }
        ]
      } ]
      scale: { minReplicas: 1, maxReplicas: 5, rules: [ { name: 'http-concurrency', http: { metadata: { concurrentRequests: '10' } } } ] }
    }
  }
}

var webScaleEnv = enableScaleOutSettings ? [
  { name: 'DataProtection__Mode', value: 'AzureBlob' }
  { name: 'DataProtection__ApplicationName', value: 'Shortener.Web' }
  { name: 'DataProtection__BlobUri', value: '${dpBlobBase}web-keys.xml' }
  { name: 'DataProtection__KeyVaultKeyId', value: '${dpKeyBase}dataprotection-web' }
  { name: 'TokenCache__Mode', value: 'Distributed' }
  { name: 'TokenCache__InstanceName', value: 'shortener:web:tokens:' }
  { name: 'ForwardedHeaders__KnownNetworks', value: '10.60.0.0/23,100.100.0.0/17' }
  { name: 'ForwardedHeaders__ForwardLimit', value: '1' }
  { name: 'LiveUpdates__Mode', value: 'Redis' }
  { name: 'LiveUpdates__Channel', value: 'shortener:links:v1' }
  { name: 'LiveUpdates__DebounceMilliseconds', value: '1000' }
] : []
resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-shortener-web'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${webId.id}': {} } }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'http', stickySessions: { affinity: 'sticky' } }
      registries: [ { server: registry.properties.loginServer, identity: webId.id } ]
      secrets: [
        { name: 'redis-connection', keyVaultUrl: '${vaultBase}redis-connection', identity: webId.id }
        { name: 'web-client-secret', keyVaultUrl: '${vaultBase}web-client-secret', identity: webId.id }
      ]
    }
    template: {
      containers: [ {
        name: 'web'
        image: webImage
        env: concat([
          { name: 'AZURE_CLIENT_ID', value: webId.properties.clientId }
          { name: 'ConnectionStrings__Redis', secretRef: 'redis-connection' }
          { name: 'Identity__Mode', value: commonIdentityEnv }
          { name: 'AzureAd__Authority', value: 'https://schwaburlshortener.ciamlogin.com/6adc36e9-dfe5-469f-8aed-6ef83abcdd19/v2.0' }
          { name: 'AzureAd__TenantId', value: '6adc36e9-dfe5-469f-8aed-6ef83abcdd19' }
          { name: 'AzureAd__ClientId', value: webClientId }
          { name: 'AzureAd__ClientSecret', secretRef: 'web-client-secret' }
          { name: 'AzureAd__CallbackPath', value: '/signin-oidc' }
          { name: 'AzureAd__SignedOutCallbackPath', value: '/signout-callback-oidc' }
          { name: 'ShortenerApi__Scope', value: 'api://${apiClientId}/access_as_user' }
          { name: 'ShortenerApi__BaseUrl', value: 'http://ca-shortener-api/' }
          { name: 'ShortenerApi__AllowedInternalHttpHosts', value: 'ca-shortener-api' }
          { name: 'AllowedHosts', value: 'ca-shortener-web.${environment.properties.defaultDomain};${routeHost};${publicHost};www.ziplio.io' }
          { name: 'PublicLinks__BaseUrl', value: 'https://${publicHost}/' }
        ], webScaleEnv, appInsightsEnv)
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        probes: [
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, httpHeaders: [ { name: 'Host', value: 'ca-shortener-web.${environment.properties.defaultDomain}' } ] }, initialDelaySeconds: 10, periodSeconds: 15 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, httpHeaders: [ { name: 'Host', value: 'ca-shortener-web.${environment.properties.defaultDomain}' } ] }, initialDelaySeconds: 15, periodSeconds: 15 }
        ]
      } ]
      scale: { minReplicas: 1, maxReplicas: 3, rules: [ { name: 'http-concurrency', http: { metadata: { concurrentRequests: '10' } } } ] }
    }
  }
}

var analyticsScaleEnv = enableScaleOutSettings ? [
  { name: 'Kafka__SecurityProtocol', value: 'SaslSsl' }
  { name: 'Kafka__SaslMechanism', value: 'Plain' }
  { name: 'Kafka__SaslUsername', value: '$ConnectionString' }
  { name: 'Kafka__SaslPassword', secretRef: 'eventhubs-connection' }
  { name: 'Kafka__EnableIdempotence', value: 'true' }
  { name: 'LiveUpdates__Mode', value: 'Redis' }
  { name: 'LiveUpdates__Channel', value: 'shortener:links:v1' }
  { name: 'LiveUpdates__DebounceMilliseconds', value: '1000' }
] : []
resource analytics 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-shortener-analytics'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${analyticsId.id}': {} } }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8080, transport: 'http' }
      registries: [ { server: registry.properties.loginServer, identity: analyticsId.id } ]
      secrets: [
        { name: 'pg-app-connection', keyVaultUrl: '${vaultBase}pg-app-connection', identity: analyticsId.id }
        { name: 'redis-connection', keyVaultUrl: '${vaultBase}redis-connection', identity: analyticsId.id }
        { name: 'eventhubs-connection', keyVaultUrl: '${vaultBase}eventhubs-connection', identity: analyticsId.id }
        { name: 'kafka-username', value: '$ConnectionString' }
      ]
    }
    template: {
      containers: [ {
        name: 'analytics'
        image: analyticsImage
        env: concat([
          { name: 'AZURE_CLIENT_ID', value: analyticsId.properties.clientId }
          { name: 'ConnectionStrings__ShortenerDb', secretRef: 'pg-app-connection' }
          { name: 'ConnectionStrings__Redis', secretRef: 'redis-connection' }
          { name: 'Kafka__BootstrapServers', value: '${eventhubsName}.servicebus.windows.net:9093' }
          { name: 'Kafka__Topic', value: 'managed-clicks-v2' }
          { name: 'Kafka__ConsumerGroup', value: 'shortener-analytics-v1' }
        ], analyticsScaleEnv, appInsightsEnv)
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        probes: [
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 15 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, initialDelaySeconds: 30, periodSeconds: 20 }
        ]
      } ]
      scale: {
        minReplicas: enableScaleOutSettings ? 1 : 0
        maxReplicas: enableScaleOutSettings ? 4 : 0
        rules: enableScaleOutSettings ? [ {
          name: 'kafka-lag'
          custom: {
            type: 'kafka'
            metadata: {
              bootstrapServers: '${eventhubsName}.servicebus.windows.net:9093'
              consumerGroup: 'shortener-analytics-v1'
              topic: 'managed-clicks-v2'
              lagThreshold: '50'
              offsetResetPolicy: 'earliest'
              allowIdleConsumers: 'false'
              sasl: 'plaintext'
              tls: 'enable'
            }
            auth: [
              { secretRef: 'kafka-username', triggerParameter: 'username' }
              { secretRef: 'eventhubs-connection', triggerParameter: 'password' }
            ]
          }
        } ] : []
      }
    }
  }
}

resource migration 'Microsoft.App/jobs@2024-03-01' = {
  name: 'caj-shortener-migrate'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${migrateId.id}': {} } }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [ { server: registry.properties.loginServer, identity: migrateId.id } ]
      secrets: [ { name: 'pg-migrate-connection', keyVaultUrl: '${vaultBase}pg-migrate-connection', identity: migrateId.id } ]
    }
    template: {
      containers: [ {
        name: 'migrate'
        image: apiImage
        command: [ 'dotnet' ]
        args: [ 'Shortener.Api.dll', '--migrate' ]
        env: [
          { name: 'AZURE_CLIENT_ID', value: migrateId.properties.clientId }
          { name: 'ConnectionStrings__ShortenerDb', secretRef: 'pg-migrate-connection' }
          { name: 'Identity__Mode', value: commonIdentityEnv }
          { name: 'AzureAd__Instance', value: 'https://schwaburlshortener.ciamlogin.com/' }
          { name: 'AzureAd__TenantId', value: '6adc36e9-dfe5-469f-8aed-6ef83abcdd19' }
          { name: 'AzureAd__ClientId', value: apiClientId }
        ]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
      } ]
    }
  }
}

resource route 'Microsoft.App/managedEnvironments/httpRouteConfigs@2024-10-02-preview' = {
  parent: environment
  name: routeName
  properties: {
    customDomains: [
      { name: 'ziplio.io', bindingType: 'Auto' }
      { name: 'www.ziplio.io', bindingType: 'Auto' }
    ]
    rules: [
      { description: 'API', routes: [ { match: { pathSeparatedPrefix: '/api' } }, { match: { pathSeparatedPrefix: '/r' } }, { match: { pathSeparatedPrefix: '/health' } } ], targets: [ { containerApp: api.name } ] }
      { description: 'Web', routes: [ { match: { prefix: '/' } } ], targets: [ { containerApp: web.name } ] }
    ]
  }
}

output publicHost string = publicHost
output routeFqdn string = route.properties.fqdn
output environmentDefaultDomain string = environment.properties.defaultDomain
