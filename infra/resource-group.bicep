targetScope = 'subscription'

@allowed([ 'centralus', 'eastus2' ])
param location string = 'centralus'

resource staging 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-shortener-staging'
  location: location
}

output resourceGroupName string = staging.name
