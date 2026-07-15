# Accessibility and Resilient State Pass

**Verified:** 15 July 2026

The Tauri shell now exposes skip navigation, current-page semantics, route-change focus management, labelled API preflight state, assertive error outcomes, polite slow-load status, visible focus treatment, bounded responsive content, and explicit safe retry controls.

## Verification matrix

| Concern | Product behavior | Automated evidence |
|---|---|---|
| Keyboard operation | Skip link is the first shell tab stop; product navigation and project tiles are native controls | `flow-shell-smoke.spec.ts`, `project-entry.spec.ts` |
| Focus management | Skip activation and product route changes focus the main region; route outcomes focus their alert | `flow-shell-smoke.spec.ts`, `project-routing.spec.ts` |
| Screen-reader labels | Active product destination uses `aria-current`; API failure is labelled/described; loading and error states use appropriate live regions | `flow-shell-smoke.spec.ts`, `ux-start.spec.ts` |
| Responsive layout | Main content can shrink and long backend text wraps | shell CSS plus narrow-viewport suites |
| No horizontal overflow | Board, Governance, Chat, ticket draft, and session drawer are exercised at narrow viewports | existing Playwright viewport tests |
| Error recovery | Board and unavailable API states expose explicit read/connection retries | `board-ux.spec.ts`, `ux-start.spec.ts` |
| Retry safety | Retry repeats the failed read or preflight; governed refusal/recovery evidence does not invent retry | Work Item and governance evidence suites |
| Slow/unavailable API | Board announces loading politely; API preflight and route outcomes announce failure without rendering fallback product truth | `BoardScreen`, `PreflightGate`, existing unavailable-state suites |

Accessibility semantics do not alter authority. `aria-current`, focus, loading, and retry presentation report client state only. A retry does not bypass authentication, tenant/project scope, governed refusal, human review, or backend permission.
