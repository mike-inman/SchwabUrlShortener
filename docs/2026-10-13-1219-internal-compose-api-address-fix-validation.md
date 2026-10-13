# Internal Compose API address fix validation — 2026-10-13 12:19 CDT

Source: `codex/workspace-authorization` at `76c270c`. During customer live acceptance, sign-in succeeded but the dashboard reported that the Shortener API address was not configured. The Web client rejected `http://api:8080` because its prior rule accepted only HTTPS or loopback HTTP. Web readiness parsed the address but did not apply that rule.

The Web now uses one address validator for its API client and readiness endpoint. HTTPS and loopback HTTP remain accepted by default. Non-loopback HTTP requires an exact hostname in `ShortenerApi:AllowedInternalHttpHosts`; no wildcard or IP address allowance is inferred. The two private Compose Web env files and `docker/web.env.example` list `api`. A rejected address produces a sanitized startup warning with a reason code and makes readiness return 503. No URL, secret, token, or cookie is logged by that warning.

Before editing, mode-0600 byte-verified backups of `web.env` and `web.customer.env` were made in the private config directory with suffix `.backup-2026-10-13-1219-internal-api-host`. Each source file gained only `ShortenerApi__AllowedInternalHttpHosts=api`; every prior line, including credentials, was preserved.

Verification:

- Focused API client tests passed 17/17, including rejection of unlisted HTTP hosts and acceptance of `http://api:8080` only when `api` is listed.
- `dotnet build SchwabUrlShortener.slnx --no-restore` passed with zero warnings and errors; full Web tests passed 26/26 and unit tests passed 25/25.
- `docker compose ... config --quiet` passed. Only the Schwab Web image and container were rebuilt and recreated with `--no-deps`; API, PostgreSQL, Redis, Kafka, Analytics, their volumes, and unrelated services were not restarted.
- The customer Web returned 200 from `/health/ready`, reported identity configured, and all six Schwab Compose services were running and healthy. The new container emitted no rejected-address warning. A separate disposable Web process configured with `http://api:8080` and no allowance returned 503 from `/health/ready`.

The PostgreSQL integration suite was not rerun during live acceptance because existing tests truncate link tables in the same guarded database used by the live customer app. Browser workspace loading, managed-link creation, sign-out/in persistence, second-customer isolation, and inspection of the first real delegated token remain pending the operator's refreshed dashboard test. A Web restart preserves the persistent Data Protection key ring, but the in-memory token cache resets; an OIDC renewal may be needed before managed API calls resume. No data table, database, or volume was deleted.
