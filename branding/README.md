# Ziplio brand kit

Provisional brand assets for the URL shortener. The engineering name stays
`SchwabUrlShortener`. See `docs/2026-10-13-ziplio-branding-guidance.md` for
naming and trademark status.

## Direction

A night-sky dashboard. Midnight ground with a faint dot grid, slate panels
with a mint glow rim, mint for every action, and amber kept for one meaning:
the destination. The logo is a Z-shaped route from an open origin ring to an
amber destination dot. The same dot marks the active nav item, short-link
chips, and created links.

Type:

- **Bricolage Grotesque** for headings and the wordmark. It has more
  personality than a plain grotesque without being a novelty face.
- **Instrument Sans** for interface text.
- **JetBrains Mono** for URLs, short codes, and counts, since those are the
  product's raw material.

All three are on Google Fonts under the SIL Open Font License.

## Contents

| Path | Purpose |
| --- | --- |
| `tokens.css` | Brand values as CSS variables; source of truth |
| `app/app.css` | Dark Bootstrap 5.3 theme; replaces `Shortener.Web/wwwroot/app.css` |
| `logo/ziplio-mark.svg` | Bare route mark for dark surfaces (nav) |
| `logo/ziplio-app-icon.svg` | Mark on a tile; app icon and avatar |
| `logo/favicon.svg` | Browser tab icon |
| `entra/ziplio-entra.css` | Custom CSS for the External ID hosted pages |
| `entra/render/` | HTML/SVG sources for the Entra images |
| `entra/render.sh` | Renders the Entra PNG/JPEG images with Chrome on macOS |
| `entra/out/` | Rendered images to upload |
| `entra/portal-setup.md` | Exact portal steps for the sign-in pages |

App integration is in
`docs/2026-10-13-1240-ziplio-branding-integration-handoff.md`.

## Signature details

- **Hero moment (home page):** an example long URL gets struck through, a
  short Z route draws, and a `ziplio.io/launch` chip lights up. It plays once
  and is skipped when the visitor prefers reduced motion.
- **Live pulse:** "Updates automatically" on the dashboard has a breathing
  mint dot, tying the visual to the real-time updates.
- **Destination dot:** amber appears only where a link lands.
- **Sign-in background:** many long, wandering routes converge into one short
  Z route that ends at the amber dot.
