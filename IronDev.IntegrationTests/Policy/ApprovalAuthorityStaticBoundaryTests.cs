namespace IronDev.IntegrationTests.Policy;

[TestClass]
[TestCategory("ApprovalAuthorityBoundary")]
[TestCategory("ApprovalAuthorityStaticBoundary")]
public sealed class ApprovalAuthorityStaticBoundaryTests
{
    [TestMethod] public void ApprovalBoundaryWording_DoesNotSayGateApproved() => AssertNoUnsafePositiveWording("gate approved", "approved by gate");
    [TestMethod] public void ApprovalBoundaryWording_DoesNotSayDogfoodApprovedRelease() => AssertNoUnsafePositiveWording("dogfood approved release", "dogfood release approved");
    [TestMethod] public void ApprovalBoundaryWording_DoesNotSayCriticApproved() => AssertNoUnsafePositiveWording("critic approved");
    [TestMethod] public void ApprovalBoundaryWording_DoesNotSayPolicySatisfiedWithoutApproval() => AssertNoUnsafePositiveWording("policy satisfied without approval", "policy satisfied by evaluator", "policy satisfied by package");
    [TestMethod] public void ApprovalBoundaryWording_DoesNotSayReadyForReviewMeansApproved() => AssertNoUnsafePositiveWording("readyforreview means approved", "ready for review means approved");
    [TestMethod] public void ApprovalBoundaryWording_DoesNotSayExperimentalMeansPermission() => AssertNoUnsafePositiveWording("experimental means permission", "experimental means free", "experimental grants permission");
    [TestMethod] public void ApprovalBoundary_Static_NoGateDecisionConsumedAsApproval() => AssertRuntimeDoesNotContain("GateDecisionApproved", "ApprovedByGate", "GateApproved");
    [TestMethod] public void ApprovalBoundary_Static_NoDogfoodReceiptConsumedAsApproval() => AssertRuntimeDoesNotContain("DogfoodApprovedRelease", "DogfoodReleaseApproved", "ReceiptApprovedRelease");
    [TestMethod] public void ApprovalBoundary_Static_NoCriticOutputConsumedAsApproval() => AssertRuntimeDoesNotContain("CriticApproved", "ApprovedByCritic", "CriticApproval");
    [TestMethod] public void ApprovalBoundary_Static_NoApprovalPackageConsumedAsApproval() => AssertRuntimeDoesNotContain("ReadyForReviewMeansApproved", "ApprovalPackageApproved", "PackageGrantsApproval");

    [TestMethod]
    public void ApprovalBoundary_Static_NoPolicyProfileConsumedAsRuntimeDefault()
    {
        var program = ReadFile("IronDev.Api", "Program.cs");
        var cli = ReadFile("tools", "IronDev.Cli", "IronDevCli.cs");

        Assert.IsFalse(program.Contains("ProjectPolicyProfileFactory", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("ProjectPolicyProfileFactory", StringComparison.Ordinal));
    }

    [TestMethod] public void ApprovalBoundary_Static_NoEvaluatorResultConsumedAsExecutionPermission() => AssertRuntimeDoesNotContain("ApprovalRequirementEvaluationResult.GrantsExecution", "NoApprovalRequiredExecutes", "EvaluationAllowsExecution");
    [TestMethod] public void ApprovalBoundary_Static_NoValidationOutputConsumedAsReleaseApproval() => AssertRuntimeDoesNotContain("ValidationPassedReleaseApproved", "BuildPassedReleaseApproved", "TestPassedReleaseApproved");
    [TestMethod] public void ApprovalBoundary_Static_NoApprovalSatisfactionCheckerAdded() => AssertRuntimeDoesNotContain("IApprovalSatisfactionChecker", "ApprovalSatisfactionService", "SatisfyApprovalRequirement");
    [TestMethod] public void ApprovalBoundary_Static_NoApprovalDecisionLookupAddedToPolicyLayer() => AssertFileDoesNotContain(Path.Combine("IronDev.Core", "Policy", "ApprovalRequirementEvaluatorModels.cs"), "ApprovalDecisionStore", "IApprovalDecisionStore", "ApprovalDecisionReadModel");
    [TestMethod] public void ApprovalBoundary_Static_NoWorkflowRunnerAdded() => AssertRuntimeDoesNotContain("IWorkflowRunner", "WorkflowRunner", "ContinueWorkflowFromApproval");
    [TestMethod] public void ApprovalBoundary_Static_NoA2aRuntimeAdded() => AssertRuntimeDoesNotContain("IA2aRuntime", "A2aMessageBus", "ExecuteA2aHandoff");
    [TestMethod] public void ApprovalBoundary_Static_NoLangGraphRuntimeAdded() => AssertRuntimeDoesNotContain("LangGraphRuntime", "ILangGraphRunner", "LangGraphOrchestrator");
    [TestMethod] public void ApprovalBoundary_Static_NoSourceApplyRuntimeAdded() => AssertRuntimeDoesNotContain("SourceApplyAllowedByApproval", "ApprovalAllowsSourceApply", "ApplySourceAfterApproval");
    [TestMethod] public void ApprovalBoundary_Static_NoMemoryPromotionRuntimeAdded() => AssertRuntimeDoesNotContain("MemoryPromotionAllowedByApproval", "ApprovalAllowsMemoryPromotion", "PromoteMemoryAfterApproval");

    private static void AssertNoUnsafePositiveWording(params string[] phrases)
    {
        var text = RuntimeAndDocsText();
        foreach (var phrase in phrases)
            Assert.IsFalse(text.Contains(phrase, StringComparison.OrdinalIgnoreCase), phrase);
    }

    private static void AssertRuntimeDoesNotContain(params string[] tokens)
    {
        var text = RuntimeText();
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
    }

    private static void AssertFileDoesNotContain(string relativePath, params string[] tokens)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), token);
    }

    private static string RuntimeAndDocsText() => RuntimeText() + Environment.NewLine + ReadFile("Docs", "BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md");

    private static string RuntimeText()
    {
        var root = RepositoryRoot();
        var directories = new[]
        {
            Path.Combine(root, "IronDev.Api"),
            Path.Combine(root, "tools", "IronDev.Cli"),
            Path.Combine(root, "IronDev.Core", "Policy"),
            Path.Combine(root, "IronDev.Core", "Governance")
        };

        return string.Join(Environment.NewLine, directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Select(File.ReadAllText));
    }

    private static string ReadFile(params string[] pathParts) => File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
