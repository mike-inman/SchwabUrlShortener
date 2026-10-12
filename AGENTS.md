# SchwabUrlShortener agent guide — 2026-10-12

This repository is the .NET 10 starter solution for the Schwab URL shortener assignment. All eight generated projects are in `SchwabUrlShortener.slnx`. The current tests are template tests; passing them does not establish URL shortener behavior.

## Start with the actual repository state

- Confirm `pwd -P` is `/Users/michaelinman/Repos/SchwabUrlShortener`. Read this file and the task's relevant source and documentation before editing.
- Inspect `git status --short --branch`, the local commit, and `origin/main`. Treat pre-existing modified and untracked files as user-owned. Do not reset, stash, overwrite, or delete them to make the checkout appear clean. If a task overlaps one of those files, resolve ownership and scope before editing it.
- Keep changes within the assigned task. For substantial work, write a dated plan in `docs/YYYY-MM-DD-HHMM-purpose.md` with purpose, scope, tasks, and acceptance criteria. Record the outcome and any unfinished work there. If scope grows, document a follow-up instead of silently expanding the task.
- Use relevant available MCP servers when they help with the task. Verify their results against the repository and report the evidence actually obtained.

## Protect local data and access

- Preserve local API keys, passwords, and other credentials. Never print, delete, or commit them. The `.env` files and local `.claude/` and `.codex/` directories are excluded by `.gitignore`; leave them untouched unless the user assigns a specific change.
- Do not add the local assignment PDF under `docs/` to GitHub. Other dated project documents may be committed when they are part of the assigned work.
- Back up a credential-bearing configuration before changing it. If `claude_desktop_config.json` is ever introduced here, preserve all unrelated MCP server entries. Do not include secrets, tokens, cookies, authorization headers, or raw credential material in logs, URLs, error messages, documentation, or commits.
- Do not weaken authentication or other security controls to make a test pass. State the exact access or configuration blocker instead.

## Solution structure

- `src/Shortener.Domain`: domain models and rules, with no project dependencies.
- `src/Shortener.Application`: use cases and interfaces; references Domain.
- `src/Shortener.Infrastructure`: persistence and external integrations; references Application and Domain.
- `src/Shortener.Api`: ASP.NET Core API; references Application and Infrastructure.
- `src/Shortener.Web`: Blazor web app; references Application.
- `src/Shortener.Analytics`: background worker; references Application and Infrastructure.
- `tests/Shortener.UnitTests`: unit tests for Application and Domain.
- `tests/Shortener.IntegrationTests`: integration tests for API and Infrastructure.

Keep business rules out of host projects and do not add a dependency from Domain to an outer layer. Add project and package references only when the implementation needs them.

## Implementation and verification

- Build the real application path. Test doubles and fixtures belong in isolated tests; never present a mock, hard-coded response, or simulated integration as delivered behavior.
- For API, worker, or external integration changes, make failures inspectable: preserve useful exception context, use structured and sanitized logs, carry request or operation correlation IDs where available, and give callers safe, actionable errors. For background work, consider retry, duplicate processing, and durable failure handling before claiming operational reliability.
- After a substantive edit, run the narrowest useful check first. For solution or project changes, run `dotnet build SchwabUrlShortener.slnx` and `dotnet test SchwabUrlShortener.slnx`. Preserve existing tests; add meaningful tests for new behavior rather than treating the template tests as acceptance.
- Distinguish implementation, local test results, real service verification, deployment, and operational acceptance. State which environment and paths were exercised, plus any remaining blockers.

## Finish and hand off

- Date new documents. Keep plans, implementation notes, and validation records under `docs/` when the work warrants them. For multi-session work, record what changed, what was verified, and the next action so the task can resume without reconstructing it from chat.
- Check the staged paths and diff before committing. Commit only intended changes, keeping unrelated local files and secrets out. Verify the pushed branch and commit when publishing.
- Report files changed, checks run, known issues, and any intentional pre-existing untracked files. Do not erase user-owned files to claim a clean worktree.
- Before deleting a local or remote branch, create and push an `archive/<branch>` tag for its tip. Do not delete branches merely because they are old.

## Gated implementation handoffs

For a handoff to another coding agent, start from `docs/2026-10-12-1855-gated-implementation-handoff-template.md`. Save the filled handoff in `docs/` with its drafting date, time, and purpose in the filename. Replace every `{{...}}` field, keep the sections in order, and state “Not applicable” with a reason for any section that does not affect the slice. The realtime section records the actual progress and refresh design; the source project's blanket SSE policy does not apply here.

The handoff must identify the exact worktree and branch, preserve existing local work and secrets, define in-scope and out-of-scope work, name files to inspect before edits, specify narrow checks, and make completion criteria reviewable. Validate it before dispatch:

```bash
bash scripts/check-codex-handoff.sh docs/<dated-handoff-file>.md
```

The checker returns 0 for a valid format, 1 for a format failure, and 2 for a missing file or incorrect invocation. A passing format check does not approve the work or prove implementation. Run `bash scripts/test-check-codex-handoff.sh` when changing the checker.
