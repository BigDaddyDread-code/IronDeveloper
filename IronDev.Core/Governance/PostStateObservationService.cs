namespace IronDev.Core.Governance;

public sealed class PostStateObservationService
{
    public PostStateObservationDecision Evaluate(PostStateObservationRequest request)
    {
        var validation = PostStateObservationValidator.ValidateRequest(request);
        if (validation.HasUnsafePayload)
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnsafePayload,
                PostStateObservationBlockKind.UnsafePayload,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                validation.Issues.FirstOrDefault() ?? "PostStateObservationUnsafePayloadRejected");
        }

        if (validation.Issues.Count > 0)
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.Invalid,
                PostStateObservationBlockKind.InvalidRequest,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                validation.Issues.First());
        }

        var dimensionBlock = ValidateObservationDimensions(request, validation);
        if (dimensionBlock is not null)
        {
            return dimensionBlock;
        }

        var staleBlock = ValidateFreshness(request, validation);
        if (staleBlock is not null)
        {
            return staleBlock;
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
                PostStateObservationDecisionKind.BlockedByMissingEvidence,
                PostStateObservationBlockKind.MissingEvidence,
                SignalForTransition(request.ObservedTransition),
                validation,
                missingEvidence.First());
        }

        var consistencyIssue = StateConsistencyIssue(request);
        if (consistencyIssue is not null)
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByInconsistentState,
                PostStateObservationBlockKind.InconsistentState,
                PostStateBoundarySignal.RequiresPostStateTriage,
                validation,
                consistencyIssue);
        }

        var signal = BoundarySignal(request);
        return Decision(
            request,
            PostStateObservationDecisionKind.AcceptedAsPostStateEvidence,
            PostStateObservationBlockKind.None,
            signal,
            validation,
            $"PostStateObservationAccepted:{request.ObservedTransition}");
    }

    private static PostStateObservationDecision? ValidateObservationDimensions(
        PostStateObservationRequest request,
        PostStateObservationValidationResult validation)
    {
        if (request.SubjectKind == PostStateObservationSubjectKind.Unknown ||
            !Enum.IsDefined(request.SubjectKind))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnknownSubject,
                PostStateObservationBlockKind.UnknownSubject,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservationSubjectKindUnknown");
        }

        if (request.ObservationMethod == PostStateObservationMethod.Unknown ||
            !Enum.IsDefined(request.ObservationMethod))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnknownMethod,
                PostStateObservationBlockKind.UnknownMethod,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservationMethodUnknown");
        }

        if (request.ObservedTransition == PostStateObservedTransition.Unknown ||
            !Enum.IsDefined(request.ObservedTransition))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnknownTransition,
                PostStateObservationBlockKind.UnknownTransition,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservedTransitionUnknown");
        }

        if (request.TransitionExpectation == PostStateTransitionExpectation.Unknown &&
            request.ObservedTransition != PostStateObservedTransition.ObservationUnavailable)
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnknownTransition,
                PostStateObservationBlockKind.UnknownTransition,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateTransitionExpectationUnknown");
        }

        if (!Enum.IsDefined(request.TransitionExpectation))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnknownTransition,
                PostStateObservationBlockKind.UnknownTransition,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateTransitionExpectationUnknown");
        }

        if (request.ObservationCompleteness == PostStateObservationCompleteness.Unknown ||
            !Enum.IsDefined(request.ObservationCompleteness))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnknownCompleteness,
                PostStateObservationBlockKind.UnknownCompleteness,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservationCompletenessUnknown");
        }

        if (request.ObservationTrustLevel == PostStateObservationTrustLevel.Unknown ||
            !Enum.IsDefined(request.ObservationTrustLevel))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUnknownTrustLevel,
                PostStateObservationBlockKind.UnknownTrustLevel,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservationTrustLevelUnknown");
        }

        return null;
    }

    private static PostStateObservationDecision? ValidateFreshness(
        PostStateObservationRequest request,
        PostStateObservationValidationResult validation)
    {
        if ((request.RecordedAtUtc - request.ObservedAtUtc).TotalSeconds > PostStateObservationValidator.MaxObservationAgeSeconds ||
            request.ObservationCompleteness == PostStateObservationCompleteness.Stale)
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByStaleObservation,
                PostStateObservationBlockKind.StaleObservation,
                PostStateBoundarySignal.RequiresFreshObservation,
                validation,
                "PostStateObservationStale");
        }

        if (request.ObservationExpiresAtUtc is { } expiresAt &&
            expiresAt <= request.RecordedAtUtc)
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByExpiredObservation,
                PostStateObservationBlockKind.ExpiredObservation,
                PostStateBoundarySignal.RequiresFreshObservation,
                validation,
                "PostStateObservationExpired");
        }

        return null;
    }

    private static PostStateObservationDecision? ValidateTrust(
        PostStateObservationRequest request,
        PostStateObservationValidationResult validation)
    {
        if (request.ObservationMethod == PostStateObservationMethod.SyntheticTestObservation &&
            !request.Source.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUntrustedObservation,
                PostStateObservationBlockKind.UntrustedObservation,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservationSyntheticSourceRequired");
        }

        if (request.ObservationTrustLevel == PostStateObservationTrustLevel.TestFixture &&
            !request.Source.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUntrustedObservation,
                PostStateObservationBlockKind.UntrustedObservation,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservationTestFixtureSourceRequired");
        }

        if (request.ObservationTrustLevel == PostStateObservationTrustLevel.SelfReported &&
            !HasCorroboratingEvidence(request))
        {
            return Decision(
                request,
                PostStateObservationDecisionKind.BlockedByUntrustedObservation,
                PostStateObservationBlockKind.UntrustedObservation,
                PostStateBoundarySignal.RequiresManualTriage,
                validation,
                "PostStateObservationSelfReportedCorroborationRequired");
        }

        return null;
    }

    private static IReadOnlyList<string> RequiredEvidenceIssues(PostStateObservationRequest request)
    {
        var missing = new List<string>();

        if (request.ObservationTrustLevel == PostStateObservationTrustLevel.ReadModelBacked &&
            string.IsNullOrWhiteSpace(request.ReadModelStateRef))
        {
            missing.Add("PostStateObservationReadModelStateRefRequired");
        }

        if ((request.ObservationTrustLevel == PostStateObservationTrustLevel.ReceiptBacked ||
                request.ObservationMethod == PostStateObservationMethod.ReceiptReadback) &&
            string.IsNullOrWhiteSpace(request.FailureReceiptRef) &&
            string.IsNullOrWhiteSpace(request.MutationReceiptRef))
        {
            missing.Add("PostStateObservationReceiptRefRequired");
        }

        switch (request.ObservedTransition)
        {
            case PostStateObservedTransition.NoChangeObserved:
                Require(request.PreStateRef, "PostStateObservationPreStateRefRequired", missing);
                Require(request.PreStateFingerprint, "PostStateObservationPreStateFingerprintRequired", missing);
                Require(request.ObservedPostStateRef, "PostStateObservationObservedPostStateRefRequired", missing);
                Require(request.ObservedPostStateFingerprint, "PostStateObservationObservedPostStateFingerprintRequired", missing);
                break;

            case PostStateObservedTransition.ExpectedChangeObserved:
                Require(request.PreStateRef, "PostStateObservationPreStateRefRequired", missing);
                Require(request.ExpectedPostStateRef, "PostStateObservationExpectedPostStateRefRequired", missing);
                Require(request.ExpectedPostStateFingerprint, "PostStateObservationExpectedPostStateFingerprintRequired", missing);
                Require(request.ObservedPostStateRef, "PostStateObservationObservedPostStateRefRequired", missing);
                Require(request.ObservedPostStateFingerprint, "PostStateObservationObservedPostStateFingerprintRequired", missing);
                Require(request.MutationReceiptRef, "PostStateObservationMutationReceiptRefRequired", missing);
                break;

            case PostStateObservedTransition.UnexpectedChangeObserved:
                Require(request.PreStateRef, "PostStateObservationPreStateRefRequired", missing);
                Require(request.ObservedPostStateRef, "PostStateObservationObservedPostStateRefRequired", missing);
                Require(request.ObservedPostStateFingerprint, "PostStateObservationObservedPostStateFingerprintRequired", missing);
                Require(request.FailureClassificationRef, "PostStateObservationFailureClassificationRefRequired", missing);
                break;

            case PostStateObservedTransition.PartialChangeObserved:
                Require(request.PreStateRef, "PostStateObservationPreStateRefRequired", missing);
                Require(request.ObservedPostStateRef, "PostStateObservationObservedPostStateRefRequired", missing);
                Require(request.ObservedPostStateFingerprint, "PostStateObservationObservedPostStateFingerprintRequired", missing);
                Require(request.FailureClassificationRef, "PostStateObservationFailureClassificationRefRequired", missing);
                break;

            case PostStateObservedTransition.DivergentChangeObserved:
                Require(request.PreStateRef, "PostStateObservationPreStateRefRequired", missing);
                Require(request.ExpectedPostStateRef, "PostStateObservationExpectedPostStateRefRequired", missing);
                Require(request.ObservedPostStateRef, "PostStateObservationObservedPostStateRefRequired", missing);
                Require(request.ObservedPostStateFingerprint, "PostStateObservationObservedPostStateFingerprintRequired", missing);
                Require(request.FailureClassificationRef, "PostStateObservationFailureClassificationRefRequired", missing);
                break;

            case PostStateObservedTransition.ProviderAcceptedOutcomeUnknown:
                Require(request.ProviderStateRef, "PostStateObservationProviderStateRefRequired", missing);
                Require(request.FailureClassificationRef, "PostStateObservationFailureClassificationRefRequired", missing);
                break;

            case PostStateObservedTransition.ProviderRejectedBeforeMutation:
                Require(request.FailureClassificationRef, "PostStateObservationFailureClassificationRefRequired", missing);
                Require(request.ProviderStateRef, "PostStateObservationProviderStateRefRequired", missing);
                break;

            case PostStateObservedTransition.ProviderRejectedAfterMutationStarted:
                Require(request.ProviderStateRef, "PostStateObservationProviderStateRefRequired", missing);
                Require(request.ObservedPostStateRef, "PostStateObservationObservedPostStateRefRequired", missing);
                Require(request.FailureClassificationRef, "PostStateObservationFailureClassificationRefRequired", missing);
                break;

            case PostStateObservedTransition.ObservationUnavailable:
            case PostStateObservedTransition.ObservationFailed:
                Require(request.FailureClassificationRef, "PostStateObservationFailureClassificationRefRequired", missing);
                break;
        }

        return missing
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? StateConsistencyIssue(PostStateObservationRequest request)
    {
        if (request.ObservedTransition == PostStateObservedTransition.NoChangeObserved &&
            !Same(request.PreStateFingerprint, request.ObservedPostStateFingerprint))
        {
            return "PostStateObservationNoChangeFingerprintMismatch";
        }

        if (request.ObservedTransition == PostStateObservedTransition.ExpectedChangeObserved)
        {
            if (!Same(request.ExpectedPostStateFingerprint, request.ObservedPostStateFingerprint))
            {
                return "PostStateObservationExpectedFingerprintMismatch";
            }

            if (Same(request.PreStateFingerprint, request.ObservedPostStateFingerprint))
            {
                return "PostStateObservationExpectedChangeDidNotChange";
            }
        }

        return null;
    }

    private static PostStateBoundarySignal BoundarySignal(PostStateObservationRequest request) =>
        request.ObservedTransition switch
        {
            PostStateObservedTransition.NoChangeObserved => CanSupportRetryAssessment(request)
                ? PostStateBoundarySignal.SupportsRetryAssessmentOnly
                : PostStateBoundarySignal.RequiresManualTriage,
            PostStateObservedTransition.ProviderRejectedBeforeMutation => CanSupportRetryAssessment(request)
                ? PostStateBoundarySignal.SupportsRetryAssessmentOnly
                : PostStateBoundarySignal.RequiresPostStateTriage,
            PostStateObservedTransition.ExpectedChangeObserved => PostStateBoundarySignal.RequiresPostStateTriage,
            PostStateObservedTransition.UnexpectedChangeObserved => PostStateBoundarySignal.RequiresPostStateTriage,
            PostStateObservedTransition.PartialChangeObserved => PostStateBoundarySignal.RequiresRecoveryAssessment,
            PostStateObservedTransition.DivergentChangeObserved => PostStateBoundarySignal.RequiresManualTriage,
            PostStateObservedTransition.ProviderAcceptedOutcomeUnknown => PostStateBoundarySignal.RequiresPostStateTriage,
            PostStateObservedTransition.ProviderRejectedAfterMutationStarted => PostStateBoundarySignal.RequiresPostStateTriage,
            PostStateObservedTransition.ObservationUnavailable => PostStateBoundarySignal.RequiresFreshObservation,
            PostStateObservedTransition.ObservationFailed => PostStateBoundarySignal.RequiresFreshObservation,
            _ => PostStateBoundarySignal.RequiresManualTriage
        };

    private static PostStateBoundarySignal SignalForTransition(PostStateObservedTransition transition) =>
        transition switch
        {
            PostStateObservedTransition.ObservationUnavailable or
                PostStateObservedTransition.ObservationFailed => PostStateBoundarySignal.RequiresFreshObservation,
            PostStateObservedTransition.PartialChangeObserved => PostStateBoundarySignal.RequiresRecoveryAssessment,
            PostStateObservedTransition.ProviderAcceptedOutcomeUnknown or
                PostStateObservedTransition.ProviderRejectedAfterMutationStarted or
                PostStateObservedTransition.UnexpectedChangeObserved => PostStateBoundarySignal.RequiresPostStateTriage,
            _ => PostStateBoundarySignal.RequiresManualTriage
        };

    private static bool CanSupportRetryAssessment(PostStateObservationRequest request) =>
        request.ObservationCompleteness == PostStateObservationCompleteness.Complete &&
        (request.ObservationTrustLevel is PostStateObservationTrustLevel.ProviderMetadata
                or PostStateObservationTrustLevel.LocalMetadata
                or PostStateObservationTrustLevel.ReceiptBacked
                or PostStateObservationTrustLevel.ReadModelBacked
                or PostStateObservationTrustLevel.OperatorObserved) &&
        request.ObservedTransition is PostStateObservedTransition.NoChangeObserved
            or PostStateObservedTransition.ProviderRejectedBeforeMutation;

    private static bool HasCorroboratingEvidence(PostStateObservationRequest request) =>
        !string.IsNullOrWhiteSpace(request.FailureReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.MutationReceiptRef) ||
        !string.IsNullOrWhiteSpace(request.ProviderStateRef) ||
        !string.IsNullOrWhiteSpace(request.ReadModelStateRef);

    private static void Require(string? value, string issue, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(issue);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static PostStateObservationDecision Decision(
        PostStateObservationRequest request,
        PostStateObservationDecisionKind decision,
        PostStateObservationBlockKind blockKind,
        PostStateBoundarySignal boundarySignal,
        PostStateObservationValidationResult validation,
        string reason) =>
        new()
        {
            Decision = decision,
            Reason = reason,
            BlockKind = blockKind,
            BoundarySignal = boundarySignal,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            CorrelationId = request.CorrelationId,
            MutationSurface = request.MutationSurface,
            AttemptRef = request.AttemptRef,
            TargetRef = request.TargetRef,
            ObservationRef = request.ObservationRef,
            SubjectKind = request.SubjectKind,
            ObservationMethod = request.ObservationMethod,
            TransitionExpectation = request.TransitionExpectation,
            ObservedTransition = request.ObservedTransition,
            ObservationCompleteness = request.ObservationCompleteness,
            ObservationTrustLevel = request.ObservationTrustLevel,
            MatchedPreStateRef = request.PreStateRef ?? string.Empty,
            MatchedExpectedPostStateRef = request.ExpectedPostStateRef ?? string.Empty,
            MatchedObservedPostStateRef = request.ObservedPostStateRef ?? string.Empty,
            MatchedFailureClassificationRef = request.FailureClassificationRef ?? string.Empty,
            MatchedFailureReceiptRef = request.FailureReceiptRef ?? string.Empty,
            MatchedMutationReceiptRef = request.MutationReceiptRef ?? string.Empty,
            MatchedProviderStateRef = request.ProviderStateRef ?? string.Empty,
            MatchedReadModelStateRef = request.ReadModelStateRef ?? string.Empty,
            MatchedConcurrentGuardDecisionRef = request.ConcurrentGuardDecisionRef ?? string.Empty,
            MatchedLeaseObservationRef = request.LeaseObservationRef ?? string.Empty,
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresFreshConcurrentGuard = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = PostStateObservationValidator.BuildRecordFingerprint(
                request,
                decision,
                boundarySignal,
                blockKind)
        };
}
