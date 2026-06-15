# PR153 - Read-only Governance Timeline UI

PR153 adds the Read-only Governance Timeline UI.

Governance Timeline UI is read-only.

Timeline is not authority.

Observation is not approval.

Traceability is not mutation permission.

Refresh is not retry.

Navigation is not workflow continuation.

Search is not governance replay.

Copy reference is not approval.

The UI consumes existing GET-only governance trace APIs.

This PR does not add backend API endpoints, CLI commands, SQL migrations, stores, runners, executors, hosted services, background workers, schedulers, cleanup jobs, repair paths, restart paths, approval paths, policy satisfaction paths, workflow transition paths, workflow continuation paths, source apply paths, patch apply paths, model calls, tool invocation, agent dispatch, memory promotion, retrieval activation, or raw/private payload exposure.

## What changed

- Added the `/governance/timeline` workspace route.
- Added a read-only Governance Timeline page.
- Added safe summary, correlation, causation, trace detail, and related-report inspection views.
- Added UI tests proving the page is GET-only and does not render control actions or raw/private payload fields.
- Added static boundary tests for the UI route, API facade usage, receipt language, and forbidden control/action vocabulary.

## Boundary

The Governance Timeline UI can search existing governance traces, refresh the current read-only search, clear local filters, copy safe references, open trace detail, and show related read-only report links.

The Governance Timeline UI cannot approve, reject, grant, satisfy policy, continue workflow, transition workflow, retry, rerun, resume, repair, fix, restart, invoke tools, dispatch agents, call models, build prompts, create tickets, promote memory, activate retrieval, apply source, apply patches, approve release, mark dogfood passed, run migrations, clean up data, delete data, purge data, archive data, or redact data.

## Review line

PR153 draws the governance timeline. It does not add timeline controls.
