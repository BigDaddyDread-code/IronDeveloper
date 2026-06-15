# PR164 - L4 Failure Mode Report

## Purpose

PR164 adds the L4 failure mode report.

This PR is tests/receipt only.

Failure mode report is not failure remediation.

Failure mode detection is not failure correction.

Risk naming is not authority.

Dogfood campaign is evidence.

Dogfood campaign is not release readiness.

Memory proposal is not accepted memory.

UI is glass, not controls.

Trace output is not approval.

Backend authority must be backend-owned.

## Failure mode report

The report names the ways governed L4 execution can fail, lie, blur authority, or become theatre.

It does not fix those risks.

It does not implement L4.

It does not grant authority.

It does not apply source.

It does not continue workflow.

It does not approve release.

## Failure modes

### AUTHORITY_COLLAPSE

- Severity: Critical
- Related capability: L4_ACCEPTED_APPROVAL_RECORD
- Description: A requirement, trace, UI state, matrix row, or evidence record is treated as backend authority.
- Trigger: A caller treats definition, visibility, or evidence shape as permission.
- False-positive risk: Legitimate review evidence may be noisy but still non-authoritative.
- Required guard: backend-owned authority records must be validated separately from definitions, reports, traces, and UI state.
- Detection signal: output claims approval, policy satisfaction, workflow continuation, source apply, or release approval without a matching backend authority record.
- Blocked effect: approve; satisfy policy; continue workflow; apply source; release software.
- Boundary maxim: Definition is not authority. Matrix row is not permission. Backend authority must be backend-owned.

### EVIDENCE_COLLAPSE

- Severity: High
- Related capability: L4_POLICY_SATISFACTION_RECORD
- Description: Evidence requirement or evidence reference is treated as actual proof.
- Trigger: A required evidence reference exists and is mistaken for evidence verification.
- False-positive risk: Evidence references can be valid pointers while still not proving the underlying claim.
- Required guard: evidence references must resolve to governed evidence before they can support a later authority decision.
- Detection signal: requirement-only material is used as proof of dry-run acceptance, policy satisfaction, source apply readiness, or release readiness.
- Blocked effect: policy satisfaction; dry-run acceptance; source apply; release readiness.
- Boundary maxim: Evidence requirement is not evidence. Evidence reference is not proof.

### APPROVAL_THEATRE

- Severity: Critical
- Related capability: L4_ACCEPTED_APPROVAL_RECORD
- Description: Approval-looking material is treated as accepted approval.
- Trigger: approval package, UI review, requested approval, or approval-shaped text is used as if accepted by the backend.
- False-positive risk: human review material can look decisive before an accepted approval record exists.
- Required guard: accepted approval must be a backend-owned accepted approval record, not review prose or package readiness.
- Detection signal: accepted approval is inferred from required approval, approval package, UI review, or dogfood result.
- Blocked effect: accepted approval; policy satisfaction; source apply; workflow continuation; release readiness.
- Boundary maxim: Required approval is not accepted approval. Approval package is not accepted approval. UI review is not decision.

### POLICY_SATISFACTION_THEATRE

- Severity: Critical
- Related capability: L4_POLICY_SATISFACTION_RECORD
- Description: Policy requirement, rule text, profile, or gate preview is treated as policy satisfaction.
- Trigger: a policy rule or gate preview is visible and a caller treats that as a satisfied policy decision.
- False-positive risk: policy checks can be readable and still not be backend satisfaction records.
- Required guard: policy satisfaction must be recorded as its own backend-owned satisfaction record.
- Detection signal: dry-run, source apply, workflow continuation, or release readiness proceeds from policy text or gate preview alone.
- Blocked effect: controlled dry-run; source apply; workflow continuation; release readiness.
- Boundary maxim: Required policy is not policy satisfaction. Gate preview is not gate satisfaction.

### DRY_RUN_THEATRE

- Severity: High
- Related capability: L4_CONTROLLED_DRY_RUN
- Description: Dry-run plan, dry-run requirement, preview, or simulated text is treated as real dry-run execution.
- Trigger: a dry-run requirement or preview receipt exists without actual controlled dry-run execution proof.
- False-positive risk: preview text can be useful review material while still not proving execution.
- Required guard: controlled dry-run requires a backend-owned dry-run execution receipt.
- Detection signal: patch artifact creation or source apply readiness is claimed from preview-only material.
- Blocked effect: patch artifact creation; source apply; workflow continuation.
- Boundary maxim: Required dry-run is not dry-run execution. Apply preview is not dry-run execution.

### PATCH_ARTIFACT_THEATRE

- Severity: High
- Related capability: L4_PATCH_ARTIFACT
- Description: Patch proposal evidence is treated as an immutable patch artifact.
- Trigger: proposal package, patch summary, or candidate repair text is treated as a governed patch artifact.
- False-positive risk: proposal evidence can be high quality without being an artifact suitable for apply.
- Required guard: patch artifact creation must be separate from proposal evidence.
- Detection signal: source apply, commit, push, or workflow continuation is attempted from proposal evidence.
- Blocked effect: source apply; commit; push; workflow continuation.
- Boundary maxim: Required patch artifact is not a patch artifact. Patch proposal evidence package is not a patch.

### SOURCE_APPLY_ESCAPE

- Severity: Critical
- Related capability: L4_CONTROLLED_SOURCE_APPLY
- Description: A path writes source without accepted approval, policy satisfaction, dry-run proof, patch artifact, and rollback support.
- Trigger: file write, patch apply, branch mutation, commit, or push is reachable without the full backend chain.
- False-positive risk: read-only previews can mention source apply without mutating source.
- Required guard: source apply must be backend-controlled and gated by every prior authority and evidence requirement.
- Detection signal: source mutation or git mutation appears without accepted approval, policy satisfaction, dry-run proof, patch artifact, rollback, and source apply approval requirement.
- Blocked effect: file write; patch apply; commit changes; push branch; branch mutation.
- Boundary maxim: Required source apply is not source apply. Source apply must be backend controlled.

### ROLLBACK_THEATRE

- Severity: High
- Related capability: L4_ROLLBACK_RECORD
- Description: Rollback plan is treated as executable rollback proof.
- Trigger: rollback text or requirement is accepted as proof that rollback can or did execute.
- False-positive risk: rollback planning is necessary but does not prove runtime rollback capability.
- Required guard: rollback execution or rollback record must be distinct from rollback requirement and plan text.
- Detection signal: source apply completion, workflow continuation, or release readiness relies on rollback plan only.
- Blocked effect: source apply completion; workflow continuation; release readiness.
- Boundary maxim: Required rollback is not rollback. Rollback plan is not rollback execution.

### WORKFLOW_CONTINUATION_ESCAPE

- Severity: Critical
- Related capability: L4_WORKFLOW_CONTINUATION
- Description: Workflow continues because a previous step looks good, not because backend transition authority exists.
- Trigger: review package, UI navigation, trace, or dogfood pass is treated as continuation permission.
- False-positive risk: read-only navigation can look like progress while no workflow transition occurred.
- Required guard: workflow continuation must require backend-owned transition authority.
- Detection signal: workflow transition, retry, repair, or continuation occurs without a workflow transition decision.
- Blocked effect: continue workflow; transition workflow; retry workflow; repair workflow.
- Boundary maxim: Required workflow continuation is not workflow continuation. UI navigation is not workflow continuation.

### RELEASE_READINESS_THEATRE

- Severity: Critical
- Related capability: L4_RELEASE_READINESS_GATE
- Description: Dogfood pass, health check, validation summary, UI review, or correlation report is treated as release readiness.
- Trigger: a positive report or passed dogfood campaign is used as release readiness.
- False-positive risk: positive evidence can be valuable while still not being a release gate.
- Required guard: release readiness must be a backend-owned release readiness gate decision.
- Detection signal: release-ready state, tag, deploy, or ship action appears from dogfood, health, validation, UI, or report-only evidence.
- Blocked effect: approve release; mark release ready; tag release; deploy; ship software.
- Boundary maxim: Required release gate is not release readiness. Dogfood pass is not release readiness. Health check is not release readiness. Validation summary is not release readiness. UI review is not release readiness.

### DOGFOOD_CONFUSION

- Severity: High
- Related capability: L4_RELEASE_READINESS_GATE
- Description: Dogfood campaign result is treated as approval, release readiness, policy satisfaction, or workflow continuation.
- Trigger: dogfood pass or campaign success is promoted into authority.
- False-positive risk: dogfood evidence should influence review but not satisfy authority.
- Required guard: dogfood receipts must remain evidence-only until a separate backend authority decision consumes them.
- Detection signal: accepted approval, policy satisfaction, release readiness, or workflow continuation is inferred from dogfood result.
- Blocked effect: accepted approval; policy satisfaction; release readiness; workflow continuation.
- Boundary maxim: Dogfood campaign is evidence. Dogfood pass is not release approval. Campaign success is not workflow continuation.

### MEMORY_PROMOTION_ESCAPE

- Severity: Critical
- Related capability: L4_RELEASE_READINESS_GATE
- Description: Memory proposal, candidate learning, or campaign observation is treated as accepted memory or portable engineering memory.
- Trigger: memory proposal or dogfood observation is promoted without governed memory promotion.
- False-positive risk: repeated observations can be strong candidates while still not accepted memory.
- Required guard: memory promotion must require governed review and accepted memory creation.
- Detection signal: retrieval activation, accepted memory, or cross-project learning authority appears from proposal or campaign observation.
- Blocked effect: promote memory; activate retrieval; cross-project learning authority.
- Boundary maxim: Memory proposal is not accepted memory. Campaign observation is not memory promotion. Candidate learning is not portable engineering memory.

### UI_AUTHORITY_ESCAPE

- Severity: Critical
- Related capability: L4_WORKFLOW_CONTINUATION
- Description: UI route, status chip, copy action, refresh, or button becomes backend authority.
- Trigger: frontend state or user interface affordance is treated as approval, policy, workflow, tool, source, or release authority.
- False-positive risk: UI should expose evidence clearly, which can visually resemble a control surface.
- Required guard: UI must call only explicit backend APIs and must not own backend authority.
- Detection signal: UI state claims approval, policy satisfaction, workflow transition, tool invocation, source apply, or release readiness.
- Blocked effect: approve; satisfy policy; transition workflow; invoke tool; apply source; release software.
- Boundary maxim: UI is glass, not controls. UI cannot own L4 authority. UI route is not capability. UI view model is not authority.

### TRACE_OBSERVABILITY_CONFUSION

- Severity: High
- Related capability: L4_WORKFLOW_CONTINUATION
- Description: Trace visibility is treated as replay, approval, or control.
- Trigger: timeline output or trace detail is mistaken for authority to replay, approve, transition, or release.
- False-positive risk: trace exploration needs rich context without becoming a control plane.
- Required guard: observability surfaces must remain read-only and non-authoritative.
- Detection signal: trace output causes replay, transition, approval, or release-readiness effects.
- Blocked effect: governance replay; workflow transition; approval; release readiness.
- Boundary maxim: Trace output is not approval. Timeline is not authority. Observability is not mutation permission.

### RAW_PRIVATE_PAYLOAD_LEAK

- Severity: Critical
- Related capability: L4_PATCH_ARTIFACT
- Description: Failure reports expose raw payloads, prompts, completions, private reasoning, secrets, source contents, or patch payloads.
- Trigger: diagnostic report serializes unsafe payload material.
- False-positive risk: safe summaries may mention payload categories without retaining payload values.
- Required guard: reports must retain safe summaries and references only.
- Detection signal: raw prompt, raw completion, raw tool output, hidden/private reasoning, secret, source content, or patch payload appears in report material.
- Blocked effect: payload leak; secret leak; private reasoning leak; source leak.
- Boundary maxim: No raw/private payload exposure. No hidden/private reasoning exposure.

### CROSS_PROJECT_CONTAMINATION

- Severity: Critical
- Related capability: L4_ACCEPTED_APPROVAL_RECORD
- Description: Evidence, memory, approval, or authority leaks across project boundaries.
- Trigger: project-scoped truth is reused in another project as authority or confidential evidence.
- False-positive risk: sanitized engineering learning can be portable only after governed review.
- Required guard: project-scoped evidence and authority must remain isolated unless explicitly sanitized and re-approved.
- Detection signal: cross-project authority transfer, confidential detail exposure, or memory contamination appears.
- Blocked effect: cross-project authority transfer; confidential detail exposure; memory contamination.
- Boundary maxim: Project-specific truth remains isolated. Portable engineering memory must be sanitized. Cross-project learning suggestion is not cross-project authority.

## Explicit non-goals

PR164 does not fix these failure modes.

PR164 does not implement L4.

PR164 does not grant authority.

PR164 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, release readiness, memory promotion, retrieval activation, or release approval.

PR164 does not add fixes, runtime mitigations, accepted approval storage, policy satisfaction storage, dry-run execution, patch artifact creation, source apply, rollback execution, workflow continuation, release readiness gate, release approval, memory promotion, retrieval activation, UI controls, API endpoints, SQL, CLI, hosted services, schedulers, model execution, tool execution, or agent execution.

## Review line

PR164 names the ways L4 can fail. It does not fix them.
