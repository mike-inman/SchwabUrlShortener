param location string
param resourceTags object

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-shortener'
  location: location
  tags: resourceTags
  properties: {
    addressSpace: { addressPrefixes: [ '10.60.0.0/16' ] }
    subnets: [
      {
        name: 'snet-shortener-aca'
        properties: {
          addressPrefix: '10.60.0.0/23'
          delegations: [ { name: 'aca', properties: { serviceName: 'Microsoft.App/environments' } } ]
          serviceEndpoints: [
            { service: 'Microsoft.Storage' }
            { service: 'Microsoft.KeyVault' }
            { service: 'Microsoft.EventHub' }
          ]
        }
      }
      {
        name: 'snet-shortener-postgres'
        properties: {
          addressPrefix: '10.60.2.0/24'
          delegations: [ { name: 'postgres', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } } ]
        }
      }
    ]
  }
}

resource postgresZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'shortener.private.postgres.database.azure.com'
  location: 'global'
  tags: resourceTags
}

resource postgresZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresZone
  name: 'vnet-shortener'
  location: 'global'
  properties: { registrationEnabled: false, virtualNetwork: { id: vnet.id } }
}

output vnetId string = vnet.id
output acaSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'snet-shortener-aca')
output postgresSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'snet-shortener-postgres')
output postgresDnsZoneId string = postgresZone.id
