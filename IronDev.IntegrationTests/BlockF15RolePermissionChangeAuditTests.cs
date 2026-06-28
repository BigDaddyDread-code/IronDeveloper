using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF15RolePermissionChangeAuditTests
{
    [TestMethod]
    public void AuditRequestValidatesSafeInput()
    {
        var validation = RolePermissionAuditValidator.ValidateRequest(Request());

        Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Issues));
        Assert.AreEqual(0, validation.UnsafeRefs.Count);
    }

    [DataTestMethod]
    [DataRow("audit record grants permission", RolePermissionAuditClassification.BlockedByAuthorityGrantLanguage)]
    [DataRow("audit trail applies role change", RolePermissionAuditClassification.BlockedByAuthorityGrantLanguage)]
    [DataRow("role assigned", RolePermissionAuditClassification.BlockedByPerformedChangeLanguage)]
    [DataRow("permission granted", RolePermissionAuditClassification.BlockedByPerformedChangeLanguage)]
    public void AuditRequestRejectsUnsafeAuthorityOrAppliedChangeText(
        string unsafeText,
        RolePermissionAuditClassification expected)
    {
        var decision = Create(Request() with { SourceEvidenceRef = unsafeText });

        Assert.AreEqual(expected, decision.Classification);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        CollectionAssert.DoesNotContain(decision.EvidenceRefs.ToList(), unsafeText);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("raw provider response", RolePermissionAuditClassification.BlockedByRawMaterial)]
    [DataRow("raw source", RolePermissionAuditClassification.BlockedByRawMaterial)]
    [DataRow("raw log", RolePermissionAuditClassification.BlockedByRawMaterial)]
    [DataRow("private reasoning", RolePermissionAuditClassification.BlockedByPrivateReasoningMaterial)]
    public void AuditRequestRejectsRawOrPrivateReasoningText(
        string unsafeText,
        RolePermissionAuditClassification expected)
    {
        var decision = Create(Request() with { SourceEvidenceRef = unsafeText });

        Assert.AreEqual(expected, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuditRequestRejectsSecretText()
    {
        var secretLike = string.Concat("to", "ken", "=fake");

        var decision = Create(Request() with { SourceEvidenceRef = secretLike });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedBySecretMaterial, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuditRequestRejectsCredentialText()
    {
        var credentialLike = string.Concat("pass", "word", "=fake");

        var decision = Create(Request() with { SourceEvidenceRef = credentialLike });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByCredentialMaterial, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownEventKindFailsClosed()
    {
        var decision = Create(Request() with { RequestedEventKind = RolePermissionAuditEventKind.Unknown });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByUnknownEvent, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownSubjectKindFailsClosed()
    {
        var decision = Create(Request() with { RequestedSubjectKind = RolePermissionAuditSubjectKind.Unknown });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByUnknownSubject, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownOutcomeKindFailsClosed()
    {
        var decision = Create(Request() with { RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Unknown });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByUnknownOutcome, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownAuthoritySourceKindFailsClosed()
    {
        var decision = Create(Request() with { RequestedAuthoritySourceKind = RolePermissionAuditAuthoritySourceKind.Unknown });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByUnknownAuthoritySource, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("RoleCatalogEvidenceRef", RolePermissionAuditClassification.BlockedByMissingRoleCatalogEvidence)]
    [DataRow("ForbiddenActionCatalogEvidenceRef", RolePermissionAuditClassification.BlockedByMissingForbiddenActionCatalogEvidence)]
    [DataRow("SourceEvidenceRef", RolePermissionAuditClassification.BlockedByMissingSourceEvidence)]
    public void MissingRequiredEvidenceFailsClosed(
        string missingRef,
        RolePermissionAuditClassification expected)
    {
        var request = missingRef switch
        {
            "RoleCatalogEvidenceRef" => Request() with { RoleCatalogEvidenceRef = string.Empty },
            "ForbiddenActionCatalogEvidenceRef" => Request() with { ForbiddenActionCatalogEvidenceRef = string.Empty },
            "SourceEvidenceRef" => Request() with { SourceEvidenceRef = string.Empty },
            _ => Request()
        };

        var decision = Create(request);

        Assert.AreEqual(expected, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void InvalidF01RoleCatalogFailsClosed()
    {
        var decision = Service().CreateAuditRecordCandidate(
            RoleCatalog() with { Entries = [] },
            ForbiddenCatalog(),
            Request());

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByMissingRoleCatalogEvidence, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void InvalidF13ForbiddenActionCatalogFailsClosed()
    {
        var decision = Service().CreateAuditRecordCandidate(
            RoleCatalog(),
            ForbiddenCatalog() with { Entries = [] },
            Request());

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByMissingForbiddenActionCatalogEvidence, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RequestedRoleIdMustBeKnownWhenProvided()
    {
        var decision = Create(Request() with { RequestedRoleId = "role:f15:not-present" });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByUnknownSubject, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RequestedTargetRoleIdMustBeKnownWhenProvided()
    {
        var decision = Create(Request() with { RequestedTargetRoleId = "role:f15:not-present" });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByUnknownSubject, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuditRecordCandidateIsImmutableAuditOnlyAppendOnlyContract()
    {
        var decision = CandidateFromOmittedF13(Request() with
        {
            RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRequested,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Requested
        });

        Assert.AreEqual(RolePermissionAuditClassification.AuditRecordCandidate, decision.Classification);
        Assert.IsTrue(decision.Record.IsAuditOnly);
        Assert.IsTrue(decision.Record.IsImmutableRecord);
        Assert.IsTrue(decision.Record.IsAppendOnlyContract);
        AssertRecordAuthorityFlagsFalse(decision.Record);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(RolePermissionAuditEventKind.RoleAssignmentRequested, RoleForbiddenActionKind.RoleAssignment)]
    [DataRow(RolePermissionAuditEventKind.PermissionGrantRequested, RoleForbiddenActionKind.PermissionManagement)]
    [DataRow(RolePermissionAuditEventKind.AccessGrantRequested, RoleForbiddenActionKind.AccessGrant)]
    [DataRow(RolePermissionAuditEventKind.VisibilityGrantRequested, RoleForbiddenActionKind.VisibilityGrant)]
    [DataRow(RolePermissionAuditEventKind.ExternalAccessRequested, RoleForbiddenActionKind.ExternalAccessGrant)]
    [DataRow(RolePermissionAuditEventKind.TenantBoundaryOverrideRequested, RoleForbiddenActionKind.CrossTenantVisibility)]
    [DataRow(RolePermissionAuditEventKind.PlatformPermissionRequested, RoleForbiddenActionKind.PlatformVisibility)]
    public void AuditEventsMapDefensivelyToF13Actions(
        RolePermissionAuditEventKind eventKind,
        RoleForbiddenActionKind expected)
    {
        Assert.AreEqual(expected, RolePermissionAuditService.MapEventToForbiddenAction(eventKind));
    }

    [TestMethod]
    public void F13ForbiddenLookupProducesBlockedAuditRecordCandidate()
    {
        var decision = Create(Request() with
        {
            RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRequested,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Requested
        });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByForbiddenActionCatalog, decision.Classification);
        Assert.AreEqual(RolePermissionAuditEventKind.RoleAssignmentRequested, decision.Record.EventKind);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void F13OmissionDoesNotBecomeAllowedOrApplied()
    {
        var decision = CandidateFromOmittedF13(Request() with
        {
            RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRequested,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Requested
        });

        Assert.AreEqual(RolePermissionAuditClassification.AuditRecordCandidate, decision.Classification);
        Assert.IsFalse(decision.IsAppliedChange);
        Assert.IsFalse(decision.IsRecordedAuthority);
        Assert.IsFalse(decision.Record.GrantsRoleAssignmentAuthority);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
        AssertRecordAuthorityFlagsFalse(decision.Record);
    }

    [TestMethod]
    public void BlockedEventProducesBlockedAuditRecordCandidateWhenF13DoesNotForceBlock()
    {
        var decision = CandidateFromOmittedF13(Request() with
        {
            RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentAttemptBlocked,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Blocked
        });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedAuditRecordCandidate, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RejectedEventProducesRejectedAuditRecordCandidateWhenF13DoesNotForceBlock()
    {
        var decision = CandidateFromOmittedF13(Request() with
        {
            RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRejected,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Rejected
        });

        Assert.AreEqual(RolePermissionAuditClassification.RejectedAuditRecordCandidate, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuditOnlyObservationProducesAuditOnlyObservationCandidate()
    {
        var decision = Create(Request() with
        {
            RequestedEventKind = RolePermissionAuditEventKind.AuditOnlyObservation,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.AuditOnly
        });

        Assert.AreEqual(RolePermissionAuditClassification.AuditOnlyObservationCandidate, decision.Classification);
        Assert.IsTrue(decision.Record.IsAuditOnly);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void OutcomeVocabularyDoesNotContainAppliedGrantedAuthorizedSucceededOrCompleted()
    {
        var names = Enum.GetNames<RolePermissionAuditOutcomeKind>();
        foreach (var forbidden in new[] { "Applied", "Granted", "Authorized", "Succeeded", "Completed", "Accepted" })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void ClassificationVocabularyDoesNotContainAppliedGrantedAuthorizedAllowedSucceededOrCompleted()
    {
        var names = Enum.GetNames<RolePermissionAuditClassification>();
        foreach (var forbidden in new[] { "Applied", "Granted", "Authorized", "Allowed", "Succeeded", "Completed" })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void EveryDecisionAndRecordHasAllAuthorityActionAndDisclosureFlagsFalse()
    {
        var decisions = new[]
        {
            Create(Request()),
            Create(Request() with { RequestedEventKind = RolePermissionAuditEventKind.AuditOnlyObservation, RequestedOutcomeKind = RolePermissionAuditOutcomeKind.AuditOnly }),
            Create(Request() with { SourceEvidenceRef = "raw source" }),
            CandidateFromOmittedF13(Request() with { RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRequested }),
            CandidateFromOmittedF13(Request() with { RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentAttemptBlocked, RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Blocked }),
            CandidateFromOmittedF13(Request() with { RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRejected, RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Rejected })
        };

        foreach (var decision in decisions)
        {
            AssertDecisionAuthorityFlagsFalse(decision);
            AssertRecordAuthorityFlagsFalse(decision.Record);
        }
    }

    [TestMethod]
    public void AuditRecordCandidateIsNotPersistedOrRuntimeAuditWriter()
    {
        var source = F15CoreSourceWithoutStrings();

        foreach (var forbiddenToken in new[] { "AuditStore", "AppendAsync", "SaveChanges", "DbContext", "SqlConnection", "IRepository" })
        {
            Assert.IsFalse(source.Contains(forbiddenToken, StringComparison.Ordinal), forbiddenToken);
        }
    }

    [TestMethod]
    public void StaticScanF15AddsNoRuntimeMutationPersistenceOrAuthorizationSurface()
    {
        var source = F15CoreSourceWithoutStrings();

        foreach (var forbiddenToken in new[]
        {
            "ApiController",
            "ControllerBase",
            "MapGet",
            "MapPost",
            "HttpGet",
            "HttpPost",
            "OpenApi",
            "DbContext",
            "SqlConnection",
            "IRepository",
            "HttpClient",
            "ProcessStartInfo",
            "ClaimsPrincipal",
            "UserManager",
            "RoleManager",
            "IAuthorizationHandler",
            "AuthorizationHandler",
            "PermissionResolver",
            "AccessControl",
            "RoleAssignmentService",
            "PermissionService",
            "AccessGrantService",
            "EvidenceWriter",
            "EvidenceStore",
            "AuditStore",
            "AppendAsync",
            "SaveChanges",
            "WorkflowRunner",
            "SourceApplyExecutor",
            "CommitGateway",
            "PushGateway",
            "PullRequestGateway",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "RetryExecutor",
            "RollbackExecutor",
            "RecoveryExecutor",
            "AllowAnonymous",
            "AuthorizeAttribute"
        })
        {
            Assert.IsFalse(
                source.Contains(forbiddenToken, StringComparison.Ordinal),
                $"Unexpected runtime/authority surface token found: {forbiddenToken}");
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesRolePermissionAuditRecordsAreNotAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F15_ROLE_PERMISSION_CHANGE_AUDIT_TRAIL.md"));

        StringAssert.Contains(receipt, "Role/permission audit records are not role/permission authority.");
        StringAssert.Contains(receipt, "Writing down a power change is not performing it.");
        StringAssert.Contains(receipt, "does not implement role assignment");
        StringAssert.Contains(receipt, "does not implement permission grant");
        StringAssert.Contains(receipt, "does not implement audit persistence");
        StringAssert.Contains(receipt, "F09 boundary tests remain intentionally deferred.");
    }

    private static RolePermissionAuditDecision Create(RolePermissionAuditRequest request) =>
        Service().CreateAuditRecordCandidate(RoleCatalog(), ForbiddenCatalog(), request);

    private static RolePermissionAuditDecision CandidateFromOmittedF13(RolePermissionAuditRequest request)
    {
        var action = RolePermissionAuditService.MapEventToForbiddenAction(request.RequestedEventKind);
        var catalog = ForbiddenCatalog();
        var entries = catalog.Entries
            .Where(entry => !string.Equals(entry.RoleId, request.RequestedRoleId, StringComparison.OrdinalIgnoreCase) ||
                entry.RoleForbiddenActionKind != action)
            .ToArray();

        return Service().CreateAuditRecordCandidate(RoleCatalog(), catalog with { Entries = entries }, request);
    }

    private static RolePermissionAuditRequest Request() =>
        new()
        {
            CorrelationId = "correlation-f15",
            RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRequested,
            RequestedSubjectKind = RolePermissionAuditSubjectKind.Role,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Requested,
            RequestedAuthoritySourceKind = RolePermissionAuditAuthoritySourceKind.RoleEvidence,
            RequestedRoleId = RoleId(GovernanceRoleKind.Requester),
            RequestedTargetRoleId = RoleId(GovernanceRoleKind.Reviewer),
            RequestedPermissionKey = "permission:f15:review-evidence",
            RequestedActorRef = "actor-ref:f15:requester",
            RequestedTenantRef = "tenant-ref:f15",
            RequestedProjectRef = "project-ref:f15",
            RequestedOperationRef = "operation-ref:f15",
            RoleCatalogEvidenceRef = "role-catalog:f15",
            ForbiddenActionCatalogEvidenceRef = "forbidden-action-catalog:f15",
            MissingEvidenceVisibilityEvidenceRef = "missing-evidence-visibility:f15",
            SourceEvidenceRef = "role-permission-audit-source:f15",
            OptionalPolicyEvidenceRef = "policy-evidence:f15",
            OptionalApprovalEvidenceRef = "approval-evidence:f15",
            OptionalTenantBoundaryEvidenceRef = "tenant-boundary-evidence:f15",
            OptionalRedactionEvidenceRef = "redaction-evidence:f15",
            PreviousAuditRecordFingerprint = "previous-audit-record:f15"
        };

    private static RolePermissionAuditService Service() => new();

    private static GovernanceRoleCatalog RoleCatalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static ForbiddenActionCatalog ForbiddenCatalog() =>
        new ForbiddenActionCatalogService().BuildDefaultCatalog(RoleCatalog());

    private static string RoleId(GovernanceRoleKind kind) =>
        RoleCatalog().Entries.Single(entry => entry.RoleKind == kind).RoleId;

    private static void AssertDecisionAuthorityFlagsFalse(RolePermissionAuditDecision decision)
    {
        Assert.IsFalse(decision.IsRecordedAuthority);
        Assert.IsFalse(decision.IsAppliedChange);
        Assert.IsFalse(decision.IsAuthorizationDecision);
        Assert.IsFalse(decision.IsPermissionDecision);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
    }

    private static void AssertRecordAuthorityFlagsFalse(RolePermissionAuditRecord record)
    {
        Assert.IsFalse(record.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(record.GrantsPermissionAuthority);
        Assert.IsFalse(record.GrantsAccess);
        Assert.IsFalse(record.GrantsVisibilityAuthority);
        Assert.IsFalse(record.GrantsExternalAccess);
        Assert.IsFalse(record.GrantsTenantBoundaryOverride);
        Assert.IsFalse(record.GrantsPlatformAuthority);
        Assert.IsFalse(record.AcceptsApproval);
        Assert.IsFalse(record.SatisfiesPolicy);
        Assert.IsFalse(record.RefreshesValidation);
        Assert.IsFalse(record.ProvesSourceSafety);
        Assert.IsFalse(record.CreatesEvidence);
        Assert.IsFalse(record.SatisfiesEvidence);
        Assert.IsFalse(record.OverridesMissingEvidence);
        Assert.IsFalse(record.WaivesEvidenceRequirement);
        Assert.IsFalse(record.AllowsExecution);
        Assert.IsFalse(record.AllowsMutation);
        Assert.IsFalse(record.AllowsWorkflowContinuation);
        Assert.IsFalse(record.AllowsMerge);
        Assert.IsFalse(record.AllowsRelease);
        Assert.IsFalse(record.AllowsDeployment);
        Assert.IsFalse(record.BypassesRedaction);
        Assert.IsFalse(record.DisclosesSecrets);
        Assert.IsFalse(record.DisclosesCredentials);
        Assert.IsFalse(record.DisclosesRawPayload);
        Assert.IsFalse(record.DisclosesPrivateReasoning);
        Assert.IsTrue(record.RequiresSeparateAuthority);
    }

    private static string F15CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "RolePermissionAudit*.cs");
        return string.Join(Environment.NewLine, files.Select(path => StripStringLiterals(File.ReadAllText(path))));
    }

    private static string StripStringLiterals(string source) =>
        Regex.Replace(source, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"", RegexOptions.CultureInvariant);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Unable to locate repository root.");
    }
}
