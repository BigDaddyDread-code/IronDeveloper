namespace IronDev.UnitTests.Authority;

internal static class AuthorityProfileEvaluatorTestFixtures
{
    internal const string Repository = "repo:g04";
    internal const string Branch = "feature/g04";
    internal const string RunId = "run:g04";
    internal const string PatchHash = "sha256:abcdef1234567890";
    internal const string FilePath = "src/g04/Example.cs";
    internal static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    internal static OperationEligibilityRequest Request(
        RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.PatchPackageWrite,
        RunAuthorityProfile? profile = null,
        BoundedRunAuthorityGrant? grant = null,
        IReadOnlyCollection<OperationEligibilityValidationEvidence>? validationEvidence = null) =>
        new()
        {
            Profile = profile ?? ProposalOnlyProfile(),
            Grant = grant ?? Grant(
                RunAuthorityOperationKind.PatchPackageWrite,
                requiredValidation: [RequiredValidation("FocusedG04", "validation-result:")]),
            OperationKind = operationKind,
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            AffectedFilePaths = [FilePath],
            ObservedAtUtc = ObservedAtUtc,
            PatchHash = PatchHash,
            MutationsAlreadyConsumed = 0,
            RequestedMutationCount = 1,
            ValidationEvidence = validationEvidence ?? [ValidationEvidence("FocusedG04", "validation-result:g04")]
        };

    internal static RunAuthorityProfile ProposalOnlyProfile() =>
        new()
        {
            ProfileId = "profile:g04:proposal-only",
            Kind = AuthorityProfileKind.ProposalOnly,
            AllowedOperations = RunAuthorityProfileValidator.ProposalOnlyAllowedOperations,
            ForbiddenOperations = RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations,
            CanReadRepo = true,
            CanMutateDisposableWorkspace = true,
            CanWriteProposalEvidence = true,
            CanInspectGovernedStatus = true,
            CanMutateDurableSource = false,
            CanApplyPatch = false,
            CanExecuteRollback = false,
            CanCommit = false,
            CanPush = false,
            CanCreatePullRequest = false,
            CanMarkReadyForReview = false,
            CanMerge = false,
            CanRelease = false,
            CanDeploy = false,
            CanCreateApprovalRequest = false,
            CanSatisfyPolicy = false,
            CanPromoteMemory = false,
            CanContinueWorkflow = false,
            CanExecuteProviderMutation = false,
            CanPublishPackage = false
        };

    internal static RunAuthorityProfile AskBeforeMutationProfile() =>
        ProposalOnlyProfile() with
        {
            ProfileId = "profile:g04:ask-before-mutation",
            Kind = AuthorityProfileKind.AskBeforeMutation,
            AllowedOperations = RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations,
            ForbiddenOperations = RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations,
            CanMutateDurableSource = true,
            CanApplyPatch = true
        };

    internal static RunAuthorityProfile BoundedRunAuthorityProfile() =>
        AskBeforeMutationProfile() with
        {
            ProfileId = "profile:g04:bounded-run-authority",
            Kind = AuthorityProfileKind.BoundedRunAuthority,
            AllowedOperations = RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations,
            ForbiddenOperations = RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations,
            CanExecuteRollback = true,
            CanCommit = true,
            CanPush = true,
            CanCreatePullRequest = true
        };

    internal static BoundedRunAuthorityGrant Grant(
        RunAuthorityOperationKind allowedOperation,
        IReadOnlyCollection<BoundedRunAuthorityRequiredValidation>? requiredValidation = null,
        string? patchHash = PatchHash) =>
        new()
        {
            GrantId = $"grant:g04:{allowedOperation}",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            AllowedOperationKinds = [allowedOperation],
            AllowedFileGlobs = ["src/g04/**"],
            ForbiddenFileGlobs = ["src/g04/private/**"],
            PatchHash = patchHash,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            MaxMutations = 1,
            RequiredValidation = requiredValidation ?? [RequiredValidation("FocusedG04", "validation-result:")],
            StopBeforeOperationKinds = RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations,
            GrantedBy = new BoundedRunAuthorityGrantedBy
            {
                PrincipalId = "human:g04",
                PrincipalKind = "Human",
                EvidenceRef = "approval-note:g04"
            },
            HumanReadableIntent = "Evaluate a bounded G04 authority request."
        };

    internal static BoundedRunAuthorityRequiredValidation RequiredValidation(
        string validationKind,
        string evidenceRefPrefix) =>
        new()
        {
            ValidationKind = validationKind,
            MustPass = true,
            EvidenceRefPrefixes = [evidenceRefPrefix]
        };

    internal static OperationEligibilityValidationEvidence ValidationEvidence(
        string validationKind,
        string evidenceRef,
        OperationEligibilityValidationOutcome outcome = OperationEligibilityValidationOutcome.Passed,
        string? patchHash = PatchHash) =>
        new()
        {
            ValidationKind = validationKind,
            Outcome = outcome,
            EvidenceRef = evidenceRef,
            PatchHash = patchHash
        };

    internal static IReadOnlyCollection<OperationEligibilityValidationEvidence> Evidence(
        params OperationEligibilityValidationEvidence[] evidence) =>
        evidence;

    internal static void AssertBlocked(OperationEligibilityDecision decision, string expectedReason)
    {
        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        AssertContains(decision.BlockedReasons, expectedReason);
    }

    internal static void AssertMissing(OperationEligibilityDecision decision, string expectedEvidence)
    {
        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        AssertContains(decision.MissingEvidence, expectedEvidence);
    }

    internal static void AssertContains(IEnumerable<string> values, string expected, string? because = null) =>
        CollectionAssert.Contains(values.ToList(), expected, because ?? expected);

    internal static void AssertContainsPrefix(IEnumerable<string> values, string expectedPrefix, string? because = null) =>
        Assert.IsTrue(
            values.Any(value => value.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)),
            because ?? expectedPrefix);

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
