# Azure staging infrastructure — 2026-10-13

This directory implements the [Azure scale-out contract](../docs/2026-10-13-1720-azure-scaleout-contract.md) for `rg-shortener-staging`. Read the [validation record](../docs/2026-10-13-1757-track-c-azure-infrastructure-validation.md) before running any deployment. The 18:10 contract includes each app's origin FQDN in `AllowedHosts`.

## Parameters and environment

`infra/main.bicep` is the entry point. `main.staging.bicepparam` records the initial staging values and the commented Track A scale set. `main.parameters.json` maps azd environment values into the Bicep deployment. `azure.yaml` uses prebuilt digest images and invokes `scripts/build-images.sh` before `azd deploy`.

The azd environment must target the confirmed subscription and exactly `rg-shortener-staging`. Required values before C1 are `AZURE_LOCATION=centralus`, `DEPLOYMENT_PHASE=foundation`, `API_CLIENT_ID` (the existing Entra API app registration ID), `REDIS_SKU=Balanced_B0`, and nonsecret bootstrap values for `SERVICE_API_IMAGE`, `SERVICE_WEB_IMAGE`, and `SERVICE_ANALYTICS_IMAGE`. The `main.parameters.json` mapping also requires a `POSTGRES_PASSWORD` azd environment value; it may be a nonsecret bootstrap placeholder for foundation only and must be replaced with a real password before C2. Michael enters the real password without displaying it. Set `BUDGET_CONTACT_EMAIL` to an alert address or an empty value, and `CUSTOM_DOMAIN` to an approved staging hostname or an empty value. Empty `CUSTOM_DOMAIN` uses the Azure Front Door endpoint hostname. `POSTGRES_PASSWORD` is a secure Bicep parameter and is not written into the repository.

Use `Balanced_B1` only if the B0 SKU is unavailable in `centralus`; use `eastus2` only when a required SKU is unavailable in `centralus`. Check region availability and role assignments before provisioning. The three image values can be `bootstrap-unset` while `DEPLOYMENT_PHASE=foundation`, because no apps are created in that phase.

## Phased workflow

1. C1: confirm subscription, account, access, and that `rg-shortener-staging` does not already exist. Preview its creation with `az deployment sub what-if --location centralus --template-file infra/resource-group.bicep`, then create only that new group. Preview foundation changes with `azd provision --preview` before provisioning. Do not modify any existing resource group. The foundation creates ACR before any image build.
2. C2: Michael sets the real PostgreSQL password without displaying it. Set `DEPLOYMENT_PHASE=data`, preview, then provision private data services. Verify each private endpoint and secret name without reading values.
3. From a clean committed tree, run `infra/scripts/build-images.sh`. It runs `az acr build --target` for `api`, `web`, and `analytics`, finds each digest, and stores `SERVICE_*_IMAGE` in the active azd environment. A given Git commit reuses its existing ACR tag and digest. The script must never receive secrets as build args.
4. C3: set `DEPLOYMENT_PHASE=apps`, preview, then provision. Run `azd deploy`; its `predeploy` hook verifies or builds the same digest images. The migration job uses the API digest. Start `caj-shortener-migrate-stg` once per release with `az containerapp job start --resource-group rg-shortener-staging --name caj-shortener-migrate-stg`, then inspect its execution status before allowing traffic. This job uses the direct PostgreSQL port and must not run concurrently with another migration.
5. C4: after the apps are healthy, set `DEPLOYMENT_PHASE=edge`, preview, then provision. Validate public routes and a WebSocket upgrade through Front Door. A custom domain additionally requires the DNS validation and certificate steps in the Azure portal.

The active staging replica set is API 1/1, Web 1/1, Analytics 0/0. The commented Track A set in `main.staging.bicepparam` is API 2/10, Web 2/6, Analytics 1/32. `enableScaleOutSettings=false` keeps Track A and B settings out of today's images. Enable it only after their code has merged. Track A confirmed Event Hubs idempotent Kafka producer support from Microsoft documentation, so `Kafka__EnableIdempotence=true` is staged behind that switch.

## Secrets and diagnosis

Key Vault holds `pg-app-connection`, `pg-migrate-connection`, `redis-connection`, and `eventhubs-connection`. App and job definitions use Key Vault references with their own user-assigned identities. Review names and access status only; do not list secret values. PostgreSQL's app connection uses PgBouncer port 6432 and `No Reset On Close=true`; migration uses port 5432.

Log Analytics receives container console logs. A useful starting KQL query is:

```kusto
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(1h)
| where Log_s has "Service started"
| project TimeGenerated, ContainerAppName_s, Log_s
| order by TimeGenerated desc
```

The exact table and column casing must be verified against the deployed workspace. Application Insights receives a connection string for later OpenTelemetry instrumentation; this track does not add exporters to application code.
