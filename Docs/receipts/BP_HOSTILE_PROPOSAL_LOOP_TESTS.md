# BP Hostile Proposal-loop Tests

## Review Line

Hostile proposal evidence must stay evidence. It must not become authority.

## Receipt

This slice adds hostile proposal-loop boundary tests.

It proves hostile proposal evidence cannot become authority.

It exercises ProposalOnly, validation result packages, disposable workspace patch packages, canonical status validation, and status inspection.

It does not add a runner.
It does not run validation.
It does not generate code changes.
It does not apply source.
It does not mutate durable source.
It does not commit.
It does not push.
It does not create PRs.
It does not approve.
It does not satisfy policy.
It does not promote memory.
It does not continue workflow.

Hostile validation messages remain evidence only.
Hostile patch text remains evidence only.
Hostile refs remain refs only.
Validation passed is not approval.
Patch package completed is not source apply authority.
NextSafeActions are guidance only.
Durable source remains unchanged.

## Boundary

This slice adds tests and a receipt only. It does not add new authority models, execution paths, CLI commands, runners, provider gateways, approval flows, policy satisfaction flows, memory promotion flows, or workflow continuation flows.

The hostile tests exercise strings that claim approval, policy satisfaction, source apply authority, commit authority, push authority, PR authority, merge authority, release authority, deployment authority, memory authority, workflow authority, and rollback authority. Those strings must stay inside evidence, refs, summaries, status lines, or red-flag diagnostics.

## Killjoy

A hostile package can shout "approved." The system must still hear "evidence."
