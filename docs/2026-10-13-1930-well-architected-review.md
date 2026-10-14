# Well-Architected review — 2026-10-13 19:30 CDT

## Purpose

Review the whole system (application, containers, Azure environment, and delivery) against the Azure Well-Architected Framework pillars plus application security and delivery practices, so best practices are applied deliberately rather than as individual fixes. Scope: `origin/main` at `66b8318`, Track A at `a577f62`, Track B at `dc3f51b`, and the contract revised through 19:20 CDT. This review changes no code or Azure resource; it assigns each gap to a track.

Guiding constraint (from the contract's Design principles): low expected load, elastic scale-out with automatic retraction, cost-conscious without sacrificing security or performance, every lean choice naming its upgrade, and scaling that is demonstrated.

Priority:
- **P0** — do before evaluation; visible to a reviewer or a real risk; low cost.
- **P1** — strongly recommended if time allows.
- **P2** — document as a production step only.

## Already in place

| Pillar | Practice | Where |
|---|---|---|
| Security | Entra sign-in with delegated scope; tenant and workspace isolation enforced in the API from validated claims | Slices 1, workspace authorization |
| Security | Every runtime secret in Key Vault; one managed identity per service with per-secret roles | Contract 19:15, 19:20 |
| Security | PostgreSQL with no public endpoint; Key Vault, Storage, Event Hubs restricted to the app subnet; TLS everywhere | Contract 19:00 |
| Security | No vulnerable NuGet packages (`dotnet list package --vulnerable`, 2026-10-13 19:25) | Verified in this review |
| Reliability | Durable outbox, idempotent consumer with event-ID dedup, dead-letter handling | Slice 5 |
| Reliability | Real readiness and liveness probes on every service | Slice 6, Track A |
| Reliability | Shared Data Protection keys and distributed token cache, so any replica can be replaced | Track A |
| Performance | Redis redirect cache; autoscale on HTTP and Kafka lag; connection budget sized to the database limit | Slice 4, contract |
| Cost | Consumption-billed apps that retract to 1 replica; fixed small database and Event Hubs with alerts | Contract 19:10 |
| Operations | Infrastructure as code (Bicep, `azd`); images by digest; structured JSON logs; alert rules | Track C, contract |

## Gaps and actions

### Security

| # | Gap | Why it matters | Action | Cost | Priority | Owner |
|---|---|---|---|---|---|---|
| S1 | Containers run as `root` (no `USER` in `Dockerfile`) | A compromised process gets root inside the container; reviewers check this | Implemented in Track D; disposable API, Web, and Analytics containers ran as UID 1654. Existing key volumes need a backed-up, one-time ownership change before non-root startup. Cloud deployment pending. | Free | P0 | Track D — local verified |
| S2 | No rate limiting on public endpoints | Guest link creation is anonymous: abuse, spam links, and cost-driven denial of service | Implemented configurable guest, redirect, and managed policies in Track D; disposable HTTP and tests verify 429/`Retry-After`. Cloud load validation pending. | Free | P0 | Track D — local verified |
| S3 | Only HSTS is set; no other security headers | Clickjacking, MIME sniffing, and script injection defenses missing | Implemented Web/API headers and nonce-based Web CSP in Track D; local WebSocket handshake and HTTP checks passed. Live signed-in dashboard and Entra flow remain unverified. | Free | P0 | Track D — partial local verification |
| S4 | Web uses an Entra client secret | Secrets expire and can leak; Microsoft recommends no secrets where possible | Use the Web app's managed identity as a federated credential on the SchwabUrlShortener Web registration (`SignedAssertionFromManagedIdentity` in Microsoft.Identity.Web), so Azure needs no client secret. Keep the secret only for local development. Verify support for this multitenant registration before adopting; if unsupported, keep the Key Vault secret and record why. | Free | P1 | Track C (config) + Michael (registration) |
| S5 | Storage, Event Hubs, PostgreSQL minimum TLS and storage public-blob settings not explicit | Defaults vary by resource and API version | Set minimum TLS 1.2 everywhere, secure transfer required, public blob access disabled, ACR anonymous pull disabled. | Free | P0 | Track C |
| S6 | No posture assessment | Reviewers ask "how do you know it is secure?" | Enable Microsoft Defender for Cloud foundational CSPM (free tier) on the subscription, record the secure score and remediate free recommendations for this resource group. | Free | P1 | Track C |
| S7 | Open redirector risk | Any shortener can be used to disguise phishing links | Document current URL validation and the production step (URL reputation check, abuse reporting, link takedown). | Free | P2 | Docs |
| S8 | Passwords for PostgreSQL and Event Hubs | Secrets remain, though only in Key Vault | Entra authentication to both (code change). | Free | P2 | Docs |

### Reliability

| # | Gap | Action | Cost | Priority | Owner |
|---|---|---|---|---|---|
| R1 | Nothing prevents accidental deletion of the environment | `CanNotDelete` resource lock on `rg-schwab-url-shortener`, documented (removing it is a deliberate step) | Free | P0 | Track C |
| R2 | No documented recovery | Runbook: restore PostgreSQL to a point in time, redeploy from Bicep, re-run migrations, recreate Redis (cache only), rotate secrets | Free | P1 | Track C docs |
| R3 | Web runs in single revision mode (required for sticky sessions), so no traffic-split rollback | Document rollback by redeploying the previous image digest; record the previous digests in the deployment record | Free | P1 | Track C docs |
| R4 | Single zone, single region | Production step with cost: zone-redundant environment and PostgreSQL high availability | — | P2 | Docs |

### Operational excellence

| # | Gap | Action | Cost | Priority | Owner |
|---|---|---|---|---|---|
| O1 | No CI/CD pipeline | GitHub Actions in `mike-inman/SchwabUrlShortener`: on pull request, build, unit and Web tests, the Compose integration suite, `dotnet list package --vulnerable`, Bicep lint; on merge to `main`, deploy with `azd` using GitHub OIDC workload identity federation (no stored Azure secret), with a manual approval environment gate | Free (GitHub-hosted minutes) | P0 | Track F |
| O2 | No resource tags | Tag every resource: `app=schwab-url-shortener`, `env=prod`, `owner=social@mediaraq.com`, `costCenter=assessment`, `managedBy=bicep` | Free | P0 | Track C |
| O3 | No dependency update process | Dependabot for NuGet, GitHub Actions, and Docker base images | Free | P1 | Track F |
| O4 | Secret rotation undocumented | Runbook for rotating the PostgreSQL password, Redis password, Event Hubs key, and Web secret (Key Vault new version; Container Apps picks up the latest version on restart) | Free | P1 | Track C docs |

### Performance efficiency and observability

| # | Gap | Action | Cost | Priority | Owner |
|---|---|---|---|---|---|
| P1 | Metrics and traces are defined in code but not exported; Application Insights is configured with nothing sending to it; Slice 7 recorded per-event trace correlation as unverified | Implemented conditional Azure Monitor export and W3C context through outbox/Kafka in Track D. Disposable integration trace verified; actual Application Insights ingestion and one live Azure trace remain unverified. | Within the 1 GB/day log cap | P0 | Track D — local verified, Azure pending |
| P2 | No single view for evaluators | An Azure Monitor workbook: request rate, latency, errors, replica counts per app, consumer lag, outbox age, database CPU and connections | Free | P1 | Track C |
| P3 | Sampling not configured | Added configurable sampling, default 1.0 locally and recommended 0.25 in Azure pending ingestion review against the daily cap. | Free | P0 | Track D — configured, Azure pending |

### Cost optimization

No new gaps beyond the contract. Add to `infra/README.md`: a cost table per resource (calculator-based), what scales with load versus fixed, and the monthly budget alert.

## Track assignments

| Track | Status | New scope from this review |
|---|---|---|
| A — scale-out readiness | Fixing `appsettings.json` defaults, then merge | None |
| B — live push | Fixing throttle, then rebase and merge | None |
| C — Azure environment | P1 discovery | S5, S6, R1, R2, R3, O2, O4, P2, and the infrastructure half of S4 |
| D — application hardening (new) | Local implementation and verification complete; Azure and signed-in browser acceptance pending | S1, S2, S3, P1, P3 |
| F — CI/CD (new) | Not started | O1, O3; can start now, touches only `.github/**` |

## Decisions recorded

- Defender for Cloud: foundational CSPM only (free). Paid Defender plans are a production step.
- WAF: production step (needs Front Door Premium).
- Rate limits: initial values are set by Track D from expected low load and recorded; they are configuration, not code constants.
