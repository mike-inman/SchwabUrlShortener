param location string
param acaSubnetId string
param logWorkspaceCustomerId string
@secure()
param logWorkspaceSharedKey string
param appInsightsConnectionString string
param acrLoginServer string
param apiIdentity object
param webIdentity object
param analyticsIdentity object
param apiImage string
param webImage string
param analyticsImage string
param apiClientId string
param publicHost string
param pgAppSecretUrl string
param pgMigrateSecretUrl string
param redisSecretUrl string
param eventhubsSecretUrl string
param eventhubsHost string
param storageBlobBaseUrl string
param apiKeyId string
param webKeyId string
param apiMinReplicas int
param apiMaxReplicas int
param webMinReplicas int
param webMaxReplicas int
param analyticsMinReplicas int
param analyticsMaxReplicas int
param enableScaleOutSettings bool

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-shortener-stg'
  location: location
  properties: {
    vnetConfiguration: { infrastructureSubnetId: acaSubnetId }
    workloadProfiles: [ { name: 'Consumption', workloadProfileType: 'Consumption' } ]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logWorkspaceCustomerId
        sharedKey: logWorkspaceSharedKey
      }
    }
  }
}

var apiOriginFqdn = 'ca-shortener-api-stg.${environment.properties.defaultDomain}'
var webOriginFqdn = 'ca-shortener-web-stg.${environment.properties.defaultDomain}'
var commonSecrets = [
  { name: 'pg-app', keyVaultUrl: pgAppSecretUrl }
  { name: 'redis', keyVaultUrl: redisSecretUrl }
]
var commonEnv = [
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
  { name: 'PublicLinks__BaseUrl', value: 'https://${publicHost}/' }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-shortener-api-stg'
  location: location
  tags: { 'azd-service-name': 'api' }
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${apiIdentity.id}': {} } }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [ { server: acrLoginServer, identity: apiIdentity.id } ]
      secrets: map(concat(commonSecrets, [ { name: 'eventhubs', keyVaultUrl: eventhubsSecretUrl } ]), s => union(s, { identity: apiIdentity.id }))
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 20 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 20 }
          ]
          env: concat(commonEnv, [
            { name: 'ConnectionStrings__ShortenerDb', secretRef: 'pg-app' }
            { name: 'ConnectionStrings__Redis', secretRef: 'redis' }
            { name: 'AZURE_CLIENT_ID', value: apiIdentity.clientId }
            { name: 'AzureAd__ClientId', value: apiClientId }
            { name: 'AzureAd__TenantId', value: 'organizations' }
            { name: 'AllowedHosts', value: '${apiOriginFqdn};ca-shortener-api-stg;${publicHost}' }
            { name: 'Kafka__BootstrapServers', value: '${eventhubsHost}:9093' }
            { name: 'Kafka__Topic', value: 'managed-clicks-v2' }
          ], enableScaleOutSettings ? [
            { name: 'Kafka__SecurityProtocol', value: 'SaslSsl' }
            { name: 'Kafka__SaslMechanism', value: 'Plain' }
            { name: 'Kafka__SaslUsername', value: '$ConnectionString' }
            { name: 'Kafka__SaslPassword', secretRef: 'eventhubs' }
            { name: 'Kafka__EnableIdempotence', value: 'true' }
            { name: 'DataProtection__Mode', value: 'AzureBlob' }
            { name: 'DataProtection__ApplicationName', value: 'Shortener.Api' }
            { name: 'DataProtection__BlobUri', value: '${storageBlobBaseUrl}/dataprotection/api-keys.xml' }
            { name: 'DataProtection__KeyVaultKeyId', value: apiKeyId }
            { name: 'ForwardedHeaders__KnownNetworks', value: '10.60.0.0/23' }
            { name: 'ForwardedHeaders__ForwardLimit', value: '2' }
            { name: 'LiveUpdates__Mode', value: 'Redis' }
            { name: 'LiveUpdates__Channel', value: 'shortener:links:v1' }
            { name: 'LiveUpdates__DebounceMilliseconds', value: '1000' }
          ] : [])
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: apiMaxReplicas
        rules: [ { name: 'http-50', http: { metadata: { concurrentRequests: '50' } } } ]
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-shortener-web-stg'
  location: location
  tags: { 'azd-service-name': 'web' }
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${webIdentity.id}': {} } }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [ { server: acrLoginServer, identity: webIdentity.id } ]
      secrets: [ { name: 'redis', keyVaultUrl: redisSecretUrl, identity: webIdentity.id } ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        stickySessions: { affinity: 'sticky' }
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 20 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 20 }
          ]
          env: concat(commonEnv, [
            { name: 'ConnectionStrings__Redis', secretRef: 'redis' }
            { name: 'AZURE_CLIENT_ID', value: webIdentity.clientId }
            { name: 'AllowedHosts', value: '${webOriginFqdn};${publicHost}' }
            { name: 'ShortenerApi__BaseUrl', value: 'http://ca-shortener-api-stg/' }
            { name: 'ShortenerApi__AllowedInternalHttpHosts', value: 'ca-shortener-api-stg' }
          ], enableScaleOutSettings ? [
            { name: 'TokenCache__Mode', value: 'Distributed' }
            { name: 'TokenCache__InstanceName', value: 'shortener:web:tokens:' }
            { name: 'DataProtection__Mode', value: 'AzureBlob' }
            { name: 'DataProtection__ApplicationName', value: 'Shortener.Web' }
            { name: 'DataProtection__BlobUri', value: '${storageBlobBaseUrl}/dataprotection/web-keys.xml' }
            { name: 'DataProtection__KeyVaultKeyId', value: webKeyId }
            { name: 'ForwardedHeaders__KnownNetworks', value: '10.60.0.0/23' }
            { name: 'ForwardedHeaders__ForwardLimit', value: '2' }
            { name: 'LiveUpdates__Mode', value: 'Redis' }
            { name: 'LiveUpdates__Channel', value: 'shortener:links:v1' }
            { name: 'LiveUpdates__DebounceMilliseconds', value: '1000' }
          ] : [])
        }
      ]
      scale: {
        minReplicas: webMinReplicas
        maxReplicas: webMaxReplicas
        rules: [ { name: 'http-50', http: { metadata: { concurrentRequests: '50' } } } ]
      }
    }
  }
}

resource analytics 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-shortener-analytics-stg'
  location: location
  tags: { 'azd-service-name': 'analytics' }
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${analyticsIdentity.id}': {} } }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [ { server: acrLoginServer, identity: analyticsIdentity.id } ]
      secrets: concat(map(commonSecrets, s => union(s, { identity: analyticsIdentity.id })), [
        { name: 'eventhubs', keyVaultUrl: eventhubsSecretUrl, identity: analyticsIdentity.id }
        { name: 'kafka-username', value: '$ConnectionString' }
      ])
    }
    template: {
      containers: [
        {
          name: 'analytics'
          image: analyticsImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, [
            { name: 'ConnectionStrings__ShortenerDb', secretRef: 'pg-app' }
            { name: 'ConnectionStrings__Redis', secretRef: 'redis' }
            { name: 'AZURE_CLIENT_ID', value: analyticsIdentity.clientId }
            { name: 'Kafka__BootstrapServers', value: '${eventhubsHost}:9093' }
            { name: 'Kafka__Topic', value: 'managed-clicks-v2' }
            { name: 'Kafka__ConsumerGroup', value: 'shortener-analytics-v1' }
          ], enableScaleOutSettings ? [
            { name: 'Kafka__SecurityProtocol', value: 'SaslSsl' }
            { name: 'Kafka__SaslMechanism', value: 'Plain' }
            { name: 'Kafka__SaslUsername', value: '$ConnectionString' }
            { name: 'Kafka__SaslPassword', secretRef: 'eventhubs' }
            { name: 'Kafka__EnableIdempotence', value: 'true' }
            { name: 'LiveUpdates__Mode', value: 'Redis' }
            { name: 'LiveUpdates__Channel', value: 'shortener:links:v1' }
            { name: 'LiveUpdates__DebounceMilliseconds', value: '1000' }
          ] : [])
        }
      ]
      scale: {
        minReplicas: analyticsMinReplicas
        maxReplicas: analyticsMaxReplicas
        rules: [
          {
            name: 'kafka-lag'
            custom: {
              type: 'kafka'
              metadata: {
                bootstrapServers: '${eventhubsHost}:9093'
                consumerGroup: 'shortener-analytics-v1'
                topic: 'managed-clicks-v2'
                lagThreshold: '100'
                tls: 'enable'
                sasl: 'plaintext'
              }
              auth: [
                { secretRef: 'kafka-username', triggerParameter: 'username' }
                { secretRef: 'eventhubs', triggerParameter: 'password' }
              ]
            }
          }
        ]
      }
    }
  }
}

resource migrate 'Microsoft.App/jobs@2024-03-01' = {
  name: 'caj-shortener-migrate-stg'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${apiIdentity.id}': {} } }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [ { server: acrLoginServer, identity: apiIdentity.id } ]
      secrets: [ { name: 'pg-migrate', keyVaultUrl: pgMigrateSecretUrl, identity: apiIdentity.id } ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: apiImage
          args: [ '--migrate' ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ConnectionStrings__ShortenerDb', secretRef: 'pg-migrate' }
            { name: 'AzureAd__ClientId', value: apiClientId }
            { name: 'AZURE_CLIENT_ID', value: apiIdentity.clientId }
          ]
        }
      ]
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output webFqdn string = web.properties.configuration.ingress.fqdn
output environmentId string = environment.id
