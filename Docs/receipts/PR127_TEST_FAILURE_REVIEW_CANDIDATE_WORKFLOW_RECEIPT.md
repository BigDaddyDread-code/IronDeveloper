# PR127 - Test Failure Review Candidate Workflow Receipt

## Summary

PR127 adds a Test Failure Review candidate workflow. It turns supplied test failure evidence into safe review material only.

This is the first Block M L4 candidate workflow and remains non-mutating.

The candidate workflow normalizes supplied test failure evidence into a review package containing a safe failure summary, advisory classification, affected test names, missing evidence notes, confidence label, and safe next-review suggestions.

## Boundary

It does not run tests, inspect the repository, invoke tools, dispatch agents, call models, mutate source, apply patches, create tickets, promote memory, activate retrieval, or transition workflow state.

It does not read logs from disk. It does not call CI. It does not call GitHub. It does not use OCR. It does not store hidden/private reasoning, raw prompts, raw completions, raw tool output, raw logs, or whole-patch payloads.

Classification is advisory. It is not root-cause proof.

Candidate workflow output cannot grant authority.

Evidence is not approval. Dry-run is not execution. Route label is not decision ownership.

## What this can do

- accept supplied test failure evidence snapshots
- validate supplied safe fields
- report missing evidence
- classify supplied failure summaries deterministically
- list affected tests from supplied names
- produce safe review material
- produce safe next-review suggestions
- preserve non-authority flags as false

## What this cannot do

- cannot debug tests autonomously
- cannot run tests
- cannot inspect repository files
- cannot read logs from disk
- cannot invoke tools
- cannot dispatch agents
- cannot send A2A handoffs
- cannot call models
- cannot build prompts
- cannot mutate source
- cannot generate patches
- cannot apply patches
- cannot create tickets
- cannot promote memory
- cannot activate retrieval
- cannot satisfy approval
- cannot satisfy policy
- cannot transition workflow state
- cannot provide root-cause proof

## Block L interaction

The candidate can consume supplied Block L snapshots such as runner evaluation, dry-run result, and boxed advisory route suggestion. Blocking snapshots block review material. Eligible or completed snapshots remain evidence only and do not grant authority.

## Review line

PR127 reviews the crash report. It does not touch the car.
