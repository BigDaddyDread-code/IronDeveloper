# PR262-266 AI Patch Assistance Receipt

## Summary

This block adds AI patch assistance for the patch proposal workbench.

It creates bounded task context, deterministic model request/response artifacts, patch suggestions, workspace edit plans, bounded refinement evidence, and AI patch review evidence.

The model may propose changes. It may not apply authority.

## Boundary

This block does not apply source.
It does not execute rollback.
It does not promote memory.
It does not mutate accepted memory.
It does not dispatch agents.
It does not allow model-to-tool direct execution.
It does not continue workflow.
It does not approve release.
It does not approve deployment.
It does not approve merge.
It does not add API, SQL, UI, scheduler, worker, or autonomous runtime behavior.

Model output is proposal evidence only.
Model output is not approval.
Model output is not policy satisfaction.
Model output is not release readiness.
Model output is not merge authority.
Model output is not source apply authority.

Any command execution still goes through the Block AC ToolRequest, WorkspaceToolGate, and ToolExecutionResult path.

## What exists

- `PatchTaskContextBundle`
- `ModelRequestEnvelope`
- `ModelResponseEnvelope`
- `PatchSuggestion`
- `PatchEditPlan`
- `WorkspacePatchEditor`
- `TestFailureAnalysis`
- `RefinementIterationRecord`
- `AiPatchReview`
- `IPatchModelProvider`
- `DeterministicPatchModelProvider`
- `DisabledPatchModelProvider`
- `irondev patch assist`
- `irondev patch refine`
- `irondev patch review`
- `irondev patch ai`

## Artifacts

AI-assisted patch runs may write:

- `task-context.md`
- `task-context.json`
- `model-requests.jsonl`
- `model-responses.jsonl`
- `patch-suggestions.jsonl`
- `model-edit-plan.json`
- `model-response.md`
- `ai-assist-summary.md`
- `test-failure-analysis.md`
- `refinement-iterations.jsonl`
- `ai-review.md`
- `ai-review.json`

Existing patch workbench artifacts remain review-package evidence only.

## Governance events

Block AD adds these non-authority action kinds:

- `PatchContextBundleCreated`
- `ModelPatchSuggestionRequested`
- `ModelPatchSuggestionReceived`
- `WorkspacePatchEditApplied`
- `ModelTestFailureAnalysisRequested`
- `ModelTestFailureAnalysisReceived`
- `ModelPatchReviewRequested`
- `ModelPatchReviewReceived`
- `PatchRefinementIterationCompleted`

Model request/response events may record `ModelCalled = true` for the deterministic provider. That means the model-assistance seam was invoked. It does not mean authority was created.

All authority flags remain false:

- source repo mutated
- source applied
- git commit created
- git push performed
- pull request created
- approval granted
- policy satisfied
- release approved
- workflow continued
- memory promoted
- accepted memory mutated
- agent dispatched

## Hidden reasoning

Hidden chain-of-thought, private scratchpad, raw prompts, raw completions, raw tool output, and authority claims are not valid persisted model material.

Artifacts store safe summaries, requests, responses, edit plans, and review findings only.

## Known limitations

Block AD is not autonomous coding.
It does not prove the model is correct.
It does not prove the patch is safe.
It does not approve the patch.
It does not apply source.
It does not merge, deploy, release, or promote memory.
It does not provide a full sandbox.
It does not guarantee model output is secure.
It does not store hidden chain-of-thought.
It uses bounded context and may miss relevant files.
The deterministic provider proves pipeline behavior, not model intelligence.

## Review line

PR262-266 adds AI patch assistance inside the governed patch workbench. It proposes and reviews workspace changes but does not grant authority.

## Killjoy line

Block AD is finished when the model can help make a patch, not when the model can pretend to be the developer.