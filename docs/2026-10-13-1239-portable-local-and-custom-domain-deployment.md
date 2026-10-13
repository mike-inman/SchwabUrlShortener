# Portable local and custom-domain deployment — 2026-10-13 12:39 CDT

Purpose: Run this project on another computer or on one cloud server without exposing the private Docker hostname in shared links. This guide uses Docker Compose and Caddy as the public reverse proxy. Caddy provides routing and, on the domain deployment, automatic HTTPS. It does not add a separate login layer; Entra still protects managed routes.

## What is public

The proxy is the only container with published ports. `/r/*`, `/api/*`, and `/health/*` go to the API; all other paths, including Blazor's interactive connection and Entra callbacks, go to Web. PostgreSQL, Redis, Kafka, API, and Web stay on the Compose network. The Web server calls `http://api:8080/`, while links shown to users use the independently configured `PUBLIC_BASE_URL`. API create responses retain their relative `shortUrl` path.

Both hosts accept `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host` only from the Docker `proxy` service address resolved at startup. The configured public host is the only accepted forwarded host. Restart API and Web if the proxy container is recreated with a different address. Never enable the broad `ASPNETCORE_FORWARDEDHEADERS_ENABLED` trust switch for this stack.

## Local setup on another computer

Install Docker Engine/Desktop with Compose, then copy or clone the repository. Do not copy this computer's `.env`, `.env.deploy`, or `.env.local-dev` files. In the new checkout:

```sh
cp deploy/local.env.example deploy/local.env
chmod 600 deploy/local.env
```

Edit `deploy/local.env`: replace `POSTGRES_PASSWORD` with a long random value and `API_CLIENT_ID` with your API app registration's client ID. The all-zero sample ID permits only an unverified guest-only prototype configuration; it is not an Entra registration and must not be used to claim managed sign-in. Keep the password free of semicolons because the current Npgsql connection string is composed from environment variables. Set `LOCAL_PORT` and `PUBLIC_BASE_URL` together if port 8080 is occupied, for example `8090` and `http://localhost:8090/`. The URL must describe the address opened by the browser on this computer.

```sh
docker compose --env-file deploy/local.env -f compose.yaml -f compose.local.yaml config --quiet
docker compose --env-file deploy/local.env -f compose.yaml -f compose.local.yaml up --build -d
docker compose --env-file deploy/local.env -f compose.yaml -f compose.local.yaml ps
```

The `migrate` service applies the checked-in EF migrations and must exit successfully before API startup. The first build downloads container images and .NET packages. The database and Kafka volumes persist across `down`/`up`; do not use `down -v` unless you intend to erase their data. Existing credentials in this checkout are never read by these commands because `--env-file` names the new private file explicitly.

PostgreSQL, Redis, Kafka, Caddy, API, Web, and Analytics have bounded container health checks. API readiness requires the current migrations and Redis; Analytics checks database access and the configured Kafka topic. A one-shot `kafka-init` service creates the three-partition local topic before Analytics starts. The API and Web data-protection key volumes keep local cookies stable across container restarts. Protect and back up those volumes along with the database; a cloud deployment needs an approved at-rest key-protection mechanism because local Docker volumes alone do not encrypt these keys. Application logs use JSON on standard output with service startup/shutdown and sanitized dependency events.

If Docker reports `all predefined address pools have been fully subnetted`, use the optional [network override](../deploy/network.override.example.yaml) after choosing a subnet unused by the host, VPN, and other Docker networks. Add `-f deploy/network.override.example.yaml` after `-f compose.local.yaml` in each Compose command. This machine needed that override for the isolated smoke test; no existing Docker networks were changed.

Open `http://localhost:8080/` (or your chosen port). Create a guest link and check that the UI shows `http://localhost:8080/r/<code>`. Open that link to check the redirect. The API itself is not published on a second port. Quick command checks:

```sh
curl -i http://localhost:8080/health/live
curl -i -H 'Content-Type: application/json' -d '{"originalUrl":"https://example.com/","shortCode":"local123"}' http://localhost:8080/api/urls/guest
curl -sS -D - -o /dev/null http://localhost:8080/r/local123
```

The create API response intentionally includes `"shortUrl":"/r/local123"`; the UI combines it with `PUBLIC_BASE_URL`. A repeated custom code returns conflict; choose another code on repeat.

For managed sign-in, fill `WEB_CLIENT_ID`, `WEB_CLIENT_SECRET`, and `API_SCOPE` with the actual confidential Web app and delegated API scope. Register `http://localhost:8080/signin-oidc` as the local Web redirect URI if your Entra policy permits HTTP loopback. Managed access must be verified with a real tenant and token; Compose startup does not prove it.

## Single-server domain deployment

Use a server with Docker Compose, persistent disk for the named volumes, and inbound TCP 80/443 plus UDP 443. Point the domain's DNS A/AAAA record to the server first. Copy or clone the repo, then create `deploy/cloud.env` from `deploy/cloud.env.example`, set permissions to 600, and fill every placeholder with this server's values. `PUBLIC_DOMAIN` is the host only, and `PUBLIC_BASE_URL` is the matching `https://` root URL with a trailing slash. Keep `POSTGRES_PASSWORD` private and retain the resulting volume on upgrades. Use a real API app registration; managed mode also needs the Web registration, secret, delegated scope, tenant settings, and the HTTPS redirect URI `https://<domain>/signin-oidc` registered in Entra.

```sh
docker compose --env-file deploy/cloud.env -f compose.yaml -f compose.cloud.yaml config --quiet
docker compose --env-file deploy/cloud.env -f compose.yaml -f compose.cloud.yaml up --build -d
docker compose --env-file deploy/cloud.env -f compose.yaml -f compose.cloud.yaml ps
curl -i https://<domain>/health/ready
```

Caddy requests and renews certificates after DNS and ports are correct. Its data volume must persist. The database migration runs as a one-shot container before API starts. For updates, back up the PostgreSQL volume/database, API/Web key volumes, Caddy data volume, and the private env file first, then pull the intended revision and run the same `up --build -d` command. Keep the Kafka volume as well; inspect `migrate`, `kafka-init`, `api`, `web`, and `analytics` logs if a service is unhealthy. This Compose file is a single-host prototype, not a high-availability deployment.

## Disposable real-service test run

Use a fresh, uniquely named Compose project. Add `compose.test.yaml` only for this disposable run: it initializes the guarded `Schwab_test_local` database, migrates it separately from the application database, and builds a test image from source/tests only. The integration tests refuse any other database name and truncate tables in this test database. Never attach the test override to a valued application volume. Example, after creating private `deploy/local.env` as above:

```sh
LOCAL_PORT=18080 PUBLIC_BASE_URL=http://localhost:18080/ docker compose -p schwab-slice6-check --env-file deploy/local.env -f compose.yaml -f compose.local.yaml -f compose.test.yaml --profile test up --build -d
LOCAL_PORT=18080 PUBLIC_BASE_URL=http://localhost:18080/ docker compose -p schwab-slice6-check --env-file deploy/local.env -f compose.yaml -f compose.local.yaml -f compose.test.yaml --profile test ps
LOCAL_PORT=18080 PUBLIC_BASE_URL=http://localhost:18080/ docker compose -p schwab-slice6-check --env-file deploy/local.env -f compose.yaml -f compose.local.yaml -f compose.test.yaml --profile test logs --no-color tests
```

Choose a different project name or port if either is in use. Add the optional network override file after `compose.test.yaml` when Docker's automatic subnet pool is exhausted. Confirm the `tests` container exits 0 and all three project test summaries pass. The profile also runs the normal API/Web/Analytics stack against a *different* `shortener` database. Only remove its containers and volumes when you are certain that project name belongs exclusively to this disposable run.

## Verification boundaries

`/health/live` tests API process liveness; `/health/ready` checks database migrations and Redis. A public guest create plus GET `/r/<code>` verifies the proxy route and redirect. Check the UI's displayed URL separately to confirm the public origin. Real Entra sign-in, managed API authorization, analytics processing, TLS issuance, DNS, and backups require checks against the target environment; local tests and Compose rendering cannot establish them.
