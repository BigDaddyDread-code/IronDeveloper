using System.Data;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using IronDev.Data;
using IronDev.Infrastructure.ToolExecutionAudit;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ToolExecutionAuditStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 11, 1, 0, 2, TimeSpan.Zero);

    private SqlToolExecutionAuditStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropToolExecutionAuditSchemaAsync();
        await ApplyToolExecutionAuditMigrationAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlToolExecutionAuditStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropToolExecutionAuditSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void ToolExecutionAuditContracts_ExposeAppendOnlyStoreShape()
    {
        Assert.IsNotNull(typeof(ToolExecutionAuditRecord));
        Assert.IsNotNull(typeof(ToolExecutionAuditPayload));
        Assert.IsNotNull(typeof(ToolExecutionAuditScope));
        Assert.IsNotNull(typeof(ToolExecutionAuditActor));
        Assert.IsNotNull(typeof(ToolExecutionAuditTool));
        Assert.IsNotNull(typeof(ToolExecutionAuditGate));
        Assert.IsNotNull(typeof(ToolExecutionAuditOutcome));
        Assert.IsNotNull(typeof(ToolExecutionAuditEvidence));
        Assert.IsNotNull(typeof(ToolExecutionAuditValidator));
        Assert.IsNotNull(typeof(ToolExecutionAuditRecordFactory));
        Assert.IsNotNull(typeof(IToolExecutionAuditStore));

        CollectionAssert.AreEquivalent(
            new[]
            {
                ToolExecutionAuditAppendStatus.Appended,
                ToolExecutionAuditAppendStatus.AlreadyExists,
                ToolExecutionAuditAppendStatus.Conflict,
                ToolExecutionAuditAppendStatus.Rejected
            },
            Enum.GetValues<ToolExecutionAuditAppendStatus>());

        var methods = typeof(IToolExecutionAuditStore).GetMethods().Select(method => method.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "AppendAsync", "GetAsync", "ListByRunAsync" },
            methods);
    }

    [TestMethod]
    public void ToolExecutionAuditFactory_BuildsTesterSuccessAndFailureRecords()
    {
        var succeeded = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        var failed = ToolExecutionAuditRecordFactory.FromManualTesterResult(FailedTesterResult(), CreatedAt);

        Assert.AreEqual(AgentToolKind.TestRun, succeeded.ToolKind);
        Assert.AreEqual(AgentToolRequestType.TestExecutionRequest, succeeded.RequestType);
        Assert.AreEqual(AgentKind.TestingAgent, succeeded.AgentKind);
        Assert.AreEqual(ToolExecutionAuditRecordFactory.ManualTesterPayloadKind, succeeded.PayloadKind);
        Assert.IsTrue(succeeded.Succeeded);
        Assert.IsTrue(failed.Status == ManualTesterAgentToolExecutionStatus.Failed.ToString());
        Assert.IsFalse(failed.Succeeded);
        AssertNoUnsafeFlags(succeeded);
        AssertNoUnsafeFlags(failed);
        AssertNoValidationIssues(succeeded);
        AssertNoValidationIssues(failed);
    }

    [TestMethod]
    public void ToolExecutionAuditFactory_BuildsImplementationPatchProposalRecord()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualImplementationPatchProposalResult(SuccessfulImplementationResult(), CreatedAt);

        Assert.AreEqual(AgentToolKind.PatchProposal, record.ToolKind);
        Assert.AreEqual(AgentToolRequestType.PatchProposalRequest, record.RequestType);
        Assert.AreEqual(AgentKind.ImplementationAgent, record.AgentKind);
        Assert.AreEqual(ToolExecutionAuditRecordFactory.ManualImplementationPatchProposalPayloadKind, record.PayloadKind);
        Assert.IsTrue(record.Succeeded);
        AssertNoUnsafeFlags(record);
        AssertNoValidationIssues(record);
    }

    [TestMethod]
    public void ToolExecutionAuditFactory_RejectsIncompleteOrUnsafeManualResults()
    {
        var testerRequest = BackendManualToolExecutionFixtures.TesterExecutionRequestWithGovernanceGateApproval();
        var blockedTester = new ManualTesterAgentToolExecutionService(BackendManualToolExecutionFixtures.ScriptedTestExecutorSucceedsWithEvidence())
            .Execute(testerRequest with
            {
                GateDecision = testerRequest.GateDecision with
                {
                    Decision = AgentToolExecutionGateDecisionType.Blocked,
                    GrantsExecution = false
                }
            });

        var failedImplementation = new ManualImplementationAgentPatchProposalService(new ScriptedPatchProposalGenerator(_ => new PatchProposalGenerationResult
        {
            Succeeded = false,
            Summary = "Generator failed safely.",
            Issues =
            [
                new ManualImplementationPatchProposalIssue
                {
                    Code = ManualImplementationPatchProposalValidator.OutputUnsafe,
                    Severity = "error",
                    Message = "No proposal was produced."
                }
            ]
        })).Propose(BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource());

        ExpectArgumentException(() => ToolExecutionAuditRecordFactory.FromManualTesterResult(blockedTester, CreatedAt));
        ExpectArgumentException(() => ToolExecutionAuditRecordFactory.FromManualImplementationPatchProposalResult(failedImplementation, CreatedAt));
    }

    [TestMethod]
    public void ToolExecutionAuditValidator_RejectsMissingFieldsUnsupportedShapesUnsafeFlagsAndText()
    {
        var valid = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        var cases = new Dictionary<string, ToolExecutionAuditRecord>
        {
            [ToolExecutionAuditValidator.ToolAuditIdRequired] = valid with { ToolExecutionAuditId = "" },
            [ToolExecutionAuditValidator.ToolAuditScopeRequired] = valid with { TenantId = "" },
            [ToolExecutionAuditValidator.ToolAuditAgentRequired] = valid with { AgentId = "" },
            [ToolExecutionAuditValidator.ToolAuditToolRequestRequired] = valid with { ToolRequestId = "" },
            [ToolExecutionAuditValidator.ToolAuditGateRequired] = valid with { GateDecisionId = "" },
            [ToolExecutionAuditValidator.ToolAuditPayloadRequired] = valid with { PayloadJson = "" },
            [ToolExecutionAuditValidator.ToolAuditPayloadKindInvalid] = valid with { PayloadKind = "RawToolOutput" },
            [ToolExecutionAuditValidator.ToolAuditToolKindInvalid] = valid with { ToolKind = AgentToolKind.SourceApply },
            [ToolExecutionAuditValidator.ToolAuditRequestTypeInvalid] = valid with { RequestType = AgentToolRequestType.SourceMutationRequest },
            [ToolExecutionAuditValidator.ToolAuditToolRequestMismatch] = valid with { AgentKind = AgentKind.ImplementationAgent },
            [ToolExecutionAuditValidator.ToolAuditHashRequired] = valid with { PayloadSha256 = "" },
            [ToolExecutionAuditValidator.ToolAuditHashInvalid] = valid with { PayloadSha256 = new string('a', 64) },
            [ToolExecutionAuditValidator.ToolAuditEvidenceRequired] = valid with { EvidenceRefs = [] },
            [ToolExecutionAuditValidator.ToolAuditRawReasoningBlocked] = valid with { ContainsRawPrivateReasoning = true },
            [ToolExecutionAuditValidator.ToolAuditSecretBlocked] = valid with { ContainsSecret = true },
            [ToolExecutionAuditValidator.ToolAuditApprovalClaimBlocked] = valid with { ClaimsApproval = true },
            [ToolExecutionAuditValidator.ToolAuditMemoryPromotionClaimBlocked] = valid with { ClaimsMemoryPromotion = true },
            [ToolExecutionAuditValidator.ToolAuditUnsafeEffectBlocked] = valid with { MutatesSource = true },
            [ToolExecutionAuditValidator.ToolAuditPayloadTextUnsafe] = RehashPayload(valid with { PayloadJson = "{\"summary\":\"raw private reasoning: unsafe\"}" }),
            [ToolExecutionAuditValidator.ToolAuditEnvelopeTextUnsafe] = RehashEnvelope(valid with { AuditEnvelopeJson = "{\"summary\":\"grant authority\"}" })
        };

        var validator = new ToolExecutionAuditValidator();
        foreach (var pair in cases)
        {
            var issues = validator.Validate(pair.Value);
            AssertHasIssue(issues, pair.Key);
        }
    }

    [TestMethod]
    public async Task ToolExecutionAuditStore_AppendsReadsAndListsTesterAndImplementationRecords()
    {
        var tester = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        var implementation = ToolExecutionAuditRecordFactory.FromManualImplementationPatchProposalResult(SuccessfulImplementationResult(), CreatedAt.AddSeconds(1));

        var testerAppend = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = tester });
        var implementationAppend = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = implementation });

        Assert.AreEqual(ToolExecutionAuditAppendStatus.Appended, testerAppend.Status);
        Assert.AreEqual(ToolExecutionAuditAppendStatus.Appended, implementationAppend.Status);

        var read = await _store.GetAsync(new ToolExecutionAuditQuery
        {
            TenantId = tester.TenantId,
            ProjectId = tester.ProjectId,
            ToolExecutionAuditId = tester.ToolExecutionAuditId
        });
        Assert.IsTrue(read.Found);
        Assert.AreEqual(tester.PayloadSha256, read.Record!.PayloadSha256);

        var listed = await _store.ListByRunAsync(new ToolExecutionAuditRunQuery
        {
            TenantId = tester.TenantId,
            ProjectId = tester.ProjectId,
            RunId = tester.RunId!
        });
        Assert.AreEqual(2, listed.Count);
        Assert.IsTrue(listed.Any(record => record.PayloadKind == ToolExecutionAuditRecordFactory.ManualTesterPayloadKind));
        Assert.IsTrue(listed.Any(record => record.PayloadKind == ToolExecutionAuditRecordFactory.ManualImplementationPatchProposalPayloadKind));
    }

    [TestMethod]
    public async Task ToolExecutionAuditStore_IsIdempotentForSameHashAndConflictsForDifferentHash()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);

        var first = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });
        var second = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });
        var changed = RehashPayload(record with { PayloadJson = record.PayloadJson.Replace("Scripted test-plan executor", "Changed scripted test-plan executor", StringComparison.Ordinal) });
        var conflict = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = changed });

        Assert.AreEqual(ToolExecutionAuditAppendStatus.Appended, first.Status);
        Assert.AreEqual(ToolExecutionAuditAppendStatus.AlreadyExists, second.Status);
        Assert.AreEqual(ToolExecutionAuditAppendStatus.Conflict, conflict.Status);
        AssertHasIssue(conflict.Issues, ToolExecutionAuditValidator.ToolAuditStoreConflict);
    }

    [TestMethod]
    public async Task ToolExecutionAuditStore_IsScopedByTenantProjectAndRun()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });

        var wrongProject = await _store.GetAsync(new ToolExecutionAuditQuery
        {
            TenantId = record.TenantId,
            ProjectId = "other-project",
            ToolExecutionAuditId = record.ToolExecutionAuditId
        });

        var wrongRun = await _store.ListByRunAsync(new ToolExecutionAuditRunQuery
        {
            TenantId = record.TenantId,
            ProjectId = record.ProjectId,
            RunId = "other-run"
        });

        Assert.IsFalse(wrongProject.Found);
        Assert.AreEqual(0, wrongRun.Count);
    }

    [TestMethod]
    public async Task ToolExecutionAuditSqlBoundary_DirectUnsafeFlagsAndUpdatesAreBlocked()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ContainsRawPrivateReasoning = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsApproval = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsPolicyApproval = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsHumanApproval = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsMemoryPromotion = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ExecutesTool = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { MutatesSource = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { WritesFiles = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { RunsGit = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { CallsExternalSystem = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { SubmitsGitHubReview = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { WritesWeaviate = true }));

        await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });
        await ExpectSqlFailsAsync(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.ExecuteAsync(
                "UPDATE toolaudit.ToolExecutionAuditRecord SET Status = N'Tampered' WHERE ToolExecutionAuditId = @ToolExecutionAuditId",
                new { record.ToolExecutionAuditId });
        });
        await ExpectSqlFailsAsync(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.ExecuteAsync(
                "DELETE FROM toolaudit.ToolExecutionAuditRecord WHERE ToolExecutionAuditId = @ToolExecutionAuditId",
                new { record.ToolExecutionAuditId });
        });
    }

    [TestMethod]
    public void ToolExecutionAuditStore_StaticBoundary_UsesStoredProceduresAndNoExecutorRuntimeWiring()
    {
        var root = FindRepositoryRoot();
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "ToolExecutionAudit", "SqlToolExecutionAuditStore.cs"));
        var migrationText = File.ReadAllText(Path.Combine(root, "Database", "migrate_tool_execution_audit.sql"));
        var coreText = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Agents", "ToolExecutionAuditModels.cs"));
        var apiProgramText = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var cliText = File.Exists(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"))
            ? File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"))
            : string.Empty;

        StringAssert.Contains(storeText, "CommandType.StoredProcedure");
        AssertNoForbiddenTokens(storeText, "INSERT INTO toolaudit.ToolExecutionAuditRecord", "UPDATE toolaudit.ToolExecutionAuditRecord", "DELETE FROM toolaudit.ToolExecutionAuditRecord", "MERGE toolaudit.ToolExecutionAuditRecord", "ProcessStartInfo", "File.Copy", "File.Delete");
        AssertNoForbiddenTokens(coreText, "SqlConnection", "Dapper", "ProcessStartInfo", "File.Copy", "File.Delete", "HttpClient");
        AssertNoForbiddenTokens(apiProgramText, "SqlToolExecutionAuditStore", "IToolExecutionAuditStore");
        AssertNoForbiddenTokens(cliText, "SqlToolExecutionAuditStore", "IToolExecutionAuditStore");
        AssertNoForbiddenTokens(migrationText, "CREATE TABLE dbo.ToolExecutionAudit");
        StringAssert.Contains(migrationText, "CREATE OR ALTER PROCEDURE toolaudit.AppendToolExecutionAuditRecord");
        StringAssert.Contains(migrationText, "DENY INSERT, UPDATE, DELETE ON OBJECT::toolaudit.ToolExecutionAuditRecord");
    }

    private static ManualTesterAgentToolExecutionResult SuccessfulTesterResult() =>
        new ManualTesterAgentToolExecutionService(BackendManualToolExecutionFixtures.ScriptedTestExecutorSucceedsWithEvidence()).Execute(BackendManualToolExecutionFixtures.TesterExecutionRequestWithGovernanceGateApproval());

    private static ManualTesterAgentToolExecutionResult FailedTesterResult()
    {
        var executor = new ScriptedTestRunPlanExecutor(request => new TestRunPlanExecutionResult
        {
            Succeeded = false,
            ExecutionId = request.ExecutionId,
            ExitCode = 1,
            Summary = "Scripted test-plan executor found failures.",
            Outcome = "failed",
            TestsPassed = 7,
            TestsFailed = 2,
            Duration = TimeSpan.FromSeconds(3),
            EvidenceRefs = ["test-failure-1", request.TestPlanRef]
        });

        return new ManualTesterAgentToolExecutionService(executor).Execute(BackendManualToolExecutionFixtures.TesterExecutionRequestWithGovernanceGateApproval() with { ManualExecutionId = "manual-test-execution-failed" });
    }

    private static ManualImplementationPatchProposalResult SuccessfulImplementationResult() =>
        new ManualImplementationAgentPatchProposalService(BackendManualToolExecutionFixtures.ScriptedPatchProposalGeneratorReturnsProposalOnlyPackage()).Propose(BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource());

    private static ToolExecutionAuditRecord RehashPayload(ToolExecutionAuditRecord record) =>
        record with { PayloadSha256 = ToolExecutionAuditRecordFactory.Sha256(record.PayloadJson) };

    private static ToolExecutionAuditRecord RehashEnvelope(ToolExecutionAuditRecord record) =>
        record with { AuditEnvelopeSha256 = ToolExecutionAuditRecordFactory.Sha256(record.AuditEnvelopeJson) };

    private static void AssertNoValidationIssues(ToolExecutionAuditRecord record)
    {
        var issues = new ToolExecutionAuditValidator().Validate(record);
        Assert.IsFalse(issues.Any(), FormatIssues(issues));
    }

    private static void AssertNoUnsafeFlags(ToolExecutionAuditRecord record)
    {
        Assert.IsFalse(record.ContainsRawPrivateReasoning);
        Assert.IsFalse(record.ContainsSecret);
        Assert.IsFalse(record.ClaimsApproval);
        Assert.IsFalse(record.ClaimsPolicyApproval);
        Assert.IsFalse(record.ClaimsHumanApproval);
        Assert.IsFalse(record.ClaimsMemoryPromotion);
        Assert.IsFalse(record.ExecutesTool);
        Assert.IsFalse(record.MutatesSource);
        Assert.IsFalse(record.AppliesPatch);
        Assert.IsFalse(record.WritesFiles);
        Assert.IsFalse(record.DeletesFiles);
        Assert.IsFalse(record.RunsGit);
        Assert.IsFalse(record.CallsExternalSystem);
        Assert.IsFalse(record.SubmitsGitHubReview);
        Assert.IsFalse(record.CreatesPullRequest);
        Assert.IsFalse(record.PromotesMemory);
        Assert.IsFalse(record.CreatesCollectiveMemory);
        Assert.IsFalse(record.WritesWeaviate);
    }

    private async Task DirectInsertAsync(ToolExecutionAuditRecord record)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO toolaudit.ToolExecutionAuditRecord
            (
                ToolExecutionAuditId, TenantId, ProjectId, CampaignId, RunId, AgentRunId, ManualExecutionId,
                ToolRequestId, GateDecisionId, ToolKind, RequestType, AgentKind, AgentId, AgentName, Status,
                Succeeded, PayloadKind, PayloadJson, PayloadSha256, AuditEnvelopeJson, AuditEnvelopeSha256,
                EvidenceRefsJson, CreatedAtUtc, ContainsRawPrivateReasoning, ContainsSecret, ClaimsApproval,
                ClaimsPolicyApproval, ClaimsHumanApproval, ClaimsMemoryPromotion, ExecutesTool, MutatesSource,
                AppliesPatch, WritesFiles, DeletesFiles, RunsGit, CallsExternalSystem, SubmitsGitHubReview,
                CreatesPullRequest, PromotesMemory, CreatesCollectiveMemory, WritesWeaviate
            )
            VALUES
            (
                @ToolExecutionAuditId, @TenantId, @ProjectId, @CampaignId, @RunId, @AgentRunId, @ManualExecutionId,
                @ToolRequestId, @GateDecisionId, @ToolKind, @RequestType, @AgentKind, @AgentId, @AgentName, @Status,
                @Succeeded, @PayloadKind, @PayloadJson, @PayloadSha256, @AuditEnvelopeJson, @AuditEnvelopeSha256,
                @EvidenceRefsJson, @CreatedAtUtc, @ContainsRawPrivateReasoning, @ContainsSecret, @ClaimsApproval,
                @ClaimsPolicyApproval, @ClaimsHumanApproval, @ClaimsMemoryPromotion, @ExecutesTool, @MutatesSource,
                @AppliesPatch, @WritesFiles, @DeletesFiles, @RunsGit, @CallsExternalSystem, @SubmitsGitHubReview,
                @CreatesPullRequest, @PromotesMemory, @CreatesCollectiveMemory, @WritesWeaviate
            );
            """,
            new
            {
                record.ToolExecutionAuditId,
                record.TenantId,
                record.ProjectId,
                record.CampaignId,
                record.RunId,
                record.AgentRunId,
                record.ManualExecutionId,
                record.ToolRequestId,
                record.GateDecisionId,
                ToolKind = record.ToolKind.ToString(),
                RequestType = record.RequestType.ToString(),
                AgentKind = record.AgentKind.ToString(),
                record.AgentId,
                record.AgentName,
                record.Status,
                record.Succeeded,
                record.PayloadKind,
                record.PayloadJson,
                record.PayloadSha256,
                record.AuditEnvelopeJson,
                record.AuditEnvelopeSha256,
                EvidenceRefsJson = ToolExecutionAuditRecordFactory.Serialize(record.EvidenceRefs),
                record.CreatedAtUtc,
                record.ContainsRawPrivateReasoning,
                record.ContainsSecret,
                record.ClaimsApproval,
                record.ClaimsPolicyApproval,
                record.ClaimsHumanApproval,
                record.ClaimsMemoryPromotion,
                record.ExecutesTool,
                record.MutatesSource,
                record.AppliesPatch,
                record.WritesFiles,
                record.DeletesFiles,
                record.RunsGit,
                record.CallsExternalSystem,
                record.SubmitsGitHubReview,
                record.CreatesPullRequest,
                record.PromotesMemory,
                record.CreatesCollectiveMemory,
                record.WritesWeaviate
            });
    }

    private static async Task ExpectSqlFailsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail("Expected SQL operation to fail.");
    }

    private static void ExpectArgumentException(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }

        Assert.Fail("Expected ArgumentException.");
    }

    private async Task ApplyToolExecutionAuditMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var migration = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_tool_execution_audit.sql"));
        foreach (var batch in SplitSqlBatches(migration))
            await connection.ExecuteAsync(batch);
    }

    private async Task DropToolExecutionAuditSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'toolaudit.TR_ToolExecutionAuditRecord_BlockUpdateDelete', N'TR') IS NOT NULL
                DROP TRIGGER toolaudit.TR_ToolExecutionAuditRecord_BlockUpdateDelete;
            IF OBJECT_ID(N'toolaudit.AppendToolExecutionAuditRecord', N'P') IS NOT NULL
                DROP PROCEDURE toolaudit.AppendToolExecutionAuditRecord;
            IF OBJECT_ID(N'toolaudit.GetToolExecutionAuditRecord', N'P') IS NOT NULL
                DROP PROCEDURE toolaudit.GetToolExecutionAuditRecord;
            IF OBJECT_ID(N'toolaudit.ListToolExecutionAuditRecordsByRun', N'P') IS NOT NULL
                DROP PROCEDURE toolaudit.ListToolExecutionAuditRecordsByRun;
            IF OBJECT_ID(N'toolaudit.ToolExecutionAuditRecord', N'U') IS NOT NULL
                DROP TABLE toolaudit.ToolExecutionAuditRecord;
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevToolExecutionAuditRuntimeRole' AND type = N'R')
                DROP ROLE IronDevToolExecutionAuditRuntimeRole;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'toolaudit')
                EXEC(N'DROP SCHEMA toolaudit');
            """);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        sql.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\nGO\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static void AssertHasIssue(IReadOnlyList<ToolExecutionAuditIssue> issues, string code) =>
        Assert.IsTrue(issues.Any(issue => issue.Code == code), $"Expected issue {code}.{Environment.NewLine}{FormatIssues(issues)}");

    private static string FormatIssues(IReadOnlyList<ToolExecutionAuditIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected token found: {token}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
