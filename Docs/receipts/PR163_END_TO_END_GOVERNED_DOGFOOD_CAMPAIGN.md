# PR163 - End-to-end Governed Dogfood Campaign

## Purpose

PR163 adds an end-to-end governed dogfood campaign proof.

It proves IronDev can run a bounded internal dogfood campaign and produce auditable evidence across the existing governance surfaces without granting backend authority.

This PR is integration-test and receipt evidence only.

## Boundary

Dogfood campaign is evidence.

Dogfood campaign is not release readiness.

Dogfood pass is not release approval.

Campaign result is not policy satisfaction.

Campaign receipt is not accepted approval.

Campaign success is not workflow continuation.

Campaign failure is not repair permission.

Campaign observation is not memory promotion.

Campaign trace is not backend authority.

End-to-end proof is not L4 activation.

## Campaign shape

The test-scoped campaign proof follows this safe path:

campaign request -> governed campaign plan -> bounded dogfood execution -> trace events -> dogfood receipt -> correlation report -> workflow evidence reference -> campaign summary receipt

It stops before accepted approval, policy satisfaction, source apply, workflow continuation, and release readiness.

## What the proof covers

The campaign has a project reference.

The campaign has a workflow run reference.

The campaign has a correlation id.

The campaign emits governance trace evidence.

The campaign produces dogfood receipt evidence.

The campaign can be correlated with gate and dogfood reporting.

The campaign can be inspected through existing read-only trace, dogfood, workflow evidence, and correlation report read-model surfaces.

The campaign does not create accepted approval records.

The campaign does not create policy satisfaction records.

The campaign does not execute source apply.

The campaign does not continue workflow.

The campaign does not approve release.

The campaign does not promote memory.

The campaign does not activate retrieval.

The campaign does not expose raw/private payloads.

## Explicit non-goals

PR163 does not implement accepted approval records, policy satisfaction records, source apply, rollback, workflow continuation, release readiness, memory promotion, retrieval activation, or release approval.

PR163 does not add accepted approval storage, accepted approval API, policy satisfaction storage, policy satisfaction API, patch apply, file mutation, git mutation, rollback execution, workflow continuation, release readiness gate implementation, deployment, UI control buttons, CLI release commands, runtime workers, schedulers, or autonomous repair loops.

## Review line

PR163 dogfoods the machine. It does not ship the machine.
