using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class L4CandidateCannotMutateSourceOrMemoryTests
{
    [TestMethod]
    public void TestFailureReviewCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "TestFailureReviewCandidateResult").Result);

    [TestMethod]
    public void CriticReviewRequestCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "CriticReviewRequestCandidateResult").Result);

    [TestMethod]
    public void ImplementationProposalPackageCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "ImplementationProposalPackageCandidateResult").Result);

    [TestMethod]
    public void ToolRequestGatePreviewCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "ToolRequestGatePreviewCandidateResult").Result);

    [TestMethod]
    public void MemoryImprovementPackageCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "MemoryImprovementPackageCandidateResult").Result);

    [TestMethod]
    public void HumanApprovalPackageCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "HumanApprovalPackageCandidateResult").Result);

    [TestMethod]
    public void DogfoodEvidenceBundleCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "DogfoodEvidenceBundleCandidateResult").Result);

    [TestMethod]
    public void RepeatedFailurePatternReviewCandidateResult_CannotMutateSourceOrApplyPatch() => AssertCannotMutateSourceOrApplyPatch(CandidateResults().Single(candidate => candidate.Name == "RepeatedFailurePatternReviewCandidateResult").Result);

    [TestMethod]
    public void TestFailureReviewCandidateResult_CannotPromoteMemoryOrActivateRetrieval() => AssertCannotPromoteMemoryOrActivateRetrieval(CandidateResults().Single(candidate => candidate.Name == "TestFailureReviewCandidateResult").Result);

    [TestMethod]
    public void CriticReviewRequestCandidateResult_CannotPromoteMemoryOrActivateRetrieval() => AssertCannotPromoteMemoryOrActivateRetrieval(CandidateResults().Single(candidate => candidate.Name == "CriticReviewRequestCandidateResult").Result);

    [TestMethod]
    public void ImplementationProposalPackageCandidateResult_CannotPromoteMemoryOrActivateRetrieval() => AssertCannotPromoteMemoryOrActivateRetrieval(CandidateResults().Single(candidate => candidate.Name == "ImplementationProposalPackageCandidateResult").Result);

    [TestMethod]
    public void ToolRequestGatePreviewCandidateResult_CannotPromoteMemoryOrActivateRetrieval() => AssertCannotPromoteMemoryOrActivateRetrieval(CandidateResults().Single(candidate => candidate.Name == "ToolRequestGatePreviewCandidateResult").Result);

    [TestMethod]
    public void MemoryImprovementPackageCandidateResult_CannotPromoteOrMutateAcceptedMemory()
    {
        var result = CandidateResults().Single(candidate => candidate.Name == "MemoryImprovementPackageCandidateResult").Result;

        AssertCannotPromoteMemoryOrActivateRetrieval(result);
        AssertFalsePropertyIfPresent(result, "IsAcceptedMemory");
        AssertFalsePropertyIfPresent(result, "IsPromotion");
        AssertFalsePropertyIfPresent(result, "CanMutateAcceptedMemory");
        AssertFalsePropertyIfPresent(result, "CanWriteSql");
        AssertFalsePropertyIfPresent(result, "CanWriteVectorStore");
        AssertFalsePropertyIfPresent(result, "CanGenerateEmbedding");
        AssertFalsePropertyIfPresent(result, "CanResolveDuplicate");
        AssertFalsePropertyIfPresent(result, "CanResolveConflict");
        AssertFalsePropertyIfPresent(result, "CanMarkStale");
    }

    [TestMethod]
    public void HumanApprovalPackageCandidateResult_CannotPromoteMemoryOrActivateRetrieval() => AssertCannotPromoteMemoryOrActivateRetrieval(CandidateResults().Single(candidate => candidate.Name == "HumanApprovalPackageCandidateResult").Result);

    [TestMethod]
    public void DogfoodEvidenceBundleCandidateResult_CannotPromoteMemoryOrActivateRetrieval() => AssertCannotPromoteMemoryOrActivateRetrieval(CandidateResults().Single(candidate => candidate.Name == "DogfoodEvidenceBundleCandidateResult").Result);

    [TestMethod]
    public void RepeatedFailurePatternReviewCandidateResult_CannotPromoteMemoryOrActivateRetrieval() => AssertCannotPromoteMemoryOrActivateRetrieval(CandidateResults().Single(candidate => candidate.Name == "RepeatedFailurePatternReviewCandidateResult").Result);

    [DataTestMethod]
    [DataRow("CanInvokeTool")]
    [DataRow("CanDispatchAgent")]
    [DataRow("CanCallModel")]
    [DataRow("CanBuildPrompt")]
    [DataRow("CanCreateTicket")]
    [DataRow("CanCreateIncident")]
    [DataRow("CanTransitionWorkflow")]
    [DataRow("CanSatisfyApproval")]
    [DataRow("CanSatisfyPolicy")]
    [DataRow("CanWriteSql")]
    [DataRow("CanRunCommand")]
    [DataRow("CanRunTests")]
    public void AllCandidateResultTypes_CannotPerformRuntimeActions(string propertyName)
    {
        foreach (var candidate in CandidateResults())
            AssertFalsePropertyIfPresent(candidate.Result, propertyName);
    }

    [TestMethod]
    public void AllCandidateResults_CarryNoSourceMemoryRuntimeOrGateAuthority()
    {
        foreach (var candidate in CandidateResults())
            AssertNoAuthorityProperties(candidate.Result);
    }

    internal static IReadOnlyList<(string Name, object Result)> CandidateResults() =>
    [
        ("TestFailureReviewCandidateResult", new TestFailureReviewCandidateWorkflow().Review(TestFailureReviewCandidateFixtures.ValidRequest())),
        ("CriticReviewRequestCandidateResult", new CriticReviewRequestCandidateWorkflow().Prepare(CriticReviewRequestCandidateFixtures.ValidRequest())),
        ("ImplementationProposalPackageCandidateResult", new ImplementationProposalPackageCandidateWorkflow().Prepare(ImplementationProposalPackageFixtures.ValidRequest())),
        ("ToolRequestGatePreviewCandidateResult", HumanApprovalPackageFixtures.ToolPreview()),
        ("MemoryImprovementPackageCandidateResult", HumanApprovalPackageFixtures.MemoryImprovement()),
        ("HumanApprovalPackageCandidateResult", new HumanApprovalPackageCandidateWorkflow().Prepare(HumanApprovalPackageFixtures.ValidRequest())),
        ("DogfoodEvidenceBundleCandidateResult", new DogfoodEvidenceBundleCandidateWorkflow().Prepare(DogfoodEvidenceBundleFixtures.ValidRequest())),
        ("RepeatedFailurePatternReviewCandidateResult", new RepeatedFailurePatternReviewCandidateWorkflow().Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest()))
    ];

    internal static void AssertCannotMutateSourceOrApplyPatch(object result)
    {
        AssertFalsePropertyIfPresent(result, "CanMutateSource");
        AssertFalsePropertyIfPresent(result, "CanApplyPatch");
        AssertFalsePropertyIfPresent(result, "IsImplementation");
        AssertFalsePropertyIfPresent(result, "IsPatch");
    }

    internal static void AssertCannotPromoteMemoryOrActivateRetrieval(object result)
    {
        AssertFalsePropertyIfPresent(result, "CanPromoteMemory");
        AssertFalsePropertyIfPresent(result, "CanActivateRetrieval");
        AssertFalsePropertyIfPresent(result, "IsAcceptedMemory");
        AssertFalsePropertyIfPresent(result, "IsPromotion");
    }

    internal static void AssertNoAuthorityProperties(object result)
    {
        foreach (var propertyName in NoAuthorityPropertyNames)
            AssertFalsePropertyIfPresent(result, propertyName);
    }

    internal static void AssertFalsePropertyIfPresent(object result, string propertyName)
    {
        var property = result.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
            return;

        Assert.AreEqual(typeof(bool), property.PropertyType, $"{result.GetType().Name}.{propertyName} must remain a bool authority flag.");
        Assert.AreEqual(false, property.GetValue(result), $"{result.GetType().Name}.{propertyName} must be false.");
    }

    internal static readonly string[] NoAuthorityPropertyNames =
    [
        "CanMutateSource",
        "CanApplyPatch",
        "CanPromoteMemory",
        "CanMutateAcceptedMemory",
        "CanActivateRetrieval",
        "CanInvokeTool",
        "CanAuthorizeTool",
        "CanReserveTool",
        "CanRunCommand",
        "CanRunTests",
        "CanDispatchAgent",
        "CanDispatchCriticAgent",
        "CanCallModel",
        "CanBuildPrompt",
        "CanCreateTicket",
        "CanCreateIncident",
        "CanSatisfyApproval",
        "CanSatisfyPolicy",
        "CanTransitionWorkflow",
        "CanWriteSql",
        "CanWriteVectorStore",
        "CanGenerateEmbedding",
        "CanResolveDuplicate",
        "CanResolveConflict",
        "CanMarkStale",
        "CanPostReviewComment",
        "CanApprove",
        "CanReject",
        "CanRunDogfood",
        "IsImplementation",
        "IsPatch",
        "IsToolExecution",
        "IsApprovalDecision",
        "IsApproved",
        "IsRejected",
        "IsAcceptedMemory",
        "IsPromotion",
        "IsValidationProof",
        "IsReleaseReady",
        "IsRootCauseProof",
        "IsPatternProof"
    ];
}
