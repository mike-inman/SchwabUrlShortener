using './main.bicep'

param location = 'centralus'
param postgresPassword = readEnvironmentVariable('POSTGRES_PASSWORD')
param apiClientId = readEnvironmentVariable('API_CLIENT_ID', '')
param budgetContactEmail = '' // Set only when BUDGET_CONTACT_EMAIL is supplied.
param customDomain = '' // Use the Front Door azurefd.net host until DNS is ready.
param redisSku = 'Balanced_B0' // Use Balanced_B1 only if B0 is unavailable.
param deploymentPhase = 'foundation' // Then 'data' with a real password, 'apps' with digests, then 'edge'.
param apiImage = ''
param webImage = ''
param analyticsImage = ''
param apiMinReplicas = 1
param apiMaxReplicas = 1
param webMinReplicas = 1
param webMaxReplicas = 1
param analyticsMinReplicas = 0
param analyticsMaxReplicas = 0
param enableScaleOutSettings = false

// After Track A merges and staging validation passes:
// param apiMinReplicas = 2
// param apiMaxReplicas = 10
// param webMinReplicas = 2
// param webMaxReplicas = 6
// param analyticsMinReplicas = 1
// param analyticsMaxReplicas = 32
// param enableScaleOutSettings = true
