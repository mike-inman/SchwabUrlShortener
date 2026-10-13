# Ziplio.io: Branding Guidance

**Date:** 2026-10-13  
**Status:** Provisional concept; trademark clearance pending  
**Project:** SchwabUrlShortener / potential public Ziplio product

## 1. Executive recommendation

Use **Ziplio** as the working brand for the URL shortener, retaining `SchwabUrlShortener` as the engineering solution/repository name for assignment traceability. Do not treat the brand as legally cleared or incur substantial launch/branding costs until appropriate checks are complete.

**Positioning:** A premium, modern link-management platform for creating short links, managing destinations, and understanding engagement.

**Preferred tagline:** **Short links. Smart connections.**

## 2. Preliminary naming and trademark-risk guidance

A preliminary investigation identified another software project publicly described under the exact name **Ziplio**, reportedly associated with file and document utilities (image compression, PDF processing, conversions, ZIP exports). This is a potential competing-brand/search-discoverability concern. The existence, current commercial activity, geographic scope and rights of that project require independent verification.

No definitive U.S. trademark clearance was established. The absence of a verified registration is **not** evidence that the name is free to use. Unregistered rights and confusingly similar marks may matter.

| Risk area | Preliminary assessment | Rationale |
| --- | --- | --- |
| Exact-name software use | Elevated | Another software utility reportedly uses Ziplio |
| Adjacent product categories | Moderate | File utilities and link utilities are distinguishable but related software services |
| U.S. trademark status | Unknown | No conclusive live clearance |
| International rights | Unknown | Comprehensive international check not completed |
| Domain availability | User reported available | Registration remains subject to registrar confirmation |
| Search distinctiveness | Moderate concern | Another product with the exact name may affect discovery |

### Clearance checklist

1. Search the [USPTO Trademark Search](https://tmsearch.uspto.gov/) for `ZIPLIO` and reasonably similar marks (for example ZIPLIA, ZIPLO, ZIPLY); review relevant goods/services, owners and statuses.
2. Review [WIPO Global Brand Database](https://branddb.wipo.int/) and appropriate country-level registries if operating internationally.
3. Investigate the other project's active product pages, first-use claims, jurisdiction, commercial activity and market overlap.
4. Check company-name, search, app-store, social-handle and domain collisions.
5. Before material commercial investment, seek an appropriate trademark attorney's clearance opinion.
6. Only after clearance, secure domain and handles and finalize the production brand.

This document is branding and product guidance, **not legal advice or a trademark clearance opinion**.

## 3. Brand identity

**Personality:** Confident, precise, approachable and technically sophisticated. Professional enough for business/financial-services audiences, friendly enough for a consumer-facing product.

**Visual concept:** A stylized **Z** suggests a shortened route between endpoints; a small accent dot suggests the destination. This is a concept for design exploration, not a final logo.

### Color tokens

| Token | Hex | Suggested purpose |
| --- | --- | --- |
| Midnight | `#0B1426` | Primary dark page background |
| Slate | `#182941` | Panels, forms, dashboard surfaces |
| Mint | `#59E6CF` | Primary accent, focused actions, link markers |
| Amber | `#F2BC68` | Sparing secondary highlights |
| Cloud | `#F4F8FF` | Primary light text and light-theme base |

**Typography:** Inter for UI copy, data and controls; Manrope for headings and brand treatment, subject to font licensing and performance review.

**Design direction:** Restrained dark SaaS dashboard; generous spacing, high-contrast text, clear data hierarchy and mint-accented primary actions. Ensure accessible contrast, focus states, reduced-motion behavior and mobile responsiveness.

## 4. Product and site naming

| Surface | Proposed name |
| --- | --- |
| Public brand/product | Ziplio |
| Primary domain | `ziplio.io` |
| Authenticated dashboard | Ziplio Studio |
| Analytics | Ziplio Insights |
| Link-management section | My Links |
| API and documentation | Ziplio Developers |
| Admin/preferences | Settings |
| Browser page title | Ziplio · Link Management |
| Engineering solution/repository | `SchwabUrlShortener` (retain) |

### Domain and route plan

- `ziplio.io` — landing page and public link redirects (for example `ziplio.io/abc123`). Reserve routing namespaces to prevent collision between marketing pages and short codes.
- `app.ziplio.io` — authenticated Blazor dashboard.
- `api.ziplio.io` — management API protected by Entra-issued access tokens as appropriate.
- Public redirect routes are anonymous, while link creation, management and private analytics require authorization.

Domain layout remains a proposal until routing, DNS, certificates and brand rights are verified. Optional additional short domains can be supported later.

## 5. Homepage copy and UX

**Hero heading:** Every link deserves a smarter destination.

**Subheading:** Create short links, track engagement, and manage your digital connections from one powerful dashboard.

**Primary action:** Shorten your link (for a private initial rollout, require sign-in before creation).

**Supporting statement:** Secure links · Real-time insights · Built for reliability.

**Hero example:** `ziplio.io/launch` with a prominent Copy action. Do not imply a link exists until it has been created.

### Core screens

1. **Dashboard:** Link and click summary, recent activity.
2. **Create Link:** URL input, optional alias and expiration, validation and copy result.
3. **My Links:** Search, filter, status, edit/deactivate where authorized.
4. **Insights:** Clicks over time and per-link analytics, with clear data freshness and privacy considerations.

## 6. Taglines to evaluate

- **Short links. Smart connections.** *(preferred)*
- **Make every link count.** *(analytics-oriented)*
- **Shorten smarter. Share faster.** *(consumer-oriented)*
- **Your links. Your insights.** *(business-oriented)*
- **Less URL. More impact.** *(playful)*

## 7. Implementation and governance notes

- Preserve the existing engineering repository and domain model. Treat product name, UI strings, route hosts and brand assets as configurable and replaceable until legal clearance.
- Do not let branding exploration jeopardize the interview deliverable: working prototype, tests, architecture, three engineering scenarios, and AI-assisted development traceability come first.
- Current identity design is **provisional**, not an approved production brand or final artwork.
- Document any ultimate brand decision in an ADR and separately verify trademark/domain status before publicly launching.

## 8. Immediate next steps

1. Complete brand/trademark clearance.
2. Confirm domain purchase, DNS ownership and availability.
3. Approve logo/wordmark and finalize accessible design tokens.
4. Build a reusable Blazor component/theme library and landing page around the validated brand.
5. Configure Entra sign-in for Studio and anonymous short-link resolution separately.

---

*This guidance captures the branding concept discussed on 2026-10-13. Risk labels are preliminary and should not be interpreted as authoritative legal findings.*
