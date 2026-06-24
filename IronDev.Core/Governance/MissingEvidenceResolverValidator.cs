namespace IronDev.Core.Governance;

public static class MissingEvidenceResolverValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "missing evidence resolver is read-only",
        "missing evidence resolver is not operation identity",
        "missing evidence resolver is not operation lookup",
        "missing evidence resolver is not correlation authority",
        "missing evidence resolver is not timeline assembly",
        "missing evidence resolver is not status projection",
        "missing evidence resolver is not evidence payload resolution",
        "missing evidence resolver is not receipt resolution",
        "missing evidence resolver is not validation freshness",
        "missing evidence resolver is not policy satisfaction",
        "missing evidence resolver is not approval",
        "missing evidence resolver is not blocked-state explanation",
        "missing evidence resolver is not forbidden-action resolution",
        "missing evidence resolver is not next-safe-action formatting",
        "missing evidence resolver is not authority-warning formatting",
        "missing evidence resolver is not source apply",
        "missing evidence resolver is not rollback",
        "missing evidence resolver is not retry permission",
        "missing evidence resolver is not commit",
        "missing evidence resolver is not push",
        "missing evidence resolver is not PR creation",
        "missing evidence resolver is not merge readiness",
        "missing evidence resolver is not release readiness",
        "missing evidence resolver is not deployment readiness",
        "missing evidence resolver is not memory promotion",
        "missing evidence resolver is not workflow continuation",
        "evidence present is not approval",
        "evidence missing is not a policy decision",
        "evidence ambiguity is not denial",
        "redacted metadata is not raw payload",
        "requirement severity is not authority severity",
        "complete evidence presence is not action allowed"
    ];

    public static MissingEvidenceResolutionResult ValidateRequest(MissingEvidenceResolverRequest? request)
    {
        if (request is null)
        {
            return Invalid(["MissingEvidenceResolverRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "MissingEvidenceTenantIdRequired", "MissingEvidenceTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "MissingEvidenceProjectIdRequired", "MissingEvidenceProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.Requirements is null)
        {
            issues.Add("MissingEvidenceRequirementsRequired");
        }

        if (request.ObservedEvidence is null)
        {
            issues.Add("MissingEvidenceObservedEvidenceRequired");
        }

        if (request.Requirements is not null)
        {
            foreach (var requirement in request.Requirements)
            {
                AddRequirementIssues(requirement, issues);

                if (requirement is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, requirement.TenantId))
                {
                    issues.Add("MissingEvidenceRequirementTenantMismatch");
                }

                if (!Same(request.ProjectId, requirement.ProjectId))
                {
                    issues.Add("MissingEvidenceRequirementProjectMismatch");
                }

                if (!Same(request.OperationId, requirement.OperationId))
                {
                    issues.Add("MissingEvidenceRequirementOperationMismatch");
                }
            }
        }

        if (request.ObservedEvidence is not null)
        {
            foreach (var observed in request.ObservedEvidence)
            {
                AddObservedEvidenceIssues(observed, issues);

                if (observed is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, observed.TenantId))
                {
                    issues.Add("MissingEvidenceObservedTenantMismatch");
                }

                if (!Same(request.ProjectId, observed.ProjectId))
                {
                    issues.Add("MissingEvidenceObservedProjectMismatch");
                }

                if (!Same(request.OperationId, observed.OperationId))
                {
                    issues.Add("MissingEvidenceObservedOperationMismatch");
                }
            }
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            MissingEvidenceResolutionStatus.InvalidRequest);
    }

    private static void AddRequirementIssues(
        MissingEvidenceRequirement? requirement,
        ICollection<string> issues)
    {
        if (requirement is null)
        {
            issues.Add("MissingEvidenceRequirementRequired");
            return;
        }

        AddScopeIssues(requirement.TenantId, "MissingEvidenceRequirementTenantIdRequired", "MissingEvidenceRequirementTenantIdInvalid", issues);
        AddScopeIssues(requirement.ProjectId, "MissingEvidenceRequirementProjectIdRequired", "MissingEvidenceRequirementProjectIdInvalid", issues);
        AddOperationIdIssues(requirement.OperationId, issues);

        AddIdIssues(requirement.RequirementId, "MissingEvidenceRequirementIdRequired", "MissingEvidenceRequirementIdInvalid", issues);

        if (requirement.RequirementKind == MissingEvidenceRequirementKind.Unknown ||
            !Enum.IsDefined(requirement.RequirementKind))
        {
            issues.Add("MissingEvidenceRequirementKindRequired");
        }

        AddLabelIssues(requirement.RequiredLabel, "MissingEvidenceRequiredLabelRequired", "MissingEvidenceRequiredLabelInvalid", issues);
        AddLabelIssues(requirement.RequiredFor, "MissingEvidenceRequiredForRequired", "MissingEvidenceRequiredForInvalid", issues);

        if (requirement.Severity == MissingEvidenceRequirementSeverity.Unknown ||
            !Enum.IsDefined(requirement.Severity))
        {
            issues.Add("MissingEvidenceRequirementSeverityRequired");
        }

        AddSourceIssues(requirement.Source, "MissingEvidenceRequirementSourceRequired", "MissingEvidenceRequirementSourceInvalid", issues);

        if (requirement.CreatedAtUtc == default)
        {
            issues.Add("MissingEvidenceRequirementCreatedAtRequired");
        }
    }

    private static void AddObservedEvidenceIssues(
        ObservedEvidenceReference? observed,
        ICollection<string> issues)
    {
        if (observed is null)
        {
            issues.Add("MissingEvidenceObservedReferenceRequired");
            return;
        }

        AddScopeIssues(observed.TenantId, "MissingEvidenceObservedTenantIdRequired", "MissingEvidenceObservedTenantIdInvalid", issues);
        AddScopeIssues(observed.ProjectId, "MissingEvidenceObservedProjectIdRequired", "MissingEvidenceObservedProjectIdInvalid", issues);
        AddOperationIdIssues(observed.OperationId);

        AddObservedCorrelationIssues(observed, issues);
        AddIdIssues(observed.ObservedEvidenceId, "MissingEvidenceObservedEvidenceIdRequired", "MissingEvidenceObservedEvidenceIdInvalid", issues);

        if (observed.EvidenceKind == ObservedEvidenceKind.Unknown ||
            !Enum.IsDefined(observed.EvidenceKind))
        {
            issues.Add("MissingEvidenceObservedEvidenceKindRequired");
        }

        if (observed.SurfaceKind == OperationCorrelationSurfaceKind.Unknown ||
            !Enum.IsDefined(observed.SurfaceKind))
        {
            issues.Add("MissingEvidenceObservedSurfaceKindRequired");
        }

        AddIdIssues(observed.SurfaceId, "MissingEvidenceObservedSurfaceIdRequired", "MissingEvidenceObservedSurfaceIdInvalid", issues);
        AddReferencePairIssues(observed, issues);

        if (observed.ObservedAtUtc == default)
        {
            issues.Add("MissingEvidenceObservedAtRequired");
        }

        AddSourceIssues(observed.Source, "MissingEvidenceObservedSourceRequired", "MissingEvidenceObservedSourceInvalid", issues);

        if (observed.IsRedacted && string.IsNullOrWhiteSpace(observed.RedactionReason))
        {
            issues.Add("MissingEvidenceObservedRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(observed.RedactionReason) &&
            (ContainsUnsafeText(observed.RedactionReason) ||
                ContainsSecretMarker(observed.RedactionReason) ||
                ContainsAuthorityText(observed.RedactionReason)))
        {
            issues.Add("MissingEvidenceObservedRedactionReasonInvalid");
        }

        void AddOperationIdIssues(string? operationId)
        {
            var result = OperationIdentityValidator.ValidateOperationId(operationId);
            foreach (var issue in result.Issues)
            {
                issues.Add(issue);
            }
        }
    }

    private static void AddObservedCorrelationIssues(
        ObservedEvidenceReference observed,
        ICollection<string> issues)
    {
        var link = new OperationCorrelationLink
        {
            TenantId = observed.TenantId,
            ProjectId = observed.ProjectId,
            OperationId = observed.OperationId,
            CorrelationId = observed.CorrelationId,
            SurfaceKind = observed.SurfaceKind,
            SurfaceId = observed.SurfaceId,
            ObservedAtUtc = observed.ObservedAtUtc,
            Source = observed.Source
        };

        var result = OperationCorrelationValidator.ValidateLink(link);
        foreach (var issue in result.Issues)
        {
            issues.Add($"MissingEvidenceObservedCorrelation:{issue}");
        }
    }

    private static void AddReferencePairIssues(
        ObservedEvidenceReference observed,
        ICollection<string> issues)
    {
        var hasReferenceKind = observed.ReferenceKind != OperationReferenceKind.Unknown &&
            Enum.IsDefined(observed.ReferenceKind);
        var hasReferenceId = !string.IsNullOrWhiteSpace(observed.ReferenceId);

        if (!hasReferenceKind && !hasReferenceId)
        {
            return;
        }

        if (!hasReferenceKind)
        {
            issues.Add("MissingEvidenceObservedReferenceKindRequired");
        }

        if (!hasReferenceId)
        {
            issues.Add("MissingEvidenceObservedReferenceIdRequired");
            return;
        }

        if (ContainsUnsafeText(observed.ReferenceId) ||
            observed.ReferenceId.Any(char.IsWhiteSpace))
        {
            issues.Add("MissingEvidenceObservedReferenceIdInvalid");
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

    private static void AddLabelIssues(
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
            ContainsSecretMarker(value) ||
            ContainsRawPayloadMarker(value))
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

    private static MissingEvidenceResolutionResult Invalid(IReadOnlyList<string> issues) =>
        Result(issues, string.Empty, string.Empty, string.Empty, MissingEvidenceResolutionStatus.InvalidRequest);

    private static MissingEvidenceResolutionResult Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        MissingEvidenceResolutionStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? MissingEvidenceResolutionStatus.Complete : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            MissingEvidence = [],
            SatisfiedEvidence = [],
            AmbiguousEvidence = [],
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

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
