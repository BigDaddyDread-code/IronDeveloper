# IronDev Future UX Product Spec

**Status:** Committed planning spec (FUTURE-0)
**Scope:** v0.2+ product and UX direction
**Audience:** product, engineering, architecture review, future UI implementation, dogfood operators
**Core principle:** UX must expose backend truth. UX must not invent authority.

---

## Amendments (2026-07-07, FUTURE-0 review)

This spec is committed together with `Docs/ux/full-ux-map.md`, which is the full-experience
design pass over it. Where the two differ, the following resolutions apply:

```text
1. Delivery vehicle    The existing Tauri + React shell (IronDev.TauriShell). No second frontend.
2. Information arch.   Section 7's noun navigation is superseded by the flow-first pipeline IA
                       (Board / Work Item / Library — see flow-first-ux-spec.md and full-ux-map).
                       The screen contracts in Sections 8–22 remain binding inside that IA.
3. Affordance contract Every screen's actions come from a backend-supplied allowed-actions
                       envelope { action, allowed, reason, nextSafeAction }. The UI renders
                       eligibility; it never derives it. NotImplemented is a first-class refusal
                       reason: unbuilt screens ship as real routes returning real 501 envelopes.
                       No mock mode, ever.
4. Multi-user first    Separation of duties is the default (SelfApprovalProhibited). A solo
                       operator is a team of one under an explicit, visibly-labeled policy
                       exception. See full-ux-map Section 2.
5. Role scoping        All eight roles exist in the data model from the start. v0.2 UI makes
                       Owner + Developer real; the rest render honestly as planned.
6. Autonomy dial       Section 19 generalizes to a configurable human-intervention dial
                       (Levels 0–3, full-ux-map Section 9.6): teams decide how much human
                       intervention they want, from approval at every gate to a delegated
                       happy path. Default remains Level 0 (hands-on). The dial changes who
                       satisfies a gate, never whether the gate exists.
7. Integrations        Azure DevOps and Slack integrate as clients, never authority
                       (full-ux-map Section 9.5). Slack can tell you; Slack cannot decide.
8. Roadmap             Section 24 adjusted: TEAM-0 (tenant scope + role matrix) gates any
                       second human; INTEG-ADO-0/1 and INTEG-SLACK-0 follow TEAM-0;
                       DOGFOOD-2 starts the moment PROJECT-3 lands; AUTH-* implementation
                       stays deferred until dogfooding demands it.
```

---

## 1. Purpose

This document describes the future IronDev user experience required to move from a credible
local-alpha proof into a usable governed engineering product.

IronDev is becoming a governed engineering cockpit:

> A user can bring a repository, describe work, shape a ticket, run governed build/test,
> inspect failures, see bounded repair attempts, review critic findings, record human
> decisions, approve consciously, apply safely, and inspect the full evidence trail.

The UX must make the system understandable without weakening the authority model.

The product must answer four user questions at all times:

```text
What happened?
Why did it stop?
What evidence exists?
What am I safely allowed to do next?
```

---

## 2. Product North Star

IronDev turns messy human intent into reviewable, buildable, critic-reviewed, repairable,
human-approved engineering work.

The UX should support this path:

```text
Project imported
-> architecture/profile confirmed
-> ticket created from chat or form
-> governed run started
-> build/test evidence produced
-> failure classified if needed
-> bounded repair attempted if configured
-> critic package prepared
-> critic review recorded
-> findings dispositioned
-> human approval recorded
-> continuation requested
-> controlled apply performed
-> final report reconstructed
```

The UX is not an authority source. It is a readable cockpit over backend-owned state.

---

## 3. Current Proven Spine

The current system has established the following product spine:

```text
BookSeller fixture
demo seed
running API path
live model proof
critic route
finding disposition
human approval gate
bounded build/test repair
controlled apply
final report reconstruction
```

This proves the loop is real enough to dogfood, but not mature enough to hide complexity.

Important truths:

```text
Proof of life is not reliability.
Green CI is evidence, not authority.
Repair is proposal-shaped work, not autonomy.
A report is reconstruction, not permission.
A UI state is not backend truth.
```

---

## 4. Non-Negotiable UX Authority Boundaries

The UX must never blur these boundaries.

| Thing | What it is | What it is not |
|---|---|---|
| Proposal | Review material | Approval |
| Build/test output | Evidence | Permission |
| Repair attempt | New proposal attempt | Autonomy |
| Critic finding | Review finding | Veto |
| Finding disposition | Human response to a finding | Approval |
| Accepted approval | Human/delegated approval evidence | Apply permission by itself |
| Continuation | Move from gate to completed state | Source apply |
| Apply | Controlled copy-only mutation | Commit, push, release, deploy |
| Final report | Evidence reconstruction | Authority |
| UI button | Request to backend | Permission by itself |
| Green CI | Evidence | Merge/release approval |

Killjoy line:

```text
The UI can request. The backend decides. The evidence explains.
```

---

## 5. Primary User Types

### Owner

Owns tenant/project authority.

Can:

```text
create tenant/project
assign admins
configure authority policy
view all evidence
disable delegated authority
```

Cannot:

```text
silently bypass run gates
rewrite evidence
turn reports into approval
```

### Admin

Manages project setup and configuration.

Can:

```text
provision projects
configure build/test commands
configure safe paths
configure repair budgets
configure model/provider settings
create approval profiles if allowed by owner
```

### Developer

Creates work and runs governed loops.

Can:

```text
create tickets
start runs
inspect evidence
respond to blocked states by editing tickets
request review/approval
```

Cannot:

```text
approve own work unless policy explicitly permits
force repair outside budget
force apply
```

### Reviewer / Critic Operator

Reviews packages and findings.

Can:

```text
record critic review
view package evidence
record finding details
verify ground-truth checks
```

### Approver

Records accepted approval or delegated policy decisions.

Can:

```text
approve continuation when evidence satisfies policy
approve named risk with reason
```

Cannot:

```text
approve missing critic package
approve undispositioned findings
approve expired/stale package
```

### Auditor

Reads evidence only.

Can:

```text
view runs
view reports
view approvals
view repair attempts
view audit log
```

Cannot mutate.

### Runner / Service Account

Executes backend jobs.

Can:

```text
run configured jobs
write system events
package evidence
```

Cannot:

```text
approve
disposition findings
create delegated authority
```

---

## 6. UX Design Principles

### 6.1 Backend Truth First

Every screen must show the backend state that it is reading from.

Examples:

```text
Run state: PausedForApproval
Source: GET /api/projects/{projectId}/tickets/{ticketId}/skeleton-runs/{runId}/report
Last refreshed: timestamp
```

### 6.2 No Dead Buttons

Every disabled action must explain why.

Bad:

```text
[Continue] disabled
```

Good:

```text
[Continue] disabled
Reason: critic review missing.
Next safe action: request critic review.
```

### 6.3 No Hidden Repair

A repaired run must not look clean.

It must say:

```text
Attempt 1 failed.
Attempt 2 repaired and passed.
Final proposal under review: proposal-repair-2.json.
Original failed proposal preserved: proposal.json.
```

### 6.4 Evidence Before Optimism

Show evidence before success labels.

```text
Build passed
Test passed
Critic package hash verified
Approval target hash matched
Apply receipt chain exists
```

### 6.5 No Authority Theatre

Avoid labels like:

```text
Auto approved
AI approved
Ready because green
Safe because repaired
```

Use:

```text
Delegated approval policy matched
Accepted approval verified
Backend readiness satisfied
Evidence complete
```

---

## 7. Information Architecture

> **Superseded by Amendment 2:** navigation follows the flow-first pipeline IA
> (Board / Work Item / Library). The sections below survive as the required *capability
> areas* the pipeline IA must expose; their per-screen truth requirements (Section 22)
> remain binding.

Capability areas the product must cover:

```text
Home (Board)        current system state, attention queue, next safe actions
Projects            import, provisioning, readiness
Tickets             create and shape work before running it
Runs                governed work through build, repair, review, approval, apply
Reviews             critic packages and findings
Approvals           approval evidence and requirements
Reports             reconstructed run truth
Admin               users, roles, provisioning, authority profiles
Settings            user / project / authority / runtime, kept separate
Audit               who did what, when, under what authority, with what evidence
```

---

## 8. Project Provisioning UX

Project provisioning is the bridge from BookSeller demo to real use.

### 8.1 Provisioning Journey

```text
Create project
-> select local repo path
-> root safety check
-> repo scan
-> architecture/profile detection
-> build/test command detection
-> pointed questions
-> human confirmation
-> architecture decision record
-> readiness result
-> first ticket allowed
```

### 8.2 Project Provisioning Screen

Required fields:

```text
Project name
Repo path
Default branch
Language/runtime
Architecture style
Build command
Test command
Source folders
Test folders
Docs folders
Generated-code paths
Forbidden mutation paths
Allowed mutation paths
Workspace root
Evidence root
Model profile
Repair budget
Approval profile
```

### 8.3 Readiness States

```text
ReadyToRun
BlockedMissingRepoPath
BlockedUnsafeRepoPath
BlockedMissingBuildCommand
BlockedMissingTestCommand
BlockedUnknownArchitecture
BlockedForbiddenPathConfigMissing
BlockedApprovalProfileMissing
BlockedExternalDependencyMissing
BlockedDirtyRepo
```

### 8.4 Pointed Questions

The wizard should ask only what detection cannot prove.

Examples:

```text
Which project is the main application?
Which command builds this repo?
Which command runs tests?
Which folders may IronDev edit?
Which folders must never be edited?
Are generated files present?
Does this project require SQL, Redis, queues, storage, or external APIs?
Who can approve continuation?
Is bounded repair allowed for this project?
```

Killjoy line:

```text
A folder path is not a project. A project is a repo with build, test, safety, and readiness evidence.
```

---

## 9. Architecture Wizard UX

The architecture wizard should create a backend-owned profile, not a frontend-only form.

### 9.1 Inputs

```text
repo scan evidence
detected solution/project files
README/docs evidence
build/test command candidates
folder structure
CI files
migration/database folders
user answers
```

### 9.2 Outputs

```text
ProjectArchitectureProfile
ProjectArchitectureDecisionRecord
ProjectReadinessResult
```

### 9.3 UX Pattern

Each detected item should have a confidence label:

```text
Detected
Needs confirmation
Missing
Rejected
Human supplied
```

Example:

```text
Build command
Detected: dotnet build BookSeller.slnx
Confidence: High
User action: Confirm / Edit
```

### 9.4 Forbidden UX

Do not display:

```text
Architecture understood
AI knows the project
Ready because scan completed
```

Use:

```text
Profile proposed
Profile confirmed
Readiness satisfied
Unknowns remain
```

---

## 10. Ticket UX

### 10.1 Ticket Creation Modes

```text
Chat-shaped ticket
Manual ticket form
Imported issue
Template-based ticket
```

### 10.2 Required Ticket Fields

```text
title
summary
problem
proposed change
acceptance criteria
linked files
provenance
readiness result
```

### 10.3 Ticket Readiness Panel

Shows:

```text
linked project
linked files
missing acceptance criteria
build/test readiness
forbidden path risk
architecture profile status
start-run eligibility
```

### 10.4 Ticket Actions

Allowed:

```text
save draft
confirm ticket
link files
evaluate readiness
start governed run if ready
```

Forbidden:

```text
start run from unconfirmed ticket
start run when readiness blocked
seed final state
fake linked files
client-supplied authority status
```

---

## 11. Run Cockpit UX

The run cockpit is the main product screen.

### 11.1 Header

Shows:

```text
Run ID
Project
Ticket
Status
Current node
Requires human action
Model mode
Repair budget
Last event
```

### 11.2 Timeline

Shows ordered backend events:

```text
RunStarted
ProposalGenerated
TestsAuthored / TestAuthoringSkipped
DisposableWorkspaceCreated
DisposableCommandStarted
DisposableCommandCompleted / Failed
SkeletonEvidencePackaged
SkeletonRepairAttemptStarted
SkeletonRepairProposalGenerated
CriticReviewPackageReady
ApprovalRequiredHalt
SkeletonContinuationUnblocked
SkeletonApplyStarted
SkeletonApplied
SkeletonRunBlocked
```

### 11.3 Current State Panel

Examples:

```text
PausedForApproval
Reason: critic package ready; approval required.
Next safe action: request critic review or record approval after review.

Failed
Reason: RepairBudgetExhausted.
Next safe action: inspect failed attempts, refine ticket, or start a new run.

Applied
Reason: governed apply spine completed.
Next safe action: inspect final report; commit/push/release remain separate.
```

### 11.4 Evidence Panel

Shows:

```text
proposal evidence
build/test logs
critic package
package hash
repair proposals
apply receipts
final report gaps
```

---

## 12. Repair UX

Repair visibility is the next important product step.

### 12.1 Repair Summary

If a run repaired, show:

```text
This run required repair.
Attempt 1 failed: BuildFailed
Attempt 2 repaired and passed.
Final proposal under review: proposal-repair-2.json
Original failed proposal preserved: proposal.json
```

### 12.2 Repair Attempt Card

Each attempt card should show:

```text
attempt number
attempt type: initial / repair
failure kind
failed command
bounded excerpt
workspace path
evidence path
proposal id
proposal evidence file
succeeded / failed
```

### 12.3 Final Proposal Binding

The UX must show:

```text
Proposal under review: proposal-repair-2.json
Initial failed proposal: proposal.json
Approval package binds to: proposal-repair-2.json
```

### 12.4 Repair Actions

For now:

```text
No client-triggered repair button.
No manual "repair now" button.
Repair happens only inside server-orchestrated bounded run.
```

Future possible action:

```text
Start new run with increased repair budget
```

but only if the project policy allows it.

### 12.5 Repair Warnings

Display:

```text
Repair does not approve the run.
Repair does not reduce review requirements.
Repair history is preserved.
A repaired run still requires critic review and accepted approval.
```

Killjoy line:

```text
If the UI hides the failed attempt, repair becomes laundering.
```

---

## 13. Critic Review and Findings UX

### 13.1 Critic Package Screen

Shows:

```text
package id
package path
package hash
hash verified
proposal id
proposal evidence file
build/test evidence
criterion coverage
uncovered criteria
repair attempts
```

### 13.2 Critic Review Screen

Shows:

```text
critic model/provider
verdict
finding count
blocking finding count
ground truth check count
ground truth mismatch count
package hash reviewed
```

### 13.3 Finding Card

Each finding shows:

```text
finding id
severity
title
problem
why it matters
required fix
blocks merge?
disposition status
disposition reason
decided by
timestamp
```

### 13.4 Disposition Actions

Allowed dispositions:

```text
AcceptRisk
RejectFinding
DeferFix
FixRequired
```

Each must require a reason.

### 13.5 Continuation Rules

The UX must show:

```text
Continuation refuses if critic review missing.
Continuation refuses if findings are undispositioned.
A finding is not a veto.
A disposition is not approval.
Approval does not bypass findings.
```

---

## 14. Approval UX

### 14.1 Approval Requirement Panel

Shows:

```text
approval target kind
approval target id
approval target hash
capability code
approval purpose
expires at
required actor/role
package hash verified
critic review present
findings dispositioned
```

### 14.2 Approval Action

When recording approval, require:

```text
actor
reason
scope
expiry
target hash confirmation
```

### 14.3 Delegated Approval Eligibility

If delegated authority exists, show:

```text
policy matched / refused
policy id
policy name
scope
reason code
expiry
remaining uses
```

Do not say:

```text
Auto approved
```

Say:

```text
Delegated approval policy matched.
```

### 14.4 Refusal Reasons

```text
CriticReviewMissing
UndispositionedFindings
MissingOrUnsatisfiedApproval
ApprovalExpired
PackageHashMismatch
StaleAfterUpstreamApply
CriticPackageEvidenceMissing
DelegatedApprovalScopeMismatch
DelegatedApprovalPathViolation
```

---

## 15. Apply UX

### 15.1 Apply Preflight

Before apply, show:

```text
run status
package hash
approval status
critic review status
finding disposition status
mutation lease status
apply receipt chain expectation
```

### 15.2 Apply Button

Enabled only when backend says eligible.

Disabled examples:

```text
Apply disabled: run is PausedForApproval, not Completed.
Apply disabled: accepted approval no longer satisfies package hash.
Apply disabled: mutation lease held by another run.
Apply disabled: critic package missing.
```

### 15.3 Apply Result

Show stages:

```text
promotion-package
promotion-approval
apply-preflight
apply-dry-run
apply-copy
```

Each stage:

```text
succeeded / blocked
summary
errors
receipt path
```

### 15.4 Post-Apply Boundary

Display:

```text
Applied means copy-only source mutation completed.
It is not commit.
It is not push.
It is not PR.
It is not release.
```

---

## 16. Final Report UX

The final report must be a readable reconstruction of backend truth.

Sections:

```text
Summary
Timeline
Initial proposal
Final proposal
Repair attempts
Build/test evidence
Critic package
Critic reviews
Finding dispositions
Approval requirement
Accepted approval consumed
Apply chain
Receipts
Gaps
Boundary statement
```

Report status:

```text
Complete
IncompleteWithNamedGaps
FailedWithEvidence
Applied
```

The report must name missing links. It must never patch over gaps.

---

## 17. Admin, Users, and Roles UX

### 17.1 User Management

Required screens:

```text
user list
invite user
disable user
assign role
assign project membership
view authority assignments
view audit history
```

### 17.2 Roles

Minimum roles:

```text
Owner
Admin
Developer
Reviewer
Approver
Auditor
Runner
Viewer
```

### 17.3 Role Assignment Rules

Show exactly what each role can do.

Example:

```text
Approver can record accepted approval.
Approver cannot apply source unless also granted apply-request permission.
Runner can execute backend jobs.
Runner cannot approve.
Developer can create tickets and start runs.
Developer cannot approve own run unless policy explicitly permits.
```

Killjoy line:

```text
A user account is not authority. A role assignment plus a scoped policy is authority.
```

---

## 18. Project Settings UX

Project settings should be separated from user preferences.

### 18.1 Project Runtime Settings

```text
repo path
workspace root
evidence root
build command
test command
default branch
model mode
model provider
repair budget
```

### 18.2 Project Safety Settings

```text
allowed mutation paths
forbidden paths
generated-code paths
secret paths
external dependency requirements
root safety status
dirty repo policy
```

### 18.3 Project Authority Settings

```text
approver roles
reviewer roles
approval profile
delegated continuation policy
delegated apply policy, future only
critic requirement profile
human-intervention dial (Amendment 6; full-ux-map Section 9.6)
```

### 18.4 Dangerous Settings

These require warning, reason, and audit:

```text
workspace root
evidence root
allowed mutation paths
forbidden paths
repair budget
model provider
approval profiles
delegated authority
human-intervention dial loosening
AllowBuilderApply
```

---

## 19. Approval Profiles and Delegated Authority UX

Do not call this "automatic approval" in the UI.

Use:

```text
Delegated approval profile
Pre-authorized continuation policy
```

> **Amendment 6:** these profiles are the machinery under the human-intervention dial
> (Levels 0–3, full-ux-map Section 9.6). Teams choose how much intervention they want;
> every level expands into the inspectable policy fields below. The default is unchanged.

### 19.1 Default

```text
No delegated approval.
Every continuation/apply requires explicit human approval.
```

### 19.2 Approval Profile Fields

```text
name
scope
tenant
project
enabled
created by
allowed actors
target kind
capability code
allowed paths
forbidden paths
max changed files
max repair attempts allowed
require no critic findings
require all findings dispositioned
require build passed
require tests passed
require package hash verified
require no uncovered criteria
expiry
max uses
reason required
audit label
```

### 19.3 Delegated Continuation

Allowed later when constrained.

Example:

```text
Allow continuation when:
- build passed
- tests passed
- critic verdict NoObjection
- no findings
- no repair attempts, or repair attempts allowed by policy
- package hash verified
- changed paths inside allowed scope
- approval profile not expired
```

### 19.4 Delegated Apply

Do not build soon.

If ever allowed:

```text
must be narrow
must be short-lived
must exclude risky paths
must require no critic findings
must require no stale state
must not include commit/push/release
must be fully audited
```

Recommendation:

```text
v0.2/v0.3 may support delegated continuation.
Do not support delegated apply yet.
```

Killjoy line:

```text
Automatic approval is hidden authority unless a human scoped it, the backend enforced it,
and the audit trail explains exactly why it was consumed.
```

---

## 20. Model and Provider Settings UX

Model settings are dangerous because model behavior affects proposals, tests, critic reviews,
and repair attempts.

Show:

```text
provider
model name
mode: deterministic / live
scope: builder / tester / critic
API key source status, never the key
last successful live proof
known limitations
```

Required warnings:

```text
Changing model provider changes output quality.
Live model pass is proof of life, not reliability.
Model output is proposal/review evidence, not authority.
```

---

## 21. Audit UX

Audit should answer:

```text
Who did what?
When?
Against which run/ticket/project?
Under what role/profile?
What evidence existed?
What backend decision happened?
What reason was supplied?
```

Audit events should cover:

```text
project created
profile confirmed
settings changed
repair budget changed
approval profile changed
run started
repair attempted
critic review recorded
finding dispositioned
approval recorded
delegated policy consumed/refused
continuation requested
apply requested
apply refused/completed
```

---

## 22. Required Backend Truth Per Screen

| Screen | Backend truth required |
|---|---|
| Home | doctor status, active run summaries, blocked states |
| Project list | project readiness, profile status |
| Project provisioning | repo scan, profile detection, root safety |
| Ticket detail | ticket state, linked files, readiness |
| Run cockpit | run report, timeline, state |
| Repair panel | repair attempts from durable events |
| Critic package | package path/hash/evidence refs |
| Findings | critic review events and disposition events |
| Approval | approval requirement and accepted approvals |
| Apply | apply eligibility and receipt chain |
| Report | reconstructed run evidence |
| Admin users | users, roles, memberships |
| Approval profiles | policy definitions and audit |
| Settings | project/runtime/authority settings |

---

## 23. v0.2 UX Acceptance Criteria

v0.2 should be considered successful when:

```text
A user can import/provision a second small repo.
The system detects or asks for build/test configuration.
The user can create a ticket.
The user can start a governed run.
If build fails, the user can see failure evidence.
If bounded repair is configured, the user can see repair attempt history.
If repair succeeds, the user sees the repaired proposal as the final proposal.
The run halts at the human gate.
The user can see critic/finding/approval requirements.
The user can apply only through backend-governed paths.
The final report reconstructs the run without hiding gaps.
```

---

## 24. UX Roadmap Slices

Recommended PR order (as amended):

```text
FUTURE-0 — commit this spec + full-ux-map (this PR)
UX-0 — screen/state inventory against backend truth
AFFORDANCE-1 — allowed-actions envelope + NotImplemented pattern, proven on one screen
NAV-1 — full IA visible; unbuilt surfaces as honest 501 routes
REPAIR-2 — repair attempts visible in run report/UI
CRITIC-UX-1 — critic package and findings screen
APPROVAL-UX-1 — approval requirement screen
APPLY-UX-1 — apply receipt chain screen
PROJECT-0 — project provisioning/profile contract
PROJECT-1 — repo scan and root safety UI
PROJECT-2 — architecture wizard pointed questions
PROJECT-3 — readiness result and blockers UI
DOGFOOD-2 — second repo testing cycle (starts the moment PROJECT-3 lands)
TEAM-0 — tenant-scope proof + role/visibility matrix + invite/role backend
INTEG-ADO-0 — import ADO work item as draft ticket
INTEG-ADO-1 — outbound status mirror as pointers to truth
INTEG-SLACK-0 — "waiting on you" digest with deep links
AUTH-0 — approval profile contract        (when dogfooding demands)
AUTH-1 — delegated continuation evaluator (when dogfooding demands)
AUTH-2 — delegated authority audit log
ADMIN-0 — user/role model spec
ADMIN-1 — project settings screen
```

---

## 25. Do Not Build Yet

Do not build these until the above is boring:

```text
autonomous multi-agent loops
automatic apply
automatic commit/push/release
delegated apply
memory-driven mutation
team marketplace
cloud deployment
batch default execution
self-improving agents
hidden repair retries
```

---

## 26. Final UX Killjoy Lines

```text
The UI is not authority.
A report is not approval.
A repair is not success unless the repaired proposal stands trial.
A delegated approval is not automatic approval.
A disabled button without a reason is a dead end.
A green run without evidence is theatre.
A hidden failed attempt is laundering.
```

---

## 27. One-Line Product Direction

```text
Build the UX around evidence, blocked states, and next safe actions — not around pretending
the agents are smarter than the gates.
```
