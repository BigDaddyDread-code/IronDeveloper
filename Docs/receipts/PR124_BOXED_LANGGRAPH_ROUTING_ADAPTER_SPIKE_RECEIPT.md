# PR124 - Boxed LangGraph Routing Adapter Spike Receipt

PR124 adds a boxed LangGraph-style routing adapter spike.
It maps supplied workflow runner and dry-run snapshots into advisory route labels only.

The adapter does not own workflow decisions.
It does not execute, dispatch, transition, approve, satisfy policy, mutate source, promote memory, activate retrieval, or call models.

The adapter is optional and deletable.

If the adapter starts owning decisions, delete it.

Runner skeleton remains evaluation-only.
Dry-run executor remains dry-run only.

## Boundary

- Route labels are advisory only.
- Route labels are not approval evidence.
- Route labels are not policy evidence.
- Route labels are not workflow transition evidence.
- Route labels are not dry-run evidence.
- Route labels are not A2A validation.
- No workflow state transition.
- No step completion.
- No approval creation or mutation.
- No policy satisfaction.
- No authority grant.
- No agent dispatch.
- No A2A send.
- No tool invocation.
- No model call.
- No prompt construction.
- No source mutation.
- No patch application.
- No memory promotion.
- No retrieval activation.
- No vector search.
- No embedding write.
- No SQL read or write.
- No API, CLI, UI, hosted service, background worker, scheduler, or orchestrator.
- No hidden/private reasoning, raw prompts, raw completions, raw tool output, or whole-patch payloads.

## Labels

- BlockedInvalidStep
- BlockedMissingEvidence
- BlockedPolicyPreflight
- BlockedA2aValidation
- BlockedApprovalRequired
- EligibleForDryRun
- DryRunReviewMaterialAvailable
- NoRouteSuggested
- InvalidRoutingSnapshot

## Review line

PR124 puts LangGraph in a box. It may label the route; it may not choose the road.
