# Track H create form usability addendum plan

Date: 2026-10-14 19:48 CDT
Purpose: implement the four create form and dashboard time fixes from the addendum handoff on `codex/ui-polish-contact` for PR #9.

## Scope and tasks

1. Rebase the clean Track H branch onto current `origin/main`, preserving its revised generic 400 fallback. Copy the addendum handoff into this worktree first.
2. Give guest and managed forms the code hint, HTML constraints, field-specific inline errors, input retention on failure, and clearing and destination focus on success.
3. Reuse Insights' browser time-zone preference for dashboard timestamps, with an explicit UTC fallback.
4. Add focused Web tests, run solution checks and the full isolated Compose suite, and append screenshot and check evidence to the Track H validation note.

## Acceptance criteria

- Both create forms retain values on failure and reset and focus the destination on success.
- URL, code, and duplicate errors identify their fields; unknown API 400 uses the existing generic fallback.
- Dashboard timestamps reflect the viewer's time zone after hydration.
- Full local Compose tests pass on an isolated project, port, and `10.251.50.0/24` subnet; screenshots and test counts are recorded.
- Changes are committed locally and reviewed before requesting a push. PR #9 is not merged.

## Outcome

The branch was rebased onto `e5fbaad` without conflicts. Both forms now validate, retain or clear input as appropriate, and focus the destination after success. The dashboard uses the browser time zone from Insights and labels UTC fallback. The validation note links local guest-form before/after screenshots and records a signed-in dashboard screenshot gap.

The zero-warning solution build, 59 focused Web tests, and fresh Compose suite (40 integration, 44 unit, 85 Web) passed. A live guest POST confirmed cleared fields and autofocus after success; an invalid URL POST confirmed input retention and the linked field error. The implementation and evidence were committed locally at `48bf307`. Next action: ask Michael to approve a `--force-with-lease` push of the rebased feature branch, then check PR #9 CI. PR #9 remains unmerged; its CI for this new head has not run.
