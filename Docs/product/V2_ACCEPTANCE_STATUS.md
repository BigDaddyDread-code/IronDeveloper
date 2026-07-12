# IronDev V2 Acceptance Status

**Status:** Release candidate, non-author desktop qualification pending
**Reviewed against:** `main` at `b5c3afa7`
**Date:** 11 July 2026

This file closes the repository-side V2 feature scope without pretending the final human qualification has happened. Code, contracts, CI, and product truth are in release-candidate shape; the remaining gate is a non-author person using the Tauri desktop app through the normal visible journey.

## Acceptance Status

| Area | Status | Evidence |
| --- | --- | --- |
| Entry, tenant, project chooser, and setup | Passed | Sign-in, conditional tenant choice, project selection, repository connection, and provisioning readiness are live product routes. |
| Board | Passed | Board consumes backend readiness, waiting, run, and collaboration projection truth. |
| Chat | Passed | Direct IronDev sessions, shared channels, explicit `@IronDev`, mentions, unread state, and notification preferences are durable. |
| Documents | Passed | Upload, processing, detail, immutable versions, and exact-version Chat context are live. |
| Tools catalogue | Passed with exclusions | Tool catalogue/detail are read-only and governed. General connection setup and invocation remain outside V2. |
| Work Item spine | Passed | Shape, Ticket, Build, Review, and Done render backend lifecycle, gate, action, recovery, execution proof, and evidence truth. |
| Project membership and Work Item ownership | Passed | Project members, project roles, Work Item assignee, followers, waiting-on actor, recent activity, and Board assignment projection are backend-owned. |
| Shared-write integrity | Passed | Work Item collaboration and channel membership refuse stale writes with reload-and-compare recovery. |
| Apply recovery | Passed with explicit boundary | Interrupted/failed apply states are classified with safe resume/retry/abandon/manual-review actions. Rollback execution remains outside V2. |
| Two-user authority routing | Passed | Reviewer/approver eligibility, self-approval refusal, solo exception display, and acting-human attribution are backend-governed. |
| Unified audit ledger | Passed | Library Audit exposes read-only actor/action/outcome/correlation/evidence rows, filters, safe event detail, and a bounded backend-produced JSON export with integrity metadata. It grants no authority. |
| Non-author Tauri qualification | Pending human gate | Must be performed by a person who did not build the slice, in the Tauri desktop app, using only supported product instructions. |
| Production shared-host service | Excluded | V2 is a local/technical pilot, not a production hosted service. |
| Commit, push, pull request, merge, release, deploy from product | Excluded | Source apply receipts do not imply source-control or release authority. |

## Known Limitations

- Production identity, hosting, backup, monitoring, SLO, and security-operations contracts are not implemented.
- General external tool connection setup and mutating tool invocation are not implemented.
- Bounded Audit JSON export is supported. Audit analytics and reporting are not implemented.
- Realtime presence and live typing are not implemented.
- Secure provider credential setup is not implemented.
- Settings remain functionally split but not redesigned.
- Rollback execution is not implemented; recovery classifies and guides.
- Project assignment and collaboration are REST-reconciled, not realtime.
- The Work Item identity still rides on the existing ticket substrate.

## Deferred to V2.5

- Rename Chat to Workshop.
- Durable Work Item aggregate replacing ticket-as-identity.
- Analyst/Workshop agent.
- Built-in skill and personality defaults.
- Secure provider credential setup.
- Tenant/project agent-profile inheritance.
- Reset and restore configuration.
- Agent run snapshots.
- Full Settings redesign.
- General tool connection setup.
- Production shared-host packaging.

## Freeze Line

V2 feature scope is frozen at this release-candidate close. New capability work moves to V2.5 unless it fixes a defect found during non-author qualification.
