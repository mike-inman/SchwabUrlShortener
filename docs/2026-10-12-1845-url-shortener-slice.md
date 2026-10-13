# URL Shortener Assignment Handoff

Status: superseded for implementation sequencing and persistence direction by [the production-minded slice plan](./2026-10-12-1953-production-ready-slices-and-handoffs.md) and its gated Codex handoffs. Retained as the original assignment-scope record.

Status: drafted on 2026-10-12

You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Keep the implementation focused on the shortener prototype and assignment deliverables.
- Preserve the .NET solution structure and avoid unrelated refactors.
- Do not modify user secrets or environment files beyond necessary local runtime settings.
- Validate with the smallest relevant .NET test/build commands after implementation.

Task: Build a working URL shortener prototype with API endpoints, analytics, reliability guardrails, and assignment documentation.

Goal:
Create a production-style prototype that demonstrates requirement understanding, decomposition, and AI-assisted engineering discipline for the Schwab assignment.

Important context:
- The repository currently contains only the solution shell and empty placeholder classes.
- The assignment requires a runnable end-to-end prototype, architecture documentation, and validation evidence.
- This is a greenfield implementation within the existing .NET solution.

In scope only:
1. Implement the URL shortener domain, application, infrastructure, and API layers.
2. Add in-memory repository and analytics tracking for hits and metadata.
3. Add unit/integration tests covering core behavior.
4. Document architecture, setup, and assignment scenarios.

Out of scope:
- External databases or cloud deployment.
- Large-scale distributed systems beyond the prototype.
- Unrelated solution cleanup.

Required discovery before edits:
- Inspect the existing solution projects and empty placeholder classes.
- Confirm the .NET SDK version available in this environment.
- Review the current tests and ensure they can be replaced with meaningful coverage.

Implementation requirements:
- Expose a POST endpoint to create short URLs with validation.
- Expose a GET endpoint to resolve short codes to original URLs, recording hit analytics.
- Provide an analytics endpoint returning summary metrics for a short code.
- Include a simple UI or developer-facing endpoint for manual testing.
- Use clear separation between domain, application, infrastructure, and API responsibilities.
- Ensure IDs are deterministic or safe and validate user input.

Exception / observability requirements:
- No runtime or production-impacting exception path is required for this small prototype beyond structured logging and clear validation responses.

Realtime / no-polling requirements:
- This prototype does not include operator-facing async workflows; no polling implementation is required.

Testing:
- Add focused unit tests for URL validation and analytics behavior.
- Add an integration test covering create/resolve/analytics workflow.
- Run dotnet test for the relevant solution or test projects.

Documentation:
- Update the project README or create a concise assignment summary covering setup, architecture, scenarios, and validation notes.
- Include the three required scenario decompositions (greenfield, brownfield, ambiguous) in the final summary or docs.

Completion criteria:
- [ ] The solution builds successfully.
- [ ] API endpoints work for create, resolve, and analytics.
- [ ] Validation tests pass.
- [ ] Architecture and setup documentation are present.
- [ ] The project reflects the AI-assisted engineering assignment deliverables.
