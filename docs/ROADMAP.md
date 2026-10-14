# Ziplio Product Roadmap

**Updated:** 2026-10-14 · **Owner:** Michael Inman

This is what's planned after the current release. Each item is designed as an extension of the existing architecture, not a rewrite. The [Architecture Addendum](ARCHITECTURE-ADDENDUM.md) explains the pieces each one builds on.

## Shipped

- Guest links (7-day expiry) and permanent managed links in customer workspaces (Entra External ID)
- Redis-cached redirects; click analytics through a transactional outbox and Kafka/Event Hubs
- Live dashboard and per-link **Insights** (clicks over time, estimated visitors, busiest hours, referrers, devices), with privacy-preserving visitor hashing
- Autoscaling Azure deployment defined in Bicep; CI with security gates; an approval-gated deploy pipeline

## Next

### QR codes for every link

A downloadable QR code next to every managed link (and, optionally, the guest result).

- **How it works:** the server generates SVG and PNG with an open-source .NET library, with no third-party QR service. The image depends only on the short URL, so it's cached and costs almost nothing to serve.
- **Branding:** optional Ziplio or workspace logo in the center, with error correction high enough for the code to stay scannable.
- **Scan tracking:** QR codes encode a source marker (for example `/r/{code}?src=qr`). The redirect records it with the click, so Insights can split **scans versus clicks**. The marker is an allow-listed value, so arbitrary query strings are never stored.
- **Builds on:** the existing redirect path, click events and Insights aggregates. Adding a source column is a small schema change that stays compatible with old images.

### MCP server: Ziplio as a tool for AI agents

An MCP server so assistants and agents (Claude, IDE copilots, other people's agents) can create and inspect short links directly.

- **Tools:** `shorten_url`, `get_link`, `list_links`, `get_link_insights`. Create and read only at first. Any future edit or delete tool asks the user to confirm in the client.
- **Architecture:** a thin adapter over the **existing API**, in its own small container app behind the same route (`/mcp`), so business rules, authorization, rate limits and the outbox stay in one place. It scales separately, so agent traffic can't starve the website.
- **Phased access:**
  1. **Guest tools:** anonymous shortening with the same 7-day expiry and rate limits as guests.
  2. **Workspace API keys:** users create a key in the dashboard. **Azure API Management** is adopted here to handle keys, per-key quotas, revocation and usage reporting ([decision #18](ARCHITECTURE-ADDENDUM.md#16-architecture-decision-log)).
  3. **OAuth sign-in** for MCP clients that support it, issued by Entra External ID.
- **Safety:** tools return only Ziplio's own fields (code, URL, counts), never content fetched from destination pages, to limit prompt-injection exposure. Agent-created links and clicks are tagged with their source for Insights. URL screening becomes mandatory before guest tools go public.

## Later

### Ziplio Assistant: an AI assistant in the dashboard

A conversational assistant for link owners, for example: *"Which links did best this week?"*, *"Why did clicks drop on Tuesday?"*, *"Create a link for this campaign and give me the QR code."*

- **Architecture:** the assistant uses **the same MCP tools** as external agents. One tool layer serves both the in-app assistant and other AI clients, and the assistant can never do anything the signed-in user can't do through the API.
- **Grounded answers:** answers come from Insights aggregates returned by tools, not model memory. Numbers are shown with their source range.
- **Safety and privacy:** read-only by default; any create or change needs a confirmation in the UI. Only aggregates go to the model, never raw click data or visitor hashes. Destination page content is treated as untrusted. Per-user rate limits and spend caps apply, and the model provider is configurable.
- **Builds on:** MCP tools, workspace authorization, Insights, and the existing Blazor live channel for streaming replies.

### Platform

| Item | Why |
|---|---|
| Front Door with WAF in front of the Container Apps route | Edge protection, bot filtering, global caching of redirects |
| URL reputation screening at create time | Abuse prevention; required before public MCP guest tools |
| Custom domains per workspace (`go.yourbrand.com`) | A common customer request; uses the existing route and managed certificates |
| Link editing, expiry control and deletion | Workspace management basics |
| Azure load test showing scale-out and scale-in | Turns "designed to scale" into measured numbers |
| PostgreSQL zone-redundant HA, then a read replica for analytics | Availability; keeps analytics reads off the primary |
| Staged rollouts through Container Apps revisions with traffic splitting | Safer releases without a separate staging environment |

## Principles that apply to every item

- Extend the API; don't bypass it. Every new surface (QR, MCP, assistant) goes through the same authorization and rate limits.
- Privacy by default: aggregates over raw data, no third-party tracking.
- Cost-conscious: scale to demand, and adopt a paid service (APIM, Front Door) only when a feature needs it.
- Ship each item with the same discipline as the core: a dated plan, tests against real services, validation evidence, and a gated deploy.
