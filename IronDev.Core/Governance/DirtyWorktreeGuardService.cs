namespace IronDev.Core.Governance;

public sealed class DirtyWorktreeGuardService
{
    public DirtyWorktreeGuardDecision Evaluate(DirtyWorktreeGuardRequest request)
    {
        var validation = DirtyWorktreeGuardValidator.ValidateRequest(request);
        if (validation.HasUnsafePayload)
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUnsafePayload,
                DirtyWorktreeGuardBlockKind.UnsafePayload,
                validation,
                validation.Issues.FirstOrDefault() ?? "DirtyWorktreeGuardUnsafePayloadRejected");
        }

        if (validation.Issues.Count > 0)
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.Invalid,
                DirtyWorktreeGuardBlockKind.InvalidRequest,
                validation,
                validation.Issues.First());
        }

        var dimensionBlock = ValidateDimensions(request, validation);
        if (dimensionBlock is not null)
        {
            return dimensionBlock;
        }

        var freshnessBlock = ValidateFreshness(request, validation);
        if (freshnessBlock is not null)
        {
            return freshnessBlock;
        }

        var trustBlock = ValidateTrust(request, validation);
        if (trustBlock is not null)
        {
            return trustBlock;
        }

        var missingEvidence = RequiredEvidenceIssues(request);
        if (missingEvidence.Count > 0)
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence,
                DirtyWorktreeGuardBlockKind.MissingEvidence,
                validation,
                missingEvidence.First());
        }

        var consistencyIssue = ConsistencyIssue(request);
        if (consistencyIssue is not null)
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByInconsistentWorktreeEvidence,
                DirtyWorktreeGuardBlockKind.InconsistentEvidence,
                validation,
                consistencyIssue);
        }

        if (IsUnknownState(request.WorktreeState))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUnknownWorktreeState,
                DirtyWorktreeGuardBlockKind.UnknownWorktreeState,
                validation,
                $"DirtyWorktreeGuardUnknownState:{request.WorktreeState}");
        }

        if (IsDirtyState(request.WorktreeState))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByDirtyWorktree,
                DirtyWorktreeGuardBlockKind.DirtyWorktree,
                validation,
                $"DirtyWorktreeGuardDirtyState:{request.WorktreeState}");
        }

        if (!CanProceedToNextGate(request))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence,
                DirtyWorktreeGuardBlockKind.UntrustedEvidence,
                validation,
                "DirtyWorktreeGuardEvidenceCannotProceedToAuthorityGate");
        }

        return Decision(
            request,
            DirtyWorktreeGuardDecisionKind.MayProceedToNextAuthorityGate,
            DirtyWorktreeGuardBlockKind.None,
            validation,
            "DirtyWorktreeGuardCleanEvidenceMayProceedToNextAuthorityGate");
    }

    private static DirtyWorktreeGuardDecision? ValidateDimensions(
        DirtyWorktreeGuardRequest request,
        DirtyWorktreeGuardValidationResult validation)
    {
        if (request.SubjectKind == DirtyWorktreeGuardSubjectKind.Unknown ||
            !Enum.IsDefined(request.SubjectKind))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.Invalid,
                DirtyWorktreeGuardBlockKind.InvalidRequest,
                validation,
                "DirtyWorktreeGuardSubjectKindUnknown");
        }

        if (request.WorktreeState == DirtyWorktreeState.Unknown ||
            !Enum.IsDefined(request.WorktreeState))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUnknownWorktreeState,
                DirtyWorktreeGuardBlockKind.UnknownWorktreeState,
                validation,
                "DirtyWorktreeGuardWorktreeStateUnknown");
        }

        if (request.EvidenceKind == DirtyWorktreeEvidenceKind.Unknown ||
            !Enum.IsDefined(request.EvidenceKind))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence,
                DirtyWorktreeGuardBlockKind.UntrustedEvidence,
                validation,
                "DirtyWorktreeGuardEvidenceKindUnknown");
        }

        if (request.EvidenceTrustLevel == DirtyWorktreeEvidenceTrustLevel.Unknown ||
            !Enum.IsDefined(request.EvidenceTrustLevel))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence,
                DirtyWorktreeGuardBlockKind.UntrustedEvidence,
                validation,
                "DirtyWorktreeGuardEvidenceTrustLevelUnknown");
        }

        if (request.ObservationFreshness == DirtyWorktreeObservationFreshness.Unknown ||
            !Enum.IsDefined(request.ObservationFreshness))
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByStaleWorktreeObservation,
                DirtyWorktreeGuardBlockKind.StaleObservation,
                validation,
                "DirtyWorktreeGuardObservationFreshnessUnknown");
        }

        return null;
    }

    private static DirtyWorktreeGuardDecision? ValidateFreshness(
        DirtyWorktreeGuardRequest request,
        DirtyWorktreeGuardValidationResult validation)
    {
        if (request.ObservationFreshness == DirtyWorktreeObservationFreshness.NotTimestamped ||
            request.ObservationFreshness == DirtyWorktreeObservationFreshness.Stale ||
            (request.RecordedAtUtc - request.ObservedAtUtc).TotalSeconds > DirtyWorktreeGuardValidator.MaxObservationAgeSeconds)
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByStaleWorktreeObservation,
                DirtyWorktreeGuardBlockKind.StaleObservation,
                validation,
                "DirtyWorktreeGuardObservationStale");
        }

        if (request.ObservationFreshness == DirtyWorktreeObservationFreshness.Expired ||
            request.EvidenceExpiresAtUtc is { } expiresAt && expiresAt <= request.RecordedAtUtc)
        {
            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByExpiredWorktreeObservation,
                DirtyWorktreeGuardBlockKind.ExpiredObservation,
                validation,
                "DirtyWorktreeGuardObservationExpired");
        }

        return null;
    }

    private static DirtyWorktreeGuardDecision? ValidateTrust(
        DirtyWorktreeGuardRequest request,
        DirtyWorktreeGuardValidationResult validation)
    {
        if (request.EvidenceKind == DirtyWorktreeEvidenceKind.SyntheticTestObservation ||
            request.EvidenceTrustLevel == DirtyWorktreeEvidenceTrustLevel.TestFixture)
        {
            if (!request.Source.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(
                    request,
                    DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence,
                    DirtyWorktreeGuardBlockKind.UntrustedEvidence,
                    validation,
                    "DirtyWorktreeGuardTestFixtureSourceRequired");
            }

            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence,
                DirtyWorktreeGuardBlockKind.UntrustedEvidence,
                validation,
                "DirtyWorktreeGuardTestFixtureCannotProceedToAuthorityGate");
        }

        if (request.EvidenceTrustLevel == DirtyWorktreeEvidenceTrustLevel.SelfReported)
        {
            if (!HasCorroboratingEvidence(request))
            {
                return Decision(
                    request,
                    DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence,
                    DirtyWorktreeGuardBlockKind.MissingEvidence,
                    validation,
                    "DirtyWorktreeGuardSelfReportedCorroborationRequired");
            }

            return Decision(
                request,
                DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence,
                DirtyWorktreeGuardBlockKind.UntrustedEvidence,
                validation,
                "DirtyWorktreeGuardSelfReportedCannotProceedToAuthorityGate");
        }

        return null;
    }

    private static IReadOnlyList<string> RequiredEvidenceIssues(DirtyWorktreeGuardRequest request)
    {
        var missing = new List<string>();

        if (request.WorktreeState == DirtyWorktreeState.Clean)
        {
            Require(request.WorktreeObservationRef, "DirtyWorktreeGuardWorktreeObservationRefRequired", missing);
            Require(request.ObservedHeadRef, "DirtyWorktreeGuardObservedHeadRefRequired", missing);
            Require(request.ObservedBranchRef, "DirtyWorktreeGuardObservedBranchRefRequired", missing);
            Require(request.ObservedWorktreeFingerprint, "DirtyWorktreeGuardObservedWorktreeFingerprintRequired", missing);
        }

        if (request.EvidenceKind == DirtyWorktreeEvidenceKind.PostStateObservation ||
            request.EvidenceTrustLevel == DirtyWorktreeEvidenceTrustLevel.PostStateObservationBacked)
        {
            Require(request.PostStateObservationRef, "DirtyWorktreeGuardPostStateObservationRefRequired", missing);
        }

        if (request.EvidenceKind == DirtyWorktreeEvidenceKind.ReceiptBackedObservation ||
            request.EvidenceTrustLevel == DirtyWorktreeEvidenceTrustLevel.ReceiptBacked)
        {
            if (string.IsNullOrWhiteSpace(request.FailureReceiptRef) &&
                string.IsNullOrWhiteSpace(request.MutationReceiptRef))
            {
                missing.Add("DirtyWorktreeGuardReceiptRefRequired");
            }
        }

        if (request.EvidenceKind == DirtyWorktreeEvidenceKind.ProviderMetadata ||
            request.EvidenceTrustLevel == DirtyWorktreeEvidenceTrustLevel.ProviderMetadataBacked)
        {
            Require(request.ProviderStateRef, "DirtyWorktreeGuardProviderStateRefRequired", missing);
        }

        if (request.EvidenceKind == DirtyWorktreeEvidenceKind.OperatorReportedObservation ||
            request.EvidenceTrustLevel == DirtyWorktreeEvidenceTrustLevel.OperatorObserved)
        {
            Require(request.OperatorObservationRef, "DirtyWorktreeGuardOperatorObservationRefRequired", missing);
        }

        return missing
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ConsistencyIssue(DirtyWorktreeGuardRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ExpectedHeadRef) &&
            !Same(request.ExpectedHeadRef, request.ObservedHeadRef))
        {
            return "DirtyWorktreeGuardExpectedHeadMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedBranchRef) &&
            !Same(request.ExpectedBranchRef, request.ObservedBranchRef))
        {
            return "DirtyWorktreeGuardExpectedBranchMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedWorktreeFingerprint) &&
            !Same(request.ExpectedWorktreeFingerprint, request.ObservedWorktreeFingerprint))
        {
            return "DirtyWorktreeGuardExpectedFingerprintMismatch";
        }

        return null;
    }

    private static bool CanProceedToNextGate(DirtyWorktreeGuardRequest request) =>
        request.WorktreeState == DirtyWorktreeState.Clean &&
        request.ObservationFreshness == DirtyWorktreeObservationFreshness.Fresh &&
        request.EvidenceKind != DirtyWorktreeEvidenceKind.SyntheticTestObservation &&
        request.EvidenceTrustLevel is DirtyWorktreeEvidenceTrustLevel.PostStateObservationBacked
            or DirtyWorktreeEvidenceTrustLevel.ReceiptBacked
            or DirtyWorktreeEvidenceTrustLevel.ProviderMetadataBacked
            or DirtyWorktreeEvidenceTrustLevel.OperatorObserved;

    private static bool IsDirtyState(DirtyWorktreeState state) =>
        state is DirtyWorktreeState.Dirty
            or DirtyWorktreeState.Modified
            or DirtyWorktreeState.Untracked
            or DirtyWorktreeState.Deleted
            or DirtyWorktreeState.Renamed
            or DirtyWorktreeState.Conflict
            or DirtyWorktreeState.MergeInProgress
            or DirtyWorktreeState.RebaseInProgress
            or DirtyWorktreeState.CherryPickInProgress
            or DirtyWorktreeState.DetachedHead
            or DirtyWorktreeState.IndexLocked;

    private static bool IsUnknownState(DirtyWorktreeState state) =>
        state is DirtyWorktreeState.Unknown
            or DirtyWorktreeState.Unreadable
            or DirtyWorktreeState.Unavailable;

    private static bool HasCorroboratingEvidence(DirtyWorktreeGuardRequest request) =>
        !string.IsNullOrWhiteSpace(request.PostStateObservationRef) ||
        !string.IsNullOrWhiteSpace(request.FailureReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.MutationReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.ProviderStateRef) ||
        !string.IsNullOrWhiteSpace(request.OperatorObservationRef);

    private static void Require(string? value, string issue, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(issue);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static DirtyWorktreeGuardDecision Decision(
        DirtyWorktreeGuardRequest request,
        DirtyWorktreeGuardDecisionKind decision,
        DirtyWorktreeGuardBlockKind blockKind,
        DirtyWorktreeGuardValidationResult validation,
        string reason) =>
        new()
        {
            Decision = decision,
            Reason = reason,
            BlockKind = blockKind,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            CorrelationId = request.CorrelationId,
            MutationSurface = request.MutationSurface,
            AttemptRef = request.AttemptRef,
            TargetRef = request.TargetRef,
            GuardRef = request.GuardRef,
            SubjectKind = request.SubjectKind,
            WorktreeState = request.WorktreeState,
            EvidenceKind = request.EvidenceKind,
            EvidenceTrustLevel = request.EvidenceTrustLevel,
            ObservationFreshness = request.ObservationFreshness,
            MatchedWorktreeObservationRef = request.WorktreeObservationRef ?? string.Empty,
            MatchedPostStateObservationRef = request.PostStateObservationRef ?? string.Empty,
            MatchedFailureClassificationRef = request.FailureClassificationRef ?? string.Empty,
            MatchedFailureReceiptRef = request.FailureReceiptRef ?? string.Empty,
            MatchedMutationReceiptRef = request.MutationReceiptRef ?? string.Empty,
            MatchedProviderStateRef = request.ProviderStateRef ?? string.Empty,
            MatchedOperatorObservationRef = request.OperatorObservationRef ?? string.Empty,
            MatchedExpectedHeadRef = request.ExpectedHeadRef ?? string.Empty,
            MatchedObservedHeadRef = request.ObservedHeadRef ?? string.Empty,
            MatchedExpectedBranchRef = request.ExpectedBranchRef ?? string.Empty,
            MatchedObservedBranchRef = request.ObservedBranchRef ?? string.Empty,
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresFreshConcurrentGuard = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = DirtyWorktreeGuardValidator.BuildRecordFingerprint(request, decision, blockKind)
        };
}
