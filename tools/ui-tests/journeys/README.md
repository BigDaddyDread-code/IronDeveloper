# Journey Contracts

Journey specs live in `../tests`.

Each journey must:

- Seed state through `IronDev.Api`.
- Use semantic `data-testid` selectors.
- Assert workflow state, not visual layout.
- Produce JSON, HTML, trace, screenshot, and video artifacts through the shared Playwright config.
- Avoid deep CSS selector chains.

Current journeys:

- `login-smoke`
- `project-open-smoke`
- `ticket-review-smoke`
- `document-to-ticket-journey`
- `build-run-review-smoke`
