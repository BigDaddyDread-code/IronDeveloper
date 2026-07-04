# P3-0a Critic Review Required For Gate Recommendation

## Purpose

P3-0a closes the recommendation gap where an empty critic-review set could pass review-derived checks vacuously and receive a low-risk `policy-would-approve-advisory-only` recommendation.

Policy cannot advise on work the critic never reviewed.

## Required Boundary Line

No critic review, no low-risk policy advice. Policy cannot advise on work the critic never reviewed. A gate recommendation remains advice only; it is not approval, continuation, apply permission, release readiness, or deployment readiness.

## Bug Being Closed

Before this slice, `SkeletonGateRecommendationService` used `All(...)` checks over `SkeletonRunReport.CriticReviews`.

When `CriticReviews` was empty, checks such as "every finding is dispositioned", "no blocking findings", and "no ground-truth mismatches" could pass without any critic review existing.

That made a missing witness look like clean evidence.

## Behavior After

`SkeletonGateRecommendationService.RecommendAsync(...)` now requires at least one critic review before any low-risk policy recommendation can be returned.

When no critic review exists:

- `Tier = HumanRequired`
- `Recommendation = human-judgment-required`
- `Reasons` includes: `No critic review is recorded for this run. Policy cannot advise on work the critic never reviewed.`
- Review-derived checks no longer pass vacuously.

Clean reviewed runs still receive the advisory low tier when every other low-risk condition is satisfied.

## Files Changed

- `IronDev.Infrastructure/Services/SkeletonGateRecommendationService.cs`
- `IronDev.IntegrationTests/SkeletonGateRecommendationTests.cs`
- `Docs/receipts/P3_0A_CRITIC_REVIEW_REQUIRED_FOR_RECOMMENDATION.md`

## Tests Added

- `NoCriticReview_CannotReceiveLowRiskRecommendation`
- `EmptyCriticReviews_AreNotEquivalentToCleanCriticReviews`

The existing clean-run advisory test still proves a run with a recorded clean critic review can receive:

- `Tier = Low`
- `Recommendation = policy-would-approve-advisory-only`

## Authority Boundary

P3-0a changes policy recommendation only.

It does not:

- run the critic
- request a critic review
- create a critic review
- record approval
- satisfy policy
- request continuation
- request apply
- continue workflow
- mutate source
- write reports
- write evidence
- publish anything
- change batch state
- change human gate behavior
- change release readiness
- change deployment readiness

Advice is still advice. Policy still cannot click.

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~SkeletonGateRecommendationTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- GitHub CI: tracked by the draft PR checks and PR body; this receipt records the local validation run before PR creation.

## Next PR

P3-0b - continuation/apply refuses missing critic review.

Review line: A human cannot continue work the critic never reviewed.

Killjoy line: Approval without a critic review is not informed approval.
