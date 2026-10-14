// Targeted P1 recovery deployment for the contract's AzureServices bypass.
// Keep the other vault settings identical to modules/foundation.bicep.
targetScope = 'resourceGroup'

param location string = 'centralus'
param suffix string
param acaSubnetId string
param operatorPublicIp string = ''

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-shortener-${suffix}'
  location: location
  tags: {
    app: 'schwab-url-shortener'
    env: 'prod'
    owner: 'social@mediaraq.com'
    costCenter: 'assessment'
    managedBy: 'bicep'
  }
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
      virtualNetworkRules: [ { id: acaSubnetId } ]
      ipRules: empty(operatorPublicIp) ? [] : [ { value: operatorPublicIp } ]
    }
  }
}
