namespace IronDev.Core.Governance;

public sealed class ConcurrentMutationGuardService
{
    private readonly IConcurrentMutationGuardReadStore _readStore;

    public ConcurrentMutationGuardService(IConcurrentMutationGuardReadStore readStore)
    {
        _readStore = readStore ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<ConcurrentMutationGuardDecision> EvaluateAsync(
        ConcurrentMutationGuardRequest request,
        CancellationToken cancellationToken)
    {
        var requestValidation = ConcurrentMutationGuardValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            return Decision(
                request,
                ConcurrentMutationGuardDecisionKind.Invalid,
                requestValidation.HasUnsafePayload
                    ? ConcurrentMutationConflictKind.UnsafePayload
                    : ConcurrentMutationConflictKind.InvalidRequest,
                requestValidation.Issues.FirstOrDefault() ?? "ConcurrentMutationGuardRequestInvalid");
        }

        var readResult = await _readStore.FindPotentialConflictsAsync(request, cancellationToken).ConfigureAwait(false);
        if (readResult is null)
        {
            return Decision(
                request,
                ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation,
                ConcurrentMutationConflictKind.StaleObservation,
                "ConcurrentMutationGuardReadResultRequired");
        }

        if (readResult.WasTruncated ||
            readResult.Observations.Count > ConcurrentMutationGuardValidator.MaxObservations)
        {
            return Decision(
                request,
                ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation,
                ConcurrentMutationConflictKind.TooManyObservations,
                "ConcurrentMutationGuardObservationWindowTruncated");
        }

        var relevantObservations = readResult.Observations
            .Where(observation => IsRelevant(request, observation))
            .OrderBy(static observation => observation.TenantId, StringComparer.Ordinal)
            .ThenBy(static observation => observation.ProjectId, StringComparer.Ordinal)
            .ThenBy(static observation => observation.MutationSurface)
            .ThenBy(static observation => observation.MutationTargetRef, StringComparer.Ordinal)
            .ThenBy(static observation => observation.AttemptRef, StringComparer.Ordinal)
            .ThenBy(static observation => observation.IdempotencyKeyRef, StringComparer.Ordinal)
            .ThenBy(static observation => observation.IdempotencyFingerprint, StringComparer.Ordinal)
            .ThenBy(static observation => observation.ObservedLeaseRef ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static observation => observation.ObservedFenceRef ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static observation => observation.ObservedSequenceRef ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        foreach (var observation in relevantObservations)
        {
            var observationValidation = ConcurrentMutationGuardValidator.ValidateObservation(observation, request.NowUtc);
            if (observationValidation.IsValid)
            {
                continue;
            }

            if (observationValidation.HasUnsafePayload)
            {
                return Decision(
                    request,
                    ConcurrentMutationGuardDecisionKind.BlockedByUnknownState,
                    ConcurrentMutationConflictKind.UnsafePayload,
                    observationValidation.Issues.FirstOrDefault() ?? "ConcurrentMutationObservationUnsafePayload",
                    observation);
            }

            if (observationValidation.Issues.Any(static issue => issue.Contains("StateUnknown", StringComparison.Ordinal)))
            {
                return Decision(
                    request,
                    ConcurrentMutationGuardDecisionKind.BlockedByUnknownState,
                    ConcurrentMutationConflictKind.UnknownState,
                    "ConcurrentMutationObservationStateUnknown",
                    observation);
            }

            if (observationValidation.Issues.Any(ConcurrentMutationGuardValidator.IsStaleObservationIssue))
            {
                return Decision(
                    request,
                    ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation,
                    ConcurrentMutationConflictKind.StaleObservation,
                    observationValidation.Issues.First(ConcurrentMutationGuardValidator.IsStaleObservationIssue),
                    observation);
            }

            return Decision(
                request,
                ConcurrentMutationGuardDecisionKind.BlockedByUnknownState,
                ConcurrentMutationConflictKind.UnknownState,
                observationValidation.Issues.FirstOrDefault() ?? "ConcurrentMutationObservationInvalid",
                observation);
        }

        foreach (var observation in relevantObservations)
        {
            if (Same(observation.IdempotencyKeyRef, request.IdempotencyKeyRef) &&
                !Same(observation.IdempotencyFingerprint, request.IdempotencyFingerprint))
            {
                return Decision(
                    request,
                    ConcurrentMutationGuardDecisionKind.BlockedByConflictingIdempotency,
                    ConcurrentMutationConflictKind.ConflictingIdempotency,
                    "ConcurrentMutationGuardConflictingIdempotencyFingerprint",
                    observation);
            }
        }

        foreach (var observation in relevantObservations)
        {
            if (observation.ObservedState != ConcurrentMutationObservationState.ObservedHeld)
            {
                continue;
            }

            if (IsSameAttempt(request, observation) &&
                IsSameObservedLease(request, observation))
            {
                return Decision(
                    request,
                    ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate,
                    ConcurrentMutationConflictKind.None,
                    "ConcurrentMutationGuardSameHeldLeaseAttemptMetadataOnly",
                    observation,
                    safeToReuseExistingAttempt: true);
            }

            return Decision(
                request,
                ConcurrentMutationGuardDecisionKind.BlockedByConflictingLease,
                ConcurrentMutationConflictKind.ConflictingLease,
                "ConcurrentMutationGuardConflictingHeldLease",
                observation);
        }

        foreach (var observation in relevantObservations)
        {
            if (!IsActive(observation.ObservedState))
            {
                continue;
            }

            if (IsSameAttempt(request, observation))
            {
                return Decision(
                    request,
                    ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate,
                    ConcurrentMutationConflictKind.None,
                    "ConcurrentMutationGuardSameAttemptMetadataOnly",
                    observation,
                    safeToReuseExistingAttempt: true);
            }

            return Decision(
                request,
                ConcurrentMutationGuardDecisionKind.BlockedByActiveMutation,
                ConcurrentMutationConflictKind.ActiveMutation,
                "ConcurrentMutationGuardActiveMutationForTarget",
                observation);
        }

        var terminalMatch = relevantObservations.FirstOrDefault(observation => IsSameAttempt(request, observation));
        if (terminalMatch is not null)
        {
            return Decision(
                request,
                ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate,
                ConcurrentMutationConflictKind.None,
                "ConcurrentMutationGuardTerminalAttemptMetadataOnly",
                terminalMatch,
                safeToReuseExistingAttempt: true);
        }

        return Decision(
            request,
            ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate,
            ConcurrentMutationConflictKind.None,
            "ConcurrentMutationGuardNoKnownConcurrentConflict");
    }

    private static bool IsRelevant(
        ConcurrentMutationGuardRequest request,
        ConcurrentMutationObservation observation) =>
        Same(request.TenantId, observation.TenantId) &&
        Same(request.ProjectId, observation.ProjectId) &&
        request.MutationSurface == observation.MutationSurface &&
        Same(request.MutationTargetRef, observation.MutationTargetRef);

    private static bool IsSameAttempt(
        ConcurrentMutationGuardRequest request,
        ConcurrentMutationObservation observation) =>
        Same(request.RequestedAttemptRef, observation.AttemptRef) &&
        Same(request.IdempotencyKeyRef, observation.IdempotencyKeyRef) &&
        Same(request.IdempotencyFingerprint, observation.IdempotencyFingerprint);

    private static bool IsSameObservedLease(
        ConcurrentMutationGuardRequest request,
        ConcurrentMutationObservation observation)
    {
        if (string.IsNullOrWhiteSpace(request.ObservedLeaseRef) &&
            string.IsNullOrWhiteSpace(request.ObservedLeaseOwnerRef) &&
            string.IsNullOrWhiteSpace(request.ObservedFenceRef) &&
            string.IsNullOrWhiteSpace(request.ObservedSequenceRef))
        {
            return true;
        }

        return Same(request.ObservedLeaseRef, observation.ObservedLeaseRef) &&
            Same(request.ObservedLeaseOwnerRef, observation.ObservedLeaseOwnerRef) &&
            Same(request.ObservedFenceRef, observation.ObservedFenceRef) &&
            Same(request.ObservedSequenceRef, observation.ObservedSequenceRef);
    }

    private static bool IsActive(ConcurrentMutationObservationState state) =>
        state is ConcurrentMutationObservationState.Requested
            or ConcurrentMutationObservationState.InProgress
            or ConcurrentMutationObservationState.ObservedHeld
            or ConcurrentMutationObservationState.ObservedDenied
            or ConcurrentMutationObservationState.ObservedConflicted
            or ConcurrentMutationObservationState.Unknown;

    private static ConcurrentMutationGuardDecision Decision(
        ConcurrentMutationGuardRequest request,
        ConcurrentMutationGuardDecisionKind decision,
        ConcurrentMutationConflictKind conflictKind,
        string reason,
        ConcurrentMutationObservation? observation = null,
        bool safeToReuseExistingAttempt = false)
    {
        var conflictRef = observation?.AttemptRef ?? string.Empty;
        var matchedAttemptRef = observation?.AttemptRef ?? string.Empty;
        var matchedIdempotencyKeyRef = observation?.IdempotencyKeyRef ?? string.Empty;
        var matchedLeaseRef = observation?.ObservedLeaseRef ?? string.Empty;
        var matchedFenceRef = observation?.ObservedFenceRef ?? string.Empty;
        var matchedSequenceRef = observation?.ObservedSequenceRef ?? string.Empty;

        var warnings = ConcurrentMutationGuardValidator.RequiredWarnings.ToList();
        if (safeToReuseExistingAttempt)
        {
            warnings.Add("safe attempt reuse is metadata only");
        }

        warnings.AddRange(ConcurrentMutationGuardValidator.RequiredForbiddenAuthorityImplications);

        return new ConcurrentMutationGuardDecision
        {
            Decision = decision,
            Reason = reason,
            ConflictKind = conflictKind,
            ConflictRef = conflictRef,
            MatchedAttemptRef = matchedAttemptRef,
            MatchedIdempotencyKeyRef = matchedIdempotencyKeyRef,
            MatchedLeaseRef = matchedLeaseRef,
            MatchedFenceRef = matchedFenceRef,
            MatchedSequenceRef = matchedSequenceRef,
            SafeToReuseExistingAttempt = safeToReuseExistingAttempt,
            RequiresFreshAuthority = true,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            RecordFingerprint = ConcurrentMutationGuardValidator.BuildRecordFingerprint(
                request,
                decision,
                conflictRef,
                matchedAttemptRef,
                matchedIdempotencyKeyRef,
                matchedLeaseRef,
                matchedFenceRef,
                matchedSequenceRef)
        };
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
