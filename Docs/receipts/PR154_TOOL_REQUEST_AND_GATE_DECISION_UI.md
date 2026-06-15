# PR154 Tool Request and Gate Decision UI Receipt

PR154 adds the Tool Request and Gate Decision UI.

Tool Request and Gate Decision UI is read-only.

Tool request visibility is not tool execution.

Gate decision visibility is not gate authority.

Gate allowed status is not tool invocation.

Gate denied status is not repair.

Approval requirement is not approval.

Policy evidence is not policy satisfaction.

Refresh is not retry.

Navigation is not workflow continuation.

Copy request id is not approval.

Copy decision id is not policy satisfaction.

The UI consumes existing GET-only tool request and tool gate APIs.

This PR does not add backend API endpoints, CLI commands, SQL migrations, stores, runners, executors, hosted services, background workers, schedulers, cleanup jobs, repair paths, restart paths, approval paths, policy satisfaction paths, workflow transition paths, workflow continuation paths, source apply paths, patch apply paths, model calls, tool invocation, agent dispatch, memory promotion, retrieval activation, gate override, gate reopen, or raw/private payload exposure.

## Boundary

The UI is a read-only inspection surface for safe tool request and gate decision evidence. It is not a tool console, gate remote control, approval surface, policy satisfier, workflow transition surface, source apply surface, memory promotion surface, or cleanup surface.

## Review line

PR154 shows the gate ledger. It does not open the gate.