using './main.bicep'

param location = 'centralus'
param postgresPassword = readEnvironmentVariable('POSTGRES_PASSWORD')
param redisPassword = readEnvironmentVariable('REDIS_PASSWORD')
param webClientSecret = readEnvironmentVariable('WEB_CLIENT_SECRET')
param budgetContactEmail = readEnvironmentVariable('BUDGET_CONTACT_EMAIL')
param operatorPublicIp = readEnvironmentVariable('OPERATOR_PUBLIC_IP', '')
param deploymentPhase = 'p1'
