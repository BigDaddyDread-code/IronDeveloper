# ADR-016: Governed Chat Mode Gating In the Cockpit

## Status

Accepted.

## Context

The chat cockpit had drifted into a fixed template that always exposed governance actions and repeated the same completion shape regardless of intent. That violates:

- `Exploration` intent (user asks questions, probes, or asks for options)
- `Formalization` intent (user requests handoff into Discussion/Ticket/buildable work)
- `Confirmation` intent (mixed exploration + formalization language)

Product integrity requires explicit, inspectable transitions between these modes.

## Decision

`POST /api/projects/{projectId}/chat/complete` must operate as a mode-aware contract:

1. `projectQuestion` (default) and `projectStateReview` remain supported for existing clients.
2. New explicit modes `exploration`, `formalization`, and `confirmation` are accepted.
3. `projectQuestion` remains the inference default but now drives prompt classification into
   `Exploration`, `Formalization`, or `Confirmation`.
4. `Formalization` responses surface governance affordances (`showGovernanceActions`, `governanceActions`), while `Exploration` and `Confirmation` do not.
5. `Confirmation` requires user confirmation text before escalating to governance actions.
6. Chat responses include reason-chain fields (`reasoningTrace`, `reasoningSummary`, optional `disambiguationQuestion`) so the user can inspect how the assistant decided mode and next step.
7. Frontend rendering of governance actions is UI-gated to the response mode/flags, and `Copy Markdown` remains always available.

## Reasoning

The goal is not to stop governance; it is to delay governance controls until intent is explicit. The cockpit should reveal reasoning by default and only show formalization controls once the lane is clear. This prevents fake process and aligns UI behavior with requested “watch the system think” use cases.

## Consequences

- `ChatController` and `IProjectChatResponseService` now own mode inference and response shaping.
- `IronDev.TauriShell` consumes the expanded chat payload and hides save/view actions in exploration by default.
- Tests must assert mode-shape differences rather than a single hard-coded template.
- `Docs/ARCHITECTURE.md`, `Docs/AGENTS.md`, `Docs/ALPHA_COCKPIT_BACKEND_CONTRACT.md`, and boundary docs must reference the cockpit mode contract.
- `ApiBoundaryTests` must validate that chat ownership remains context-only and that stage ownership remains explicit.
