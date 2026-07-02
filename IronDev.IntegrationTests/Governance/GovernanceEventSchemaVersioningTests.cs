using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("GovernanceEvent")]
[TestCategory("StaticBoundary")]
[TestCategory("Contract")]
public sealed class GovernanceEventSchemaVersioningTests
{
    [TestMethod]
    public void CurrentSchemaVersion_IsExplicitAndWritable()
    {
        Assert.AreEqual(0, GovernanceEventSchemaVersions.LegacyUnversioned);
        Assert.AreEqual(1, GovernanceEventSchemaVersions.Current);
        Assert.IsTrue(GovernanceEventSchemaVersions.Current > GovernanceEventSchemaVersions.LegacyUnversioned);

        var decision = GovernanceEventSchemaVersioning.ForNewEvent(GovernanceEventSchemaVersions.Current);

        Assert.AreEqual(GovernanceEventSchemaVersionClassification.Current, decision.Classification);
        Assert.IsTrue(decision.CanWrite);
        Assert.IsTrue(decision.CanReadForDiagnostics);
        Assert.IsTrue(decision.CanInterpretPayload);
        Assert.IsFalse(decision.CanSatisfyAuthorityChecks);
        StringAssert.Contains(decision.Reason, "Current governance event schema version is explicit.");

        var issues = new GovernanceEventValidator().ValidateAppend(ValidRequest());
        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void MissingOrInvalidSchemaVersion_IsRejectedForNewEvents()
    {
        var validator = new GovernanceEventValidator();

        AssertContainsIssue(validator.ValidateAppend(ValidRequest() with { PayloadVersion = GovernanceEventSchemaVersions.LegacyUnversioned }), GovernanceEventValidator.PayloadVersionInvalid);
        AssertContainsIssue(validator.ValidateAppend(ValidRequest() with { PayloadVersion = -1 }), GovernanceEventValidator.PayloadVersionInvalid);
        AssertContainsIssue(validator.ValidateAppend(ValidRequest() with { PayloadVersion = GovernanceEventSchemaVersions.Current + 1 }), GovernanceEventValidator.PayloadVersionUnsupported);

        var missingDecision = GovernanceEventSchemaVersioning.Classify(null);
        Assert.AreEqual(GovernanceEventSchemaVersionClassification.Invalid, missingDecision.Classification);
        Assert.IsFalse(missingDecision.CanWrite);
        Assert.IsFalse(missingDecision.CanReadForDiagnostics);
        Assert.IsFalse(missingDecision.CanInterpretPayload);
        Assert.IsFalse(missingDecision.CanSatisfyAuthorityChecks);
        StringAssert.Contains(missingDecision.Reason, "Schema version is required.");
    }

    [TestMethod]
    public void LegacyUnversionedEvents_AreDiagnosticOnly()
    {
        var decision = GovernanceEventSchemaVersioning.Classify(GovernanceEventSchemaVersions.LegacyUnversioned);

        Assert.AreEqual(GovernanceEventSchemaVersionClassification.LegacyUnversioned, decision.Classification);
        Assert.IsFalse(decision.CanWrite);
        Assert.IsTrue(decision.CanReadForDiagnostics);
        Assert.IsFalse(decision.CanInterpretPayload);
        Assert.IsFalse(decision.CanSatisfyAuthorityChecks);
        StringAssert.Contains(decision.Reason, "diagnostics only");

        var writeDecision = GovernanceEventSchemaVersioning.ForNewEvent(GovernanceEventSchemaVersions.LegacyUnversioned);
        Assert.IsFalse(writeDecision.CanWrite);
        StringAssert.Contains(writeDecision.Reason, "New governance events must use the explicit current schema version.");
    }

    [TestMethod]
    public void UnknownFutureSchemaVersion_FailsClosedForPayloadInterpretation()
    {
        var decision = GovernanceEventSchemaVersioning.Classify(GovernanceEventSchemaVersions.Current + 5);

        Assert.AreEqual(GovernanceEventSchemaVersionClassification.UnknownFuture, decision.Classification);
        Assert.IsFalse(decision.CanWrite);
        Assert.IsTrue(decision.CanReadForDiagnostics);
        Assert.IsFalse(decision.CanInterpretPayload);
        Assert.IsFalse(decision.CanSatisfyAuthorityChecks);
        StringAssert.Contains(decision.Reason, "metadata only");

        var writeDecision = GovernanceEventSchemaVersioning.ForNewEvent(GovernanceEventSchemaVersions.Current + 5);
        Assert.IsFalse(writeDecision.CanWrite);
        StringAssert.Contains(writeDecision.Reason, "New governance events must use the explicit current schema version.");
    }

    [TestMethod]
    public void SchemaVersionClassification_DoesNotGrantAuthority()
    {
        var decisions = new[]
        {
            GovernanceEventSchemaVersioning.Classify(null),
            GovernanceEventSchemaVersioning.Classify(-1),
            GovernanceEventSchemaVersioning.Classify(GovernanceEventSchemaVersions.LegacyUnversioned),
            GovernanceEventSchemaVersioning.Classify(GovernanceEventSchemaVersions.Current),
            GovernanceEventSchemaVersioning.Classify(GovernanceEventSchemaVersions.Current + 1)
        };

        foreach (var decision in decisions)
        {
            Assert.IsFalse(decision.CanSatisfyAuthorityChecks, $"Schema version decision must not satisfy authority checks: {decision.Classification}");
            AssertAuthorityFlagsStayOutOfDecision(decision);
        }
    }

    [TestMethod]
    public void SchemaVersioning_DoesNotIntroduceSqlMigrationReplayOrBackfill()
    {
        var root = RepositoryRoot();
        var h03Files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceEventSchemaVersioning.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceEventModels.cs"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "GovernanceEventSchemaVersioningTests.cs"),
            Path.Combine(root, "Docs", "receipts", "H03_GOVERNANCE_EVENT_SCHEMA_VERSIONING.md"),
            Path.Combine(root, "Docs", "testing", "INTEGRATION_TEST_CATEGORIES.md")
        };

        foreach (var file in h03Files)
            Assert.IsTrue(File.Exists(file), $"Expected H03 file missing: {file}");

        var productionFiles = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceEventSchemaVersioning.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceEventModels.cs")
        };
        var forbiddenProductionTokens = new[]
        {
            "SqlConnection",
            "DbConnection",
            "Dapper",
            "MigrationRunner",
            "DbUp",
            "PerformUpgrade",
            "EventReplay",
            "Backfill",
            "ControllerBase",
            "IHostedService",
            "BackgroundService",
            "SourceApply",
            "ReleaseReadiness",
            "DeploymentReadiness"
        };

        foreach (var file in productionFiles)
            AssertDoesNotContainAny(File.ReadAllText(file), forbiddenProductionTokens, file);

        var changedFileNames = h03Files.Select(path => NormalizePath(Path.GetRelativePath(root, path))).ToArray();
        Assert.IsFalse(changedFileNames.Any(path => path.StartsWith("Database/", StringComparison.Ordinal)), "H03 must not change Database files.");
        CollectionAssert.DoesNotContain(changedFileNames, "Database/migrations.json");
        CollectionAssert.DoesNotContain(changedFileNames, "Database/apply-migrations.ps1");
        CollectionAssert.DoesNotContain(changedFileNames, "Database/verify-migrations.ps1");
    }

    [TestMethod]
    public void Receipt_RecordsBoundaryAndLimitations()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "H03_GOVERNANCE_EVENT_SCHEMA_VERSIONING.md"));

        AssertContainsAll(receipt,
            "H03 does not add a SQL migration.",
            "H03 does not alter the governance-event table.",
            "H03 does not replay events.",
            "H03 does not backfill old events.",
            "H03 does not mutate existing governance events.",
            "H03 does not add API/CLI/UI behavior.",
            "H03 does not change workflow/source-apply/rollback/release/deployment authority.",
            "Schema versioning is parser evidence only.",
            "A readable event is not an authoritative event.",
            "A governance-event schema version is not approval.",
            "A governance-event schema version is not policy satisfaction.",
            "A governance-event schema version is not source-apply authority.",
            "A governance-event schema version is not workflow continuation authority.",
            "A governance-event schema version is not release readiness.",
            "A governance-event schema version is not deployment readiness.",
            "H04 - Governance event append-only DB constraint tests.",
            "Append-only storage preserves evidence. It does not validate authority.",
            "An immutable lie is still a lie.");
    }

    private static GovernanceEventAppendRequest ValidRequest() =>
        new()
        {
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EventType = "governance.event.created",
            ActorType = "test",
            ActorId = "test-actor",
            CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CausationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SubjectType = "tool_request",
            SubjectId = "tool-request-1",
            PayloadVersion = GovernanceEventSchemaVersions.Current,
            PayloadJson = "{\"schema\":\"governance.event.created.v1\",\"message\":\"Schema version test event.\"}"
        };

    private static void AssertContainsIssue(IReadOnlyList<GovernanceEventValidationIssue> issues, string expectedCode)
    {
        Assert.IsTrue(issues.Any(issue => issue.Code == expectedCode), $"Expected issue code {expectedCode}. Actual: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertAuthorityFlagsStayOutOfDecision(GovernanceEventSchemaVersionDecision decision)
    {
        var serialized = $"{decision.Classification} {decision.CanWrite} {decision.CanReadForDiagnostics} {decision.CanInterpretPayload} {decision.CanSatisfyAuthorityChecks} {decision.Reason}";
        AssertDoesNotContainAny(
            serialized,
            [
                "approval granted",
                "policy satisfied",
                "source apply allowed",
                "workflow continuation allowed",
                "release ready",
                "deployment ready"
            ],
            "schema version decision");
    }

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static void AssertDoesNotContainAny(string text, IReadOnlyList<string> forbidden, string source)
    {
        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Unexpected token '{value}' in {source}.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string NormalizePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}
