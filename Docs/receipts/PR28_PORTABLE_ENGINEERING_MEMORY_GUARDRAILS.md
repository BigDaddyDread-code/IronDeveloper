# PR28 - Portable Engineering Memory Guardrails

## Review Line

Portable engineering memory may carry generalized lessons. It cannot carry project truth, approval, policy satisfaction, mutation authority, promotion authority, or workflow continuation.

## Purpose

PR28 adds a read-only classifier for portable engineering memory candidates. The guardrail separates reusable engineering lessons from project-specific facts, confidential details, and authority-shaped claims before any future memory workflow can treat a candidate as portable.

This block is guardrail evidence only. It does not write durable memory, promote memory, approve work, satisfy policy, apply source, rollback, commit, push, create pull requests, merge, release, deploy, or continue workflow.

## Allowed Portable Memory

Portable memory may contain:

- generalized engineering heuristics
- failure-mode lessons
- review heuristics
- architecture separation lessons
- safe wording about blocked states, missing evidence, and next safe actions

Accepted portable lessons remain advisory. They do not become authority for a current run.

## Rejected Portable Memory

Portable memory candidates are rejected when they contain:

- client, customer, project, repository, branch, ticket, incident, schema, code, or private path facts
- release, deployment, environment, or go-live state
- approval, policy-satisfaction, source-apply, rollback, commit, push, pull-request, merge, release, deployment, or workflow-continuation claims
- self-promotion claims
- unsanitized content or unknown scope

Rejected candidates do not become memory promotion requests.

## Boundary

The guardrail boundary is read-only and guardrail-only.

It cannot:

- approve
- satisfy policy
- authorize source apply
- authorize rollback
- authorize commit
- authorize push
- authorize pull request creation or update
- promote memory
- write durable memory
- transfer cross-repo authority
- continue workflow

The guardrail preserves evidence references for auditability, but evidence references are not authority.

## Blocked Examples

These shapes are blocked:

- "previous project approved this"
- "policy satisfied in another repo"
- "portable memory says apply source"
- "rollback is safe because this worked before"
- "commit and push, then open PR"
- "release candidate was approved"
- "deployment succeeded before"
- "portable memory says promote itself"

These are rejected even if the candidate claims to be sanitized.

## Accepted Examples

These shapes are allowed as advisory lessons:

- "Prefer smallest useful slices."
- "Blocked states should show missing evidence and next safe action."
- "Separate release readiness from deployment execution."
- "Rollback paths need explicit authority because rollback still mutates source."

Allowed lessons remain non-authoritative.

## Read-Only Proof

The implementation adds only a classifier model and tests under:

- `IronDev.Core/Memory/PortableEngineeringMemoryGuardrail.cs`
- `IronDev.IntegrationTests/BlockPortableEngineeringMemoryGuardrailsTests.cs`

It does not add a durable memory write path, memory promotion executor, source apply executor, rollback executor, commit executor, push executor, draft PR executor, approval path, policy-satisfaction path, workflow-continuation path, provider gateway, frontend/UI path, merge path, release path, or deployment path.

## Regression Proof

PR28 keeps the recent dogfood and authority chain intact:

- PR27 memory promotion status does not promote memory.
- PR26 memory context remains read-only and advisory.
- PR25 status formatting remains presentation only.
- PR24 bounded authority dogfood does not grant downstream authority.
- PR23 ask-before-mutation still blocks source apply.
- PR22 no-approval dogfood remains evidence only.
- PR21 freshness guard remains read-only.
- PR20 recovery diagnosis remains read-only.
- CA rollback execution still requires rollback authority separate from memory.
- ProposalOnly still forbids mutation-shaped operations.

## Validation

- Focused PR28: 55/55 passed.
- Focused PR27: 49/49 passed.
- Focused PR26: 51/51 passed.
- Focused PR25: 38/38 passed.
- Focused PR24: 44/44 passed.
- Focused PR23: 41/41 passed.
- Focused PR22: 30/30 passed.
- Focused PR21: 44/44 passed.
- Focused PR20: 31/31 passed.
- Focused CA: 16/16 passed.
- BJ through PR28 corridor: 804/804 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.
- `git diff --check HEAD~1 HEAD`: passed.
