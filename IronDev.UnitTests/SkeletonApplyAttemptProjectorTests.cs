using IronDev.Core.Builder;
using IronDev.Core.RunReports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests;

[TestClass]
public sealed class SkeletonApplyAttemptProjectorTests
{
    [TestMethod]
    public void InterruptedBeforeCopy_OffersFreshResumeAndRetry()
    {
        var attempt = SkeletonApplyAttemptProjector.Build(
        [
            Event("SkeletonApplyAttemptStarted", ("applyAttemptId", "run-1-apply-001"), ("attemptNumber", "1")),
            Event("SkeletonApplyStageStarted", ("applyAttemptId", "run-1-apply-001"), ("stage", "validate")),
            Event("SkeletonApplyInterrupted", ("applyAttemptId", "run-1-apply-001"), ("stage", "validate"))
        ]).Single();

        Assert.AreEqual(SkeletonApplyAttemptStatuses.Interrupted, attempt.Status);
        Assert.AreEqual(SkeletonApplyMutationStates.NotObserved, attempt.MutationState);
        CollectionAssert.Contains(attempt.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Resume);
        CollectionAssert.Contains(attempt.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Retry);
    }

    [TestMethod]
    public void InterruptedDuringCopy_RequiresManualReviewAndBlocksRetry()
    {
        var attempt = SkeletonApplyAttemptProjector.Build(
        [
            Event("SkeletonApplyAttemptStarted", ("applyAttemptId", "run-1-apply-001"), ("attemptNumber", "1")),
            Event("SkeletonApplyStageStarted", ("applyAttemptId", "run-1-apply-001"), ("stage", "apply-copy")),
            Event("SkeletonApplyInterrupted", ("applyAttemptId", "run-1-apply-001"), ("stage", "apply-copy"))
        ]).Single();

        Assert.AreEqual(SkeletonApplyMutationStates.Uncertain, attempt.MutationState);
        CollectionAssert.DoesNotContain(attempt.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Resume);
        CollectionAssert.DoesNotContain(attempt.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Retry);
        CollectionAssert.Contains(attempt.AvailableActions.ToList(), SkeletonApplyRecoveryActions.ManualReview);
        CollectionAssert.Contains(attempt.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Abandon);
    }

    [TestMethod]
    public void FailedPreMutationAttempt_PreservesIdentityAndOffersRetry()
    {
        var attempt = SkeletonApplyAttemptProjector.Build(
        [
            Event("SkeletonApplyAttemptStarted",
                ("applyAttemptId", "run-1-apply-002"),
                ("attemptNumber", "2"),
                ("requestedAction", "Retry"),
                ("workspacePath", "C:/attempts/run-1-apply-002")),
            Event("SkeletonApplyStageStarted", ("applyAttemptId", "run-1-apply-002"), ("stage", "validate")),
            Event("SkeletonApplyStage", ("applyAttemptId", "run-1-apply-002"), ("stage", "validate"), ("succeeded", "false")),
            Event("SkeletonApplyRefused", ("applyAttemptId", "run-1-apply-002"), ("refusedReason", "SpineBlocked:validate"))
        ]).Single();

        Assert.AreEqual("run-1-apply-002", attempt.AttemptId);
        Assert.AreEqual(2, attempt.AttemptNumber);
        Assert.AreEqual(SkeletonApplyAttemptStatuses.Failed, attempt.Status);
        Assert.AreEqual("C:/attempts/run-1-apply-002", attempt.WorkspacePath);
        CollectionAssert.Contains(attempt.AvailableActions.ToList(), SkeletonApplyRecoveryActions.Retry);
    }

    private static RunEventDto Event(string eventType, params (string Key, string Value)[] payload) => new()
    {
        RunId = "run-1",
        EventType = eventType,
        TimestampUtc = DateTimeOffset.UtcNow,
        Payload = payload.ToDictionary(item => item.Key, item => item.Value)
    };
}
