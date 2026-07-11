# V2 Release Candidate Receipt

## Purpose

This receipt records the V2 release-candidate close. It is not a production release, deployment approval, merge authority, or claim that non-author qualification has passed.

## Scope Closed

The current V2 scope includes:

- flow-first sign-in, tenant selection, project chooser, and guided setup;
- backend-owned Board and Work Item projections;
- first-class Chat with direct IronDev sessions and shared channels;
- document upload, processing, immutable versions, and exact context attachment;
- governed run, review, approval, continuation, apply, recovery, and execution-proof surfaces;
- project membership, Work Item ownership, stale-write refusal, and two-user authority routing;
- read-only unified audit ledger;
- deterministic OpenAPI generation and CI drift detection;
- current product truth documentation.

## Final V2 PR Chain

- PR #782: V2-FINAL-1 project membership and Work Item ownership.
- PR #783: V2-FINAL-2 versioned collaboration writes.
- PR #784: V2-FINAL-3 apply retry and interrupted recovery.
- PR #785: V2-FINAL-4 two-user approval and authority journey.
- PR #786: V2-FINAL-5 unified audit ledger.

## Remaining Gate

V2-FINAL-6 remains a human gate:

A person who did not build IronDev must start the Tauri desktop app through the supported LocalTest launcher, sign in, select or connect a project, complete setup, shape work, run real model-backed governed work, review, disposition, approve through a second user or explicit solo exception, continue, apply, recover from a failure/interruption, restart, and confirm all state reloads from backend truth.

The qualification checklist lives in `Docs/product/V2_NON_AUTHOR_QUALIFICATION.md`.

## Release-Candidate Status

| Gate | Status |
| --- | --- |
| Feature scope | Frozen |
| Capability matrix | Updated |
| Acceptance status | Recorded |
| Known limitations | Recorded |
| V2.5 deferrals | Recorded |
| Non-author Tauri qualification | Pending |
| Production release readiness | Not implemented |

## Boundary

This receipt does not authorize commit, push, pull-request creation, merge, release, deploy, production hosting, source mutation, workflow continuation, accepted approval, policy satisfaction, or credential setup.

Green CI is evidence. This receipt is evidence. Human qualification is evidence. None of them, alone, are production release authority.
