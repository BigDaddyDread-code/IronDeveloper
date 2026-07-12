using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class GovernedRefusalEnvelopeTests
{
    [TestMethod]
    public void Create_AlwaysReturnsCompleteNormalizedRefusal()
    {
        var refusal = GovernedRefusal.Create(
            "approval_missing",
            " Approval evidence is missing. ",
            "corr-1",
            blockedReasons: ["No accepted approval.", "No accepted approval.", " "],
            missingEvidence: ["accepted-approval"],
            nextSafeActions: ["Request approval."],
            forbiddenActions: ["Apply source changes."]);

        Assert.IsFalse(refusal.Allowed);
        Assert.AreEqual("approval_missing", refusal.ReasonCode);
        Assert.AreEqual("Approval evidence is missing.", refusal.Message);
        Assert.HasCount(1, refusal.BlockedReasons);
        Assert.HasCount(1, refusal.MissingEvidence);
        Assert.HasCount(1, refusal.NextSafeActions);
        Assert.HasCount(1, refusal.ForbiddenActions);
        Assert.AreEqual("corr-1", refusal.CorrelationId);
    }

    [TestMethod]
    public void GovernedControllers_ExposeCanonicalRefusalAndDoNotDefineLocalRefusalTypes()
    {
        var root = FindRepositoryRoot();
        foreach (var file in new[]
                 {
                     "GovernedWorkflowContinuationController.cs",
                     "GovernedReleaseGateController.cs"
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", file));
            StringAssert.Contains(source, "GovernedRefusalEnvelope? Refusal");
            StringAssert.Contains(source, "GovernedRefusal.Create(");
            Assert.IsFalse(source.Contains("record GovernedRefusal", StringComparison.Ordinal));
        }

        var scopeFilter = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Filters", "RouteBodyScopeBindingFilter.cs"));
        StringAssert.Contains(scopeFilter, "GovernedRefusal.Create(");
        Assert.IsFalse(scopeFilter.Contains("RouteBodyScopeMismatchResponse", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
