namespace IronDev.Core.Governance;

public sealed class BranchRemoteHeadVerificationService
{
    public BranchRemoteHeadVerificationDecision Evaluate(BranchRemoteHeadVerificationRequest? request)
    {
        var validation = BranchRemoteHeadVerificationValidator.ValidateRequest(request);
        if (request is null)
        {
            return NullDecision(
                BranchRemoteHeadVerificationDecisionKind.Invalid,
                BranchRemoteHeadVerificationBlockKind.InvalidRequest,
                validation,
                validation.Issues.FirstOrDefault() ?? "BranchRemoteHeadVerificationRequestRequired");
        }

        if (validation.HasUnsafePayload)
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByUnsafePayload,
                BranchRemoteHeadVerificationBlockKind.UnsafePayload,
                validation,
                validation.Issues.FirstOrDefault() ?? "BranchRemoteHeadUnsafePayloadRejected");
        }

        if (validation.Issues.Count > 0)
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.Invalid,
                BranchRemoteHeadVerificationBlockKind.InvalidRequest,
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

        var outcomeBlock = ValidateOutcome(request, validation);
        if (outcomeBlock is not null)
        {
            return outcomeBlock;
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
                BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence,
                BranchRemoteHeadVerificationBlockKind.MissingEvidence,
                validation,
                missingEvidence.First());
        }

        var consistencyIssue = ConsistencyIssue(request);
        if (consistencyIssue is not null)
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByInconsistentEvidence,
                BranchRemoteHeadVerificationBlockKind.InconsistentEvidence,
                validation,
                consistencyIssue);
        }

        if (!CanProceedToNextGate(request))
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence,
                BranchRemoteHeadVerificationBlockKind.UntrustedEvidence,
                validation,
                "BranchRemoteHeadEvidenceCannotProceedToAuthorityGate");
        }

        return Decision(
            request,
            BranchRemoteHeadVerificationDecisionKind.MayProceedToNextAuthorityGate,
            BranchRemoteHeadVerificationBlockKind.None,
            validation,
            "BranchRemoteHeadVerifiedEvidenceMayProceedToNextAuthorityGate");
    }

    private static BranchRemoteHeadVerificationDecision? ValidateDimensions(
        BranchRemoteHeadVerificationRequest request,
        BranchRemoteHeadVerificationValidationResult validation)
    {
        if (request.SubjectKind == BranchRemoteHeadSubjectKind.Unknown ||
            !Enum.IsDefined(request.SubjectKind))
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.Invalid,
                BranchRemoteHeadVerificationBlockKind.InvalidRequest,
                validation,
                "BranchRemoteHeadSubjectKindUnknown");
        }

        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.Unknown ||
            !Enum.IsDefined(request.EvidenceKind))
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence,
                BranchRemoteHeadVerificationBlockKind.UntrustedEvidence,
                validation,
                "BranchRemoteHeadEvidenceKindUnknown");
        }

        if (request.EvidenceTrustLevel == BranchRemoteHeadEvidenceTrustLevel.Unknown ||
            !Enum.IsDefined(request.EvidenceTrustLevel))
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence,
                BranchRemoteHeadVerificationBlockKind.UntrustedEvidence,
                validation,
                "BranchRemoteHeadEvidenceTrustLevelUnknown");
        }

        if (request.ObservationFreshness == BranchRemoteHeadObservationFreshness.Unknown ||
            !Enum.IsDefined(request.ObservationFreshness))
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByStaleObservation,
                BranchRemoteHeadVerificationBlockKind.StaleObservation,
                validation,
                "BranchRemoteHeadObservationFreshnessUnknown");
        }

        if (request.VerificationOutcome == BranchRemoteHeadVerificationOutcome.Unknown ||
            !Enum.IsDefined(request.VerificationOutcome))
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByInconsistentEvidence,
                BranchRemoteHeadVerificationBlockKind.InconsistentEvidence,
                validation,
                "BranchRemoteHeadVerificationOutcomeUnknown");
        }

        return null;
    }

    private static BranchRemoteHeadVerificationDecision? ValidateFreshness(
        BranchRemoteHeadVerificationRequest request,
        BranchRemoteHeadVerificationValidationResult validation)
    {
        if (request.ObservationFreshness == BranchRemoteHeadObservationFreshness.NotTimestamped ||
            request.ObservationFreshness == BranchRemoteHeadObservationFreshness.Stale ||
            (request.RecordedAtUtc - request.ObservedAtUtc).TotalSeconds > BranchRemoteHeadVerificationValidator.MaxObservationAgeSeconds)
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByStaleObservation,
                BranchRemoteHeadVerificationBlockKind.StaleObservation,
                validation,
                "BranchRemoteHeadObservationStale");
        }

        if (request.ObservationFreshness == BranchRemoteHeadObservationFreshness.Expired ||
            request.EvidenceExpiresAtUtc is { } expiresAt && expiresAt <= request.RecordedAtUtc)
        {
            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByExpiredObservation,
                BranchRemoteHeadVerificationBlockKind.ExpiredObservation,
                validation,
                "BranchRemoteHeadObservationExpired");
        }

        return null;
    }

    private static BranchRemoteHeadVerificationDecision? ValidateOutcome(
        BranchRemoteHeadVerificationRequest request,
        BranchRemoteHeadVerificationValidationResult validation) =>
        request.VerificationOutcome switch
        {
            BranchRemoteHeadVerificationOutcome.BranchMismatch => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByBranchMismatch,
                BranchRemoteHeadVerificationBlockKind.BranchMismatch,
                validation,
                "BranchRemoteHeadBranchMismatch"),
            BranchRemoteHeadVerificationOutcome.RemoteMismatch => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByRemoteMismatch,
                BranchRemoteHeadVerificationBlockKind.RemoteMismatch,
                validation,
                "BranchRemoteHeadRemoteMismatch"),
            BranchRemoteHeadVerificationOutcome.HeadMismatch => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByHeadMismatch,
                BranchRemoteHeadVerificationBlockKind.HeadMismatch,
                validation,
                "BranchRemoteHeadHeadMismatch"),
            BranchRemoteHeadVerificationOutcome.BaseMismatch => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByBaseMismatch,
                BranchRemoteHeadVerificationBlockKind.BaseMismatch,
                validation,
                "BranchRemoteHeadBaseMismatch"),
            BranchRemoteHeadVerificationOutcome.DetachedHead => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByDetachedHead,
                BranchRemoteHeadVerificationBlockKind.DetachedHead,
                validation,
                "BranchRemoteHeadDetachedHead"),
            BranchRemoteHeadVerificationOutcome.AmbiguousBranch => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByAmbiguousBranch,
                BranchRemoteHeadVerificationBlockKind.AmbiguousBranch,
                validation,
                "BranchRemoteHeadAmbiguousBranch"),
            BranchRemoteHeadVerificationOutcome.MissingBranch => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence,
                BranchRemoteHeadVerificationBlockKind.MissingEvidence,
                validation,
                "BranchRemoteHeadMissingBranch"),
            BranchRemoteHeadVerificationOutcome.MissingRemote => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence,
                BranchRemoteHeadVerificationBlockKind.MissingEvidence,
                validation,
                "BranchRemoteHeadMissingRemote"),
            BranchRemoteHeadVerificationOutcome.MissingHead => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence,
                BranchRemoteHeadVerificationBlockKind.MissingEvidence,
                validation,
                "BranchRemoteHeadMissingHead"),
            BranchRemoteHeadVerificationOutcome.RemoteUnavailable => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByRemoteUnavailable,
                BranchRemoteHeadVerificationBlockKind.RemoteUnavailable,
                validation,
                "BranchRemoteHeadRemoteUnavailable"),
            BranchRemoteHeadVerificationOutcome.DeletedRemoteBranch => Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByDeletedRemoteBranch,
                BranchRemoteHeadVerificationBlockKind.DeletedRemoteBranch,
                validation,
                "BranchRemoteHeadDeletedRemoteBranch"),
            _ => null
        };

    private static BranchRemoteHeadVerificationDecision? ValidateTrust(
        BranchRemoteHeadVerificationRequest request,
        BranchRemoteHeadVerificationValidationResult validation)
    {
        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.TestFixtureBranchObservation ||
            request.EvidenceTrustLevel == BranchRemoteHeadEvidenceTrustLevel.TestFixture)
        {
            if (!request.Source.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(
                    request,
                    BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence,
                    BranchRemoteHeadVerificationBlockKind.UntrustedEvidence,
                    validation,
                    "BranchRemoteHeadTestFixtureSourceRequired");
            }

            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence,
                BranchRemoteHeadVerificationBlockKind.UntrustedEvidence,
                validation,
                "BranchRemoteHeadTestFixtureCannotProceedToAuthorityGate");
        }

        if (request.EvidenceTrustLevel == BranchRemoteHeadEvidenceTrustLevel.SelfReported)
        {
            if (!HasCorroboratingEvidence(request))
            {
                return Decision(
                    request,
                    BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence,
                    BranchRemoteHeadVerificationBlockKind.MissingEvidence,
                    validation,
                    "BranchRemoteHeadSelfReportedCorroborationRequired");
            }

            return Decision(
                request,
                BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence,
                BranchRemoteHeadVerificationBlockKind.UntrustedEvidence,
                validation,
                "BranchRemoteHeadSelfReportedCannotProceedToAuthorityGate");
        }

        return null;
    }

    private static IReadOnlyList<string> RequiredEvidenceIssues(BranchRemoteHeadVerificationRequest request)
    {
        var missing = new List<string>();

        if (request.VerificationOutcome == BranchRemoteHeadVerificationOutcome.Verified)
        {
            Require(request.BranchObservationRef, "BranchRemoteHeadBranchObservationRefRequired", missing);
            Require(request.RemoteObservationRef, "BranchRemoteHeadRemoteObservationRefRequired", missing);
            Require(request.HeadObservationRef, "BranchRemoteHeadHeadObservationRefRequired", missing);
            Require(request.ObservedBranchRef, "BranchRemoteHeadObservedBranchRefRequired", missing);
            Require(request.ObservedRemoteRef, "BranchRemoteHeadObservedRemoteRefRequired", missing);
            Require(request.ObservedRemoteUrlFingerprint, "BranchRemoteHeadObservedRemoteUrlFingerprintRequired", missing);
            Require(request.ObservedLocalHeadRef, "BranchRemoteHeadObservedLocalHeadRefRequired", missing);
            Require(request.ObservedRemoteHeadRef, "BranchRemoteHeadObservedRemoteHeadRefRequired", missing);
            Require(request.ObservedBaseRef, "BranchRemoteHeadObservedBaseRefRequired", missing);
            Require(request.DirtyWorktreeGuardRef, "BranchRemoteHeadDirtyWorktreeGuardRefRequired", missing);
            Require(request.MovedBaseGuardRef, "BranchRemoteHeadMovedBaseGuardRefRequired", missing);
            Require(request.StaleValidationGuardRef, "BranchRemoteHeadStaleValidationGuardRefRequired", missing);
            Require(request.ConcurrentGuardDecisionRef, "BranchRemoteHeadConcurrentGuardDecisionRefRequired", missing);
            Require(request.PostStateObservationRef, "BranchRemoteHeadPostStateObservationRefRequired", missing);
        }

        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.BranchRemoteCompositeObservation)
        {
            Require(request.CompositeObservationRef, "BranchRemoteHeadCompositeObservationRefRequired", missing);
        }

        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.ProviderBranchState ||
            request.EvidenceTrustLevel == BranchRemoteHeadEvidenceTrustLevel.ProviderMetadataBacked)
        {
            Require(request.ProviderBranchStateRef, "BranchRemoteHeadProviderBranchStateRefRequired", missing);
        }

        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.OperatorBranchObservation ||
            request.EvidenceTrustLevel == BranchRemoteHeadEvidenceTrustLevel.OperatorObserved)
        {
            Require(request.OperatorObservationRef, "BranchRemoteHeadOperatorObservationRefRequired", missing);
        }

        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.LocalBranchObservation)
        {
            Require(request.BranchObservationRef, "BranchRemoteHeadBranchObservationRefRequired", missing);
        }

        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.LocalHeadObservation)
        {
            Require(request.HeadObservationRef, "BranchRemoteHeadHeadObservationRefRequired", missing);
        }

        if (request.EvidenceKind == BranchRemoteHeadEvidenceKind.RemoteHeadObservation)
        {
            Require(request.RemoteObservationRef, "BranchRemoteHeadRemoteObservationRefRequired", missing);
            Require(request.HeadObservationRef, "BranchRemoteHeadHeadObservationRefRequired", missing);
        }

        return missing
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ConsistencyIssue(BranchRemoteHeadVerificationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ExpectedBranchRef) &&
            !Same(request.ExpectedBranchRef, request.ObservedBranchRef))
        {
            return "BranchRemoteHeadExpectedBranchMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedRemoteRef) &&
            !Same(request.ExpectedRemoteRef, request.ObservedRemoteRef))
        {
            return "BranchRemoteHeadExpectedRemoteMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedRemoteUrlFingerprint) &&
            !Same(request.ExpectedRemoteUrlFingerprint, request.ObservedRemoteUrlFingerprint))
        {
            return "BranchRemoteHeadExpectedRemoteUrlFingerprintMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedLocalHeadRef) &&
            !Same(request.ExpectedLocalHeadRef, request.ObservedLocalHeadRef))
        {
            return "BranchRemoteHeadExpectedLocalHeadMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedRemoteHeadRef) &&
            !Same(request.ExpectedRemoteHeadRef, request.ObservedRemoteHeadRef))
        {
            return "BranchRemoteHeadExpectedRemoteHeadMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedBaseRef) &&
            !Same(request.ExpectedBaseRef, request.ObservedBaseRef))
        {
            return "BranchRemoteHeadExpectedBaseMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedSourceStateRef) &&
            !Same(request.ExpectedSourceStateRef, request.ObservedSourceStateRef))
        {
            return "BranchRemoteHeadExpectedSourceStateMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedPatchPackageRef) &&
            !Same(request.ExpectedPatchPackageRef, request.ObservedPatchPackageRef))
        {
            return "BranchRemoteHeadExpectedPatchPackageMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedCommitRef) &&
            !Same(request.ExpectedCommitRef, request.ObservedCommitRef))
        {
            return "BranchRemoteHeadExpectedCommitMismatch";
        }

        return null;
    }

    private static bool CanProceedToNextGate(BranchRemoteHeadVerificationRequest request) =>
        request.VerificationOutcome == BranchRemoteHeadVerificationOutcome.Verified &&
        request.ObservationFreshness == BranchRemoteHeadObservationFreshness.Fresh &&
        request.EvidenceKind != BranchRemoteHeadEvidenceKind.TestFixtureBranchObservation &&
        request.EvidenceTrustLevel is BranchRemoteHeadEvidenceTrustLevel.ReceiptBacked
            or BranchRemoteHeadEvidenceTrustLevel.ProviderMetadataBacked
            or BranchRemoteHeadEvidenceTrustLevel.LocalObservationBacked
            or BranchRemoteHeadEvidenceTrustLevel.OperatorObserved;

    private static bool HasCorroboratingEvidence(BranchRemoteHeadVerificationRequest request) =>
        !string.IsNullOrWhiteSpace(request.BranchObservationRef) ||
        !string.IsNullOrWhiteSpace(request.RemoteObservationRef) ||
        !string.IsNullOrWhiteSpace(request.HeadObservationRef) ||
        !string.IsNullOrWhiteSpace(request.CompositeObservationRef) ||
        !string.IsNullOrWhiteSpace(request.ProviderBranchStateRef) ||
        !string.IsNullOrWhiteSpace(request.OperatorObservationRef) ||
        !string.IsNullOrWhiteSpace(request.PostStateObservationRef) ||
        !string.IsNullOrWhiteSpace(request.DirtyWorktreeGuardRef) ||
        !string.IsNullOrWhiteSpace(request.MovedBaseGuardRef);

    private static void Require(string? value, string issue, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(issue);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string SafeDecisionValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : BranchRemoteHeadVerificationValidator.ContainsUnsafeText(value)
                ? "[unsafe-rejected]"
                : value;

    private static BranchRemoteHeadVerificationDecision NullDecision(
        BranchRemoteHeadVerificationDecisionKind decision,
        BranchRemoteHeadVerificationBlockKind blockKind,
        BranchRemoteHeadVerificationValidationResult validation,
        string reason) =>
        new()
        {
            Decision = decision,
            BlockKind = blockKind,
            Reason = reason,
            TenantId = string.Empty,
            ProjectId = string.Empty,
            OperationId = string.Empty,
            CorrelationId = string.Empty,
            MutationSurface = MutationLeaseSurfaceKind.Unknown,
            AttemptRef = string.Empty,
            TargetRef = string.Empty,
            GuardRef = string.Empty,
            SubjectKind = BranchRemoteHeadSubjectKind.Unknown,
            EvidenceKind = BranchRemoteHeadEvidenceKind.Unknown,
            EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.Unknown,
            ObservationFreshness = BranchRemoteHeadObservationFreshness.Unknown,
            VerificationOutcome = BranchRemoteHeadVerificationOutcome.Unknown,
            MatchedBranchObservationRef = string.Empty,
            MatchedRemoteObservationRef = string.Empty,
            MatchedHeadObservationRef = string.Empty,
            MatchedCompositeObservationRef = string.Empty,
            MatchedProviderBranchStateRef = string.Empty,
            MatchedOperatorObservationRef = string.Empty,
            MatchedExpectedBranchRef = string.Empty,
            MatchedObservedBranchRef = string.Empty,
            MatchedExpectedRemoteRef = string.Empty,
            MatchedObservedRemoteRef = string.Empty,
            MatchedExpectedRemoteUrlFingerprint = string.Empty,
            MatchedObservedRemoteUrlFingerprint = string.Empty,
            MatchedExpectedLocalHeadRef = string.Empty,
            MatchedObservedLocalHeadRef = string.Empty,
            MatchedExpectedRemoteHeadRef = string.Empty,
            MatchedObservedRemoteHeadRef = string.Empty,
            MatchedExpectedBaseRef = string.Empty,
            MatchedObservedBaseRef = string.Empty,
            MatchedExpectedSourceStateRef = string.Empty,
            MatchedObservedSourceStateRef = string.Empty,
            MatchedExpectedPatchPackageRef = string.Empty,
            MatchedObservedPatchPackageRef = string.Empty,
            MatchedExpectedCommitRef = string.Empty,
            MatchedObservedCommitRef = string.Empty,
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresDirtyWorktreeGuard = true,
            RequiresMovedBaseGuard = true,
            RequiresStaleValidationGuard = true,
            RequiresConcurrentGuard = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = "branch-remote-head-verification|null|Invalid|InvalidRequest"
        };

    private static BranchRemoteHeadVerificationDecision Decision(
        BranchRemoteHeadVerificationRequest request,
        BranchRemoteHeadVerificationDecisionKind decision,
        BranchRemoteHeadVerificationBlockKind blockKind,
        BranchRemoteHeadVerificationValidationResult validation,
        string reason) =>
        new()
        {
            Decision = decision,
            BlockKind = blockKind,
            Reason = reason,
            TenantId = SafeDecisionValue(request.TenantId),
            ProjectId = SafeDecisionValue(request.ProjectId),
            OperationId = SafeDecisionValue(request.OperationId),
            CorrelationId = SafeDecisionValue(request.CorrelationId),
            MutationSurface = request.MutationSurface,
            AttemptRef = SafeDecisionValue(request.AttemptRef),
            TargetRef = SafeDecisionValue(request.TargetRef),
            GuardRef = SafeDecisionValue(request.GuardRef),
            SubjectKind = request.SubjectKind,
            EvidenceKind = request.EvidenceKind,
            EvidenceTrustLevel = request.EvidenceTrustLevel,
            ObservationFreshness = request.ObservationFreshness,
            VerificationOutcome = request.VerificationOutcome,
            MatchedBranchObservationRef = SafeDecisionValue(request.BranchObservationRef),
            MatchedRemoteObservationRef = SafeDecisionValue(request.RemoteObservationRef),
            MatchedHeadObservationRef = SafeDecisionValue(request.HeadObservationRef),
            MatchedCompositeObservationRef = SafeDecisionValue(request.CompositeObservationRef),
            MatchedProviderBranchStateRef = SafeDecisionValue(request.ProviderBranchStateRef),
            MatchedOperatorObservationRef = SafeDecisionValue(request.OperatorObservationRef),
            MatchedExpectedBranchRef = SafeDecisionValue(request.ExpectedBranchRef),
            MatchedObservedBranchRef = SafeDecisionValue(request.ObservedBranchRef),
            MatchedExpectedRemoteRef = SafeDecisionValue(request.ExpectedRemoteRef),
            MatchedObservedRemoteRef = SafeDecisionValue(request.ObservedRemoteRef),
            MatchedExpectedRemoteUrlFingerprint = SafeDecisionValue(request.ExpectedRemoteUrlFingerprint),
            MatchedObservedRemoteUrlFingerprint = SafeDecisionValue(request.ObservedRemoteUrlFingerprint),
            MatchedExpectedLocalHeadRef = SafeDecisionValue(request.ExpectedLocalHeadRef),
            MatchedObservedLocalHeadRef = SafeDecisionValue(request.ObservedLocalHeadRef),
            MatchedExpectedRemoteHeadRef = SafeDecisionValue(request.ExpectedRemoteHeadRef),
            MatchedObservedRemoteHeadRef = SafeDecisionValue(request.ObservedRemoteHeadRef),
            MatchedExpectedBaseRef = SafeDecisionValue(request.ExpectedBaseRef),
            MatchedObservedBaseRef = SafeDecisionValue(request.ObservedBaseRef),
            MatchedExpectedSourceStateRef = SafeDecisionValue(request.ExpectedSourceStateRef),
            MatchedObservedSourceStateRef = SafeDecisionValue(request.ObservedSourceStateRef),
            MatchedExpectedPatchPackageRef = SafeDecisionValue(request.ExpectedPatchPackageRef),
            MatchedObservedPatchPackageRef = SafeDecisionValue(request.ObservedPatchPackageRef),
            MatchedExpectedCommitRef = SafeDecisionValue(request.ExpectedCommitRef),
            MatchedObservedCommitRef = SafeDecisionValue(request.ObservedCommitRef),
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresDirtyWorktreeGuard = true,
            RequiresMovedBaseGuard = true,
            RequiresStaleValidationGuard = true,
            RequiresConcurrentGuard = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = BranchRemoteHeadVerificationValidator.BuildRecordFingerprint(request, decision, blockKind)
        };
}
