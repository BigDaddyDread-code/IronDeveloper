# CLN-01A Frontend Behavior CI Receipt

**Status:** Local lane passed; GitHub lane verification required
**Baseline:** `main` at `17a75a77`
**Date:** 12 July 2026

## Purpose

Close the current-product frontend execution gap identified by CLN-01 without putting all 747 historical and component tests into one unbounded lane.

## Review Line

Current product routes need browser behavior evidence. Mock-backed Playwright does not replace live LocalTest or non-author Tauri qualification.

## Killjoy Line

A larger timeout is not test ownership.

## Scope

Allowed:

- one stale project-entry mock contract;
- one bounded CI runner script;
- one GitHub workflow;
- CI map and receipt updates.

Forbidden:

- product runtime behavior;
- authority or workflow semantics;
- API contracts;
- live LocalTest replacement;
- broad historical test cleanup.

## Finding and Correction

The bounded candidate selected 158 tests across 18 current-product files. The first run produced 157 passes and one deterministic failure.

`changing projects clears the active work item` still mocked the legacy ticket detail endpoint but not the backend-owned Work Item projection added later. The product correctly reported the projection as unavailable. The test now mocks that existing read contract before asserting project-switch cleanup.

Classification: `TestExpectationDrift`.

Runtime behavior changed: **No**.

## Lane Contract

`frontend-behavior-ci`:

- runs on Windows for pull requests to `main`;
- pins Node and npm;
- installs Chromium;
- runs the production Vite build;
- lists and executes 158 tests from 18 explicit files;
- uses four workers and no retry;
- compares selected and executed counts;
- emits JUnit and bounded summary evidence;
- scans artifacts before 14-day upload.

## Local Evidence

Before the stale mock correction:

```text
158 selected
157 passed
1 failed
Duration: 2.8 minutes
```

Executed after the stale mock correction:

```powershell
./Scripts/ci/run-frontend-behavior-ci.ps1
```

Result:

```text
Production build: passed
Selected: 158
Executed: 158
Passed: 158
Failed: 0
Skipped: 0
Duration: 2.7 minutes
Artifact safety: passed
```

## Remaining Boundary

The other Playwright files are not called covered. CLN-08 owns their suite classification and lane assignment.

## Next Cleanup Slice

CLN-02 product-completion map, while CLN-08 retains the explicit integration and historical frontend coverage gaps.
