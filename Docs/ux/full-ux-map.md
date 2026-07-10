# IronDev Full UX Map

> **Status: Superseded as a current product contract.** Retained as historical synthesis. Use [IronDev Product and UX Specification v2](../product/IRONDEV_PRODUCT_UX_SPEC_V2.md) and [Current Product Capabilities](../product/CURRENT_PRODUCT_CAPABILITIES.md) for current navigation and implementation truth.

**Status:** Draft — full-experience design pass, revised for multi-user project teams.
Uncommitted working draft for review.

**Team stance:** the experience is designed **multi-user-first**. A solo operator is a team of
one, not a separate mode. Separation of duties is the real model; the Two-Chair Rule
(Section 2.2) is its degenerate case when one human holds multiple roles under explicit policy.
**Lineage:** Synthesizes two documents that currently conflict on information architecture:

```text
future-ux-product-spec (draft)  — authority boundaries, screen contracts, roles, provisioning, audit
flow-first-ux-spec              — pipeline IA: Board / Work Item / Library, gates as locks
```

**Resolution rule used throughout:** where they conflict on *navigation shape*, flow-first wins
(the user lives on the pipeline, not in noun tabs). Where they conflict on *what a screen must
show or refuse*, the future spec's authority contracts win. Nothing in either document's
authority model is weakened here.

**Core principle (unchanged):** UX exposes backend truth. UX never invents authority.

Every surface in this map must answer the four questions at all times:

```text
What happened?
Why did it stop?
What evidence exists?
What am I safely allowed to do next?
```

---

## 1. The Experience Thesis

IronDev's product is a **pipeline with tension**: intent is shaped, frozen into a contract,
built under governance, adversarially reviewed, gated by a human, applied under receipt, and
reconstructed as evidence. The UX is that pipeline made visible — with the gates rendered as
locks you satisfy, never as pages you visit or buttons you hope are enabled.

```text
The UI should feel like a flight deck, not a dashboard of tabs.
Calm when the system is calm. Specific when the system is blocked.
Never optimistic. Never theatrical. Never vague.
```

Three emotional targets, in priority order:

```text
Trust      — every claim on screen is traceable to a backend response and an evidence file.
Orientation — the user always knows where the work item is on the spine and what holds it.
Momentum   — the next safe action is always one visible, honest step away.
```

### 1.1 The Team Benefit: Shared Visible Truth

For a project team, the product's first benefit is not automation — it is that **everyone sees
the same truth without asking**:

```text
What tickets exist and where each one is on the spine        (Board, team scope)
What has been done, by whom, with what evidence               (Reports archive, Audit)
What was decided architecturally, and why                     (ADRs + decision records, Library)
What is blocked, on whom, and what unblocks it                (gates + waiting-on routing)
```

This is why the Viewer role exists and why the Library is a first-class surface, not an
afterthought: a stakeholder who never starts a run still gets the full, honest picture. The
same property is what makes external platforms (Section 9.5) cheap to integrate — they read
and mirror the one truth; they never hold a second copy of it.

---

## 2. People, Roles, and Separation of Duties

The role model (Owner, Admin, Developer, Reviewer, Approver, Auditor, Runner, Viewer) is real
product structure, not future decoration. A project team is several humans holding different
subsets of these roles; a solo operator is one human holding several under explicit policy.
The UX is identical in shape for both — only the routing of attention and the friction of
self-approval differ.

Hard prerequisite carried from the flow-first spec, restated as a product gate:

```text
No second human enters a tenant until A12 tenant-scope proof and the
F01–F15 role/visibility matrix exist in the backend.
The UI renders team surfaces before then only as honest NotImplemented states.
```

### 2.1 Identity and the Role Lens

The shell shows who you are and what you hold, at all times, in the global chrome:

```text
bob · roles: Developer, Approver          Viewing as: Developer   [change lens]
```

Rules:

```text
Roles are backend-assigned facts. The lens is a per-user attention filter over them.
Changing lens changes what the UI foregrounds, never what the backend permits.
The backend checks actor + role + policy on every request regardless of the lens shown.
A user never sees a lens for a role they do not hold.
```

Lenses reorder attention; they do not grant authority:

```text
Developer lens — Board foregrounds my items in Shape/Ticket/Build; my blocked runs first.
Reviewer lens  — Board foregrounds unreviewed critic packages routed to me.
Approver lens  — Board foregrounds approval requirements I am eligible to satisfy.
Auditor lens   — read-only chrome; all mutation affordances render as visible-but-refused.
```

### 2.2 Separation of Duties and the Two-Chair Rule

The default policy is strict:

```text
The human who authored a run cannot approve it.
The backend refuses with SelfApprovalProhibited; the UI renders it as a first-class
refusal: "You authored this run. Approval requires another eligible Approver
(2 eligible: alice, marek)."
```

A project policy may explicitly permit self-approval (the solo case). When it does, the UX
compensates with ceremony — the **Two-Chair Rule**:

```text
1. Approval affordances stay hidden under the Developer lens even when policy allows them.
2. Recording an approval requires an explicit lens switch to Approver.
3. The switch replays what the Approver is about to judge:
   package hash, proposal file, repair history, finding count, dispositions —
   and states plainly: "You authored this run. Policy permits self-approval on this project."
4. The approval form then requires: reason, scope, expiry, and target-hash confirmation.
5. The audit row records the actor, the lens, and the self-approval fact.
```

Killjoy lines:

```text
Separation of duties is the design. Solo is a policy exception, visibly labeled.
Solo does not mean casual. The chair switch is the review.
```

### 2.3 Assignment and Ownership

Work items carry team routing facts, all backend-owned:

```text
author        the human who shaped/confirmed the ticket
assignee      the human currently responsible for moving it (default: author)
reviewers     humans or roles the critic package is routed to
approvers     eligible actors computed by the backend from role + policy, never by the client
```

The UI shows actor chips on cards, timelines, findings, dispositions, and audit rows. Every
recorded decision names its human. Handoffs are explicit actions ("assign to…"), audited.

---

## 3. First-Run Experience: Cockpit Preflight

A fresh machine gets named remedies, never mysteries. First launch (and any launch where a
dependency check fails) lands on **Preflight**, not on an empty Board.

### 3.1 Preflight Checklist

Each check renders: status, evidence, and a named remedy.

```text
[✓] API reachable            GET /api/health          200 in 41ms
[✓] SQL reachable            doctor: sql-check         ok
[✗] Model provider proof     doctor: model-proof       FAILED
    Remedy: set IRONDEV_MODEL_API_KEY and re-run doctor. See Docs/alpha-smoke.
    Last successful proof: never on this machine.
[✓] Workspace root writable  C:\IronDev\workspace      ok
[✓] Evidence root writable   C:\IronDev\evidence       ok
[–] Project provisioned      none yet
    Next safe action: provision your first project.
```

Rules:

```text
Preflight is read-only reporting over doctor/backend checks. It fixes nothing itself.
Every failure names its remedy in imperative text with a doc link.
A degraded preflight does not block browsing; it blocks starting runs, with reasons.
Preflight is re-reachable at any time from the Truth Strip health chip.
```

### 3.2 First-Session Path

```text
Preflight green (or knowingly degraded)
-> Provision first project (Library > Projects, wizard — Section 8.1)
-> Readiness result shown with any blockers
-> Board renders with an honest empty state:
   "No work items. Next safe action: shape one in a new work item, or import an issue."
```

---

## 4. Global Chrome

Present on every screen. Three elements.

### 4.1 The Truth Strip

A persistent one-line strip (bottom of the shell). Every screen cites its sources:

```text
API ✓  SQL ✓  Model: live(claude-*)  |  Project: BookSeller  |  bob · lens: Developer
Source: GET /api/projects/{id}/tickets/{tid}/skeleton-runs/{rid}/report  ·  refreshed 12s ago  ·  corr 8f3a…
```

Rules:

```text
Every data panel names the endpoint it rendered from (hover/expand for full URL + correlation id).
"Refreshed Xs ago" is always visible. Stale data is labeled, never silently shown.
Clicking the health chips opens Preflight.
```

### 4.2 The Attention Queue (pull, not push — routed per user)

Attention is a **queue each user pulls from**, split into two backend-computed buckets:

```text
Waiting on YOU: 2       (1 approval you are eligible for, 1 finding on your run)
Waiting on others: 3    (2 reviews with alice, 1 approval — any of 2 eligible approvers)
Blocked runs: 1         (RepairBudgetExhausted · WI-4 · assignee: you)
```

Rules:

```text
"Waiting on you" is a backend query over (actor, roles, assignments, eligibility) — never
client logic guessing who should act.
"Waiting on others" always names who or which role the item waits on. A team must be able
to see where work is stuck and on whom, without asking around.
A badge count is a backend query result, never a client-side accumulation.
A toast is not truth. If the UI wants to announce a change, it refetches and renders state.
Clicking a badge filters the Board to those items.
```

Out-of-app notification (email/webhook digest of your "waiting on you" queue) is a v0.3+
delivery channel over the same backend query — a notification is a pointer to truth, never a
copy of it. Until built: honest 501 in notification settings.

### 4.3 The Command Bar

Keyboard-first navigation (existing `CommandBar` component): jump to work item, run, package
hash, correlation id, or audit search. Commands that would mutate state are **not** in the
command bar — mutation lives only on screens where its full context is visible.

---

## 5. The Universal Screen Contract

Every screen in the product implements the same anatomy and the same six states. This is what
makes 501-first shipping possible: unbuilt screens are just screens in state 6.

### 5.1 Screen Anatomy

```text
┌──────────────────────────────────────────────────────────┐
│ HEADER      identity + state chip + owning object links  │
├──────────────────────────────────────────────────────────┤
│ BODY        panes per screen (see Sections 6–8)          │
├──────────────────────────────────────────────────────────┤
│ AFFORDANCE RAIL   backend-supplied actions, each with:   │
│                   allowed | refused(reason) | not impl.  │
├──────────────────────────────────────────────────────────┤
│ EVIDENCE FOOTER   files, hashes, receipts, corr ids      │
└──────────────────────────────────────────────────────────┘
```

### 5.2 The Six Universal States

```text
1. Loading         skeleton, endpoint named, no fake data
2. Ready           evidence-backed content
3. Empty           honest absence: "No runs exist for this ticket." + provenance + next safe action
4. Blocked         backend refusal: reason code, human sentence, next safe action
5. DegradedTruth   backend unreachable / evidence file missing / data stale past threshold
6. NotImplemented  real 501 envelope from a real controller; roadmap slice named
```

State 5 rules:

```text
API unreachable  -> last-known snapshot rendered, banner: "Showing snapshot from 14:02:11.
                    Backend unreachable. All actions disabled: BackendUnreachable."
Evidence missing -> the artifact row renders EvidenceMissing with the expected path.
                    The report names the gap. Nothing patches over it.
Stale            -> past the per-screen staleness threshold, data dims and the banner appears.
```

State 6 rules:

```text
The screen exists, is navigable, and renders the backend's 501 envelope:
  "Not implemented. Source: GET /api/projects/{id}/readiness → 501.
   Planned: roadmap slice PROJECT-3. No UI workaround exists."
No mock mode. Ever. Not even for demos.
```

### 5.3 The Affordance Envelope

The UI never derives what the user may do. Every action on every screen comes from a
backend-supplied structure:

```text
{ action, allowed, reason, detail, nextSafeAction, correlationId }
```

Reason codes are one vocabulary — governed refusals and unbuilt features flow through the same
pipe and render through the same component:

```text
CriticReviewMissing · UndispositionedFindings · MissingOrUnsatisfiedApproval ·
ApprovalExpired · PackageHashMismatch · StaleAfterUpstreamApply ·
RepairBudgetExhausted · MutationLeaseHeld · BackendUnreachable · NotImplemented …
```

Killjoy line:

```text
A disabled button is a claim. Only the backend gets to make claims.
```

---

## 6. Surface One: The Board

The home screen. The portfolio of work items as cards in pipeline columns.

### 6.1 Layout

```text
┌ SHAPE ──────┬ TICKET ─────┬ BUILD ──────┬ REVIEW ─────┬ DONE ───────┐
│ WI-7 draft  │ WI-5 ready  │ WI-4 ▶ run  │ WI-3 ⛔ gate │ WI-1 ✓ appl │
│  shaping…   │  start ok   │  attempt 2  │  2 findings │ WI-2 ✓ rprt │
│             │ WI-6 blocked│  (repair)   │  1 approval │             │
│             │  no criteria│             │   pending   │             │
└─────────────┴─────────────┴─────────────┴─────────────┴─────────────┘
Waiting on human: 3          Blocked: 1           Active runs: 1
```

### 6.2 Card Contract

Each card shows only backend truth:

```text
work item id · title · stage · state chip · blocking reason (if any) · last event time
assignee chip · "waiting on" chip when gate-held (names the human or role it waits on)
repair indicator when any attempt > 1 exists (a repaired run must never look clean, even here)
lease chip when the item's run holds the project mutation lease
```

### 6.3 Board Rules

```text
Columns are stages, not statuses the client computes — a card's column comes from run/ticket state.
Default scope: "My items" under Developer lens, "Waiting on me" under Reviewer/Approver lens;
one click to "Team" scope. Scope is a filter over the same backend query, never a different truth.
The Board is the attention queue rendered spatially; lenses reorder column emphasis (Section 2.1).
Dependency edges ("blocked by WI-42") render when the backend models them; until then, absent — not faked.
Empty Board is state 3 with a next safe action, not a marketing illustration.
```

---

## 7. Surface Two: The Work Item Spine

Where ~90% of time is spent. One constant layout; the left pane changes per stage; the right
pane is always the contract.

```text
┌──────────────────────────────────────────────────────────────────────┐
│  SHAPE ─🔒─ TICKET ─🔒─ BUILD ─🔒─ REVIEW ─🔒─ DONE      (stage rail) │
├────────────────────────────────────────────┬─────────────────────────┤
│  LEFT PANE (stage-specific)                │  CONTRACT RAIL (always) │
│                                            │  acceptance criteria    │
│                                            │  architecture chips     │
│                                            │  affected files         │
│                                            │  open questions         │
│                                            │  per-stage enrichment   │
├────────────────────────────────────────────┴─────────────────────────┤
│  AFFORDANCE RAIL  ·  EVIDENCE FOOTER  ·  TRUTH STRIP                 │
└──────────────────────────────────────────────────────────────────────┘
```

### 7.1 Gate Anatomy (the locks between stages)

Gates are **designed screens**, not disabled arrows. Every lock, expanded, shows:

```text
Gate: Ticket -> Build
State: LOCKED
Unmet conditions (backend truth):
  [✗] Acceptance criteria present         (0 found)
  [✓] Linked project readiness            ReadyToRun
  [✗] Ticket confirmed                    still draft
Next safe action: add acceptance criteria, then confirm the ticket.
Source: GET .../tickets/{id}/build-readiness · refreshed 8s ago
```

The four gates and their condition sources:

```text
Shape  -> Ticket   ticket draft completeness (title, problem, criteria, linked files)
Ticket -> Build    readiness result (project readiness + ticket readiness + no lease conflict)
Build  -> Review   evidence packaged (build/test outcome + critic package ready + hash)
Review -> Done     approval requirement satisfied (review recorded, findings dispositioned,
                   accepted approval matching package hash, not expired, not stale)
```

Killjoy line:

```text
A gate the user cannot explain is a gate the UI has failed to render.
```

### 7.2 Stage: Shape

Left pane: the shaping discussion — chat embedded as a tool, not a destination. The existing
chat governance model applies unchanged (Exploration may not return governance actions;
Formalization may; ChatGovernanceGate remains the single source for chat UI permissions).

```text
Shows:    conversation, context sources used, promote-to-criteria affordances,
          draft ticket panel accumulating in the contract rail
Actions:  promote statement to acceptance criterion · link file · raise open question ·
          generate draft ticket (Formalization only)
Refuses:  any governance affordance in Exploration mode — rendered as refused-with-reason,
          not hidden, so the mode boundary is legible
```

A draft work item exists from the first shaping message (resolves the flow-first spec's open
question) so the Board always shows shaping work.

### 7.3 Stage: Ticket

Left pane: the frozen contract.

```text
Shows:    title, summary, problem, proposed change, acceptance criteria,
          linked files, provenance (created-from chat session / imported issue / manual),
          readiness panel: project readiness state + ticket blockers + start-run eligibility
Actions:  edit draft · confirm ticket · evaluate readiness · start governed run (gate-checked)
Refuses:  start from unconfirmed ticket · start when readiness blocked ·
          start when mutation lease held (chip links to the holding run)
```

### 7.4 Stage: Build — the Run Cockpit

Left pane: the run, live (poll-shaped until the API streams).

**Header:**

```text
Run 0193-a2… · BookSeller · WI-4 · Status: Running · Node: DisposableCommand
Model: live · Repair budget: 1 of 2 used · Lease: held by this run · Last event: 6s ago
```

**Timeline** (ordered backend events, raw names shown — honesty over polish):

```text
RunStarted → ProposalGenerated → TestsAuthored → DisposableWorkspaceCreated
→ DisposableCommandStarted → DisposableCommandFailed
→ SkeletonRepairAttemptStarted → SkeletonRepairProposalGenerated
→ DisposableCommandStarted → DisposableCommandCompleted
→ SkeletonEvidencePackaged → CriticReviewPackageReady → ApprovalRequiredHalt
```

**Repair panel** — a repaired run must not look clean:

```text
This run required repair.
┌ Attempt 1 · initial ────────────────────────────────┐
│ FAILED: BuildFailed · dotnet build BookSeller.slnx  │
│ excerpt: error CS0117 …                             │
│ proposal.json (preserved) · workspace · evidence    │
└─────────────────────────────────────────────────────┘
┌ Attempt 2 · repair ─────────────────────────────────┐
│ PASSED · proposal-repair-2.json                     │
└─────────────────────────────────────────────────────┘
Proposal under review: proposal-repair-2.json
Approval package binds to: proposal-repair-2.json
Repair does not approve. Repair does not reduce review. History is preserved.
```

```text
Actions:  none that trigger repair — repair happens only inside the server-orchestrated
          bounded run. Future (policy-gated): "start new run with increased budget".
Terminal states here:
  Failed / RepairBudgetExhausted -> state 4 with attempts inspectable and next safe actions:
  inspect attempts · refine ticket · start new run
```

### 7.5 Stage: Review — Critic, Findings, and the Human Gate

Left pane, three stacked sections:

**Critic package** (evidence-first):

```text
package id · path · hash · hash-verified ✓ · proposal binding · build/test evidence ·
criterion coverage (covered / uncovered named) · repair attempt count
```

**Findings ledger** — every finding demands a disposition; nothing is silently absorbed:

```text
┌ F-1 · High · Missing null guard in OrderService ────────────────────┐
│ problem · why it matters · required fix · blocks merge: yes         │
│ Disposition: [AcceptRisk] [FixInFollowUp] [Reject]   [Revise…]      │
│ — reason required for all three; decided-by + timestamp recorded —  │
└─────────────────────────────────────────────────────────────────────┘
Ground truth checks: 4 run · 0 mismatched
```

`[Revise…]` is a verb, not a fourth disposition (shipped as REVISE-1, #736): it
cites this finding (and any other undispositioned ones) into
`POST .../skeleton-runs/{runId}/revise` with the human's written instruction. A
green revision replaces the gate package (superseded package preserved) and
records `AddressedByRevision` for the cited findings; a failed revision returns
to the unchanged gate with the budget spent. Off unless
`SkeletonRevision:MaxAttempts` is configured — the button renders its named
refusal (`RevisionDisabled`, `RevisionBudgetExhausted`,
`UndispositionedFindingsNotCited`, …) exactly like every other governed action.
The revised package needs its OWN critic review: the review requirement is
hash-scoped to the current package.

**Human gate** (foot of the pane) — visible to every role, actionable only by an eligible
Approver (Section 2.2):

```text
Approval requirement
  target: skeleton-run 0193-a2… · capability: continue-after-review
  package hash: 41c9… ✓ verified · expires: 2026-07-09 14:00
  run author: bob
  eligible approvers: alice, marek     (backend-computed from role + policy)
  you: not eligible — SelfApprovalProhibited (you authored this run)
  [✓] critic review recorded
  [✗] 1 finding undispositioned  ← blocks approval, with link
  Delegated policy: none matched (no delegated approval configured — the default)
Record approval  → requires: reason · scope · expiry · typed hash-prefix confirmation
```

Eligibility is always shown, even when it excludes the viewer — an ineligible user sees who
*can* act, so a stuck gate is a routing fact, not a mystery.

```text
Refusal codes rendered inline when the gate refuses:
CriticReviewMissing (hash-scoped: means no review of the CURRENT package —
a superseded pre-revision review satisfies nothing) · UndispositionedFindings ·
ApprovalExpired · PackageHashMismatch · StaleAfterUpstreamApply ·
DelegatedApprovalScopeMismatch
Language rules: never "Auto approved". Always "Delegated approval policy matched (policy name,
reason code, remaining uses)". A finding is not a veto. A disposition is not approval.
```

### 7.6 Stage: Done — Apply and the Final Report

Left pane, two sections:

**Apply** (only reachable when the backend says eligible):

```text
Preflight:  run status · package hash · approval status · review status ·
            dispositions · mutation lease · expected receipt chain
Stages:     promotion-package → promotion-approval → apply-preflight
            → apply-dry-run → apply-copy
            each: succeeded/blocked · summary · errors · receipt path
Boundary:   "Applied means copy-only source mutation completed.
             Not commit. Not push. Not PR. Not release."
```

**Final report** — reconstruction, not authority:

```text
Sections: summary · timeline · initial proposal · final proposal · repair attempts ·
          build/test evidence · critic package · reviews · dispositions ·
          approval requirement · accepted approval consumed · apply chain · receipts ·
          GAPS (named, never patched) · boundary statement
Status:   Complete | IncompleteWithNamedGaps | FailedWithEvidence | Applied
Export:   file copy of the reconstruction, watermarked "reconstruction, not authority".
```

---

## 8. Surface Three: The Library

Reference and administration — and for a team, **the shared memory**. This is where "what has
been done" and "what was decided" live for people who weren't in the room: the reports archive
answers the first, the ADR/decision browser answers the second, and every entry links back to
the work item and evidence that produced it. Non-workflow surfaces, reached from global nav,
linked into from work-item receipts.

```text
Library
├── Projects        provisioning, readiness, settings
├── Solution        code-index explorer (staleness banner), documents, decisions, ADRs
├── Governance      the 17 existing viewer panels, re-homed as drill-down targets
├── Reports         archive of final reports across runs
├── Audit           the ledger
├── Admin           users, roles, approval profiles (mostly dormant in v0.2)
└── Settings        four-way split (Section 8.4)
```

### 8.1 Projects and the Provisioning Wizard

The bridge from BookSeller demo to real use. The wizard is a front-end over a backend-owned
profile — every answer becomes `ProjectArchitectureProfile` + decision record + readiness result.

Journey:

```text
Create project -> repo path -> root safety check -> repo scan
-> detection (architecture, build/test commands, folders)
-> pointed questions (only what detection cannot prove)
-> human confirmation -> decision record -> readiness result -> first ticket allowed
```

Each detected item carries a confidence label and a human action:

```text
Build command    Detected: dotnet build BookSeller.slnx    High    [Confirm] [Edit]
Test command     Missing                                    —       [Supply]
Forbidden paths  Needs confirmation: Database/, artifacts/          [Confirm] [Edit]
```

Vocabulary rules: `Profile proposed / confirmed · Readiness satisfied · Unknowns remain`.
Never "architecture understood", never "ready because scan completed".

Readiness states render as state-4 blocked screens with named remedies, same pattern as
Preflight:

```text
BlockedMissingTestCommand
  Remedy: supply the test command in project settings, or re-run detection.
```

### 8.2 Reports and Audit

**Reports:** the archive view — final reports across all runs, filterable by project/status,
each opening the same reconstruction renderer as the Done stage.

**Audit:** the ledger. Every row:

```text
timestamp · actor · lens context · action · target · policy · result · reason ·
correlation id · run id · evidence refs (clickable)
```

Filterable by actor, action kind, target, time. Read-only, exportable. The Auditor lens's home.

### 8.3 Admin (the team's front door)

Admin is no longer dormant decoration — it is the surface where a project becomes a team. It
lights up in two steps, gated on backend truth:

**Step 1 (before A12 + F01–F15 exist):**

```text
Users:              the current tenant reality, truthfully — one human, one runner account.
Invite user:        real route, backend 501 -> state 6:
                    "Planned: gated on A12 tenant-scope proof + F01–F15 role matrix."
Roles:              the eight roles listed with exact capability sentences (data model real).
```

**Step 2 (team-ready):**

```text
Invite user:        email invite -> pending state visible -> accepted, audited.
Assign roles:       per-project role grants; every grant/revoke produces an audit row
                    and takes effect on the next backend check (no client cache of authority).
Membership view:    who holds what, on which project, granted by whom, when.
Self-approval:      per-project policy toggle, default OFF, dangerous-setting ceremony
                    (warning + typed reason + audit) to enable.
Approval profiles:  default shown truthfully: "No delegated approval. Every continuation
                    requires explicit human approval." Creation UI: state 6 until AUTH-0.
```

Killjoy line:

```text
A user account is not authority. A role assignment plus a scoped policy is authority.
```

### 8.4 Settings — the four-way split

Never mix harmless preferences with authority policy. Four separately-navigated groups:

```text
User preferences    theme, density, keyboard — no warnings, no audit
Project runtime     repo path, roots, commands, branch, model mode/provider, repair budget
Project safety      allowed/forbidden/generated/secret paths, dirty-repo policy, root safety
Authority           approver/reviewer roles, approval profiles, delegated policies,
                    the human-intervention dial (Section 9.6)
```

Dangerous settings (roots, mutation paths, budgets, provider, profiles, delegated authority)
require: warning, typed reason, and produce an audit row. Model settings show provider, model,
mode, key **source status** (never the key), last live proof, and the standing warnings
("live model pass is proof of life, not reliability").

---

## 9. Cross-Cutting Behaviors

### 9.1 Concurrency and the Mutation Lease

The lease is visible long before apply:

```text
Project header:   "Mutation lease: held by run 0193-a2… (WI-4)" — chip links to the run.
Start-run gate:   refuses with MutationLeaseHeld + link to the holder.
Board card:       lease chip on the holding item.
```

Two work items touching the same files is surfaced at readiness evaluation (overlap warning,
advisory), and enforced at lease acquisition (hard, backend).

### 9.2 The Ceremony Gradient

Friction is proportional to authority consumed. Deliberately inconsistent interaction cost:

```text
Navigate / inspect / filter        zero friction
Save draft, link file              one click
Confirm ticket                     one click + gate re-evaluation shown
Disposition a finding              reason required
Record approval                    eligibility check + lens switch + reason + scope + expiry
                                   + typed hash prefix (+ self-approval label when policy applies)
Change dangerous setting           warning + typed reason + audit row
Apply                              backend-eligibility only; no client shortcut exists
```

### 9.3 Humans Racing Humans

With a team, two people will view and act on the same object. The rule: **last write does not
silently win — the backend rejects stale writes, and the UI renders the race honestly.**

```text
Disposition race    alice dispositions F-1 while marek has the form open. Marek's submit is
                    refused (StateChangedSinceRead); the panel refetches and shows:
                    "F-1 was dispositioned AcceptRisk by alice at 14:02 while you were viewing.
                     Your draft reason is preserved below. Next safe action: review her
                     disposition; contest it by recording a note, not by overwriting."
Approval race       two eligible approvers act on the same requirement — first satisfies it;
                    the second sees "already satisfied by alice (14:03)", not an error.
Ticket edit race    draft edits carry a version; stale saves are refused with a rendered diff
                    of what changed, never merged silently.
Run start race      two people start a run on the same item — the second gets the existing
                    run: "Run already started by alice 20s ago. Joining as viewer."
```

Rules:

```text
Every mutating request carries the version/hash of the state it was rendered from.
A refused stale write always preserves the user's typed input.
No client-side locking, no "someone is editing" soft locks in v0.x — races are resolved by
backend refusal + honest re-render, which is cheaper and never lies.
Presence indicators ("alice is viewing this run") are a v0.3+ nicety, not a correctness tool.
```

### 9.4 Time and Staleness

```text
Every panel: "refreshed Xs ago". Poll cadence per screen; manual refresh always available.
Approval expiries render as countdowns near expiry.
StaleAfterUpstreamApply renders as a first-class blocked state explaining what changed upstream.
```

### 9.5 Integrations: Platforms Are Clients, Never Authority

Future direction (post-TEAM-0): hook the pipeline into Azure DevOps, Slack, and similar
platforms. The rule is inherited from the flow-first spec and hardened here:

```text
A platform integrates at the API contract, as a client with a service identity and roles.
A platform can create input, read truth, and receive pointers to truth.
A platform can never satisfy a gate, record an approval, or hold the authoritative state.
```

**Azure DevOps (work tracking):**

```text
Inbound   ADO work item -> imported ticket (provenance: imported-issue, source id + link kept).
          Import is a draft in Shape; it earns readiness like any other ticket — an ADO
          "Ready" column means nothing to IronDev gates.
Outbound  IronDev mirrors status back to the ADO item as comments/state pointers:
          "Run halted at human gate — evidence: <link>". A mirror is a pointer, not a copy;
          on conflict, the IronDev report is truth and the mirror says so.
Never     ADO approvals/policies satisfying IronDev gates; two-way field sync fighting over
          who owns a ticket's text after confirmation (confirmed contract is frozen here).
```

**Slack (attention delivery):**

```text
Inbound   nothing that mutates. At most, a slash command that answers with links to truth.
Outbound  the per-user "waiting on you" queue delivered as a digest or gate-halt message,
          with deep links into the cockpit. The message names the same facts as the queue:
          what, why halted, who is eligible.
Never     approve/disposition buttons in Slack. The ceremony (Section 9.2) requires the
          full context replay; a chat button is authority theatre by construction.
```

Killjoy lines:

```text
Slack can tell you. Slack cannot decide.
An imported ticket earns its gates like any other. Import is provenance, not readiness.
```

### 9.6 The Autonomy Dial: Configurable Human Intervention

Teams decide how much human intervention they want — from approval at every gate to none in
the happy path. The dial is a per-project authority setting, chosen by humans who hold the
authority to give it away, enforced by the backend, and visible everywhere it acts.

The framing rule that keeps this honest:

```text
The dial changes WHO satisfies a gate, never WHETHER the gate exists.
Every gate is always evaluated, always audited. "No intervention" means a human
pre-authorized a delegated policy to satisfy it under named conditions —
and when any condition fails, the system halts to a human regardless of the level.
```

**The levels** (presets over the same per-gate policy machinery — each expands to exact
§19-style delegated-policy fields the user can inspect and edit):

```text
Level 0 · Hands-on (default)   human confirms ticket, dispositions every finding,
                               approves every continuation, requests every apply.
Level 1 · Review-light         delegated continuation when: build passed · tests passed ·
                               critic NoObjection · zero findings · no repair attempts ·
                               hash verified · paths in scope. Anything else halts to human.
                               Human still applies.
Level 2 · Trusted lane         Level 1 + repair attempts within budget allowed +
                               delegated apply within a narrow path scope (future; AUTH-3).
Level 3 · Autonomous lane      all gates delegated within scope; humans review reports and
                               audit after the fact. Never includes commit/push/release.

Every level above 0 ALSO requires a fresh, VERIFIED critic canary catch-rate
measurement (P2-6 parity: required catch-rate met, control clean, re-execution
available). Stale, missing, or failing measurement halts every delegated gate to
a human, named — the dial reads the eval; autonomy is earned by measurement, and
each delegated satisfaction's audit row names the measurement it relied on.
```

**UX:**

```text
Where      Settings > Authority > Human intervention. A preset picker plus a per-gate matrix
           showing exactly what each level delegates, expanded into inspectable policy fields.
Loosening  dangerous-setting ceremony: warning + typed reason + audit row. Per level, again.
Tightening one click, effective immediately, audited.
Visibility work running under delegation is labeled everywhere: Board card chip
           ("lane: Trusted"), run header, gate panel ("satisfied by delegated policy
           <name> · reason code · remaining uses"), report, audit. Autonomy is never silent.
Halts      a delegated run that hits any unmet condition halts exactly like Level 0 —
           same gate panel, same "waiting on you" routing, plus which condition failed.
```

**Cut-line:** the dial UI ships early with Level 0 real and Levels 1–3 rendered as honest
NotImplemented (gated on AUTH-0/1). The dial is committed product direction; the evaluator
is still built only when dogfooding demands it.

Killjoy lines:

```text
The dial is a contract, not a mood. Every level is inspectable policy, not a vibe slider.
Autonomy that cannot halt is not autonomy — it is abandonment.
```

---

## 10. Journeys (acceptance narratives)

**J1 — Fresh machine to first applied change.** Preflight (one failure, named remedy, fixed) →
provision second repo via wizard (two pointed questions) → shape a small work item in chat →
promote criteria → confirm ticket → gate opens → run passes clean → critic returns NoObjection,
zero findings → lens switch, approval ceremony (self-approval policy labeled) → continuation
→ apply chain, five receipts →
report `Applied`. *Every screen used exists; no state was invented.*

**J2 — Failure, repair, finding, accepted risk.** Run fails build (attempt 1) → bounded repair
passes (attempt 2) → Review shows repair history and binds approval to `proposal-repair-2.json`
→ critic raises one Medium finding → operator dispositions `AcceptRisk` with reason → approval
ceremony shows the disposition inline → applied → report shows the failed attempt, the repair,
the accepted risk, forever.

**J3 — Refusal.** Approver attempts approval after an upstream apply landed →
`StaleAfterUpstreamApply` → screen explains what changed, next safe action: re-run against
current state. No override affordance exists.

**J4 — Audit reconstruction.** Auditor lens → Audit ledger filtered to WI-4 → follows
correlation ids from run start through approval to receipts → opens the final report → every
claim in the report links to an evidence file that exists (or a named gap).

**J5 — The edge of the product.** User opens Admin → Invite user → real route, real 501 →
"Planned: gated on A12 + F01–F15. No UI workaround exists." The user knows exactly where the
product ends.

**J6 — Three humans, one work item.** Bob (Developer) shapes and confirms WI-9, starts the
run; it halts at the gate. Bob's gate panel says: *not eligible — SelfApprovalProhibited;
eligible: alice, marek.* Alice (Reviewer) finds WI-9 in her "waiting on you" queue, records
the critic review, dispositions nothing (one finding is Bob's to answer). Bob directs a
revision citing the finding with a written instruction → the run rebuilds green, the revised
package (new hash) supersedes the old one, and the cited finding reads `AddressedByRevision`.
The revised package needs its own review — alice reviews again, clean. Marek (Approver) sees
it in his queue, replays the package (new hash, the revision trail, alice's fresh review,
Bob's disposition trail), records approval with reason. Bob applies. The audit ledger reads as a conversation between three
named humans and one backend — no step required anyone to ask "who was supposed to do this?"

**J7 — The autonomous lane halts.** A project runs at Level 1 (Review-light). Four tickets
flow through with zero human touches — each gate satisfied by the delegated policy, each
satisfaction labeled and audited. The fifth run comes back with one Medium finding. The lane
halts exactly like Level 0: the gate panel shows which condition failed ("zero findings —
not met"), the item lands in the approver's "waiting on you" queue, and the Board card drops
its lane chip for a blocked chip. Autonomy earned four merges and correctly refused the fifth.

---

## 11. State Vocabulary and Visual Legend

One legend, product-wide. No semantic color reuse.

```text
Evidence / ready        neutral surface, no celebration color
Running                 animated but quiet (pulse, not spinner theatre)
Blocked (governed)      amber · reason code · next safe action
Refused (authority)     red · reason code · never a dead end
NotImplemented          grey, dashed border · roadmap slice named
DegradedTruth           striped banner · snapshot timestamp
Applied / complete      confirmed check, muted — completion is normal, not a fireworks moment
Repair-touched          persistent small badge; never removed by later success
```

Language rules (product-wide ban list): "auto approved", "AI approved", "ready because green",
"safe because repaired", "architecture understood". Replacements per the authority vocabulary.

---

## 12. Cut-Lines: v0.2 (team of one) and v0.3 (team-ready)

The experience is designed multi-user-first, shipped in two cuts. **v0.2 is the full spine
for a team of one** — every team surface exists in the UI, rendering honest 501s where the
backend team prerequisites (A12 tenant-scope proof, F01–F15 role matrix) are not yet built.
**v0.3 is the team-ready cut** — the same screens light up; no navigation changes.

```text
READY in v0.2 (team of one)            501 in v0.2 -> READY in v0.3 (team-ready)
Preflight with named remedies          Invite user + membership view
Board (My items scope)                 Role assignment beyond Owner/Developer
Work item spine: all five stages       Team Board scope with real multi-actor data
All four gates with condition detail   "Waiting on others" routing to other humans
Repair panel with attempt history      SelfApprovalProhibited default (needs 2nd human)
Findings ledger + dispositions         Reviewer/approval routing + eligibility lists
Approval ceremony + Two-Chair Rule     Stale-write refusals exercised by real races
Apply chain + receipts                 Out-of-app notification digest
Final report with named gaps
Provisioning wizard (second repo)      REMAINS 501 PAST v0.3
Audit ledger (read + filter)           Approval profile creation (AUTH-0)
Settings four-way split                Delegated continuation evaluator (AUTH-1)
Actor chips + version-carrying writes  Delegated apply (do not build)
  (cheap now, mandatory later)         Presence indicators · dependency edges ·
                                       report export · streaming run view
```

Two things move *into* v0.2 because retrofitting them is expensive:

```text
Actor chips everywhere    every decision names its human from day one, even when every
                          human is bob — the audit trail and UI never need a schema change.
Version-carrying writes   mutating requests carry read-state version from the start, so
                          team races (9.3) need no client rework, only a second human.
```

Slice mapping stays as planned (FUTURE-0 → UX-0 → AFFORDANCE-1 → NAV-1 → REPAIR-2 →
CRITIC/APPROVAL/APPLY-UX → PROJECT-0..3 → DOGFOOD-2), with additions: **TEAM-0** (A12
tenant-scope proof + F01–F15 matrix + invite/role backend) sits between DOGFOOD-2 and any
second human, and the integration slices sit after the team is real:

```text
INTEG-ADO-0     import ADO work item as draft ticket (provenance kept, gates earned)
INTEG-ADO-1     outbound status mirror as pointers to truth
INTEG-SLACK-0   "waiting on you" digest with deep links (no mutating affordances)
```

AUTH-* stays deferred until dogfooding hurts. This map is the target those slices converge on.

---

## 13. Open Design Decisions

```text
1. Stage rail vs. deep links — governance viewers re-home under Library, but should a receipt
   deep-link open in-place (drawer) or navigate away? Recommend: drawer, spine stays visible.
2. Multi-ticket epics — Board grouping vs. parent work item. Defer until a real epic exists.
3. Poll cadences per screen — needs measurement against API cost; start coarse (5s run view,
   30s Board, manual elsewhere).
4. Lens persistence — does the Approver lens auto-revert after the ceremony? Recommend: yes,
   revert to the user's primary lens on navigation away from the gate.
6. Reviewer routing granularity — route critic packages to a role (any Reviewer) or to a
   named human? Recommend: role by default, named assignment as an explicit action.
5. Whether "skeleton run" remains user-facing vocabulary or becomes "governed run" in UI copy
   while event names stay raw in timelines. Recommend: rename in chrome, raw in timelines.
```

---

## 14. Killjoy Lines (full set, carried + new)

```text
The UI can request. The backend decides. The evidence explains.
The lens filters attention; the backend grants authority.
Separation of duties is the design. Solo is a policy exception, visibly labeled.
Solo does not mean casual. The chair switch is the review.
A stuck gate is a routing fact, not a mystery — it names who can act.
Last write does not silently win.
Slack can tell you. Slack cannot decide.
Import is provenance, not readiness.
The dial changes who satisfies a gate, never whether the gate exists.
Autonomy that cannot halt is not autonomy — it is abandonment.
A disabled button is a claim. Only the backend gets to make claims.
A gate the user cannot explain is a gate the UI has failed to render.
A toast is not truth.
A badge count is a backend query result.
A hidden failed attempt is laundering.
A report is reconstruction, not permission.
Completion is normal, not a fireworks moment.
```
