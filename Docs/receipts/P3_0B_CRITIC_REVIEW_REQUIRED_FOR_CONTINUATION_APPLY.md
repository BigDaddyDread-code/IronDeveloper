# P3-0b Critic Review Required For Continuation And Apply

## Purpose

P3-0b closes the continuation/apply gap where accepted approval evidence or malformed historical continuation evidence could let a skeleton run advance even though no durable critic review was recorded.

A human cannot continue or mutate work the critic never reviewed.

## Required Boundary Line

No critic review, no continuation, no apply. A critic review is a required independent witness before continuation or source mutation. It is not approval, authority, policy satisfaction, release readiness, or deployment readiness.

## Bug Being Closed

Before this slice, `TicketSkeletonRunService.ContinueAsync(...)` could evaluate live accepted approval records without first proving that a durable `SkeletonCriticReviewRecorded` event existed for the run.

`TicketSkeletonRunService.ApplyAsync(...)` could also rely on a prior continuation-unblocked event without re-checking that the run had a recorded critic review before source mutation.

That allowed a self-consistent bundle of package/approval/continuation evidence to skip the independent critic witness.

## Behavior After

`ContinueAsync(...)` now checks durable run events after staleness detection and before finding/package/approval checks.

When no critic review exists:

- publishes `ContinuationRefused`
- sets `refusedReason = CriticReviewMissing`
- sets `currentNode = SkeletonRun`
- leaves the run paused for approval
- does not consume accepted approval records

`ApplyAsync(...)` now checks durable run events after `ContinuationNotUnblocked` and before staleness, finding disposition, approval, package, mutation lease, or source-copy work.

When no critic review exists:

- publishes `SkeletonApplyRefused`
- sets `refusedReason = CriticReviewMissing`
- sets `currentNode = SkeletonApply`
- does not acquire a mutation lease
- does not start source-copy/apply work
- does not publish `SkeletonApplied`

Existing clearer refusal reasons still win where they already owned the gate:

- not awaiting approval
- stale after upstream apply
- missing package evidence
- undispositioned findings
- missing or unsatisfied approval when a critic review exists
- apply disabled
- continuation not unblocked

## Files Changed

- `IronDev.Infrastructure/Services/TicketSkeletonRunService.cs`
- `IronDev.IntegrationTests/SkeletonRunTests.cs`
- `Docs/receipts/P3_0B_CRITIC_REVIEW_REQUIRED_FOR_CONTINUATION_APPLY.md`

## Tests Added Or Updated

New regression tests:

- `Continue_WithAcceptedApprovalButNoCriticReview_IsRefused`
- `Apply_WithContinuationUnblockedButNoCriticReview_IsRefused`

Existing success-path tests now record a clean durable critic review when they intend to reach approval, continuation, apply, staleness, or mutation-lease behavior beyond the critic-review gate.

The static service-surface test now also rejects auto/run/create critic-review markers in the orchestrator service.

## Authority Boundary

P3-0b does not:

- run the critic
- request a critic review
- create a critic review
- fake or synthesize a critic review
- record approval
- satisfy policy
- grant continuation authority
- grant apply authority
- mutate source
- acquire a mutation lease unless the review gate has already passed
- create commits
- push branches
- create or update pull requests
- mark a draft PR ready for review
- merge
- release
- deploy
- promote memory
- continue workflow from UI, memory, status text, or receipt text

The critic review is a required witness. It is not permission.

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~SkeletonRunTests --logger "console;verbosity=minimal"`: passed, 50/50.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact P3-0b files.

GitHub CI: tracked by the draft PR checks and PR body; this receipt records the local validation run before PR creation.

## Next PR

P0-3 - approval consumption in the live run path.

Review line: Accepted approval is consumed live at the gate that owns it.

Killjoy line: A reviewed package can ask for approval. It still cannot approve itself.
