targetScope = 'subscription'

param location string = 'centralus'

resource shortenerGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-schwab-url-shortener'
  location: location
  tags: {
    app: 'schwab-url-shortener'
    env: 'prod'
    owner: 'social@mediaraq.com'
    costCenter: 'assessment'
    managedBy: 'bicep'
  }
}

output resourceGroupId string = shortenerGroup.id
