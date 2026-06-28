namespace IronDev.Core.Governance;

public sealed class IdempotencyKeyContractService
{
    public IdempotencyKeyContractDecision Evaluate(IdempotencyKeyContractRequest? request)
    {
        var validation = IdempotencyKeyContractValidator.ValidateRequest(request);

        if (request is null)
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.Invalid,
                IdempotencyKeyBlockKind.InvalidRequest,
                validation,
                "IdempotencyKeyContractRequestRequired");
        }

        if (validation.HasUnsafePayload)
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByUnsafePayload,
                IdempotencyKeyBlockKind.UnsafePayload,
                validation,
                "IdempotencyUnsafePayloadRejected");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByMissingIdempotencyKey,
                IdempotencyKeyBlockKind.MissingKey,
                validation,
                "IdempotencyKeyRequired");
        }

        if (validation.HasMalformedKey)
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByMalformedIdempotencyKey,
                IdempotencyKeyBlockKind.MalformedKey,
                validation,
                validation.Issues.FirstOrDefault(static issue => issue is "IdempotencyKeyMalformed") ?? "IdempotencyKeyMalformed");
        }

        var dimensionBlock = ValidateDimensions(request, validation);
        if (dimensionBlock is not null)
        {
            return dimensionBlock;
        }

        if (validation.Issues.Count > 0)
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.Invalid,
                IdempotencyKeyBlockKind.InvalidRequest,
                validation,
                validation.Issues.First());
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
                IdempotencyKeyDecisionKind.BlockedByMissingIdempotencyEvidence,
                IdempotencyKeyBlockKind.MissingEvidence,
                validation,
                missingEvidence.First());
        }

        var consistencyBlock = ValidateConsistency(request, validation);
        if (consistencyBlock is not null)
        {
            return consistencyBlock;
        }

        return request.PriorState switch
        {
            IdempotencyPriorState.NoPriorObservation => Decision(
                request,
                IdempotencyKeyDecisionKind.MayProceedToNextAuthorityGate,
                IdempotencyKeyBlockKind.None,
                validation,
                "IdempotencyFreshNewKeyMayProceedToNextAuthorityGate"),
            IdempotencyPriorState.PriorCompletedSameRequest => Decision(
                request,
                IdempotencyKeyDecisionKind.DuplicateCompletedNoExecution,
                IdempotencyKeyBlockKind.None,
                validation,
                "IdempotencyDuplicateCompletedNoExecution"),
            IdempotencyPriorState.PriorInProgressSameRequest => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByDuplicateInProgress,
                IdempotencyKeyBlockKind.DuplicateInProgress,
                validation,
                "IdempotencyDuplicateInProgress"),
            IdempotencyPriorState.PriorFailedSameRequest => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByPriorFailedAttempt,
                IdempotencyKeyBlockKind.PriorFailedAttempt,
                validation,
                "IdempotencyPriorFailedAttemptRequiresSeparateRetryOrRecoveryAuthority"),
            IdempotencyPriorState.PriorCancelledSameRequest => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByPriorCancelledAttempt,
                IdempotencyKeyBlockKind.PriorCancelledAttempt,
                validation,
                "IdempotencyPriorCancelledAttemptRequiresFreshAuthority"),
            IdempotencyPriorState.PriorConflictingRequest => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingRequestFingerprint,
                IdempotencyKeyBlockKind.ConflictingRequest,
                validation,
                "IdempotencyPriorConflictingRequest"),
            IdempotencyPriorState.PriorConflictingAuthority => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingAuthorityFingerprint,
                IdempotencyKeyBlockKind.ConflictingAuthority,
                validation,
                "IdempotencyPriorConflictingAuthority"),
            IdempotencyPriorState.PriorConflictingTarget => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingTargetFingerprint,
                IdempotencyKeyBlockKind.ConflictingTarget,
                validation,
                "IdempotencyPriorConflictingTarget"),
            IdempotencyPriorState.PriorExpired => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByExpiredIdempotencyObservation,
                IdempotencyKeyBlockKind.ExpiredObservation,
                validation,
                "IdempotencyPriorExpired"),
            IdempotencyPriorState.PriorUnavailable or IdempotencyPriorState.Ambiguous => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByAmbiguousIdempotencyState,
                IdempotencyKeyBlockKind.AmbiguousState,
                validation,
                $"IdempotencyPriorStateAmbiguous:{request.PriorState}"),
            _ => Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByAmbiguousIdempotencyState,
                IdempotencyKeyBlockKind.AmbiguousState,
                validation,
                "IdempotencyPriorStateUnknown")
        };
    }

    private static IdempotencyKeyContractDecision? ValidateDimensions(
        IdempotencyKeyContractRequest request,
        IdempotencyKeyContractValidationResult validation)
    {
        if (request.SubjectKind == IdempotencySubjectKind.Unknown ||
            !Enum.IsDefined(request.SubjectKind))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.Invalid,
                IdempotencyKeyBlockKind.InvalidRequest,
                validation,
                "IdempotencySubjectKindUnknown");
        }

        if (request.EvidenceKind == IdempotencyEvidenceKind.Unknown ||
            !Enum.IsDefined(request.EvidenceKind))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence,
                IdempotencyKeyBlockKind.UntrustedEvidence,
                validation,
                "IdempotencyEvidenceKindUnknown");
        }

        if (request.EvidenceTrustLevel == IdempotencyEvidenceTrustLevel.Unknown ||
            !Enum.IsDefined(request.EvidenceTrustLevel))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence,
                IdempotencyKeyBlockKind.UntrustedEvidence,
                validation,
                "IdempotencyEvidenceTrustLevelUnknown");
        }

        if (request.ObservationFreshness == IdempotencyObservationFreshness.Unknown ||
            !Enum.IsDefined(request.ObservationFreshness))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByStaleIdempotencyObservation,
                IdempotencyKeyBlockKind.StaleObservation,
                validation,
                "IdempotencyObservationFreshnessUnknown");
        }

        if (request.PriorState == IdempotencyPriorState.Unknown ||
            !Enum.IsDefined(request.PriorState))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByAmbiguousIdempotencyState,
                IdempotencyKeyBlockKind.AmbiguousState,
                validation,
                "IdempotencyPriorStateUnknown");
        }

        return null;
    }

    private static IdempotencyKeyContractDecision? ValidateFreshness(
        IdempotencyKeyContractRequest request,
        IdempotencyKeyContractValidationResult validation)
    {
        if (request.ObservationFreshness == IdempotencyObservationFreshness.NotTimestamped ||
            request.ObservationFreshness == IdempotencyObservationFreshness.Stale ||
            (request.RecordedAtUtc - request.ObservedAtUtc).TotalSeconds > IdempotencyKeyContractValidator.MaxObservationAgeSeconds)
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByStaleIdempotencyObservation,
                IdempotencyKeyBlockKind.StaleObservation,
                validation,
                "IdempotencyObservationStale");
        }

        if (request.ObservationFreshness == IdempotencyObservationFreshness.Expired ||
            request.EvidenceExpiresAtUtc is { } expiresAt && expiresAt <= request.RecordedAtUtc)
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByExpiredIdempotencyObservation,
                IdempotencyKeyBlockKind.ExpiredObservation,
                validation,
                "IdempotencyObservationExpired");
        }

        return null;
    }

    private static IdempotencyKeyContractDecision? ValidateTrust(
        IdempotencyKeyContractRequest request,
        IdempotencyKeyContractValidationResult validation)
    {
        if (request.EvidenceKind == IdempotencyEvidenceKind.SyntheticTestKey ||
            request.EvidenceTrustLevel == IdempotencyEvidenceTrustLevel.TestFixture)
        {
            if (!request.Source.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(
                    request,
                    IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence,
                    IdempotencyKeyBlockKind.UntrustedEvidence,
                    validation,
                    "IdempotencyTestFixtureSourceRequired");
            }

            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence,
                IdempotencyKeyBlockKind.UntrustedEvidence,
                validation,
                "IdempotencyTestFixtureCannotProceedToAuthorityGate");
        }

        if (request.EvidenceTrustLevel == IdempotencyEvidenceTrustLevel.SelfReported)
        {
            if (!HasCorroboratingEvidence(request))
            {
                return Decision(
                    request,
                    IdempotencyKeyDecisionKind.BlockedByMissingIdempotencyEvidence,
                    IdempotencyKeyBlockKind.MissingEvidence,
                    validation,
                    "IdempotencySelfReportedCorroborationRequired");
            }

            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence,
                IdempotencyKeyBlockKind.UntrustedEvidence,
                validation,
                "IdempotencySelfReportedCannotProceedToAuthorityGate");
        }

        return null;
    }

    private static IReadOnlyList<string> RequiredEvidenceIssues(IdempotencyKeyContractRequest request)
    {
        var missing = new List<string>();

        Require(request.IdempotencyObservationRef, "IdempotencyObservationRefRequired", missing);

        if (request.EvidenceKind is IdempotencyEvidenceKind.ClientProvidedKey or IdempotencyEvidenceKind.ExecutorRequestKey ||
            request.EvidenceTrustLevel == IdempotencyEvidenceTrustLevel.RequestFingerprintBacked)
        {
            Require(request.ExpectedRequestFingerprint, "IdempotencyExpectedRequestFingerprintRequired", missing);
            Require(request.ObservedRequestFingerprint, "IdempotencyObservedRequestFingerprintRequired", missing);
        }

        if (request.PriorState != IdempotencyPriorState.NoPriorObservation)
        {
            Require(request.PriorAttemptRef, "IdempotencyPriorAttemptRefRequired", missing);
        }

        if (request.PriorState == IdempotencyPriorState.PriorCompletedSameRequest)
        {
            Require(request.PriorReceiptRef, "IdempotencyPriorReceiptRefRequired", missing);
        }

        if (request.PriorState == IdempotencyPriorState.NoPriorObservation)
        {
            Require(request.AuthorityFingerprint, "IdempotencyAuthorityFingerprintRequired", missing);
            Require(request.ExpectedAuthorityFingerprint, "IdempotencyExpectedAuthorityFingerprintRequired", missing);
            Require(request.ObservedAuthorityFingerprint, "IdempotencyObservedAuthorityFingerprintRequired", missing);
            Require(request.TargetFingerprint, "IdempotencyTargetFingerprintRequired", missing);
            Require(request.ExpectedTargetFingerprint, "IdempotencyExpectedTargetFingerprintRequired", missing);
            Require(request.ObservedTargetFingerprint, "IdempotencyObservedTargetFingerprintRequired", missing);
            Require(request.EffectFingerprint, "IdempotencyEffectFingerprintRequired", missing);
            Require(request.ExpectedEffectFingerprint, "IdempotencyExpectedEffectFingerprintRequired", missing);
            Require(request.ObservedEffectFingerprint, "IdempotencyObservedEffectFingerprintRequired", missing);
            Require(request.AuthorityReceiptRef, "IdempotencyAuthorityReceiptRefRequired", missing);
            Require(request.PolicySatisfactionRef, "IdempotencyPolicySatisfactionRefRequired", missing);
            Require(request.ValidationReceiptRef, "IdempotencyValidationReceiptRefRequired", missing);
            Require(request.ConcurrentGuardDecisionRef, "IdempotencyConcurrentGuardDecisionRefRequired", missing);
            Require(request.DirtyWorktreeGuardRef, "IdempotencyDirtyWorktreeGuardRefRequired", missing);
            Require(request.MovedBaseGuardRef, "IdempotencyMovedBaseGuardRefRequired", missing);
            Require(request.StaleValidationGuardRef, "IdempotencyStaleValidationGuardRefRequired", missing);
            Require(request.BranchRemoteHeadVerificationRef, "IdempotencyBranchRemoteHeadVerificationRefRequired", missing);
            Require(request.PostStateObservationRef, "IdempotencyPostStateObservationRefRequired", missing);
        }

        return missing
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static IdempotencyKeyContractDecision? ValidateConsistency(
        IdempotencyKeyContractRequest request,
        IdempotencyKeyContractValidationResult validation)
    {
        if (!SameIfPresent(request.ExpectedRequestFingerprint, request.ObservedRequestFingerprint))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingRequestFingerprint,
                IdempotencyKeyBlockKind.ConflictingRequest,
                validation,
                "IdempotencyExpectedObservedRequestFingerprintMismatch");
        }

        if (!SameIfPresent(request.RequestFingerprint, request.ObservedRequestFingerprint))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingRequestFingerprint,
                IdempotencyKeyBlockKind.ConflictingRequest,
                validation,
                "IdempotencySameKeyDifferentRequestFingerprint");
        }

        if (!SameIfPresent(request.ExpectedAuthorityFingerprint, request.ObservedAuthorityFingerprint))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingAuthorityFingerprint,
                IdempotencyKeyBlockKind.ConflictingAuthority,
                validation,
                "IdempotencyExpectedObservedAuthorityFingerprintMismatch");
        }

        if (!SameIfPresent(request.AuthorityFingerprint, request.ObservedAuthorityFingerprint))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingAuthorityFingerprint,
                IdempotencyKeyBlockKind.ConflictingAuthority,
                validation,
                "IdempotencySameKeyDifferentAuthorityFingerprint");
        }

        if (!SameIfPresent(request.ExpectedTargetFingerprint, request.ObservedTargetFingerprint))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingTargetFingerprint,
                IdempotencyKeyBlockKind.ConflictingTarget,
                validation,
                "IdempotencyExpectedObservedTargetFingerprintMismatch");
        }

        if (!SameIfPresent(request.TargetFingerprint, request.ObservedTargetFingerprint))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingTargetFingerprint,
                IdempotencyKeyBlockKind.ConflictingTarget,
                validation,
                "IdempotencySameKeyDifferentTargetFingerprint");
        }

        if (!SameIfPresent(request.ExpectedEffectFingerprint, request.ObservedEffectFingerprint) ||
            !SameIfPresent(request.EffectFingerprint, request.ObservedEffectFingerprint))
        {
            return Decision(
                request,
                IdempotencyKeyDecisionKind.BlockedByConflictingIdempotencyKey,
                IdempotencyKeyBlockKind.ConflictingKey,
                validation,
                !SameIfPresent(request.ExpectedEffectFingerprint, request.ObservedEffectFingerprint)
                    ? "IdempotencyExpectedObservedEffectFingerprintMismatch"
                    : "IdempotencySameKeyDifferentEffectFingerprint");
        }

        return null;
    }

    private static bool HasCorroboratingEvidence(IdempotencyKeyContractRequest request) =>
        !string.IsNullOrWhiteSpace(request.IdempotencyObservationRef) ||
        !string.IsNullOrWhiteSpace(request.PriorReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.PriorOperationStatusRef) ||
        !string.IsNullOrWhiteSpace(request.PriorLineageRef) ||
        !string.IsNullOrWhiteSpace(request.ObservedRequestFingerprint);

    private static void Require(string? value, string issue, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(issue);
        }
    }

    private static bool SameIfPresent(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) ||
        string.IsNullOrWhiteSpace(right) ||
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static IdempotencyKeyContractDecision Decision(
        IdempotencyKeyContractRequest? request,
        IdempotencyKeyDecisionKind decision,
        IdempotencyKeyBlockKind blockKind,
        IdempotencyKeyContractValidationResult validation,
        string reason) =>
        new()
        {
            Decision = decision,
            BlockKind = blockKind,
            Reason = reason,
            TenantId = Safe(request?.TenantId),
            ProjectId = Safe(request?.ProjectId),
            OperationId = Safe(request?.OperationId),
            CorrelationId = Safe(request?.CorrelationId),
            MutationSurface = request?.MutationSurface ?? MutationLeaseSurfaceKind.Unknown,
            SubjectKind = request?.SubjectKind ?? IdempotencySubjectKind.Unknown,
            AttemptRef = Safe(request?.AttemptRef),
            TargetRef = Safe(request?.TargetRef),
            RequestRef = Safe(request?.RequestRef),
            MatchedIdempotencyKey = Safe(request?.IdempotencyKey),
            MatchedIdempotencyScopeRef = Safe(request?.IdempotencyScopeRef),
            MatchedIdempotencyObservationRef = Safe(request?.IdempotencyObservationRef),
            MatchedPriorAttemptRef = Safe(request?.PriorAttemptRef),
            MatchedPriorReceiptRef = Safe(request?.PriorReceiptRef),
            MatchedPriorOperationStatusRef = Safe(request?.PriorOperationStatusRef),
            MatchedPriorLineageRef = Safe(request?.PriorLineageRef),
            MatchedRequestFingerprint = Safe(request?.RequestFingerprint),
            MatchedExpectedRequestFingerprint = Safe(request?.ExpectedRequestFingerprint),
            MatchedObservedRequestFingerprint = Safe(request?.ObservedRequestFingerprint),
            MatchedAuthorityFingerprint = Safe(request?.AuthorityFingerprint),
            MatchedExpectedAuthorityFingerprint = Safe(request?.ExpectedAuthorityFingerprint),
            MatchedObservedAuthorityFingerprint = Safe(request?.ObservedAuthorityFingerprint),
            MatchedTargetFingerprint = Safe(request?.TargetFingerprint),
            MatchedExpectedTargetFingerprint = Safe(request?.ExpectedTargetFingerprint),
            MatchedObservedTargetFingerprint = Safe(request?.ObservedTargetFingerprint),
            MatchedEffectFingerprint = Safe(request?.EffectFingerprint),
            MatchedExpectedEffectFingerprint = Safe(request?.ExpectedEffectFingerprint),
            MatchedObservedEffectFingerprint = Safe(request?.ObservedEffectFingerprint),
            EvidenceKind = request?.EvidenceKind ?? IdempotencyEvidenceKind.Unknown,
            EvidenceTrustLevel = request?.EvidenceTrustLevel ?? IdempotencyEvidenceTrustLevel.Unknown,
            ObservationFreshness = request?.ObservationFreshness ?? IdempotencyObservationFreshness.Unknown,
            PriorState = request?.PriorState ?? IdempotencyPriorState.Unknown,
            MatchedAuthorityReceiptRef = Safe(request?.AuthorityReceiptRef),
            MatchedPolicySatisfactionRef = Safe(request?.PolicySatisfactionRef),
            MatchedValidationReceiptRef = Safe(request?.ValidationReceiptRef),
            MatchedConcurrentGuardDecisionRef = Safe(request?.ConcurrentGuardDecisionRef),
            MatchedDirtyWorktreeGuardRef = Safe(request?.DirtyWorktreeGuardRef),
            MatchedMovedBaseGuardRef = Safe(request?.MovedBaseGuardRef),
            MatchedStaleValidationGuardRef = Safe(request?.StaleValidationGuardRef),
            MatchedBranchRemoteHeadVerificationRef = Safe(request?.BranchRemoteHeadVerificationRef),
            MatchedPostStateObservationRef = Safe(request?.PostStateObservationRef),
            RequiresFreshAuthority = true,
            RequiresAcceptedApproval = true,
            RequiresPolicySatisfaction = true,
            RequiresFreshValidation = true,
            RequiresConcurrentGuard = true,
            RequiresDirtyWorktreeGuard = true,
            RequiresMovedBaseGuard = true,
            RequiresStaleValidationGuard = true,
            RequiresBranchRemoteHeadVerification = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = IdempotencyKeyContractValidator.BuildRecordFingerprint(request, decision, blockKind)
        };

    private static string Safe(string? value) =>
        IdempotencyKeyContractValidator.SafeDecisionValue(value);
}
