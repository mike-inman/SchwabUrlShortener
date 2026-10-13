param suffix string
param apiFqdn string
param webFqdn string
param logWorkspaceId string
param customDomain string = ''

resource profile 'Microsoft.Cdn/profiles@2025-04-15' = {
  name: 'afd-shortener-stg'
  location: 'global'
  sku: { name: 'Standard_AzureFrontDoor' }
}
resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2025-04-15' = {
  parent: profile
  name: 'afd-shortener-stg-${suffix}'
  location: 'global'
  properties: { enabledState: 'Enabled' }
}
resource apiGroup 'Microsoft.Cdn/profiles/originGroups@2025-04-15' = {
  parent: profile
  name: 'api'
  properties: {
    loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3 }
    healthProbeSettings: { probePath: '/health/live', probeRequestType: 'GET', probeProtocol: 'Https', probeIntervalInSeconds: 60 }
  }
}
resource webGroup 'Microsoft.Cdn/profiles/originGroups@2025-04-15' = {
  parent: profile
  name: 'web'
  properties: {
    loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3 }
    healthProbeSettings: { probePath: '/health/live', probeRequestType: 'GET', probeProtocol: 'Https', probeIntervalInSeconds: 60 }
  }
}
resource apiOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2025-04-15' = {
  parent: apiGroup
  name: 'container-app'
  properties: {
    hostName: apiFqdn
    originHostHeader: apiFqdn
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
  }
}
resource webOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2025-04-15' = {
  parent: webGroup
  name: 'container-app'
  properties: {
    hostName: webFqdn
    originHostHeader: webFqdn
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
  }
}
resource domain 'Microsoft.Cdn/profiles/customDomains@2025-04-15' = if (!empty(customDomain)) {
  parent: profile
  name: 'staging'
  properties: {
    hostName: customDomain
    tlsSettings: { certificateType: 'ManagedCertificate', minimumTlsVersion: 'TLS12' }
  }
}
resource apiRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2025-04-15' = {
  parent: endpoint
  name: 'api'
  properties: {
    originGroup: { id: apiGroup.id }
    supportedProtocols: [ 'Http', 'Https' ]
    patternsToMatch: [ '/api/*', '/r/*', '/health/*' ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    customDomains: empty(customDomain) ? [] : [ { id: domain.id } ]
    enabledState: 'Enabled'
  }
  dependsOn: [ apiOrigin ]
}
resource webRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2025-04-15' = {
  parent: endpoint
  name: 'web'
  properties: {
    originGroup: { id: webGroup.id }
    supportedProtocols: [ 'Http', 'Https' ]
    patternsToMatch: [ '/*' ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    customDomains: empty(customDomain) ? [] : [ { id: domain.id } ]
    enabledState: 'Enabled'
  }
  dependsOn: [ webOrigin ]
}
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: profile
  properties: { workspaceId: logWorkspaceId, logs: [ { categoryGroup: 'allLogs', enabled: true } ], metrics: [ { category: 'AllMetrics', enabled: true } ] }
}

output endpointHost string = endpoint.properties.hostName
