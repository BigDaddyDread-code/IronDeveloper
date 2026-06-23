namespace IronDev.Core.Governance;

public static class RunAuthorityProfileValidator
{
    public static readonly IReadOnlyCollection<RunAuthorityOperationKind> ProposalOnlyAllowedOperations =
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

    public static readonly IReadOnlyCollection<RunAuthorityOperationKind> ProposalOnlyForbiddenOperations =
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

    public static readonly IReadOnlyCollection<RunAuthorityOperationKind> AskBeforeMutationAllowedOperations =
    [
        .. ProposalOnlyAllowedOperations,
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation
    ];

    public static readonly IReadOnlyCollection<RunAuthorityOperationKind> AskBeforeMutationForbiddenOperations =
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

    public static readonly IReadOnlyCollection<RunAuthorityOperationKind> BoundedRunAuthorityAllowedOperations =
    [
        .. ProposalOnlyAllowedOperations,
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest
    ];

    public static readonly IReadOnlyCollection<RunAuthorityOperationKind> BoundedRunAuthorityForbiddenOperations =
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

    public static RunAuthorityProfileValidationResult Validate(RunAuthorityProfile? profile)
    {
        var issues = new List<string>();

        if (profile is null)
        {
            issues.Add("RunAuthorityProfileRequired");
            return Result(issues);
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileId))
            issues.Add("RunAuthorityProfileIdRequired");
        if (!Enum.IsDefined(profile.Kind) || profile.Kind == AuthorityProfileKind.Unknown)
            issues.Add("AuthorityProfileKindKnownRequired");

        ValidateOperationCollections(profile, issues);

        if (profile.Kind == AuthorityProfileKind.ProposalOnly)
        {
            ValidateProposalOnly(profile, issues);
        }
        else if (profile.Kind == AuthorityProfileKind.AskBeforeMutation)
        {
            ValidateAskBeforeMutation(profile, issues);
        }
        else if (profile.Kind == AuthorityProfileKind.BoundedRunAuthority)
        {
            ValidateBoundedRunAuthority(profile, issues);
        }
        else if (Enum.IsDefined(profile.Kind) && profile.Kind != AuthorityProfileKind.Unknown)
        {
            issues.Add($"AuthorityProfileKindUnsupported:{profile.Kind}");
        }

        return Result(issues);
    }

    private static void ValidateOperationCollections(RunAuthorityProfile profile, ICollection<string> issues)
    {
        if (profile.AllowedOperations is null)
        {
            issues.Add("RunAuthorityAllowedOperationsRequired");
        }
        else if (profile.AllowedOperations.Count == 0)
        {
            issues.Add("RunAuthorityAllowedOperationsRequired");
        }
        else
        {
            foreach (var operation in profile.AllowedOperations)
                ValidateOperationKind(operation, "RunAuthorityAllowedOperationKnownRequired", issues);
        }

        if (profile.ForbiddenOperations is null)
        {
            issues.Add("RunAuthorityForbiddenOperationsRequired");
        }
        else if (profile.ForbiddenOperations.Count == 0)
        {
            issues.Add("RunAuthorityForbiddenOperationsRequired");
        }
        else
        {
            foreach (var operation in profile.ForbiddenOperations)
                ValidateOperationKind(operation, "RunAuthorityForbiddenOperationKnownRequired", issues);
        }

        if (profile.AllowedOperations is null || profile.ForbiddenOperations is null)
            return;

        foreach (var operation in profile.AllowedOperations.Intersect(profile.ForbiddenOperations).Distinct())
            issues.Add($"RunAuthorityAllowedForbiddenOverlap:{operation}");
    }

    private static void ValidateProposalOnly(RunAuthorityProfile profile, ICollection<string> issues)
    {
        if (profile.AllowedOperations is not null)
        {
            foreach (var operation in ProposalOnlyAllowedOperations)
            {
                if (!profile.AllowedOperations.Contains(operation))
                    issues.Add($"ProposalOnlyRequiredAllowedOperationMissing:{operation}");
            }

            foreach (var operation in profile.AllowedOperations.Where(ProposalOnlyForbiddenOperations.Contains).Distinct())
                issues.Add($"ProposalOnlyCannotAllowDangerousOperation:{operation}");
        }

        if (profile.ForbiddenOperations is not null)
        {
            foreach (var operation in ProposalOnlyForbiddenOperations)
            {
                if (!profile.ForbiddenOperations.Contains(operation))
                    issues.Add($"ProposalOnlyRequiredForbiddenOperationMissing:{operation}");
            }
        }

        RequireSafeFlag(profile.CanReadRepo, nameof(RunAuthorityProfile.CanReadRepo), issues);
        RequireSafeFlag(profile.CanMutateDisposableWorkspace, nameof(RunAuthorityProfile.CanMutateDisposableWorkspace), issues);
        RequireSafeFlag(profile.CanWriteProposalEvidence, nameof(RunAuthorityProfile.CanWriteProposalEvidence), issues);
        RequireSafeFlag(profile.CanInspectGovernedStatus, nameof(RunAuthorityProfile.CanInspectGovernedStatus), issues);

        RejectDangerousFlag(profile.CanMutateDurableSource, nameof(RunAuthorityProfile.CanMutateDurableSource), issues);
        RejectDangerousFlag(profile.CanApplyPatch, nameof(RunAuthorityProfile.CanApplyPatch), issues);
        RejectDangerousFlag(profile.CanExecuteRollback, nameof(RunAuthorityProfile.CanExecuteRollback), issues);
        RejectDangerousFlag(profile.CanCommit, nameof(RunAuthorityProfile.CanCommit), issues);
        RejectDangerousFlag(profile.CanPush, nameof(RunAuthorityProfile.CanPush), issues);
        RejectDangerousFlag(profile.CanCreatePullRequest, nameof(RunAuthorityProfile.CanCreatePullRequest), issues);
        RejectDangerousFlag(profile.CanMarkReadyForReview, nameof(RunAuthorityProfile.CanMarkReadyForReview), issues);
        RejectDangerousFlag(profile.CanMerge, nameof(RunAuthorityProfile.CanMerge), issues);
        RejectDangerousFlag(profile.CanRelease, nameof(RunAuthorityProfile.CanRelease), issues);
        RejectDangerousFlag(profile.CanDeploy, nameof(RunAuthorityProfile.CanDeploy), issues);
        RejectDangerousFlag(profile.CanCreateApprovalRequest, nameof(RunAuthorityProfile.CanCreateApprovalRequest), issues);
        RejectDangerousFlag(profile.CanSatisfyPolicy, nameof(RunAuthorityProfile.CanSatisfyPolicy), issues);
        RejectDangerousFlag(profile.CanPromoteMemory, nameof(RunAuthorityProfile.CanPromoteMemory), issues);
        RejectDangerousFlag(profile.CanContinueWorkflow, nameof(RunAuthorityProfile.CanContinueWorkflow), issues);
        RejectDangerousFlag(profile.CanExecuteProviderMutation, nameof(RunAuthorityProfile.CanExecuteProviderMutation), issues);
        RejectDangerousFlag(profile.CanPublishPackage, nameof(RunAuthorityProfile.CanPublishPackage), issues);
    }

    private static void ValidateAskBeforeMutation(RunAuthorityProfile profile, ICollection<string> issues)
    {
        if (profile.AllowedOperations is not null)
        {
            foreach (var operation in AskBeforeMutationAllowedOperations)
            {
                if (!profile.AllowedOperations.Contains(operation))
                    issues.Add($"AskBeforeMutationRequiredAllowedOperationMissing:{operation}");
            }

            foreach (var operation in profile.AllowedOperations.Where(AskBeforeMutationForbiddenOperations.Contains).Distinct())
                issues.Add($"AskBeforeMutationCannotAllowDangerousOperation:{operation}");
        }

        if (profile.ForbiddenOperations is not null)
        {
            foreach (var operation in AskBeforeMutationForbiddenOperations)
            {
                if (!profile.ForbiddenOperations.Contains(operation))
                    issues.Add($"AskBeforeMutationRequiredForbiddenOperationMissing:{operation}");
            }
        }

        RequireAskBeforeMutationFlag(profile.CanReadRepo, nameof(RunAuthorityProfile.CanReadRepo), issues);
        RequireAskBeforeMutationFlag(profile.CanMutateDisposableWorkspace, nameof(RunAuthorityProfile.CanMutateDisposableWorkspace), issues);
        RequireAskBeforeMutationFlag(profile.CanWriteProposalEvidence, nameof(RunAuthorityProfile.CanWriteProposalEvidence), issues);
        RequireAskBeforeMutationFlag(profile.CanInspectGovernedStatus, nameof(RunAuthorityProfile.CanInspectGovernedStatus), issues);
        RequireAskBeforeMutationFlag(profile.CanMutateDurableSource, nameof(RunAuthorityProfile.CanMutateDurableSource), issues);
        RequireAskBeforeMutationFlag(profile.CanApplyPatch, nameof(RunAuthorityProfile.CanApplyPatch), issues);

        RejectAskBeforeMutationFlag(profile.CanExecuteRollback, nameof(RunAuthorityProfile.CanExecuteRollback), issues);
        RejectAskBeforeMutationFlag(profile.CanCommit, nameof(RunAuthorityProfile.CanCommit), issues);
        RejectAskBeforeMutationFlag(profile.CanPush, nameof(RunAuthorityProfile.CanPush), issues);
        RejectAskBeforeMutationFlag(profile.CanCreatePullRequest, nameof(RunAuthorityProfile.CanCreatePullRequest), issues);
        RejectAskBeforeMutationFlag(profile.CanMarkReadyForReview, nameof(RunAuthorityProfile.CanMarkReadyForReview), issues);
        RejectAskBeforeMutationFlag(profile.CanMerge, nameof(RunAuthorityProfile.CanMerge), issues);
        RejectAskBeforeMutationFlag(profile.CanRelease, nameof(RunAuthorityProfile.CanRelease), issues);
        RejectAskBeforeMutationFlag(profile.CanDeploy, nameof(RunAuthorityProfile.CanDeploy), issues);
        RejectAskBeforeMutationFlag(profile.CanCreateApprovalRequest, nameof(RunAuthorityProfile.CanCreateApprovalRequest), issues);
        RejectAskBeforeMutationFlag(profile.CanSatisfyPolicy, nameof(RunAuthorityProfile.CanSatisfyPolicy), issues);
        RejectAskBeforeMutationFlag(profile.CanPromoteMemory, nameof(RunAuthorityProfile.CanPromoteMemory), issues);
        RejectAskBeforeMutationFlag(profile.CanContinueWorkflow, nameof(RunAuthorityProfile.CanContinueWorkflow), issues);
        RejectAskBeforeMutationFlag(profile.CanExecuteProviderMutation, nameof(RunAuthorityProfile.CanExecuteProviderMutation), issues);
        RejectAskBeforeMutationFlag(profile.CanPublishPackage, nameof(RunAuthorityProfile.CanPublishPackage), issues);
    }

    private static void ValidateBoundedRunAuthority(RunAuthorityProfile profile, ICollection<string> issues)
    {
        if (profile.AllowedOperations is not null)
        {
            foreach (var operation in BoundedRunAuthorityAllowedOperations)
            {
                if (!profile.AllowedOperations.Contains(operation))
                    issues.Add($"BoundedRunAuthorityRequiredAllowedOperationMissing:{operation}");
            }

            foreach (var operation in profile.AllowedOperations.Where(BoundedRunAuthorityForbiddenOperations.Contains).Distinct())
                issues.Add($"BoundedRunAuthorityCannotAllowDangerousOperation:{operation}");
        }

        if (profile.ForbiddenOperations is not null)
        {
            foreach (var operation in BoundedRunAuthorityForbiddenOperations)
            {
                if (!profile.ForbiddenOperations.Contains(operation))
                    issues.Add($"BoundedRunAuthorityRequiredForbiddenOperationMissing:{operation}");
            }
        }

        RequireBoundedRunAuthorityFlag(profile.CanReadRepo, nameof(RunAuthorityProfile.CanReadRepo), issues);
        RequireBoundedRunAuthorityFlag(profile.CanMutateDisposableWorkspace, nameof(RunAuthorityProfile.CanMutateDisposableWorkspace), issues);
        RequireBoundedRunAuthorityFlag(profile.CanWriteProposalEvidence, nameof(RunAuthorityProfile.CanWriteProposalEvidence), issues);
        RequireBoundedRunAuthorityFlag(profile.CanInspectGovernedStatus, nameof(RunAuthorityProfile.CanInspectGovernedStatus), issues);
        RequireBoundedRunAuthorityFlag(profile.CanMutateDurableSource, nameof(RunAuthorityProfile.CanMutateDurableSource), issues);
        RequireBoundedRunAuthorityFlag(profile.CanApplyPatch, nameof(RunAuthorityProfile.CanApplyPatch), issues);
        RequireBoundedRunAuthorityFlag(profile.CanExecuteRollback, nameof(RunAuthorityProfile.CanExecuteRollback), issues);
        RequireBoundedRunAuthorityFlag(profile.CanCommit, nameof(RunAuthorityProfile.CanCommit), issues);
        RequireBoundedRunAuthorityFlag(profile.CanPush, nameof(RunAuthorityProfile.CanPush), issues);
        RequireBoundedRunAuthorityFlag(profile.CanCreatePullRequest, nameof(RunAuthorityProfile.CanCreatePullRequest), issues);

        RejectBoundedRunAuthorityFlag(profile.CanMarkReadyForReview, nameof(RunAuthorityProfile.CanMarkReadyForReview), issues);
        RejectBoundedRunAuthorityFlag(profile.CanMerge, nameof(RunAuthorityProfile.CanMerge), issues);
        RejectBoundedRunAuthorityFlag(profile.CanRelease, nameof(RunAuthorityProfile.CanRelease), issues);
        RejectBoundedRunAuthorityFlag(profile.CanDeploy, nameof(RunAuthorityProfile.CanDeploy), issues);
        RejectBoundedRunAuthorityFlag(profile.CanCreateApprovalRequest, nameof(RunAuthorityProfile.CanCreateApprovalRequest), issues);
        RejectBoundedRunAuthorityFlag(profile.CanSatisfyPolicy, nameof(RunAuthorityProfile.CanSatisfyPolicy), issues);
        RejectBoundedRunAuthorityFlag(profile.CanPromoteMemory, nameof(RunAuthorityProfile.CanPromoteMemory), issues);
        RejectBoundedRunAuthorityFlag(profile.CanContinueWorkflow, nameof(RunAuthorityProfile.CanContinueWorkflow), issues);
        RejectBoundedRunAuthorityFlag(profile.CanExecuteProviderMutation, nameof(RunAuthorityProfile.CanExecuteProviderMutation), issues);
        RejectBoundedRunAuthorityFlag(profile.CanPublishPackage, nameof(RunAuthorityProfile.CanPublishPackage), issues);
    }

    private static void ValidateOperationKind(
        RunAuthorityOperationKind operation,
        string issue,
        ICollection<string> issues)
    {
        if (!Enum.IsDefined(operation) || operation == RunAuthorityOperationKind.Unknown)
            issues.Add(issue);
    }

    private static void RequireSafeFlag(bool value, string flagName, ICollection<string> issues)
    {
        if (!value)
            issues.Add($"ProposalOnlySafeFlagMustBeTrue:{flagName}");
    }

    private static void RejectDangerousFlag(bool value, string flagName, ICollection<string> issues)
    {
        if (value)
            issues.Add($"ProposalOnlyDangerousFlagMustBeFalse:{flagName}");
    }

    private static void RequireAskBeforeMutationFlag(bool value, string flagName, ICollection<string> issues)
    {
        if (!value)
            issues.Add($"AskBeforeMutationRequiredFlagMustBeTrue:{flagName}");
    }

    private static void RejectAskBeforeMutationFlag(bool value, string flagName, ICollection<string> issues)
    {
        if (value)
            issues.Add($"AskBeforeMutationDangerousFlagMustBeFalse:{flagName}");
    }

    private static void RequireBoundedRunAuthorityFlag(bool value, string flagName, ICollection<string> issues)
    {
        if (!value)
            issues.Add($"BoundedRunAuthorityRequiredFlagMustBeTrue:{flagName}");
    }

    private static void RejectBoundedRunAuthorityFlag(bool value, string flagName, ICollection<string> issues)
    {
        if (value)
            issues.Add($"BoundedRunAuthorityDangerousFlagMustBeFalse:{flagName}");
    }

    private static RunAuthorityProfileValidationResult Result(IEnumerable<string> issues)
    {
        var issueList = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new RunAuthorityProfileValidationResult
        {
            IsValid = issueList.Length == 0,
            Issues = issueList
        };
    }
}
