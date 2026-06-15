# PR162 - L4 Invariant Regression Suite

## Purpose

PR162 adds the L4 invariant regression suite.

This PR is tests/receipt only.

It proves the L4 doctrine remains locked after the L4 capability matrix was introduced.

The suite protects the distinction between definitions, requirements, evidence, approvals, policy satisfaction, dry-runs, patch artifacts, source apply, workflow continuation, and release readiness.

## Core invariants

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

UI cannot own L4 authority.

Backend authority must be backend-owned.

Dogfood pass is not release readiness.

Health check is not release readiness.

Validation summary is not release readiness.

UI review is not release readiness.

## What PR162 proves

The current L4 capability matrix is definition-only.

Every current L4 matrix row remains unimplemented.

Allowed effects remain limited to definition-only effects.

Required evidence records do not become evidence.

Required approval does not become accepted approval.

Policy requirements do not become policy satisfaction.

Dry-run requirements do not execute dry-runs.

Patch requirements do not create patch artifacts.

Source apply requirements do not apply source.

Rollback requirements do not execute rollback.

Workflow continuation requirements do not continue workflow.

Release gate requirements do not approve release readiness.

## Explicit non-goals

PR162 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, or release readiness.

PR162 does not grant authority.

PR162 does not create approvals.

PR162 does not satisfy policy.

PR162 does not run dry-runs.

PR162 does not create patch artifacts.

PR162 does not apply source.

PR162 does not execute rollback.

PR162 does not continue workflow.

PR162 does not approve release.

PR162 does not add Core production behavior, API endpoints, SQL, CLI, UI, hosted services, runtime workers, model execution, tool execution, agent dispatch, memory promotion, or retrieval activation.

## Review line

PR162 nails down the L4 invariants. It does not activate L4.
