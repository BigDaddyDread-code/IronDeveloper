namespace IronDev.Core.Governance;

public static class RunAuthorityProfileEvaluator
{
    public static RunAuthorityDecision Evaluate(
        RunAuthorityProfile? profile,
        RunAuthorityOperationKind requestedOperation)
    {
        var validation = RunAuthorityProfileValidator.Validate(profile);
        var profileKind = profile?.Kind ?? AuthorityProfileKind.Unknown;
        var blockedReasons = new List<string>();
        var forbiddenActions = new List<string>();
        var requiredIndependentChecks = new List<string>();

        if (!Enum.IsDefined(requestedOperation) || requestedOperation == RunAuthorityOperationKind.Unknown)
        {
            blockedReasons.Add("RunAuthorityRequestedOperationKnownRequired");
            forbiddenActions.Add("do not treat unknown operation as allowed by run profile");
        }

        if (!validation.IsValid)
        {
            blockedReasons.Add("RunAuthorityProfileInvalid");
            blockedReasons.AddRange(validation.Issues.Select(issue => $"RunAuthorityProfileInvalid:{issue}"));
            forbiddenActions.Add("do not proceed from invalid run authority profile");
            requiredIndependentChecks.Add("valid run authority profile required");
        }

        if (blockedReasons.Count == 0 && profile is not null)
        {
            if (profile.AllowedOperations.Contains(requestedOperation) &&
                !profile.ForbiddenOperations.Contains(requestedOperation))
            {
                requiredIndependentChecks.Add("operation-specific validation still required");
                requiredIndependentChecks.Add("profile allowance is necessary but not sufficient");
                forbiddenActions.Add("do not treat profile allowance as approval");
                forbiddenActions.Add("do not treat profile allowance as policy satisfaction");
                forbiddenActions.Add("do not treat profile allowance as execution authority");
                forbiddenActions.Add("do not mutate durable source from profile allowance");

                return Decision(
                    profileKind,
                    requestedOperation,
                    isAllowedByProfile: true,
                    blockedReasons: [],
                    forbiddenActions: forbiddenActions,
                    requiredIndependentChecks: requiredIndependentChecks);
            }

            blockedReasons.Add(profile.ForbiddenOperations.Contains(requestedOperation)
                ? $"{profileKind} does not allow {requestedOperation}."
                : $"{profileKind} has no allowance for {requestedOperation}.");
            forbiddenActions.Add($"do not perform {requestedOperation} under {profileKind}");
            forbiddenActions.Add("do not infer operation authority from run profile text");
            requiredIndependentChecks.Add("explicit governed authority required outside this profile");
        }

        return Decision(
            profileKind,
            requestedOperation,
            isAllowedByProfile: false,
            blockedReasons: blockedReasons,
            forbiddenActions: forbiddenActions,
            requiredIndependentChecks: requiredIndependentChecks);
    }

    private static RunAuthorityDecision Decision(
        AuthorityProfileKind profileKind,
        RunAuthorityOperationKind requestedOperation,
        bool isAllowedByProfile,
        IReadOnlyCollection<string> blockedReasons,
        IReadOnlyCollection<string> forbiddenActions,
        IReadOnlyCollection<string> requiredIndependentChecks) =>
        new()
        {
            ProfileKind = profileKind,
            RequestedOperation = requestedOperation,
            IsAllowedByProfile = isAllowedByProfile,
            BlockedReasons = Clean(blockedReasons),
            ForbiddenActions = Clean(forbiddenActions),
            RequiredIndependentChecks = Clean(requiredIndependentChecks)
        };

    private static IReadOnlyCollection<string> Clean(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
