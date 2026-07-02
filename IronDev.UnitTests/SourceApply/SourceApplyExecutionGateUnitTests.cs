using IronDev.Core.SourceApply;

namespace IronDev.UnitTests.SourceApply;

[TestClass]
public sealed class SourceApplyExecutionGateUnitTests
{
    [TestMethod]
    public void CompleteBoundedEvidenceAllowsWorkingTreeApplyGateDecision()
    {
        var decision = SourceApplyExecutionGateTestFixtures.Evaluate();

        SourceApplyExecutionGateTestFixtures.AssertAllowed(decision);
        Assert.AreEqual(SourceApplyExecutionGateTestFixtures.RunId, decision.RunId);
        Assert.AreEqual(SourceApplyExecutionGateTestFixtures.SourceApplyExecutionRequestId, decision.SourceApplyExecutionRequestId);
        Assert.AreEqual(SourceApplyExecutionGateTestFixtures.SourceApplyRequestId, decision.SourceApplyRequestId);
        SourceApplyExecutionGateTestFixtures.AssertNoDownstreamAuthority(decision.Boundary);
    }

    [TestMethod]
    public void AllowDecisionContainsNoBlockReasons()
    {
        var decision = SourceApplyExecutionGateTestFixtures.Evaluate();

        Assert.AreEqual(0, decision.Reasons.Length);
    }

    [TestMethod]
    public void SourceApplyRequestMismatchesBlockWithDistinctReasons()
    {
        var cases = new (string Reason, Action<GateInputs> Mutate)[]
        {
            ("MissingSourceApplyRequest", inputs => inputs.ApplyRequest = null),
            ("SourceApplyRequestRunMismatch", inputs => inputs.ApplyRequest = inputs.ApplyRequest! with { RunId = "run:other" }),
            ("SourceApplyRequestIdMismatch", inputs => inputs.ApplyRequest = inputs.ApplyRequest! with { SourceApplyRequestId = "source-apply-request:other" }),
            ("SourceApplyRequestRepoMismatch", inputs => inputs.ApplyRequest = inputs.ApplyRequest! with { SourceRepoIdentity = "repo-identity:other" }),
            ("SourceApplyRequestBaseCommitMismatch", inputs => inputs.ApplyRequest = inputs.ApplyRequest! with { BaseCommit = "base-commit:other" }),
            ("SourceApplyRequestPatchHashMismatch", inputs => inputs.ApplyRequest = inputs.ApplyRequest! with { PatchSha256 = "sha256:other" })
        };

        AssertCasesBlock(cases);
    }

    [TestMethod]
    public void PatchVerificationMustBeSatisfied()
    {
        var missing = SourceApplyExecutionGateTestFixtures.Evaluate(inputs => inputs.Verification = null);
        var failed = SourceApplyExecutionGateTestFixtures.Evaluate(inputs =>
            inputs.Verification = inputs.Verification! with { Decision = PatchArtifactVerificationDecision.Blocked });

        SourceApplyExecutionGateTestFixtures.AssertBlocked(missing, "PatchVerificationNotSatisfied");
        SourceApplyExecutionGateTestFixtures.AssertBlocked(failed, "PatchVerificationNotSatisfied");
    }

    [TestMethod]
    public void ApprovalEvidenceMustMatchRequestAndRemainBounded()
    {
        var cases = new (string Reason, Action<GateInputs> Mutate)[]
        {
            ("MissingApprovalEvidence", inputs => inputs.Approval = null),
            ("ApprovalSourceApplyRequestMismatch", inputs => inputs.Approval = inputs.Approval! with { SourceApplyRequestId = "source-apply-request:other" }),
            ("ApprovalRunMismatch", inputs => inputs.Approval = inputs.Approval! with { RunId = "run:other" }),
            ("ApprovalSourceRepoMismatch", inputs => inputs.Approval = inputs.Approval! with { SourceRepoIdentity = "repo-identity:other" }),
            ("ApprovalBaseCommitMismatch", inputs => inputs.Approval = inputs.Approval! with { BaseCommit = "base-commit:other" }),
            ("ApprovalPatchHashMismatch", inputs => inputs.Approval = inputs.Approval! with { PatchSha256 = "sha256:other" }),
            ("ApprovalChangedFilesMismatch", inputs => inputs.Approval = inputs.Approval! with { ApprovedChangedFiles = ["src/g05/Other.cs"] }),
            ("ApprovalMissingHumanReviewer", inputs => inputs.Approval = inputs.Approval! with { ApprovedBy = "" }),
            ("ApprovalMissingHumanReviewer", inputs => inputs.Approval = inputs.Approval! with { HumanReviewRequired = false }),
            ("OverbroadApproval", inputs => inputs.Approval = inputs.Approval! with { ApprovalText = SourceApplyExecutionGateTestFixtures.BoundedApprovalText + " I also approve commit." }),
            ("OverbroadApproval", inputs => inputs.Approval = inputs.Approval! with { ApprovalText = SourceApplyExecutionGateTestFixtures.BoundedApprovalText + " I also approve push." }),
            ("OverbroadApproval", inputs => inputs.Approval = inputs.Approval! with { ApprovalText = SourceApplyExecutionGateTestFixtures.BoundedApprovalText + " I also approve pull request." }),
            ("OverbroadApproval", inputs => inputs.Approval = inputs.Approval! with { ApprovalText = SourceApplyExecutionGateTestFixtures.BoundedApprovalText + " I also approve merge." }),
            ("OverbroadApproval", inputs => inputs.Approval = inputs.Approval! with { ApprovalText = SourceApplyExecutionGateTestFixtures.BoundedApprovalText + " I also approve release." }),
            ("OverbroadApproval", inputs => inputs.Approval = inputs.Approval! with { ApprovalText = SourceApplyExecutionGateTestFixtures.BoundedApprovalText + " I also approve deployment." })
        };

        AssertCasesBlock(cases);
    }

    [TestMethod]
    public void BoundedApprovalTextAllowsWorkingTreeOnly()
    {
        var decision = SourceApplyExecutionGateTestFixtures.Evaluate(inputs =>
            inputs.Approval = inputs.Approval! with { ApprovalText = SourceApplyExecutionGateTestFixtures.BoundedApprovalText });

        SourceApplyExecutionGateTestFixtures.AssertAllowed(decision);
        SourceApplyExecutionGateTestFixtures.AssertNoDownstreamAuthority(decision.Boundary);
    }

    [TestMethod]
    public void ReadinessAndDryRunMustBeReadyAndRehearsalOnly()
    {
        var cases = new (string Reason, Action<GateInputs> Mutate)[]
        {
            ("MissingSourceApplyReadiness", inputs => inputs.Readiness = null),
            ("SourceApplyReadinessNotReady", inputs => inputs.Readiness = inputs.Readiness! with { Readiness = SourceApplyReadiness.Blocked }),
            ("MissingDryRunResult", inputs => inputs.DryRun = null),
            ("DryRunDidNotApplyPatch", inputs => inputs.DryRun = inputs.DryRun! with { PatchAppliedInRehearsalWorkspace = false }),
            ("DryRunBaseCommitMismatch", inputs => inputs.DryRun = inputs.DryRun! with { RehearsalHeadCommit = "base-commit:other" }),
            ("DryRunMutatedSourceRepo", inputs => inputs.DryRun = inputs.DryRun! with { SourceRepoMutated = true })
        };

        AssertCasesBlock(cases);
    }

    [TestMethod]
    public void RollbackPlanAndCleanPreSourceSnapshotAreRequired()
    {
        var cases = new (string Reason, Action<GateInputs> Mutate)[]
        {
            ("RollbackPlanMissing", inputs => inputs.RollbackDraft = null),
            ("MissingPreSourceSnapshot", inputs => inputs.PreSnapshot = null),
            ("SourceHeadMismatch", inputs => inputs.PreSnapshot = inputs.PreSnapshot! with { HeadCommit = "base-commit:other" }),
            ("SourceRepoDirty", inputs => inputs.PreSnapshot = inputs.PreSnapshot! with { StatusPorcelain = " M src/g05/Example.cs" })
        };

        AssertCasesBlock(cases);
    }

    [TestMethod]
    public void ConscienceDecisionMustAllowCurrentSourceApplySubjectAndRemainFresh()
    {
        var cases = new (string Reason, Action<GateInputs> Mutate)[]
        {
            ("MissingConscienceDecision", inputs => inputs.Conscience = null),
            ("ConscienceDecisionActionMismatch", inputs => inputs.Conscience = SourceApplyExecutionGateTestFixtures.Conscience(actionKind: GovernedActionKind.SourceRollback)),
            ("ConscienceDecisionDoesNotAllow", inputs => inputs.Conscience = SourceApplyExecutionGateTestFixtures.Conscience(outcome: ConscienceDecisionOutcome.Block)),
            ("ConscienceDecisionExpired", inputs => inputs.Conscience = SourceApplyExecutionGateTestFixtures.Conscience(expiresAtUtc: SourceApplyExecutionGateTestFixtures.ObservedAtUtc)),
            ("ConscienceDecisionSubjectMismatch", inputs => inputs.Conscience = SourceApplyExecutionGateTestFixtures.Conscience(subjectId: "source-apply-execution:other"))
        };

        AssertCasesBlock(cases);
    }

    [TestMethod]
    public void ThoughtLedgerReferenceIsRequiredButMayComeFromExplicitRef()
    {
        var missing = SourceApplyExecutionGateTestFixtures.Evaluate(inputs =>
        {
            inputs.ThoughtLedgerRef = null;
            inputs.Conscience = SourceApplyExecutionGateTestFixtures.Conscience(thoughtLedgerRef: null);
        });
        var explicitRef = SourceApplyExecutionGateTestFixtures.Evaluate(inputs =>
        {
            inputs.ThoughtLedgerRef = SourceApplyExecutionGateTestFixtures.ThoughtLedgerRef;
            inputs.Conscience = SourceApplyExecutionGateTestFixtures.Conscience(thoughtLedgerRef: null);
        });

        SourceApplyExecutionGateTestFixtures.AssertBlocked(missing, "MissingThoughtLedger");
        SourceApplyExecutionGateTestFixtures.AssertAllowed(explicitRef);
    }

    [TestMethod]
    public void AllowApplyToWorkingTreeDoesNotCreateReceiptOrDownstreamAuthority()
    {
        var decision = SourceApplyExecutionGateTestFixtures.Evaluate();
        var decisionPropertyNames = typeof(SourceApplyExecutionGateDecision)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        SourceApplyExecutionGateTestFixtures.AssertAllowed(decision);
        Assert.IsFalse(decisionPropertyNames.Any(name => name.Contains("Receipt", StringComparison.OrdinalIgnoreCase)));
        SourceApplyExecutionGateTestFixtures.AssertNoDownstreamAuthority(decision.Boundary);
    }

    [TestMethod]
    public void BlockedDecisionDoesNotAttemptFallbackExecution()
    {
        var decision = SourceApplyExecutionGateTestFixtures.Evaluate(inputs => inputs.Approval = null);

        SourceApplyExecutionGateTestFixtures.AssertBlocked(decision, "MissingApprovalEvidence");
        SourceApplyExecutionGateTestFixtures.AssertNoDownstreamAuthority(decision.Boundary);
    }

    [TestMethod]
    public void SourceApplyUnitTestsRemainFastLaneAndDependencyClean()
    {
        var root = SourceApplyExecutionGateTestFixtures.RepoRoot();
        var projectPath = Path.Combine(root, "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var sourceApplyTestFiles = Directory.GetFiles(Path.Combine(root, "IronDev.UnitTests", "SourceApply"), "*.cs");
        var combinedSourceApplyTests = string.Join(Environment.NewLine, sourceApplyTestFiles.Select(File.ReadAllText));
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
            Assert.IsFalse(combinedSourceApplyTests.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static void AssertCasesBlock(IEnumerable<(string Reason, Action<GateInputs> Mutate)> cases)
    {
        foreach (var (reason, mutate) in cases)
        {
            var decision = SourceApplyExecutionGateTestFixtures.Evaluate(mutate);

            SourceApplyExecutionGateTestFixtures.AssertBlocked(decision, reason);
            SourceApplyExecutionGateTestFixtures.AssertNoDownstreamAuthority(decision.Boundary);
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
        string.Concat("Controlled", "Source", "Apply", "Executor"),
        string.Concat("Process", ".Start"),
        string.Concat("File", ".Write"),
        string.Concat("DateTimeOffset", ".Utc", "Now"),
        string.Concat("DateTimeOffset", ".Now")
    ];
}
