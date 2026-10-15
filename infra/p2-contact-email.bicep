// 2026-10-14. Deploy with enableSending=false until ziplio.io verifies.
targetScope = 'resourceGroup'

param domainName string = 'ziplio.io'
param enableSending bool = false

var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var tags = {
  app: 'schwab-url-shortener'
  env: 'prod'
  owner: 'social@mediaraq.com'
  costCenter: 'assessment'
  managedBy: 'bicep'
}

resource analyticsId 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-shortener-analytics'
}

resource emailService 'Microsoft.Communication/emailServices@2025-05-01' = {
  name: 'email-shortener-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'United States'
  }
}

resource domain 'Microsoft.Communication/emailServices/domains@2025-05-01' = {
  parent: emailService
  name: domainName
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'CustomerManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource sender 'Microsoft.Communication/emailServices/domains/senderUsernames@2025-05-01' = {
  parent: domain
  name: 'noreply'
  properties: {
    username: 'noreply'
    displayName: 'Ziplio'
  }
}

resource communication 'Microsoft.Communication/communicationServices@2025-05-01' = {
  name: 'comm-shortener-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'United States'
    disableLocalAuth: true
    linkedDomains: enableSending ? [domain.id] : []
  }
}

// The narrowest suitable built-in role in this subscription. Its resource scope
// is the Communication Service, never the resource group or subscription.
var communicationEmailOwnerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '09976791-48a7-449e-bb21-39d1a415f350')
resource analyticsEmailRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableSending) {
  name: guid(communication.id, analyticsId.id, communicationEmailOwnerRole)
  scope: communication
  properties: {
    principalId: analyticsId.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: communicationEmailOwnerRole
  }
}

output emailServiceName string = emailService.name
output domainResourceId string = domain.id
output communicationServiceName string = communication.name
output communicationEndpoint string = 'https://${communication.properties.hostName}'
output verificationRecords object = domain.properties.verificationRecords
