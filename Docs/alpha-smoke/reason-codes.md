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
| `RootSafetyBlocked` | Smoke output root is unsafe. | Output under repo/root/home/temp root or reparse-point ancestor. | Choose a safe output directory outside source. | Write artifacts under source. |
| `UnsafeRoot` | Legacy detail value for unsafe smoke output roots. | The release-facing stage reason is `RootSafetyBlocked`. | Use `RootSafetyBlocked` for new checks. | Treat legacy detail as permission to write. |
| `DeterministicModelNotConfigured` | Deterministic model mode lacks explicit configuration. | Future config-driven deterministic smoke was requested without fixture setup. | Configure deterministic smoke explicitly. | Fall back to arbitrary fake output. |
| `LiveModelModeNotImplemented` | Live mode is intentionally absent in D-2a. | `-ModelMode Live` was requested. | Implement D-2b explicitly. | Fall back to deterministic silently. |
| `LiveModelNotConfigured` | Future live mode lacks provider config. | Model provider/key/alias missing. | Configure live provider safely. | Log secrets or fake live mode. |
| `TicketPersistFailed` | Future ticket persistence failed. | API/store problem. | Inspect API/store logs. | Insert final ticket state manually. |
| `ReadinessBlocked` | Readiness/build preflight failed. | Build failure, missing dependency, or invalid fixture. | Fix the underlying issue. | Mark readiness passed manually. |
| `SkeletonRunStartFailed` | Skeleton run smoke test failed. | Orchestrator/workspace/build/test failure. | Inspect TRX and console output. | Skip the skeleton lane. |
| `CriticPackageMissing` | Critic package hash is absent. | Evidence packaging failed. | Inspect smoke receipt and run events. | Invent a package hash. |
| `CriticReviewFailed` | Future critic review request failed. | Critic service/API failure. | Rerun critic review path safely. | Treat package existence as review. |
| `CriticReviewRequestNotAutomated` | Current D-2a did not request critic review. | This is a known named gap. | Implement the critic request slice. | Simulate a clean review. |
| `CriticReviewRecorded` | REL-2 recorded deterministic critic review evidence before continuation. | Applied deterministic smoke is running. | Treat it as service-level smoke evidence only. | Treat deterministic critic evidence as approval. |
| `GateStateUnexpected` | Run did not halt at the expected gate. | Loop changed state unexpectedly. | Inspect run report and events. | Continue anyway. |
| `AcceptedApprovalRequired` | Applied mode lacks an explicit accepted approval input. | `-RunUntil Applied` was used without supported approval mode. | Use `-RecordHumanApproval` with the exact phrase template, or wait for SQL/API approval persistence. | Create approval silently. |
| `AcceptedApprovalRecorded` | REL-2 recorded a hash-bound accepted approval for deterministic smoke. | Applied deterministic smoke is running with `-RecordHumanApproval`. | Verify the consumed approval ID in `run-receipt.json`. | Treat the recorded approval as commit, push, release, or deployment authority. |
| `ApprovalPhraseMissing` | Applied mode lacks the required approval phrase. | `-RecordHumanApproval` was used without `-ApprovalPhrase`. | Supply `I approve continuation for run <runId> package <hash>`. | Accept vague approval text. |
| `ApprovalPhraseMismatch` | Approval phrase did not match the required template. | Caller supplied unbound approval text. | Use the exact documented phrase template. | Accept "approve all" style text. |
| `ApprovalTargetHashMismatch` | Approval target hash and critic package hash differ. | Receipt or approval binding mismatch. | Stop and inspect the critic package and approval evidence. | Continue or apply anyway. |
| `ContinuationRefused` | Continuation did not unblock after approval. | Missing critic review, undispositioned findings, stale evidence, or unsatisfied approval. | Inspect run events and named blocker. | Treat approval as direct apply permission. |
| `ContinuationUnblocked` | Continuation consumed a matching accepted approval. | REL-2 deterministic smoke passed the continuation gate. | Proceed only to the separate controlled apply request. | Treat continuation as source mutation authority. |
| `ContinuationRequiresCriticReview` | Continuation needs recorded critic review evidence. | Critic review is missing. | Record/request the critic review through the governed path. | Let approval bypass critic review. |
| `ContinuationRequiresFindingDisposition` | Continuation needs dispositions for critic findings. | Findings exist without human disposition. | Record a disposition for each finding. | Ignore critic findings. |
| `ApplyRefused` | Controlled apply did not reach `Applied`. | Apply gate or spine blocked. | Inspect `SkeletonApplyRefused` and apply-stage evidence. | Mark the run applied manually. |
| `Applied` | Controlled copy-only apply reached `Applied`. | REL-2 deterministic smoke applied through the workspace spine. | Open the final report and apply receipt. | Treat Applied as commit, push, release, or deployment. |
| `ApplyRequiresContinuation` | Apply was requested before continuation unblocked. | Missing `SkeletonContinuationUnblocked` evidence. | Request continuation first. | Apply from halted state. |
| `ApplyTargetMismatch` | Apply target did not match the approved package/run. | Receipt or package mismatch. | Stop and inspect package/approval/apply refs. | Copy files manually. |
| `ApplyReceiptMissing` | Applied mode did not leave the apply-copy receipt. | Apply evidence chain is incomplete. | Inspect workspace evidence and rerun only after understanding the gap. | Call source mutation successful without a receipt. |
| `FinalReportMissing` | The final report did not reconstruct the applied loop. | Report/evidence chain incomplete. | Inspect report gaps. | Declare success from terminal state only. |
| `ReportMissing` | Report reconstruction failed. | Missing run/evidence records. | Inspect run report service/evidence paths. | Declare success without report. |
| `ReceiptWriteFailed` | Smoke receipt was not written. | Output path or test failure. | Inspect root safety and filesystem errors. | Hand-write a success receipt. |
| `SourceRootDirty` | Source root was dirty before mutation-shaped work. | Local edits or generated files exist. | Commit/stash/clean before running. | Apply over unknown source state. |
| `SourceRootMutationDetected` | Source-root mutation was detected outside the expected controlled path. | Unexpected write path. | Inspect status and smoke artifacts. | Ignore untracked/modified files. |
| `SourceRepoDirtyBeforeRun` | Source worktree has uncommitted changes. | Local edits present. | Commit/stash before running gate smoke. | Run smoke and claim it preserved source. |
| `SourceRepoChangedUnexpectedly` | Source worktree changed during smoke execution. | Smoke/test path wrote to source or generated source-root artifacts. | Inspect `git status --short` and fix the mutating path. | Ignore the dirty repo and call the smoke clean. |
