namespace IronDev.Core.Governance;

public static class ControlledReleaseExecutor
{
    private static readonly ReleaseExecutionAction[] DeterministicActionOrder =
    [
        ReleaseExecutionAction.CreateTag,
        ReleaseExecutionAction.CreateGitHubRelease,
        ReleaseExecutionAction.UploadReleaseArtifacts
    ];

    public static async Task<ReleaseExecutionResult> ExecuteAsync(
        ReleaseReadinessDecisionPackage? package,
        ReleaseExecutionRequest? request,
        IReleaseExecutionGateway gateway,
        CancellationToken cancellationToken = default)
    {
        var now = request?.RequestedAtUtc ?? DateTimeOffset.UtcNow;
        if (request is null)
            return WithoutReceipt(ReleaseExecutionVerdict.Blocked, ReleaseExecutionFailureKind.MissingExecutionRequest, ["MissingExecutionRequest"]);

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

        var mutations = new List<ReleaseExecutionMutationResult>();
        var completed = new List<ReleaseExecutionAction>();
        foreach (var action in DeterministicActionOrder.Where(action => request.ApprovedActions.Contains(action)))
        {
            var mutation = action switch
            {
                ReleaseExecutionAction.CreateTag => await gateway.CreateTagAsync(readyPackage, request, cancellationToken).ConfigureAwait(false),
                ReleaseExecutionAction.CreateGitHubRelease => await gateway.CreateGitHubReleaseAsync(readyPackage, request, cancellationToken).ConfigureAwait(false),
                ReleaseExecutionAction.UploadReleaseArtifacts => await gateway.UploadReleaseArtifactsAsync(readyPackage, request, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported action {action}.")
            };
            mutations.Add(mutation);
            if (!mutation.Attempted || !mutation.Accepted || mutation.Action != action)
            {
                var mutationIssues = FeedbackText.SafeList(["ReleaseMutationFailed", mutation.Error ?? mutation.Message ?? string.Empty]);
                var postAfterFailure = await gateway.ObserveAsync(readyPackage, request, cancellationToken).ConfigureAwait(false);
                return FailedAfterMutation(
                    readyPackage,
                    request,
                    preState,
                    postAfterFailure,
                    mutations,
                    completed,
                    completed.Count == 0 ? ReleaseExecutionVerdict.Failed : ReleaseExecutionVerdict.PartiallyExecuted,
                    ReleaseExecutionFailureKind.ReleaseMutationFailed,
                    mutationIssues,
                    now,
                    postStateVerified: false);
            }

            completed.Add(action);
        }

        var postState = await gateway.ObserveAsync(readyPackage, request, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostState(readyPackage, request, postState, mutations, postIssues);
        if (postIssues.Count > 0)
        {
            return FailedAfterMutation(
                readyPackage,
                request,
                preState,
                postState,
                mutations,
                completed,
                ReleaseExecutionVerdict.Failed,
                ReleaseExecutionFailureKind.PostStateVerificationFailed,
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
            ReleaseExecutionVerdict.ExecutedAndVerified,
            ReleaseExecutionFailureKind.None,
            [],
            now,
            preStateVerified: true,
            postStateVerified: true,
            ReleaseExecutionBoundary.Executor);
        return new ReleaseExecutionResult
        {
            Verdict = ReleaseExecutionVerdict.ExecutedAndVerified,
            FailureKind = ReleaseExecutionFailureKind.None,
            Issues = [],
            Receipt = receipt
        };
    }

    private static void ValidatePackage(ReleaseReadinessDecisionPackage? package, List<string> issues)
    {
        if (package is null)
        {
            issues.Add("MissingReleaseReadinessPackage");
            return;
        }

        if (package.PackageVerdict == ReleaseReadinessDecisionPackageVerdict.PackageRejected)
            issues.Add("ReleaseReadinessPackageRejected");
        if (package.PackageVerdict != ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor)
            issues.Add($"ReleaseReadinessPackageNotReady:{package.PackageVerdict}");
        if (!package.CanReleaseForExecutor)
            issues.Add("ReleaseReadinessPackageCannotReleaseForExecutor");
        if (package.BlockReasons.Length > 0)
            issues.Add("ReleaseReadinessPackageBlocked");
        if (!package.Boundary.EvidenceOnly || PackageBoundaryCarriesAuthority(package.Boundary))
            issues.Add("ReleaseReadinessBoundaryAuthorityViolation");
        if (package.ReleaseReadinessDecision is null)
            issues.Add("MissingReleaseReadinessDecisionEvidence");
        if (package.ReleaseArtifactReadinessEvidence is null)
            issues.Add("MissingArtifactReadinessEvidence");
    }

    private static bool PackageBoundaryCarriesAuthority(ReleaseReadinessDecisionPackageBoundary boundary) =>
        boundary.CanMarkReadyForReview ||
        boundary.CanRequestReviewers ||
        boundary.CanRemoveReviewers ||
        boundary.CanResolveReviewThreads ||
        boundary.CanReplyToReviewThreads ||
        boundary.CanApprove ||
        boundary.CanSubmitReview ||
        boundary.CanMerge ||
        boundary.CanAutoMerge ||
        boundary.CanRelease ||
        boundary.CanDeploy ||
        boundary.CanTag ||
        boundary.CanPublish ||
        boundary.CanPromoteMemory ||
        boundary.CanContinueWorkflow ||
        boundary.CanCommit ||
        boundary.CanPush ||
        boundary.CanMutateSource ||
        boundary.CanMutateWorkspace;

    private static void ValidateRequest(ReleaseReadinessDecisionPackage? package, ReleaseExecutionRequest request, List<string> issues)
    {
        if (!request.ConfirmReleaseExecution)
            issues.Add("ReleaseExecutionNotConfirmed");
        if (string.IsNullOrWhiteSpace(request.ReleaseExecutionRequestId))
            issues.Add("MissingReleaseExecutionRequestId");
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            issues.Add("MissingReleaseExecutionRequester");

        if (package is not null)
        {
            if (!Same(request.ReleaseReadinessDecisionPackageId, package.ReleaseReadinessDecisionPackageId))
                issues.Add("RequestPackageMismatch");
            if (!Same(request.Repository, package.Repository))
                issues.Add("RepositoryMismatch");
            if (!Same(request.CandidateCommitSha, package.CandidateCommitSha))
                issues.Add("CandidateCommitMismatch");
            if (!Same(request.CandidateVersion, package.CandidateVersion))
                issues.Add("CandidateVersionMismatch");
            if (!Same(request.CandidateTagName, package.CandidateTagName))
                issues.Add("CandidateTagMismatch");
            if (!Same(request.ReleaseSourceBranch, package.ReleaseSourceBranch))
                issues.Add("ReleaseSourceBranchMismatch");
            if (!Same(request.ReleaseChannel, package.ReleaseChannel))
                issues.Add("ReleaseChannelMismatch");
        }

        ValidateActions(request, issues);
        ValidateReleaseNotes(request, issues);
        ValidateArtifacts(package, request, issues);
    }

    private static void ValidateActions(ReleaseExecutionRequest request, List<string> issues)
    {
        if (request.ApprovedActions.Length == 0)
        {
            issues.Add("MissingApprovedActions");
            return;
        }

        foreach (var action in request.ApprovedActions)
        {
            if (!Enum.IsDefined(action))
                issues.Add($"UnsupportedApprovedAction:{action}");
        }

        var actions = request.ApprovedActions.Distinct().ToArray();
        if (actions.Contains(ReleaseExecutionAction.CreateGitHubRelease) &&
            !actions.Contains(ReleaseExecutionAction.CreateTag))
        {
            issues.Add("ReleaseCreationRequiresTagCreation");
        }

        if (actions.Contains(ReleaseExecutionAction.UploadReleaseArtifacts) &&
            !actions.Contains(ReleaseExecutionAction.CreateGitHubRelease))
        {
            issues.Add("ArtifactUploadRequiresReleaseCreation");
        }
    }

    private static void ValidateReleaseNotes(ReleaseExecutionRequest request, List<string> issues)
    {
        if (!request.ApprovedActions.Contains(ReleaseExecutionAction.CreateGitHubRelease))
            return;

        var body = request.ReleaseNotesBody;
        if (string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(request.ReleaseNotesPath) && File.Exists(request.ReleaseNotesPath))
            body = File.ReadAllText(request.ReleaseNotesPath);

        if (string.IsNullOrWhiteSpace(body))
        {
            issues.Add("MissingReleaseNotes");
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ReleaseNotesSha256) &&
            !Same(BbReleaseExecutionHashing.TextHash(body), request.ReleaseNotesSha256))
        {
            issues.Add("ReleaseNotesChecksumMismatch");
        }
    }

    private static void ValidateArtifacts(ReleaseReadinessDecisionPackage? package, ReleaseExecutionRequest request, List<string> issues)
    {
        if (!request.ApprovedActions.Contains(ReleaseExecutionAction.UploadReleaseArtifacts))
            return;

        var readiness = package?.ReleaseArtifactReadinessEvidence;
        if (readiness is null || !readiness.ArtifactsRequired || !readiness.ArtifactsReady)
        {
            issues.Add("ArtifactUploadRequiresReadyBAArtifactEvidence");
        }

        if (request.Artifacts.Length == 0)
        {
            issues.Add("MissingArtifact");
            return;
        }

        if (readiness is not null)
            ValidateArtifactsAuthorizedByPackage(readiness, request, issues);

        foreach (var artifact in request.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Name) || string.IsNullOrWhiteSpace(artifact.Path) || !File.Exists(artifact.Path))
            {
                issues.Add($"MissingArtifact:{artifact.Name}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(artifact.Sha256) &&
                !Same(BbReleaseExecutionHashing.FileHash(artifact.Path), artifact.Sha256))
            {
                issues.Add($"ArtifactChecksumMismatch:{artifact.Name}");
            }
        }
    }

    private static void ValidateArtifactsAuthorizedByPackage(
        ReleaseArtifactReadinessEvidence readiness,
        ReleaseExecutionRequest request,
        List<string> issues)
    {
        var packageArtifacts = readiness.Artifacts
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select((item, index) => new
            {
                Name = item.Trim(),
                Checksum = index < readiness.Checksums.Length ? readiness.Checksums[index] : null
            })
            .ToArray();
        var packageNames = new HashSet<string>(packageArtifacts.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        var requestNames = new HashSet<string>(request.Artifacts.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in request.Artifacts)
        {
            var packageArtifact = packageArtifacts.FirstOrDefault(item => Same(item.Name, artifact.Name));
            if (packageArtifact is null)
            {
                issues.Add($"ArtifactNotAuthorizedByReleaseReadinessPackage:{artifact.Name}");
                continue;
            }

            var requestedChecksum = !string.IsNullOrWhiteSpace(artifact.Sha256)
                ? artifact.Sha256
                : File.Exists(artifact.Path)
                    ? BbReleaseExecutionHashing.FileHash(artifact.Path)
                    : null;
            if (string.IsNullOrWhiteSpace(packageArtifact.Checksum) ||
                string.IsNullOrWhiteSpace(requestedChecksum) ||
                !Same(packageArtifact.Checksum, requestedChecksum))
            {
                issues.Add($"ArtifactChecksumMismatch:{artifact.Name}");
            }
        }

        foreach (var packageArtifactName in packageNames)
        {
            if (!requestNames.Contains(packageArtifactName))
                issues.Add($"MissingArtifact:{packageArtifactName}");
        }
    }

    private static void ValidateBoundary(List<string> issues)
    {
        var boundary = ReleaseExecutionBoundary.Executor;
        if (!boundary.CanCreateTag || !boundary.CanCreateGitHubRelease || !boundary.CanUploadReleaseArtifacts)
            issues.Add("ReleaseMutationNotAllowed");
        if (boundary.CanDeploy)
            issues.Add("DeployNotAllowed");
        if (boundary.CanPublishPackages)
            issues.Add("PublishPackagesNotAllowed");
        if (boundary.CanPromoteMemory)
            issues.Add("MemoryPromotionNotAllowed");
        if (boundary.CanContinueWorkflow)
            issues.Add("WorkflowContinuationNotAllowed");
        if (boundary.CanCommit || boundary.CanPush)
            issues.Add("CommitPushNotAllowed");
        if (boundary.CanMerge)
            issues.Add("MergeNotAllowed");
        if (boundary.CanMutateSource || boundary.CanMutateWorkspace)
            issues.Add("SourceMutationNotAllowed");
        if (boundary.CanExecuteRollback)
            issues.Add("RollbackExecutionNotAllowed");
        if (boundary.CanApprove || boundary.CanMarkReadyForReview || boundary.CanRequestReviewers)
            issues.Add("ReviewAuthorityNotAllowed");
    }

    private static void ValidatePreState(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        ReleaseExecutionObservedState observed,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"ObservationFailed:{observed.ObservationError ?? "pre-state observation failed"}");
            return;
        }

        ValidateSharedObservedState(package, request, observed, issues);
        if (!Same(observed.ReleaseSourceHeadSha, package.CandidateCommitSha) ||
            !Same(observed.CandidateCommitSha, package.CandidateCommitSha) ||
            !observed.CommitPresentOnReleaseSource)
        {
            issues.Add("SourceBranchMoved");
        }

        if (observed.ExistingTagFound)
            issues.Add("CandidateTagAlreadyExists");
        if (observed.ExistingReleaseFound)
            issues.Add("CandidateReleaseAlreadyExists");
    }

    private static void ValidatePostState(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        ReleaseExecutionObservedState observed,
        IReadOnlyList<ReleaseExecutionMutationResult> mutations,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"PostStateVerificationFailed:{observed.ObservationError ?? "post-state observation failed"}");
            return;
        }

        ValidateSharedObservedState(package, request, observed, issues);
        if (request.ApprovedActions.Contains(ReleaseExecutionAction.CreateTag))
        {
            if (!observed.ExistingTagFound)
                issues.Add("PostStateTagMissing");
            if (!string.IsNullOrWhiteSpace(observed.ExistingTagSha) && !Same(observed.ExistingTagSha, package.CandidateCommitSha))
                issues.Add("PostStateTagShaMismatch");
        }

        if (request.ApprovedActions.Contains(ReleaseExecutionAction.CreateGitHubRelease) && !observed.ExistingReleaseFound)
            issues.Add("PostStateReleaseMissing");

        if (request.ApprovedActions.Contains(ReleaseExecutionAction.UploadReleaseArtifacts))
        {
            var observedArtifacts = new HashSet<string>(observed.ExistingReleaseArtifactNames, StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in request.Artifacts.Select(item => item.Name))
            {
                if (!observedArtifacts.Contains(artifact))
                    issues.Add($"PostStateArtifactMissing:{artifact}");
            }
        }
    }

    private static void ValidateSharedObservedState(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        ReleaseExecutionObservedState observed,
        List<string> issues)
    {
        if (!Same(observed.Repository, package.Repository) || !Same(observed.Repository, request.Repository))
            issues.Add("RepositoryMismatch");
        if (!Same(observed.ReleaseSourceBranch, package.ReleaseSourceBranch) || !Same(observed.ReleaseSourceBranch, request.ReleaseSourceBranch))
            issues.Add("ReleaseSourceBranchMismatch");
        if (!Same(observed.CandidateTagName, package.CandidateTagName) || !Same(observed.CandidateTagName, request.CandidateTagName))
            issues.Add("CandidateTagMismatch");
    }

    private static ReleaseExecutionResult Blocked(
        ReleaseReadinessDecisionPackage? package,
        ReleaseExecutionRequest request,
        ReleaseExecutionObservedState? preState,
        ReleaseExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now)
    {
        var verdict = issues.Any(issue => issue.Contains("ReleaseReadinessPackageRejected", StringComparison.OrdinalIgnoreCase))
            ? ReleaseExecutionVerdict.Rejected
            : ReleaseExecutionVerdict.Blocked;
        var receipt = BuildReceipt(package, request, preState, preState, [], [], verdict, kind, issues, now, preStateVerified: false, postStateVerified: false, ReleaseExecutionBoundary.Blocked);
        return new ReleaseExecutionResult
        {
            Verdict = verdict,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static ReleaseExecutionResult FailedAfterMutation(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        ReleaseExecutionObservedState preState,
        ReleaseExecutionObservedState postState,
        IReadOnlyList<ReleaseExecutionMutationResult> mutations,
        IReadOnlyList<ReleaseExecutionAction> completedActions,
        ReleaseExecutionVerdict verdict,
        ReleaseExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now,
        bool postStateVerified)
    {
        var receipt = BuildReceipt(package, request, preState, postState, mutations, completedActions, verdict, kind, issues, now, preStateVerified: true, postStateVerified, ReleaseExecutionBoundary.Executor);
        return new ReleaseExecutionResult
        {
            Verdict = verdict,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static ReleaseExecutionResult WithoutReceipt(
        ReleaseExecutionVerdict verdict,
        ReleaseExecutionFailureKind kind,
        string[] issues) => new()
        {
            Verdict = verdict,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = null
        };

    private static ReleaseExecutionReceipt BuildReceipt(
        ReleaseReadinessDecisionPackage? package,
        ReleaseExecutionRequest request,
        ReleaseExecutionObservedState? preState,
        ReleaseExecutionObservedState? postState,
        IReadOnlyList<ReleaseExecutionMutationResult> mutations,
        IReadOnlyList<ReleaseExecutionAction> completedActions,
        ReleaseExecutionVerdict verdict,
        ReleaseExecutionFailureKind failureKind,
        string[] issues,
        DateTimeOffset now,
        bool preStateVerified,
        bool postStateVerified,
        ReleaseExecutionBoundary boundary)
    {
        var packageId = package?.ReleaseReadinessDecisionPackageId ?? request.ReleaseReadinessDecisionPackageId;
        var releaseMutation = mutations.LastOrDefault(item => item.Action == ReleaseExecutionAction.CreateGitHubRelease);
        var tagMutation = mutations.LastOrDefault(item => item.Action == ReleaseExecutionAction.CreateTag);
        var uploadMutation = mutations.LastOrDefault(item => item.Action == ReleaseExecutionAction.UploadReleaseArtifacts);
        return new ReleaseExecutionReceipt
        {
            ReleaseExecutionId = $"release_exec_{BbReleaseExecutionHashing.ShortHash($"{packageId}|{request.ReleaseExecutionRequestId}|{request.CandidateTagName}|{verdict}|{now:O}")}",
            ReleaseExecutionRequestId = FeedbackText.Safe(request.ReleaseExecutionRequestId),
            ReleaseReadinessDecisionPackageId = FeedbackText.Safe(packageId),
            Repository = FeedbackText.Safe(request.Repository),
            ReleaseSourceBranch = FeedbackText.Safe(request.ReleaseSourceBranch),
            CandidateCommitSha = FeedbackText.Safe(request.CandidateCommitSha),
            CandidateVersion = FeedbackText.Safe(request.CandidateVersion),
            CandidateTagName = FeedbackText.Safe(request.CandidateTagName),
            ReleaseChannel = FeedbackText.Safe(request.ReleaseChannel),
            PreState = preState,
            PostState = postState,
            ApprovedActions = request.ApprovedActions.Distinct().ToArray(),
            CompletedActions = completedActions.Distinct().ToArray(),
            MutationResults = mutations.ToArray(),
            PreStateVerified = preStateVerified,
            TagCreated = completedActions.Contains(ReleaseExecutionAction.CreateTag),
            GitHubReleaseCreated = completedActions.Contains(ReleaseExecutionAction.CreateGitHubRelease),
            ReleaseArtifactsUploaded = completedActions.Contains(ReleaseExecutionAction.UploadReleaseArtifacts),
            CreatedTagSha = FeedbackText.SafeOrNull(postState?.ExistingTagSha ?? tagMutation?.ResourceId),
            GitHubReleaseId = FeedbackText.SafeOrNull(postState?.ExistingReleaseId ?? releaseMutation?.ResourceId),
            GitHubReleaseUrl = FeedbackText.SafeOrNull(postState?.ExistingReleaseUrl ?? releaseMutation?.ResourceUrl),
            UploadedArtifacts = FeedbackText.SafeList(postState?.ExistingReleaseArtifactNames.Length > 0
                ? postState.ExistingReleaseArtifactNames
                : uploadMutation?.UploadedArtifacts ?? []),
            PostStateVerified = postStateVerified,
            DeploymentAttempted = false,
            PackagePublicationAttempted = false,
            MemoryPromotionAttempted = false,
            WorkflowContinuationAttempted = false,
            RollbackExecutionAttempted = false,
            ExecutionVerdict = verdict,
            FailureClassification = failureKind,
            Issues = FeedbackText.SafeList(issues),
            RequestedBy = FeedbackText.Safe(request.RequestedBy),
            RequestedAtUtc = request.RequestedAtUtc ?? now,
            ExecutedAtUtc = now,
            Boundary = boundary
        };
    }

    private static ReleaseExecutionFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("MissingReleaseReadinessPackage", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.MissingReleaseReadinessPackage;
            if (issue.Contains("ReleaseReadinessPackageRejected", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseReadinessPackageRejected;
            if (issue.Contains("ReleaseReadinessPackageBlocked", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseReadinessPackageBlocked;
            if (issue.Contains("ReleaseReadinessBoundaryAuthorityViolation", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseReadinessBoundaryAuthorityViolation;
            if (issue.Contains("ArtifactNotAuthorizedByReleaseReadinessPackage", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ArtifactNotAuthorizedByReleaseReadinessPackage;
            if (issue.Contains("ReleaseReadinessPackage", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseReadinessPackageNotReady;
            if (issue.Contains("ReleaseExecutionNotConfirmed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseExecutionNotConfirmed;
            if (issue.Contains("RequestPackageMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.RequestPackageMismatch;
            if (issue.Contains("RepositoryMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.RepositoryMismatch;
            if (issue.Contains("CandidateCommitMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.CandidateCommitMismatch;
            if (issue.Contains("CandidateVersionMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.CandidateVersionMismatch;
            if (issue.Contains("CandidateTagMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.CandidateTagMismatch;
            if (issue.Contains("ReleaseSourceBranchMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseSourceBranchMismatch;
            if (issue.Contains("ReleaseChannelMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseChannelMismatch;
            if (issue.Contains("MissingApprovedActions", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.MissingApprovedActions;
            if (issue.Contains("UnsupportedApprovedAction", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.UnsupportedApprovedAction;
            if (issue.Contains("ReleaseCreationRequiresTagCreation", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseCreationRequiresTagCreation;
            if (issue.Contains("ArtifactUploadRequiresReleaseCreation", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ArtifactUploadRequiresReleaseCreation;
            if (issue.Contains("MissingReleaseNotes", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.MissingReleaseNotes;
            if (issue.Contains("ReleaseNotesChecksumMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ReleaseNotesChecksumMismatch;
            if (issue.Contains("MissingArtifact", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.MissingArtifact;
            if (issue.Contains("ArtifactChecksumMismatch", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ArtifactChecksumMismatch;
            if (issue.Contains("ObservationFailed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.ObservationFailed;
            if (issue.Contains("SourceBranchMoved", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.SourceBranchMoved;
            if (issue.Contains("CandidateTagAlreadyExists", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.CandidateTagAlreadyExists;
            if (issue.Contains("CandidateReleaseAlreadyExists", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.CandidateReleaseAlreadyExists;
            if (issue.Contains("PostStateVerificationFailed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.PostStateVerificationFailed;
            if (issue.Contains("DeployNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.DeployNotAllowed;
            if (issue.Contains("PublishPackagesNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.PublishPackagesNotAllowed;
            if (issue.Contains("MemoryPromotionNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.MemoryPromotionNotAllowed;
            if (issue.Contains("WorkflowContinuationNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.WorkflowContinuationNotAllowed;
            if (issue.Contains("CommitPushNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.CommitPushNotAllowed;
            if (issue.Contains("SourceMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.SourceMutationNotAllowed;
            if (issue.Contains("RollbackExecutionNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReleaseExecutionFailureKind.RollbackExecutionNotAllowed;
        }

        return ReleaseExecutionFailureKind.BoundaryViolation;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
