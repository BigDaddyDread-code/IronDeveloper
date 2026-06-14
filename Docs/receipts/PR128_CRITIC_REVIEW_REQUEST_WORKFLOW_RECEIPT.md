# PR128 - Critic Review Request Workflow Receipt

PR128 adds a Critic Review Request candidate workflow. It turns supplied review material into a safe request package for later human/governed critic review.

This is a Block M L4 candidate workflow and remains non-mutating.

## Boundary

A review request is not a review decision.

Candidate workflow output cannot grant authority.

The workflow prepares the envelope. It does not deliver it, open it, or decide what is inside.

## What this does

- Records supplied review target references.
- Records supplied review questions.
- Records supplied evidence references.
- Records supplied risk hints.
- Records missing review material.
- Records Block L gate snapshot blocks when supplied.
- Produces safe package summary lines.

## What this does not do

It does not dispatch CriticAgent, call models, build prompts, post comments, approve, reject, satisfy policy, mutate source, create tickets, promote memory, activate retrieval, or transition workflow state.

It does not read repository files, read logs from disk, call GitHub, call CI, run tools, invoke agents, create SQL state, expose API/CLI/UI, or add runtime hosting.

It does not store hidden/private reasoning, raw prompts, raw completions, raw tool output, raw logs, or whole-patch payloads.

## L4 boundary statement

Review request is not review.

Review package is not decision.

Evidence is not approval.

Traceability is not authority.

Dry-run is not execution.

Route label is not decision ownership.

Candidate workflow output cannot grant authority.

## Review line

PR128 writes the critic request envelope. It does not summon the critic.
