using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD02OperationLookupByReferenceIdTests
{
    private const string TenantId = "tenant-d02";
    private const string ProjectId = "project-d02";
    private const string OperationId = "op_0000000000000001";
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T01:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T01:05:00Z");

    [DataTestMethod]
    [DataRow(OperationReferenceKind.RunId, "run-123")]
    [DataRow(OperationReferenceKind.PatchArtifactId, "patch-artifact-123")]
    [DataRow(OperationReferenceKind.SourceApplyId, "source-apply-123")]
    [DataRow(OperationReferenceKind.CommitPackageId, "commit-package-123")]
    [DataRow(OperationReferenceKind.CommitSha, "0123456789abcdef0123456789abcdef01234567")]
    [DataRow(OperationReferenceKind.PushId, "push-123")]
    [DataRow(OperationReferenceKind.PullRequestId, "566")]
    public void LookupByReference_FindsCanonicalOperationId(
        OperationReferenceKind referenceKind,
        string referenceId)
    {
        var record = Record(
            operationId: OperationId,
            references: [Reference(referenceKind, referenceId)]);

        var result = Resolve(Request(referenceKind, referenceId), record);

        Assert.AreEqual(OperationIdentityLookupStatus.FoundOne, result.LookupStatus);
        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual(OperationId, result.Matches[0].OperationId);
        Assert.AreEqual(referenceKind, result.Matches[0].MatchedReferenceKind);
        Assert.AreEqual(referenceId, result.Matches[0].MatchedReferenceId);
        Assert.AreEqual(ObservedAtUtc, result.Matches[0].MatchedReferenceObservedAtUtc);
        Assert.AreEqual("d02-reference-source", result.Matches[0].MatchedReferenceSource);
    }

    [TestMethod]
    public void Lookup_ReturnsStoredCanonicalOperationIdInsteadOfDerivingFromExternalId()
    {
        var result = Resolve(
            Request(OperationReferenceKind.RunId, "run-999"),
            Record(operationId: OperationId, references: [Reference(OperationReferenceKind.RunId, "run-999")]));

        Assert.AreEqual(OperationIdentityLookupStatus.FoundOne, result.LookupStatus);
        Assert.AreEqual(OperationId, result.Matches[0].OperationId);
        Assert.AreNotEqual("run-999", result.Matches[0].OperationId);
    }

    [TestMethod]
    public void Lookup_DoesNotMintOperationWhenNoMatchExists()
    {
        var result = Resolve(
            Request(OperationReferenceKind.RunId, "run-missing"),
            Record(operationId: OperationId, references: [Reference(OperationReferenceKind.RunId, "run-present")]));

        Assert.AreEqual(OperationIdentityLookupStatus.NotFound, result.LookupStatus);
        Assert.AreEqual(0, result.Matches.Count);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public void Lookup_ReturnsFoundMultipleWithoutSelectingWinner()
    {
        var later = Record(
            operationId: "op_0000000000000003",
            createdAtUtc: CreatedAtUtc.AddMinutes(2),
            references: [Reference(OperationReferenceKind.RunId, "run-shared", ObservedAtUtc.AddMinutes(3), "source-c")]);
        var first = Record(
            operationId: "op_0000000000000001",
            createdAtUtc: CreatedAtUtc,
            references: [Reference(OperationReferenceKind.RunId, "run-shared", ObservedAtUtc.AddMinutes(2), "source-b")]);
        var second = Record(
            operationId: "op_0000000000000002",
            createdAtUtc: CreatedAtUtc,
            references: [Reference(OperationReferenceKind.RunId, "run-shared", ObservedAtUtc, "source-a")]);

        var result = Resolve(Request(OperationReferenceKind.RunId, "run-shared"), later, first, second);

        Assert.AreEqual(OperationIdentityLookupStatus.FoundMultiple, result.LookupStatus);
        var actualOperationIds = result.Matches.Select(static match => match.OperationId).ToArray();
        var expectedOperationIds = new[] { "op_0000000000000001", "op_0000000000000002", "op_0000000000000003" };
        Assert.IsTrue(
            expectedOperationIds.SequenceEqual(actualOperationIds),
            $"Expected deterministic found-multiple ordering: {string.Join(", ", expectedOperationIds)}; actual: {string.Join(", ", actualOperationIds)}");
    }

    [DataTestMethod]
    [DataRow(null, "project-d02", OperationReferenceKind.RunId, "run-123", "OperationIdentityLookupTenantIdRequired")]
    [DataRow("", "project-d02", OperationReferenceKind.RunId, "run-123", "OperationIdentityLookupTenantIdRequired")]
    [DataRow("tenant d02", "project-d02", OperationReferenceKind.RunId, "run-123", "OperationIdentityLookupTenantIdInvalid")]
    [DataRow("tenant-d02", null, OperationReferenceKind.RunId, "run-123", "OperationIdentityLookupProjectIdRequired")]
    [DataRow("tenant-d02", "", OperationReferenceKind.RunId, "run-123", "OperationIdentityLookupProjectIdRequired")]
    [DataRow("tenant-d02", "project d02", OperationReferenceKind.RunId, "run-123", "OperationIdentityLookupProjectIdInvalid")]
    [DataRow("tenant-d02", "project-d02", OperationReferenceKind.Unknown, "run-123", "OperationIdentityLookupReferenceKindRequired")]
    [DataRow("tenant-d02", "project-d02", OperationReferenceKind.RunId, null, "OperationIdentityLookupReferenceIdRequired")]
    [DataRow("tenant-d02", "project-d02", OperationReferenceKind.RunId, "", "OperationIdentityLookupReferenceIdRequired")]
    [DataRow("tenant-d02", "project-d02", OperationReferenceKind.RunId, "run 123", "OperationIdentityLookupReferenceIdInvalid")]
    [DataRow("tenant-d02", "project-d02", OperationReferenceKind.RunId, "run-\u0001", "OperationIdentityLookupReferenceIdInvalid")]
    [DataRow("tenant-d02", "project-d02", OperationReferenceKind.RunId, "approved-for-merge", "OperationIdentityLookupReferenceIdAuthorityTextBlocked")]
    public void LookupRequestValidation_FailsClosedForInvalidShape(
        string? tenantId,
        string? projectId,
        OperationReferenceKind referenceKind,
        string? referenceId,
        string expectedIssue)
    {
        var result = Resolve(new OperationIdentityLookupRequest
        {
            TenantId = tenantId!,
            ProjectId = projectId!,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId!
        });

        Assert.AreEqual(OperationIdentityLookupStatus.InvalidRequest, result.LookupStatus);
        AssertContains(result.Issues, expectedIssue);
        Assert.AreEqual(0, result.Matches.Count);
    }

    [TestMethod]
    public void LookupRequestValidation_BlocksUrlForNonUrlReferenceKinds()
    {
        var result = Resolve(Request(OperationReferenceKind.RunId, "https://github.com/org/repo/pull/566"));

        Assert.AreEqual(OperationIdentityLookupStatus.InvalidRequest, result.LookupStatus);
        AssertContains(result.Issues, "OperationIdentityLookupReferenceUrlNotAllowedForReferenceKind");
    }

    [TestMethod]
    public void Lookup_MatchesReferencesCaseInsensitivelyButPreservesStoredCasing()
    {
        var result = Resolve(
            Request(OperationReferenceKind.RunId, "RUN-ABC"),
            Record(references: [Reference(OperationReferenceKind.RunId, "run-AbC")]));

        Assert.AreEqual(OperationIdentityLookupStatus.FoundOne, result.LookupStatus);
        Assert.AreEqual("run-AbC", result.Matches[0].MatchedReferenceId);
    }

    [TestMethod]
    public void Lookup_SearchesOnlyWithinMatchingTenant()
    {
        var wrongTenant = Record(
            operationId: "op_0000000000000002",
            tenantId: "tenant-other",
            references: [Reference(OperationReferenceKind.RunId, "run-123")]);

        var result = Resolve(Request(OperationReferenceKind.RunId, "run-123"), wrongTenant);

        Assert.AreEqual(OperationIdentityLookupStatus.NotFound, result.LookupStatus);
        Assert.AreEqual(0, result.Matches.Count);
    }

    [TestMethod]
    public void Lookup_SearchesOnlyWithinMatchingProject()
    {
        var wrongProject = Record(
            operationId: "op_0000000000000002",
            projectId: "project-other",
            references: [Reference(OperationReferenceKind.RunId, "run-123")]);

        var result = Resolve(Request(OperationReferenceKind.RunId, "run-123"), wrongProject);

        Assert.AreEqual(OperationIdentityLookupStatus.NotFound, result.LookupStatus);
        Assert.AreEqual(0, result.Matches.Count);
    }

    [TestMethod]
    public void Lookup_DoesNotFallBackAcrossReferenceKinds()
    {
        var result = Resolve(
            Request(OperationReferenceKind.PushId, "shared-id"),
            Record(references: [Reference(OperationReferenceKind.RunId, "shared-id")]));

        Assert.AreEqual(OperationIdentityLookupStatus.NotFound, result.LookupStatus);
    }

    [TestMethod]
    public void Lookup_FailsClosedForMatchedInvalidOperationIdentityRecord()
    {
        var invalid = Record(
            operationId: "run-123",
            references: [Reference(OperationReferenceKind.RunId, "run-123")]);

        var result = Resolve(Request(OperationReferenceKind.RunId, "run-123"), invalid);

        Assert.AreEqual(OperationIdentityLookupStatus.InvalidRequest, result.LookupStatus);
        AssertContains(result.Issues, "MatchedOperationIdentityRecordInvalid");
        AssertContains(result.Issues, "MatchedOperationIdentityRecord:RunIdCannotReplaceOperationId");
        Assert.AreEqual(0, result.Matches.Count);
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.CommitSha, "0123456789abcdef0123456789abcdef01234567", OperationIdentityLifecycleState.LinkedToCommit, "lookup is not push")]
    [DataRow(OperationReferenceKind.PushId, "push-123", OperationIdentityLifecycleState.LinkedToPush, "lookup is not pull request creation")]
    [DataRow(OperationReferenceKind.PullRequestId, "566", OperationIdentityLifecycleState.LinkedToPullRequest, "lookup is not merge readiness")]
    [DataRow(OperationReferenceKind.RunId, "run-completed", OperationIdentityLifecycleState.Completed, "lookup is not release readiness")]
    [DataRow(OperationReferenceKind.RunId, "run-interrupted", OperationIdentityLifecycleState.Interrupted, "lookup is not retry permission")]
    [DataRow(OperationReferenceKind.RunId, "run-rolled-back", OperationIdentityLifecycleState.RolledBack, "lookup is not rollback")]
    public void Lookup_DoesNotImplyDownstreamAuthority(
        OperationReferenceKind referenceKind,
        string referenceId,
        OperationIdentityLifecycleState lifecycleState,
        string expectedForbiddenImplication)
    {
        var result = Resolve(
            Request(referenceKind, referenceId),
            Record(lifecycleState: lifecycleState, references: [Reference(referenceKind, referenceId)]));

        Assert.AreEqual(OperationIdentityLookupStatus.FoundOne, result.LookupStatus);
        AssertContains(result.ForbiddenAuthorityImplications, expectedForbiddenImplication);
        AssertContains(result.ForbiddenAuthorityImplications, "external reference id is not operation id");
    }

    [TestMethod]
    public void LookupResult_HasNoAuthorityProperties()
    {
        foreach (var property in typeof(OperationIdentityLookupResult).GetProperties()
            .Concat(typeof(OperationIdentityLookupMatch).GetProperties()))
        {
            AssertDoesNotContain(property.Name, "Can");
            AssertDoesNotContain(property.Name, "Approval");
            AssertDoesNotContain(property.Name, "Policy");
            AssertDoesNotContain(property.Name, "Release");
            AssertDoesNotContain(property.Name, "Deploy");
            AssertDoesNotContain(property.Name, "Rollback");
            AssertDoesNotContain(property.Name, "Continue");
        }
    }

    [TestMethod]
    public void D01OperationIdentityValidationStillPasses()
    {
        var result = OperationIdentityValidator.ValidateRecord(Record(references: [Reference(OperationReferenceKind.RunId, "run-123")]));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void D01ReadAdaptersStillDoNotMintOperationIds()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationStatusReadRepository.cs")) +
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationTimelineReadRepository.cs"));

        AssertDoesNotContain(source, "Guid.NewGuid");
        AssertDoesNotContain(source, "OperationIdentityLookupResolver.Resolve");
        AssertDoesNotContain(source, "new OperationIdentityRecord");
    }

    [TestMethod]
    public void StaticScan_D02CoreFilesAddNoApiSqlUiProjectionOrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var source = string.Join(
            Environment.NewLine,
            new[]
            {
                Path.Combine(root, "IronDev.Core", "Governance", "OperationIdentityLookupModels.cs"),
                Path.Combine(root, "IronDev.Core", "Governance", "OperationIdentityLookupValidator.cs"),
                Path.Combine(root, "IronDev.Core", "Governance", "OperationIdentityLookupResolver.cs")
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
            "Process.Start",
            "RunProcessAsync",
            "File.Write",
            "HttpClient",
            "SourceApplyExecutor",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "DraftPullRequestGateway",
            "AcceptedApproval",
            "PolicySatisfaction",
            "PromoteMemory",
            "ContinueWorkflow"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsLookupBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D02_OPERATION_LOOKUP_BY_REFERENCE_IDS.md"));

        Assert.IsTrue(receipt.Contains(
            "Operation lookup can find canonical operation identity by scoped external references. It does not mint operation IDs, derive operation IDs, select authority, approve work, satisfy policy, validate freshness, execute mutation, create PRs, merge, release, deploy, promote memory, retry, rollback, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static OperationIdentityLookupResult Resolve(
        OperationIdentityLookupRequest request,
        params OperationIdentityRecord[] records) =>
        OperationIdentityLookupResolver.Resolve(request, records);

    private static OperationIdentityLookupRequest Request(
        OperationReferenceKind referenceKind,
        string referenceId,
        string tenantId = TenantId,
        string projectId = ProjectId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId
        };

    private static OperationIdentityRecord Record(
        string operationId = OperationId,
        string tenantId = TenantId,
        string projectId = ProjectId,
        OperationIdentityLifecycleState lifecycleState = OperationIdentityLifecycleState.LinkedToRun,
        DateTimeOffset? createdAtUtc = null,
        IReadOnlyList<OperationIdentityReference>? references = null) =>
        new()
        {
            OperationId = operationId,
            TenantId = tenantId,
            ProjectId = projectId,
            CreatedAtUtc = createdAtUtc ?? CreatedAtUtc,
            CreatedBy = "backend-operation-identity-service",
            LifecycleState = lifecycleState,
            References = references ?? [],
            CorrelationId = "correlation-d02"
        };

    private static OperationIdentityReference Reference(
        OperationReferenceKind kind,
        string referenceId,
        DateTimeOffset? observedAtUtc = null,
        string source = "d02-reference-source") =>
        new()
        {
            ReferenceKind = kind,
            ReferenceId = referenceId,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            Source = source
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
