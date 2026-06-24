using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD03CorrelationIdModelTests
{
    private const string TenantId = "tenant-d03";
    private const string ProjectId = "project-d03";
    private const string OperationId = "op_0000000000000001";
    private const string CorrelationId = "corr_0123456789abcdef";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T02:00:00Z");
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T01:55:00Z");

    [TestMethod]
    public void ValidCorrelationLink_Passes()
    {
        var result = OperationCorrelationValidator.ValidateLink(Link(
            OperationCorrelationSurfaceKind.OperationStatus,
            "status-record-123"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(0, result.Issues.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "correlation id is not operation id");
        AssertContains(result.ForbiddenAuthorityImplications, "correlation group is not authority");
    }

    [TestMethod]
    public void ValidCorrelationGroupAcrossStatusEvidenceReceiptTimelineAndGovernance_Passes()
    {
        var result = OperationCorrelationValidator.ValidateGroup(Group(
            Link(OperationCorrelationSurfaceKind.OperationStatus, "status-record-123", ObservedAtUtc.AddMinutes(1)),
            Link(OperationCorrelationSurfaceKind.EvidenceMetadata, "evidence-metadata-123", ObservedAtUtc.AddMinutes(2)),
            Link(OperationCorrelationSurfaceKind.ReceiptMetadata, "receipt-metadata-123", ObservedAtUtc.AddMinutes(3)),
            Link(OperationCorrelationSurfaceKind.TimelineEvent, "timeline-event-123", ObservedAtUtc.AddMinutes(4)),
            Link(OperationCorrelationSurfaceKind.GovernanceEvent, "governance-event-123", ObservedAtUtc.AddMinutes(5))));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [DataTestMethod]
    [DataRow(null, ProjectId, OperationId, CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationTenantIdRequired")]
    [DataRow("", ProjectId, OperationId, CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationTenantIdRequired")]
    [DataRow("tenant d03", ProjectId, OperationId, CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationTenantIdInvalid")]
    [DataRow(TenantId, null, OperationId, CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationProjectIdRequired")]
    [DataRow(TenantId, "", OperationId, CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationProjectIdRequired")]
    [DataRow(TenantId, "project d03", OperationId, CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationProjectIdInvalid")]
    [DataRow(TenantId, ProjectId, null, CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationIdRequired")]
    [DataRow(TenantId, ProjectId, "run-123", CorrelationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow(TenantId, ProjectId, OperationId, null, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, OperationId, OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotReplaceOperationId")]
    [DataRow(TenantId, ProjectId, OperationId, "op_0000000000000002", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikeOperationId")]
    [DataRow(TenantId, ProjectId, OperationId, "run-123", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikeRunId")]
    [DataRow(TenantId, ProjectId, OperationId, "patch-artifact-123", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikePatchArtifactId")]
    [DataRow(TenantId, ProjectId, OperationId, "source-apply-123", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikeSourceApplyId")]
    [DataRow(TenantId, ProjectId, OperationId, "commit-package-123", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikeCommitPackageId")]
    [DataRow(TenantId, ProjectId, OperationId, "0123456789abcdef0123456789abcdef01234567", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikeCommitSha")]
    [DataRow(TenantId, ProjectId, OperationId, "push-123", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikePushId")]
    [DataRow(TenantId, ProjectId, OperationId, "pr-566", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikePullRequestId")]
    [DataRow(TenantId, ProjectId, OperationId, "receipt-123", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikeReceiptId")]
    [DataRow(TenantId, ProjectId, OperationId, "evidence-123", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdCannotLookLikeEvidenceId")]
    [DataRow(TenantId, ProjectId, OperationId, "https://github.com/org/repo/pull/566", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdUrlNotAllowed")]
    [DataRow(TenantId, ProjectId, OperationId, "approved-for-merge", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdAuthorityTextBlocked")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdeg", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "d03-source", "OperationCorrelationIdMustBeCanonical")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdef", OperationCorrelationSurfaceKind.Unknown, "status-123", "d03-source", "OperationCorrelationSurfaceKindRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdef", OperationCorrelationSurfaceKind.OperationStatus, null, "d03-source", "OperationCorrelationSurfaceIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdef", OperationCorrelationSurfaceKind.OperationStatus, "", "d03-source", "OperationCorrelationSurfaceIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdef", OperationCorrelationSurfaceKind.OperationStatus, "status 123", "d03-source", "OperationCorrelationSurfaceIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdef", OperationCorrelationSurfaceKind.OperationStatus, "status-123", null, "OperationCorrelationSourceRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdef", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "", "OperationCorrelationSourceRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_0123456789abcdef", OperationCorrelationSurfaceKind.OperationStatus, "status-123", "source with space", "OperationCorrelationSourceInvalid")]
    public void CorrelationLinkValidation_FailsClosedForInvalidShape(
        string? tenantId,
        string? projectId,
        string? operationId,
        string? correlationId,
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string? source,
        string expectedIssue)
    {
        var result = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = tenantId!,
            ProjectId = projectId!,
            OperationId = operationId!,
            CorrelationId = correlationId!,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId!,
            ObservedAtUtc = ObservedAtUtc,
            Source = source!
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertContains(result.ForbiddenAuthorityImplications, "correlation group is not workflow continuation");
    }

    [TestMethod]
    public void CorrelationLinkValidation_RequiresObservedTimestamp()
    {
        var result = OperationCorrelationValidator.ValidateLink(Link(
            OperationCorrelationSurfaceKind.OperationStatus,
            "status-record-123",
            observedAtUtc: default(DateTimeOffset)));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationCorrelationObservedAtRequired");
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, CorrelationId, "OperationCorrelationGroupTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, CorrelationId, "OperationCorrelationGroupProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000002", CorrelationId, "OperationCorrelationGroupOperationMismatch")]
    [DataRow(TenantId, ProjectId, OperationId, "corr_1111111111111111", "OperationCorrelationGroupCorrelationMismatch")]
    public void CorrelationGroupValidation_FailsClosedForMismatchedScope(
        string linkTenantId,
        string linkProjectId,
        string linkOperationId,
        string linkCorrelationId,
        string expectedIssue)
    {
        var result = OperationCorrelationValidator.ValidateGroup(Group(Link(
            OperationCorrelationSurfaceKind.EvidenceMetadata,
            "evidence-metadata-123",
            tenantId: linkTenantId,
            projectId: linkProjectId,
            operationId: linkOperationId,
            correlationId: linkCorrelationId)));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void CorrelationGroupValidation_RejectsDuplicateSurfaceIds()
    {
        var result = OperationCorrelationValidator.ValidateGroup(Group(
            Link(OperationCorrelationSurfaceKind.ReceiptMetadata, "receipt-metadata-123", ObservedAtUtc.AddMinutes(1)),
            Link(OperationCorrelationSurfaceKind.ReceiptMetadata, "receipt-metadata-123", ObservedAtUtc.AddMinutes(2))));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationCorrelationDuplicateSurfaceId");
    }

    [TestMethod]
    public void CorrelationGroupValidation_AllowsDuplicateSurfaceKindWhenSurfaceIdsAreDistinctAndOrderable()
    {
        var result = OperationCorrelationValidator.ValidateGroup(Group(
            Link(OperationCorrelationSurfaceKind.ReceiptMetadata, "receipt-metadata-123", ObservedAtUtc.AddMinutes(1), "receipt-source-a"),
            Link(OperationCorrelationSurfaceKind.ReceiptMetadata, "receipt-metadata-456", ObservedAtUtc.AddMinutes(2), "receipt-source-b")));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [DataTestMethod]
    [DataRow("correlation group is not authority")]
    [DataRow("correlation group is not approval")]
    [DataRow("correlation group is not policy satisfaction")]
    [DataRow("correlation group is not validation freshness")]
    [DataRow("correlation group is not retry permission")]
    [DataRow("correlation group is not workflow continuation")]
    [DataRow("correlation group is not rollback")]
    [DataRow("correlation group is not merge readiness")]
    [DataRow("correlation group is not release readiness")]
    [DataRow("correlation group is not deployment readiness")]
    public void CorrelationGroup_DoesNotSelectAuthority(string expectedForbiddenImplication)
    {
        var result = OperationCorrelationValidator.ValidateGroup(Group(
            Link(OperationCorrelationSurfaceKind.OperationStatus, "status-record-123"),
            Link(OperationCorrelationSurfaceKind.PullRequestReceipt, "pull-request-receipt-123")));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, expectedForbiddenImplication);
    }

    [TestMethod]
    public void CorrelationModels_ExposeNoAuthorityProperties()
    {
        foreach (var property in typeof(OperationCorrelationLink).GetProperties()
            .Concat(typeof(OperationCorrelationGroup).GetProperties())
            .Concat(typeof(OperationCorrelationValidationResult).GetProperties()))
        {
            AssertDoesNotContain(property.Name, "Can");
            AssertDoesNotContain(property.Name, "Approval");
            AssertDoesNotContain(property.Name, "Policy");
            AssertDoesNotContain(property.Name, "Fresh");
            AssertDoesNotContain(property.Name, "NextAction");
            AssertDoesNotContain(property.Name, "Release");
            AssertDoesNotContain(property.Name, "Deploy");
            AssertDoesNotContain(property.Name, "Rollback");
            AssertDoesNotContain(property.Name, "Retry");
            AssertDoesNotContain(property.Name, "Continue");
        }
    }

    [TestMethod]
    public void D01OperationIdentityValidationStillPasses()
    {
        var result = OperationIdentityValidator.ValidateRecord(IdentityRecord());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void D02LookupValidationStillPasses()
    {
        var result = OperationIdentityLookupValidator.ValidateRequest(new OperationIdentityLookupRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            ReferenceKind = OperationReferenceKind.RunId,
            ReferenceId = "run-123"
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void D02LookupDoesNotDependOnCorrelationIds()
    {
        var result = OperationIdentityLookupResolver.Resolve(
            new OperationIdentityLookupRequest
            {
                TenantId = TenantId,
                ProjectId = ProjectId,
                ReferenceKind = OperationReferenceKind.RunId,
                ReferenceId = "run-123"
            },
            [IdentityRecord(correlationId: "corr_aaaaaaaaaaaaaaaa")]);

        Assert.AreEqual(OperationIdentityLookupStatus.FoundOne, result.LookupStatus);
        Assert.AreEqual(OperationId, result.Matches[0].OperationId);
        Assert.AreEqual("corr_aaaaaaaaaaaaaaaa", result.Matches[0].CorrelationId);
    }

    [TestMethod]
    public void StaticScan_D03CoreFilesAddNoApiSqlUiProjectionLookupOrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var source = string.Join(
            Environment.NewLine,
            new[]
            {
                Path.Combine(root, "IronDev.Core", "Governance", "OperationCorrelationModels.cs"),
                Path.Combine(root, "IronDev.Core", "Governance", "OperationCorrelationValidator.cs")
            }.Select(File.ReadAllText));

        foreach (var marker in new[]
        {
            "Controller",
            "MapGet",
            "Route(",
            "OpenApi",
            "SqlConnection",
            "DbContext",
            "MigrationBuilder",
            "IActionResult",
            "OperationIdentityLookupResolver.Resolve(",
            "OperationTimelineReadRepository",
            "GovernedOperationStatusReadRepository",
            "ProjectionStore",
            "Projector",
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
            "NextSafeAction",
            "AuthorityWarning"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsCorrelationBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D03_CORRELATION_ID_MODEL.md"));

        Assert.IsTrue(receipt.Contains(
            "Correlation IDs connect scoped operation records across status, evidence, receipts, validation, and events. They do not replace OperationId, mint identity, perform lookup, project status, create timelines, approve work, satisfy policy, validate freshness, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static OperationCorrelationGroup Group(params OperationCorrelationLink[] links) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Links = links
        };

    private static OperationCorrelationLink Link(
        OperationCorrelationSurfaceKind surfaceKind,
        string surfaceId,
        DateTimeOffset? observedAtUtc = null,
        string source = "d03-source",
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            Source = source
        };

    private static OperationIdentityRecord IdentityRecord(string correlationId = CorrelationId) =>
        new()
        {
            OperationId = OperationId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            CreatedAtUtc = CreatedAtUtc,
            CreatedBy = "backend-operation-identity-service",
            LifecycleState = OperationIdentityLifecycleState.LinkedToRun,
            References =
            [
                new OperationIdentityReference
                {
                    ReferenceKind = OperationReferenceKind.RunId,
                    ReferenceId = "run-123",
                    ObservedAtUtc = ObservedAtUtc,
                    Source = "d03-reference-source"
                }
            ],
            CorrelationId = correlationId
        };

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
