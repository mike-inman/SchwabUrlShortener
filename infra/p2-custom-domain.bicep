// 2026-10-13: focused custom-domain deployment. DNS remains at GoDaddy.
targetScope = 'resourceGroup'

var tags = {
  app: 'schwab-url-shortener'
  env: 'prod'
  owner: 'social@mediaraq.com'
  costCenter: 'assessment'
  managedBy: 'bicep'
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: 'cae-shortener'
}

resource route 'Microsoft.App/managedEnvironments/httpRouteConfigs@2024-10-02-preview' = {
  parent: environment
  name: 'routeshortener'
  properties: {
    customDomains: [
      { name: 'ziplio.io', bindingType: 'Auto' }
      { name: 'www.ziplio.io', bindingType: 'Auto' }
    ]
    rules: [
      { description: 'API', routes: [ { match: { pathSeparatedPrefix: '/api' } }, { match: { pathSeparatedPrefix: '/r' } }, { match: { pathSeparatedPrefix: '/health' } } ], targets: [ { containerApp: 'ca-shortener-api' } ] }
      { description: 'Web', routes: [ { match: { prefix: '/' } } ], targets: [ { containerApp: 'ca-shortener-web' } ] }
    ]
  }
}

resource apexCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' = {
  parent: environment
  name: 'mc-ziplio-apex'
  location: resourceGroup().location
  tags: tags
  properties: {
    subjectName: 'ziplio.io'
    domainControlValidation: 'HTTP'
  }
  dependsOn: [ route ]
}

resource wwwCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' = {
  parent: environment
  name: 'mc-ziplio-www'
  location: resourceGroup().location
  tags: tags
  properties: {
    subjectName: 'www.ziplio.io'
    domainControlValidation: 'HTTP'
  }
  dependsOn: [ route ]
}
