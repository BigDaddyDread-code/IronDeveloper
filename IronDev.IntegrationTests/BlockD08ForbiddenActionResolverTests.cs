using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD08ForbiddenActionResolverTests
{
    private const string TenantId = "tenant-d08";
    private const string ProjectId = "project-d08";
    private const string OperationId = "op_0000000000000008";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T07:00:00Z");

    [TestMethod]
    public void ValidRequestWithNoFacts_ReturnsNoForbiddenFactsObserved()
    {
        var result = Resolve([]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, result.ResolutionStatus);
        Assert.AreEqual(0, result.Findings.Count);
        AssertContains(result.Warnings, "no forbidden facts observed is not action permission");
        AssertContains(result.ForbiddenAuthorityImplications, "no forbidden facts observed is not action allowed");
    }

    [DataTestMethod]
    [DataRow(ForbiddenActionFactKind.MissingEvidence)]
    [DataRow(ForbiddenActionFactKind.ValidationStale)]
    [DataRow(ForbiddenActionFactKind.ValidationExpired)]
    [DataRow(ForbiddenActionFactKind.WorktreeUnsafe)]
    [DataRow(ForbiddenActionFactKind.BaseMoved)]
    [DataRow(ForbiddenActionFactKind.CapabilityUnavailable)]
    [DataRow(ForbiddenActionFactKind.ExplicitGovernanceBlock)]
    public void BlockingFacts_ReturnForbidden(ForbiddenActionFactKind factKind)
    {
        var result = Resolve([Fact(factKind)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.Forbidden, result.ResolutionStatus);
        Assert.AreEqual(1, result.Findings.Count);
        Assert.AreEqual(factKind, result.Findings[0].FactKind);
        Assert.AreEqual($"SuppliedBlockingFact:{factKind}", result.Findings[0].Reason);
    }

    [TestMethod]
    public void NonBlockingInfoFact_DoesNotReturnForbidden()
    {
        var result = Resolve([
            Fact(
                ForbiddenActionFactKind.RoleVisibilityOnly,
                isBlocking: false,
                severity: ForbiddenActionFactSeverity.Info)
        ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, result.ResolutionStatus);
        Assert.AreEqual(0, result.Findings.Count);
    }

    [TestMethod]
    public void MultipleBlockingFacts_ReturnSortedFindings()
    {
        var result = Resolve([
            Fact(ForbiddenActionFactKind.WorktreeUnsafe, factId: "fact-c", severity: ForbiddenActionFactSeverity.Blocking),
            Fact(ForbiddenActionFactKind.ValidationExpired, factId: "fact-a", severity: ForbiddenActionFactSeverity.Critical),
            Fact(ForbiddenActionFactKind.BaseMoved, factId: "fact-b", severity: ForbiddenActionFactSeverity.Critical)
        ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.Forbidden, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[] { "fact-a", "fact-b", "fact-c" },
            result.Findings.Select(static finding => finding.FactId).ToArray());
    }

    [TestMethod]
    public void DuplicateFactIds_ReturnAmbiguousFacts()
    {
        var result = Resolve([
            Fact(ForbiddenActionFactKind.ValidationStale, factId: "fact-duplicate"),
            Fact(ForbiddenActionFactKind.ValidationStale, factId: "fact-duplicate")
        ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.AmbiguousFacts, result.ResolutionStatus);
        AssertContains(result.AmbiguousFacts, "DuplicateForbiddenActionFactId:fact-duplicate");
        Assert.AreEqual(0, result.Findings.Count);
    }

    [TestMethod]
    public void ConflictingSameFactIdMetadata_ReturnsAmbiguousFacts()
    {
        var result = Resolve([
            Fact(ForbiddenActionFactKind.ValidationStale, factId: "fact-conflict"),
            Fact(ForbiddenActionFactKind.WorktreeUnsafe, factId: "fact-conflict")
        ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.AmbiguousFacts, result.ResolutionStatus);
        AssertContains(result.AmbiguousFacts, "ConflictingForbiddenActionFactMetadata:fact-conflict");
    }

    [TestMethod]
    public void AmbiguousEvidenceFact_ReturnsAmbiguousFactsAndDoesNotChooseWinner()
    {
        var result = Resolve([Fact(ForbiddenActionFactKind.AmbiguousEvidence, factId: "fact-ambiguous")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.AmbiguousFacts, result.ResolutionStatus);
        AssertContains(result.AmbiguousFacts, "SuppliedAmbiguousEvidenceFact:fact-ambiguous");
        Assert.AreEqual(0, result.Findings.Count);
    }

    [DataTestMethod]
    [DataRow(null, ProjectId, OperationId, ForbiddenActionKind.SourceApply, "ForbiddenActionTenantIdRequired")]
    [DataRow("", ProjectId, OperationId, ForbiddenActionKind.SourceApply, "ForbiddenActionTenantIdRequired")]
    [DataRow("tenant d08", ProjectId, OperationId, ForbiddenActionKind.SourceApply, "ForbiddenActionTenantIdInvalid")]
    [DataRow(TenantId, null, OperationId, ForbiddenActionKind.SourceApply, "ForbiddenActionProjectIdRequired")]
    [DataRow(TenantId, "", OperationId, ForbiddenActionKind.SourceApply, "ForbiddenActionProjectIdRequired")]
    [DataRow(TenantId, "project d08", OperationId, ForbiddenActionKind.SourceApply, "ForbiddenActionProjectIdInvalid")]
    [DataRow(TenantId, ProjectId, null, ForbiddenActionKind.SourceApply, "OperationIdRequired")]
    [DataRow(TenantId, ProjectId, "run-123", ForbiddenActionKind.SourceApply, "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow(TenantId, ProjectId, OperationId, ForbiddenActionKind.Unknown, "ForbiddenActionKindRequired")]
    public void RequestValidation_FailsClosed(
        string? tenantId,
        string? projectId,
        string? operationId,
        ForbiddenActionKind actionKind,
        string expectedIssue)
    {
        var result = ForbiddenActionResolver.Resolve(new ForbiddenActionResolverRequest
        {
            TenantId = tenantId!,
            ProjectId = projectId!,
            OperationId = operationId!,
            ActionKind = actionKind,
            Facts = []
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(ForbiddenActionResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void NullFactsList_FailsClosed()
    {
        var result = ForbiddenActionResolver.Resolve(new ForbiddenActionResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            ActionKind = ForbiddenActionKind.SourceApply,
            Facts = null!
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ForbiddenActionFactsRequired");
    }

    [DataTestMethod]
    [DataRow(null, "ForbiddenActionFactIdRequired")]
    [DataRow("", "ForbiddenActionFactIdRequired")]
    [DataRow("fact id", "ForbiddenActionFactIdInvalid")]
    [DataRow("approval granted", "ForbiddenActionFactIdInvalid")]
    public void FactValidation_FailsClosedForMissingOrUnsafeFactId(string? factId, string expectedIssue)
    {
        var result = Resolve([Fact(ForbiddenActionFactKind.MissingEvidence, factId: factId!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ForbiddenActionFactKind.Unknown, ForbiddenActionFactSeverity.Blocking, "ForbiddenActionFactKindRequired")]
    [DataRow(ForbiddenActionFactKind.MissingEvidence, ForbiddenActionFactSeverity.Unknown, "ForbiddenActionFactSeverityRequired")]
    public void FactValidation_FailsClosedForUnknownKindOrSeverity(
        ForbiddenActionFactKind factKind,
        ForbiddenActionFactSeverity severity,
        string expectedIssue)
    {
        var result = Resolve([Fact(factKind, severity: severity)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "ForbiddenActionFactSourceRequired")]
    [DataRow("", "ForbiddenActionFactSourceRequired")]
    [DataRow("source with space", "ForbiddenActionFactSourceInvalid")]
    [DataRow("policy satisfied", "ForbiddenActionFactSourceInvalid")]
    public void FactValidation_FailsClosedForMissingOrUnsafeSource(string? source, string expectedIssue)
    {
        var result = Resolve([Fact(ForbiddenActionFactKind.MissingEvidence, source: source!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void FactValidation_RequiresObservedTimestamp()
    {
        var result = Resolve([Fact(ForbiddenActionFactKind.MissingEvidence, observedAtUtc: default(DateTimeOffset))]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ForbiddenActionFactObservedAtRequired");
    }

    [DataTestMethod]
    [DataRow(OperationCorrelationSurfaceKind.Unknown, "surface-1", "ForbiddenActionFactSurfaceKindRequired")]
    [DataRow(OperationCorrelationSurfaceKind.EvidenceMetadata, null, "ForbiddenActionFactSurfaceIdRequired")]
    [DataRow(OperationCorrelationSurfaceKind.EvidenceMetadata, "surface 1", "ForbiddenActionFactSurfaceIdInvalid")]
    public void FactValidation_FailsClosedForUnsafeSurfaceMetadata(
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string expectedIssue)
    {
        var result = Resolve([Fact(
            ForbiddenActionFactKind.MissingEvidence,
            surfaceKind: surfaceKind,
            surfaceId: surfaceId!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.EvidenceId, null, "ForbiddenActionFactReferenceIdRequired")]
    [DataRow(OperationReferenceKind.Unknown, "evidence-123", "ForbiddenActionFactReferenceKindRequired")]
    [DataRow(OperationReferenceKind.EvidenceId, "evidence 123", "ForbiddenActionFactReferenceIdInvalid")]
    [DataRow(OperationReferenceKind.EvidenceId, "https://example.test/evidence", "ForbiddenActionFactReferenceIdInvalid")]
    public void FactValidation_FailsClosedForUnsafeReferenceMetadata(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string expectedIssue)
    {
        var result = Resolve([Fact(
            ForbiddenActionFactKind.MissingEvidence,
            referenceKind: referenceKind,
            referenceId: referenceId!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(true, null, "ForbiddenActionFactRedactionReasonRequired")]
    [DataRow(true, "", "ForbiddenActionFactRedactionReasonRequired")]
    [DataRow(true, "api key leaked", "ForbiddenActionFactRedactionReasonInvalid")]
    [DataRow(true, "raw evidence payload", "ForbiddenActionFactRedactionReasonInvalid")]
    public void RedactedFact_RequiresSafeRedactionReason(
        bool isRedacted,
        string? redactionReason,
        string expectedIssue)
    {
        var result = Resolve([Fact(
            ForbiddenActionFactKind.MissingEvidence,
            isRedacted: isRedacted,
            redactionReason: redactionReason)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void HostileDisplayLabel_DoesNotInferForbiddenFact()
    {
        var result = Resolve([
            Fact(
                ForbiddenActionFactKind.RoleVisibilityOnly,
                isBlocking: false,
                severity: ForbiddenActionFactSeverity.Info,
                displayLabel: "this mentions ready for review and release candidate")
        ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, result.ResolutionStatus);
        Assert.AreEqual(0, result.Findings.Count);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "ForbiddenActionFactTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "ForbiddenActionFactProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000009", "ForbiddenActionFactOperationMismatch")]
    public void CrossScopeFact_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve([Fact(
            ForbiddenActionFactKind.MissingEvidence,
            tenantId: tenantId,
            projectId: projectId,
            operationId: operationId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceResolutionStatus.Complete)]
    [DataRow(MissingEvidenceResolutionStatus.NoRequirements)]
    public void MissingEvidenceStatus_DoesNotReturnPermission(MissingEvidenceResolutionStatus missingEvidenceStatus)
    {
        var result = Resolve([], missingEvidenceStatus: missingEvidenceStatus);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, result.ResolutionStatus);
        AssertContains(result.Warnings, "missing evidence status is not permission");
    }

    [DataTestMethod]
    [DataRow(OperationProjectedStatusKind.CompletedObserved, "release readiness")]
    [DataRow(OperationProjectedStatusKind.InterruptedObserved, "retry")]
    [DataRow(OperationProjectedStatusKind.RollbackObserved, "rollback")]
    [DataRow(OperationProjectedStatusKind.PullRequestObserved, "merge")]
    public void ProjectedStatus_DoesNotImplyAuthority(
        OperationProjectedStatusKind projectedStatusKind,
        string forbiddenMarker)
    {
        var result = Resolve([], projectedStatusKind: projectedStatusKind);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, result.ResolutionStatus);
        AssertContains(result.Warnings, "projected status is not permission");
        Assert.IsFalse(result.Findings.Any(static finding => finding.Reason.Contains("ready", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(result.ForbiddenAuthorityImplications.Any(item => item.Equals(forbiddenMarker, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Resolver_DoesNotInferForbiddenFactsFromReferenceIdOrCorrelationStyleText()
    {
        var result = Resolve([
            Fact(
                ForbiddenActionFactKind.RoleVisibilityOnly,
                isBlocking: false,
                severity: ForbiddenActionFactSeverity.Info,
                referenceId: "merge-ready-ref")
        ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, result.ResolutionStatus);
        Assert.AreEqual(0, result.Findings.Count);
    }

    [TestMethod]
    public void ResultModelsExposeNoAuthorityOrPermissionProperties()
    {
        var resultProperties = typeof(ForbiddenActionResolutionResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .ToArray();

        foreach (var forbiddenName in new[]
        {
            "Allowed",
            "ActionAllowed",
            "AuthorityGranted",
            "ApprovalStatus",
            "PolicySatisfied",
            "ValidationFresh",
            "NextSafeAction"
        })
        {
            Assert.IsFalse(resultProperties.Contains(forbiddenName, StringComparer.Ordinal));
        }

        Assert.IsFalse(resultProperties.Any(static property => property.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(Enum.GetNames<ForbiddenActionResolutionStatus>().Contains("Allowed", StringComparer.Ordinal));
    }

    [TestMethod]
    public void D01IdentityValidationStillPasses()
    {
        var validation = OperationIdentityValidator.ValidateOperationId(OperationId);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
    }

    [TestMethod]
    public void D03CorrelationValidationStillPasses()
    {
        var validation = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = "corr_1123456789abcdef",
            SurfaceKind = OperationCorrelationSurfaceKind.EvidenceMetadata,
            SurfaceId = "surface-d08",
            ObservedAtUtc = ObservedAtUtc,
            Source = "d08-test"
        });

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
    }

    [TestMethod]
    public void D02D04D05D06D07CompatibilityTypesRemainAvailable()
    {
        Assert.AreEqual(OperationIdentityLookupStatus.NotFound, OperationIdentityLookupStatus.NotFound);
        Assert.AreEqual(GovernedOperationTimelineEventKind.CompletedObserved, GovernedOperationTimelineEventKind.CompletedObserved);
        Assert.AreEqual(OperationProjectedStatusKind.CompletedObserved, OperationProjectedStatusKind.CompletedObserved);

        var d07 = MissingEvidenceResolver.Resolve(new MissingEvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            Requirements = [],
            ObservedEvidence = []
        });

        Assert.AreEqual(MissingEvidenceResolutionStatus.NoRequirements, d07.ResolutionStatus);
    }

    [TestMethod]
    public void A02AndA05ReadAdaptersRemainReadOnly()
    {
        AssertDoesNotContain(A02StatusReadSource(), "CanExecute = true");
        AssertDoesNotContain(A02StatusReadSource(), "SaveChanges");
        AssertDoesNotContain(A05TimelineSource(), "CanExecute = true");
        AssertDoesNotContain(A05TimelineSource(), "SaveChanges");
    }

    [TestMethod]
    public void StaticScan_D08CoreFilesAddNoApiSqlUiStoreExecutorOrMutationSurface()
    {
        var source = D08CoreSource();

        foreach (var marker in new[]
        {
            "Controller",
            "MapGet",
            "Route(",
            "OpenApi",
            "SqlConnection",
            "DbContext",
            "MigrationBuilder",
            "EventStore",
            "ProjectionStore",
            "EvidenceStore",
            "ReceiptStore",
            "Repository",
            "SaveChanges",
            ".Save",
            ".Update",
            ".Delete",
            ".Remove",
            "File.ReadAllText",
            "RawPayloadReader",
            "ReadRawPayload",
            "OperationIdentityLookupResolver.Resolve",
            "GovernedOperationTimelineAssembler.Assemble",
            "OperationStatusProjector.Project(",
            "MissingEvidenceResolver.Resolve",
            "ReceiptResolver",
            "EvidenceResolver",
            "FreshnessResolver",
            "BlockedStateFormatter",
            "NextSafeActionFormatter",
            "AuthorityWarningFormatter",
            "Process.Start",
            "RunProcessAsync",
            "File.Write",
            "HttpClient",
            "SourceApplyExecutor",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "DraftPullRequestGateway",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "PromoteMemory",
            "ContinueWorkflow",
            "AcceptApproval"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsForbiddenActionIsDiagnosticBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D08_FORBIDDEN_ACTION_RESOLVER.md"));

        Assert.IsTrue(receipt.Contains(
            "The forbidden action resolver explains supplied diagnostic facts that block a requested action. It does not grant permission, accept approval, satisfy policy, validate freshness, resolve evidence, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static ForbiddenActionResolutionResult Resolve(
        IReadOnlyList<ForbiddenActionInputFact> facts,
        ForbiddenActionKind actionKind = ForbiddenActionKind.SourceApply,
        OperationProjectedStatusKind projectedStatusKind = OperationProjectedStatusKind.Unknown,
        MissingEvidenceResolutionStatus missingEvidenceStatus = MissingEvidenceResolutionStatus.Unknown) =>
        ForbiddenActionResolver.Resolve(new ForbiddenActionResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            ActionKind = actionKind,
            ProjectedStatusKind = projectedStatusKind,
            MissingEvidenceStatus = missingEvidenceStatus,
            Facts = facts
        });

    private static ForbiddenActionInputFact Fact(
        ForbiddenActionFactKind factKind,
        string factId = "fact-1",
        ForbiddenActionFactSeverity severity = ForbiddenActionFactSeverity.Blocking,
        bool isBlocking = true,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string source = "d08-source",
        DateTimeOffset? observedAtUtc = null,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.EvidenceMetadata,
        string surfaceId = "surface-1",
        OperationReferenceKind referenceKind = OperationReferenceKind.EvidenceId,
        string referenceId = "evidence-1",
        string? displayLabel = null,
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            FactId = factId,
            FactKind = factKind,
            Severity = severity,
            IsBlocking = isBlocking,
            Source = source,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            DisplayLabel = displayLabel,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static string D08CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ForbiddenActionResolverModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ForbiddenActionResolverValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ForbiddenActionResolver.cs")));
    }

    private static string A02StatusReadSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationStatusFrontendReadinessBackendTruthSource.cs")));
    }

    private static string A05TimelineSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineFrontendReadinessBackendTruthSource.cs")));
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(
            values.Contains(expected, StringComparer.Ordinal),
            $"Expected '{expected}' in [{string.Join(", ", values)}].");

    private static void AssertDoesNotContain(string value, string unexpected) =>
        Assert.IsFalse(
            value.Contains(unexpected, StringComparison.Ordinal),
            $"Unexpected marker '{unexpected}' was present.");

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
