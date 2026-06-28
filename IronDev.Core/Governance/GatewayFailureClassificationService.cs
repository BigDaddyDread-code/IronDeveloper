namespace IronDev.Core.Governance;

public sealed class GatewayFailureClassificationService
{
    public GatewayFailureClassificationDecision Classify(GatewayFailureClassificationRequest request)
    {
        var validation = GatewayFailureClassificationValidator.ValidateRequest(request);
        if (validation.HasUnsafePayload)
        {
            return Decision(
                request,
                GatewayFailureClassificationDecisionKind.BlockedByUnsafePayload,
                GatewayFailureClassificationBlockKind.UnsafePayload,
                GatewayFailureRoutingHint.BlockedUntilClassified,
                validation,
                validation.Issues.FirstOrDefault() ?? "GatewayFailureUnsafePayloadRejected");
        }

        if (validation.Issues.Count > 0)
        {
            return Decision(
                request,
                GatewayFailureClassificationDecisionKind.Invalid,
                GatewayFailureClassificationBlockKind.InvalidRequest,
                GatewayFailureRoutingHint.BlockedUntilClassified,
                validation,
                validation.Issues.First());
        }

        if (validation.MissingEvidence.Count > 0)
        {
            return Decision(
                request,
                GatewayFailureClassificationDecisionKind.BlockedByMissingEvidence,
                GatewayFailureClassificationBlockKind.MissingEvidence,
                GatewayFailureRoutingHint.BlockedUntilClassified,
                validation,
                validation.MissingEvidence.First());
        }

        if (request.FailureClass == GatewayFailureClass.Unknown ||
            !Enum.IsDefined(request.FailureClass))
        {
            return Decision(
                request,
                GatewayFailureClassificationDecisionKind.BlockedByUnknownFailureClass,
                GatewayFailureClassificationBlockKind.UnknownFailureClass,
                GatewayFailureRoutingHint.BlockedUntilClassified,
                validation,
                "GatewayFailureClassUnknown");
        }

        if (request.FailurePhase == GatewayFailurePhase.Unknown ||
            !Enum.IsDefined(request.FailurePhase))
        {
            return Decision(
                request,
                GatewayFailureClassificationDecisionKind.BlockedByUnknownFailurePhase,
                GatewayFailureClassificationBlockKind.UnknownFailurePhase,
                GatewayFailureRoutingHint.BlockedUntilClassified,
                validation,
                "GatewayFailurePhaseUnknown");
        }

        if (request.MutationBoundaryState == GatewayFailureMutationBoundaryState.Unknown ||
            !Enum.IsDefined(request.MutationBoundaryState))
        {
            return Decision(
                request,
                GatewayFailureClassificationDecisionKind.BlockedByUnknownMutationBoundary,
                GatewayFailureClassificationBlockKind.UnknownMutationBoundary,
                GatewayFailureRoutingHint.RequiresPostStateObservation,
                validation,
                "GatewayFailureMutationBoundaryUnknown");
        }

        var evidenceBlock = ValidateSpecialEvidence(request);
        if (evidenceBlock is not null)
        {
            return Decision(
                request,
                evidenceBlock.Value.Decision,
                evidenceBlock.Value.BlockKind,
                evidenceBlock.Value.RoutingHint,
                validation,
                evidenceBlock.Value.Reason);
        }

        var routingHint = RoutingHintFor(request);
        if (routingHint == GatewayFailureRoutingHint.MayProceedToRetryAssessment &&
            request.MutationBoundaryState != GatewayFailureMutationBoundaryState.MutationNotStarted)
        {
            return Decision(
                request,
                GatewayFailureClassificationDecisionKind.BlockedByInconsistentBoundary,
                GatewayFailureClassificationBlockKind.InconsistentBoundary,
                GatewayFailureRoutingHint.RequiresPostStateObservation,
                validation,
                "GatewayFailureRetryAssessmentRequiresMutationNotStarted");
        }

        return Decision(
            request,
            GatewayFailureClassificationDecisionKind.Classified,
            GatewayFailureClassificationBlockKind.None,
            routingHint,
            validation,
            $"GatewayFailureClassified:{request.FailureClass}");
    }

    private static SpecialEvidenceBlock? ValidateSpecialEvidence(GatewayFailureClassificationRequest request)
    {
        if (RequiresIdempotencyEvidence(request) &&
            (string.IsNullOrWhiteSpace(request.IdempotencyKeyRef) ||
                string.IsNullOrWhiteSpace(request.IdempotencyFingerprint)))
        {
            return new SpecialEvidenceBlock(
                GatewayFailureClassificationDecisionKind.BlockedByMissingIdempotencyEvidence,
                GatewayFailureClassificationBlockKind.MissingIdempotencyEvidence,
                GatewayFailureRoutingHint.RequiresManualTriage,
                string.IsNullOrWhiteSpace(request.IdempotencyKeyRef)
                    ? "GatewayFailureIdempotencyKeyRefRequired"
                    : "GatewayFailureIdempotencyFingerprintRequired");
        }

        if (RequiresConcurrentGuardEvidence(request) &&
            string.IsNullOrWhiteSpace(request.ConcurrentGuardDecisionRef))
        {
            return new SpecialEvidenceBlock(
                GatewayFailureClassificationDecisionKind.BlockedByMissingConcurrentGuardEvidence,
                GatewayFailureClassificationBlockKind.MissingConcurrentGuardEvidence,
                GatewayFailureRoutingHint.RequiresManualTriage,
                "GatewayFailureConcurrentGuardDecisionRefRequired");
        }

        if (RequiresLeaseEvidence(request) &&
            string.IsNullOrWhiteSpace(request.LeaseObservationRef))
        {
            return new SpecialEvidenceBlock(
                GatewayFailureClassificationDecisionKind.BlockedByMissingLeaseEvidence,
                GatewayFailureClassificationBlockKind.MissingLeaseEvidence,
                GatewayFailureRoutingHint.RequiresManualTriage,
                "GatewayFailureLeaseObservationRefRequired");
        }

        if (RequiresPostStateObservation(request) &&
            string.IsNullOrWhiteSpace(request.PostStateObservationRef))
        {
            return new SpecialEvidenceBlock(
                GatewayFailureClassificationDecisionKind.BlockedByMissingPostStateObservation,
                GatewayFailureClassificationBlockKind.MissingPostStateObservation,
                GatewayFailureRoutingHint.RequiresPostStateObservation,
                "GatewayFailurePostStateObservationRefRequired");
        }

        return null;
    }

    private static GatewayFailureRoutingHint RoutingHintFor(GatewayFailureClassificationRequest request)
    {
        if (request.MutationBoundaryState is GatewayFailureMutationBoundaryState.MutationMayHaveStarted
            or GatewayFailureMutationBoundaryState.MutationStarted
            or GatewayFailureMutationBoundaryState.MutationPartiallyObserved
            or GatewayFailureMutationBoundaryState.MutationCompleted)
        {
            return request.FailureClass switch
            {
                GatewayFailureClass.RollbackPlanUnavailable => GatewayFailureRoutingHint.RequiresRollbackAssessment,
                GatewayFailureClass.RecoveryPlanUnavailable or GatewayFailureClass.InterruptedRun => GatewayFailureRoutingHint.RequiresRecoveryAssessment,
                _ => GatewayFailureRoutingHint.RequiresPostStateObservation
            };
        }

        if (request.MutationBoundaryState == GatewayFailureMutationBoundaryState.ObservationOnly)
        {
            return request.FailureClass switch
            {
                GatewayFailureClass.ReceiptConflict => GatewayFailureRoutingHint.RequiresReceiptConflictResolution,
                GatewayFailureClass.StatusProjectionFailed or
                    GatewayFailureClass.ReadModelStale or
                    GatewayFailureClass.ReadModelUnavailable => GatewayFailureRoutingHint.RequiresReadModelRebuild,
                GatewayFailureClass.RollbackPlanUnavailable => GatewayFailureRoutingHint.RequiresRollbackAssessment,
                GatewayFailureClass.RecoveryPlanUnavailable or GatewayFailureClass.InterruptedRun => GatewayFailureRoutingHint.RequiresRecoveryAssessment,
                _ => GatewayFailureRoutingHint.RequiresManualTriage
            };
        }

        var phaseRoutingHint = RoutingHintForPhase(request.FailurePhase);
        if (phaseRoutingHint is not null)
        {
            return phaseRoutingHint.Value;
        }

        return request.FailureClass switch
        {
            GatewayFailureClass.PreMutationInfrastructureFailure or
                GatewayFailureClass.PreMutationDependencyUnavailable or
                GatewayFailureClass.PreMutationTimeout or
                GatewayFailureClass.PreMutationLeaseUnavailable or
                GatewayFailureClass.PreMutationConcurrentGuardBlocked or
                GatewayFailureClass.RateLimited or
                GatewayFailureClass.ExternalProviderUnavailable or
                GatewayFailureClass.ExternalProviderTimeout => IsExplicitPreMutationRetryPhase(request.FailurePhase)
                    ? GatewayFailureRoutingHint.MayProceedToRetryAssessment
                    : GatewayFailureRoutingHint.RequiresManualTriage,

            GatewayFailureClass.ValidationFailed or
                GatewayFailureClass.FreshnessExpired or
                GatewayFailureClass.StaleValidation or
                GatewayFailureClass.StalePatch or
                GatewayFailureClass.PatchBaseMoved => GatewayFailureRoutingHint.RequiresFreshValidation,

            GatewayFailureClass.AuthorityBoundaryViolation or
                GatewayFailureClass.ApprovalMissing or
                GatewayFailureClass.ApprovalDenied or
                GatewayFailureClass.PolicyDenied => GatewayFailureRoutingHint.RequiresFreshAuthority,

            GatewayFailureClass.MutationBoundaryUnknown or
                GatewayFailureClass.MutationMayHaveStarted or
                GatewayFailureClass.PartialMutationObserved or
                GatewayFailureClass.ProviderAcceptedButOutcomeUnknown or
                GatewayFailureClass.ProviderRejectedAfterMutationStarted or
                GatewayFailureClass.PostStateUnknown or
                GatewayFailureClass.SourceStateUnknown => GatewayFailureRoutingHint.RequiresPostStateObservation,

            GatewayFailureClass.RollbackPlanUnavailable => GatewayFailureRoutingHint.RequiresRollbackAssessment,
            GatewayFailureClass.RecoveryPlanUnavailable or GatewayFailureClass.InterruptedRun => GatewayFailureRoutingHint.RequiresRecoveryAssessment,
            GatewayFailureClass.ReceiptConflict => GatewayFailureRoutingHint.RequiresReceiptConflictResolution,
            GatewayFailureClass.StatusProjectionFailed or
                GatewayFailureClass.ReadModelStale or
                GatewayFailureClass.ReadModelUnavailable => GatewayFailureRoutingHint.RequiresReadModelRebuild,

            _ => GatewayFailureRoutingHint.RequiresManualTriage
        };
    }

    private static GatewayFailureRoutingHint? RoutingHintForPhase(GatewayFailurePhase phase) =>
        phase switch
        {
            GatewayFailurePhase.ProviderAcceptedBoundaryUnknown or
                GatewayFailurePhase.ProviderRejectedAfterMutationStarted or
                GatewayFailurePhase.PostStateObservation => GatewayFailureRoutingHint.RequiresPostStateObservation,

            GatewayFailurePhase.RollbackPlanning => GatewayFailureRoutingHint.RequiresRollbackAssessment,
            GatewayFailurePhase.RecoveryPlanning => GatewayFailureRoutingHint.RequiresRecoveryAssessment,
            GatewayFailurePhase.WorkflowContinuationPlanning or
                GatewayFailurePhase.ManualCancellation => GatewayFailureRoutingHint.RequiresManualTriage,
            _ => null
        };

    private static bool IsExplicitPreMutationRetryPhase(GatewayFailurePhase phase) =>
        phase is GatewayFailurePhase.PreMutationValidation
            or GatewayFailurePhase.PreMutationDependencyCheck
            or GatewayFailurePhase.ConcurrentMutationGuard
            or GatewayFailurePhase.LeaseObservation;

    private static bool RequiresIdempotencyEvidence(GatewayFailureClassificationRequest request) =>
        request.FailureClass == GatewayFailureClass.IdempotencyConflict ||
        request.FailurePhase == GatewayFailurePhase.IdempotencyEvaluation;

    private static bool RequiresConcurrentGuardEvidence(GatewayFailureClassificationRequest request) =>
        request.FailureClass == GatewayFailureClass.PreMutationConcurrentGuardBlocked ||
        request.FailurePhase == GatewayFailurePhase.ConcurrentMutationGuard;

    private static bool RequiresLeaseEvidence(GatewayFailureClassificationRequest request) =>
        request.FailureClass == GatewayFailureClass.PreMutationLeaseUnavailable ||
        request.FailurePhase == GatewayFailurePhase.LeaseObservation;

    private static bool RequiresPostStateObservation(GatewayFailureClassificationRequest request) =>
        request.FailurePhase is GatewayFailurePhase.PostStateObservation
            or GatewayFailurePhase.ProviderAcceptedBoundaryUnknown
            or GatewayFailurePhase.ProviderRejectedAfterMutationStarted ||
        request.FailureClass is GatewayFailureClass.MutationBoundaryUnknown
            or GatewayFailureClass.MutationMayHaveStarted
            or GatewayFailureClass.PartialMutationObserved
            or GatewayFailureClass.ProviderAcceptedButOutcomeUnknown
            or GatewayFailureClass.ProviderRejectedAfterMutationStarted
            or GatewayFailureClass.PostStateUnknown
            or GatewayFailureClass.SourceStateUnknown ||
        request.MutationBoundaryState is GatewayFailureMutationBoundaryState.MutationMayHaveStarted
            or GatewayFailureMutationBoundaryState.MutationStarted
            or GatewayFailureMutationBoundaryState.MutationPartiallyObserved
            or GatewayFailureMutationBoundaryState.MutationCompleted;

    private static GatewayFailureClassificationDecision Decision(
        GatewayFailureClassificationRequest request,
        GatewayFailureClassificationDecisionKind decision,
        GatewayFailureClassificationBlockKind blockKind,
        GatewayFailureRoutingHint routingHint,
        GatewayFailureClassificationValidationResult validation,
        string reason) =>
        new()
        {
            Decision = decision,
            Reason = reason,
            BlockKind = blockKind,
            FailurePhase = request.FailurePhase,
            FailureClass = request.FailureClass,
            MutationBoundaryState = request.MutationBoundaryState,
            RoutingHint = routingHint,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            CorrelationId = request.CorrelationId,
            MutationSurface = request.MutationSurface,
            AttemptRef = request.AttemptRef,
            GatewayRef = request.GatewayRef,
            FailureRef = request.FailureRef,
            MatchedFailureEvidenceRef = request.FailureEvidenceRef ?? string.Empty,
            MatchedFailureReceiptRef = request.FailureReceiptRef ?? string.Empty,
            MatchedPostStateObservationRef = request.PostStateObservationRef ?? string.Empty,
            MatchedConcurrentGuardDecisionRef = request.ConcurrentGuardDecisionRef ?? string.Empty,
            MatchedLeaseObservationRef = request.LeaseObservationRef ?? string.Empty,
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresFreshConcurrentGuard = true,
            RequiresFreshPostStateObservation = true,
            RequiresHumanReview = true,
            Warnings = validation.Warnings,
            ForbiddenAuthorityImplications = validation.ForbiddenAuthorityImplications,
            RecordFingerprint = GatewayFailureClassificationValidator.BuildRecordFingerprint(
                request,
                decision,
                routingHint,
                blockKind)
        };

    private readonly record struct SpecialEvidenceBlock(
        GatewayFailureClassificationDecisionKind Decision,
        GatewayFailureClassificationBlockKind BlockKind,
        GatewayFailureRoutingHint RoutingHint,
        string Reason);
}
