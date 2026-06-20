namespace IronDev.Core.Governance;

public static class ControlledDeploymentExecutor
{
    public static async Task<DeploymentExecutionResult> ExecuteAsync(
        DeploymentReadinessDecisionPackage? package,
        DeploymentExecutionRequest? request,
        IDeploymentExecutionGateway gateway,
        CancellationToken cancellationToken = default)
    {
        var now = request?.RequestedAtUtc ?? DateTimeOffset.UtcNow;
        if (request is null)
            return WithoutReceipt(DeploymentExecutionVerdict.Blocked, DeploymentExecutionFailureKind.MissingDeploymentExecutionRequest, ["MissingDeploymentExecutionRequest"]);

        var issues = new List<string>();
        ValidatePackage(package, issues);
        ValidateRequest(package, request, issues);
        ValidateBoundary(issues);
        if (issues.Count > 0)
            return Blocked(package, request, null, Classify(issues), issues.ToArray(), now);

        var readyPackage = package!;
        var preState = await gateway.ObserveAsync(readyPackage, request, cancellationToken).ConfigureAwait(false);
        var preIssues = new List<string>();
        ValidatePreState(readyPackage, request, preState, preIssues);
        if (preIssues.Count > 0)
            return Blocked(readyPackage, request, preState, Classify(preIssues), preIssues.ToArray(), now);

        var mutation = await gateway.DeployApprovedArtifactAsync(readyPackage, request, cancellationToken).ConfigureAwait(false);
        var mutations = new[] { mutation };
        if (!mutation.Attempted || !mutation.Accepted || mutation.Action != DeploymentExecutionAction.DeployApprovedArtifact)
        {
            var mutationIssues = FeedbackText.SafeList(["DeploymentMutationFailed", mutation.Error ?? mutation.Message ?? string.Empty]);
            var postAfterFailure = await gateway.ObserveAsync(readyPackage, request, cancellationToken).ConfigureAwait(false);
            return FailedAfterMutation(
                readyPackage,
                request,
                preState,
                postAfterFailure,
                mutations,
                [],
                DeploymentExecutionVerdict.Failed,
                DeploymentExecutionFailureKind.DeploymentMutationFailed,
                mutationIssues,
                now,
                postStateVerified: false);
        }

        var completed = new[] { DeploymentExecutionAction.DeployApprovedArtifact };
        var postState = await gateway.ObserveAsync(readyPackage, request, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostState(readyPackage, request, postState, postIssues);
        if (postIssues.Count > 0)
        {
            return FailedAfterMutation(
                readyPackage,
                request,
                preState,
                postState,
                mutations,
                completed,
                DeploymentExecutionVerdict.PartiallyExecuted,
                DeploymentExecutionFailureKind.PostDeploymentVerificationFailed,
                postIssues.ToArray(),
                now,
                postStateVerified: false);
        }

        var receipt = BuildReceipt(
            readyPackage,
            request,
            preState,
            postState,
            mutations,
            completed,
            DeploymentExecutionVerdict.ExecutedAndVerified,
            DeploymentExecutionFailureKind.None,
            [],
            now,
            preStateVerified: true,
            postStateVerified: true,
            DeploymentExecutionBoundary.Executor);
        return new DeploymentExecutionResult
        {
            Verdict = DeploymentExecutionVerdict.ExecutedAndVerified,
            FailureKind = DeploymentExecutionFailureKind.None,
            Issues = [],
            Receipt = receipt
        };
    }

    private static void ValidatePackage(DeploymentReadinessDecisionPackage? package, List<string> issues)
    {
        if (package is null)
        {
            issues.Add("MissingDeploymentReadinessDecisionPackage");
            return;
        }

        if (package.PackageVerdict == DeploymentReadinessDecisionPackageVerdict.PackageRejected)
            issues.Add("DeploymentReadinessDecisionPackageRejected");
        if (package.PackageVerdict == DeploymentReadinessDecisionPackageVerdict.PackageBlocked)
            issues.Add("DeploymentReadinessDecisionPackageBlocked");
        if (package.PackageVerdict != DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor)
            issues.Add($"DeploymentReadinessDecisionPackageNotReady:{package.PackageVerdict}");
        if (!package.CanProceedToControlledDeploymentExecutor)
            issues.Add("DeploymentReadinessDecisionPackageCannotProceedToControlledDeploymentExecutor");
        if (package.BlockReasons.Length > 0)
            issues.Add("DeploymentReadinessDecisionPackageBlocked");
        if (!package.Boundary.EvidenceOnly || PackageBoundaryCarriesAuthority(package.Boundary))
            issues.Add("DeploymentReadinessDecisionBoundaryViolation");
    }

    private static bool PackageBoundaryCarriesAuthority(DeploymentReadinessDecisionPackageBoundary boundary) =>
        boundary.CanDeploy ||
        boundary.CanPublishPackages ||
        boundary.CanPromoteMemory ||
        boundary.CanContinueWorkflow ||
        boundary.CanMutateEnvironment ||
        boundary.CanMutateSource ||
        boundary.CanCommit ||
        boundary.CanPush ||
        boundary.CanExecuteRollback ||
        boundary.CanDispatchPipeline;

    private static void ValidateRequest(
        DeploymentReadinessDecisionPackage? package,
        DeploymentExecutionRequest request,
        List<string> issues)
    {
        if (!request.ConfirmDeploymentExecution)
            issues.Add("DeploymentExecutionNotConfirmed");
        if (string.IsNullOrWhiteSpace(request.DeploymentExecutionRequestId))
            issues.Add("MissingDeploymentExecutionRequestId");
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            issues.Add("MissingDeploymentExecutionRequester");

        if (package is not null)
        {
            if (!Same(request.DeploymentReadinessDecisionPackageId, package.DeploymentReadinessDecisionPackageId))
                issues.Add("RequestPackageMismatch");
            if (!Same(request.Repository, package.Repository))
                issues.Add("RepositoryMismatch");
            if (!Same(request.CandidateCommitSha, package.CandidateCommitSha))
                issues.Add("CandidateCommitMismatch");
            if (!Same(request.CandidateVersion, package.CandidateVersion))
                issues.Add("CandidateVersionMismatch");
            if (!Same(request.CandidateTagName, package.CandidateTagName))
                issues.Add("CandidateTagMismatch");
            if (!Same(request.ReleaseChannel, package.ReleaseChannel))
                issues.Add("ReleaseChannelMismatch");
            if (!Same(request.DeploymentTarget, package.DeploymentTarget))
                issues.Add("DeploymentTargetMismatch");
            if (!Same(request.DeploymentEnvironment, package.DeploymentEnvironment))
                issues.Add("DeploymentEnvironmentMismatch");
            if (!Same(request.DeploymentArtifactName, package.DeploymentArtifactName))
                issues.Add("DeploymentArtifactMismatch");
            if (!Same(request.DeploymentArtifactSha256, package.DeploymentArtifactSha256))
                issues.Add("DeploymentArtifactChecksumMismatch");
        }

        ValidateActions(request, issues);
    }

    private static void ValidateActions(DeploymentExecutionRequest request, List<string> issues)
    {
        if (request.ApprovedActions.Length == 0)
        {
            issues.Add("MissingApprovedDeploymentAction");
            return;
        }

        foreach (var action in request.ApprovedActions)
        {
            if (!Enum.IsDefined(action) || action != DeploymentExecutionAction.DeployApprovedArtifact)
                issues.Add($"UnsupportedDeploymentAction:{action}");
        }
    }

    private static void ValidateBoundary(List<string> issues)
    {
        var boundary = DeploymentExecutionBoundary.Executor;
        if (!boundary.CanDeployApprovedArtifact)
            issues.Add("DeploymentMutationNotAllowed");
        if (boundary.CanPublishPackages)
            issues.Add("PackagePublicationNotAllowed");
        if (boundary.CanPromoteMemory)
            issues.Add("MemoryPromotionNotAllowed");
        if (boundary.CanContinueWorkflow)
            issues.Add("WorkflowContinuationNotAllowed");
        if (boundary.CanCommit || boundary.CanPush)
            issues.Add("CommitPushNotAllowed");
        if (boundary.CanMutateSource || boundary.CanMutateWorkspace)
            issues.Add("SourceMutationNotAllowed");
        if (boundary.CanExecuteRollback)
            issues.Add("RollbackExecutionNotAllowed");
        if (boundary.CanCreateTag || boundary.CanCreateGitHubRelease || boundary.CanDispatchPipeline)
            issues.Add("BoundaryViolation");
    }

    private static void ValidatePreState(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request,
        DeploymentTargetObservedState observed,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"DeploymentTargetObservationFailed:{observed.ObservationError ?? "pre-deployment observation failed"}");
            return;
        }

        ValidateSharedObservedState(package, request, observed, issues);
        if (observed.DeploymentTargetLocked)
            issues.Add("DeploymentTargetLocked");
        if (observed.DeploymentInProgress)
            issues.Add("DeploymentInProgress");
        if (Same(observed.CurrentlyDeployedArtifactSha256, package.DeploymentArtifactSha256) &&
            (string.IsNullOrWhiteSpace(observed.CurrentlyDeployedCommitSha) || Same(observed.CurrentlyDeployedCommitSha, package.CandidateCommitSha)) &&
            (string.IsNullOrWhiteSpace(observed.CurrentlyDeployedVersion) || Same(observed.CurrentlyDeployedVersion, package.CandidateVersion)))
        {
            issues.Add("DeploymentAlreadyApplied");
        }
    }

    private static void ValidatePostState(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request,
        DeploymentTargetObservedState observed,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"PostDeploymentVerificationFailed:{observed.ObservationError ?? "post-deployment observation failed"}");
            return;
        }

        ValidateSharedObservedState(package, request, observed, issues);
        if (!Same(observed.CurrentlyDeployedVersion, package.CandidateVersion))
            issues.Add("PostDeploymentVersionMismatch");
        if (!Same(observed.CurrentlyDeployedCommitSha, package.CandidateCommitSha))
            issues.Add("PostDeploymentCommitMismatch");
        if (!Same(observed.CurrentlyDeployedArtifactSha256, package.DeploymentArtifactSha256))
            issues.Add("PostDeploymentArtifactMismatch");
    }

    private static void ValidateSharedObservedState(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request,
        DeploymentTargetObservedState observed,
        List<string> issues)
    {
        if (!Same(observed.DeploymentTarget, package.DeploymentTarget) || !Same(observed.DeploymentTarget, request.DeploymentTarget))
            issues.Add("DeploymentTargetMismatch");
        if (!Same(observed.DeploymentEnvironment, package.DeploymentEnvironment) || !Same(observed.DeploymentEnvironment, request.DeploymentEnvironment))
            issues.Add("DeploymentEnvironmentMismatch");
    }

    private static DeploymentExecutionResult Blocked(
        DeploymentReadinessDecisionPackage? package,
        DeploymentExecutionRequest request,
        DeploymentTargetObservedState? preState,
        DeploymentExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now)
    {
        var verdict = issues.Any(issue => issue.Contains("DeploymentReadinessDecisionPackageRejected", StringComparison.OrdinalIgnoreCase))
            ? DeploymentExecutionVerdict.Rejected
            : DeploymentExecutionVerdict.Blocked;
        var receipt = BuildReceipt(package, request, preState, preState, [], [], verdict, kind, issues, now, preStateVerified: false, postStateVerified: false, DeploymentExecutionBoundary.Blocked);
        return new DeploymentExecutionResult
        {
            Verdict = verdict,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static DeploymentExecutionResult FailedAfterMutation(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request,
        DeploymentTargetObservedState preState,
        DeploymentTargetObservedState postState,
        IReadOnlyList<DeploymentExecutionMutationResult> mutations,
        IReadOnlyList<DeploymentExecutionAction> completedActions,
        DeploymentExecutionVerdict verdict,
        DeploymentExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now,
        bool postStateVerified)
    {
        var receipt = BuildReceipt(package, request, preState, postState, mutations, completedActions, verdict, kind, issues, now, preStateVerified: true, postStateVerified, DeploymentExecutionBoundary.Executor);
        return new DeploymentExecutionResult
        {
            Verdict = verdict,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static DeploymentExecutionResult WithoutReceipt(
        DeploymentExecutionVerdict verdict,
        DeploymentExecutionFailureKind kind,
        string[] issues) => new()
        {
            Verdict = verdict,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = null
        };

    private static DeploymentExecutionReceipt BuildReceipt(
        DeploymentReadinessDecisionPackage? package,
        DeploymentExecutionRequest request,
        DeploymentTargetObservedState? preState,
        DeploymentTargetObservedState? postState,
        IReadOnlyList<DeploymentExecutionMutationResult> mutations,
        IReadOnlyList<DeploymentExecutionAction> completedActions,
        DeploymentExecutionVerdict verdict,
        DeploymentExecutionFailureKind failureKind,
        string[] issues,
        DateTimeOffset now,
        bool preStateVerified,
        bool postStateVerified,
        DeploymentExecutionBoundary boundary)
    {
        var packageId = package?.DeploymentReadinessDecisionPackageId ?? request.DeploymentReadinessDecisionPackageId;
        var attempted = mutations.Where(item => item.Attempted).Select(item => item.Action).Distinct().ToArray();
        var accepted = mutations.Any(item => item.Action == DeploymentExecutionAction.DeployApprovedArtifact && item.Attempted && item.Accepted);
        return new DeploymentExecutionReceipt
        {
            DeploymentExecutionReceiptId = $"deployment_exec_{BeDeploymentExecutionHashing.ShortHash($"{packageId}|{request.DeploymentExecutionRequestId}|{request.DeploymentTarget}|{request.DeploymentEnvironment}|{verdict}|{now:O}")}",
            DeploymentExecutionRequestId = FeedbackText.Safe(request.DeploymentExecutionRequestId),
            DeploymentReadinessDecisionPackageId = FeedbackText.Safe(packageId),
            Repository = FeedbackText.Safe(request.Repository),
            CandidateCommitSha = FeedbackText.Safe(request.CandidateCommitSha),
            CandidateVersion = FeedbackText.Safe(request.CandidateVersion),
            CandidateTagName = FeedbackText.Safe(request.CandidateTagName),
            ReleaseChannel = FeedbackText.Safe(request.ReleaseChannel),
            DeploymentTarget = FeedbackText.Safe(request.DeploymentTarget),
            DeploymentEnvironment = FeedbackText.Safe(request.DeploymentEnvironment),
            DeployedArtifactName = FeedbackText.Safe(request.DeploymentArtifactName),
            DeployedArtifactSha256 = FeedbackText.Safe(request.DeploymentArtifactSha256),
            ApprovedActions = request.ApprovedActions.Distinct().ToArray(),
            AttemptedActions = attempted,
            CompletedActions = completedActions.Distinct().ToArray(),
            PreDeploymentState = preState,
            PostDeploymentState = postState,
            MutationResults = mutations.ToArray(),
            PreDeploymentStateVerified = preStateVerified,
            DeploymentAttempted = mutations.Any(item => item.Attempted),
            DeploymentAccepted = accepted,
            PostDeploymentStateVerified = postStateVerified,
            PackagePublicationAttempted = false,
            MemoryPromotionAttempted = false,
            WorkflowContinuationAttempted = false,
            RollbackExecutionAttempted = false,
            SourceMutationAttempted = false,
            ExecutionVerdict = verdict,
            FailureClassification = failureKind,
            Issues = FeedbackText.SafeList(issues),
            RequestedBy = FeedbackText.Safe(request.RequestedBy),
            RequestedAtUtc = request.RequestedAtUtc ?? now,
            ExecutedAtUtc = now,
            Boundary = boundary
        };
    }

    private static DeploymentExecutionFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("MissingDeploymentReadinessDecisionPackage", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.MissingDeploymentReadinessDecisionPackage;
            if (issue.Contains("DeploymentReadinessDecisionPackageRejected", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentReadinessDecisionPackageRejected;
            if (issue.Contains("DeploymentReadinessDecisionPackageBlocked", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentReadinessDecisionPackageBlocked;
            if (issue.Contains("DeploymentReadinessDecisionBoundaryViolation", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentReadinessDecisionBoundaryViolation;
            if (issue.Contains("DeploymentReadinessDecisionPackage", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentReadinessDecisionPackageNotReady;
            if (issue.Contains("DeploymentExecutionNotConfirmed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentExecutionNotConfirmed;
            if (issue.Contains("RequestPackageMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.RequestPackageMismatch;
            if (issue.Contains("RepositoryMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.RepositoryMismatch;
            if (issue.Contains("CandidateCommitMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.CandidateCommitMismatch;
            if (issue.Contains("CandidateVersionMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.CandidateVersionMismatch;
            if (issue.Contains("CandidateTagMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.CandidateTagMismatch;
            if (issue.Contains("ReleaseChannelMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.ReleaseChannelMismatch;
            if (issue.Contains("DeploymentTargetMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentTargetMismatch;
            if (issue.Contains("DeploymentEnvironmentMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentEnvironmentMismatch;
            if (issue.Contains("DeploymentArtifactChecksumMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentArtifactChecksumMismatch;
            if (issue.Contains("DeploymentArtifactMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentArtifactMismatch;
            if (issue.Contains("MissingApprovedDeploymentAction", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.MissingApprovedDeploymentAction;
            if (issue.Contains("UnsupportedDeploymentAction", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.UnsupportedDeploymentAction;
            if (issue.Contains("DeploymentTargetObservationFailed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentTargetObservationFailed;
            if (issue.Contains("DeploymentTargetStateMismatch", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentTargetStateMismatch;
            if (issue.Contains("DeploymentAlreadyApplied", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentAlreadyApplied;
            if (issue.Contains("DeploymentInProgress", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentInProgress;
            if (issue.Contains("DeploymentTargetLocked", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.DeploymentTargetLocked;
            if (issue.Contains("PostDeployment", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.PostDeploymentVerificationFailed;
            if (issue.Contains("PackagePublicationNotAllowed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.PackagePublicationNotAllowed;
            if (issue.Contains("MemoryPromotionNotAllowed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.MemoryPromotionNotAllowed;
            if (issue.Contains("WorkflowContinuationNotAllowed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.WorkflowContinuationNotAllowed;
            if (issue.Contains("SourceMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.SourceMutationNotAllowed;
            if (issue.Contains("CommitPushNotAllowed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.CommitPushNotAllowed;
            if (issue.Contains("RollbackExecutionNotAllowed", StringComparison.OrdinalIgnoreCase)) return DeploymentExecutionFailureKind.RollbackExecutionNotAllowed;
        }

        return DeploymentExecutionFailureKind.BoundaryViolation;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
