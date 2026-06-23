using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBSOperationEligibilityEvaluatorTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:abcdef1234567890";

    private static readonly RunAuthorityOperationKind[] ProposalSafeOperations =
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

    [TestMethod]
    public void BlockBS_HappyPath_MarksPatchPackageWriteEligibleUnderProfileAndGrant()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(ValidRequest());

        Assert.IsTrue(decision.IsEligibleUnderProfileAndGrant, string.Join(", ", decision.BlockedReasons.Concat(decision.MissingEvidence)));
        Assert.AreEqual(RunAuthorityOperationKind.PatchPackageWrite, decision.OperationKind);
        Assert.IsEmpty(decision.BlockedReasons.ToArray());
        Assert.IsEmpty(decision.MissingEvidence.ToArray());
        AssertContains(decision.ForbiddenActions, "do not treat eligibility as approval");
        AssertContains(decision.ForbiddenActions, "do not treat eligibility as policy satisfaction");
        AssertContains(decision.ForbiddenActions, "do not treat eligibility as execution authority");
        AssertContains(decision.ForbiddenActions, "do not treat eligibility as source apply authority");
        AssertContains(decision.RequiredIndependentChecks, "operation-specific governance still required");
        AssertContains(decision.RequiredIndependentChecks, "profile and grant eligibility is necessary but not sufficient");
    }

    [TestMethod]
    public void BlockBS_Evaluator_FailsClosedForMissingRequestProfileOrGrant()
    {
        AssertBlocked(OperationEligibilityEvaluator.Evaluate(null), "OperationEligibilityRequestRequired");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Profile = null! }),
            "RunAuthorityProfileCheckFailed");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = null! }),
            "BoundedRunAuthorityGrantCheckFailed");
    }

    [TestMethod]
    public void BlockBS_Evaluator_FailsClosedForInvalidProfileOrProfileDeniedOperation()
    {
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Profile = ValidProfile() with { Kind = AuthorityProfileKind.Unknown } }),
            "RunAuthorityProfileCheckFailed");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Profile = ValidProfile() with { CanCommit = true } }),
            "RunAuthorityProfileCheckFailed");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { OperationKind = RunAuthorityOperationKind.SourceApply }),
            "RunAuthorityProfileCheckFailed:ProposalOnly does not allow SourceApply.");
    }

    [TestMethod]
    public void BlockBS_Evaluator_FailsClosedForInvalidGrantOrEnvelopeMismatch()
    {
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { ExpiresAtUtc = ObservedAtUtc } }),
            "BoundedRunAuthorityGrantCheckFailed");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Repository = "other/repo" }),
            "AffectedFileRejected:Docs/receipts/BS_OPERATION_ELIGIBILITY_EVALUATOR.md:RepositoryMismatch");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Branch = "other-branch" }),
            "AffectedFileRejected:Docs/receipts/BS_OPERATION_ELIGIBILITY_EVALUATOR.md:BranchMismatch");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { RunId = "other-run" }),
            "AffectedFileRejected:Docs/receipts/BS_OPERATION_ELIGIBILITY_EVALUATOR.md:RunIdMismatch");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { OperationKind = RunAuthorityOperationKind.GovernedStatusInspect }),
            "AffectedFileRejected:Docs/receipts/BS_OPERATION_ELIGIBILITY_EVALUATOR.md:OperationNotAllowed:GovernedStatusInspect");
    }

    [TestMethod]
    public void BlockBS_Evaluator_BlocksDangerousOperationKinds()
    {
        var dangerous = new[]
        {
            RunAuthorityOperationKind.Unknown,
            (RunAuthorityOperationKind)999,
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.MemoryPromotion,
            RunAuthorityOperationKind.WorkflowContinuation,
            RunAuthorityOperationKind.PolicySatisfaction,
            RunAuthorityOperationKind.ProviderMutation,
            RunAuthorityOperationKind.DurableSourceMutation,
            RunAuthorityOperationKind.DurableEventWrite
        };

        foreach (var operation in dangerous)
        {
            var decision = OperationEligibilityEvaluator.Evaluate(ValidRequest() with { OperationKind = operation });

            Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant, operation.ToString());
            Assert.IsTrue(decision.BlockedReasons.Count > 0, operation.ToString());
        }
    }

    [TestMethod]
    public void BlockBS_Evaluator_RequiresPatchHashForPatchBoundOperations()
    {
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { PatchHash = null } }),
            "GrantPatchHashRequired");
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { PatchHash = null }),
            "RequestPatchHashRequired");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { PatchHash = "sha256:other" }),
            "PatchHashMismatch");

        foreach (var badHash in new[] { "*", "latest", "current", "approved", "unknown", "has whitespace", " validation-passed" })
        {
            AssertBlocked(
                OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { PatchHash = badHash } }),
                "BoundedRunAuthorityGrantCheckFailed");
            AssertBlocked(
                OperationEligibilityEvaluator.Evaluate(ValidRequest() with { PatchHash = badHash }),
                "RequestPatchHashInvalid");
        }
    }

    [TestMethod]
    public void BlockBS_Evaluator_DoesNotAllowSourceApplyBecausePatchHashMatches()
    {
        var request = ValidRequest() with
        {
            OperationKind = RunAuthorityOperationKind.SourceApply,
            Grant = ValidGrant() with
            {
                AllowedOperationKinds = [RunAuthorityOperationKind.PatchPackageWrite],
                StopBeforeOperationKinds = RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations
            },
            PatchHash = PatchHash
        };

        var decision = OperationEligibilityEvaluator.Evaluate(request);

        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        AssertContains(decision.BlockedReasons, "RunAuthorityProfileCheckFailed:ProposalOnly does not allow SourceApply.");
        AssertContains(decision.BlockedReasons, "OperationStoppedBefore:SourceApply");
        AssertContains(decision.ForbiddenActions, "do not treat patch hash match as source apply authority");
    }

    [TestMethod]
    public void BlockBS_Evaluator_BlocksUnsafeDisallowedOrForbiddenAffectedFiles()
    {
        foreach (var path in new[] { "/rooted/path", "C:\\rooted\\path", "..\\outside", "src/../secret", "https://example.test/file", "~/file" })
        {
            AssertIssueStartsWith(
                OperationEligibilityEvaluator.Evaluate(ValidRequest() with { AffectedFilePaths = [path] }),
                "AffectedFileRejected:");
        }

        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { AffectedFilePaths = ["outside/file.md"] }),
            "AffectedFileRejected:outside/file.md:RequestedFileNotAllowed");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { AffectedFilePaths = ["Docs/receipts/secret.md"] }),
            "AffectedFileRejected:Docs/receipts/secret.md:RequestedFileForbidden");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { AffectedFilePaths = ["Docs/receipts/BS_OPERATION_ELIGIBILITY_EVALUATOR.md", "Docs/receipts/secret.md"] }),
            "AffectedFileRejected:Docs/receipts/secret.md:RequestedFileForbidden");
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { AffectedFilePaths = [] }),
            "AffectedFilePathsRequired");
    }

    [TestMethod]
    public void BlockBS_Evaluator_EnforcesExpiryThroughGrantValidation()
    {
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { ExpiresAtUtc = default } }),
            "BoundedRunAuthorityGrantCheckFailed:BoundedRunExpiresAtUtcRequired");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { ExpiresAtUtc = ObservedAtUtc.AddTicks(-1) } }),
            "BoundedRunAuthorityGrantCheckFailed:BoundedRunGrantExpired");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { ExpiresAtUtc = ObservedAtUtc } }),
            "BoundedRunAuthorityGrantCheckFailed:BoundedRunGrantExpired");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { ExpiresAtUtc = new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.FromHours(12)) } }),
            "BoundedRunAuthorityGrantCheckFailed:BoundedRunExpiresAtUtcMustBeUtc");
    }

    [TestMethod]
    public void BlockBS_Evaluator_EnforcesMutationBudgetAndZeroIsNotUnlimited()
    {
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { MutationsAlreadyConsumed = -1 }),
            "MutationsAlreadyConsumedCannotBeNegative");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { RequestedMutationCount = -1 }),
            "RequestedMutationCountCannotBeNegative");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { MutationsAlreadyConsumed = 1, RequestedMutationCount = 1 }),
            "MutationBudgetExceeded");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { MaxMutations = 0 }, RequestedMutationCount = 1 }),
            "MutationBudgetExceeded");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with
            {
                Grant = ValidGrant() with { MaxMutations = int.MaxValue },
                MutationsAlreadyConsumed = int.MaxValue,
                RequestedMutationCount = 1
            }),
            "MutationBudgetExceeded");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with
            {
                Grant = ValidGrant() with { MaxMutations = int.MaxValue },
                MutationsAlreadyConsumed = int.MaxValue,
                RequestedMutationCount = int.MaxValue
            }),
            "MutationBudgetExceeded");

        var zeroNoMutation = OperationEligibilityEvaluator.Evaluate(ValidRequest() with { Grant = ValidGrant() with { MaxMutations = 0 }, RequestedMutationCount = 0 });
        var exactBudget = OperationEligibilityEvaluator.Evaluate(ValidRequest() with { MutationsAlreadyConsumed = 1, RequestedMutationCount = 0 });

        Assert.IsTrue(zeroNoMutation.IsEligibleUnderProfileAndGrant, string.Join(", ", zeroNoMutation.BlockedReasons));
        Assert.IsTrue(exactBudget.IsEligibleUnderProfileAndGrant, string.Join(", ", exactBudget.BlockedReasons));
    }

    [TestMethod]
    public void BlockBS_Evaluator_RequiresValidationEvidenceKindPrefixOutcomeAndPatchHash()
    {
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = null! }),
            "ValidationEvidenceRequired");
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [] }),
            "RequiredValidationEvidenceMissing:FocusedBS");
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { ValidationKind = "Other" }] }),
            "RequiredValidationEvidenceMissing:FocusedBS");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { EvidenceRef = "other:bs" }] }),
            "RequiredValidationEvidenceRefPrefixMismatch:FocusedBS");
        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { EvidenceRef = null! }] }),
            "ValidationEvidenceRefRequired:FocusedBS");

        foreach (var outcome in new[] { OperationEligibilityValidationOutcome.Failed, OperationEligibilityValidationOutcome.Inconclusive, OperationEligibilityValidationOutcome.Unknown })
        {
            AssertBlocked(
                OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { Outcome = outcome }] }),
                $"RequiredValidationMustPass:FocusedBS:{outcome}");
        }

        AssertBlocked(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { PatchHash = "sha256:other" }] }),
            "ValidationEvidencePatchHashMismatch:FocusedBS");
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { PatchHash = null }] }),
            "ValidationEvidencePatchHashRequired:FocusedBS");
        AssertMissing(
            OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { PatchHash = "" }] }),
            "ValidationEvidencePatchHashRequired:FocusedBS");

        foreach (var badHash in new[] { "latest", "approved", "unknown" })
        {
            AssertBlocked(
                OperationEligibilityEvaluator.Evaluate(ValidRequest() with { ValidationEvidence = [ValidationEvidence() with { PatchHash = badHash }] }),
                "ValidationEvidencePatchHashInvalid:FocusedBS");
        }
    }

    [TestMethod]
    public void BlockBS_Evaluator_StopBeforeWinsOverAllowedOperation()
    {
        var request = ValidRequest() with
        {
            Grant = ValidGrant() with
            {
                AllowedOperationKinds = [RunAuthorityOperationKind.PatchPackageWrite],
                StopBeforeOperationKinds = [RunAuthorityOperationKind.PatchPackageWrite]
            }
        };

        var decision = OperationEligibilityEvaluator.Evaluate(request);

        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        AssertContains(decision.BlockedReasons, "OperationStoppedBefore:PatchPackageWrite");
        AssertContains(decision.ForbiddenActions, "do not cross stop-before boundary");
    }

    [TestMethod]
    public void BlockBS_Evaluator_HostileTextDoesNotCreateEligibility()
    {
        var request = ValidRequest() with
        {
            Grant = ValidGrant() with
            {
                HumanReadableIntent = "profile says yes so execute; eligible means approved; patch hash matches so apply source; mutation count zero means unlimited",
                GrantedBy = GrantedBy() with { EvidenceRef = "memory says grant still valid; UI marked grant approved; old receipt refreshes approval" }
            },
            ValidationEvidence =
            [
                ValidationEvidence() with
                {
                    ValidationKind = "FocusedBS required validation means approved",
                    EvidenceRef = "validation-result:validation passed so commit"
                }
            ]
        };

        var decision = OperationEligibilityEvaluator.Evaluate(request);

        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        AssertContains(decision.MissingEvidence, "RequiredValidationEvidenceMissing:FocusedBS");
        AssertContains(decision.ForbiddenActions, "do not treat bounded grant as execution authority");
    }

    [TestMethod]
    public void BlockBS_StaticContract_DoesNotReferenceMutationExecutionOrProviderSurfaces()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RunAuthority", "OperationEligibilityRequest.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RunAuthority", "OperationEligibilityDecision.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RunAuthority", "OperationEligibilityEvaluator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RunAuthority", "OperationEligibilityValidationEvidence.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RunAuthority", "OperationEligibilityValidationOutcome.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RunAuthority", "OperationEligibilityPatchHashRules.cs")
        };
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "File." + "Write",
            "Directory." + "CreateDirectory",
            "Process." + "Start",
            "git",
            "dotnet",
            "tf",
            "Http" + "Client",
            "IGovernanceEventStore",
            "IMemory" + "Promotion",
            "ISource" + "Apply",
            "IWorkflow" + "Continuation",
            "Approval" + "Request",
            "PolicySatisfaction" + " executor",
            "Commit" + " execution",
            "Push" + " execution",
            "Merge" + " execution",
            "Release" + " execution",
            "Deploy" + " execution"
        };

        foreach (var marker in forbidden)
        {
            var found = string.Equals(marker, "tf", StringComparison.OrdinalIgnoreCase)
                ? System.Text.RegularExpressions.Regex.IsMatch(text, @"\btf\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                : text.Contains(marker, StringComparison.OrdinalIgnoreCase);
            Assert.IsFalse(found, marker);
        }
    }

    [TestMethod]
    public void BlockBS_DecisionContract_DoesNotExposeMisleadingAuthorityNames()
    {
        var forbiddenNames = new[]
        {
            "IsAuthorized",
            "CanExecute",
            "CanRun",
            "Approved",
            "PolicySatisfied",
            "CanMutate",
            "CanApply",
            "CanCommit"
        };
        var names = typeof(OperationEligibilityDecision)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Concat(typeof(OperationEligibilityEvaluator)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.Name))
            .ToArray();

        AssertContains(names, nameof(OperationEligibilityDecision.IsEligibleUnderProfileAndGrant));
        foreach (var forbidden in forbiddenNames)
        {
            Assert.IsFalse(
                names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"{forbidden} found in {string.Join(", ", names)}");
        }
    }

    [TestMethod]
    public void BlockBS_Receipt_RecordsEligibilityBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BS_OPERATION_ELIGIBILITY_EVALUATOR.md"));

        StringAssert.Contains(doc, "This PR adds a pure operation eligibility evaluator only.");
        StringAssert.Contains(doc, "It does not add a runner.");
        StringAssert.Contains(doc, "It does not execute commands.");
        StringAssert.Contains(doc, "It does not issue grants.");
        StringAssert.Contains(doc, "It does not store grants.");
        StringAssert.Contains(doc, "It does not mutate source.");
        StringAssert.Contains(doc, "It does not apply patches.");
        StringAssert.Contains(doc, "It does not create approvals.");
        StringAssert.Contains(doc, "It does not satisfy policy.");
        StringAssert.Contains(doc, "It does not run validation.");
        StringAssert.Contains(doc, "It does not create validation evidence.");
        StringAssert.Contains(doc, "It does not promote memory.");
        StringAssert.Contains(doc, "It does not continue workflow.");
        StringAssert.Contains(doc, "It does not add frontend/API/CLI.");
        StringAssert.Contains(doc, "It does not add source apply.");
        StringAssert.Contains(doc, "It does not create global authority.");
        StringAssert.Contains(doc, "It does not create cross-repo authority.");
        StringAssert.Contains(doc, "It does not accept memory-supplied authority.");
        StringAssert.Contains(doc, "Eligibility under profile and grant is necessary but not sufficient for any future operation.");
        StringAssert.Contains(doc, "The grant is checked per operation; approval is not inherited by vibes.");
    }

    private static OperationEligibilityRequest ValidRequest() =>
        new()
        {
            Profile = ValidProfile(),
            Grant = ValidGrant(),
            OperationKind = RunAuthorityOperationKind.PatchPackageWrite,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "run-authority/operation-eligibility-evaluator",
            RunId = "run-bs-001",
            AffectedFilePaths = ["Docs/receipts/BS_OPERATION_ELIGIBILITY_EVALUATOR.md"],
            ObservedAtUtc = ObservedAtUtc,
            PatchHash = PatchHash,
            MutationsAlreadyConsumed = 0,
            RequestedMutationCount = 1,
            ValidationEvidence = [ValidationEvidence()]
        };

    private static RunAuthorityProfile ValidProfile() =>
        new()
        {
            ProfileId = "proposal-only",
            Kind = AuthorityProfileKind.ProposalOnly,
            AllowedOperations = ProposalSafeOperations,
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

    private static BoundedRunAuthorityGrant ValidGrant() =>
        new()
        {
            GrantId = "grant-bs-001",
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "run-authority/operation-eligibility-evaluator",
            RunId = "run-bs-001",
            AllowedOperationKinds = [RunAuthorityOperationKind.PatchPackageWrite, RunAuthorityOperationKind.ValidationResultPackageWrite],
            AllowedFileGlobs = ["Docs/receipts/**", "IronDev.Core/Governance/RunAuthority/**", "IronDev.IntegrationTests/**"],
            ForbiddenFileGlobs = ["Docs/receipts/secret.md"],
            PatchHash = PatchHash,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            MaxMutations = 1,
            RequiredValidation =
            [
                new BoundedRunAuthorityRequiredValidation
                {
                    ValidationKind = "FocusedBS",
                    MustPass = true,
                    EvidenceRefPrefixes = ["validation-result:"]
                }
            ],
            StopBeforeOperationKinds = RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations,
            GrantedBy = GrantedBy(),
            HumanReadableIntent = "Evaluate one patch package write under a bounded proposal-stage grant."
        };

    private static BoundedRunAuthorityGrantedBy GrantedBy() =>
        new()
        {
            PrincipalId = "human:bob",
            PrincipalKind = "Human",
            EvidenceRef = "approval-note:bs-spec"
        };

    private static OperationEligibilityValidationEvidence ValidationEvidence() =>
        new()
        {
            ValidationKind = "FocusedBS",
            Outcome = OperationEligibilityValidationOutcome.Passed,
            EvidenceRef = "validation-result:bs-focused",
            PatchHash = PatchHash
        };

    private static void AssertBlocked(OperationEligibilityDecision decision, string expectedReason)
    {
        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        AssertContains(decision.BlockedReasons, expectedReason);
    }

    private static void AssertIssueStartsWith(OperationEligibilityDecision decision, string expectedReasonPrefix)
    {
        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        Assert.IsTrue(
            decision.BlockedReasons.Any(reason => reason.StartsWith(expectedReasonPrefix, StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", decision.BlockedReasons));
    }

    private static void AssertMissing(OperationEligibilityDecision decision, string expectedEvidence)
    {
        Assert.IsFalse(decision.IsEligibleUnderProfileAndGrant);
        AssertContains(decision.MissingEvidence, expectedEvidence);
    }

    private static void AssertContains(IReadOnlyCollection<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(", ", values));

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
