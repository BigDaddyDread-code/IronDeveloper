# PR161 - L4 Capability Matrix

## Purpose

PR161 defines the L4 capability matrix.

This is a backend planning, contract, and receipt slice.

It defines the ordered backend authority chain required for governed L4 execution.

It does not implement L4 execution.

## Boundary

Capability matrix is not capability execution.

Capability definition is not authority.

Matrix row is not permission.

Evidence requirement is not evidence.

Required approval is not accepted approval.

Required policy is not policy satisfaction.

Required dry-run is not dry-run execution.

Required patch artifact is not a patch artifact.

Required source apply is not source apply.

Required rollback is not rollback.

Required workflow continuation is not workflow continuation.

Required release gate is not release readiness.

Backend authority must be backend-owned.

UI cannot own L4 authority.

L4 is governed execution, not autonomous theatre.

## L4 backend authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

This chain is ordered.

Later capability definitions must not be treated as available until earlier authority records and evidence records exist and are validated by backend-owned mechanisms.

## Required capability rows

- L4_ACCEPTED_APPROVAL_RECORD
- L4_POLICY_SATISFACTION_RECORD
- L4_CONTROLLED_DRY_RUN
- L4_PATCH_ARTIFACT
- L4_CONTROLLED_SOURCE_APPLY
- L4_ROLLBACK_RECORD
- L4_WORKFLOW_CONTINUATION
- L4_RELEASE_READINESS_GATE

All PR161 rows are definition-only.

All PR161 rows are marked unimplemented.

All PR161 rows allow only definition.

All PR161 rows forbid their dangerous runtime side effects.

## Release readiness boundary

Dogfood pass is not release readiness.

Health check is not release readiness.

Validation summary is not release readiness.

UI review is not release readiness.

Release readiness must be a backend gate decision, not a UI status chip, dogfood pass, health check, or validation summary.

## Explicit non-goals

PR161 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, or release readiness.

PR161 does not grant authority.

PR161 does not create accepted approvals.

PR161 does not satisfy policy.

PR161 does not run dry-runs.

PR161 does not create patches.

PR161 does not apply source.

PR161 does not continue workflow.

PR161 does not approve release.

PR161 does not add UI, SQL, CLI, runtime workers, mutation services, model execution, tool execution, agent dispatch, memory promotion, or retrieval activation.

## Review line

PR161 names the L4 ladder. It does not climb it.
