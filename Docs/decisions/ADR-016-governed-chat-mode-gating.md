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

1. `projectQuestion` (default) and `projectStateReview` remain the only accepted chat completion request kinds.
2. Explicit governance modes `exploration`, `formalization`, and `confirmation` are removed from the API boundary.
3. Any future explicit governance intent may be classifier evidence only; it must never become a controller bypass.
4. `IChatModeClassifier` is the only authority that selects `Exploration`, `Formalization`, or `Confirmation`.
5. `IChatClarificationClassifier` is the only authority that selects clarification kind/questions; it must not mutate the governance mode.
6. `Formalization` responses surface governance affordances through `ChatGovernanceGate`, while `Exploration` and `Confirmation` do not.
7. `Confirmation` requires user confirmation text before escalating to governance actions.
8. Chat responses include reason-chain fields (`reasoningTrace`, `reasoningSummary`, optional `disambiguationQuestion`) so the user can inspect how the assistant decided mode and next step.
9. Frontend rendering of governance actions and rich-copy affordances is mode-aware:
   - `Save Discussion` and `View Sources` are only shown in Formalization mode.
   - `Copy Markdown` is hidden for Exploration and Confirmation, shown for Formalization mode paths.
   - Components render from the single `ChatGovernanceGate` projection instead of duplicating mode checks.
   - Raw reasoning is shown in message/thread surfaces regardless of mode.
10. Anti-inference guard:
   - `CreateTicket`/`CreateTicketsFromDiscussion` classifications do not auto-escalate unless explicit lane-lock language is present.
   - Missing mode or legacy history tags must not be treated as an affirmative governance signal.
   - Context clarification does not force `Confirmation`; product-scope vagueness remains `Exploration` with passive clarification evidence.
11. The response composer receives the selected mode and must not reclassify the turn while generating content.
12. Mode and clarification classifier boundaries are prompt-constrained JSON with strict validation. They are not provider-enforced structured output.
13. Saved assistant envelopes are normalized into `ChatTurnGovernance`, `ChatTurnClarifications`, and `ChatTurnTraces`; `ChatMessage.Tags` remains a replay bridge.
14. Chat-turn audit tables are created by migration/setup scripts, not runtime services.
15. Assistant message insert, session timestamp update, and normalized turn persistence share one transaction.
16. Clarification fallback preserves evidence conservatively and must not mutate mode or gate.

## Reasoning

The goal is not to stop governance; it is to delay governance controls until intent is explicit. The cockpit should reveal reasoning by default and only show formalization controls once the lane is clear. This prevents fake process and aligns UI behavior with requested “watch the system think” use cases.

## Consequences

- `ChatController` owns HTTP shape only and must not infer mode from router output.
- `IProjectChatResponseService` owns route/classify/compose/gate orchestration.
- `IronDev.TauriShell` consumes the expanded chat payload and hides save/view/copy actions unless the gate allows them.
- Tests must assert mode-shape differences rather than a single hard-coded template.
- Persisted assistant responses now write a versioned metadata envelope into `ChatMessage.Tags` so replayed sessions reconstruct the same `mode`, `clarification`, and `gate` instead of defaulting to opaque templates.
- `ChatHistoryService.SaveMessageAsync` persists assistant envelopes into normalized turn tables for audit; legacy string tags intentionally create no turn-governance rows.
- `Database/migrate_chat_turn_audit.sql` and `Docs/migrations/008_ChatTurnAudit.sql` own chat-turn audit schema; runtime chat services fail loudly when the schema is missing.
- Debt ticket `CHAT-GOV-STRUCTURED-OUTPUT-001`: replace prompt-constrained classifier JSON with provider-enforced JSON/schema output once `ILLMService` exposes that capability. Removal condition: classifier parse recovery no longer needs brace extraction.
- `Docs/ARCHITECTURE.md`, `Docs/AGENTS.md`, `Docs/ALPHA_COCKPIT_BACKEND_CONTRACT.md`, and boundary docs must reference the cockpit mode contract.
- `ApiBoundaryTests` must validate that chat ownership remains context-only and that stage ownership remains explicit.
