# Track D vulnerable package remediation — 2026-10-14 07:44 CDT

## Purpose and scope

Complete handoff item 0 as an independently reviewable commit before application hardening. The worktree is `/Users/michaelinman/Repos/SchwabUrlShortener-hardening`, branch `codex/app-hardening`, based on `origin/main` `912ac3b`. No application settings changed.

## Finding and dependency paths

The initial `dotnet list SchwabUrlShortener.slnx package --vulnerable --include-transitive` resolved `System.Security.Cryptography.Pkcs` and `System.Security.Cryptography.Xml` at `10.0.10`. `dotnet nuget why` traced Pkcs to `Microsoft.Identity.Web` → `Microsoft.Identity.Web.TokenCache`, and Xml to that token cache and the Azure Data Protection packages through `Microsoft.AspNetCore.DataProtection`.

| Package | Advisory | Severity | Old → patched version | Source |
|---|---|---|---|---|
| Pkcs | GHSA-846f-f764-76mx | High | 10.0.10 → 10.0.13 | https://github.com/advisories/GHSA-846f-f764-76mx |
| Pkcs | GHSA-2fmv-rv2c-59x7 | Moderate | 10.0.10 → 10.0.13 | https://github.com/advisories/GHSA-2fmv-rv2c-59x7 |
| Xml | GHSA-r955-8q5j-3h83 | High | 10.0.10 → 10.0.13 | https://github.com/advisories/GHSA-r955-8q5j-3h83 |
| Xml | GHSA-7hqp-7c5m-4vq6 | High | 10.0.10 → 10.0.13 | https://github.com/advisories/GHSA-7hqp-7c5m-4vq6 |
| Xml | GHSA-c399-9853-6jmj | High | 10.0.10 → 10.0.13 | https://github.com/advisories/GHSA-c399-9853-6jmj |

The listed advisories identify `10.0.13` as the first patched 10.0 release. Updating `Microsoft.Identity.Web` from `4.16.0` to `4.16.1` alone left both vulnerable packages at `10.0.10`. The current Azure Data Protection packages likewise pull the older version. API and Web therefore reference both patched packages directly. .NET 10's default package pruning rejected a direct Xml reference with `NU1510`, so pruning is disabled in those two host projects. This keeps the patched assemblies in their build and publish dependency graphs; the built `Shortener.Api.deps.json` and `Shortener.Web.deps.json` both list Pkcs and Xml `10.0.13`. The local shared runtime reports `10.0.7`; runtime/base-image patching remains a separate operational update.

## Verification

- `dotnet build SchwabUrlShortener.slnx -warnaserror`: passed, 0 warnings and 0 errors.
- `dotnet list SchwabUrlShortener.slnx package --vulnerable --include-transitive`: no vulnerable packages in all nine projects.
- `dotnet test tests/Shortener.UnitTests/Shortener.UnitTests.csproj --no-build`: 30 passed.
- `ConnectionStrings__Redis=127.0.0.1:16379 dotnet test tests/Shortener.WebTests/Shortener.WebTests.csproj --no-build`: 59 passed. This used a temporary `redis:7.4-alpine` container named `schwab-trackd-package-redis`, then stopped and removed it. Existing project containers and volumes were untouched.

`dotnet run` and Compose startup checks belong to the full Track D validation after the application changes. No settings, Dockerfile, or Compose files changed in this item.
