# UI Testing Contract

This is an engineering rule for IronDev UI work.

No serious new UI shell workflow is accepted unless it is testable through stable selectors, deterministic API-seeded state, trace artifacts, screenshots/videos on failure, JSON output, and a compact markdown report.

## Future UI Gate

No serious new UI shell work starts until:

- Playwright harness exists.
- `data-testid` convention exists.
- JSON and markdown reports exist.
- At least one journey can run or is explicitly marked pending against a missing shell.
- Future UI PRs add or update tests for changed workflows.

This gate does not choose a production UI framework.

## Test ID Rules

All interactive workflow controls need stable `data-testid` attributes.

Test IDs are semantic, not visual. They must describe workflow meaning and survive layout, styling, navigation, and component refactors.

Required rules:

- Use stable semantic IDs.
- Use generated or random IDs nowhere in workflow tests.
- Use styling-coupled selectors nowhere in workflow tests.
- Use deep CSS chains nowhere in workflow tests.
- Keep IDs stable across redesigns unless the workflow contract changes.
- Prefer `getByTestId` for workflow assertions.
- Use visible text only as supporting evidence, not the primary locator for workflow controls.

## Preferred Format

Use dot-separated semantic names:

```text
area.surface.control
area.collection.itemStableId
```

Examples:

```text
shell.nav.tickets
shell.nav.documents
login.email
login.password
login.submit
project.list
project.row.{projectSlug}
ticket.list
ticket.row.{ticketId}
ticket.create
ticket.review.importSelected
document.editor.title
document.editor.body
chat.composer.input
chat.composer.send
build.run.start
runReport.detail
```

## Workflow Test Standard

Workflow tests must:

- Start from deterministic API-seeded data.
- Use stable `data-testid` selectors for actions and primary assertions.
- Assert product state transitions.
- Capture trace on failure.
- Capture screenshot on failure.
- Capture video on failure where practical.
- Emit Playwright JSON output.
- Generate a markdown summary for Codex and human review.

## Forbidden Patterns

These are not accepted in workflow tests:

```text
.sidebar > div:nth-child(2) button
button:has-text("Submit")
[class*="primary"]
#auto-generated-123
```

Text locators may be used for secondary content assertions after the stable workflow control has been located.
