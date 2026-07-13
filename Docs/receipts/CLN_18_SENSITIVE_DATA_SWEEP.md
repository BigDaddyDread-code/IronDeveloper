# CLN-18 Sensitive Data Sweep Receipt

**Recorded:** 13 July 2026

## Delivered

- Moved browser bearer-token persistence from local storage to session storage with one-time legacy migration and complete session cleanup.
- Added a shared redactor for credential assignments, bearer/provider/JWT values, private-key blocks, and local absolute paths.
- Applied redaction to project context exports.
- Shaped run-report and run-event API responses so execution internals retain full paths while clients receive bounded references and redacted payloads.
- Replaced hard-coded and publicly derived SQL CI passwords with masked random per-job credentials.
- Extended retained-evidence scanning and removed provider-key-shaped values from frontend fixtures.

## Proof

- API project build passed.
- Focused Core redaction and API run-response boundary tests passed.
- Tauri production build passed.
- Thirteen FlowShell Playwright tests passed, including login storage and legacy-token migration assertions.
- The evidence scanner passed all existing local CI artifact directories and rejected a synthetic credential leak.

## Boundary

Redaction and session storage reduce disclosure. Backend authorization, bounded filesystem resolution, and explicit governed authority remain mandatory.
