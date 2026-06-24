namespace IronDev.Core.Governance;

public sealed class SafeRetryContractService
{
    private readonly ISafeRetryAttemptReadStore _readStore;

    public SafeRetryContractService(ISafeRetryAttemptReadStore readStore)
    {
        _readStore = readStore ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<SafeRetryAssessmentDecision> EvaluateAsync(
        SafeRetryAssessmentRequest request,
        CancellationToken cancellationToken)
    {
        var requestValidation = SafeRetryContractValidator.ValidateRequest(request);
        if (requestValidation.HasUnsafePayload)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByUnsafePayload,
                SafeRetryAssessmentBlockKind.UnsafePayload,
                requestValidation.Issues.FirstOrDefault() ??
                requestValidation.MissingReceiptEvidence.FirstOrDefault() ??
                "SafeRetryUnsafePayloadRejected");
        }

        if (requestValidation.Issues.Count > 0)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.Invalid,
                SafeRetryAssessmentBlockKind.InvalidRequest,
                requestValidation.Issues.First());
        }

        if (requestValidation.MissingReceiptEvidence.Count > 0)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByMissingReceiptEvidence,
                SafeRetryAssessmentBlockKind.MissingReceiptEvidence,
                requestValidation.MissingReceiptEvidence.First());
        }

        var preReadDecision = ClassifyWithoutLineage(request);
        if (preReadDecision is not null)
        {
            return preReadDecision;
        }

        var lineage = await _readStore.FindRetryLineageAsync(request, cancellationToken).ConfigureAwait(false);
        var lineageValidation = SafeRetryContractValidator.ValidateLineage(request, lineage);
        if (lineageValidation.HasUnsafePayload)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByUnsafePayload,
                SafeRetryAssessmentBlockKind.UnsafePayload,
                lineageValidation.Issues.FirstOrDefault() ??
                lineageValidation.MissingReceiptEvidence.FirstOrDefault() ??
                "SafeRetryLineageUnsafePayloadRejected");
        }

        if (lineageValidation.Issues.Any(static issue => issue.Contains("Truncated", StringComparison.Ordinal)))
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByRetryBudget,
                SafeRetryAssessmentBlockKind.RetryBudget,
                "SafeRetryLineageReadWindowTruncated");
        }

        if (lineageValidation.Issues.Count > 0)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.Invalid,
                SafeRetryAssessmentBlockKind.InvalidRequest,
                lineageValidation.Issues.First());
        }

        if (lineageValidation.MissingReceiptEvidence.Count > 0)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByMissingReceiptEvidence,
                SafeRetryAssessmentBlockKind.MissingReceiptEvidence,
                lineageValidation.MissingReceiptEvidence.First());
        }

        if (request.PriorRetryCount >= request.MaxRetryCount)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByRetryBudget,
                SafeRetryAssessmentBlockKind.RetryBudget,
                "SafeRetryBudgetExceeded");
        }

        if (Same(request.PreviousIdempotencyKeyRef, request.ProposedRetryIdempotencyKeyRef) &&
            !Same(request.PreviousIdempotencyFingerprint, request.ProposedRetryIdempotencyFingerprint))
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByConflictingIdempotency,
                SafeRetryAssessmentBlockKind.ConflictingIdempotency,
                "SafeRetryConflictingIdempotencyFingerprint");
        }

        if (!SafeRetryContractValidator.IsCandidateFailureClass(request.FailureClass))
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByUnsafeFailureClass,
                SafeRetryAssessmentBlockKind.UnsafeFailureClass,
                $"SafeRetryFailureClassNotRetryCandidate:{request.FailureClass}");
        }

        return Decision(
            request,
            SafeRetryAssessmentDecisionKind.RetryRequestMayProceedToAuthorityGate,
            SafeRetryAssessmentBlockKind.None,
            "SafeRetryPreMutationFailureMayProceedToAuthorityGate",
            safeRetryCandidateForNextGate: true);
    }

    private static SafeRetryAssessmentDecision? ClassifyWithoutLineage(
        SafeRetryAssessmentRequest request)
    {
        if (request.FailedAttemptOutcome == SafeRetryAttemptOutcome.Succeeded)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedBySucceededAttempt,
                SafeRetryAssessmentBlockKind.SucceededAttempt,
                "SafeRetrySucceededAttemptCannotRetry");
        }

        if (request.FailedAttemptOutcome == SafeRetryAttemptOutcome.Cancelled)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByCancelledAttempt,
                SafeRetryAssessmentBlockKind.CancelledAttempt,
                "SafeRetryCancelledAttemptCannotRetry");
        }

        if (request.FailedAttemptOutcome == SafeRetryAttemptOutcome.Interrupted)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByInterruptedAttempt,
                SafeRetryAssessmentBlockKind.InterruptedAttempt,
                "SafeRetryInterruptedAttemptRequiresRecovery");
        }

        if (request.FailedAttemptOutcome is SafeRetryAttemptOutcome.Unknown
            or SafeRetryAttemptOutcome.Requested
            or SafeRetryAttemptOutcome.InProgress)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByNonTerminalAttempt,
                SafeRetryAssessmentBlockKind.NonTerminalAttempt,
                "SafeRetryAttemptNotTerminalFailed");
        }

        if (request.FailureClass == SafeRetryFailureClass.Unknown)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByUnknownFailureClass,
                SafeRetryAssessmentBlockKind.UnknownFailureClass,
                "SafeRetryFailureClassUnknown");
        }

        if (request.MutationBoundaryState == SafeRetryMutationBoundaryState.Unknown)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByMutationBoundaryUnknown,
                SafeRetryAssessmentBlockKind.MutationBoundaryUnknown,
                "SafeRetryMutationBoundaryUnknown");
        }

        if (request.MutationBoundaryState is SafeRetryMutationBoundaryState.Started
            or SafeRetryMutationBoundaryState.PartiallyObserved
            or SafeRetryMutationBoundaryState.Completed)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByMutationMayHaveStarted,
                SafeRetryAssessmentBlockKind.MutationMayHaveStarted,
                "SafeRetryMutationMayHaveStarted");
        }

        if (request.FailureClass == SafeRetryFailureClass.PostStateUnknown)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByUnknownPostState,
                SafeRetryAssessmentBlockKind.UnknownPostState,
                "SafeRetryPostStateUnknown");
        }

        if (request.FailureClass == SafeRetryFailureClass.IdempotencyConflict)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByConflictingIdempotency,
                SafeRetryAssessmentBlockKind.ConflictingIdempotency,
                "SafeRetryFailureClassIdempotencyConflict");
        }

        if (request.CurrentGuardDecision != SafeRetryCurrentGuardState.AllowedToProceedToNextGate)
        {
            return Decision(
                request,
                SafeRetryAssessmentDecisionKind.BlockedByConcurrentMutationGuard,
                SafeRetryAssessmentBlockKind.ConcurrentMutationGuard,
                $"SafeRetryCurrentGuardDecisionBlocked:{request.CurrentGuardDecision}");
        }

        return null;
    }

    private static SafeRetryAssessmentDecision Decision(
        SafeRetryAssessmentRequest request,
        SafeRetryAssessmentDecisionKind decision,
        SafeRetryAssessmentBlockKind blockKind,
        string reason,
        bool safeRetryCandidateForNextGate = false)
    {
        var warnings = SafeRetryContractValidator.RequiredWarnings.ToArray();
        var forbidden = SafeRetryContractValidator.RequiredForbiddenAuthorityImplications.ToArray();

        return new SafeRetryAssessmentDecision
        {
            Decision = decision,
            Reason = reason,
            BlockKind = blockKind,
            FailedAttemptRef = request.FailedAttemptRef,
            RetryLineageRef = request.RetryLineageRef,
            MatchedFailureReceiptRef = request.FailureReceiptRef,
            MatchedTerminalOutcomeRef = request.TerminalOutcomeRef,
            MatchedPostStateObservationRef = request.PostStateObservationRef,
            MatchedGuardDecisionRef = request.CurrentGuardDecisionRef,
            SafeRetryCandidateForNextGate = safeRetryCandidateForNextGate,
            RequiresFreshAuthority = true,
            RequiresFreshValidation = true,
            RequiresFreshConcurrentGuard = true,
            RequiresFreshPostStateObservation = true,
            Warnings = warnings,
            ForbiddenAuthorityImplications = forbidden,
            RecordFingerprint = SafeRetryContractValidator.BuildRecordFingerprint(
                request,
                decision,
                blockKind)
        };
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
