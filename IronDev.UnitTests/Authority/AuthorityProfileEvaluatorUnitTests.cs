namespace IronDev.UnitTests.Authority;

[TestClass]
public sealed class AuthorityProfileEvaluatorUnitTests
{
    [TestMethod]
    public void ProposalOnlyAllowsPatchPackageWriteButDecisionRemainsNonAuthority()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request());

        Assert.IsTrue(decision.IsEligibleUnderProfileAndGrant, string.Join("; ", decision.BlockedReasons.Concat(decision.MissingEvidence)));
        Assert.AreEqual(RunAuthorityOperationKind.PatchPackageWrite, decision.OperationKind);
        Assert.AreEqual(0, decision.BlockedReasons.Count);
        Assert.AreEqual(0, decision.MissingEvidence.Count);
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat eligibility as approval");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat eligibility as policy satisfaction");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat eligibility as execution authority");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat eligibility as source apply authority");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not mutate durable source from eligibility");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.RequiredIndependentChecks, "profile and grant eligibility is necessary but not sufficient");
    }

    [TestMethod]
    public void ProposalOnlyBlocksDurableMutationEvenWhenValidationEvidenceExists()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment
        })
        {
            var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
                operationKind: operation,
                profile: AuthorityProfileEvaluatorTestFixtures.ProposalOnlyProfile(),
                grant: AuthorityProfileEvaluatorTestFixtures.Grant(operation)));

            Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant, operation.ToString());
            AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.BlockedReasons, "RunAuthorityProfileCheckFailed", operation.ToString());
            AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not proceed outside run profile", operation.ToString());
        }
    }

    [TestMethod]
    public void AskBeforeMutationSourceApplyRequiresExplicitApprovalLikeEvidence()
    {
        var grant = AuthorityProfileEvaluatorTestFixtures.Grant(
            RunAuthorityOperationKind.SourceApply,
            requiredValidation:
            [
                AuthorityProfileEvaluatorTestFixtures.RequiredValidation("AcceptedApplyApproval", "accepted-apply-approval:"),
                AuthorityProfileEvaluatorTestFixtures.RequiredValidation("PolicySatisfaction", "policy-satisfaction:"),
                AuthorityProfileEvaluatorTestFixtures.RequiredValidation("FreshValidation", "validation-result:")
            ]);

        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            operationKind: RunAuthorityOperationKind.SourceApply,
            profile: AuthorityProfileEvaluatorTestFixtures.AskBeforeMutationProfile(),
            grant: grant,
            validationEvidence: AuthorityProfileEvaluatorTestFixtures.Evidence(
                AuthorityProfileEvaluatorTestFixtures.ValidationEvidence("FreshValidation", "validation-result:g04"),
                AuthorityProfileEvaluatorTestFixtures.ValidationEvidence("PolicySatisfaction", "policy-satisfaction:g04"))));

        AuthorityProfileEvaluatorTestFixtures.AssertMissing(decision, "RequiredValidationEvidenceMissing:AcceptedApplyApproval");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat validation evidence as approval");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat validation evidence as policy satisfaction");
    }

    [TestMethod]
    public void AskBeforeMutationCanReachEligibilityOnlyWithSeparateRequiredEvidence()
    {
        var grant = AuthorityProfileEvaluatorTestFixtures.Grant(
            RunAuthorityOperationKind.SourceApply,
            requiredValidation:
            [
                AuthorityProfileEvaluatorTestFixtures.RequiredValidation("AcceptedApplyApproval", "accepted-apply-approval:"),
                AuthorityProfileEvaluatorTestFixtures.RequiredValidation("PolicySatisfaction", "policy-satisfaction:"),
                AuthorityProfileEvaluatorTestFixtures.RequiredValidation("FreshValidation", "validation-result:")
            ]);

        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            operationKind: RunAuthorityOperationKind.SourceApply,
            profile: AuthorityProfileEvaluatorTestFixtures.AskBeforeMutationProfile(),
            grant: grant,
            validationEvidence: AuthorityProfileEvaluatorTestFixtures.Evidence(
                AuthorityProfileEvaluatorTestFixtures.ValidationEvidence("AcceptedApplyApproval", "accepted-apply-approval:g04"),
                AuthorityProfileEvaluatorTestFixtures.ValidationEvidence("PolicySatisfaction", "policy-satisfaction:g04"),
                AuthorityProfileEvaluatorTestFixtures.ValidationEvidence("FreshValidation", "validation-result:g04"))));

        Assert.IsTrue(decision.IsEligibleUnderProfileAndGrant, string.Join("; ", decision.BlockedReasons.Concat(decision.MissingEvidence)));
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat eligibility as execution authority");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.RequiredIndependentChecks, "operation-specific governance still required");
    }

    [TestMethod]
    public void BoundedRunAuthorityAllowsOnlyGrantedOperationKind()
    {
        var grant = AuthorityProfileEvaluatorTestFixtures.Grant(RunAuthorityOperationKind.SourceApply);

        var sourceApplyDecision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            operationKind: RunAuthorityOperationKind.SourceApply,
            profile: AuthorityProfileEvaluatorTestFixtures.BoundedRunAuthorityProfile(),
            grant: grant));
        var commitDecision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            operationKind: RunAuthorityOperationKind.Commit,
            profile: AuthorityProfileEvaluatorTestFixtures.BoundedRunAuthorityProfile(),
            grant: grant));

        Assert.IsTrue(sourceApplyDecision.IsEligibleUnderProfileAndGrant, string.Join("; ", sourceApplyDecision.BlockedReasons.Concat(sourceApplyDecision.MissingEvidence)));
        Assert.IsFalse(commitDecision.IsEligibleUnderProfileAndGrant);
        AuthorityProfileEvaluatorTestFixtures.AssertContainsPrefix(commitDecision.BlockedReasons, $"AffectedFileRejected:{AuthorityProfileEvaluatorTestFixtures.FilePath}:OperationNotAllowed:Commit");
    }

    [TestMethod]
    public void GrantScopeMismatchBlocksRepositoryBranchAndRunDrift()
    {
        foreach (var request in new[]
        {
            AuthorityProfileEvaluatorTestFixtures.Request() with { Repository = "repo:other" },
            AuthorityProfileEvaluatorTestFixtures.Request() with { Branch = "feature/other" },
            AuthorityProfileEvaluatorTestFixtures.Request() with { RunId = "run:other" }
        })
        {
            var decision = OperationEligibilityEvaluator.Evaluate(request);

            Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
            AuthorityProfileEvaluatorTestFixtures.AssertContainsPrefix(decision.BlockedReasons, $"AffectedFileRejected:{AuthorityProfileEvaluatorTestFixtures.FilePath}:");
        }
    }

    [TestMethod]
    public void PatchBoundOperationRejectsPatchHashMismatch()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request() with
        {
            PatchHash = "sha256:other"
        });

        AuthorityProfileEvaluatorTestFixtures.AssertBlocked(decision, "PatchHashMismatch");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat patch hash match as source apply authority");
    }

    [TestMethod]
    public void ExpiredGrantBlocksOtherwiseValidRequest()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request() with
        {
            Grant = AuthorityProfileEvaluatorTestFixtures.Grant(RunAuthorityOperationKind.PatchPackageWrite) with
            {
                ExpiresAtUtc = AuthorityProfileEvaluatorTestFixtures.ObservedAtUtc
            }
        });

        AuthorityProfileEvaluatorTestFixtures.AssertBlocked(decision, "BoundedRunAuthorityGrantCheckFailed");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.BlockedReasons, "BoundedRunAuthorityGrantCheckFailed:BoundedRunGrantExpired");
    }

    [TestMethod]
    public void MissingValidationEvidenceFailsClosed()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request() with
        {
            ValidationEvidence = []
        });

        AuthorityProfileEvaluatorTestFixtures.AssertMissing(decision, "RequiredValidationEvidenceMissing:FocusedG04");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat validation evidence as approval");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not run validation from eligibility evaluation");
    }

    [TestMethod]
    public void FailedValidationEvidenceFailsClosed()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            validationEvidence: AuthorityProfileEvaluatorTestFixtures.Evidence(
                AuthorityProfileEvaluatorTestFixtures.ValidationEvidence(
                    "FocusedG04",
                    "validation-result:g04",
                    OperationEligibilityValidationOutcome.Failed))));

        AuthorityProfileEvaluatorTestFixtures.AssertBlocked(decision, "RequiredValidationMustPass:FocusedG04:Failed");
    }

    [TestMethod]
    public void MutationBudgetExhaustionFailsClosed()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request() with
        {
            MutationsAlreadyConsumed = 1,
            RequestedMutationCount = 1
        });

        AuthorityProfileEvaluatorTestFixtures.AssertBlocked(decision, "MutationBudgetExceeded");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not treat zero max mutations as unlimited");
    }

    [TestMethod]
    public void UnknownProfileAndUnknownOperationFailClosed()
    {
        var unknownProfileDecision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request() with
        {
            Profile = AuthorityProfileEvaluatorTestFixtures.ProposalOnlyProfile() with
            {
                Kind = AuthorityProfileKind.Unknown
            }
        });
        var unknownOperationDecision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            operationKind: RunAuthorityOperationKind.Unknown));

        AuthorityProfileEvaluatorTestFixtures.AssertBlocked(unknownProfileDecision, "RunAuthorityProfileCheckFailed");
        AuthorityProfileEvaluatorTestFixtures.AssertBlocked(unknownOperationDecision, "OperationKindKnownRequired");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(unknownOperationDecision.ForbiddenActions, "do not treat unknown operation as eligible");
    }

    [TestMethod]
    public void DownstreamOperationAuthorityDoesNotCrossNextBoundary()
    {
        AssertGrantedOperationDoesNotAuthorize(
            grantedOperation: RunAuthorityOperationKind.SourceApply,
            requestedOperation: RunAuthorityOperationKind.Commit,
            expectedBlockedPrefix: $"AffectedFileRejected:{AuthorityProfileEvaluatorTestFixtures.FilePath}:OperationNotAllowed:Commit");
        AssertGrantedOperationDoesNotAuthorize(
            grantedOperation: RunAuthorityOperationKind.Commit,
            requestedOperation: RunAuthorityOperationKind.Push,
            expectedBlockedPrefix: $"AffectedFileRejected:{AuthorityProfileEvaluatorTestFixtures.FilePath}:OperationNotAllowed:Push");
        AssertGrantedOperationDoesNotAuthorize(
            grantedOperation: RunAuthorityOperationKind.Push,
            requestedOperation: RunAuthorityOperationKind.DraftPullRequest,
            expectedBlockedPrefix: $"AffectedFileRejected:{AuthorityProfileEvaluatorTestFixtures.FilePath}:OperationNotAllowed:DraftPullRequest");
        AssertGrantedOperationDoesNotAuthorize(
            grantedOperation: RunAuthorityOperationKind.DraftPullRequest,
            requestedOperation: RunAuthorityOperationKind.ReadyForReview,
            expectedBlockedPrefix: "RunAuthorityProfileCheckFailed");
    }

    [TestMethod]
    public void RollbackPlanEvidenceDoesNotAuthorizeRollbackExecutionWhenGrantStopsBeforeRollback()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            operationKind: RunAuthorityOperationKind.Rollback,
            profile: AuthorityProfileEvaluatorTestFixtures.BoundedRunAuthorityProfile(),
            grant: AuthorityProfileEvaluatorTestFixtures.Grant(RunAuthorityOperationKind.Rollback) with
            {
                StopBeforeOperationKinds = [RunAuthorityOperationKind.Rollback]
            },
            validationEvidence: AuthorityProfileEvaluatorTestFixtures.Evidence(
                AuthorityProfileEvaluatorTestFixtures.ValidationEvidence("FocusedG04", "rollback-plan:g04"))));

        AuthorityProfileEvaluatorTestFixtures.AssertBlocked(decision, "OperationStoppedBefore:Rollback");
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not cross stop-before boundary");
    }

    [TestMethod]
    public void AuthorityEvaluatorUnitTestsStayFastLaneAndDependencyClean()
    {
        var root = AuthorityProfileEvaluatorTestFixtures.RepoRoot();
        var projectPath = Path.Combine(root, "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var authorityTestFiles = Directory.GetFiles(Path.Combine(root, "IronDev.UnitTests", "Authority"), "*.cs");
        var combinedAuthorityTests = string.Join(Environment.NewLine, authorityTestFiles.Select(File.ReadAllText));
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
            Assert.IsFalse(combinedAuthorityTests.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static void AssertGrantedOperationDoesNotAuthorize(
        RunAuthorityOperationKind grantedOperation,
        RunAuthorityOperationKind requestedOperation,
        string expectedBlockedPrefix)
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityProfileEvaluatorTestFixtures.Request(
            operationKind: requestedOperation,
            profile: AuthorityProfileEvaluatorTestFixtures.BoundedRunAuthorityProfile(),
            grant: AuthorityProfileEvaluatorTestFixtures.Grant(grantedOperation)));

        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant, $"{grantedOperation} -> {requestedOperation}");
        AuthorityProfileEvaluatorTestFixtures.AssertContainsPrefix(decision.BlockedReasons, expectedBlockedPrefix);
        AuthorityProfileEvaluatorTestFixtures.AssertContains(decision.ForbiddenActions, "do not proceed outside bounded run grant envelope");
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
        string.Concat("DateTimeOffset", ".Utc", "Now"),
        string.Concat("DateTimeOffset", ".Now")
    ];
}
