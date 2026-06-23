using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD01CanonicalOperationIdLifecycleTests
{
    private const string OperationId = "op_0123456789abcdef";
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T00:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T00:05:00Z");

    [TestMethod]
    public void Models_DefineCanonicalOperationIdentityContract()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(OperationIdentityLifecycleState), OperationIdentityLifecycleState.Minted));
        Assert.IsTrue(Enum.IsDefined(typeof(OperationReferenceKind), OperationReferenceKind.RunId));
        Assert.IsTrue(Enum.IsDefined(typeof(OperationReferenceKind), OperationReferenceKind.PullRequestId));

        AssertHasProperty<OperationIdentityRecord>("OperationId");
        AssertHasProperty<OperationIdentityRecord>("CreatedAtUtc");
        AssertHasProperty<OperationIdentityRecord>("LifecycleState");
        AssertHasProperty<OperationIdentityRecord>("References");
        AssertHasProperty<OperationIdentityValidationResult>("ForbiddenAuthorityImplications");
    }

    [TestMethod]
    public void Models_DoNotContainExecutionOrAuthorityFields()
    {
        var modelTypes = new[]
        {
            typeof(OperationIdentityRecord),
            typeof(OperationIdentityReference)
        };

        foreach (var property in modelTypes.SelectMany(static type => type.GetProperties()))
        {
            AssertDoesNotContain(property.Name, "Can");
            AssertDoesNotContain(property.Name, "Authority");
            AssertDoesNotContain(property.Name, "Approval");
            AssertDoesNotContain(property.Name, "Policy");
            AssertDoesNotContain(property.Name, "Execute");
            AssertDoesNotContain(property.Name, "SourceApplyExecution");
            AssertDoesNotContain(property.Name, "Release");
            AssertDoesNotContain(property.Name, "Deploy");
        }
    }

    [DataTestMethod]
    [DataRow(null, "OperationIdRequired")]
    [DataRow("", "OperationIdRequired")]
    [DataRow("   ", "OperationIdRequired")]
    [DataRow("approve this operation", "OperationIdCannotContainWhitespace")]
    [DataRow("op_0123 4567", "OperationIdCannotContainWhitespace")]
    [DataRow("op_012345\u0001", "OperationIdInvalidCharacters")]
    public void OperationIdValidation_FailsClosedForMissingProseWhitespaceOrControlCharacters(
        string? candidate,
        string expectedIssue)
    {
        var result = OperationIdentityValidator.ValidateOperationId(candidate);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            AssertContains(result.Issues, "OperationIdMustBeBackendMintedCanonicalId");
        }
    }

    [TestMethod]
    public void OperationIdValidation_AllowsBackendShapedOperationId()
    {
        var result = OperationIdentityValidator.ValidateOperationId(OperationId);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [DataTestMethod]
    [DataRow("run_123", "RunIdCannotReplaceOperationId")]
    [DataRow("patch-artifact:123", "PatchArtifactIdCannotReplaceOperationId")]
    [DataRow("source-apply:123", "SourceApplyIdCannotReplaceOperationId")]
    [DataRow("commit-package:123", "CommitPackageIdCannotReplaceOperationId")]
    [DataRow("0123456789abcdef0123456789abcdef01234567", "CommitShaCannotReplaceOperationId")]
    [DataRow("push:123", "PushIdCannotReplaceOperationId")]
    [DataRow("https://github.com/org/repo/pull/12", "PullRequestIdCannotReplaceOperationId")]
    [DataRow("12", "PullRequestIdCannotReplaceOperationId")]
    [DataRow("receipt:123", "ReceiptIdCannotReplaceOperationId")]
    [DataRow("evidence:123", "EvidenceIdCannotReplaceOperationId")]
    [DataRow("correlation:123", "CorrelationIdCannotReplaceOperationId")]
    public void OperationIdValidation_ReferenceIdsCannotSubstituteForOperationId(
        string candidate,
        string expectedIssue)
    {
        var result = OperationIdentityValidator.ValidateOperationId(candidate);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void OperationIdValidation_OperationIdIsImmutableOnceAssigned()
    {
        var result = OperationIdentityValidator.ValidateOperationIdPreserved(
            OperationId,
            "op_fedcba9876543210");

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationIdImmutableOnceAssigned");
    }

    [TestMethod]
    public void RecordValidation_FailsClosedForUnknownLifecycleState()
    {
        var result = OperationIdentityValidator.ValidateRecord(Record(lifecycleState: OperationIdentityLifecycleState.Unknown));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationIdentityLifecycleStateRequired");
    }

    [TestMethod]
    public void References_AttachWithoutChangingOperationId()
    {
        var record = Record(
            references:
            [
                Reference(OperationReferenceKind.RunId, "run-001", ObservedAtUtc.AddMinutes(1)),
                Reference(OperationReferenceKind.PatchArtifactId, "patch-artifact-001", ObservedAtUtc)
            ]);

        var result = OperationIdentityValidator.ValidateRecord(record);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(OperationId, record.OperationId);
        CollectionAssert.AreEqual(
            new[] { "patch-artifact-001", "run-001" },
            result.References.Select(static item => item.ReferenceId).ToArray());
    }

    [TestMethod]
    public void References_DuplicateIdenticalReferencesAreRejected()
    {
        var duplicate = Reference(OperationReferenceKind.RunId, "run-001", ObservedAtUtc);
        var result = OperationIdentityValidator.ValidateRecord(Record(references: [duplicate, duplicate]));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "DuplicateOperationReference");
    }

    [TestMethod]
    public void References_DuplicateKindsAreAllowedWhenIdsAreDistinctAndOrderable()
    {
        var result = OperationIdentityValidator.ValidateRecord(
            Record(
                references:
                [
                    Reference(OperationReferenceKind.ReceiptId, "receipt-002", ObservedAtUtc.AddMinutes(1)),
                    Reference(OperationReferenceKind.ReceiptId, "receipt-001", ObservedAtUtc)
                ]));

        Assert.IsTrue(result.IsValid);
        CollectionAssert.AreEqual(
            new[] { "receipt-001", "receipt-002" },
            result.References.Select(static item => item.ReferenceId).ToArray());
    }

    [TestMethod]
    public void References_DuplicateKindsRequireOrderableReferences()
    {
        var result = OperationIdentityValidator.ValidateRecord(
            Record(
                references:
                [
                    Reference(OperationReferenceKind.ReceiptId, "receipt-001", default),
                    Reference(OperationReferenceKind.ReceiptId, "receipt-002", ObservedAtUtc)
                ]));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "DuplicateReferenceKindRequiresOrderableReferences");
        AssertContains(result.Issues, "OperationReferenceObservedAtRequired");
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.Unknown, "run-001", "source", "OperationReferenceKindRequired")]
    [DataRow(OperationReferenceKind.RunId, "", "source", "OperationReferenceIdRequired")]
    [DataRow(OperationReferenceKind.RunId, "run 001", "source", "OperationReferenceIdInvalid")]
    [DataRow(OperationReferenceKind.RunId, "run-001", "", "OperationReferenceSourceRequired")]
    public void References_FailClosedForInvalidReferenceShape(
        OperationReferenceKind kind,
        string referenceId,
        string source,
        string expectedIssue)
    {
        var result = OperationIdentityValidator.ValidateRecord(
            Record(references: [Reference(kind, referenceId, ObservedAtUtc, source)]));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void References_CannotOverrideCanonicalOperationId()
    {
        var result = OperationIdentityValidator.ValidateRecord(
            Record(references: [Reference(OperationReferenceKind.RunId, OperationId, ObservedAtUtc)]));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationReferenceCannotReplaceOperationId");
    }

    [TestMethod]
    public void References_DoNotImplyAuthority()
    {
        var result = OperationIdentityValidator.ValidateRecord(
            Record(references: [Reference(OperationReferenceKind.CommitSha, "0123456789abcdef0123456789abcdef01234567", ObservedAtUtc)]));

        Assert.IsTrue(result.IsValid);
        AssertContains(result.ForbiddenAuthorityImplications, "linked operation reference is not authority");
        AssertContains(result.ForbiddenAuthorityImplications, "operation id is not commit");
        AssertContains(result.ForbiddenAuthorityImplications, "operation id is not push");
    }

    [TestMethod]
    public void LifecycleTransitions_AllowedPathPasses()
    {
        var allowed = new[]
        {
            (OperationIdentityLifecycleState.Unknown, OperationIdentityLifecycleState.Minted),
            (OperationIdentityLifecycleState.Minted, OperationIdentityLifecycleState.LinkedToRun),
            (OperationIdentityLifecycleState.LinkedToRun, OperationIdentityLifecycleState.LinkedToPatch),
            (OperationIdentityLifecycleState.LinkedToPatch, OperationIdentityLifecycleState.LinkedToApply),
            (OperationIdentityLifecycleState.LinkedToApply, OperationIdentityLifecycleState.LinkedToCommit),
            (OperationIdentityLifecycleState.LinkedToCommit, OperationIdentityLifecycleState.LinkedToPush),
            (OperationIdentityLifecycleState.LinkedToPush, OperationIdentityLifecycleState.LinkedToPullRequest)
        };

        foreach (var (from, to) in allowed)
        {
            Assert.IsTrue(
                OperationIdentityValidator.ValidateTransition(from, to).IsValid,
                $"{from} -> {to} should be allowed.");
        }
    }

    [TestMethod]
    public void LifecycleTransitions_ActiveStatesMayMoveToTerminalStates()
    {
        foreach (var terminal in new[]
        {
            OperationIdentityLifecycleState.Failed,
            OperationIdentityLifecycleState.Interrupted,
            OperationIdentityLifecycleState.RolledBack,
            OperationIdentityLifecycleState.Completed
        })
        {
            Assert.IsTrue(OperationIdentityValidator
                .ValidateTransition(OperationIdentityLifecycleState.LinkedToPatch, terminal)
                .IsValid);
        }
    }

    [DataTestMethod]
    [DataRow(OperationIdentityLifecycleState.LinkedToCommit, OperationIdentityLifecycleState.LinkedToPullRequest)]
    [DataRow(OperationIdentityLifecycleState.LinkedToPush, OperationIdentityLifecycleState.LinkedToCommit)]
    [DataRow(OperationIdentityLifecycleState.Completed, OperationIdentityLifecycleState.LinkedToRun)]
    [DataRow(OperationIdentityLifecycleState.Unknown, OperationIdentityLifecycleState.LinkedToRun)]
    public void LifecycleTransitions_ImpossibleTransitionsFail(
        OperationIdentityLifecycleState from,
        OperationIdentityLifecycleState to)
    {
        var result = OperationIdentityValidator.ValidateTransition(from, to);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationIdentityTransitionNotAllowed");
    }

    [TestMethod]
    public void LifecycleTransitions_UnknownTargetFailsClosed()
    {
        var result = OperationIdentityValidator.ValidateTransition(
            OperationIdentityLifecycleState.Minted,
            OperationIdentityLifecycleState.Unknown);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationIdentityTransitionToStateRequired");
    }

    [TestMethod]
    public void LifecycleStates_DoNotImplyDownstreamAuthority()
    {
        var linkedToCommit = OperationIdentityValidator.ValidateRecord(Record(lifecycleState: OperationIdentityLifecycleState.LinkedToCommit));
        var linkedToPush = OperationIdentityValidator.ValidateRecord(Record(lifecycleState: OperationIdentityLifecycleState.LinkedToPush));
        var linkedToPr = OperationIdentityValidator.ValidateRecord(Record(lifecycleState: OperationIdentityLifecycleState.LinkedToPullRequest));
        var completed = OperationIdentityValidator.ValidateRecord(Record(lifecycleState: OperationIdentityLifecycleState.Completed));
        var interrupted = OperationIdentityValidator.ValidateRecord(Record(lifecycleState: OperationIdentityLifecycleState.Interrupted));
        var rolledBack = OperationIdentityValidator.ValidateRecord(Record(lifecycleState: OperationIdentityLifecycleState.RolledBack));

        AssertContains(linkedToCommit.ForbiddenAuthorityImplications, "operation id is not push");
        AssertContains(linkedToPush.ForbiddenAuthorityImplications, "operation id is not pull request creation");
        AssertContains(linkedToPr.ForbiddenAuthorityImplications, "operation id is not merge readiness");
        AssertContains(completed.ForbiddenAuthorityImplications, "operation id is not release readiness");
        AssertContains(interrupted.ForbiddenAuthorityImplications, "operation id is not retry permission");
        AssertContains(rolledBack.ForbiddenAuthorityImplications, "operation id is not rollback execution");
    }

    [TestMethod]
    public void StaticScan_OperationStatusReadRepositoryDoesNotMintOperationIds()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Core", "Governance", "GovernedOperationStatusReadRepository.cs"));

        AssertDoesNotContain(source, "Guid.NewGuid");
        AssertDoesNotContain(source, "new OperationIdentityRecord");
        AssertDoesNotContain(source, "OperationIdentityValidator");
        AssertDoesNotContain(source, "op_");
    }

    [TestMethod]
    public void StaticScan_OperationTimelineReadRepositoryDoesNotMintOperationIds()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Core", "Governance", "OperationTimelineReadRepository.cs"));

        AssertDoesNotContain(source, "Guid.NewGuid");
        AssertDoesNotContain(source, "new OperationIdentityRecord");
        AssertDoesNotContain(source, "OperationIdentityValidator");
        AssertDoesNotContain(source, "op_");
    }

    [TestMethod]
    public void StaticScan_FrontendReadinessSourcesDoNotMintOperationIds()
    {
        var root = FindRepositoryRoot();
        var source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(root, "IronDev.Infrastructure", "Governance"), "*FrontendReadiness*Source.cs")
                .Select(File.ReadAllText));

        AssertDoesNotContain(source, "Guid.NewGuid");
        AssertDoesNotContain(source, "new OperationIdentityRecord");
        AssertDoesNotContain(source, "OperationIdentityValidator");
    }

    [TestMethod]
    public void StaticScan_D01AddsNoApiOrUiOperationIdentitySurface()
    {
        var root = FindRepositoryRoot();
        var apiSource = string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(root, "IronDev.Api", "Controllers"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        AssertDoesNotContain(apiSource, "OperationIdentityRecord");
        AssertDoesNotContain(apiSource, "OperationIdentityValidator");

        var tauriRoot = Path.Combine(root, "IronDev.TauriShell", "src");
        if (Directory.Exists(tauriRoot))
        {
            var uiSource = string.Join(
                Environment.NewLine,
                Directory.GetFiles(tauriRoot, "*.*", SearchOption.AllDirectories)
                    .Where(static file => file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));

            AssertDoesNotContain(uiSource, "OperationIdentity");
        }
    }

    [TestMethod]
    public void StaticScan_D01CoreFilesDoNotAddLookupProjectionOrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var source = string.Join(
            Environment.NewLine,
            new[]
            {
                Path.Combine(root, "IronDev.Core", "Governance", "OperationIdentityModels.cs"),
                Path.Combine(root, "IronDev.Core", "Governance", "OperationIdentityValidator.cs")
            }.Select(File.ReadAllText));

        foreach (var marker in new[]
        {
            "Controller",
            "DbContext",
            "SqlConnection",
            "HttpClient",
            "Process.Start",
            "RunProcessAsync",
            "File.WriteAllText",
            "git ",
            "gh ",
            "Merge",
            "Deploy",
            "Release",
            "PromoteMemory",
            "ContinueWorkflow"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsIdentityAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D01_CANONICAL_OPERATION_ID_LIFECYCLE_RULES.md"));

        Assert.IsTrue(receipt.Contains(
            "Operation identity is a durable reference spine. It does not grant authority, approval, policy satisfaction, validation freshness, source apply, rollback, commit, push, PR creation, merge readiness, release readiness, deployment readiness, memory promotion, or workflow continuation.",
            StringComparison.Ordinal));
        Assert.IsTrue(receipt.Contains(
            "Run IDs, patch IDs, apply IDs, commit IDs, push IDs, PR IDs, receipt IDs, evidence IDs, and correlation IDs may reference an operation. They must not replace the canonical OperationId.",
            StringComparison.Ordinal));
    }

    private static OperationIdentityRecord Record(
        OperationIdentityLifecycleState lifecycleState = OperationIdentityLifecycleState.Minted,
        IReadOnlyList<OperationIdentityReference>? references = null) =>
        new()
        {
            OperationId = OperationId,
            TenantId = "tenant-d01",
            ProjectId = "project-d01",
            CreatedAtUtc = CreatedAtUtc,
            CreatedBy = "backend-operation-identity-service",
            LifecycleState = lifecycleState,
            References = references ?? [],
            CorrelationId = "correlation-d01"
        };

    private static OperationIdentityReference Reference(
        OperationReferenceKind kind,
        string referenceId,
        DateTimeOffset observedAtUtc,
        string source = "backend-observation") =>
        new()
        {
            ReferenceKind = kind,
            ReferenceId = referenceId,
            ObservedAtUtc = observedAtUtc,
            Source = source
        };

    private static void AssertHasProperty<T>(string propertyName) =>
        Assert.IsNotNull(typeof(T).GetProperty(propertyName), $"{typeof(T).Name}.{propertyName} is required.");

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
