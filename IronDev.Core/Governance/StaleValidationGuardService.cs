namespace IronDev.Core.Governance;

public sealed class StaleValidationGuardService
{
    public StaleValidationGuardDecision Evaluate(StaleValidationGuardRequest request)
    {
        var validation = StaleValidationGuardValidator.ValidateRequest(request);
        if (validation.HasUnsafePayload)
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByUnsafePayload,
                StaleValidationGuardBlockKind.UnsafePayload,
                validation,
                validation.Issues.FirstOrDefault() ?? "StaleValidationGuardUnsafePayloadRejected");
        }

        if (validation.Issues.Count > 0)
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.Invalid,
                StaleValidationGuardBlockKind.InvalidRequest,
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
                StaleValidationGuardDecisionKind.BlockedByMissingValidationEvidence,
                StaleValidationGuardBlockKind.MissingEvidence,
                validation,
                missingEvidence.First());
        }

        var consistencyIssue = ConsistencyIssue(request);
        if (consistencyIssue is not null)
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByInconsistentValidationEvidence,
                StaleValidationGuardBlockKind.InconsistentEvidence,
                validation,
                consistencyIssue);
        }

        if (!CanProceedToNextGate(request))
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence,
                StaleValidationGuardBlockKind.UntrustedEvidence,
                validation,
                "StaleValidationGuardEvidenceCannotProceedToAuthorityGate");
        }

        return Decision(
            request,
            StaleValidationGuardDecisionKind.MayProceedToNextAuthorityGate,
            StaleValidationGuardBlockKind.None,
            validation,
            "StaleValidationGuardFreshPassedEvidenceMayProceedToNextAuthorityGate");
    }

    private static StaleValidationGuardDecision? ValidateDimensions(
        StaleValidationGuardRequest request,
        StaleValidationGuardValidationResult validation)
    {
        if (request.SubjectKind == StaleValidationSubjectKind.Unknown ||
            !Enum.IsDefined(request.SubjectKind))
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.Invalid,
                StaleValidationGuardBlockKind.InvalidRequest,
                validation,
                "StaleValidationGuardSubjectKindUnknown");
        }

        if (request.ValidationEvidenceKind == ValidationEvidenceKind.Unknown ||
            !Enum.IsDefined(request.ValidationEvidenceKind))
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence,
                StaleValidationGuardBlockKind.UntrustedEvidence,
                validation,
                "StaleValidationGuardValidationEvidenceKindUnknown");
        }

        if (request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.Unknown ||
            !Enum.IsDefined(request.EvidenceTrustLevel))
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence,
                StaleValidationGuardBlockKind.UntrustedEvidence,
                validation,
                "StaleValidationGuardEvidenceTrustLevelUnknown");
        }

        if (request.ObservationFreshness == ValidationObservationFreshness.Unknown ||
            !Enum.IsDefined(request.ObservationFreshness))
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByStaleValidation,
                StaleValidationGuardBlockKind.StaleValidation,
                validation,
                "StaleValidationGuardObservationFreshnessUnknown");
        }

        if (request.ValidationOutcome == ValidationOutcomeState.Unknown ||
            !Enum.IsDefined(request.ValidationOutcome))
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByUnknownValidationState,
                StaleValidationGuardBlockKind.UnknownValidationState,
                validation,
                "StaleValidationGuardValidationOutcomeUnknown");
        }

        if (request.ValidationScope == ValidationScopeKind.Unknown ||
            !Enum.IsDefined(request.ValidationScope))
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.Invalid,
                StaleValidationGuardBlockKind.InvalidRequest,
                validation,
                "StaleValidationGuardValidationScopeUnknown");
        }

        return null;
    }

    private static StaleValidationGuardDecision? ValidateFreshness(
        StaleValidationGuardRequest request,
        StaleValidationGuardValidationResult validation)
    {
        if (request.ObservationFreshness == ValidationObservationFreshness.NotTimestamped ||
            request.ObservationFreshness == ValidationObservationFreshness.Stale ||
            (request.RecordedAtUtc - request.ValidatedAtUtc).TotalSeconds > StaleValidationGuardValidator.MaxValidationAgeSeconds)
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByStaleValidation,
                StaleValidationGuardBlockKind.StaleValidation,
                validation,
                "StaleValidationGuardValidationStale");
        }

        if (request.ObservationFreshness == ValidationObservationFreshness.Expired ||
            request.EvidenceExpiresAtUtc is { } expiresAt && expiresAt <= request.RecordedAtUtc)
        {
            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByExpiredValidation,
                StaleValidationGuardBlockKind.ExpiredValidation,
                validation,
                "StaleValidationGuardValidationExpired");
        }

        return null;
    }

    private static StaleValidationGuardDecision? ValidateOutcome(
        StaleValidationGuardRequest request,
        StaleValidationGuardValidationResult validation) =>
        request.ValidationOutcome switch
        {
            ValidationOutcomeState.Failed or
                ValidationOutcomeState.TimedOut or
                ValidationOutcomeState.Cancelled => Decision(
                    request,
                    StaleValidationGuardDecisionKind.BlockedByFailedValidation,
                    StaleValidationGuardBlockKind.FailedValidation,
                    validation,
                    $"StaleValidationGuardValidationFailed:{request.ValidationOutcome}"),
            ValidationOutcomeState.NotRun or
                ValidationOutcomeState.Partial or
                ValidationOutcomeState.Unavailable => Decision(
                    request,
                    StaleValidationGuardDecisionKind.BlockedByIncompleteValidation,
                    StaleValidationGuardBlockKind.IncompleteValidation,
                    validation,
                    $"StaleValidationGuardValidationIncomplete:{request.ValidationOutcome}"),
            _ => null
        };

    private static StaleValidationGuardDecision? ValidateTrust(
        StaleValidationGuardRequest request,
        StaleValidationGuardValidationResult validation)
    {
        if (request.ValidationEvidenceKind == ValidationEvidenceKind.SyntheticTestValidation ||
            request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.TestFixture)
        {
            if (!request.Source.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(
                    request,
                    StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence,
                    StaleValidationGuardBlockKind.UntrustedEvidence,
                    validation,
                    "StaleValidationGuardTestFixtureSourceRequired");
            }

            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence,
                StaleValidationGuardBlockKind.UntrustedEvidence,
                validation,
                "StaleValidationGuardTestFixtureCannotProceedToAuthorityGate");
        }

        if (request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.SelfReported)
        {
            if (!HasCorroboratingEvidence(request))
            {
                return Decision(
                    request,
                    StaleValidationGuardDecisionKind.BlockedByMissingValidationEvidence,
                    StaleValidationGuardBlockKind.MissingEvidence,
                    validation,
                    "StaleValidationGuardSelfReportedCorroborationRequired");
            }

            return Decision(
                request,
                StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence,
                StaleValidationGuardBlockKind.UntrustedEvidence,
                validation,
                "StaleValidationGuardSelfReportedCannotProceedToAuthorityGate");
        }

        return null;
    }

    private static IReadOnlyList<string> RequiredEvidenceIssues(StaleValidationGuardRequest request)
    {
        var missing = new List<string>();

        if (request.ValidationOutcome == ValidationOutcomeState.Passed)
        {
            Require(request.ValidationEvidenceRef, "StaleValidationGuardValidationEvidenceRefRequired", missing);
            Require(request.ObservedValidationTargetRef, "StaleValidationGuardObservedValidationTargetRefRequired", missing);
            Require(request.ObservedValidationFingerprint, "StaleValidationGuardObservedValidationFingerprintRequired", missing);
            Require(request.ConcurrentGuardDecisionRef, "StaleValidationGuardConcurrentGuardDecisionRefRequired", missing);
            Require(request.DirtyWorktreeGuardRef, "StaleValidationGuardDirtyWorktreeGuardRefRequired", missing);
            Require(request.MovedBaseGuardRef, "StaleValidationGuardMovedBaseGuardRefRequired", missing);
            Require(request.PostStateObservationRef, "StaleValidationGuardPostStateObservationRefRequired", missing);
        }

        if (request.ValidationEvidenceKind == ValidationEvidenceKind.CompositeValidationReceipt ||
            request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.ReceiptBacked)
        {
            RequireAnyReceipt(request, "StaleValidationGuardReceiptRefRequired", missing);
        }

        if (request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.BuildReceiptBacked)
        {
            Require(request.BuildReceiptRef, "StaleValidationGuardBuildReceiptRefRequired", missing);
        }

        if (request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.TestReceiptBacked)
        {
            Require(request.TestReceiptRef, "StaleValidationGuardTestReceiptRefRequired", missing);
        }

        if (request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.GovernanceReceiptBacked)
        {
            Require(request.GovernanceReceiptRef, "StaleValidationGuardGovernanceReceiptRefRequired", missing);
        }

        if (request.ValidationEvidenceKind == ValidationEvidenceKind.ProviderCiStatus ||
            request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.ProviderMetadataBacked)
        {
            Require(request.ProviderCiStateRef, "StaleValidationGuardProviderCiStateRefRequired", missing);
        }

        if (request.ValidationEvidenceKind == ValidationEvidenceKind.OperatorReportedValidation ||
            request.EvidenceTrustLevel == ValidationEvidenceTrustLevel.OperatorObserved)
        {
            Require(request.OperatorObservationRef, "StaleValidationGuardOperatorObservationRefRequired", missing);
        }

        return missing
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ConsistencyIssue(StaleValidationGuardRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ExpectedValidationTargetRef) &&
            !Same(request.ExpectedValidationTargetRef, request.ObservedValidationTargetRef))
        {
            return "StaleValidationGuardExpectedValidationTargetMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedValidationFingerprint) &&
            !Same(request.ExpectedValidationFingerprint, request.ObservedValidationFingerprint))
        {
            return "StaleValidationGuardExpectedValidationFingerprintMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedSourceStateRef) &&
            !Same(request.ExpectedSourceStateRef, request.ObservedSourceStateRef))
        {
            return "StaleValidationGuardExpectedSourceStateMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedPatchPackageRef) &&
            !Same(request.ExpectedPatchPackageRef, request.ObservedPatchPackageRef))
        {
            return "StaleValidationGuardExpectedPatchPackageMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedCommitRef) &&
            !Same(request.ExpectedCommitRef, request.ObservedCommitRef))
        {
            return "StaleValidationGuardExpectedCommitMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedHeadRef) &&
            !Same(request.ExpectedHeadRef, request.ObservedHeadRef))
        {
            return "StaleValidationGuardExpectedHeadMismatch";
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedBaseRef) &&
            !Same(request.ExpectedBaseRef, request.ObservedBaseRef))
        {
            return "StaleValidationGuardExpectedBaseMismatch";
        }

        return null;
    }

    private static bool CanProceedToNextGate(StaleValidationGuardRequest request) =>
        request.ValidationOutcome == ValidationOutcomeState.Passed &&
        request.ObservationFreshness == ValidationObservationFreshness.Fresh &&
        request.ValidationEvidenceKind != ValidationEvidenceKind.SyntheticTestValidation &&
        request.EvidenceTrustLevel is ValidationEvidenceTrustLevel.ReceiptBacked
            or ValidationEvidenceTrustLevel.BuildReceiptBacked
            or ValidationEvidenceTrustLevel.TestReceiptBacked
            or ValidationEvidenceTrustLevel.GovernanceReceiptBacked
            or ValidationEvidenceTrustLevel.ProviderMetadataBacked
            or ValidationEvidenceTrustLevel.OperatorObserved;

    private static bool HasCorroboratingEvidence(StaleValidationGuardRequest request) =>
        !string.IsNullOrWhiteSpace(request.ValidationReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.BuildReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.TestReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.GovernanceReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.ProviderCiStateRef) ||
        !string.IsNullOrWhiteSpace(request.OperatorObservationRef) ||
        !string.IsNullOrWhiteSpace(request.PostStateObservationRef) ||
        !string.IsNullOrWhiteSpace(request.DirtyWorktreeGuardRef) ||
        !string.IsNullOrWhiteSpace(request.MovedBaseGuardRef);

    private static void RequireAnyReceipt(
        StaleValidationGuardRequest request,
        string issue,
        ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(request.ValidationReceiptRef) &&
            string.IsNullOrWhiteSpace(request.BuildReceiptRef) &&
            string.IsNullOrWhiteSpace(request.TestReceiptRef) &&
            string.IsNullOrWhiteSpace(request.GovernanceReceiptRef))
        {
            missing.Add(issue);
        }
    }

    private static void Require(string? value, string issue, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(issue);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static StaleValidationGuardDecision Decision(
        StaleValidationGuardRequest request,
        StaleValidationGuardDecisionKind decision,
        StaleValidationGuardBlockKind blockKind,
        StaleValidationGuardValidationResult validation,
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
            ValidationEvidenceKind = request.ValidationEvidenceKind,
            EvidenceTrustLevel = request.EvidenceTrustLevel,
            ObservationFreshness = request.ObservationFreshness,
            ValidationOutcome = request.ValidationOutcome,
            ValidationScope = request.ValidationScope,
            MatchedValidationEvidenceRef = request.ValidationEvidenceRef ?? string.Empty,
            MatchedValidationReceiptRef = request.ValidationReceiptRef ?? string.Empty,
            MatchedBuildReceiptRef = request.BuildReceiptRef ?? string.Empty,
            MatchedTestReceiptRef = request.TestReceiptRef ?? string.Empty,
            MatchedGovernanceReceiptRef = request.GovernanceReceiptRef ?? string.Empty,
            MatchedProviderCiStateRef = request.ProviderCiStateRef ?? string.Empty,
            MatchedOperatorObservationRef = request.OperatorObservationRef ?? string.Empty,
            MatchedPostStateObservationRef = request.PostStateObservationRef ?? string.Empty,
            MatchedDirtyWorktreeGuardRef = request.DirtyWorktreeGuardRef ?? string.Empty,
            MatchedMovedBaseGuardRef = request.MovedBaseGuardRef ?? string.Empty,
            MatchedConcurrentGuardDecisionRef = request.ConcurrentGuardDecisionRef ?? string.Empty,
            MatchedExpectedValidationTargetRef = request.ExpectedValidationTargetRef ?? string.Empty,
            MatchedObservedValidationTargetRef = request.ObservedValidationTargetRef ?? string.Empty,
            MatchedExpectedValidationFingerprint = request.ExpectedValidationFingerprint ?? string.Empty,
            MatchedObservedValidationFingerprint = request.ObservedValidationFingerprint ?? string.Empty,
            MatchedExpectedSourceStateRef = request.ExpectedSourceStateRef ?? string.Empty,
            MatchedObservedSourceStateRef = request.ObservedSourceStateRef ?? string.Empty,
            MatchedExpectedPatchPackageRef = request.ExpectedPatchPackageRef ?? string.Empty,
            MatchedObservedPatchPackageRef = request.ObservedPatchPackageRef ?? string.Empty,
            MatchedExpectedCommitRef = request.ExpectedCommitRef ?? string.Empty,
            MatchedObservedCommitRef = request.ObservedCommitRef ?? string.Empty,
            MatchedExpectedHeadRef = request.ExpectedHeadRef ?? string.Empty,
            MatchedObservedHeadRef = request.ObservedHeadRef ?? string.Empty,
            MatchedExpectedBaseRef = request.ExpectedBaseRef ?? string.Empty,
            MatchedObservedBaseRef = request.ObservedBaseRef ?? string.Empty,
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresFreshConcurrentGuard = true,
            RequiresDirtyWorktreeGuard = true,
            RequiresMovedBaseGuard = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = StaleValidationGuardValidator.BuildRecordFingerprint(request, decision, blockKind)
        };
}
