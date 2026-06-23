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
