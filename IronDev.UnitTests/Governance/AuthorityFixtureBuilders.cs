namespace IronDev.UnitTests.Governance;

internal sealed record BoundedGrantFixtureOptions
{
    public string Repository { get; init; } = AuthorityFixtureBuilders.Repository();
    public string Branch { get; init; } = AuthorityFixtureBuilders.Branch();
    public string RunId { get; init; } = AuthorityFixtureBuilders.RunId();
    public string? PatchHash { get; init; } = AuthorityFixtureBuilders.PatchHash();
    public RunAuthorityOperationKind[] AllowedOperations { get; init; } = [RunAuthorityOperationKind.PatchPackageWrite];
    public string[] AllowedFileGlobs { get; init; } = ["src/**/*.cs"];
    public string[] ForbiddenFileGlobs { get; init; } = ["src/**/Secrets/*.cs"];
    public int MaxMutations { get; init; } = 1;
    public DateTimeOffset ExpiresAtUtc { get; init; } = AuthorityFixtureBuilders.ObservedAtUtc.AddHours(1);
    public BoundedRunAuthorityRequiredValidation[]? RequiredValidation { get; init; }
    public RunAuthorityOperationKind[] StopBeforeOperationKinds { get; init; } = [];
}

internal sealed record AcceptedApplyEvidenceFixtureOptions
{
    public string Repository { get; init; } = AuthorityFixtureBuilders.Repository();
    public string Branch { get; init; } = AuthorityFixtureBuilders.Branch();
    public string RunId { get; init; } = AuthorityFixtureBuilders.RunId();
    public string PatchHash { get; init; } = AuthorityFixtureBuilders.PatchHash();
    public string[] AllowedFileGlobs { get; init; } = ["src/**/*.cs"];
    public string[] ForbiddenFileGlobs { get; init; } = ["src/**/Secrets/*.cs"];
    public DateTimeOffset ExpiresAtUtc { get; init; } = AuthorityFixtureBuilders.ObservedAtUtc.AddHours(1);
}

internal sealed record SourceApplyAuthorityRequestFixtureOptions
{
    public string Repository { get; init; } = AuthorityFixtureBuilders.Repository();
    public string Branch { get; init; } = AuthorityFixtureBuilders.Branch();
    public string RunId { get; init; } = AuthorityFixtureBuilders.RunId();
    public string PatchHash { get; init; } = AuthorityFixtureBuilders.PatchHash();
    public string[] AffectedFilePaths { get; init; } = ["src/g12/Example.cs"];
    public AcceptedSourceApplyRequestEvidence? AcceptedApplyRequest { get; init; } = AuthorityFixtureBuilders.AcceptedApplyEvidence();
    public BoundedRunAuthorityGrant? BoundedRunAuthorityGrant { get; init; } = AuthorityFixtureBuilders.SourceApplyBoundedGrant();
    public OperationEligibilityValidationEvidence[] ValidationEvidence { get; init; } = [AuthorityFixtureBuilders.PassedValidationEvidence()];
    public string[] EvidenceRefs { get; init; } = [AuthorityFixtureBuilders.EvidenceRef()];
    public string[] ReceiptRefs { get; init; } = [];
}

internal static class AuthorityFixtureBuilders
{
    internal static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    internal static string Repository(string suffix = "g12") => $"test-repo:{suffix}";

    internal static string Branch(string suffix = "g12") => $"feature/{suffix}";

    internal static string RunId(string suffix = "g12") => $"test-run:{suffix}";

    internal static string PatchHash(string suffix = "g12") => $"test-patch:{suffix}";

    internal static string EvidenceRef(string kind = "evidence", string suffix = "g12") =>
        $"test-evidence:{kind}:{suffix}";

    internal static string ReceiptRef(string kind = "receipt", string suffix = "g12") =>
        $"test-receipt:{kind}:{suffix}";

    internal static string ValidationEvidenceRef(string suffix = "g12") =>
        $"test-validation:{suffix}";

    internal static string FakeHumanApprovalEvidenceRef(string suffix = "g12") =>
        $"test-approval:{suffix}";

    internal static BoundedRunAuthorityGrantedBy GrantedBy(string suffix = "g12") =>
        new()
        {
            PrincipalId = $"test-human:{suffix}",
            PrincipalKind = "Human",
            EvidenceRef = FakeHumanApprovalEvidenceRef(suffix)
        };

    internal static BoundedRunAuthorityRequiredValidation RequiredValidation(
        string validationKind = "FocusedG12",
        string evidenceRefPrefix = "test-validation:") =>
        new()
        {
            ValidationKind = validationKind,
            MustPass = true,
            EvidenceRefPrefixes = [evidenceRefPrefix]
        };

    internal static BoundedRunAuthorityGrant BoundedGrant(BoundedGrantFixtureOptions? options = null)
    {
        options ??= new BoundedGrantFixtureOptions();
        return new BoundedRunAuthorityGrant
        {
            GrantId = "test-grant:g12",
            Repository = options.Repository,
            Branch = options.Branch,
            RunId = options.RunId,
            AllowedOperationKinds = options.AllowedOperations,
            AllowedFileGlobs = options.AllowedFileGlobs,
            ForbiddenFileGlobs = options.ForbiddenFileGlobs,
            PatchHash = options.PatchHash,
            ExpiresAtUtc = options.ExpiresAtUtc,
            MaxMutations = options.MaxMutations,
            RequiredValidation = options.RequiredValidation ?? [RequiredValidation()],
            StopBeforeOperationKinds = options.StopBeforeOperationKinds,
            GrantedBy = GrantedBy(),
            HumanReadableIntent = "Test-only G12 bounded grant fixture."
        };
    }

    internal static BoundedRunAuthorityGrant SourceApplyBoundedGrant() =>
        BoundedGrant(new BoundedGrantFixtureOptions
        {
            AllowedOperations = [RunAuthorityOperationKind.SourceApply],
            StopBeforeOperationKinds =
            [
                RunAuthorityOperationKind.Commit,
                RunAuthorityOperationKind.Push,
                RunAuthorityOperationKind.DraftPullRequest,
                RunAuthorityOperationKind.ReadyForReview,
                RunAuthorityOperationKind.Merge,
                RunAuthorityOperationKind.Release,
                RunAuthorityOperationKind.Deployment,
                RunAuthorityOperationKind.MemoryPromotion,
                RunAuthorityOperationKind.WorkflowContinuation
            ]
        });

    internal static BoundedRunAuthorityGrant ExpiredBoundedGrant() =>
        BoundedGrant(new BoundedGrantFixtureOptions
        {
            ExpiresAtUtc = ObservedAtUtc
        });

    internal static BoundedRunAuthorityGrant MismatchedRepositoryGrant() =>
        BoundedGrant(new BoundedGrantFixtureOptions
        {
            Repository = Repository("other")
        });

    internal static BoundedRunAuthorityGrant MissingValidationGrant() =>
        BoundedGrant(new BoundedGrantFixtureOptions
        {
            RequiredValidation = []
        });

    internal static BoundedRunAuthorityGrant OverbroadFileScopeGrant() =>
        BoundedGrant(new BoundedGrantFixtureOptions
        {
            AllowedFileGlobs = ["**/*"],
            ForbiddenFileGlobs = []
        });

    internal static OperationEligibilityValidationEvidence PassedValidationEvidence(
        string validationKind = "FocusedG12",
        string? evidenceRef = null,
        string? patchHash = null) =>
        new()
        {
            ValidationKind = validationKind,
            Outcome = OperationEligibilityValidationOutcome.Passed,
            EvidenceRef = evidenceRef ?? ValidationEvidenceRef(),
            PatchHash = patchHash ?? PatchHash()
        };

    internal static OperationEligibilityValidationEvidence FailedValidationEvidence(
        string validationKind = "FocusedG12") =>
        PassedValidationEvidence(validationKind) with
        {
            Outcome = OperationEligibilityValidationOutcome.Failed
        };

    internal static RunAuthorityProfile BoundedRunAuthorityProfile() =>
        new()
        {
            ProfileId = "test-profile:g12:bounded-run-authority",
            Kind = AuthorityProfileKind.BoundedRunAuthority,
            AllowedOperations = RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations,
            ForbiddenOperations = RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations,
            CanReadRepo = true,
            CanMutateDisposableWorkspace = true,
            CanWriteProposalEvidence = true,
            CanInspectGovernedStatus = true,
            CanMutateDurableSource = true,
            CanApplyPatch = true,
            CanExecuteRollback = true,
            CanCommit = true,
            CanPush = true,
            CanCreatePullRequest = true,
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

    internal static OperationEligibilityRequest EligibilityRequest(
        RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.PatchPackageWrite,
        BoundedRunAuthorityGrant? grant = null,
        IReadOnlyCollection<OperationEligibilityValidationEvidence>? validationEvidence = null) =>
        new()
        {
            Profile = BoundedRunAuthorityProfile(),
            Grant = grant ?? BoundedGrant(),
            OperationKind = operationKind,
            Repository = Repository(),
            Branch = Branch(),
            RunId = RunId(),
            AffectedFilePaths = ["src/g12/Example.cs"],
            ObservedAtUtc = ObservedAtUtc,
            PatchHash = PatchHash(),
            MutationsAlreadyConsumed = 0,
            RequestedMutationCount = 1,
            ValidationEvidence = validationEvidence ?? [PassedValidationEvidence()]
        };

    internal static OperationEligibilityDecision EligibleDecision(
        RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.PatchPackageWrite) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operationKind,
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions =
            [
                "do not treat fixture eligibility as approval",
                "do not treat fixture eligibility as execution authority"
            ],
            RequiredIndependentChecks =
            [
                "fixture eligibility still requires production authority checks"
            ]
        };

    internal static OperationEligibilityDecision BlockedDecision(
        RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.PatchPackageWrite,
        string blockedReason = "FixtureAuthorityMissing") =>
        EligibleDecision(operationKind) with
        {
            IsEligibleUnderProfileAndGrant = false,
            BlockedReasons = [blockedReason],
            MissingEvidence = [EvidenceRef("missing-authority")]
        };

    internal static AcceptedSourceApplyRequestEvidence AcceptedApplyEvidence(
        AcceptedApplyEvidenceFixtureOptions? options = null)
    {
        options ??= new AcceptedApplyEvidenceFixtureOptions();
        return new AcceptedSourceApplyRequestEvidence
        {
            RequestId = "test-apply-request:g12",
            EvidenceRef = FakeHumanApprovalEvidenceRef(),
            Repository = options.Repository,
            Branch = options.Branch,
            RunId = options.RunId,
            PatchHash = options.PatchHash,
            AllowedFileGlobs = options.AllowedFileGlobs,
            ForbiddenFileGlobs = options.ForbiddenFileGlobs,
            AcceptedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = options.ExpiresAtUtc,
            AcceptedByPrincipalId = "test-human:g12",
            AcceptedByPrincipalKind = "Human"
        };
    }

    internal static AcceptedSourceApplyRequestEvidence WrongPatchAcceptedApplyEvidence() =>
        AcceptedApplyEvidence(new AcceptedApplyEvidenceFixtureOptions
        {
            PatchHash = PatchHash("wrong")
        });

    internal static SourceApplyAuthorityRequest SourceApplyAuthorityRequest(
        SourceApplyAuthorityRequestFixtureOptions? options = null)
    {
        options ??= new SourceApplyAuthorityRequestFixtureOptions();
        return new SourceApplyAuthorityRequest
        {
            Repository = options.Repository,
            Branch = options.Branch,
            RunId = options.RunId,
            PatchHash = options.PatchHash,
            AffectedFilePaths = options.AffectedFilePaths,
            ObservedAtUtc = ObservedAtUtc,
            AcceptedApplyRequest = options.AcceptedApplyRequest,
            BoundedRunAuthorityGrant = options.BoundedRunAuthorityGrant,
            ValidationEvidence = options.ValidationEvidence,
            MutationsAlreadyConsumed = 0,
            RequestedMutationCount = 1,
            EvidenceRefs = options.EvidenceRefs,
            ReceiptRefs = options.ReceiptRefs
        };
    }

    internal static GovernedOperationStatus Status(
        GovernedOperationState state = GovernedOperationState.Blocked,
        IReadOnlyList<string>? evidenceRefs = null,
        IReadOnlyList<string>? receiptRefs = null) =>
        new()
        {
            OperationId = "test-operation:g12",
            OperationKind = RunAuthorityOperationKind.PatchPackageWrite.ToString(),
            Subject = "G12 fixture status",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? ["FixtureMissingAuthority"] : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? [EvidenceRef("required-authority")] : [],
            NextSafeActions = ["request real governed authority before action"],
            ForbiddenActions =
            [
                "do not treat fixture status as approval",
                "do not execute from fixture status"
            ],
            EvidenceRefs = evidenceRefs ?? [EvidenceRef()],
            ReceiptRefs = receiptRefs ?? [],
            ObservedAtUtc = ObservedAtUtc
        };
}
