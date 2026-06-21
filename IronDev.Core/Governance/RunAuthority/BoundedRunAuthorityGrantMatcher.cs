namespace IronDev.Core.Governance;

public static class BoundedRunAuthorityGrantMatcher
{
    public static BoundedRunAuthorityGrantDecision Evaluate(
        BoundedRunAuthorityGrant? grant,
        DateTimeOffset observedAtUtc,
        string repository,
        string branch,
        string runId,
        RunAuthorityOperationKind requestedOperation,
        string filePath)
    {
        var blocked = new List<string>();
        var forbidden = new List<string>();
        var checks = new List<string>();
        var validation = BoundedRunAuthorityGrantValidator.Validate(grant, observedAtUtc);

        if (!validation.IsValid)
        {
            blocked.Add("BoundedRunAuthorityGrantInvalid");
            blocked.AddRange(validation.Issues.Select(issue => $"BoundedRunAuthorityGrantInvalid:{issue}"));
            forbidden.Add("do not proceed from invalid bounded run grant");
            checks.Add("valid bounded run grant required");
            return Result(false, blocked, forbidden, checks);
        }

        if (grant is null)
            return Result(false, blocked, forbidden, checks);

        if (!Enum.IsDefined(requestedOperation) || requestedOperation == RunAuthorityOperationKind.Unknown)
            blocked.Add("RequestedOperationKnownRequired");
        if (!string.Equals(grant.Repository, repository, StringComparison.OrdinalIgnoreCase))
            blocked.Add("RepositoryMismatch");
        if (!string.Equals(grant.Branch, branch, StringComparison.OrdinalIgnoreCase))
            blocked.Add("BranchMismatch");
        if (!string.Equals(grant.RunId, runId, StringComparison.OrdinalIgnoreCase))
            blocked.Add("RunIdMismatch");
        if (grant.StopBeforeOperationKinds.Contains(requestedOperation))
            blocked.Add($"OperationStoppedBefore:{requestedOperation}");
        if (!grant.AllowedOperationKinds.Contains(requestedOperation))
            blocked.Add($"OperationNotAllowed:{requestedOperation}");
        if (!BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(filePath))
        {
            blocked.Add("RequestedFilePathUnsafe");
        }
        else if (BoundedRunAuthorityGrantFileScope.IsForbidden(filePath, grant.ForbiddenFileGlobs))
        {
            blocked.Add("RequestedFileForbidden");
        }
        else if (!BoundedRunAuthorityGrantFileScope.IsAllowed(filePath, grant.AllowedFileGlobs, grant.ForbiddenFileGlobs))
        {
            blocked.Add("RequestedFileNotAllowed");
        }

        if (blocked.Count > 0)
        {
            forbidden.Add("do not proceed outside bounded run grant envelope");
            checks.Add("separate governed authority required outside this grant envelope");
            return Result(false, blocked, forbidden, checks);
        }

        forbidden.Add("do not treat bounded grant as execution authority");
        forbidden.Add("do not treat bounded grant as approval");
        forbidden.Add("do not treat bounded grant as policy satisfaction");
        checks.Add("grant envelope is necessary but not sufficient");
        checks.Add("operation-specific governance still required");
        checks.Add("required validation evidence still must be checked");
        return Result(true, blocked, forbidden, checks);
    }

    private static BoundedRunAuthorityGrantDecision Result(
        bool isInside,
        IEnumerable<string> blocked,
        IEnumerable<string> forbidden,
        IEnumerable<string> checks) =>
        new()
        {
            IsInsideGrantEnvelope = isInside,
            BlockedReasons = blocked.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ForbiddenActions = forbidden.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RequiredIndependentChecks = checks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
}
