# Portable local and custom-domain deployment plan — 2026-10-13 12:39 CDT

Purpose: Make short links use a configured public origin while the Web app can call an API on a private Docker network. Package the stack for a second computer and for a domain-backed server.

Scope: Public URL and internal API configuration, trusted proxy headers, Docker images and Compose files, example configuration, deployment instructions, and focused tests. Preserve existing untracked files and all local `.env` files. No commit or deployment in this task.

Tasks:
1. Separate public redirect origin from the server-to-server API address and validate both.
2. Configure forwarded headers only from the named reverse proxy; route `/r/` and `/api/` to the API and other requests to Web.
3. Add portable container definitions, explicit database migration step, local HTTP and domain HTTPS proxy variants, and safe sample variables.
4. Verify builds, focused tests, Compose rendering, and any runnable smoke path available locally.

Acceptance: Created links show the configured local or domain origin and never `api:8080`; public `/r/{code}` resolves through the proxy; forwarded client scheme/host are accepted only from the configured proxy; another computer can supply its own values without copying this machine's secrets.

Outcome (2026-10-13 12:48 CDT): Implemented separate internal/public origins, trusted proxy handling, a one-shot migration mode, container images, local/cloud proxy routes, sample env files, and deployment instructions. The solution build completed with zero warnings; Web tests passed 19/19 and unit tests passed 24/24. Both Compose variants rendered, and API/Web/Analytics images built. An isolated Compose stack on port 18080 used the optional subnet override because this host's Docker automatic address pools were exhausted. Its Web page, liveness, and readiness returned 200; guest creation returned 201 with `/r/local123`; and the public redirect returned 302 to `https://example.com/`. All runtime services stayed up; the isolated containers, network, and volumes were removed afterward. Full integration tests were not run because the required guarded test PostgreSQL, Redis, and Kafka connection variables were absent. Real Entra sign-in, public DNS, TLS issuance, and a cloud deployment remain unverified. No commit was made.
