# BS Operation Eligibility Evaluator

## Review Line

The grant is checked per operation; approval is not inherited by vibes.

## Receipt

This PR adds a pure operation eligibility evaluator only.

It does not add a runner.
It does not execute commands.
It does not issue grants.
It does not store grants.
It does not mutate source.
It does not apply patches.
It does not create approvals.
It does not satisfy policy.
It does not run validation.
It does not create validation evidence.
It does not promote memory.
It does not continue workflow.
It does not add frontend/API/CLI.
It does not add source apply.
It does not create global authority.
It does not create cross-repo authority.
It does not accept memory-supplied authority.

Eligibility under profile and grant is necessary but not sufficient for any future operation.

## Boundary

The evaluator composes the run authority profile ceiling and bounded run grant envelope for one requested operation. Both are required. A profile cannot replace a grant, and a grant cannot widen the profile.

The evaluator checks operation kind, repository, branch, run id, patch hash for patch-bound operations, affected file paths, expiry, mutation budget, required validation evidence, and stop-before boundaries.

Eligibility is not approval. Eligibility is not policy satisfaction. Eligibility is not validation execution. Eligibility is not source apply authority. Eligibility is not patch apply authority. Eligibility is not durable source mutation permission. Eligibility is not provider mutation permission. Eligibility is not workflow continuation. Eligibility is not memory promotion. Eligibility is not release or deployment authority.

Patch hash matching is a binding check only. It does not allow source apply. Required validation evidence is evidence only. It does not create approval, satisfy policy, execute validation, or prove authenticity unless a later resolver verifies the evidence reference.

Human-readable intent, memory, model output, agent output, UI state, old receipts, old grants, and inferred approval are not authority inputs.

## Killjoy

The grant is checked per operation; approval is not inherited by vibes.
