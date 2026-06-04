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
3. `projectQuestion` remains the default, but route/context output is only a hint.
4. `IChatModeClassifier` is the only authority that selects `Exploration`, `Formalization`, or `Confirmation`.
5. `Formalization` responses surface governance affordances (`showGovernanceActions`, `governanceActions`), while `Exploration` and `Confirmation` do not.
6. `Confirmation` requires user confirmation text before escalating to governance actions.
7. Chat responses include reason-chain fields (`reasoningTrace`, `reasoningSummary`, optional `disambiguationQuestion`) so the user can inspect how the assistant decided mode and next step.
8. Frontend rendering of governance actions and rich-copy affordances is mode-aware:
   - `Save Discussion` and `View Sources` are only shown in Formalization mode.
   - `Copy Markdown` is hidden for Exploration and Confirmation, shown for Formalization mode paths.
   - Components render from the single `chatGovernanceGate.ts` rule instead of duplicating mode checks.
   - Raw reasoning is shown in message/thread surfaces regardless of mode.
9. Anti-inference guard:
   - `CreateTicket`/`CreateTicketsFromDiscussion` classifications do not auto-escalate unless explicit lane-lock language is present.
   - Missing mode or legacy history tags must not be treated as an affirmative governance signal.
10. The response composer receives the selected mode and must not reclassify the turn while generating content.

## Reasoning

The goal is not to stop governance; it is to delay governance controls until intent is explicit. The cockpit should reveal reasoning by default and only show formalization controls once the lane is clear. This prevents fake process and aligns UI behavior with requested “watch the system think” use cases.

## Consequences

- `ChatController` owns HTTP shape only and must not infer mode from router output.
- `IProjectChatResponseService` owns route/classify/compose/gate orchestration.
- `IronDev.TauriShell` consumes the expanded chat payload and hides save/view/copy actions unless the gate allows them.
- Tests must assert mode-shape differences rather than a single hard-coded template.
- Persisted assistant responses now write a versioned metadata envelope into `ChatMessage.Tags` so replayed sessions reconstruct the same `mode` and governance affordances instead of defaulting to opaque templates.
- `Docs/ARCHITECTURE.md`, `Docs/AGENTS.md`, `Docs/ALPHA_COCKPIT_BACKEND_CONTRACT.md`, and boundary docs must reference the cockpit mode contract.
- `ApiBoundaryTests` must validate that chat ownership remains context-only and that stage ownership remains explicit.
