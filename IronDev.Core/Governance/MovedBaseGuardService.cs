namespace IronDev.Core.Governance;

public sealed class MovedBaseGuardService
{
    public MovedBaseGuardDecision Evaluate(MovedBaseGuardRequest request)
    {
        var validation = MovedBaseGuardValidator.ValidateRequest(request);
        if (validation.HasUnsafePayload)
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByUnsafePayload,
                MovedBaseGuardBlockKind.UnsafePayload,
                validation,
                validation.Issues.FirstOrDefault() ?? "MovedBaseGuardUnsafePayloadRejected");
        }

        if (validation.Issues.Count > 0)
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.Invalid,
                MovedBaseGuardBlockKind.InvalidRequest,
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
                MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence,
                MovedBaseGuardBlockKind.MissingEvidence,
                validation,
                missingEvidence.First());
        }

        var consistencyIssue = ConsistencyIssue(request);
        if (consistencyIssue is not null)
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByInconsistentRefEvidence,
                MovedBaseGuardBlockKind.InconsistentEvidence,
                validation,
                consistencyIssue);
        }

        var stateBlock = StateBlock(request, validation);
        if (stateBlock is not null)
        {
            return stateBlock;
        }

        if (!CanProceedToNextGate(request))
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence,
                MovedBaseGuardBlockKind.UntrustedEvidence,
                validation,
                "MovedBaseGuardEvidenceCannotProceedToAuthorityGate");
        }

        return Decision(
            request,
            MovedBaseGuardDecisionKind.MayProceedToNextAuthorityGate,
            MovedBaseGuardBlockKind.None,
            validation,
            "MovedBaseGuardMatchingEvidenceMayProceedToNextAuthorityGate");
    }

    private static MovedBaseGuardDecision? ValidateDimensions(
        MovedBaseGuardRequest request,
        MovedBaseGuardValidationResult validation)
    {
        if (request.SubjectKind == MovedBaseGuardSubjectKind.Unknown ||
            !Enum.IsDefined(request.SubjectKind))
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.Invalid,
                MovedBaseGuardBlockKind.InvalidRequest,
                validation,
                "MovedBaseGuardSubjectKindUnknown");
        }

        if (request.ObservedState == MovedBaseObservedState.Unknown ||
            !Enum.IsDefined(request.ObservedState))
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByUnknownBaseState,
                MovedBaseGuardBlockKind.UnknownBaseState,
                validation,
                "MovedBaseGuardObservedStateUnknown");
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.Unknown ||
            !Enum.IsDefined(request.EvidenceKind))
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence,
                MovedBaseGuardBlockKind.UntrustedEvidence,
                validation,
                "MovedBaseGuardEvidenceKindUnknown");
        }

        if (request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.Unknown ||
            !Enum.IsDefined(request.EvidenceTrustLevel))
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence,
                MovedBaseGuardBlockKind.UntrustedEvidence,
                validation,
                "MovedBaseGuardEvidenceTrustLevelUnknown");
        }

        if (request.ObservationFreshness == MovedBaseObservationFreshness.Unknown ||
            !Enum.IsDefined(request.ObservationFreshness))
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByStaleRefObservation,
                MovedBaseGuardBlockKind.StaleObservation,
                validation,
                "MovedBaseGuardObservationFreshnessUnknown");
        }

        return null;
    }

    private static MovedBaseGuardDecision? ValidateFreshness(
        MovedBaseGuardRequest request,
        MovedBaseGuardValidationResult validation)
    {
        if (request.ObservationFreshness == MovedBaseObservationFreshness.NotTimestamped ||
            request.ObservationFreshness == MovedBaseObservationFreshness.Stale ||
            (request.RecordedAtUtc - request.ObservedAtUtc).TotalSeconds > MovedBaseGuardValidator.MaxObservationAgeSeconds)
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByStaleRefObservation,
                MovedBaseGuardBlockKind.StaleObservation,
                validation,
                "MovedBaseGuardObservationStale");
        }

        if (request.ObservationFreshness == MovedBaseObservationFreshness.Expired ||
            request.EvidenceExpiresAtUtc is { } expiresAt && expiresAt <= request.RecordedAtUtc)
        {
            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByExpiredRefObservation,
                MovedBaseGuardBlockKind.ExpiredObservation,
                validation,
                "MovedBaseGuardObservationExpired");
        }

        return null;
    }

    private static MovedBaseGuardDecision? ValidateTrust(
        MovedBaseGuardRequest request,
        MovedBaseGuardValidationResult validation)
    {
        if (request.EvidenceKind == MovedBaseEvidenceKind.SyntheticTestObservation ||
            request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.TestFixture)
        {
            if (!request.Source.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(
                    request,
                    MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence,
                    MovedBaseGuardBlockKind.UntrustedEvidence,
                    validation,
                    "MovedBaseGuardTestFixtureSourceRequired");
            }

            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence,
                MovedBaseGuardBlockKind.UntrustedEvidence,
                validation,
                "MovedBaseGuardTestFixtureCannotProceedToAuthorityGate");
        }

        if (request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.SelfReported)
        {
            if (!HasCorroboratingEvidence(request))
            {
                return Decision(
                    request,
                    MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence,
                    MovedBaseGuardBlockKind.MissingEvidence,
                    validation,
                    "MovedBaseGuardSelfReportedCorroborationRequired");
            }

            return Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence,
                MovedBaseGuardBlockKind.UntrustedEvidence,
                validation,
                "MovedBaseGuardSelfReportedCannotProceedToAuthorityGate");
        }

        return null;
    }

    private static IReadOnlyList<string> RequiredEvidenceIssues(MovedBaseGuardRequest request)
    {
        var missing = new List<string>();

        if (request.ObservedState == MovedBaseObservedState.Matching)
        {
            Require(request.RefObservationRef, "MovedBaseGuardRefObservationRefRequired", missing);
            Require(request.ObservedBaseRef, "MovedBaseGuardObservedBaseRefRequired", missing);
            Require(request.ObservedHeadRef, "MovedBaseGuardObservedHeadRefRequired", missing);
            Require(request.ObservedBranchRef, "MovedBaseGuardObservedBranchRefRequired", missing);
            Require(request.ObservedTargetFingerprint, "MovedBaseGuardObservedTargetFingerprintRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.PostStateObservation ||
            request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.PostStateObservationBacked)
        {
            Require(request.PostStateObservationRef, "MovedBaseGuardPostStateObservationRefRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.DirtyWorktreeGuardEvidence ||
            request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.DirtyWorktreeGuardBacked)
        {
            Require(request.DirtyWorktreeGuardRef, "MovedBaseGuardDirtyWorktreeGuardRefRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.ValidationReceipt)
        {
            Require(request.ValidationReceiptRef, "MovedBaseGuardValidationReceiptRefRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.PatchPackageReceipt)
        {
            Require(request.PatchPackageRef, "MovedBaseGuardPatchPackageRefRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.CommitPackageReceipt)
        {
            Require(request.CommitPackageRef, "MovedBaseGuardCommitPackageRefRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.PushReceipt)
        {
            Require(request.PushReceiptRef, "MovedBaseGuardPushReceiptRefRequired", missing);
        }

        if (request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.ReceiptBacked &&
            string.IsNullOrWhiteSpace(request.ValidationReceiptRef) &&
            string.IsNullOrWhiteSpace(request.PatchPackageRef) &&
            string.IsNullOrWhiteSpace(request.CommitPackageRef) &&
            string.IsNullOrWhiteSpace(request.PushReceiptRef))
        {
            missing.Add("MovedBaseGuardReceiptRefRequired");
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.PullRequestProviderMetadata)
        {
            Require(request.PullRequestProviderStateRef, "MovedBaseGuardPullRequestProviderStateRefRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.ProviderMetadata ||
            request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.ProviderMetadataBacked)
        {
            Require(request.ProviderStateRef, "MovedBaseGuardProviderStateRefRequired", missing);
        }

        if (request.EvidenceKind == MovedBaseEvidenceKind.OperatorReportedObservation ||
            request.EvidenceTrustLevel == MovedBaseEvidenceTrustLevel.OperatorObserved)
        {
            Require(request.OperatorObservationRef, "MovedBaseGuardOperatorObservationRefRequired", missing);
        }

        return missing
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ConsistencyIssue(MovedBaseGuardRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ExpectedBaseRef) &&
            !Same(request.ExpectedBaseRef, request.ObservedBaseRef))
        {
            return "MovedBaseGuardExpectedBaseMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedHeadRef) &&
            !Same(request.ExpectedHeadRef, request.ObservedHeadRef))
        {
            return "MovedBaseGuardExpectedHeadMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedRemoteHeadRef) &&
            !Same(request.ExpectedRemoteHeadRef, request.ObservedRemoteHeadRef))
        {
            return "MovedBaseGuardExpectedRemoteHeadMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedBranchRef) &&
            !Same(request.ExpectedBranchRef, request.ObservedBranchRef))
        {
            return "MovedBaseGuardExpectedBranchMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedMergeBaseFingerprint) &&
            !Same(request.ExpectedMergeBaseFingerprint, request.ObservedMergeBaseFingerprint))
        {
            return "MovedBaseGuardExpectedMergeBaseFingerprintMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedTargetFingerprint) &&
            !Same(request.ExpectedTargetFingerprint, request.ObservedTargetFingerprint))
        {
            return "MovedBaseGuardExpectedTargetFingerprintMismatch";
        }

        return null;
    }

    private static MovedBaseGuardDecision? StateBlock(
        MovedBaseGuardRequest request,
        MovedBaseGuardValidationResult validation) =>
        request.ObservedState switch
        {
            MovedBaseObservedState.BaseMoved => Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByMovedBase,
                MovedBaseGuardBlockKind.MovedBase,
                validation,
                "MovedBaseGuardBaseMoved"),
            MovedBaseObservedState.MergeBaseMoved => Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByMovedMergeBase,
                MovedBaseGuardBlockKind.MovedMergeBase,
                validation,
                "MovedBaseGuardMergeBaseMoved"),
            MovedBaseObservedState.HeadMoved => Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByMovedHead,
                MovedBaseGuardBlockKind.MovedHead,
                validation,
                "MovedBaseGuardHeadMoved"),
            MovedBaseObservedState.RemoteHeadMoved => Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByMovedRemoteHead,
                MovedBaseGuardBlockKind.MovedRemoteHead,
                validation,
                "MovedBaseGuardRemoteHeadMoved"),
            MovedBaseObservedState.BranchMoved => Decision(
                request,
                MovedBaseGuardDecisionKind.BlockedByMovedBranch,
                MovedBaseGuardBlockKind.MovedBranch,
                validation,
                "MovedBaseGuardBranchMoved"),
            MovedBaseObservedState.Diverged or
                MovedBaseObservedState.Ahead or
                MovedBaseObservedState.Behind => Decision(
                    request,
                    MovedBaseGuardDecisionKind.BlockedByDivergedRef,
                    MovedBaseGuardBlockKind.DivergedRef,
                    validation,
                    $"MovedBaseGuardDivergedRef:{request.ObservedState}"),
            MovedBaseObservedState.Missing or
                MovedBaseObservedState.Deleted or
                MovedBaseObservedState.Unavailable or
                MovedBaseObservedState.Ambiguous => Decision(
                    request,
                    MovedBaseGuardDecisionKind.BlockedByUnknownBaseState,
                    MovedBaseGuardBlockKind.UnknownBaseState,
                    validation,
                    $"MovedBaseGuardUnknownState:{request.ObservedState}"),
            _ => null
        };

    private static bool CanProceedToNextGate(MovedBaseGuardRequest request) =>
        request.ObservedState == MovedBaseObservedState.Matching &&
        request.ObservationFreshness == MovedBaseObservationFreshness.Fresh &&
        request.EvidenceKind != MovedBaseEvidenceKind.SyntheticTestObservation &&
        request.EvidenceTrustLevel is MovedBaseEvidenceTrustLevel.RefObservationBacked
            or MovedBaseEvidenceTrustLevel.PostStateObservationBacked
            or MovedBaseEvidenceTrustLevel.DirtyWorktreeGuardBacked
            or MovedBaseEvidenceTrustLevel.ReceiptBacked
            or MovedBaseEvidenceTrustLevel.ProviderMetadataBacked
            or MovedBaseEvidenceTrustLevel.OperatorObserved;

    private static bool HasCorroboratingEvidence(MovedBaseGuardRequest request) =>
        !string.IsNullOrWhiteSpace(request.PostStateObservationRef) ||
        !string.IsNullOrWhiteSpace(request.DirtyWorktreeGuardRef) ||
        !string.IsNullOrWhiteSpace(request.ValidationReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.PatchPackageRef) ||
        !string.IsNullOrWhiteSpace(request.CommitPackageRef) ||
        !string.IsNullOrWhiteSpace(request.PushReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.PullRequestProviderStateRef) ||
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

    private static MovedBaseGuardDecision Decision(
        MovedBaseGuardRequest request,
        MovedBaseGuardDecisionKind decision,
        MovedBaseGuardBlockKind blockKind,
        MovedBaseGuardValidationResult validation,
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
            ObservedState = request.ObservedState,
            EvidenceKind = request.EvidenceKind,
            EvidenceTrustLevel = request.EvidenceTrustLevel,
            ObservationFreshness = request.ObservationFreshness,
            MatchedRefObservationRef = request.RefObservationRef ?? string.Empty,
            MatchedPostStateObservationRef = request.PostStateObservationRef ?? string.Empty,
            MatchedDirtyWorktreeGuardRef = request.DirtyWorktreeGuardRef ?? string.Empty,
            MatchedValidationReceiptRef = request.ValidationReceiptRef ?? string.Empty,
            MatchedPatchPackageRef = request.PatchPackageRef ?? string.Empty,
            MatchedCommitPackageRef = request.CommitPackageRef ?? string.Empty,
            MatchedPushReceiptRef = request.PushReceiptRef ?? string.Empty,
            MatchedPullRequestProviderStateRef = request.PullRequestProviderStateRef ?? string.Empty,
            MatchedProviderStateRef = request.ProviderStateRef ?? string.Empty,
            MatchedOperatorObservationRef = request.OperatorObservationRef ?? string.Empty,
            MatchedExpectedBaseRef = request.ExpectedBaseRef ?? string.Empty,
            MatchedObservedBaseRef = request.ObservedBaseRef ?? string.Empty,
            MatchedExpectedHeadRef = request.ExpectedHeadRef ?? string.Empty,
            MatchedObservedHeadRef = request.ObservedHeadRef ?? string.Empty,
            MatchedExpectedRemoteHeadRef = request.ExpectedRemoteHeadRef ?? string.Empty,
            MatchedObservedRemoteHeadRef = request.ObservedRemoteHeadRef ?? string.Empty,
            MatchedExpectedBranchRef = request.ExpectedBranchRef ?? string.Empty,
            MatchedObservedBranchRef = request.ObservedBranchRef ?? string.Empty,
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresFreshConcurrentGuard = true,
            RequiresDirtyWorktreeGuard = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = MovedBaseGuardValidator.BuildRecordFingerprint(request, decision, blockKind)
        };
}
