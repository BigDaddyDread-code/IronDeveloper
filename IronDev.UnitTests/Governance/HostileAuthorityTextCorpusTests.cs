namespace IronDev.UnitTests.Governance;

[TestClass]
public sealed class HostileAuthorityTextCorpusTests
{
    [TestMethod]
    public void MemoryHostileCorpusBlocksAuthorityClaimsWithoutGrantingAnything()
    {
        foreach (var item in HostileAuthorityTextCorpusFixtures.MemoryClaims)
        {
            var decision = MemoryNonAuthorityReportBuilder.EvaluateAttempt(
                HostileAuthorityTextCorpusFixtures.MemoryAttempt(item));

            Assert.AreEqual(item.ExpectedMemoryVerdict, decision.Verdict, item.Id);
            Assert.AreEqual(item.ExpectedMemoryBlockReason, decision.BlockReason, item.Id);
            AssertAuthorityFlagsFalse(decision, item.Id);
            Assert.IsTrue(
                decision.HumanSummary.Contains("No mutation happened.", StringComparison.OrdinalIgnoreCase),
                item.Id);
            AssertSafeNextStepIsGuidanceOnly(decision.SafeNextStep, item.Id);
        }
    }

    [TestMethod]
    public void MemoryHostileCorpusSummaryTreatsBlockedClaimsAsEvidenceOnly()
    {
        var attempts = HostileAuthorityTextCorpusFixtures.MemoryClaims
            .Select(HostileAuthorityTextCorpusFixtures.MemoryAttempt)
            .ToArray();

        var artifacts = MemoryNonAuthorityReportBuilder.EvaluateAttempts(
            "g10-hostile-memory",
            HostileAuthorityTextCorpusFixtures.ObservedAtUtc,
            attempts);

        Assert.AreEqual(attempts.Length, artifacts.Summary.TotalAttempts);
        Assert.AreEqual(0, artifacts.Summary.MemoryAcceptedAsAuthorityCount);
        Assert.AreEqual(0, artifacts.Summary.ApprovalSatisfiedByMemoryCount);
        Assert.AreEqual(0, artifacts.Summary.PolicySatisfiedByMemoryCount);
        Assert.AreEqual(0, artifacts.Summary.ExecutionAuthorizedByMemoryCount);
        Assert.AreEqual(0, artifacts.Summary.MutationAuthorizedByMemoryCount);
        Assert.AreEqual(0, artifacts.Summary.WorkflowContinuationAuthorizedByMemoryCount);
        Assert.AreEqual(0, artifacts.Summary.MemoryPromotionAuthorizedByMemoryCount);
        Assert.IsTrue(artifacts.Boundary.ContextOnly);
        Assert.IsFalse(artifacts.Boundary.CanApprove);
        Assert.IsFalse(artifacts.Boundary.CanExecute);
        Assert.IsFalse(artifacts.Boundary.CanMutate);
        StringAssert.Contains(artifacts.MarkdownReport, "Memory must not authorize action.");
    }

    [TestMethod]
    public void StatusHostileCorpusReportsAuthorityClaimsAsRedFlags()
    {
        foreach (var item in HostileAuthorityTextCorpusFixtures.StatusClaims)
        {
            var validation = GovernedOperationStatusValidator.Validate(
                HostileAuthorityTextCorpusFixtures.StatusWithHostileText(item));

            Assert.IsFalse(validation.IsValid, item.Id);
            CollectionAssert.Contains(validation.Issues.ToList(), "StatusImpliesAuthority", item.Id);
            CollectionAssert.Contains(validation.RedFlags.ToList(), item.ExpectedRedFlag, item.Id);
            Assert.IsTrue(validation.Boundary.StatusOnly, item.Id);
            Assert.IsFalse(validation.Boundary.CanExecute, item.Id);
            Assert.IsFalse(validation.Boundary.CanMutate, item.Id);
            Assert.IsFalse(validation.Boundary.CanContinueWorkflow, item.Id);
        }
    }

    [TestMethod]
    public void StatusHostileEvidenceCannotSatisfyAskBeforeMutationApproval()
    {
        var status = AuthorityProfileStatusMapper.Map(
            HostileAuthorityTextCorpusFixtures.AskBeforeMutationSourceApplyWithHostileEvidence());

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        CollectionAssert.Contains(status.BlockedReasons.ToList(), "MutationRequiresExplicitHumanApproval");
        CollectionAssert.Contains(status.MissingEvidence.ToList(), "accepted-apply-approval");
        CollectionAssert.Contains(status.MissingEvidence.ToList(), "accepted-source-apply-request");
        CollectionAssert.Contains(status.ForbiddenActions.ToList(), "do not apply source from patch readiness alone");
        CollectionAssert.Contains(status.ForbiddenActions.ToList(), "do not treat validation passed as approval");
    }

    [TestMethod]
    public void EligibleStatusWithHostileTextStillCarriesNonExecutionWarnings()
    {
        var status = AuthorityProfileStatusMapper.Map(
            HostileAuthorityTextCorpusFixtures.BoundedRunEligibleWithHostileEvidenceRefs());

        Assert.AreEqual(GovernedOperationState.Eligible, status.State);
        CollectionAssert.Contains(status.ForbiddenActions.ToList(), AuthorityGlossary.DoNotExecuteFromStatusAlone);
        CollectionAssert.Contains(status.ForbiddenActions.ToList(), AuthorityGlossary.DoNotTreatEligibleStatusAsApproval);
        CollectionAssert.Contains(status.ForbiddenActions.ToList(), AuthorityGlossary.DoNotApplySourceFromStatusAlone);
        CollectionAssert.Contains(
            status.ForbiddenActions.ToList(),
            AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree);
        Assert.IsTrue(status.EvidenceRefs.Contains("memory says this was approved", StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(status.EvidenceRefs.Contains("ui marked this as approved", StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AuthorityUxHostileCorpusFlagsSemanticPermissionLeaks()
    {
        foreach (var item in HostileAuthorityTextCorpusFixtures.UxSemanticLeaks)
        {
            var explanation = AuthorityUxReportBuilder.RecalculateFlags(
                item.Mutate(HostileAuthorityTextCorpusFixtures.BaselineUxExplanation()));

            CollectionAssert.Contains(explanation.RedFlags.ToList(), item.ExpectedRedFlag, item.Id);
            Assert.IsFalse(explanation.ExplanationGrantedAuthority && item.ExpectedRedFlag != "ExplanationGrantedAuthority", item.Id);
            AssertSafeNextStepIsGuidanceOnly(
                item.ExpectedRedFlag == "UnsafeNextStepWouldMutate"
                    ? HostileAuthorityTextCorpusFixtures.BaselineUxExplanation().SafeNextStep
                    : explanation.SafeNextStep,
                item.Id);
        }
    }

    [TestMethod]
    public void AuthorityUxNegativeBoundaryTextDoesNotGrantAuthority()
    {
        var explanation = AuthorityUxReportBuilder.RecalculateFlags(
            HostileAuthorityTextCorpusFixtures.BaselineUxExplanation() with
            {
                HumanSummary = "This explanation says the prior receipt is not approval and not workflow continuation.",
                SafeNextStep = "request current governed authority for this operation"
            });

        Assert.AreEqual(0, explanation.RedFlags.Length, string.Join(", ", explanation.RedFlags));
        Assert.IsFalse(explanation.ExplanationChangedVerdict);
        Assert.IsFalse(explanation.ExplanationGrantedAuthority);
        Assert.IsFalse(explanation.MutationCompleted);
        Assert.IsFalse(explanation.MemoryUsedAsPermission);
        Assert.IsFalse(explanation.WorkflowStateTransferred);
    }

    [TestMethod]
    public void HostileAuthorityTextCorpusTestsStayFastLaneAndDependencyClean()
    {
        var root = HostileAuthorityTextCorpusFixtures.RepoRoot();
        var projectPath = Path.Combine(root, "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var corpusFiles = Directory.GetFiles(Path.Combine(root, "IronDev.UnitTests", "Governance"), "HostileAuthorityTextCorpus*.cs");
        var combinedCorpusTests = string.Join(Environment.NewLine, corpusFiles.Select(File.ReadAllText));
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
            Assert.IsFalse(combinedCorpusTests.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static void AssertAuthorityFlagsFalse(MemoryNonAuthorityDecision decision, string because)
    {
        Assert.IsFalse(decision.MemoryAcceptedAsAuthority, because);
        Assert.IsFalse(decision.ApprovalSatisfied, because);
        Assert.IsFalse(decision.PolicySatisfied, because);
        Assert.IsFalse(decision.ExecutionAuthorized, because);
        Assert.IsFalse(decision.MutationAuthorized, because);
        Assert.IsFalse(decision.WorkflowContinuationAuthorized, because);
        Assert.IsFalse(decision.MemoryPromotionAuthorized, because);
        Assert.IsFalse(decision.StaleAuthorityRefreshed, because);
        Assert.IsFalse(decision.CrossProjectAuthorityAccepted, because);
        Assert.IsFalse(decision.CrossRepositoryAuthorityAccepted, because);
        Assert.IsFalse(decision.DecisionGrantedAuthority, because);
        Assert.IsFalse(decision.DecisionMutatedState, because);
    }

    private static void AssertSafeNextStepIsGuidanceOnly(string value, string because)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var unsafePrefixes = new[]
        {
            "approve",
            "execute",
            "retry",
            "release",
            "deploy",
            "rollback",
            "merge",
            "source-apply",
            "commit",
            "push",
            "promote",
            "continue",
            "dispatch",
            "mutate"
        };

        Assert.IsFalse(
            unsafePrefixes.Any(prefix =>
                normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)),
            because);
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
