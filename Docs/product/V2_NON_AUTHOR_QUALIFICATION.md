# IronDev V2 Non-Author Qualification

**Status:** Required before calling V2 release-proven
**Mode:** Tauri desktop, LocalTest
**Start command:** `.\tools\localtest\start-pr-manual-test.ps1 -FreshSession`

This is the final V2 gate. It must be run by a person who did not build the tested changes. The tester must use the visible product journey, not direct SQL, hidden IDs, filesystem surgery, or author-only fixture knowledge.

## Required Journey

- [ ] Start LocalTest through `.\tools\localtest\start-pr-manual-test.ps1 -FreshSession`.
- [ ] Confirm the app opens in the Tauri desktop window, not only BrowserOnly.
- [ ] Sign in through the visible sign-in screen.
- [ ] Select a tenant only if the UI asks for one.
- [ ] Select or connect a project through the visible project journey.
- [ ] Complete project setup using backend readiness and remedies.
- [ ] Reach Board without manually supplying IDs or tokens.
- [ ] Shape a ticket from Chat or the Work Item entry path.
- [ ] Start a real model-backed governed run.
- [ ] Review findings and record dispositions.
- [ ] Approve through a second eligible user or record the explicit solo exception when allowed.
- [ ] Continue the run after approval.
- [ ] Apply through the product-controlled apply path.
- [ ] Exercise one failed or interrupted path.
- [ ] Recover using only product actions and backend-owned state.
- [ ] Restart the shell and confirm project, Work Item, run, approval, recovery, and audit state reload from backend truth.
- [ ] Confirm no fake green state, unexplained report gap, or hidden manual cleanup is needed.

## Evidence Template

**Tester:**
**Commit tested:**
**Date/time:**
**Mode:** Tauri desktop
**Environment:** LocalTest
**Database reset:** Yes / No, with reason
**Project:**
**Repository path used:**

### Entry Journey

- [ ] Started the LocalTest API and UI through the supported script
- [ ] Confirmed `/api/environment` reports LocalTest
- [ ] Started from a clean or explicitly documented client session
- [ ] Signed in through the UI
- [ ] Selected the tenant through the UI, if shown
- [ ] Selected or created the project through the UI
- [ ] Reached the Board without manually supplying IDs or tokens

### Changed Journey

**Starting state:**
**Steps performed:**
1.
2.
3.

**Expected result:**
**Actual result:**

### Failure or Blocked Path

**Steps performed:**
**Expected refusal/error/empty state:**
**Actual result:**

### Persistence and Recovery

- [ ] Refreshed or restarted the shell
- [ ] Confirmed project context remained correct or was safely reselected
- [ ] Confirmed changed state reloaded from backend truth
- [ ] Confirmed no fake success state appeared

### Evidence

- Screenshot/video:
- Relevant run ID:
- Correlation ID:
- Report/receipt path:
- Console/API errors:
- Known gaps:

**Manual result:** PASS / FAIL

## Pass Rule

V2 can be called release-proven only when this qualification is recorded as PASS and all known gaps are either fixed or explicitly accepted as V2 limitations.
