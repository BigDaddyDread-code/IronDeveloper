namespace IronDev.Core.Governance;

public static class InterruptedRunReadModelAssembler
{
    public static InterruptedRunReadModel Assemble(InterruptedRunReadModelRequest? request)
    {
        var validation = InterruptedRunReadModelValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                request?.AsOfUtc ?? default,
                validation.Issues);
        }

        if (request.Checkpoints.Count == 0)
        {
            return Result(
                request,
                InterruptedRunReadModelStatus.NoCheckpoints,
                null,
                [],
                []);
        }

        var ambiguous = FindAmbiguity(request);
        if (ambiguous.Count > 0)
        {
            var checkpoint = LastCheckpoint(request.Checkpoints);
            var assessment = BuildAssessment(
                request,
                checkpoint,
                InterruptedRunStateKind.Ambiguous,
                InterruptedRunGapKind.Ambiguous,
                "AmbiguousInterruptedRunCheckpoints");

            return Result(
                request,
                InterruptedRunReadModelStatus.AmbiguousCheckpoints,
                assessment,
                ambiguous,
                []);
        }

        var resolved = Resolve(request);
        return Result(
            request,
            resolved.Status,
            resolved.Assessment,
            [],
            []);
    }

    private static (InterruptedRunReadModelStatus Status, InterruptedRunAssessment Assessment) Resolve(
        InterruptedRunReadModelRequest request)
    {
        var checkpoints = request.Checkpoints.ToArray();
        var kinds = checkpoints.Select(static checkpoint => checkpoint.CheckpointKind).ToHashSet();

        if (kinds.Contains(InterruptedRunCheckpointKind.Completed))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.Completed);
            return (
                InterruptedRunReadModelStatus.NoInterruptionObserved,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.NoInterruptionObserved, InterruptedRunGapKind.NoneObserved, "CompletedCheckpointObserved"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.Cancelled))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.Cancelled);
            return (
                InterruptedRunReadModelStatus.Cancelled,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Cancelled, InterruptedRunGapKind.Cancelled, "CancelledCheckpointObserved"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.Failed))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.Failed);
            return (
                InterruptedRunReadModelStatus.Failed,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Failed, InterruptedRunGapKind.Failed, "FailedCheckpointObserved"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.ValidationFailed))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.ValidationFailed);
            return (
                InterruptedRunReadModelStatus.Interrupted,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.ValidationFailed, "ValidationFailedCheckpointObserved"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.SourceApplyStarted) &&
            !kinds.Contains(InterruptedRunCheckpointKind.SourceApplyCompleted))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.SourceApplyStarted);
            return (
                InterruptedRunReadModelStatus.Interrupted,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.ApplyStartedNotCompleted, "SourceApplyStartedWithoutCompletion"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.CommitPackageCreated) &&
            !kinds.Contains(InterruptedRunCheckpointKind.CommitCreated))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.CommitPackageCreated);
            return (
                InterruptedRunReadModelStatus.Interrupted,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.CommitPackageCreatedNoCommit, "CommitPackageCreatedWithoutCommit"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.CommitCreated) &&
            !kinds.Contains(InterruptedRunCheckpointKind.PushCompleted))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.CommitCreated);
            return (
                InterruptedRunReadModelStatus.Interrupted,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.CommitCreatedNoPush, "CommitCreatedWithoutPush"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.PushCompleted) &&
            !kinds.Contains(InterruptedRunCheckpointKind.PullRequestCreated))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.PushCompleted);
            return (
                InterruptedRunReadModelStatus.Interrupted,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.PushCompletedNoPullRequest, "PushCompletedWithoutPullRequest"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.PatchArtifactCreated) &&
            !kinds.Contains(InterruptedRunCheckpointKind.ValidationStarted) &&
            !kinds.Contains(InterruptedRunCheckpointKind.ValidationPassed) &&
            !kinds.Contains(InterruptedRunCheckpointKind.ValidationFailed))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.PatchArtifactCreated);
            return (
                InterruptedRunReadModelStatus.Interrupted,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.PatchCreatedNoValidation, "PatchArtifactCreatedWithoutValidation"));
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.WorkspaceCreated) &&
            !kinds.Contains(InterruptedRunCheckpointKind.PatchArtifactCreated))
        {
            var checkpoint = LastOfKind(checkpoints, InterruptedRunCheckpointKind.WorkspaceCreated);
            return (
                InterruptedRunReadModelStatus.Interrupted,
                BuildAssessment(request, checkpoint, InterruptedRunStateKind.Interrupted, InterruptedRunGapKind.WorkspaceCreatedNoPatch, "WorkspaceCreatedWithoutPatch"));
        }

        var last = LastCheckpoint(checkpoints);
        return (
            InterruptedRunReadModelStatus.NoInterruptionObserved,
            BuildAssessment(request, last, InterruptedRunStateKind.NoInterruptionObserved, InterruptedRunGapKind.NoneObserved, "NoInterruptedRunGapObserved"));
    }

    private static IReadOnlyList<string> FindAmbiguity(InterruptedRunReadModelRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateValues(
            request.Checkpoints.Select(static checkpoint => checkpoint.CheckpointId),
            "DuplicateInterruptedRunCheckpointId",
            ambiguous);
        AddDuplicateValues(
            request.Checkpoints.Select(static checkpoint => checkpoint.AppendPosition.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "DuplicateInterruptedRunCheckpointAppendPosition",
            ambiguous);

        foreach (var group in request.Checkpoints.GroupBy(static checkpoint => checkpoint.CheckpointId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(CheckpointFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingInterruptedRunCheckpointMetadata:{group.Key}");
            }
        }

        var kinds = request.Checkpoints.Select(static checkpoint => checkpoint.CheckpointKind).ToHashSet();
        var terminals = new[]
        {
            InterruptedRunCheckpointKind.Completed,
            InterruptedRunCheckpointKind.Failed,
            InterruptedRunCheckpointKind.Cancelled
        }.Where(kinds.Contains).ToArray();

        if (terminals.Length > 1)
        {
            ambiguous.Add("ContradictoryInterruptedRunTerminalStates");
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.Completed) &&
            kinds.Contains(InterruptedRunCheckpointKind.SourceApplyStarted) &&
            !kinds.Contains(InterruptedRunCheckpointKind.SourceApplyCompleted))
        {
            ambiguous.Add("CompletedWithIncompleteSourceApply");
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.ValidationFailed) &&
            HasDownstreamAfterValidation(kinds))
        {
            ambiguous.Add("ValidationFailedWithDownstreamMutationEvidence");
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.CommitCreated) &&
            !kinds.Contains(InterruptedRunCheckpointKind.CommitPackageCreated))
        {
            ambiguous.Add("CommitCreatedWithoutCommitPackage");
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.PushCompleted) &&
            !kinds.Contains(InterruptedRunCheckpointKind.CommitCreated))
        {
            ambiguous.Add("PushCompletedWithoutCommit");
        }

        if (kinds.Contains(InterruptedRunCheckpointKind.PullRequestCreated) &&
            !kinds.Contains(InterruptedRunCheckpointKind.PushCompleted))
        {
            ambiguous.Add("PullRequestCreatedWithoutPush");
        }

        return ambiguous
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasDownstreamAfterValidation(ISet<InterruptedRunCheckpointKind> kinds) =>
        kinds.Contains(InterruptedRunCheckpointKind.SourceApplyStarted) ||
        kinds.Contains(InterruptedRunCheckpointKind.SourceApplyCompleted) ||
        kinds.Contains(InterruptedRunCheckpointKind.CommitPackageCreated) ||
        kinds.Contains(InterruptedRunCheckpointKind.CommitCreated) ||
        kinds.Contains(InterruptedRunCheckpointKind.PushCompleted) ||
        kinds.Contains(InterruptedRunCheckpointKind.PullRequestCreated) ||
        kinds.Contains(InterruptedRunCheckpointKind.Completed);

    private static void AddDuplicateValues(
        IEnumerable<string> values,
        string issuePrefix,
        ICollection<string> issues)
    {
        foreach (var duplicate in values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static value => value, StringComparer.Ordinal))
        {
            issues.Add($"{issuePrefix}:{duplicate}");
        }
    }

    private static string CheckpointFingerprint(InterruptedRunCheckpointObservation checkpoint) =>
        string.Join(
            "|",
            checkpoint.TenantId,
            checkpoint.ProjectId,
            checkpoint.OperationId,
            checkpoint.CorrelationId,
            checkpoint.CheckpointId,
            checkpoint.CheckpointKind,
            checkpoint.AppendPosition,
            checkpoint.ObservedAtUtc.ToUnixTimeMilliseconds(),
            checkpoint.RecordedAtUtc.ToUnixTimeMilliseconds(),
            checkpoint.SurfaceKind,
            checkpoint.SurfaceId,
            checkpoint.ReferenceKind,
            checkpoint.ReferenceId ?? string.Empty,
            checkpoint.Source,
            checkpoint.IsRedacted,
            checkpoint.RedactionReason ?? string.Empty);

    private static InterruptedRunCheckpointObservation LastCheckpoint(IEnumerable<InterruptedRunCheckpointObservation> checkpoints) =>
        checkpoints
            .OrderBy(static checkpoint => checkpoint.AppendPosition)
            .ThenBy(static checkpoint => checkpoint.ObservedAtUtc)
            .ThenBy(static checkpoint => checkpoint.CheckpointId, StringComparer.Ordinal)
            .Last();

    private static InterruptedRunCheckpointObservation LastOfKind(
        IEnumerable<InterruptedRunCheckpointObservation> checkpoints,
        InterruptedRunCheckpointKind kind) =>
        LastCheckpoint(checkpoints.Where(checkpoint => checkpoint.CheckpointKind == kind));

    private static InterruptedRunAssessment BuildAssessment(
        InterruptedRunReadModelRequest request,
        InterruptedRunCheckpointObservation checkpoint,
        InterruptedRunStateKind stateKind,
        InterruptedRunGapKind gapKind,
        string reason) =>
        new()
        {
            StateKind = stateKind,
            GapKind = gapKind,
            LastCheckpointId = checkpoint.CheckpointId,
            LastCheckpointKind = checkpoint.CheckpointKind,
            LastCheckpointObservedAtUtc = checkpoint.ObservedAtUtc,
            LastCheckpointRecordedAtUtc = checkpoint.RecordedAtUtc,
            DiagnosticSummary = DiagnosticSummary(request.DiagnosticSnapshot),
            Reason = reason,
            SurfaceKind = checkpoint.SurfaceKind,
            SurfaceId = checkpoint.SurfaceId,
            ReferenceKind = checkpoint.ReferenceKind,
            ReferenceId = checkpoint.ReferenceId,
            IsRedacted = checkpoint.IsRedacted
        };

    private static string? DiagnosticSummary(InterruptedRunDiagnosticSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return string.Join(
            "; ",
            $"projected={snapshot.ProjectedStatusKind}",
            $"missingEvidence={snapshot.MissingEvidenceStatus}",
            $"forbiddenActions={snapshot.ForbiddenActionStatus}",
            $"receipt={snapshot.ReceiptResolutionStatus}",
            $"evidence={snapshot.EvidenceResolutionStatus}",
            $"validation={snapshot.ValidationStalenessStatus}",
            $"patchBase={snapshot.PatchBaseFreshnessStatus}",
            $"worktreeBaseHead={snapshot.WorktreeBaseHeadFreshnessStatus}");
    }

    private static InterruptedRunReadModel InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = InterruptedRunReadModelStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessment = null,
            CheckpointIds = [],
            AmbiguousCheckpoints = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = [],
            ForbiddenAuthorityImplications = InterruptedRunReadModelValidator.ForbiddenAuthorityImplications
        };

    private static InterruptedRunReadModel Result(
        InterruptedRunReadModelRequest request,
        InterruptedRunReadModelStatus status,
        InterruptedRunAssessment? assessment,
        IReadOnlyList<string> ambiguous,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = true,
            ResolutionStatus = status,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            AsOfUtc = request.AsOfUtc,
            Assessment = assessment,
            CheckpointIds = request.Checkpoints
                .Select(static checkpoint => checkpoint.CheckpointId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static checkpointId => checkpointId, StringComparer.Ordinal)
                .ToArray(),
            AmbiguousCheckpoints = ambiguous,
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = InterruptedRunReadModelValidator.Warnings(),
            ForbiddenAuthorityImplications = InterruptedRunReadModelValidator.ForbiddenAuthorityImplications
        };
}
