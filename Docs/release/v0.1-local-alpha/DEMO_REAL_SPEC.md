# IronDev v0.1 Local Alpha Demo-Real Specification

**Target release:** v0.1 Local Alpha
**Document status:** Demo-real build specification
**Repository:** `BigDaddyDread-code/IronDeveloper`
**Demo fixture:** BookSeller
**Core rule:** BookSeller is the release fixture, not a fake demo.

## 0. Executive Decision

The v0.1 demo must show IronDev as a real governed engineering cockpit, not as a static mock, not as a seeded screenshot machine, and not as a frontend illusion.

BookSeller may be pre-canned as the known test environment. That is acceptable and desirable. A fixed target makes the demo repeatable, understandable, and safe.

What is not acceptable is pre-cooking the system result.

The demo must prove that a user can create or view real tickets, start governed runs, see real build/test evidence, hit the human gate, review critic output, record approval, request continuation, apply through the controlled backend path, open the final report, and then repeat the loop.

The demo is successful only if the system remains usable after the seeded baseline. The viewer must be able to create another ticket and run the loop again.

## 1. Product Promise Demonstrated

IronDev turns messy human intent into governed, reviewable engineering work while keeping authority boundaries explicit and human-controlled.

The demo must show:

```text
messy requirement
-> shaped work item
-> confirmed ticket
-> governed run
-> real build/test evidence
-> critic package/review
-> human approval
-> continuation
-> controlled apply
-> final report
-> repeatable history
```

The demo must not show:

```text
frontend-only fake state
direct SQL final-state insertion
fake build output
fake critic output
fake approval
fake apply receipt
UI-invented readiness
client-invented authority
```

## 2. Demo-Real Definition

A demo is demo-real when all of the following are true:

```text
Real data:
Every screen visited shows SQL-persisted state created through product APIs or existing governed backend paths.

Actually working:
API, SQL, backend services, smoke path, and UI run together. State survives restart.

Good UX:
Every screen in the demo path has populated, loading, empty, blocked, and error behavior. Blockers show reason codes and next safe actions.

No archaeology:
The user never has to hand-copy hidden IDs, inspect the database, read source code to guess the next command, or ask Rob what to do next.

Repeatable:
After baseline seed, a user can create another BookSeller ticket and run the governed path again.
```

## 3. Standing Boundary Rules

These rules apply to every demo PR:

```text
Evidence is not approval.
Validation passed is not approval.
Green CI is evidence, not merge or release approval.
UI/status/read models/memory/receipts are not authority.
A chat turn is conversation, not a work contract.
A draft ticket is not a confirmed ticket.
A critic package is not a critic review.
An approval package is not accepted approval.
Accepted approval is not policy satisfaction.
Policy satisfaction is not source apply.
A gate halt is not accepted approval.
A smoke run is evidence, not release permission.
A setup/bootstrap command is convenience, not alpha readiness.
A demo seed may replay history; it may not invent authority.
BookSeller is the fixture; the system behavior must remain real.
```

Killjoy line:

```text
A fixture is acceptable. A fake outcome is not.
```

## 4. BookSeller Fixture Rules

BookSeller is allowed to be pre-canned as:

```text
known sample repository
known buildable solution
known project profile target
known docs/source content
known example tickets
known deterministic model fixture path
known regression smoke target
known seeded history source
```

BookSeller is not allowed to hide:

```text
ticket creation
run creation
workspace execution
dotnet build output
dotnet test output
critic package generation
critic review recording
finding disposition
accepted approval
continuation request
controlled apply
report reconstruction
persistence after restart
```

BookSeller is the proving ground. The UI is not allowed to lie on its behalf.

## 5. The 12-Minute Demo Script

This script is the definition of done.

```text
1. Launch
   Run one command or documented command sequence.
   API, SQL, and UI start.
   Home shows API connected, BookSeller selected, deterministic/live mode visible, and readiness state.

2. Knowledge
   Open Knowledge.
   BookSeller docs/profile/index evidence is visible.
   The system demonstrably knows the codebase.

3. Tickets
   Open Tickets.
   See at least:
   - one Applied ticket with history
   - one PausedForApproval ticket
   - one available path to create a new ticket

4. Runs
   Open the Applied ticket's run.
   See build/test evidence, critic review, approval, continuation, apply receipt, and final report link.

5. Chat
   Type a messy requirement:
   "books need a discount validation rule"
   Assistant shapes it into a draft work item.

6. Draft ticket
   Click Promote to Work Item.
   Draft ticket appears with title, summary, acceptance criteria, and chat provenance.
   User can edit and confirm criteria.
   User creates the ticket.

7. Start governed run
   Ticket detail shows backend readiness.
   User clicks Start Governed Run.
   Run begins through backend/API path.
   Real dotnet build/test evidence appears.
   Run halts at PausedForApproval.

8. Critic/review
   Open critic package/review.
   See deterministic critic result or named finding.
   Disposition required finding if present.

9. Approval
   Approval screen shows run ID, package hash, review reference, and exact required approval phrase.
   User types phrase-bound approval.
   Backend records accepted approval.

10. Continuation/apply
   User requests continuation.
   User requests controlled apply.
   Backend verifies approval/continuation/apply boundaries.
   Ticket reaches Applied.

11. Final report
   Open final report.
   Report shows:
   - run ID
   - ticket ID
   - build/test evidence
   - critic package hash
   - critic review
   - accepted approval
   - continuation
   - apply receipt path/hash
   - final Applied status

12. Restart proof
   Kill/restart API.
   Reload UI.
   Tickets, runs, reports, and approval/apply history still exist.
```

Success means this path is boring.

Failure means any dead end, empty unexplained screen, hand-copied ID, fake state, hidden config, missing report, or lost state becomes a blocker.

## 6. Demo Data Specification

Before the demo starts, the system may contain seeded baseline history.

The seed must be produced by driving the product path, not by direct DB final-state inserts.

Required baseline:

```text
Tenant/demo user:
Created or resolved through documented setup/product path.

BookSeller project:
Registered/resolved through Projects API or release setup path.

Knowledge:
BookSeller README/docs/source profile/index visible through Documents/CodeIndex/Profile product paths.

Ticket 1:
validate-book
State: Applied
Created and driven through API/SQL governed path.
Must include run history, build/test evidence, critic review, accepted approval, continuation, apply receipt, and final report.

Ticket 2:
search-by-author
State: PausedForApproval
Created and driven only to gate.
Must be usable as a rehearsal/fallback gate example.
Must not be silently approved or applied.

Ticket 3:
Not seeded.
Created live during the demo from chat.
```

Seed cardinal rule:

```text
Seed baseline history through product APIs.
Never insert final state directly into SQL.
Never hardcode frontend rows.
Never fake receipts.
```

## 7. Demo Seed Command

Primary deliverable:

```text
Scripts/demo/demo-seed.ps1
```

Required modes:

```powershell
Scripts/demo/demo-seed.ps1 -CheckOnly
Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic
```

Optional mode, only if safe and scoped:

```powershell
Scripts/demo/demo-seed.ps1 -ResetDemoData -Project BookSeller
```

Reset must only affect demo-owned records and must refuse ambiguous deletion.

Required behavior:

```text
- Verify root safety before mutation.
- Refuse mutation if root safety is Blocked or NotEvaluated.
- Verify API availability.
- Verify SQL/API persistence mode.
- Verify BookSeller fixture exists.
- Create or resolve BookSeller project.
- Ingest/profile/index BookSeller knowledge.
- Create or resolve seeded tickets idempotently.
- Drive validate-book to Applied through governed path.
- Drive search-by-author to PausedForApproval only.
- Write a demo seed receipt.
- Redact secrets, tokens, connection strings, and raw user-local paths.
- Be safe to re-run without duplicating tickets blindly.
```

Forbidden behavior:

```text
- No direct DB final-state insert.
- No frontend fixtures.
- No fake build/test output.
- No fake critic package.
- No fake approval.
- No fake continuation.
- No fake apply receipt.
- No silent accepted approval.
- No live chat ticket seeded ahead of the demo.
- No source mutation outside controlled apply path.
```

Required reason codes:

```text
DemoApiUnavailable
DemoSqlPersistenceUnavailable
DemoRootSafetyBlocked
DemoRootSafetyNotEvaluated
DemoBookSellerMissing
DemoProjectResolveFailed
DemoKnowledgeSeedFailed
DemoTicketSeedFailed
DemoRunSeedFailed
DemoApprovalRequired
DemoApprovalPhraseMismatch
DemoContinuationFailed
DemoApplyFailed
DemoReportMissing
DemoIdempotencyConflict
DemoReceiptWriteFailed
```

Required receipt:

```text
Docs/receipts/DEMO1_API_DRIVEN_DEMO_SEED.md
```

Receipt must include:

```text
command
commit SHA
model mode
persistence mode
API base URL classification
root safety status
project ID
ticket IDs
run IDs
critic package hash for Applied ticket
critic review ID
accepted approval ID
approval target hash
continuation result
apply receipt path/hash
final report reference
idempotency result
redaction confirmation
known gaps
boundary statement
```

## 8. Demo Launch Command

Primary deliverable:

```text
Scripts/demo/demo-up.ps1
```

Required command shape:

```powershell
Scripts/demo/demo-up.ps1 -CheckOnly
Scripts/demo/demo-up.ps1 -Project BookSeller -ModelMode Deterministic
```

Optional:

```powershell
Scripts/demo/demo-up.ps1 -Project BookSeller -ModelMode Live
```

Required behavior:

```text
- Run release/local doctor checks.
- Verify root safety.
- Verify SQL availability.
- Verify API startup path.
- Verify UI startup path.
- Verify demo seed status.
- Provide one next safe action when blocked.
- Open or print the UI URL when ready.
```

The command may delegate to existing scripts. It must not fork a parallel setup stack.

Required terminal states:

```text
DemoReady
DemoBlocked
DemoSeedRequired
DemoRootSafetyBlocked
DemoSqlBlocked
DemoApiBlocked
DemoUiBlocked
DemoModelBlocked
```

Forbidden behavior:

```text
- No hidden SQL rebuild.
- No hidden Weaviate rebuild.
- No hidden Docker/service start unless explicitly documented and requested.
- No hidden smoke run unless requested.
- No hidden approval.
- No hidden apply.
- No release-ready claim.
```

## 9. UI Demo Quality Bar

Every route in the demo path must be honest.

The UI must show:

```text
API connection status
selected project
selected tenant/user context if applicable
model mode: Deterministic or Live
root safety status or link/status summary
readiness state
backend blocker reason codes
next safe action
report/receipt location where applicable
```

Every list/detail screen must support:

```text
populated state
loading state
empty-with-next-action state
blocked-with-reason-code state
error-with-safe-next-action state
```

Every disabled action must show:

```text
backend reason code
human-readable message
next safe action
```

The UI must not show:

```text
Approved when only halted.
Ready when only validated.
Applied when only completed.
Policy satisfied when only accepted approval exists.
Safe to release because smoke passed.
Critic reviewed when only critic package exists.
Continuation allowed because approval package exists.
Apply allowed because build is green.
```

No screen in the demo path may be a clickable dead end.

## 10. Required Demo Routes

The current shell may include:

```text
Home
Chat
Build
Tickets
Knowledge
Runs
Governance
Settings
```

For the demo, each route must either work honestly or be clearly flagged as experimental/out of path.

### Home

Must show:

```text
API status
SQL/persistence status if available
BookSeller selected/resolvable
demo readiness state
model mode
next safe action
```

### Knowledge

Must show:

```text
BookSeller project profile/index/docs evidence
empty state if not indexed
next action to seed/index if missing
```

### Tickets

Must show:

```text
seeded Applied ticket
seeded PausedForApproval ticket
new live ticket once created from chat
status from backend only
links to runs/reports
```

### Runs

Must show:

```text
run list
run detail
stage/status history
build/test evidence
gate state
critic package/review status
approval/continuation/apply status
```

### Chat

Must support:

```text
user requirement input
assistant shaping response
Promote to Work Item
draft ticket preview
```

Chat must not support:

```text
approval
continuation
apply
hidden ticket creation without explicit user action
```

### Build

If separate from Runs, must show:

```text
real build/test output or evidence references
stage status
blocked/error state
```

### Governance

Must show:

```text
approval records
critic review references
continuation/apply boundary history
evidence timeline if available
```

Governance must not imply approval from evidence.

### Settings

Must show:

```text
model mode/config status
API base classification
safe config status
redacted values only
```

Settings must not leak secrets.

## 11. Chat-To-Ticket Contract

Chat may propose work. It may not create authority.

Required flow:

```text
User sends messy requirement.
Assistant shapes it.
User explicitly clicks Promote to Work Item.
Backend creates or returns draft.
User edits title/summary/criteria.
User confirms criteria.
Backend persists ticket.
Ticket becomes startable only when backend readiness allows it.
```

Backend-owned status rule:

```text
ConfirmDraft must set backend-owned non-authority status.
It must ignore or reject authority-shaped client status.
```

Forbidden client-supplied statuses include:

```text
Approved
Applied
ReadyForApply
PolicySatisfied
SafeToRun
Completed
Accepted
ContinuationApproved
```

Chat provenance rule:

```text
Before persisted ticket/source references are created, backend must verify:
- chat session exists
- chat session belongs to project/tenant
- chat message exists
- message belongs to that session
- message belongs to project/tenant
```

If this is not verified, the system must call it client-supplied hints, not provenance.

Required tests:

```text
ConfirmDraft_IgnoresOrRejectsAuthorityShapedClientStatus
ConfirmDraft_WithValidChatProvenance_PersistsReferences
ConfirmDraft_WithMissingChatSession_Fails
ConfirmDraft_WithMissingChatMessage_Fails
ConfirmDraft_WithMessageFromDifferentSession_Fails
ConfirmDraft_WithChatFromDifferentProject_Fails
ChatCannotCreateAcceptedApproval
ChatCannotRequestContinuation
ChatCannotRequestApply
```

## 12. Governed Run UI Journey

Required flow:

```text
Ticket detail
-> backend readiness
-> start governed run
-> build/test evidence
-> PausedForApproval
-> critic package/review
-> finding disposition if required
-> phrase-bound accepted approval
-> continuation
-> controlled apply
-> final report
```

No hand-carried IDs:

```text
The UI must carry run/ticket/package/report references through links or backend lookups.
The user must not copy run IDs or hashes between screens except typing the explicit approval phrase when required.
```

Approval screen must show:

```text
ticket ID/key
run ID
critic package hash
critic review reference
required phrase
current backend blocker if approval is not allowed
```

Apply screen must show:

```text
continuation status
approval consumed
target run/package
apply eligibility
apply result
apply receipt path/hash
```

Final report must show:

```text
ticket
run
build/test evidence
critic package
critic review
finding dispositions
accepted approval
continuation
apply receipt
final status
known warnings
```

## 13. Repeatability Contract

The demo must not be a one-shot golden path.

After seed and after one live demo ticket, the user must be able to create another ticket and repeat the run path.

Acceptance:

```text
- New ticket can be created from chat or Tickets route.
- New run can be started without clearing hidden state.
- Build/test output appears from real execution.
- Gate is reached.
- Report history remains queryable.
- Restart does not lose prior state.
```

Forbidden:

```text
- demo can only run once
- duplicate key conflicts without recovery
- reset required between tickets
- hidden local state required
- direct DB cleanup required
- manual ID copying required
```

Reason codes:

```text
DemoRepeatRunBlocked
DemoDuplicateTicketConflict
DemoHiddenStateConflict
DemoManualResetRequired
DemoRestartPersistenceFailed
```

## 14. Persistence/Restart Proof

The demo must prove persistence.

Required restart check:

```text
1. Complete seeded baseline.
2. Complete or start live demo ticket.
3. Stop API.
4. Start API.
5. Reload UI.
6. Confirm tickets/runs/reports still load.
```

Required persisted state:

```text
project
knowledge/index references
tickets
runs
run events
critic package reference
critic review
finding dispositions
accepted approval
continuation result
apply receipt reference
final report reconstruction
```

Failure reason codes:

```text
PersistedProjectMissingAfterRestart
PersistedTicketMissingAfterRestart
PersistedRunMissingAfterRestart
PersistedEventMissingAfterRestart
PersistedApprovalMissingAfterRestart
PersistedApplyReceiptMissingAfterRestart
ReportReconstructionFailedAfterRestart
```

## 15. Demo Implementation Roadmap

The demo spec is one canonical document. Implementation must still be small PRs.

Recommended order:

```text
DEMO-0  Add this demo-real specification document.
DEMO-1  API-driven demo seed command.
DEMO-2  Chat -> confirmed ticket visible/startable in Tickets.
DEMO-3  Governed run UI journey to Applied.
DEMO-4  Screen state/dead-end UX hardening across demo routes.
DEMO-5  One-command demo-up.
DEMO-6  Live model banner/path decision.
DEMO-7  Non-author rehearsal transcript.
DEMO-8  DOGFOOD-ALPHA-LOCAL-001 release gate artifact.
DEMO-9  Optional BookSeller three-ticket batch artifact.
```

Do not combine all implementation into one heroic PR.

## 16. DEMO-0 - Canonical Demo Spec

Purpose:

```text
Add this document as the canonical demo-real contract.
```

Suggested path:

```text
Docs/release/v0.1-local-alpha/DEMO_REAL_SPEC.md
```

Suggested title:

```text
docs(release): define v0.1 demo-real contract
```

Allowed files:

```text
Docs/release/v0.1-local-alpha/DEMO_REAL_SPEC.md
Docs/receipts/DEMO0_DEMO_REAL_SPEC.md
```

Acceptance:

```text
Spec clearly distinguishes fixture from fake outcome.
Spec defines 12-minute demo script.
Spec defines seeded baseline and live ticket creation.
Spec bans direct DB final-state inserts and frontend fixtures.
Spec defines repeatability contract.
Spec contains no release-ready claim.
```

## 17. DEMO-1 - API-Driven Demo Seed

Purpose:

```text
Create baseline demo history by driving product APIs and governed backend paths.
```

Suggested title:

```text
demo(local): seed BookSeller demo history through product APIs
```

Allowed files:

```text
Scripts/demo/demo-seed.ps1
Docs/receipts/DEMO1_API_DRIVEN_DEMO_SEED.md
IronDev.IntegrationTests/Demo/*
IronDev.IntegrationTests.Api/Demo/*
```

Required tests:

```text
DemoSeed_CheckOnly_DoesNotMutate
DemoSeed_BlocksWhenRootSafetyNotEvaluated
DemoSeed_BlocksWhenRootSafetyBlocked
DemoSeed_UsesProductApisForGovernedActions
DemoSeed_DoesNotInsertFinalSqlState
DemoSeed_IsIdempotent
DemoSeed_CreatesAppliedTicketHistory
DemoSeed_CreatesPausedForApprovalTicket
DemoSeed_DoesNotCreateLiveChatTicket
DemoSeed_ReceiptRedactsSecrets
DemoSeed_ReportReconstructsFromSql
```

Acceptance:

```text
Fresh DB + seed gives visible baseline state across Home, Knowledge, Tickets, Runs, Governance, and Reports.
Seeded history is real backend/API/SQL history.
No fake outcome exists.
```

## 18. DEMO-2 - Chat To Confirmed Visible Ticket

Purpose:

```text
Make the live demo ticket creation path work from Chat through confirmed persisted ticket.
```

Suggested title:

```text
demo(flow): create visible BookSeller ticket from chat
```

Required behavior:

```text
- Chat requirement can produce a draft.
- Draft can be edited.
- Criteria must be confirmed.
- Confirmed ticket appears in Tickets.
- Ticket detail can be opened.
- Backend readiness is shown.
- Ticket can start a governed run when ready.
```

Acceptance:

```text
Demo steps 5, 6, and start of 7 work without manual fixture editing or ID copying.
```

## 19. DEMO-3 - Governed Run UI Journey

Purpose:

```text
Wire the UI path from ticket readiness to Applied and final report.
```

Suggested title:

```text
demo(flow): complete governed run journey from ticket to applied report
```

Required behavior:

```text
- Start governed run from ticket.
- Show build/test evidence.
- Show PausedForApproval.
- Show critic package/review.
- Allow finding disposition if required.
- Record phrase-bound approval.
- Request continuation.
- Request controlled apply.
- Show final report.
```

Acceptance:

```text
Demo steps 7 through 11 work through UI/API/backend state, with no hand-copied IDs except the explicit approval phrase.
```

## 20. DEMO-4 - Screen State/Dead-End Hardening

Purpose:

```text
Make all demo-path screens honest under populated, loading, empty, blocked, and error states.
```

Suggested title:

```text
demo(ux): harden demo route states and blocker messaging
```

Required behavior:

```text
- Every route in the demo path has populated/loading/empty/blocked/error behavior.
- Every disabled control shows backend reason and next action.
- Deterministic/live model mode is visible.
- Routes not ready for the demo are hidden or flagged experimental.
- Cross-links connect ticket/run/report/governance surfaces.
```

Acceptance:

```text
No empty screen without a reason.
No dead end in the demo path.
No UI state stronger than backend truth.
```

## 21. DEMO-5 - One-Command Demo Up

Purpose:

```text
Make demo startup boring.
```

Suggested title:

```text
demo(local): add one-command v0.1 demo startup
```

Required behavior:

```text
- Check root safety.
- Check SQL.
- Start or verify API.
- Start or verify UI.
- Check demo seed.
- Print/open app URL.
- Give one next safe action if blocked.
```

Acceptance:

```text
A fresh developer can reach the demo start state from documented commands without archaeology.
```

## 22. DEMO-6 - Live Model Or Honest Deterministic Banner

Purpose:

```text
Decide what the demo honestly says about model mode.
```

Allowed outcomes:

```text
Live model to Gate works and is opt-in.
```

or:

```text
Demo is deterministic-only local alpha preview and says so visibly.
```

Forbidden:

```text
- Silent fallback from live to deterministic.
- Hiding deterministic mode.
- Calling deterministic fixture a live model run.
```

Acceptance:

```text
The viewer always knows whether the run is deterministic or live.
```

## 23. DEMO-7 - Non-Author Rehearsal

Purpose:

```text
Prove someone other than the author can run the demo.
```

Suggested title:

```text
demo(dogfood): record v0.1 demo rehearsal transcript
```

Required record:

```text
Docs/dogfood/DEMO-REHEARSAL-001.md
```

Required verdicts:

```text
DemoReady
Blocked
RepeatabilityFailed
SafetyBlocked
PersistenceBlocked
UxBlocked
DocsBlocked
```

Transcript must include:

```text
commit SHA
OS/shell
.NET version
Node/npm version
Git version
SQL status
Weaviate status if applicable
root safety status
API start command
UI start command
model mode
project ID
seeded ticket IDs
live ticket ID
run ID
critic package hash
approval ID
continuation result
apply receipt path/hash
final report path
all blockers
manual fixes
repeatability verdict
```

Acceptance:

```text
One non-author can run the 12-minute demo path from fresh checkout or clearly records why not.
```

## 24. DEMO-8 - DOGFOOD-ALPHA-LOCAL-001 Gate Artifact

Purpose:

```text
Record the first explicit local-alpha dogfood gate artifact without turning evidence into release authority.
```

Suggested title:

```text
demo(dogfood): add DOGFOOD-ALPHA-LOCAL-001 gate artifact
```

Required artifacts:

```text
Docs/release/v0.1-local-alpha/DOGFOOD-ALPHA-LOCAL-001.md
Docs/release/v0.1-local-alpha/DOGFOOD-ALPHA-LOCAL-001.json
```

Allowed verdicts:

```text
GoForLocalAlphaPreview
NoGoBlocked
ConditionalGoWithNamedLimitations
EvidenceIncomplete
```

Acceptance:

```text
The artifact records command order, doctor/smoke/final-state status, evidence refs, limitations, blockers, one next safe action, and a boundary statement.
If the gate is not actually executed, the verdict must be EvidenceIncomplete and must not claim release readiness.
```

Review line:

```text
A dogfood gate is evidence with a verdict. It does not grant release authority by itself.
```

Killjoy:

```text
If the release gate cannot be repeated from the runbook, it is not a gate. It is a diary entry.
```

## 25. DEMO-9 - Optional BookSeller Three-Ticket Batch Artifact

Purpose:

```text
Represent the optional three-ticket BookSeller batch only after the single-ticket dogfood gate is boring.
```

Suggested title:

```text
demo(dogfood): add optional BookSeller batch artifact
```

Required artifacts:

```text
Docs/release/v0.1-local-alpha/DOGFOOD-BOOKSELLER-BATCH-001.md
Docs/release/v0.1-local-alpha/DOGFOOD-BOOKSELLER-BATCH-001.json
```

Required tickets:

```text
validate-book
normalise-book-metadata
reject-duplicate-isbn
```

Allowed per-ticket verdicts:

```text
TicketApplied
TicketPausedForApproval
TicketBlocked
TicketFailed
TicketDescoped
```

Allowed aggregate verdicts:

```text
BatchPassed
BatchBlocked
BatchFailed
BatchPartiallyPassed
BatchEvidenceIncomplete
```

Acceptance:

```text
The artifact names exactly three tickets, keeps their run/approval/apply/report evidence independent, and never lets aggregate success hide a ticket failure.
If DEMO-8 is EvidenceIncomplete, DEMO-9 must stay BatchEvidenceIncomplete and must not invent run IDs, approvals, apply receipts, or final reports.
```

Review line:

```text
Batch proof is confidence evidence. It is not a substitute for a boring single-ticket release path.
```

Killjoy:

```text
Three tickets do not make the product mature. They only prove the first ticket was not a lucky accident.
```

## 26. Do-Not-Ship Demo List

Do not claim demo-real if any of these are true:

```text
BookSeller screen data is hardcoded in frontend.
Seed uses direct DB final-state inserts.
Build output is fake.
Test output is fake.
Critic package is fake.
Critic review is fake.
Accepted approval is silently created.
Apply receipt is fake.
Client can set authority-shaped ticket status.
Chat provenance is not server-verified but presented as provenance.
UI says Applied when backend only says Completed.
UI says Approved when backend only says PausedForApproval.
User must copy hidden IDs between screens.
A route in the demo path opens empty with no next action.
API restart loses ticket/run/report state.
Demo only works once.
Known limitations are hidden.
```

## 25. Final Acceptance

The v0.1 demo is acceptable when:

```text
BookSeller fixture exists.
Demo seed creates real baseline history through product APIs.
Home, Knowledge, Tickets, Runs, Governance, Chat, and Reports show real backend state.
A live demo ticket can be created from chat.
A governed run can start from that ticket.
Real build/test evidence is visible.
Run halts at human gate.
Critic review/finding path is visible.
Accepted approval is recorded with backend-bound phrase/hash.
Continuation and controlled apply are requested separately.
Ticket reaches Applied.
Final report reconstructs the chain.
API restart preserves state.
Another ticket can be created and run again.
A non-author rehearsal transcript exists.
```

Final killjoy line:

```text
The demo is not successful because it looked good once. It is successful when the next developer can run it, understand it, and repeat it without archaeology.
```
