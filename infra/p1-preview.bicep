// 2026-10-13: subscription-scope what-if for a new, absent resource group.
// This file is preview-only. Do not deploy it; P1 requires explicit approval.
targetScope = 'subscription'

param location string = 'centralus'
@secure()
param postgresPassword string
@secure()
param redisPassword string
@secure()
param webClientSecret string
param budgetContactEmail string
param operatorPublicIp string = ''

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

module p1 './main.bicep' = {
  name: 'shortener-p1-preview'
  scope: shortenerGroup
  params: {
    location: location
    postgresPassword: postgresPassword
    redisPassword: redisPassword
    webClientSecret: webClientSecret
    budgetContactEmail: budgetContactEmail
    operatorPublicIp: operatorPublicIp
    deploymentPhase: 'p1'
  }
}

module freeCspm './subscription-foundational-cspm.bicep' = {
  name: 'shortener-foundational-cspm-preview'
}

// The lock follows every P1 resource and the free subscription posture plan.
module finalLock './p1-lock.bicep' = {
  name: 'shortener-p1-final-lock-preview'
  scope: shortenerGroup
  dependsOn: [ p1, freeCspm ]
}
