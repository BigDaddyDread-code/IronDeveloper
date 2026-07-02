namespace IronDev.UnitTests.Status;

[TestClass]
public sealed class OperationStatusMapperUnitTests
{
    [TestMethod]
    public void ReadyPatchProposalMapsToCompletedStatusWithoutApplyAuthority()
    {
        var result = PatchProposalGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.ReadyPatchProposal());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Concat(result.RedFlags)));
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State);
        Assert.AreEqual(PatchProposalGovernedOperationStatusMapper.OperationKind, result.Status.OperationKind);
        StatusMapperTestFixtures.AssertContains(result.Status.EvidenceRefs, "patch-proposal:proposal:g03");
        StatusMapperTestFixtures.AssertContains(result.Status.EvidenceRefs, "patch-hash:patch-hash:g03");
        StatusMapperTestFixtures.AssertContains(result.Status.ReceiptRefs, "patch-proposal-status-artifact:proposal:g03");
        StatusMapperTestFixtures.AssertContainsSubstring(result.Status.NextSafeActions, "request controlled source apply for patch hash patch-hash:g03");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not apply patch proposal directly to source");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat patch proposal completion as source apply authority");
    }

    [TestMethod]
    public void BlockedPatchProposalShowsMissingEvidenceInsteadOfControlledApply()
    {
        var result = PatchProposalGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.ReadyPatchProposal() with
        {
            StatusKind = PatchProposalStatusKind.Blocked,
            BlockedReasons = ["ValidationEvidenceMissing"],
            MissingEvidence = ["validation-result:g03"],
            ValidationRefs = []
        });

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Concat(result.RedFlags)));
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        StatusMapperTestFixtures.AssertContains(result.Status.BlockedReasons, "ValidationEvidenceMissing");
        StatusMapperTestFixtures.AssertContains(result.Status.MissingEvidence, "validation-result:g03");
        StatusMapperTestFixtures.AssertContains(result.Status.NextSafeActions, "collect missing validation evidence");
        StatusMapperTestFixtures.AssertDoesNotContainSubstring(result.Status.NextSafeActions, "controlled source apply");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not apply incomplete patch proposal");
    }

    [TestMethod]
    public void ReadyPatchProposalCannotCarryBlockedReasons()
    {
        var result = PatchProposalGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.ReadyPatchProposal() with
        {
            BlockedReasons = ["BlockedButReady"]
        });

        Assert.IsFalse(result.IsValid);
        StatusMapperTestFixtures.AssertContains(result.Issues, "ReadyPatchProposalCannotCarryBlockedReasons");
    }

    [TestMethod]
    public void AuthorityShapedPatchProposalTextIsReportedAsRedFlag()
    {
        var result = PatchProposalGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.ReadyPatchProposal() with
        {
            Subject = "patch proposal approves source apply"
        });

        Assert.IsFalse(result.IsValid);
        StatusMapperTestFixtures.AssertContains(result.RedFlags, "PatchProposalEvidenceCannotApprove");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat patch proposal completion as source apply authority");
    }

    [TestMethod]
    public void EligibleSourceApplyRequiresPolicySatisfactionEvidence()
    {
        var input = StatusMapperTestFixtures.EligibleSourceApply();
        var result = ControlledSourceApplyGovernedOperationStatusMapper.Map(input with
        {
            EvidenceRefs = input.EvidenceRefs
                .Where(value => !value.StartsWith("policy-satisfaction:", StringComparison.OrdinalIgnoreCase))
                .ToArray()
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State);
        StatusMapperTestFixtures.AssertContains(result.Issues, "EligibleSourceApplyPolicySatisfactionRequired");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat status as execution authority");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not apply from status alone");
    }

    [TestMethod]
    public void EligibleSourceApplyMapsNonExecutableStatusWhenEvidenceIsComplete()
    {
        var result = ControlledSourceApplyGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.EligibleSourceApply());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Concat(result.RedFlags)));
        Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State);
        StatusMapperTestFixtures.AssertContains(result.Status.EvidenceRefs, "source-apply:source-apply:g03");
        StatusMapperTestFixtures.AssertContains(result.Status.EvidenceRefs, "repo:repo:g03");
        StatusMapperTestFixtures.AssertContains(result.Status.EvidenceRefs, "policy-satisfaction:g03");
        StatusMapperTestFixtures.AssertContainsSubstring(result.Status.NextSafeActions, "request controlled source apply execution");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat status as execution authority");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow from source apply status");
    }

    [TestMethod]
    public void CompletedSourceApplyRequiresSourceApplyReceiptRef()
    {
        var result = ControlledSourceApplyGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.EligibleSourceApply() with
        {
            StatusKind = ControlledSourceApplyStatusKind.Completed,
            ReceiptRefs = ["receipt:g03"]
        });

        Assert.IsFalse(result.IsValid);
        StatusMapperTestFixtures.AssertContains(result.Issues, "SourceApplyCompletedReceiptRefRequired");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as commit authority");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat source apply receipt as rollback execution authority");
    }

    [TestMethod]
    public void CompletedSourceApplyMapsReceiptAsEvidenceOnly()
    {
        var result = ControlledSourceApplyGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.EligibleSourceApply() with
        {
            StatusKind = ControlledSourceApplyStatusKind.Completed,
            ReceiptRefs = ["source-apply-receipt:g03"]
        });

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Concat(result.RedFlags)));
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State);
        StatusMapperTestFixtures.AssertContains(result.Status.ReceiptRefs, "source-apply-receipt:g03");
        StatusMapperTestFixtures.AssertContains(result.Status.NextSafeActions, "review source apply receipt before requesting controlled commit package");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as commit authority");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as workflow continuation authority");
    }

    [TestMethod]
    public void SourceApplyReceiptCannotAuthorizeDownstreamOperations()
    {
        var result = ControlledSourceApplyGovernedOperationStatusMapper.Map(StatusMapperTestFixtures.EligibleSourceApply() with
        {
            StatusKind = ControlledSourceApplyStatusKind.Completed,
            ReceiptRefs = ["source-apply-receipt:g03"],
            ForbiddenActions = ["source apply receipt authorizes push"]
        });

        Assert.IsFalse(result.IsValid);
        StatusMapperTestFixtures.AssertContains(result.RedFlags, "SourceApplyReceiptCannotAuthorizeNextOperation");
        StatusMapperTestFixtures.AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as push authority");
    }

    [TestMethod]
    public void StatusMapperUnitTestsStayFastLaneAndDependencyClean()
    {
        var root = StatusMapperTestFixtures.RepoRoot();
        var projectPath = Path.Combine(root, "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var statusTestFiles = Directory.GetFiles(Path.Combine(root, "IronDev.UnitTests", "Status"), "*.cs");
        var combinedStatusTests = string.Join(Environment.NewLine, statusTestFiles.Select(File.ReadAllText));
        var project = XDocument.Load(projectPath);

        var projectReferences = project.Descendants("ProjectReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var packageReferences = project.Descendants("PackageReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.AreEqual(new[] { @"..\IronDev.Core\IronDev.Core.csproj" }, projectReferences);
        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" },
            packageReferences);

        foreach (var forbidden in ForbiddenFastLaneDependencyTokens())
        {
            Assert.IsFalse(combinedStatusTests.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static IReadOnlyList<string> ForbiddenFastLaneDependencyTokens() =>
    [
        string.Concat("IronDev", ".Api"),
        string.Concat("IronDev", ".Cli"),
        string.Concat("IronDev", ".Integration", "Tests"),
        string.Concat("IronDev", ".Infrastructure"),
        string.Concat("Web", "Application", "Factory"),
        string.Concat("Test", "Server"),
        string.Concat("Http", "Client"),
        string.Concat("Db", "Context"),
        string.Concat("Sql", "Connection"),
        string.Concat("Test", "containers"),
        string.Concat("Git", "Hub"),
        string.Concat("Pro", "vider")
    ];
}
