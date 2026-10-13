# Ziplio branding integration handoff — 2026-10-13 12:40 CDT

Status: drafted, not implemented. Purpose: apply the provisional Ziplio brand
kit to `Shortener.Web` (dark theme, logo, home hero, dashboard styling). The
External ID hosted-page branding is a separate operator task in
`branding/entra/portal-setup.md` and is out of scope here.

Work in `/Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization`
on `codex/workspace-authorization`.

## Preconditions

- Start **after** the API base-URL fix (`ShortenerApi:BaseUrl` rejected for
  `http://api:8080`) is committed. This branding work must not touch
  `ShortenerApiClient.cs`, `Program.cs`, env files, or identity code.
- Follow `AGENTS.md` repo-state checks. Treat changes you did not make as
  user-owned.
- The kit was drafted in the canonical checkout:
  `/Users/michaelinman/Repos/SchwabUrlShortener/branding/`. Copy the whole
  `branding/` folder and this handoff into the worktree unchanged, then commit
  them by themselves before any app edits.

## Brand rules

- Brand is provisional (trademark clearance pending). Keep the product name and
  tagline in one place: add a small `BrandText` static class in
  `Shortener.Web` with `Name = "Ziplio"`, `Tagline = "Short links. Smart
  connections."`, and `TitleSuffix = "Ziplio"`, and use it in markup.
- Do not rename the solution, projects, namespaces, or routes.
- Amber marks the destination only (active nav dot, link chips, created link).
  Do not use it for buttons or errors.

## Changes

1. **Static assets** (`src/Shortener.Web/wwwroot/`)
   - Add `tokens.css` from `branding/tokens.css`.
   - Replace `app.css` with `branding/app/app.css`. Every existing class the
     pages use is styled there (`site-nav`, `brand`, `nav-button`, `link-form`,
     `result`, `section-heading`, `destination`, `selected-link`, toasts).
   - Add `favicon.svg` from `branding/logo/favicon.svg`. Keep `favicon.png` as
     a fallback.

2. **`Components/App.razor` head**
   - `<html lang="en" data-bs-theme="dark">`
   - After the viewport meta: `<meta name="theme-color" content="#0B1426" />`
   - Fonts, before Bootstrap:
     ```html
     <link rel="preconnect" href="https://fonts.googleapis.com" />
     <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
     <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:opsz,wght@12..96,600;12..96,700&family=Instrument+Sans:wght@400;500;600&family=JetBrains+Mono:wght@400;500&display=swap" />
     ```
   - Stylesheet order: Bootstrap, then `tokens.css`, then `app.css`, then
     `Shortener.Web.styles.css`.
   - Icons: `<link rel="icon" type="image/svg+xml" href="favicon.svg" />`
     followed by the existing PNG link.
   - If the app sends a Content-Security-Policy, allow `fonts.googleapis.com`
     for styles and `fonts.gstatic.com` for fonts. Self-hosting the fonts is a
     later production task.

3. **`Components/Layout/NavMenu.razor`** — replace the brand link with the
   mark and wordmark (inline SVG so it inherits no extra request):
   ```razor
   <a class="brand" href="/" aria-label="@BrandText.Name home">
       <svg viewBox="0 0 48 48" aria-hidden="true">
           <circle cx="9" cy="13" r="3.4" fill="none" stroke="#59E6CF" stroke-width="2.6"/>
           <path d="M16 13H38L12 35H33" fill="none" stroke="#59E6CF" stroke-width="5.2" stroke-linecap="round" stroke-linejoin="round"/>
           <circle cx="40" cy="35" r="4.2" fill="#F2BC68"/>
       </svg>
       <span>ziplio</span>
   </a>
   ```
   Keep the existing links and sign-in/out logic unchanged.

4. **`Components/Pages/Home.razor`** — add the hero above the guest form and
   demote the form heading. Keep all existing form behavior and IDs.
   ```razor
   <PageTitle>@BrandText.TitleSuffix · Short links. Smart connections.</PageTitle>
   <section class="zl-hero" aria-labelledby="hero-title">
       <p class="zl-eyebrow">@BrandText.Tagline</p>
       <h1 id="hero-title">Every link deserves a <em>smarter destination.</em></h1>
       <p class="lead">Create short links, track engagement, and manage your links from one dashboard.</p>
       <div class="zl-demo zl-panel" aria-hidden="true">
           <span class="zl-demo-label">Example</span>
           <span class="zl-demo-long">https://www.example.com/campaigns/2026/autumn-launch?utm_source=newsletter&amp;utm_medium=email&amp;utm_campaign=launch</span>
           <span class="zl-demo-route">
               <svg viewBox="0 0 40 24"><path d="M3 4H33L9 20H37"/></svg>
               <span class="zl-chip">ziplio.io/launch</span>
           </span>
       </div>
       <ul class="zl-proof">
           <li>Secure links</li><li>Live click counts</li><li>Built for reliability</li>
       </ul>
   </section>
   <section aria-labelledby="guest-title">
       <h2 id="guest-title">Try a guest link</h2>
       ...existing guest copy, EditForm, status, and result markup...
   </section>
   ```
   The demo is decorative (`aria-hidden`) and labeled "Example" so it does not
   imply a real link exists.

5. **`Components/Pages/Dashboard.razor`** — title and heading only:
   `<PageTitle>My links · @BrandText.TitleSuffix</PageTitle>` and
   `<h1>My links</h1>`. No logic or markup structure changes; the theme styles
   the existing table, form, toasts, and alerts.

6. **Other pages** (`Error`, `NotFound`): use the same `PageTitle` suffix.

## Verification

- `dotnet build` with zero warnings; full `dotnet test`. Update any Web test
  that asserts the old "Shortener" brand text or old headings, and keep the
  behavior assertions intact. Report exact counts.
- Rebuild and restart only the Schwab Web container. Check at 1280px and 390px
  wide: home, dashboard (signed in), a created guest link, an error alert, and
  a toast. No horizontal page scroll at 390px.
- Check reduced motion (macOS: Accessibility > Display > Reduce motion): the
  hero shows its final state with no animation and the live dot is still.
- Keyboard: every link, button, and input shows a visible mint focus ring.
- Commit the branding copy first, then the app changes, then a dated
  validation note with screenshots described in text. Do not change identity,
  API, database, or Compose configuration.
