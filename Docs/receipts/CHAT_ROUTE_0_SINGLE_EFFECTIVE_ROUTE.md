# CHAT-ROUTE-0 Single Effective Route Receipt

## What Was Wrong

Chat routing had split ownership. `ProjectChatContextPipeline` produced a route, `PromptContextBuilder` could still inject decision-output instructions unconditionally, and `ContextAgentService` could compute its own route while response metadata and audit were assembled later from classifier output.

That made route provenance blurry: a prompt could follow one route while saved governance metadata described another.

## Effective Route Owner

`ProjectChatContextPipeline` now creates the turn's `EffectiveChatRoute`.

The route records:

- effective governance mode
- context request kind
- source
- confidence and reason
- original and effective work text
- route capabilities for decision tags, decision capture, and ticket drafting
- inputs used to derive the route

`ProjectChatResponseService` still calls the mode classifier once for the existing governance spine contract, but the effective route is the source of truth for response mode, gate, metadata, prompt instruction behavior, persistence, and audit. If the pipeline route is absent, the service uses the classifier only as a fallback route source.

## Context Agent Boundary

`ContextAgentService` accepts `EffectiveChatRoute` on `ContextAgentRequest`.

When supplied, it:

- uses the supplied route decision
- emits route traces that identify the effective route source
- does not call the internal route judge
- gates final prompt decision-tag instructions from `EffectiveChatRoute.AllowsDecisionTagOutput`

`ChatRouteChallenge` exists as an explicit visible result path. No challenge is silently applied as a replacement route.

## Prompt Gating

`PromptContextBuilder` no longer emits hidden `<decision>` instructions by default.

Decision-tag instructions are included only when:

```text
EffectiveChatRoute.AllowsDecisionTagOutput == true
```

Inspection, explanation, verification, and general chat routes receive explicit no-decision-tag instruction in the final context-agent prompt.

## Persistence And Audit

Saved assistant envelopes now carry:

- `routeSource`
- optional `routeChallenge`

`ChatTurnGovernance` now persists:

- `RouteSource`
- `RouteChallengeJson`

The chat audit endpoint returns these fields with the already persisted mode, confidence, reason, clarification state, gate, route trace id, dogfood trace id, and fallback-evidence flag.

The Tauri shell displays route source and route challenge as audit facts. It does not create backend route authority.

## Tests

Verified with:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ChatTurnPersistenceServiceTests|FullyQualifiedName~ChatControllerAuditTests|FullyQualifiedName~GovernedChatSemanticMemoryReleaseSmokeTests|FullyQualifiedName~ProjectChatResponseServiceTests|FullyQualifiedName~ContextAgentEvidenceTests|FullyQualifiedName~Slice1GovernanceBoundaryTests|FullyQualifiedName~ConversationContextResolutionTests"
npm run build
```

Proof coverage:

- supplied effective route prevents silent context-agent reroute
- decision-tag instructions appear only for allowed decision-capture routes
- inspection/explanation routes do not receive decision-tag instructions
- response metadata includes effective route source, capabilities, inputs, and route-challenge visibility
- saved chat-turn governance persists route source and route challenge
- audit endpoint exposes route source, route challenge, fallback evidence, mode, confidence, reason, clarification, gate, and trace ids
- chat replay and durable audit hydration surface route source and challenge without client-side authority

## Boundary

This receipt does not add action authority, approval shortcuts, chat-driven apply, new authority modes, or client-owned route truth.
