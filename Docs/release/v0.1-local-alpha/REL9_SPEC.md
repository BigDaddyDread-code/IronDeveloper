# REL-9 - Optional BookSeller Three-Ticket Batch

## Purpose

Prove that the BookSeller alpha path can handle a small batch of three tickets only after the single-ticket release path is boring, repeatable, and no longer teaching us new setup/gate lessons.

REL-9 is optional. It must not block v0.1 Local Alpha if REL-8 gives a clean single-ticket verdict and the remaining risk is batch scale rather than release-path correctness.

## Review Line

Batch proof is confidence evidence. It is not a substitute for a boring single-ticket release path.

## Killjoy Line

Three tickets do not make the product mature. They only prove the first ticket was not a lucky accident.

## Suggested PR

Title:

```text
release(alpha): add optional BookSeller three-ticket batch proof
```

Branch:

```text
release/rel9-bookseller-three-ticket-batch
```

## Entry Criteria

Do not start REL-9 until:

- REL-8 has a verdict.
- The single-ticket path reaches the intended final state repeatably.
- The setup/runbook path has no unresolved author-only steps.
- The release doctor and known limitations are honest.
- Any UI journey required for the release has either passed or been explicitly descoped.

If those are not true, REL-9 should remain parked.

## Required Batch Shape

Use exactly three deterministic BookSeller tickets:

```text
validate-book
normalise-book-metadata
reject-duplicate-isbn
```

If these exact tickets do not exist, add fixture tickets with stable names and narrow acceptance criteria.

Each ticket must produce:

- Ticket identity
- Patch package or proposal evidence
- Build/test evidence
- Critic package/review evidence
- Approval/gate state
- Continuation/apply result, if the single-ticket path includes apply
- Final report or named blocker

## Required Batch Semantics

- Tickets run independently unless the backend already has a governed batch concept.
- No shared approval across tickets.
- No shared continuation across tickets.
- No shared apply receipt across tickets.
- One ticket failure must not be hidden by two successes.
- Batch summary must show per-ticket verdicts and aggregate verdict.
- Aggregate success requires all required tickets to pass.

## Required Verdict Vocabulary

Per ticket:

```text
TicketApplied
TicketPausedForApproval
TicketBlocked
TicketFailed
TicketDescoped
```

Aggregate:

```text
BatchPassed
BatchBlocked
BatchFailed
BatchPartiallyPassed
BatchEvidenceIncomplete
```

## Required Evidence Artifacts

Suggested paths:

```text
Docs/release/v0.1-local-alpha/DOGFOOD-BOOKSELLER-BATCH-001.md
Docs/release/v0.1-local-alpha/DOGFOOD-BOOKSELLER-BATCH-001.json
Docs/receipts/REL9_BOOKSELLER_THREE_TICKET_BATCH.md
```

The batch artifact must name each ticket, run ID, final state, receipt refs, report refs, and known limitation.

## Required Tests

Contract tests:

```text
BookSellerBatch001_TranscriptExists
BookSellerBatch001_JsonListsExactlyThreeTickets
BookSellerBatch001_EachTicketHasIndependentRunId
BookSellerBatch001_EachTicketHasIndependentApprovalAndApplyEvidence
BookSellerBatch001_AggregateVerdictCannotHideTicketFailure
BookSellerBatch001_UsesAllowedVerdictVocabulary
BookSellerBatch001_DoesNotClaimReleaseReadiness
BookSellerBatch001_DoesNotLeakSecretsOrUserLocalPaths
```

If a batch script is added:

```text
BookSellerBatchScript_CheckOnlyDoesNotStartRuns
BookSellerBatchScript_RequiresExplicitRunSwitch
BookSellerBatchScript_DoesNotReuseApprovalAcrossTickets
BookSellerBatchScript_WritesPerTicketEvidence
```

## Forbidden Behavior

- No batch proof before REL-8 has a verdict.
- No shared authority between tickets.
- No aggregate pass that hides a failed ticket.
- No skipped ticket counted as pass.
- No automatic approval unless the single-ticket path already explicitly supports deterministic approval for smoke only.
- No new release authority.
- No release, tag, deploy, publish, or upload action.
- No UI state stronger than backend state.

## Acceptance Criteria

- Three named BookSeller tickets are represented.
- Each ticket has independent run/evidence state.
- Batch summary records per-ticket and aggregate verdicts.
- Failures and blockers remain visible.
- Batch proof is explicitly optional and confidence-building only.
- The docs say REL-9 does not replace REL-8.

## Review Traps

Block if:

- REL-9 is used to avoid finishing REL-8.
- Batch success is claimed from only one ticket.
- Batch summary hides per-ticket evidence.
- Approval, continuation, or apply evidence is reused across tickets.
- A partial pass is marketed as release readiness.
- The test only checks labels and not backend/result state.

## Out Of Scope

- Public release.
- Production batch orchestration.
- Real-repo import.
- Live model batch.
- Multi-project batch.
- Performance testing.
- Load testing.

## Next PR

Only after REL-8 and optional REL-9 are boring: external alpha candidate packaging/signoff, or park release work and return to product usability gaps found by dogfood.
