# BM Proposal-only Run Profile

## Review Line

No-approval mode creates evidence, not authority.

## Receipt

This slice adds the ProposalOnly run profile boundary.

ProposalOnly allows safe no-approval proposal work:
- repo inspection
- task interpretation
- disposable workspace preparation
- disposable workspace modification
- disposable workspace validation
- patch proposal
- patch package writing
- governed status inspection

ProposalOnly does not approve.
ProposalOnly does not satisfy policy.
ProposalOnly does not execute source apply.
ProposalOnly does not mutate source.
ProposalOnly does not commit.
ProposalOnly does not push.
ProposalOnly does not create PRs.
ProposalOnly does not mark ready for review.
ProposalOnly does not merge.
ProposalOnly does not release.
ProposalOnly does not deploy.
ProposalOnly does not execute rollback.
ProposalOnly does not promote memory.
ProposalOnly does not continue workflow.
ProposalOnly does not create authority records.

ProposalOnly can create evidence.
ProposalOnly evidence is not authority.
Patch proposal evidence is not approval.
Validation success is not approval.
NextSafeActions are guidance only.

## Boundary

This PR adds a profile/evaluator/status slice only. It does not add a full runner, disposable workspace creation, patch generation, source apply, rollback, commit, push, pull request creation, merge, release, deployment, memory promotion, workflow continuation, approval creation, or policy satisfaction.

The evaluator returns canonical GovernedOperationStatus records for allowed and blocked ProposalOnly operations, then validates those records through GovernedOperationStatusValidator before reporting the result.

Allowed operations remain inside the ProposalOnly boundary. Blocked operations return Blocked status with explicit missing authority and guidance-only next safe actions.

## Killjoy

ProposalOnly can build the case. It cannot carry out the sentence.
