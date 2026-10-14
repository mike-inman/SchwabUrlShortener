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
