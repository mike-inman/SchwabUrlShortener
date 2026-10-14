# Track A scale-out readiness validation — 2026-10-13 18:01 CDT

## Purpose and scope

Record Track A implementation and local evidence against the Azure scale-out contract revised through 2026-10-13 18:10. This validates replaceable local containers and configuration paths; it is not an Azure deployment or live Entra acceptance record. No existing `schwab-url-shortener-*` containers, volumes, `.env` files, passwords, or API keys were changed.

## Configuration and implementation

- API and Web require an explicit Data Protection mode and application name. `Filesystem` uses the existing named-volume path in containers. `AzureBlob` validates the blob and Key Vault URIs and configures Azure Blob key persistence with Key Vault key protection and `DefaultAzureCredential`. Each host performs a startup protection operation so key-store failure prevents startup.
- Web requires an explicit token-cache mode. `Distributed` requires Redis and an instance name, configures Microsoft.Identity.Web's distributed token cache with encryption and synchronous L2 writes, and checks Redis during startup. The multiplexer registration uses `TryAddSingleton`. `InMemory` remains explicit for single-instance development.
- API and Web support either a DNS-resolved known proxy host or comma-separated CIDR networks, with a positive forward limit and no trust source when neither setting is supplied. Both trust sources together fail startup. Existing `AllowedHosts` settings and public-host filtering remain in place.
- Analytics applies Kafka protocol, SASL, and idempotence settings to producer, consumer, monitor admin client, and one-shot health admin client. Its HTTP liveness and dependency readiness endpoints listen on port 8080; readiness uses bounded PostgreSQL and topic-metadata checks. Dependency failures log only the dependency and exception type.
- `compose.scaleout.yaml` runs two API and two Web services. The replicas share their respective key volumes and resolve through Docker DNS aliases. Caddy uses API round robin and a sticky Web cookie with dynamic A-record upstreams.

## Local checks

- Final solution build: `MSBUILDDISABLENODEREUSE=1 dotnet build SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false` — **passed, zero warnings and zero errors**.
- Focused final tests: `dotnet test tests/Shortener.WebTests/Shortener.WebTests.csproj --nologo --no-build` — **51/51 passed**. `dotnet test tests/Shortener.IntegrationTests/Shortener.IntegrationTests.csproj --nologo --no-build --filter FullyQualifiedName~KafkaConfigurationTests` — **6/6 passed**.
- `dotnet publish tests/Shortener.TokenCacheProbe/Shortener.TokenCacheProbe.csproj -c Release` — **passed**. This probe drives the real MSAL client-credentials cache callbacks with a synthetic token response and no real identity provider.
- `docker compose ... config --quiet` passed for both the base local stack and the scale-out overlay. `caddy validate --config /etc/caddy/Caddyfile` passed for `deploy/Caddyfile.scaleout`.
- Final full suite in fresh disposable `schwab-tracka-check` with `compose.yaml`, `compose.local.yaml`, `compose.test.yaml`, and `deploy/network.override.example.yaml`, loopback `18091`: **26/26 integration, 25/25 unit, and 51/51 Web tests passed**. The first run exposed a test-factory setting applied after startup configuration was read; setting it at test-host creation fixed the setup, and the rerun passed.

The final test run and cleanup used the checked-in example environment, with no local secret file:

```sh
LOCAL_PORT=18091 PUBLIC_BASE_URL=http://localhost:18091/ docker compose -p schwab-tracka-check --env-file deploy/local.env.example -f compose.yaml -f compose.local.yaml -f compose.test.yaml -f deploy/network.override.example.yaml --profile test run --build --rm tests
LOCAL_PORT=18091 PUBLIC_BASE_URL=http://localhost:18091/ docker compose -p schwab-tracka-check --env-file deploy/local.env.example -f compose.yaml -f compose.local.yaml -f compose.test.yaml -f deploy/network.override.example.yaml --profile test down --volumes --remove-orphans
```

## Scale-out proof on disposable local services

Started `schwab-tracka-scale` on loopback port `18092` with `compose.yaml`, `compose.local.yaml`, `compose.scaleout.yaml`, and `deploy/network.override.example.yaml`. PostgreSQL, Redis, Kafka, Caddy, Analytics, both APIs, and both Web services were healthy. Docker DNS returned two addresses for each of `api` and `web`; Compose resolved both replica key mounts to the shared named volumes.

```sh
LOCAL_PORT=18092 PUBLIC_BASE_URL=http://localhost:18092/ docker compose -p schwab-tracka-scale --env-file deploy/local.env.example -f compose.yaml -f compose.local.yaml -f compose.scaleout.yaml -f deploy/network.override.example.yaml up --build -d
LOCAL_PORT=18092 PUBLIC_BASE_URL=http://localhost:18092/ docker compose -p schwab-tracka-scale --env-file deploy/local.env.example -f compose.yaml -f compose.local.yaml -f compose.scaleout.yaml -f deploy/network.override.example.yaml down --volumes --remove-orphans
```

- Eight public guest creates returned 201 and their redirects returned 302. With either API replica stopped in turn, a fresh guest create still returned 201 and its redirect returned 302. Each API was restarted afterward.
- A GET through Caddy issued a Web antiforgery token and sticky routing cookie. The serving Web replica was identified from Caddy's cookie hash, stopped, and the same client's token and cookie were submitted to `/account/signout` through the other replica. The response was 302, proving cross-replica antiforgery validation with the shared key ring. The stopped Web replica was restarted.
- The real MSAL distributed-cache probe acquired a synthetic client-credentials token in one Web container. A separate process in the other Web container read it from Redis without making an identity-provider request. A raw Redis scan of the token-cache entries found no plaintext synthetic access token. Live Entra sign-in and dashboard continuation were not exercised.
- Analytics `/health/live` and `/health/ready` returned 200. With Kafka stopped, `/health/ready` returned 503; after Kafka restarted it recovered to 200. The Analytics `--healthcheck` command exited 0 when healthy and 1 with an unavailable broker. PostgreSQL and Kafka were real disposable containers.

The first disposable full-suite project, the scale-out project, and the final full-suite project were each removed with their own `down --volumes --remove-orphans`. The optional network override uses one fixed subnet, so these projects ran sequentially.

## Event Hubs idempotence decision

Microsoft's [Event Hubs Kafka overview](https://learn.microsoft.com/en-us/azure/event-hubs/azure-event-hubs-apache-kafka-overview) says Event Hubs supports idempotent Kafka producers. Microsoft's [Kafka client configuration guidance](https://learn.microsoft.com/en-us/azure/event-hubs/apache-kafka-configurations) recommends `enable.idempotence=true` with `acks=all`, request timeout of at least 60 seconds, and high retries. **Track C should set `Kafka__EnableIdempotence=true` for Event Hubs.** The SASL_SSL producer configuration uses those timeout/retry values. This is documentation-based configuration, not a live Event Hubs test.

## Remaining external evidence

Azure Blob and Key Vault access, Event Hubs SASL_SSL against a real namespace, Azure ingress forwarding, a real Entra sign-in after Web replica loss, and deployment/operational acceptance remain unverified. Local checks use the revised `managed-clicks-v2` topic and existing `ShortenerApi__AllowedInternalHttpHosts` setting. The Track C image build and Azure account sections and the revised Front Door origin `AllowedHosts` values were copied unchanged. Host filtering remains configurable for Track C to supply the origin FQDNs.
