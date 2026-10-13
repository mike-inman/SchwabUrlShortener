# Ziplio branding for the External ID sign-in pages

Applies to the external tenant `schwaburlshortener.onmicrosoft.com`
(`6adc36e9-dfe5-469f-8aed-6ef83abcdd19`). These are portal steps in the
Microsoft Entra admin center. They change only how the hosted pages look; no
app code or configuration changes.

Images come from `branding/entra/out/` after running `branding/entra/render.sh`.

| File | Portal field | Microsoft limit |
| --- | --- | --- |
| `out/background.jpg` | Basics > Background image | 1920x1080, under 300 KB |
| `out/favicon.png` | Basics > Favicon | 32x32 |
| `out/banner.png` | Sign-in form > Banner logo | 245x36, under 10 KB |
| `out/square.png` | Sign-in form > Square logo (light and dark) | 240x240, under 50 KB |
| `ziplio-entra.css` | Layout > Custom CSS | under 25 KB |

## 1. Rename the tenant

The hosted pages and verification emails show the tenant name, which is why
the card currently says "SCHWAB URL SHORTENER".

1. Search for **Tenant properties**.
2. Set **Name** to `Ziplio` and select **Save**.

## 2. Company branding

1. Go to **Entra ID > Custom Branding** (or search **Company branding**).
2. On **Default sign-in**, select **Edit**.
3. **Basics**
   - Favicon: `out/favicon.png`
   - Background image: `out/background.jpg`
   - Page background color: `#0B1426`
4. **Layout**
   - Template: **Full-screen background**
   - Show header: off
   - Show footer: on
   - Custom CSS: upload `ziplio-entra.css`
5. **Footer**
   - Leave Privacy & Cookies and Terms of Use unchecked until Ziplio has its
     own pages. Checking them without a URL shows Microsoft's links.
6. **Sign-in form**
   - Banner logo: `out/banner.png`
   - Square logo (light theme): `out/square.png`
   - Square logo (dark theme): `out/square.png`
   - Username hint text: `you@example.com`
   - Sign-in page text: `Short links. Smart connections.`
   - Self-service password reset: leave the defaults.
7. **Review + save**.

## 3. Check it

Open a new incognito window, go to `https://localhost:63656`, and select
**Sign in**. Check the sign-in, sign-up (create account), email code, and
sign-out pages. Branding can take a few minutes to appear after saving.

Known limits:

- The account tiles on the sign-out page and some helper text have no
  supported CSS selectors, so they keep Microsoft's styling on the white card.
- Brand web fonts may not load on Microsoft's pages. The CSS falls back to
  Segoe UI or the system font, and the logo images carry the brand type.
- Microsoft is phasing out custom CSS layout properties. External ID tenants
  are not affected today, and this file uses none of those properties anyway.
