# Schwab URL Shortener Prototype

Updated: 2026-10-13 14:41 CDT

This repository contains a .NET 10 URL shortener prototype. Anonymous guest links expire after seven days. Workforce managed links use the validated token's tenant; customer managed links use application workspaces and memberships. PostgreSQL stores links, managed click outbox events, and aggregate counts. Redis caches public redirect mappings. Kafka carries managed click events to the Analytics worker. A Blazor Server app provides guest creation and a signed-in managed dashboard.

## Architecture

- Domain: `src/Shortener.Domain` — `UrlMapping`, validation helpers, and business rules.
- Application: `src/Shortener.Application` — guest/managed creation, public resolution, and tenant-scoped management contracts.
- Infrastructure: `src/Shortener.Infrastructure` — PostgreSQL repository, explicit EF Core migrations, database constraints, durable managed click capture, and disposable Redis redirect cache.
- API: `src/Shortener.Api` — anonymous guest and redirect routes plus authenticated managed routes.
- Analytics: `src/Shortener.Analytics` — PostgreSQL outbox publisher, Kafka consumer, deduplication, and tenant-aware aggregate updates.
- Web: `src/Shortener.Web` — Interactive Server UI that acquires delegated API tokens on the server and calls the API over HTTP.
- Tests: `tests/Shortener.UnitTests`, `tests/Shortener.IntegrationTests`, and `tests/Shortener.WebTests` — domain, real PostgreSQL/API, and Web regression coverage.

## Setup

1. Restore .NET dependencies:
   `dotnet restore`
2. Provide `ConnectionStrings__ShortenerDb` through your environment or local secret store. It must point to a PostgreSQL database; no connection string is checked in. The database role applying migrations needs permission to create the `citext` extension and schema objects.
3. Apply checked-in migrations explicitly before starting the API:
   `dotnet ef database update --project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --startup-project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --context ShortenerDbContext`
   Repeating this command applies only pending migrations. API startup never changes the schema.
4. Configure `AzureAd__ClientId` with the API app registration's client ID. `AzureAd:TenantId` defaults to `organizations` for Entra work/school tenants. The API fails closed at startup if either the client ID or PostgreSQL connection string is missing. Managed routes require a delegated API access token with `access_as_user`; an ID token is insufficient.
5. Provide `ConnectionStrings__Redis` through the environment or local secret store, pointing to a Redis instance accessible by the API. For a disposable loopback-only local instance, use a separate Redis container/port; do not reuse another project's Redis data. The optional `RedirectCache__Namespace` sets a safe ASCII key prefix; the default is `shortener:redirect:v1`. No Redis credential belongs in tracked settings.
6. Run `dotnet run --project src/Shortener.Api/Shortener.Api.csproj`. `GET /health/live` reports process liveness; `GET /health/ready` checks PostgreSQL access/migrations and Redis availability, returning 503 when a dependency is unavailable. Redis outage still allows redirects through PostgreSQL, while readiness reports the degraded dependency.
7. Create a Kafka topic for managed clicks and set `Kafka__BootstrapServers`, `Kafka__Topic`, and `Kafka__ConsumerGroup` for the Analytics worker. Configure the same `ConnectionStrings__ShortenerDb` as the API, then run `dotnet run --project src/Shortener.Analytics/Shortener.Analytics.csproj`. The worker needs the Slice 5 migration before startup. A managed redirect commits an outbox row before returning 302; its analytics count updates after Kafka processing. If PostgreSQL capture fails, that redirect returns an error. Kafka downtime leaves the outbox queued for bounded retries; terminal rows require operator inspection and replay.
8. HTTP contract:
   - `POST /api/urls/guest` with `{ "originalUrl": "https://example.com/articles/123", "shortCode": "demo1" }` creates an anonymous link with `expiresAtUtc` exactly seven days after creation. `shortCode` is optional.
   - `POST /api/urls/managed` accepts the same body and requires a valid Entra bearer access token. Tenant ownership comes from the validated token's `tid` claim.
   - `GET /r/{shortCode}` anonymously redirects active guest or managed links. Expired guest links return 404.
   - `GET /api/urls/managed/{shortCode}` and `GET /api/urls/managed/{shortCode}/analytics` require authentication and return only the caller's tenant-owned link.
   - `GET /api/urls/managed/analytics` returns only the caller's tenant-owned links. Guest links have no management or analytics surface.

Create responses contain a relative `shortUrl` such as `/r/demo1`; the serving domain is chosen by deployment. Short codes are unique across both link modes.

For the Web app, configure `ShortenerApi:BaseUrl` with the server-to-server API origin and `PublicLinks:BaseUrl` with the public origin used in generated links. The latter must be HTTPS or loopback HTTP and must end at the origin root. Non-loopback HTTP for the internal API requires an exact DNS host in `ShortenerApi:AllowedInternalHttpHosts`; use it only on a private container network. Configure `ShortenerApi:Scope` with the API's delegated scope. Configure `AzureAd:Instance`, `AzureAd:TenantId`, `AzureAd:ClientId`, and `AzureAd:ClientSecret` for the Web app registration in its separate local secret store or environment, then run `dotnet run --project src/Shortener.Web/Shortener.Web.csproj`. The Web app keeps tokens and the client secret on the server. Guest creation remains available without Web sign-in configuration. The app registration must allow the Web callback URL used locally.

For a portable Docker installation or a single-server custom-domain deployment, use the dated [deployment guide](docs/2026-10-13-1239-portable-local-and-custom-domain-deployment.md). It provides private sample environment files, explicit migrations, public proxy routes, and verification commands. Existing local `.env` files are not used or changed by that workflow.

The [Slice 6 validation record](docs/2026-10-13-1310-slice-6-compose-observability-validation.md) separates the real local Compose results from live Entra and cloud acceptance. Its disposable test profile runs the guarded full solution suite against real PostgreSQL, Redis, and Kafka.

The [2026-10-13 Slice 7 scale validation closeout](docs/2026-10-13-1441-slice-7-scale-validation-closeout.md) records an isolated 83/83 full-suite run with real PostgreSQL, Redis, and Kafka for the integration tests, a capped loopback load sample, and bounded dependency interruption and recovery checks. Use `scripts/load-local.py` only with a fresh disposable local Compose project and `--local-test-only`; it caps each case at 500 requests and concurrency at 8. The sample is a local baseline, not a production capacity target or live identity check. Acceptance still needs an agreed traffic profile, latency/error/availability objectives, analytics freshness and retention, recovery requirements, and deployment environment.

## Existing local Docker Compose bundle

The checked-in `compose.existing.yaml` preserves the earlier local bundle and its external volume names. Use it for an existing installation; the portable `compose.yaml` has different service and volume names and must not be pointed at existing data. The existing bundle groups PostgreSQL, Redis, Kafka, API, Web, and Analytics under `schwab-url-shortener`. It uses a configurable Schwab-only subnet (`SCHWAB_SUBNET`, default `10.251.53.0/24`) for service-name DNS; inspect existing Docker networks and host routes before using that default on another machine. Host ports remain loopback-only: PostgreSQL `63651`, Redis `63652`, Kafka `63653`, API `63654`, Web HTTP `63655`, and optional Web HTTPS `63656`. Kafka advertises `localhost:63653` to host clients and `kafka:9092` to app containers.

1. Copy the six `docker/*.env.example` files to private files outside the repository, set mode `0600`, and replace placeholders with your own credentials and absolute paths. `compose.env` supplies the existing volume names and path to the PostgreSQL password environment file; `apps.env` supplies the paths to separate API, Web, and Analytics environment files. Existing installations must keep their original volume identifiers and PostgreSQL password. Never commit these files. The Web needs `ShortenerApi__BaseUrl`, `ShortenerApi__AllowedInternalHttpHosts=api`, and `PublicLinks__BaseUrl` for guest links and readiness; set the public origin to `http://localhost:63655/` for local HTTP or `https://localhost:63656/` for the optional HTTPS endpoint. Managed sign-in requires all real Web Entra settings and a registered delegated API scope. The API always requires a real `AzureAd__ClientId` and database connection at startup.
2. On a **new** installation, create the five external data volumes named in `compose.env` with `docker volume create <volume-name>` before starting. On an existing installation, verify the names with `docker inspect` and reuse them. Do not replace or prune volumes. Create the PostgreSQL password file only for a new installation; the API and Analytics connection strings must use that same password and the internal host `postgres`. After PostgreSQL first starts on a new installation, create the disposable test database with `docker exec schwab-url-shortener-postgres createdb -U schwab_local Schwab_test_local`; do not run this on an existing database.
3. From the repo root, set `SCHWAB_PRIVATE_DIR` to the directory containing your private `compose.env` and `apps.env`. Use the following explicit commands; they do not migrate an application database automatically:

   ```bash
    docker compose --env-file "$SCHWAB_PRIVATE_DIR/compose.env" --env-file "$SCHWAB_PRIVATE_DIR/apps.env" -f compose.existing.yaml config --quiet
    docker compose --env-file "$SCHWAB_PRIVATE_DIR/compose.env" --env-file "$SCHWAB_PRIVATE_DIR/apps.env" -f compose.existing.yaml --profile migration build api web analytics migrate
    docker compose --env-file "$SCHWAB_PRIVATE_DIR/compose.env" --env-file "$SCHWAB_PRIVATE_DIR/apps.env" -f compose.existing.yaml up -d postgres redis kafka
    docker compose --env-file "$SCHWAB_PRIVATE_DIR/compose.env" --env-file "$SCHWAB_PRIVATE_DIR/apps.env" -f compose.existing.yaml --profile migration run --rm --no-deps migrate
   docker exec schwab-url-shortener-kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --create --if-not-exists --topic schwab-managed-clicks-v2 --partitions 1 --replication-factor 1
    docker compose --env-file "$SCHWAB_PRIVATE_DIR/compose.env" --env-file "$SCHWAB_PRIVATE_DIR/apps.env" -f compose.existing.yaml up -d api web analytics
   ```

   The migration container accepts `Schwab_test_local` by default and is idempotent. For `Schwab_local` or another application database, first create and verify a separate backup, review the target connection string privately, then set `SCHWAB_APP_MIGRATION_APPROVED=yes` in the Compose invocation. This explicit action is never part of API startup or ordinary `up`. Use the topic name configured in `analytics.env` when creating the Kafka topic.
4. Check `http://localhost:63654/health/ready`, `http://localhost:63655/health/ready`, and the Compose health states. The Web guest page is `http://localhost:63655/`; the API guest create endpoint is `http://localhost:63654/api/urls/guest`. Analytics is healthy only when its configured PostgreSQL database and Kafka topic respond. API readiness checks migrations and Redis. Container health does not prove managed login, customer registration, or production operations.
5. For real local OIDC callbacks, trust the local development certificate with `dotnet dev-certs https --trust`, export it to a private PFX using `dotnet dev-certs https -ep <private-pfx-path> -p <private-password>`, store that path and password only in `apps.env`, and use `-f compose.existing.yaml -f compose.https.yaml` on the Web `up` command. Keep the PFX mode `0600`. The HTTPS page is `https://localhost:63656/`, and the Web app registration must allow exactly `https://localhost:63656/signin-oidc` (and the matching signed-out callback if used). Configure the real Web client ID, tenant, client secret, and delegated scope in the private `web.env`; no runtime test identity is installed. Confirm the provider redirect and complete sign-in with the operator before claiming managed access.

For a safe local stop, run `docker compose ... stop api web analytics` or `docker compose ... stop` with the same files; neither removes volumes. Back up each database to a private file before any application migration, for example `docker exec schwab-url-shortener-postgres pg_dump -U schwab_local -Fc -d Schwab_local > /private/path/Schwab_local.dump`, and verify that file before proceeding. Restore only to a separately chosen target using `pg_restore` after review; the normal startup commands never restore or truncate data. The Web Data Protection key ring persists in the dedicated `schwab-url-shortener-web-keys` volume across container recreation; protect this local volume and the private certificate. See [the dated Compose validation note](docs/2026-10-13-0942-full-local-compose-apps-validation.md) for this host's results and remaining identity gates.

## Scenarios

### Greenfield
A new repo starts empty, so we build a clean shortener from scratch with a clear layer boundary and minimal infrastructure.

### Brownfield
The solution already contains placeholder .NET scaffolding; the project is upgraded from a template to a functional prototype without large-scale refactoring.

### Ambiguous
The exact assignment details are interpreted through the governing requirement document and narrow prototypes focus on what is likely expected for evaluation: create, resolve, and analytics behaviors.

## Validation

Run the solution test suite against a real, migrated PostgreSQL test database configured via `ConnectionStrings__ShortenerDb`:

`dotnet test SchwabUrlShortener.slnx --nologo`

The current tests cover guest expiry, global code uniqueness and races, anonymous redirects, tenant isolation, authorization, restart persistence, managed click capture, Kafka publish retry/replay/deduplication, cancellation, database constraints, Redis hit/miss/outage behavior, readiness failure, and Web components/API-client behavior. API tests use a test-only authentication handler. The integration tests clear only the guarded disposable database `Schwab_test_local`; never point them at the app database. Set `ConnectionStrings__Redis` and `Kafka__BootstrapServers` to isolated real instances for those tests. See [the Slice 5 validation note](docs/2026-10-13-0542-slice-5-kafka-analytics-validation.md) for delivery and operator details.

## Notes

- PostgreSQL is the only normal runtime repository. There is no in-memory fallback on connection failure.
- Redis stores only redirect fields, with a maximum one-hour TTL. Guest TTL is shorter when required by its absolute seven-day expiry. Managed redirects capture a PostgreSQL outbox event on every redirect, including cache hits. Aggregate counts are eventually updated by the Kafka consumer.
- The short codes are validated to be 4-12 characters with letters and numbers only.
- URLs are normalized to absolute `http` or `https` addresses when missing a scheme.
