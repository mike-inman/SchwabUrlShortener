targetScope = 'subscription'

param location string = 'centralus'

resource shortenerGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-schwab-url-shortener'
  location: location
}

output resourceGroupId string = shortenerGroup.id
