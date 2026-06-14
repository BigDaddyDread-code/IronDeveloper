# PR110 - Stale Memory Detection Receipt

## Verdict

PR110 detects possible stale staged memory proposals for review.

## Boundary

No stale candidate rejects proposals.
No stale candidate deletes proposals.
No stale candidate corrects proposals.
No stale candidate accepts memory.
No stale candidate promotes memory.
No stale candidate activates retrieval.

## What exists

- Bounded stale candidate model.
- Stale candidate validator.
- Deterministic stale detector over supplied staged memory proposals.
- Evidence references for stale review.
- Review notes for stale review.
- Age, deprecated-term, supersession, contradiction, and missing-current-evidence candidate classification.

## What does not exist

- No stale decision.
- No proposal rejection.
- No proposal deletion.
- No proposal correction.
- No accepted memory creation.
- No memory promotion.
- No retrieval activation.
- No embedding creation.
- No vector store write.
- No Weaviate write.
- No model call.
- No API endpoint.
- No CLI command.
- No runtime dispatch.
- No source apply.
- No policy satisfaction.
- No release approval.

## Review rule

A stale candidate is a review docket item only.

Staleness score is not approval.
Staleness score is not truth.
Human or governed review remains required before any correction, rejection, deletion, acceptance, promotion, or retrieval activation decision.
