# Schwab URL Shortener Architecture and Implementation Plan

Status: superseded for implementation sequencing by [the 2026-10-12 production-minded slice plan](./2026-10-12-1953-production-ready-slices-and-handoffs.md). Retained as historical architecture rationale.

## 1) Interview-ready answer: how to frame the assignment and the high-volume requirement

The assignment is a focused URL shortener exercise, but the job description tells us the broader engineering lens: this is not just about building a short link service in isolation; it is about showing we can design for scale, resiliency, observability, and production-minded engineering while still keeping the implementation appropriately scoped for a time-boxed exercise.

The right framing is:

> The assignment asks for a working URL shortener with create, resolve, and analytics behaviors. I will deliver that in a clean, well-structured .NET solution. At the same time, the job description emphasizes high-volume, low-latency, resilient systems, so I will design the solution with that context in mind: durable persistence, low-latency read paths, asynchronous analytics, observability, and secure access, without overbuilding the assignment into a full distributed trading platform.

This is the strongest interview position because it demonstrates both sides of the requirement:

- It satisfies the assignment directly.
- It shows awareness of the production engineering expectations described in the job description.
- It avoids pretending the assignment itself requires a full-scale Kafka/GCP financial platform.

The solution should be intentionally straightforward for the exercise, but production-minded in design. In other words, we are not building a prototype that ignores scale concerns; we are proving we know how to design for them while prioritizing assignment correctness and time-box discipline.

## 2) Production-minded architecture for the assignment

### Recommended system design

- Frontend: Blazor Server for admin/operations UI and local demo convenience.
- API: ASP.NET Core Web API for create, resolve, and analytics operations.
- Domain/Application: clear separation for URL validation, short-code generation, metrics, and business rules.
- Persistence: PostgreSQL as the durable source of truth.
- Cache: Redis for hot redirect lookups and reduced latency.
- Messaging: Kafka for click-event ingestion and asynchronous analytics processing.
- Security: Entra ID for authenticated admin access and access control.
- Deployability: Docker / Docker Compose for local environment orchestration.
- Observability: structured logs, metrics, trace IDs, health checks, and OpenTelemetry-style instrumentation.

### Architecture flow

1. Client calls the API to create a short URL.
2. API validates URL and short code.
3. API writes the mapping to PostgreSQL.
4. Optional Redis cache stores hot redirects.
5. Redirect requests hit Redis first for low-latency lookup.
6. A managed redirect commits a tenant-owned click event in PostgreSQL's outbox before responding; guest redirects create no event.
7. The Analytics worker publishes the outbox event to Kafka and consumes it to update PostgreSQL aggregates with event-ID deduplication.
8. Blazor Server dashboard reads operational metrics and health status from the application and datastore.

This architecture supports the following interview narratives:

- Low-latency hot path: Redis handles frequent resolve requests.
- Scalable analytics: Kafka decouples hot-path operations from analytics processing.
- Resilient design: async event handling reduces coupling and allows operational flexibility.
- Production readiness: PostgreSQL + telemetry + health checks provide integrity and observability.
- Enterprise alignment: Entra ID and secure admin access align with enterprise security expectations.

### Why this is the right level of sophistication

This is not a full distributed trading platform. It is a clean, defensible architecture for a URL shortener that can be explained credibly to a hiring team without turning the assignment into a new enterprise system.

The assignment is about showing disciplined engineering judgment. The job description adds the expectation that we understand the concerns behind high-volume systems. This architecture demonstrates that understanding without overengineering the deliverable.

## 3) Implementation plan with slices

### Slice 1: Project foundation and requirements alignment

Goal: establish a clean, assignment-aligned project structure and confirm the architecture direction.

Scope:
- confirm the solution structure and placeholder state in the existing projects
- define the use-case boundaries for create, resolve, analytics, and reporting
- confirm acceptance criteria for a prototype that is production-minded but not overbuilt

In scope:
- verify project layout and .NET version
- confirm folder responsibilities for Domain, Application, Infrastructure, API, Analytics, Web, and tests
- document the assignment interpretation and scale assumptions

Out of scope:
- database implementation details beyond the prototype contract
- external cloud deployment
- full distributed system design beyond the architectural narrative

Acceptance:
- [ ] project structure is understood and documented
- [ ] assignment + job-description tradeoff is clearly captured
- [ ] implementation target is narrowed to a realistic prototype scope

### Slice 2: Domain model and business rules

Goal: implement the core business rules and validation boundary.

Scope:
- URL validation rules
- short-code validation
- URL mapping aggregate and analytics fields
- business exceptions and domain invariants

In scope:
- `UrlMapping` entity
- `UrlValidation` logic
- create/resolve domain flows
- analytics data model for hit counting and metadata

Out of scope:
- external persistence and infrastructure details
- UI or API host concerns

Acceptance:
- [ ] invalid URLs are rejected
- [ ] missing scheme is normalized consistently
- [ ] short codes follow the defined rules
- [ ] analytics counts can be recorded via the domain model

### Slice 3: Application service and repository contracts

Goal: create use cases that sit between domain rules and external implementation details.

Scope:
- repository interfaces
- application service for create/resolve/analytics
- safe operation contracts with proper exceptions / result objects

In scope:
- `IUrlShortenerRepository`
- `UrlShortenerService`
- service behaviors for create, lookup, redirect, analytics retrieval

Out of scope:
- persistence implementation specifics
- UI and HTTP concerns

Acceptance:
- [ ] create flow returns a valid short code
- [ ] lookup resolves to the original URL
- [ ] analytics service exposes click counts and metadata
- [ ] application service is testable without UI or infrastructure dependencies

### Slice 4: Infrastructure and persistence

Goal: implement the concrete data access layer for the prototype and future-ready architecture.

Scope:
- in-memory repository for prototype stage
- durable repository contract for PostgreSQL-ready design
- caching strategy definition for Redis
- model mapping between domain and storage entities

In scope:
- `InMemoryUrlShortenerRepository`
- repository abstractions for persistence and analytics
- design notes for PostgreSQL and Redis integration

Out of scope:
- production database migration scripts for full deployment
- Kafka consumer implementation in this slice unless the system is intentionally started early

Acceptance:
- [ ] repository works for create/lookup/analytics flows
- [ ] hidden assumptions are documented
- [ ] future migration path to PostgreSQL is clear

### Slice 5: ASP.NET Core API and HTTP contracts

Goal: expose the product behavior via a working API.

Scope:
- endpoints for create, resolve, and analytics read
- request/response models
- validation and error handling
- health checks and telemetry hooks

In scope:
- `POST /api/urls`
- `GET /api/urls/{shortCode}`
- `GET /api/urls/{shortCode}/analytics`
- API-level validation and status responses

Out of scope:
- full authentication enforcement for all endpoints in the prototype unless the design explicitly calls for it
- admin-facing UI logic

Acceptance:
- [ ] create endpoint works end-to-end
- [ ] resolve endpoint returns the correct redirect target
- [ ] analytics endpoint returns expected hit counts
- [ ] invalid requests fail predictably with non-200 responses

### Slice 6: Blazor Server UI and operations surface

Goal: add a lightweight admin/operator UI using Blazor Server.

Scope:
- create short URL form
- quick lookup/status screen
- metrics viewing for code and hit counts
- minimal local operational feedback

In scope:
- server-side Blazor application
- direct integration to service layer or API client
- basic operational dashboard for local demo

Out of scope:
- complex multi-page dashboards
- production-grade authentication UX beyond a secure admin pattern

Acceptance:
- [ ] short URL can be created from the UI
- [ ] lookup and analytics are viewable
- [ ] UI demonstrates the local demo story clearly

### Slice 7: Analytics pipeline and event-driven processing

Goal: demonstrate the high-volume engineering story used in the job description.

Scope:
- click-event publishing to Kafka
- background worker for analytics processing
- asynchronous aggregation path
- observability hooks

In scope:
- event contract for click events
- .NET consumer/background service
- metrics for processed events and lag

Out of scope:
- enterprise Kafka cluster deployment
- full distributed tracing across external systems beyond local demo

Acceptance:
- [ ] click events are emitted from redirect path
- [ ] worker processes them without losing the core semantics
- [ ] analytics remains consistent and observable

### Slice 8: Tests, validation, and documentation

Goal: prove correctness and provide interview-ready evidence.

Scope:
- unit tests for domain rules, validation, and service behavior
- integration tests for API workflow
- README and architecture documentation
- AI engineering log / process notes

In scope:
- test coverage for create/resolve/analytics behavior
- final README summary and architecture notes
- explicit design trade-off documentation for the assignment and job description

Out of scope:
- broad end-to-end environment testing beyond the local project scope

Acceptance:
- [ ] unit tests pass
- [ ] integration tests pass
- [ ] architecture and execution instructions are present
- [ ] final documentation explains the assignment, high-volume lens, and trade-offs clearly

## 4) Recommended execution order

1. Slice 1: architecture and scope alignment
2. Slice 2: domain rules and validation
3. Slice 3: application services
4. Slice 4: infrastructure repository
5. Slice 5: API endpoints
6. Slice 6: Blazor Server UI
7. Slice 7: Kafka analytics pipeline
8. Slice 8: validation and final documentation

This order keeps the work incremental and review-friendly while preserving the production-minded story required by the job description.

## 5) Key engineering principles to preserve throughout

- Keep business rules in the domain layer.
- Keep external dependencies behind interfaces.
- Use well-scoped tests to prove behavior.
- Use AI as an accelerator while keeping human review and validation in control.
- Keep the solution simple enough to finish within the assignment, but thoughtful enough to explain the high-volume concerns in the job description.

## 6) Closing summary

The assignment is a practical shortener exercise, but the interview narrative should not ignore the high-volume expectations in the job description. The right answer is to implement a clean, production-minded .NET system that is modest in scope but thoughtful in architecture: ASP.NET Core API, PostgreSQL, Redis, Kafka, Blazor Server, Entra-based security, Docker orchestration, and observability. That demonstrates both engineering discipline and awareness of the scale, reliability, and latency concerns described in the job description without turning the assignment into an unrealistic distributed financial platform.
