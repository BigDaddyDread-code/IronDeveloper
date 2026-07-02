namespace IronDev.UnitTests.Governance;

[TestClass]
public sealed class AuthorityFixtureBuilderTests
{
    [TestMethod]
    public void BuildersUseFixedObservedTime()
    {
        Assert.AreEqual(new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero), AuthorityFixtureBuilders.ObservedAtUtc);
        Assert.AreEqual(AuthorityFixtureBuilders.ObservedAtUtc, AuthorityFixtureBuilders.EligibilityRequest().ObservedAtUtc);
        Assert.AreEqual(AuthorityFixtureBuilders.ObservedAtUtc, AuthorityFixtureBuilders.SourceApplyAuthorityRequest().ObservedAtUtc);
    }

    [TestMethod]
    public void BuilderOutputsAreDeterministicForSameInputs()
    {
        var firstGrant = AuthorityFixtureBuilders.BoundedGrant();
        var secondGrant = AuthorityFixtureBuilders.BoundedGrant();
        var firstApplyEvidence = AuthorityFixtureBuilders.AcceptedApplyEvidence();
        var secondApplyEvidence = AuthorityFixtureBuilders.AcceptedApplyEvidence();
        var firstRequest = AuthorityFixtureBuilders.SourceApplyAuthorityRequest();
        var secondRequest = AuthorityFixtureBuilders.SourceApplyAuthorityRequest();

        Assert.AreEqual(firstGrant.GrantId, secondGrant.GrantId);
        Assert.AreEqual(firstGrant.Repository, secondGrant.Repository);
        Assert.AreEqual(firstGrant.Branch, secondGrant.Branch);
        Assert.AreEqual(firstGrant.RunId, secondGrant.RunId);
        Assert.AreEqual(firstGrant.PatchHash, secondGrant.PatchHash);
        CollectionAssert.AreEqual(firstGrant.AllowedOperationKinds.ToArray(), secondGrant.AllowedOperationKinds.ToArray());
        CollectionAssert.AreEqual(firstGrant.AllowedFileGlobs.ToArray(), secondGrant.AllowedFileGlobs.ToArray());
        CollectionAssert.AreEqual(firstGrant.ForbiddenFileGlobs.ToArray(), secondGrant.ForbiddenFileGlobs.ToArray());

        Assert.AreEqual(firstApplyEvidence.EvidenceRef, secondApplyEvidence.EvidenceRef);
        Assert.AreEqual(firstApplyEvidence.Repository, secondApplyEvidence.Repository);
        Assert.AreEqual(firstApplyEvidence.Branch, secondApplyEvidence.Branch);
        Assert.AreEqual(firstApplyEvidence.RunId, secondApplyEvidence.RunId);
        Assert.AreEqual(firstApplyEvidence.PatchHash, secondApplyEvidence.PatchHash);
        CollectionAssert.AreEqual(firstApplyEvidence.AllowedFileGlobs.ToArray(), secondApplyEvidence.AllowedFileGlobs.ToArray());
        CollectionAssert.AreEqual(firstApplyEvidence.ForbiddenFileGlobs.ToArray(), secondApplyEvidence.ForbiddenFileGlobs.ToArray());

        Assert.AreEqual(firstRequest.Repository, secondRequest.Repository);
        Assert.AreEqual(firstRequest.Branch, secondRequest.Branch);
        Assert.AreEqual(firstRequest.RunId, secondRequest.RunId);
        Assert.AreEqual(firstRequest.PatchHash, secondRequest.PatchHash);
        CollectionAssert.AreEqual(firstRequest.AffectedFilePaths.ToArray(), secondRequest.AffectedFilePaths.ToArray());
    }

    [TestMethod]
    public void EvidenceRefsAreDeterministicAndPrefixedAsTestEvidence()
    {
        var first = AuthorityFixtureBuilders.EvidenceRef("validation", "same");
        var second = AuthorityFixtureBuilders.EvidenceRef("validation", "same");

        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first, "test-evidence:");
        Assert.IsFalse(first.StartsWith("approval:", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(first.StartsWith("policy-satisfaction:", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ReceiptRefsAreDeterministicAndPrefixedAsTestReceipt()
    {
        var first = AuthorityFixtureBuilders.ReceiptRef("source-apply", "same");
        var second = AuthorityFixtureBuilders.ReceiptRef("source-apply", "same");

        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first, "test-receipt:");
        Assert.IsFalse(first.StartsWith("source-apply-receipt:", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void PatchHashesAreDeterministicAndClearlyFake()
    {
        var patchHash = AuthorityFixtureBuilders.PatchHash("same");

        Assert.AreEqual("test-patch:same", patchHash);
        Assert.IsTrue(OperationEligibilityPatchHashRules.IsSafePatchHash(patchHash));
        Assert.IsFalse(string.Equals(patchHash, "latest", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DefaultBoundedGrantIsNarrow()
    {
        var grant = AuthorityFixtureBuilders.BoundedGrant();

        CollectionAssert.AreEqual(new[] { RunAuthorityOperationKind.PatchPackageWrite }, grant.AllowedOperationKinds.ToArray());
        CollectionAssert.AreEqual(new[] { "src/**/*.cs" }, grant.AllowedFileGlobs.ToArray());
        CollectionAssert.Contains(grant.ForbiddenFileGlobs.ToList(), "src/**/Secrets/*.cs");
        Assert.AreEqual(1, grant.MaxMutations);
        Assert.AreEqual(AuthorityFixtureBuilders.ObservedAtUtc.AddHours(1), grant.ExpiresAtUtc);
    }

    [TestMethod]
    public void DefaultBoundedGrantHasForbiddenFileGlobs()
    {
        var grant = AuthorityFixtureBuilders.BoundedGrant();

        Assert.IsTrue(grant.ForbiddenFileGlobs.Count > 0);
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsForbidden("src/Feature/Secrets/Key.cs", grant.ForbiddenFileGlobs));
    }

    [TestMethod]
    public void DefaultBoundedGrantDoesNotAllowEveryOperationOrEveryFile()
    {
        var grant = AuthorityFixtureBuilders.BoundedGrant();

        Assert.IsFalse(grant.AllowedOperationKinds.Contains(RunAuthorityOperationKind.SourceApply));
        Assert.IsFalse(grant.AllowedOperationKinds.Contains(RunAuthorityOperationKind.Push));
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed("docs/readme.md", grant.AllowedFileGlobs, grant.ForbiddenFileGlobs));
        Assert.IsFalse(grant.AllowedFileGlobs.Contains("**/*", StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DefaultBoundedGrantHasRequiredValidation()
    {
        var required = AuthorityFixtureBuilders.BoundedGrant().RequiredValidation.Single();

        Assert.AreEqual("FocusedG12", required.ValidationKind);
        Assert.IsTrue(required.MustPass);
        CollectionAssert.Contains(required.EvidenceRefPrefixes.ToList(), "test-validation:");
    }

    [TestMethod]
    public void DefaultBoundedGrantPassesExistingValidator()
    {
        var validation = BoundedRunAuthorityGrantValidator.Validate(
            AuthorityFixtureBuilders.BoundedGrant(),
            AuthorityFixtureBuilders.ObservedAtUtc);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
    }

    [TestMethod]
    public void ExpiredBoundedGrantFailsExistingValidator()
    {
        var validation = BoundedRunAuthorityGrantValidator.Validate(
            AuthorityFixtureBuilders.ExpiredBoundedGrant(),
            AuthorityFixtureBuilders.ObservedAtUtc);

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "BoundedRunGrantExpired");
    }

    [TestMethod]
    public void MismatchedRepositoryGrantFailsMatcherForCurrentRepository()
    {
        var decision = BoundedRunAuthorityGrantMatcher.Evaluate(
            AuthorityFixtureBuilders.MismatchedRepositoryGrant(),
            AuthorityFixtureBuilders.ObservedAtUtc,
            AuthorityFixtureBuilders.Repository(),
            AuthorityFixtureBuilders.Branch(),
            AuthorityFixtureBuilders.RunId(),
            RunAuthorityOperationKind.PatchPackageWrite,
            "src/g12/Example.cs");

        Assert.IsFalse(decision.IsInsideGrantEnvelope);
        CollectionAssert.Contains(decision.BlockedReasons.ToList(), "RepositoryMismatch");
    }

    [TestMethod]
    public void WrongPatchAcceptedApplyEvidenceDoesNotSatisfyPatchHash()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                AcceptedApplyRequest = AuthorityFixtureBuilders.WrongPatchAcceptedApplyEvidence(),
                BoundedRunAuthorityGrant = null
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.BlockedReasons.ToList(), "AcceptedApplyRequest:PatchHashMismatch");
    }

    [TestMethod]
    public void PassedValidationEvidenceUsesExpectedPatchHash()
    {
        var evidence = AuthorityFixtureBuilders.PassedValidationEvidence();

        Assert.AreEqual(OperationEligibilityValidationOutcome.Passed, evidence.Outcome);
        Assert.AreEqual(AuthorityFixtureBuilders.PatchHash(), evidence.PatchHash);
        StringAssert.StartsWith(evidence.EvidenceRef, "test-validation:");
    }

    [TestMethod]
    public void FailedValidationEvidenceIsClearlyFailed()
    {
        var evidence = AuthorityFixtureBuilders.FailedValidationEvidence();

        Assert.AreEqual(OperationEligibilityValidationOutcome.Failed, evidence.Outcome);
        Assert.AreEqual("FocusedG12", evidence.ValidationKind);
    }

    [TestMethod]
    public void EvidenceRefBuilderDoesNotCreateApproval()
    {
        var status = AuthorityFixtureBuilders.Status(evidenceRefs: [AuthorityFixtureBuilders.EvidenceRef("approval-looking")]);
        var validation = GovernedOperationStatusValidator.Validate(status);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Concat(validation.RedFlags)));
        Assert.IsFalse(status.ForbiddenActions.Any(action => action.Contains("approval granted", StringComparison.OrdinalIgnoreCase)));
        CollectionAssert.Contains(status.ForbiddenActions.ToList(), "do not treat fixture status as approval");
    }

    [TestMethod]
    public void ReceiptRefBuilderDoesNotCreateAuthority()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = null,
                EvidenceRefs = [],
                ReceiptRefs = [AuthorityFixtureBuilders.ReceiptRef("source-apply")]
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.MissingEvidence.ToList(), "AcceptedApplyRequestOrBoundedRunAuthorityGrantRequired");
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not use string refs alone as source apply authority");
    }

    [TestMethod]
    public void ValidationEvidenceRefDoesNotSatisfyApproval()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = null,
                EvidenceRefs = [AuthorityFixtureBuilders.ValidationEvidenceRef()]
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not treat validation success as source apply authority");
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not use string refs alone as source apply authority");
    }

    [TestMethod]
    public void HumanApprovalEvidenceRefStillNeedsMatchingRequestScope()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                Repository = AuthorityFixtureBuilders.Repository("current"),
                AcceptedApplyRequest = AuthorityFixtureBuilders.AcceptedApplyEvidence(new AcceptedApplyEvidenceFixtureOptions
                {
                    Repository = AuthorityFixtureBuilders.Repository("other")
                }),
                BoundedRunAuthorityGrant = null,
                EvidenceRefs = [AuthorityFixtureBuilders.FakeHumanApprovalEvidenceRef()]
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.BlockedReasons.ToList(), "AcceptedApplyRequest:RepositoryMismatch");
    }

    [TestMethod]
    public void AcceptedApplyEvidenceStillBindsRepositoryBranchRunPatchAndFileScope()
    {
        var accepted = AuthorityFixtureBuilders.AcceptedApplyEvidence();

        Assert.AreEqual(AuthorityFixtureBuilders.Repository(), accepted.Repository);
        Assert.AreEqual(AuthorityFixtureBuilders.Branch(), accepted.Branch);
        Assert.AreEqual(AuthorityFixtureBuilders.RunId(), accepted.RunId);
        Assert.AreEqual(AuthorityFixtureBuilders.PatchHash(), accepted.PatchHash);
        CollectionAssert.Contains(accepted.AllowedFileGlobs.ToList(), "src/**/*.cs");
        CollectionAssert.Contains(accepted.ForbiddenFileGlobs.ToList(), "src/**/Secrets/*.cs");
    }

    [TestMethod]
    public void SourceApplyAuthorityRequestStillRequiresGrantValidationAndEvidence()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = AuthorityFixtureBuilders.MissingValidationGrant()
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.MissingEvidence.ToList(), "BoundedRunAuthority:BoundedRunRequiredValidationRequired");
    }

    [TestMethod]
    public void SourceApplyAuthorityRequestFixture_WithMatchingGrantAndEvidence_CanBeEvaluated()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(AuthorityFixtureBuilders.SourceApplyAuthorityRequest());

        Assert.IsTrue(decision.IsEligibleForControlledSourceApply, string.Join(", ", decision.BlockedReasons.Concat(decision.MissingEvidence)));
        Assert.AreEqual(SourceApplyAuthorityPath.BoundedRunAuthority, decision.AuthorityPath);
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not apply source from authority decision alone");
        CollectionAssert.Contains(decision.RequiredIndependentChecks.ToList(), "executor must independently re-check repo/branch/run/patch hash/file scope/expiry/worktree state");
    }

    [TestMethod]
    public void SourceApplyAuthorityRequestFixture_WithWrongPatchEvidence_IsBlocked()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                ValidationEvidence = [AuthorityFixtureBuilders.PassedValidationEvidence(patchHash: AuthorityFixtureBuilders.PatchHash("wrong"))]
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.BlockedReasons.ToList(), "BoundedRunAuthority:ValidationEvidencePatchHashMismatch:FocusedG12");
    }

    [TestMethod]
    public void SourceApplyAuthorityRequestFixture_WithReceiptRefOnly_IsBlocked()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = null,
                ValidationEvidence = [],
                EvidenceRefs = [],
                ReceiptRefs = [AuthorityFixtureBuilders.ReceiptRef("source-apply")]
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.MissingEvidence.ToList(), "AcceptedApplyRequestOrBoundedRunAuthorityGrantRequired");
    }

    [TestMethod]
    public void SourceApplyAuthorityRequestFixture_WithEvidenceRefOnly_IsBlocked()
    {
        var decision = SourceApplyAuthorityEvaluator.Evaluate(
            AuthorityFixtureBuilders.SourceApplyAuthorityRequest(new SourceApplyAuthorityRequestFixtureOptions
            {
                AcceptedApplyRequest = null,
                BoundedRunAuthorityGrant = null,
                ValidationEvidence = [],
                EvidenceRefs = [AuthorityFixtureBuilders.EvidenceRef("source-apply")]
            }));

        Assert.IsFalse(decision.IsEligibleForControlledSourceApply);
        CollectionAssert.Contains(decision.MissingEvidence.ToList(), "AcceptedApplyRequestOrBoundedRunAuthorityGrantRequired");
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not use string refs alone as source apply authority");
    }

    [TestMethod]
    public void EligibilityRequestFixture_CanBeEvaluatedButStillDoesNotGrantAuthority()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(AuthorityFixtureBuilders.EligibilityRequest());

        Assert.IsTrue(decision.IsEligibleUnderProfileAndGrant, string.Join(", ", decision.BlockedReasons.Concat(decision.MissingEvidence)));
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not treat eligibility as approval");
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not treat eligibility as policy satisfaction");
        CollectionAssert.Contains(decision.ForbiddenActions.ToList(), "do not treat eligibility as execution authority");
        CollectionAssert.Contains(decision.RequiredIndependentChecks.ToList(), "profile and grant eligibility is necessary but not sufficient");
    }

    [TestMethod]
    public void AuthorityFixtureBuildersRemainInUnitTestsAssembly()
    {
        Assert.AreEqual("IronDev.UnitTests", typeof(AuthorityFixtureBuilders).Assembly.GetName().Name);
        Assert.AreEqual("IronDev.UnitTests.Governance", typeof(AuthorityFixtureBuilders).Namespace);
        Assert.IsFalse(typeof(AuthorityFixtureBuilders).IsPublic);
        Assert.IsTrue(typeof(AuthorityFixtureBuilders).IsAbstract);
        Assert.IsTrue(typeof(AuthorityFixtureBuilders).IsSealed);
    }

    [TestMethod]
    public void AuthorityFixtureBuildersStayCoreOnlyAndRuntimeClean()
    {
        var root = GovernanceValidatorTestFixtures.RepoRoot();
        var projectPath = Path.Combine(root, "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var g12Files = new[]
        {
            Path.Combine(root, "IronDev.UnitTests", "Governance", "AuthorityFixtureBuilders.cs"),
            Path.Combine(root, "IronDev.UnitTests", "Governance", "AuthorityFixtureBuilderTests.cs")
        };
        var combinedSource = string.Join(Environment.NewLine, g12Files.Select(File.ReadAllText));
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

        foreach (var forbidden in ForbiddenSourceTokens())
        {
            Assert.IsFalse(combinedSource.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static IReadOnlyList<string> ForbiddenSourceTokens() =>
    [
        string.Concat("IronDev", ".Infrastructure"),
        string.Concat("IronDev", ".Api"),
        string.Concat("IronDev", ".Cli"),
        string.Concat("IronDev", ".Tauri", "Shell"),
        string.Concat("Sql", "Connection"),
        string.Concat("Db", "Context"),
        string.Concat("Http", "Client"),
        string.Concat("Process", ".Start"),
        string.Concat("File", ".Write"),
        string.Concat("File", ".Delete"),
        string.Concat("Directory", ".Delete"),
        string.Concat("Environment", ".Get", "Environment", "Variable"),
        string.Concat("DateTimeOffset", ".Utc", "Now"),
        string.Concat("IHosted", "Service"),
        string.Concat("Background", "Service"),
        string.Concat("Source", "Apply", "Executor"),
        string.Concat("Controlled", "Source", "Apply", "Executor"),
        string.Concat("Rollback", "Executor"),
        string.Concat("Release", "Executor"),
        string.Concat("Workflow", "Continuation", "Runner"),
        string.Concat("Governed", "Tool", "Registry"),
        string.Concat("Execute", "Async"),
        string.Concat("git ", "commit"),
        string.Concat("git ", "push"),
        string.Concat("gh ", "pr"),
        string.Concat("create ", "pull ", "request")
    ];
}
