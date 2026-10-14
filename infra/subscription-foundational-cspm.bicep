// Subscription-wide free Defender for Cloud posture baseline. Deploy only
// after Michael approves P1 and Microsoft.Security provider registration.
targetScope = 'subscription'

resource foundationalCspm 'Microsoft.Security/pricings@2024-01-01' = {
  name: 'CloudPosture'
  properties: { pricingTier: 'Free' }
}
