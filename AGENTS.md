# SchwabUrlShortener agent guide — 2026-10-12

This repository is the .NET 10 starter solution for the Schwab URL shortener assignment. The eight generated projects are registered in `SchwabUrlShortener.slnx`. The current tests are template tests; their passing result does not establish URL shortener behavior.

## Before changing files

- Confirm the physical checkout path is `/Users/michaelinman/Repos/SchwabUrlShortener` and inspect `git status --short --branch`.
- Treat existing untracked or modified files as user-owned. Preserve them unless the user explicitly assigns them to the task. In particular, do not add the local assignment PDF under `docs/` to GitHub.
- Keep local API keys, passwords, and other credentials intact. Never print, delete, or commit them. The `.env` files and local `.claude/` and `.codex/` directories are excluded by `.gitignore`.
- Before editing a configuration that contains credentials, make a local backup. Do not remove unrelated MCP server configurations from `claude_desktop_config.json` if that file is ever introduced here.

## Solution structure

- `src/Shortener.Domain`: domain models and rules, with no project dependencies.
- `src/Shortener.Application`: use cases and interfaces; references Domain.
- `src/Shortener.Infrastructure`: persistence and external integrations; references Application and Domain.
- `src/Shortener.Api`: ASP.NET Core API; references Application and Infrastructure.
- `src/Shortener.Web`: Blazor web app; references Application.
- `src/Shortener.Analytics`: background worker; references Application and Infrastructure.
- `tests/Shortener.UnitTests`: unit tests for Application and Domain.
- `tests/Shortener.IntegrationTests`: integration tests for API and Infrastructure.

Keep business rules out of the host projects and avoid adding a project reference from Domain to an outer layer. Add new dependencies only when code needs them.

## Validation and documentation

- Run `dotnet build SchwabUrlShortener.slnx` and `dotnet test SchwabUrlShortener.slnx` after solution or project changes. Report the exact results and whether tests exercise real behavior or only the generated scaffold.
- Put plans in `docs/` with the date, time, and purpose in the filename. Date other new documents as well.
- Commit the intended task changes after validation. Check staged paths before committing and verify the pushed branch when publishing. Do not stage local secrets or unrelated files.
