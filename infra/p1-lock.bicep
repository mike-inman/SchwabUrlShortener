// Apply after all P1 resources and validation, never before foundation/data.
targetScope = 'resourceGroup'

resource protectGroup 'Microsoft.Authorization/locks@2016-09-01' = {
  name: 'lock-shortener-cannot-delete'
  properties: {
    level: 'CanNotDelete'
    notes: 'Protect the production assessment resource group from accidental deletion. Removal requires a deliberate operator step.'
  }
}
