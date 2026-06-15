# PR145 - Governance Trace Explorer API

PR145 adds the Governance Trace Explorer API.

Governance Trace Explorer API is read-only.

Traceability is not authority.

Trace output is not approval.

Trace output is not policy satisfaction.

Trace output is not workflow transition.

Trace output is not tool invocation.

Trace output is not agent dispatch.

Trace output is not model execution.

Trace output is not memory promotion.

Trace output is not source apply.

The explorer returns safe summaries and references only.

## Scope

PR145 opens a read-only API surface for governance trace search and detail inspection. It exposes safe summaries, correlation timelines, causation references, project references, workflow references, event kinds, source components, subject references, timestamps, authority posture, and related references.

The API uses existing governance event read projections. It does not introduce a new SQL table, write stored procedure, CLI command, UI surface, scheduler, orchestrator, worker, runtime dispatcher, model call, tool invocation, or source file access.

## Boundary

Governance trace summaries are non-authoritative review and operations evidence. A trace can show that something was recorded. It cannot replay governance, continue workflow, satisfy policy, satisfy approval, approve, reject, invoke tools, dispatch agents, call models, promote memory, activate retrieval, apply source, or apply patches.

This PR does not create governance events, mutate governance events, approve, reject, satisfy policy, transition workflow, invoke tools, dispatch agents, call models, promote memory, activate retrieval, apply source, apply patches, execute commands, expose raw payloads, expose raw prompts/completions/tool outputs, expose source content, expose patch payloads, or expose hidden/private reasoning.

## Block O posture

Block O starts with observability only. Operational traceability helps people understand recorded governance events and correlations, but it does not add a control surface.

## Review line

PR145 opens the trace window. It does not move the machinery.
