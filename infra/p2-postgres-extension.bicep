// 2026-10-13 P2 migration prerequisite: allow only citext on existing server.
targetScope = 'resourceGroup'

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: 'pg-shortener-${substring(uniqueString(resourceGroup().id), 0, 6)}'
}

resource allowedExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: {
    value: 'citext'
    source: 'user-override'
  }
}
