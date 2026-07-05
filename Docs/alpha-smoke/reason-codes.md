# Alpha Smoke Reason Codes

Reason codes are part of the smoke contract. A blocked or failed stage should be named, not guessed from prose.

| Reason Code | Meaning | Likely Cause | Next Safe Action | Unsafe Shortcut To Avoid |
| --- | --- | --- | --- | --- |
| `RepoRootNotFound` | The script could not find `IronDev.slnx`. | Running outside a checkout. | Start from the repo root or invoke the script by path inside the repo. | Hardcode a local path. |
| `BookSellerSampleMissing` | `Samples/BookSeller` is missing. | Incomplete checkout or branch drift. | Restore the sample from `main`. | Substitute another project silently. |
| `BookSellerTicketsMissing` | `TestFixtures/BookSeller/tickets.json` is missing. | Incomplete checkout or branch drift. | Restore the fixture from `main`. | Create an ad hoc ticket with different criteria. |
| `TicketKeyNotFound` | The requested ticket key is absent. | Typo or unsupported ticket. | Use `validate-book` for D-2a. | Run a different ticket while claiming D-2a. |
| `ExistingTicketIdNotSupported` | Existing ticket IDs are not supported by D-2a. | A caller passed `-ExistingTicketId`. | Run the fixture-backed D-2a command or implement the resume path explicitly later. | Pretend the script resumed a ticket. |
| `ExistingRunIdNotSupported` | Existing run IDs are not supported by D-2a. | A caller passed `-ExistingRunId`. | Run a fresh D-2a smoke or implement the report/resume path explicitly later. | Pretend the script resumed a run. |
| `DotnetMissing` | .NET SDK is unavailable. | SDK not installed or not on `PATH`. | Install/configure .NET SDK. | Skip build/test. |
| `NodeMissing` | Node is unavailable. | Node is not installed or not on `PATH`. | Install/configure Node before UI-backed smoke. | Claim UI readiness from service-only smoke. |
| `GitMissing` | Git is unavailable. | Git not installed or not on `PATH`. | Install/configure Git. | Run mutation-shaped smoke without source-state checks. |
| `ApiUnavailable` | API was not used or not reachable. | D-2a currently uses service-level smoke; future API smoke may need startup. | For current D-2a, treat as named gap. For future API mode, start API safely. | Insert DB rows directly. |
| `ApiAuthMissing` | API auth is missing. | Future API mode lacks login/token setup. | Use documented LocalTest auth/bootstrap. | Disable auth. |
| `SqlUnavailable` | SQL was not used or not reachable. | D-2a currently uses in-memory stores. | Treat as named gap until SQL/API smoke lands. | Pretend in-memory is durable proof. |
| `LocalOverrideMissing` | Local override config was not found. | Fresh checkout has no local override. | Follow Block J local config docs when API/SQL smoke needs it. | Commit machine-specific config. |
| `RootSafetyNotEvaluated` | Check-only mode did not evaluate writable roots. | No artifacts are written in check-only mode. | Run `-RunUntil Gate` to evaluate output root safety. | Treat check-only as execution proof. |
| `UnsafeRoot` | Smoke output root is unsafe. | Output under repo/root/home/temp root or reparse-point ancestor. | Choose a safe output directory outside source. | Write artifacts under source. |
| `DeterministicModelNotConfigured` | Deterministic model mode lacks explicit configuration. | Future config-driven deterministic smoke was requested without fixture setup. | Configure deterministic smoke explicitly. | Fall back to arbitrary fake output. |
| `LiveModelModeNotImplemented` | Live mode is intentionally absent in D-2a. | `-ModelMode Live` was requested. | Implement D-2b explicitly. | Fall back to deterministic silently. |
| `LiveModelNotConfigured` | Future live mode lacks provider config. | Model provider/key/alias missing. | Configure live provider safely. | Log secrets or fake live mode. |
| `TicketPersistFailed` | Future ticket persistence failed. | API/store problem. | Inspect API/store logs. | Insert final ticket state manually. |
| `ReadinessBlocked` | Readiness/build preflight failed. | Build failure, missing dependency, or invalid fixture. | Fix the underlying issue. | Mark readiness passed manually. |
| `SkeletonRunStartFailed` | Skeleton run smoke test failed. | Orchestrator/workspace/build/test failure. | Inspect TRX and console output. | Skip the skeleton lane. |
| `CriticPackageMissing` | Critic package hash is absent. | Evidence packaging failed. | Inspect smoke receipt and run events. | Invent a package hash. |
| `CriticReviewFailed` | Future critic review request failed. | Critic service/API failure. | Rerun critic review path safely. | Treat package existence as review. |
| `CriticReviewRequestNotAutomated` | Current D-2a did not request critic review. | This is a known named gap. | Implement the critic request slice. | Simulate a clean review. |
| `GateStateUnexpected` | Run did not halt at the expected gate. | Loop changed state unexpectedly. | Inspect run report and events. | Continue anyway. |
| `ReportMissing` | Report reconstruction failed. | Missing run/evidence records. | Inspect run report service/evidence paths. | Declare success without report. |
| `ReceiptWriteFailed` | Smoke receipt was not written. | Output path or test failure. | Inspect root safety and filesystem errors. | Hand-write a success receipt. |
| `SourceRepoDirtyBeforeRun` | Source worktree has uncommitted changes. | Local edits present. | Commit/stash before running gate smoke. | Run smoke and claim it preserved source. |
| `SourceRepoChangedUnexpectedly` | Source worktree changed during smoke execution. | Smoke/test path wrote to source or generated source-root artifacts. | Inspect `git status --short` and fix the mutating path. | Ignore the dirty repo and call the smoke clean. |
