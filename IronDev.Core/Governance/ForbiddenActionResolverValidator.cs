namespace IronDev.Core.Governance;

public static class ForbiddenActionResolverValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "forbidden action resolver is read-only",
        "forbidden action resolver is diagnostic only",
        "forbidden action resolver is not operation identity",
        "forbidden action resolver is not operation lookup",
        "forbidden action resolver is not correlation authority",
        "forbidden action resolver is not timeline assembly",
        "forbidden action resolver is not status projection",
        "forbidden action resolver is not missing evidence calculation",
        "forbidden action resolver is not evidence payload resolution",
        "forbidden action resolver is not receipt resolution",
        "forbidden action resolver is not validation freshness",
        "forbidden action resolver is not patch/base freshness",
        "forbidden action resolver is not worktree/base/head freshness",
        "forbidden action resolver is not policy satisfaction",
        "forbidden action resolver is not approval",
        "forbidden action resolver is not next-safe-action formatting",
        "forbidden action resolver is not authority-warning formatting",
        "forbidden action resolver is not source apply",
        "forbidden action resolver is not rollback",
        "forbidden action resolver is not retry permission",
        "forbidden action resolver is not commit",
        "forbidden action resolver is not push",
        "forbidden action resolver is not PR creation",
        "forbidden action resolver is not merge readiness",
        "forbidden action resolver is not release readiness",
        "forbidden action resolver is not deployment readiness",
        "forbidden action resolver is not memory promotion",
        "forbidden action resolver is not workflow continuation",
        "no forbidden facts observed is not action allowed",
        "forbidden action result is not policy decision",
        "ambiguous facts are not denial or approval",
        "diagnostic severity is not authority severity",
        "projected status is not permission",
        "evidence complete is not permission"
    ];

    public static ForbiddenActionResolutionResult ValidateRequest(ForbiddenActionResolverRequest? request)
    {
        if (request is null)
        {
            return Invalid(["ForbiddenActionResolverRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "ForbiddenActionTenantIdRequired", "ForbiddenActionTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "ForbiddenActionProjectIdRequired", "ForbiddenActionProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.ActionKind == ForbiddenActionKind.Unknown ||
            !Enum.IsDefined(request.ActionKind))
        {
            issues.Add("ForbiddenActionKindRequired");
        }

        if (request.ProjectedStatusKind != OperationProjectedStatusKind.Unknown &&
            !Enum.IsDefined(request.ProjectedStatusKind))
        {
            issues.Add("ForbiddenActionProjectedStatusKindInvalid");
        }

        if (request.MissingEvidenceStatus != MissingEvidenceResolutionStatus.Unknown &&
            !Enum.IsDefined(request.MissingEvidenceStatus))
        {
            issues.Add("ForbiddenActionMissingEvidenceStatusInvalid");
        }

        if (request.Facts is null)
        {
            issues.Add("ForbiddenActionFactsRequired");
        }
        else
        {
            foreach (var fact in request.Facts)
            {
                AddFactIssues(fact, issues);

                if (fact is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, fact.TenantId))
                {
                    issues.Add("ForbiddenActionFactTenantMismatch");
                }

                if (!Same(request.ProjectId, fact.ProjectId))
                {
                    issues.Add("ForbiddenActionFactProjectMismatch");
                }

                if (!Same(request.OperationId, fact.OperationId))
                {
                    issues.Add("ForbiddenActionFactOperationMismatch");
                }
            }
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.ActionKind,
            ForbiddenActionResolutionStatus.InvalidRequest);
    }

    private static void AddFactIssues(
        ForbiddenActionInputFact? fact,
        ICollection<string> issues)
    {
        if (fact is null)
        {
            issues.Add("ForbiddenActionFactRequired");
            return;
        }

        AddScopeIssues(fact.TenantId, "ForbiddenActionFactTenantIdRequired", "ForbiddenActionFactTenantIdInvalid", issues);
        AddScopeIssues(fact.ProjectId, "ForbiddenActionFactProjectIdRequired", "ForbiddenActionFactProjectIdInvalid", issues);
        AddOperationIdIssues(fact.OperationId, issues);
        AddIdIssues(fact.FactId, "ForbiddenActionFactIdRequired", "ForbiddenActionFactIdInvalid", issues);

        if (fact.FactKind == ForbiddenActionFactKind.Unknown ||
            !Enum.IsDefined(fact.FactKind))
        {
            issues.Add("ForbiddenActionFactKindRequired");
        }

        if (fact.Severity == ForbiddenActionFactSeverity.Unknown ||
            !Enum.IsDefined(fact.Severity))
        {
            issues.Add("ForbiddenActionFactSeverityRequired");
        }

        AddSourceIssues(fact.Source, "ForbiddenActionFactSourceRequired", "ForbiddenActionFactSourceInvalid", issues);

        if (fact.ObservedAtUtc == default)
        {
            issues.Add("ForbiddenActionFactObservedAtRequired");
        }

        AddSurfacePairIssues(fact, issues);
        AddReferencePairIssues(fact, issues);
        AddDisplayLabelIssues(fact.DisplayLabel, issues);

        if (fact.IsRedacted && string.IsNullOrWhiteSpace(fact.RedactionReason))
        {
            issues.Add("ForbiddenActionFactRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(fact.RedactionReason) &&
            (ContainsUnsafeText(fact.RedactionReason) ||
                ContainsSecretMarker(fact.RedactionReason) ||
                ContainsRawPayloadMarker(fact.RedactionReason)))
        {
            issues.Add("ForbiddenActionFactRedactionReasonInvalid");
        }
    }

    private static void AddSurfacePairIssues(
        ForbiddenActionInputFact fact,
        ICollection<string> issues)
    {
        var hasSurfaceKind = fact.SurfaceKind != OperationCorrelationSurfaceKind.Unknown &&
            Enum.IsDefined(fact.SurfaceKind);
        var hasSurfaceId = !string.IsNullOrWhiteSpace(fact.SurfaceId);

        if (!hasSurfaceKind && !hasSurfaceId)
        {
            return;
        }

        if (!hasSurfaceKind)
        {
            issues.Add("ForbiddenActionFactSurfaceKindRequired");
        }

        if (!hasSurfaceId)
        {
            issues.Add("ForbiddenActionFactSurfaceIdRequired");
            return;
        }

        if (ContainsUnsafeText(fact.SurfaceId) ||
            fact.SurfaceId.Any(char.IsWhiteSpace))
        {
            issues.Add("ForbiddenActionFactSurfaceIdInvalid");
        }
    }

    private static void AddReferencePairIssues(
        ForbiddenActionInputFact fact,
        ICollection<string> issues)
    {
        var hasReferenceKind = fact.ReferenceKind != OperationReferenceKind.Unknown &&
            Enum.IsDefined(fact.ReferenceKind);
        var hasReferenceId = !string.IsNullOrWhiteSpace(fact.ReferenceId);

        if (!hasReferenceKind && !hasReferenceId)
        {
            return;
        }

        if (!hasReferenceKind)
        {
            issues.Add("ForbiddenActionFactReferenceKindRequired");
        }

        if (!hasReferenceId)
        {
            issues.Add("ForbiddenActionFactReferenceIdRequired");
            return;
        }

        if (ContainsUnsafeText(fact.ReferenceId) ||
            fact.ReferenceId.Any(char.IsWhiteSpace) ||
            IsUrl(fact.ReferenceId))
        {
            issues.Add("ForbiddenActionFactReferenceIdInvalid");
        }
    }

    private static void AddDisplayLabelIssues(
        string? displayLabel,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(displayLabel))
        {
            return;
        }

        if (ContainsUnsafeText(displayLabel) ||
            ContainsSecretMarker(displayLabel) ||
            ContainsRawPayloadMarker(displayLabel))
        {
            issues.Add("ForbiddenActionFactDisplayLabelInvalid");
        }
    }

    private static void AddScopeIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            ContainsAuthorityText(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddOperationIdIssues(string? operationId, ICollection<string> issues)
    {
        var result = OperationIdentityValidator.ValidateOperationId(operationId);
        foreach (var issue in result.Issues)
        {
            issues.Add(issue);
        }
    }

    private static void AddIdIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            IsUrl(value) ||
            ContainsAuthorityText(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddSourceIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            ContainsAuthorityText(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static ForbiddenActionResolutionResult Invalid(IReadOnlyList<string> issues) =>
        Result(issues, string.Empty, string.Empty, string.Empty, ForbiddenActionKind.Unknown, ForbiddenActionResolutionStatus.InvalidRequest);

    private static ForbiddenActionResolutionResult Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        ForbiddenActionKind actionKind,
        ForbiddenActionResolutionStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? ForbiddenActionResolutionStatus.NoForbiddenFactsObserved : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            ActionKind = actionKind,
            Findings = [],
            AmbiguousFacts = [],
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<string> Warnings() =>
    [
        "no forbidden facts observed is not action permission",
        "projected status is diagnostic input only",
        "missing evidence status is diagnostic input only"
    ];

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) || value.Length > 512;

    private static bool ContainsAuthorityText(string value)
    {
        var markers = new[]
        {
            "approval granted",
            "approved for",
            "policy satisfied",
            "authority granted",
            "action allowed",
            "allowed to",
            "ready for review",
            "merge ready",
            "release ready",
            "deploy now",
            "continue workflow",
            "retry authorized",
            "rollback authorized",
            "ship it"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsRawPayloadMarker(string value)
    {
        var markers = new[]
        {
            "raw evidence payload",
            "raw receipt payload",
            "raw validation log",
            "raw request body",
            "raw response body",
            "full patch",
            "full diff"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecretMarker(string value)
    {
        var markers = new[]
        {
            "authorization:",
            "bearer ",
            "api key",
            "apikey",
            "password",
            "secret",
            "token=",
            "connection string",
            "private key"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
