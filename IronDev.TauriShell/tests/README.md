# IronDev Tauri Shell Smoke Tests

## Visual smoke

Run:

```powershell
npm run test:visual-smoke
```

The visual smoke is not a pixel-perfect visual regression suite. It captures stable manual-review screenshots for the current product hierarchy:

- LocalTest login, proving normal sign-in is the primary flow.
- Chat empty state, proving the composer is immediately visible.
- Chat with a grounded response, proving messages remain the dominant surface.
- Chat with Context hidden, proving the inspector is secondary and collapsible.

Screenshots are written to:

```text
reports/visual-smoke/
```

Use this when checking UI PRs that touch Login or Chat hierarchy. The regular Playwright smoke still owns behavioural assertions.
