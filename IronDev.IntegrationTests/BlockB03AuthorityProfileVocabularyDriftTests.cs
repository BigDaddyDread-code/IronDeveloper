using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB03AuthorityProfileVocabularyDriftTests
{
    private static readonly RunAuthorityOperationKind[] ExpectedProposalOnlyAllowedOperations =
    [
        RunAuthorityOperationKind.RepoInspect,
        RunAuthorityOperationKind.TaskInterpretation,
        RunAuthorityOperationKind.DisposableWorkspaceCreate,
        RunAuthorityOperationKind.DisposableWorkspaceModify,
        RunAuthorityOperationKind.DisposableWorkspaceValidate,
        RunAuthorityOperationKind.PatchProposal,
        RunAuthorityOperationKind.PatchPackageWrite,
        RunAuthorityOperationKind.ValidationResultPackageWrite,
        RunAuthorityOperationKind.GovernedStatusInspect
    ];

    private static readonly RunAuthorityOperationKind[] ExpectedProposalOnlyForbiddenOperations =
    [
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.ApprovalRequestCreate,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.PackagePublication,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    private static readonly RunAuthorityOperationKind[] ExpectedAskBeforeMutationAllowedOperations =
    [
        .. ExpectedProposalOnlyAllowedOperations,
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation
    ];

    private static readonly RunAuthorityOperationKind[] ExpectedAskBeforeMutationForbiddenOperations =
    [
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.ApprovalRequestCreate,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.PackagePublication,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    private static readonly RunAuthorityOperationKind[] ExpectedBoundedRunAuthorityAllowedOperations =
    [
        .. ExpectedProposalOnlyAllowedOperations,
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest
    ];

    private static readonly RunAuthorityOperationKind[] ExpectedBoundedRunAuthorityForbiddenOperations =
    [
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.ApprovalRequestCreate,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.PackagePublication,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    [TestMethod]
    public void BlockB03_AuthorityProfileKind_IsOnlyCanonicalProfileKind()
    {
        var coreTypes = typeof(AuthorityProfileKind).Assembly.GetTypes();
        var forbiddenEnumNames = new[]
        {
            "RunAuthorityProfileKind",
            "ProfileKind",
            "RunnerAuthorityProfileKind",
            "LegacyAuthorityProfileKind",
            "AuthorityProfileKindV2"
        };
        var authorityProfileKindEnums = coreTypes
            .Where(type => type.IsEnum && type.Name.EndsWith("AuthorityProfileKind", StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToArray();

        Assert.IsTrue(typeof(AuthorityProfileKind).IsEnum);
        CollectionAssert.AreEquivalent(new[] { nameof(AuthorityProfileKind) }, authorityProfileKindEnums);

        foreach (var enumName in forbiddenEnumNames)
        {
            Assert.IsFalse(
                coreTypes.Any(type => type.IsEnum && string.Equals(type.Name, enumName, StringComparison.Ordinal)),
                enumName);
        }
    }

    [TestMethod]
    public void BlockB03_AuthorityProfileKind_MembersAndNumericValuesRemainStable()
    {
        var expectedNames = new[]
        {
            nameof(AuthorityProfileKind.Unknown),
            nameof(AuthorityProfileKind.ProposalOnly),
            nameof(AuthorityProfileKind.AskBeforeMutation),
            nameof(AuthorityProfileKind.BoundedRunAuthority)
        };

        CollectionAssert.AreEqual(expectedNames, Enum.GetNames<AuthorityProfileKind>());
        Assert.AreEqual(0, (int)AuthorityProfileKind.Unknown);
        Assert.AreEqual(1, (int)AuthorityProfileKind.ProposalOnly);
        Assert.AreEqual(2, (int)AuthorityProfileKind.AskBeforeMutation);
        Assert.AreEqual(3, (int)AuthorityProfileKind.BoundedRunAuthority);
    }

    [TestMethod]
    public void BlockB03_RunAuthorityContracts_UseCanonicalAuthorityProfileKind()
    {
        Assert.AreEqual(typeof(AuthorityProfileKind), PropertyType<RunAuthorityProfile>(nameof(RunAuthorityProfile.Kind)));
        Assert.AreEqual(typeof(AuthorityProfileKind), PropertyType<RunAuthorityDecision>(nameof(RunAuthorityDecision.ProfileKind)));
        Assert.AreEqual(typeof(AuthorityProfileKind), PropertyType<AuthorityProfileStatusRequest>(nameof(AuthorityProfileStatusRequest.ProfileKind)));

        var contracts = new[] { typeof(RunAuthorityProfile), typeof(RunAuthorityDecision), typeof(AuthorityProfileStatusRequest) };
        var propertyNames = contracts
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToArray();
        var forbiddenPropertyNames = new[]
        {
            "RunAuthorityProfileKind",
            "LegacyProfileKind",
            "WireProfileKind",
            "ProfileKindText",
            "ProfileKindName",
            "IsAuthorized",
            "Approved",
            "Granted",
            "PolicySatisfied",
            "CanExecute"
        };

        AssertDoesNotContainExact(propertyNames, forbiddenPropertyNames);
    }

    [TestMethod]
    public void BlockB03_CoreGovernanceSource_DoesNotReintroduceLegacyVocabulary()
    {
        var source = ReadAllGovernanceSource();
        var forbiddenMarkers = new[]
        {
            "RunAuthorityProfileKind",
            "RunAuthorityProfileKindMapper",
            "AuthorityProfileKindMapper",
            "LegacyAuthorityProfileKind",
            "AuthorityProfileKindBridge",
            "TemporaryAuthorityProfileKindBridge",
            "ProfileKindBridge",
            "ProfileKindAdapter",
            "ProfileKindTranslator",
            "AuthorityProfileKindV2",
            "RunnerAuthorityProfileKind"
        };

        AssertDoesNotContainAny(source, forbiddenMarkers);
    }

    [TestMethod]
    public void BlockB03_NoProfileKindMapperBridgeOrTranslatorExists()
    {
        var allowedTypeNames = new[] { nameof(AuthorityProfileStatusMapper) };
        var suspiciousWords = new[] { "Mapper", "Bridge", "Translator", "Adapter" };
        var profileWords = new[] { "ProfileKind", "AuthorityProfile" };
        var suspiciousTypes = typeof(AuthorityProfileKind).Assembly
            .GetTypes()
            .Where(type => !allowedTypeNames.Contains(type.Name, StringComparer.Ordinal))
            .Where(type =>
                suspiciousWords.Any(word => type.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) &&
                profileWords.Any(word => type.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(type => type.FullName)
            .ToArray();

        Assert.IsEmpty(suspiciousTypes, string.Join(", ", suspiciousTypes));
    }

    [TestMethod]
    public void BlockB03_ProposalOnly_RunProfileCeilingDoesNotDrift()
    {
        CollectionAssert.AreEquivalent(
            ExpectedProposalOnlyAllowedOperations,
            RunAuthorityProfileValidator.ProposalOnlyAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            ExpectedProposalOnlyForbiddenOperations,
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.ToArray());
        Assert.AreEqual(
            ExpectedProposalOnlyAllowedOperations.Length,
            RunAuthorityProfileValidator.ProposalOnlyAllowedOperations.Count);
        Assert.AreEqual(
            ExpectedProposalOnlyForbiddenOperations.Length,
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.Count);
    }

    [TestMethod]
    public void BlockB03_AskBeforeMutation_IsSupportedOnlyBySourceApplyLaneShape()
    {
        var profile = AskBeforeMutationProfile();
        var validation = RunAuthorityProfileValidator.Validate(profile);
        var sourceApplyDecision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.SourceApply);
        var commitDecision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.Commit);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        CollectionAssert.AreEquivalent(
            ExpectedAskBeforeMutationAllowedOperations,
            RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            ExpectedAskBeforeMutationForbiddenOperations,
            RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations.ToArray());
        Assert.IsTrue(sourceApplyDecision.IsAllowedByProfile, string.Join(", ", sourceApplyDecision.BlockedReasons));
        Assert.IsFalse(commitDecision.IsAllowedByProfile);
        AssertContains(commitDecision.BlockedReasons, "AskBeforeMutation does not allow Commit.");

        var widenedProfile = profile with
        {
            AllowedOperations = [.. ExpectedAskBeforeMutationAllowedOperations, RunAuthorityOperationKind.Push]
        };
        var widenedValidation = RunAuthorityProfileValidator.Validate(widenedProfile);

        Assert.IsFalse(widenedValidation.IsValid);
        AssertContains(widenedValidation.Issues, "AskBeforeMutationCannotAllowDangerousOperation:Push");
    }

    [TestMethod]
    public void BlockB03_BoundedRunAuthority_IsSupportedOnlyByBoundedLaneShape()
    {
        var profile = BoundedRunAuthorityProfile();
        var validation = RunAuthorityProfileValidator.Validate(profile);
        var commitDecision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.Commit);
        var releaseDecision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.Release);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        CollectionAssert.AreEquivalent(
            ExpectedBoundedRunAuthorityAllowedOperations,
            RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            ExpectedBoundedRunAuthorityForbiddenOperations,
            RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations.ToArray());
        Assert.IsTrue(commitDecision.IsAllowedByProfile, string.Join(", ", commitDecision.BlockedReasons));
        Assert.IsFalse(releaseDecision.IsAllowedByProfile);
        AssertContains(releaseDecision.BlockedReasons, "BoundedRunAuthority does not allow Release.");

        var widenedProfile = profile with
        {
            AllowedOperations = [.. ExpectedBoundedRunAuthorityAllowedOperations, RunAuthorityOperationKind.ReadyForReview]
        };
        var widenedValidation = RunAuthorityProfileValidator.Validate(widenedProfile);

        Assert.IsFalse(widenedValidation.IsValid);
        AssertContains(widenedValidation.Issues, "BoundedRunAuthorityCannotAllowDangerousOperation:ReadyForReview");
    }

    [TestMethod]
    public void BlockB03_UnknownAndUndefinedProfileKinds_FailClosed()
    {
        foreach (var kind in new[] { AuthorityProfileKind.Unknown, (AuthorityProfileKind)999 })
        {
            var profile = ProposalOnlyProfile() with { Kind = kind };
            var validation = RunAuthorityProfileValidator.Validate(profile);
            var decision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.PatchPackageWrite);

            Assert.IsFalse(validation.IsValid, kind.ToString());
            AssertContains(validation.Issues, "AuthorityProfileKindKnownRequired");
            Assert.IsFalse(decision.IsAllowedByProfile, kind.ToString());
            Assert.AreNotEqual(AuthorityProfileKind.ProposalOnly, decision.ProfileKind);
            AssertContains(decision.BlockedReasons, "RunAuthorityProfileInvalid");
        }
    }

    [TestMethod]
    public void BlockB03_ProfileAllowance_WordingDoesNotBecomeAuthorityLanguage()
    {
        var decision = RunAuthorityProfileEvaluator.Evaluate(ProposalOnlyProfile(), RunAuthorityOperationKind.PatchPackageWrite);

        Assert.IsTrue(decision.IsAllowedByProfile, string.Join(", ", decision.BlockedReasons));
        AssertContains(decision.RequiredIndependentChecks, "operation-specific validation still required");
        AssertContains(decision.RequiredIndependentChecks, "profile allowance is necessary but not sufficient");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as approval");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as policy satisfaction");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as execution authority");
        AssertContains(decision.ForbiddenActions, "do not mutate durable source from profile allowance");

        var propertyNames = typeof(RunAuthorityDecision)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, nameof(RunAuthorityDecision.IsAllowedByProfile), StringComparison.Ordinal))
            .ToArray();
        var forbiddenNames = new[]
        {
            "IsAuthorized",
            "Authorized",
            "Approved",
            "Granted",
            "CanExecute",
            "PolicySatisfied",
            "HasAuthority",
            "AuthorityGranted"
        };

        AssertDoesNotContainAny(propertyNames, forbiddenNames);
    }

    [TestMethod]
    public void BlockB03_RunProfileFiles_DoNotReferenceMutationOrExecutorSurfaces()
    {
        var source = ReadRunProfileSource()
            .Replace(nameof(RunAuthorityProfile.CanCreateApprovalRequest), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(RunAuthorityProfile.CanSatisfyPolicy), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(RunAuthorityProfile.CanPromoteMemory), string.Empty, StringComparison.Ordinal)
            .Replace(nameof(RunAuthorityProfile.CanContinueWorkflow), string.Empty, StringComparison.Ordinal);
        var forbiddenMarkers = new[]
        {
            "ProcessStartInfo",
            "Process.Start",
            "File.Write",
            "Directory.CreateDirectory",
            "git apply",
            "git commit",
            "git push",
            "gh pr create",
            "SourceApplyExecutor",
            "RollbackExecutor",
            "CommitExecutor",
            "PushExecutor",
            "PullRequestExecutor",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "CreateApproval",
            "AcceptApproval",
            "SatisfyPolicy",
            "PromoteMemory",
            "ContinueWorkflow",
            "IWorkflowContinuation",
            "IMemoryPromotion",
            "ISourceApply"
        };

        AssertDoesNotContainAny(source, forbiddenMarkers);
    }

    [TestMethod]
    public void BlockB03_Receipt_RecordsVocabularyDriftBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B03_AUTHORITY_PROFILE_VOCABULARY_DRIFT_TESTS.md"));

        StringAssert.Contains(doc, "AuthorityProfileKind is canonical.");
        StringAssert.Contains(doc, "RunAuthorityProfileKind must not be reintroduced.");
        StringAssert.Contains(doc, "No mapper, bridge, translator, or adapter may hide profile-kind drift.");
        StringAssert.Contains(doc, "ProposalOnly behavior did not widen.");
        StringAssert.Contains(doc, "Profile allowance is not approval, policy satisfaction, execution authority, source mutation authority, or workflow continuation.");
        StringAssert.Contains(doc, "This PR is test-only.");
        StringAssert.Contains(doc, "No executor, mutation, source apply, rollback, commit, push, PR, merge, release, deploy, memory promotion, or workflow continuation path was added.");
        StringAssert.Contains(doc, "If the vocabulary drifts, the gate drifts.");
    }

    private static RunAuthorityProfile ProposalOnlyProfile() =>
        new()
        {
            ProfileId = "proposal-only",
            Kind = AuthorityProfileKind.ProposalOnly,
            AllowedOperations = ExpectedProposalOnlyAllowedOperations,
            ForbiddenOperations = ExpectedProposalOnlyForbiddenOperations,
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

    private static RunAuthorityProfile AskBeforeMutationProfile() =>
        ProposalOnlyProfile() with
        {
            ProfileId = "ask-before-mutation",
            Kind = AuthorityProfileKind.AskBeforeMutation,
            AllowedOperations = ExpectedAskBeforeMutationAllowedOperations,
            ForbiddenOperations = ExpectedAskBeforeMutationForbiddenOperations,
            CanMutateDurableSource = true,
            CanApplyPatch = true
        };

    private static RunAuthorityProfile BoundedRunAuthorityProfile() =>
        AskBeforeMutationProfile() with
        {
            ProfileId = "bounded-run-authority",
            Kind = AuthorityProfileKind.BoundedRunAuthority,
            AllowedOperations = ExpectedBoundedRunAuthorityAllowedOperations,
            ForbiddenOperations = ExpectedBoundedRunAuthorityForbiddenOperations,
            CanExecuteRollback = true,
            CanCommit = true,
            CanPush = true,
            CanCreatePullRequest = true
        };

    private static Type PropertyType<T>(string propertyName) =>
        typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.PropertyType
        ?? throw new MissingMemberException(typeof(T).FullName, propertyName);

    private static string ReadAllGovernanceSource()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance"),
            "*.cs",
            SearchOption.AllDirectories);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static string ReadRunProfileSource()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "RunProfiles"),
            "*.cs",
            SearchOption.TopDirectoryOnly);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static void AssertDoesNotContainAny(IEnumerable<string> values, IReadOnlyCollection<string> forbiddenMarkers)
    {
        var joined = string.Join(Environment.NewLine, values);
        AssertDoesNotContainAny(joined, forbiddenMarkers);
    }

    private static void AssertDoesNotContainExact(IEnumerable<string> values, IReadOnlyCollection<string> forbiddenValues)
    {
        var valueArray = values.ToArray();
        foreach (var forbidden in forbiddenValues)
        {
            Assert.IsFalse(
                valueArray.Any(value => string.Equals(value, forbidden, StringComparison.Ordinal)),
                forbidden);
        }
    }

    private static void AssertDoesNotContainAny(string source, IReadOnlyCollection<string> forbiddenMarkers)
    {
        foreach (var marker in forbiddenMarkers)
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
