# AI Engineering Log

This log captures the engineering process used to build the Schwab URL shortener in a way that demonstrates disciplined, AI-assisted development.

## Direction update — 2026-10-12

The initial in-memory prototype direction was superseded by a production-minded implementation plan. The intended product distinguishes anonymous guest links (redirect-only, seven-day expiry, no analytics) from signed-in, Entra-tenant-owned managed links (persistent management and analytics). PostgreSQL, Redis, Kafka, Blazor Server, and Docker Compose are planned implementation slices, not completed capabilities. See [the current slice plan](./2026-10-12-1953-production-ready-slices-and-handoffs.md).

## 1) Intent

Build a clean .NET URL shortener prototype that satisfies the assignment and explains how the architecture aligns with the job description’s emphasis on high-volume, low-latency, resilient systems.

## 2) AI Instructions

- Build a URL shortener in .NET using clean layered architecture.
- Keep the solution focused on the assignment deliverables.
- Use production-minded design principles without overbuilding.
- Explicitly acknowledge that the assignment is prototype-scale while the job description indicates broader scale expectations.
- Prioritize correctness, validation, and clear documentation.
- Keep the implementation explainable and maintainable.

## 3) AI Output

Generated architecture direction and implementation plan:

- ASP.NET Core API for create/resolve/analytics endpoints
- Domain/Application/Infrastructure separation
- Blazor Server admin UI for local demonstrations
- PostgreSQL for durable storage
- Redis for hot redirect lookups
- Kafka for asynchronous analytics processing
- Entra ID for secure admin access
- Docker Compose for local orchestration
- Observability hooks for latency, errors, and health

## 4) Human Review

The generated direction was accepted because it:

- satisfies the assignment scope
- aligns with the job description
- preserves a realistic implementation size
- demonstrates production-aware engineering judgment without unnecessary complexity

The main human decision was to keep the solution intentionally straightforward for the assignment while preserving architecture patterns that support scaling and operational resilience.

## 5) Validation

Planned and executed validation work includes:

- build verification for the .NET solution
- unit tests for validation and business rules
- API integration tests for create/resolve/analytics workflow
- review of architecture and documentation quality
- confirmation that the design explains both assignment scope and future high-volume concerns

## 6) Engineering Judgment Summary

This project should not be framed as a full financial trading platform. Instead, it should be framed as a prototype URL shortener that demonstrates disciplined software engineering, strong architecture, and AI-assisted delivery while also acknowledging that the broader job description expects an understanding of scale, caching, observability, and resilient processing patterns.

This is the right balance between assignment fidelity and job-description alignment.
