param location string

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-shortener-stg'
  location: location
  properties: {
    addressSpace: { addressPrefixes: [ '10.60.0.0/16' ] }
    subnets: [
      {
        name: 'snet-shortener-aca-stg'
        properties: {
          addressPrefix: '10.60.0.0/23'
          delegations: [
            {
              name: 'aca'
              properties: { serviceName: 'Microsoft.App/environments' }
            }
          ]
        }
      }
      {
        name: 'snet-shortener-private-stg'
        properties: {
          addressPrefix: '10.60.4.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output acaSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'snet-shortener-aca-stg')
output privateSubnetId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'snet-shortener-private-stg')
