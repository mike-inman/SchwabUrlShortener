// P2 closeout: remove the temporary operator public-IP allowances after runtime checks.
targetScope = 'resourceGroup'

param location string = 'centralus'
var suffix = substring(uniqueString(resourceGroup().id), 0, 6)
var tags = {
  app: 'schwab-url-shortener'
  env: 'prod'
  owner: 'social@mediaraq.com'
  costCenter: 'assessment'
  managedBy: 'bicep'
}
resource acaSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  name: 'vnet-shortener/snet-shortener-aca'
}
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-shortener-${suffix}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Deny'
      virtualNetworkRules: [ { id: acaSubnet.id } ]
      ipRules: []
    }
  }
}
resource eventhubs 'Microsoft.EventHub/namespaces@2024-01-01' existing = {
  name: 'eh-shortener-${suffix}'
}
resource eventhubsNetwork 'Microsoft.EventHub/namespaces/networkRuleSets@2024-01-01' = {
  parent: eventhubs
  name: 'default'
  properties: {
    defaultAction: 'Deny'
    publicNetworkAccess: 'Enabled'
    trustedServiceAccessEnabled: false
    virtualNetworkRules: [ { subnet: { id: acaSubnet.id }, ignoreMissingVnetServiceEndpoint: false } ]
    ipRules: []
  }
}
resource protectionStorage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stshortenerdp${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      virtualNetworkRules: [ { id: acaSubnet.id, action: 'Allow' } ]
      ipRules: []
    }
  }
}
resource redisStorage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stshortenerrd${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: true
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      virtualNetworkRules: [ { id: acaSubnet.id, action: 'Allow' } ]
      ipRules: []
    }
  }
}
