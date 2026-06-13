using System.Net;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkflowReadOnlyApiContractTests : ApiTestBase
{
    private const string PrivateText = "PRIVATE_MARKER chain-of-thought hidden reasoning rawPrompt rawCompletion rawToolOutput scratchpad private reasoning entirePatch";

    [TestMethod]
    public async Task WorkflowReadOnlyApi_ExposesReadOnlyRoutes()
    {
        var seeded = await SeedWorkflowAsync();
        using var client = await AuthedClientAsync();

        var responses = new[]
        {
            await client.GetAsync($"/api/v1/workflow/runs?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}?projectId={seeded.ProjectId}"),
            await client.GetAsync($"/api/v1/workflow/runs/by-correlation/{seeded.CorrelationId}?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/by-subject?projectId={seeded.ProjectId}&subjectType=ticket&subjectId={seeded.SubjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}/checkpoints?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}")
        };

        foreach (var response in responses)
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
            StringAssert.Contains(text, "readOnlyInspection");
            AssertNoMisleadingAuthorityLanguage(text);
        }
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_GetsStoredRunStepAndCheckpointFacts()
    {
        var seeded = await SeedWorkflowAsync();
        using var client = await AuthedClientAsync();

        var run = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}?projectId={seeded.ProjectId}"));
        var step = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}"));
        var checkpoint = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}"));

        Assert.AreEqual("succeeded", run.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(seeded.WorkflowRunId.ToString(), run.RootElement.GetProperty("data").GetProperty("workflowRunId").GetString());
        Assert.AreEqual("ManualDogfoodLoop", run.RootElement.GetProperty("data").GetProperty("workflowType").GetString());
        Assert.IsTrue(run.RootElement.GetProperty("data").GetProperty("evidenceReferences").GetArrayLength() > 0);
        Assert.IsTrue(run.RootElement.GetProperty("data").GetProperty("groundingReferences").GetArrayLength() > 0);
        AssertAuthorityFlagsFalse(run.RootElement.GetProperty("data").GetProperty("authorityFlags"));

        var stepData = step.RootElement.GetProperty("data");
        Assert.AreEqual(seeded.WorkflowRunStepId.ToString(), stepData.GetProperty("workflowRunStepId").GetString());
        Assert.AreEqual(1, stepData.GetProperty("sequenceNumber").GetInt32());
        Assert.IsTrue(stepData.GetProperty("evidenceReferences").GetArrayLength() > 0);
        Assert.IsTrue(stepData.GetProperty("groundingReferences").GetArrayLength() > 0);
        AssertAuthorityFlagsFalse(stepData.GetProperty("authorityFlags"));

        var checkpointData = checkpoint.RootElement.GetProperty("data");
        Assert.AreEqual(seeded.WorkflowCheckpointId.ToString(), checkpointData.GetProperty("workflowCheckpointId").GetString());
        Assert.AreEqual("ReviewSnapshot", checkpointData.GetProperty("checkpointType").GetString());
        Assert.IsTrue(checkpointData.GetProperty("evidenceReferences").GetArrayLength() > 0);
        Assert.IsTrue(checkpointData.GetProperty("groundingReferences").GetArrayLength() > 0);
        AssertAuthorityFlagsFalse(checkpointData.GetProperty("authorityFlags"));
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_ListsRunsStepsAndCheckpoints()
    {
        var seeded = await SeedWorkflowAsync();
        using var client = await AuthedClientAsync();

        var byProject = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs?projectId={seeded.ProjectId}&take=10"));
        var byCorrelation = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/by-correlation/{seeded.CorrelationId}?projectId={seeded.ProjectId}&take=10"));
        var bySubject = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/by-subject?projectId={seeded.ProjectId}&subjectType=ticket&subjectId={seeded.SubjectId}&take=10"));
        var steps = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps?projectId={seeded.ProjectId}&take=10"));
        var checkpoints = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints?projectId={seeded.ProjectId}&take=10"));
        var stepCheckpoints = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}/checkpoints?projectId={seeded.ProjectId}&take=10"));

        Assert.AreEqual(1, byProject.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.AreEqual(1, byCorrelation.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.AreEqual(1, bySubject.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.AreEqual(1, steps.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.AreEqual(1, checkpoints.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.AreEqual(1, stepCheckpoints.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_ReturnsNotFoundForMissingRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();

        var run = await client.GetAsync($"/api/v1/workflow/runs/{runId}?projectId={projectId}");
        var step = await client.GetAsync($"/api/v1/workflow/runs/{runId}/steps/{stepId}?projectId={projectId}");
        var checkpoint = await client.GetAsync($"/api/v1/workflow/runs/{runId}/checkpoints/{checkpointId}?projectId={projectId}");

        Assert.AreEqual(HttpStatusCode.NotFound, run.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, step.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, checkpoint.StatusCode);
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_RejectsInvalidAndUnsupportedReadQueries()
    {
        using var client = await AuthedClientAsync();

        var missingProject = await client.GetAsync($"/api/v1/workflow/runs/{Guid.NewGuid()}?projectId={Guid.Empty}");
        var unsupported = await client.GetAsync($"/api/v1/workflow/runs?projectId={Guid.NewGuid()}&execute=true");
        var missingSubject = await client.GetAsync($"/api/v1/workflow/runs/by-subject?projectId={Guid.NewGuid()}&subjectType=&subjectId=");

        Assert.AreEqual(HttpStatusCode.BadRequest, missingProject.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingSubject.StatusCode);
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_DoesNotLeakAcrossProjectsOrRuns()
    {
        var seeded = await SeedWorkflowAsync();
        var otherProject = Guid.NewGuid();
        var otherRun = Guid.NewGuid();
        using var client = await AuthedClientAsync();

        var list = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs?projectId={otherProject}&take=10"));
        var run = await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}?projectId={otherProject}");
        var stepWrongProject = await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={otherProject}");
        var stepWrongRun = await client.GetAsync($"/api/v1/workflow/runs/{otherRun}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}");
        var checkpointWrongProject = await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={otherProject}");
        var checkpointWrongRun = await client.GetAsync($"/api/v1/workflow/runs/{otherRun}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}");
        var checkpointWrongStep = await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{Guid.NewGuid()}/checkpoints?projectId={seeded.ProjectId}&take=10");

        Assert.AreEqual(0, list.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.AreEqual(HttpStatusCode.NotFound, run.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, stepWrongProject.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, stepWrongRun.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, checkpointWrongProject.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, checkpointWrongRun.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, checkpointWrongStep.StatusCode);
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_ReturnsEvidenceAndGroundingAsNonAuthoritativeTraceability()
    {
        var seeded = await SeedWorkflowAsync();
        using var client = await AuthedClientAsync();

        var step = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}"));
        var checkpoint = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}"));
        var text = step.RootElement.ToString() + checkpoint.RootElement.ToString();

        foreach (var evidence in step.RootElement.GetProperty("data").GetProperty("evidenceReferences").EnumerateArray())
            Assert.IsFalse(evidence.GetProperty("evidenceIsPermission").GetBoolean());
        foreach (var grounding in step.RootElement.GetProperty("data").GetProperty("groundingReferences").EnumerateArray())
            Assert.IsFalse(grounding.GetProperty("groundingIsAuthority").GetBoolean());
        foreach (var evidence in checkpoint.RootElement.GetProperty("data").GetProperty("evidenceReferences").EnumerateArray())
            Assert.IsFalse(evidence.GetProperty("evidenceIsPermission").GetBoolean());
        foreach (var grounding in checkpoint.RootElement.GetProperty("data").GetProperty("groundingReferences").EnumerateArray())
            Assert.IsFalse(grounding.GetProperty("groundingIsAuthority").GetBoolean());

        StringAssert.Contains(text, "AgentHandoff");
        StringAssert.Contains(text, "ThoughtLedgerReference");
        StringAssert.Contains(text, "ToolGateDecision");
        StringAssert.Contains(text, "ApprovalDecision");
        StringAssert.Contains(text, "DogfoodReceipt");
        StringAssert.Contains(text, "CriticReview");
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_AuthorityAndActionFlagsAreFalse()
    {
        var seeded = await SeedWorkflowAsync();
        using var client = await AuthedClientAsync();

        var run = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}?projectId={seeded.ProjectId}"));
        var step = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}"));
        var checkpoint = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}"));

        AssertBoundaryFalse(run.RootElement.GetProperty("boundary"));
        AssertBoundaryFalse(step.RootElement.GetProperty("boundary"));
        AssertBoundaryFalse(checkpoint.RootElement.GetProperty("boundary"));
        AssertAuthorityFlagsFalse(run.RootElement.GetProperty("data").GetProperty("authorityFlags"));
        AssertAuthorityFlagsFalse(step.RootElement.GetProperty("data").GetProperty("authorityFlags"));
        AssertAuthorityFlagsFalse(checkpoint.RootElement.GetProperty("data").GetProperty("authorityFlags"));
        AssertNoMisleadingAuthorityLanguage(run.RootElement.ToString() + step.RootElement.ToString() + checkpoint.RootElement.ToString());
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_GetEndpointsDoNotCreateSideEffects()
    {
        var seeded = await SeedWorkflowAsync();
        using var client = await AuthedClientAsync();
        var before = await SideEffectCountsAsync();

        var responses = new[]
        {
            await client.GetAsync($"/api/v1/workflow/runs?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}?projectId={seeded.ProjectId}"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints?projectId={seeded.ProjectId}&take=10"),
            await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}")
        };
        var after = await SideEffectCountsAsync();

        foreach (var response in responses)
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEqual(before.ToArray(), after.ToArray(), "Read-only workflow GET calls must not create side effects.");
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_DoesNotExposeWriteOrCommandRoutes()
    {
        var seeded = await SeedWorkflowAsync();
        using var client = await AuthedClientAsync();
        var before = await SideEffectCountsAsync();
        var routes = new[]
        {
            (HttpMethod.Post, $"/api/v1/workflow/runs?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints?projectId={seeded.ProjectId}"),
            (HttpMethod.Put, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}?projectId={seeded.ProjectId}"),
            (HttpMethod.Patch, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}"),
            (HttpMethod.Delete, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/start?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/continue?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/resume?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/retry?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/dispatch?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/execute?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/approve?projectId={seeded.ProjectId}"),
            (HttpMethod.Post, $"/api/v1/workflow/runs/{seeded.WorkflowRunId}/satisfy-approval?projectId={seeded.ProjectId}")
        };

        foreach (var (method, route) in routes)
        {
            using var request = new HttpRequestMessage(method, route);
            var response = await client.SendAsync(request);
            Assert.IsFalse(response.IsSuccessStatusCode, $"Command/write route unexpectedly succeeded: {method} {route}");
        }

        var after = await SideEffectCountsAsync();
        CollectionAssert.AreEqual(before.ToArray(), after.ToArray(), "Unsupported workflow command routes must not create side effects.");
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_DoesNotExposeHiddenReasoningOrRawDumps()
    {
        var seeded = await SeedWorkflowAsync();
        await PoisonWorkflowTextAsync(seeded);
        using var client = await AuthedClientAsync();

        var run = await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}?projectId={seeded.ProjectId}");
        var step = await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/steps/{seeded.WorkflowRunStepId}?projectId={seeded.ProjectId}");
        var checkpoint = await client.GetAsync($"/api/v1/workflow/runs/{seeded.WorkflowRunId}/checkpoints/{seeded.WorkflowCheckpointId}?projectId={seeded.ProjectId}");
        var text = await run.Content.ReadAsStringAsync() + await step.Content.ReadAsStringAsync() + await checkpoint.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, run.StatusCode, text);
        Assert.AreEqual(HttpStatusCode.OK, step.StatusCode, text);
        Assert.AreEqual(HttpStatusCode.OK, checkpoint.StatusCode, text);
        StringAssert.Contains(text, "[redacted: sensitive workflow text]");
        AssertNoPrivateReasoningLeak(text);
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task WorkflowReadOnlyApi_UnauthenticatedRequestsAreRejected()
    {
        var response = await Client.GetAsync($"/api/v1/workflow/runs?projectId={Guid.NewGuid()}&take=10");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public void WorkflowReadOnlyApi_ControllerDoesNotReferenceRuntimeOrMutationServices()
    {
        var root = RepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "WorkflowReadOnlyApiController.cs"));
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));

        foreach (var token in new[]
        {
            "[HttpPost",
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "WorkflowRunner",
            "WorkflowOrchestrator",
            "TicketBuildWorkflowOrchestrator",
            "Scheduler",
            "BackgroundService",
            "IHostedService",
            "LangGraph",
            "A2aRuntime",
            "MessageBus",
            "MessageQueue",
            "QueueClient",
            "ModelClient",
            "ILLMService",
            "IWorkflowRetryRunner",
            "IWorkflowResumeEngine",
            "AgentDispatcher",
            "ToolExecutor",
            "IControlledWorktreeApplyService",
            "ApplyCopy",
            "PromoteCollectiveMemory",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "File.Copy",
            "File.Delete",
            "ProcessStartInfo"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in workflow read-only API controller: {token}");
        }

        StringAssert.Contains(text, "IWorkflowRunStore");
        StringAssert.Contains(text, "IWorkflowStepStore");
        StringAssert.Contains(text, "IWorkflowCheckpointStore");
        StringAssert.Contains(program, "IWorkflowRunStore");
        StringAssert.Contains(program, "IWorkflowStepStore");
        StringAssert.Contains(program, "IWorkflowCheckpointStore");
    }

    [TestMethod]
    public void WorkflowReadOnlyApi_DocumentationPreservesBoundaries()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md"));

        StringAssert.Contains(text, "PR103 Workflow Read-only API");
        StringAssert.Contains(text, "read-only workflow inspection endpoints");
        StringAssert.Contains(text, "The API exposes durable workflow run, step, checkpoint, evidence, and grounding facts.");
        StringAssert.Contains(text, "The API does not create workflow records.");
        StringAssert.Contains(text, "The API does not update workflow records.");
        StringAssert.Contains(text, "The API does not delete workflow records.");
        StringAssert.Contains(text, "The API does not execute workflow.");
        StringAssert.Contains(text, "The API does not continue workflow.");
        StringAssert.Contains(text, "The API does not resume workflow.");
        StringAssert.Contains(text, "The API does not retry workflow.");
        StringAssert.Contains(text, "The API does not dispatch agents.");
        StringAssert.Contains(text, "The API does not call tools.");
        StringAssert.Contains(text, "The API does not call models.");
        StringAssert.Contains(text, "The API does not mutate source.");
        StringAssert.Contains(text, "The API does not promote memory.");
        StringAssert.Contains(text, "The API does not create accepted memory.");
        StringAssert.Contains(text, "The API does not approve release.");
        StringAssert.Contains(text, "The API does not satisfy approval requirements.");
        StringAssert.Contains(text, "Statuses returned by the API are stored facts, not runtime actions.");
    }

    private static async Task<SeededWorkflow> SeedWorkflowAsync(Guid? projectId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var runStore = scope.ServiceProvider.GetRequiredService<IWorkflowRunStore>();
        var checkpointStore = scope.ServiceProvider.GetRequiredService<IWorkflowCheckpointStore>();

        var project = projectId ?? Guid.NewGuid();
        var runId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var subjectId = $"ticket-{Guid.NewGuid():N}";
        var handoffId = Guid.NewGuid();
        var thoughtId = Guid.NewGuid();
        var groundingId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var dogfoodId = Guid.NewGuid();
        var criticId = Guid.NewGuid();

        var run = await runStore.CreateAsync(new WorkflowRunCreateRequest
        {
            WorkflowRunId = runId,
            ProjectId = project,
            WorkflowType = "ManualDogfoodLoop",
            WorkflowName = "Manual dogfood workflow inspection",
            Status = WorkflowRunStatus.ReadyForReview,
            SubjectType = "ticket",
            SubjectId = subjectId,
            SubjectSummary = "Safe workflow subject summary.",
            CorrelationId = correlationId,
            CausationId = causationId,
            CreatedByActorType = "human",
            CreatedByActorId = "user-1",
            MetadataVersion = 1,
            MetadataJson = "{\"note\":\"safe workflow run metadata\"}",
            Steps =
            [
                new WorkflowRunStepCreateRequest
                {
                    StepKey = "review-step",
                    StepName = "Review stored workflow facts",
                    StepType = WorkflowRunStepType.Review,
                    Status = WorkflowRunStatus.ReadyForReview,
                    AgentRole = "critic",
                    AgentId = "IndependentCriticAgent",
                    SubjectType = "ticket",
                    SubjectId = subjectId,
                    SafeSummary = "Review step facts are inspectable only.",
                    MetadataVersion = 1,
                    MetadataJson = "{\"note\":\"safe workflow step metadata\"}"
                }
            ],
            EvidenceReferences =
            [
                new WorkflowRunEvidenceReferenceCreateRequest
                {
                    StepKey = "review-step",
                    EvidenceType = WorkflowRunEvidenceType.AgentHandoff,
                    EvidenceId = handoffId.ToString(),
                    EvidenceLabel = "Handoff evidence",
                    SafeSummary = "Handoff record is evidence only.",
                    AllowedUse = WorkflowRunEvidenceAllowedUse.HandoffExplanation,
                    AgentHandoffId = handoffId
                },
                new WorkflowRunEvidenceReferenceCreateRequest
                {
                    StepKey = "review-step",
                    EvidenceType = WorkflowRunEvidenceType.ToolGateDecision,
                    EvidenceId = gateId.ToString(),
                    EvidenceLabel = "Gate decision evidence",
                    SafeSummary = "Gate decision is stored evidence only.",
                    AllowedUse = WorkflowRunEvidenceAllowedUse.AuditReference,
                    GovernanceEventId = gateId
                }
            ],
            GroundingReferences =
            [
                new WorkflowRunGroundingReferenceCreateRequest
                {
                    StepKey = "review-step",
                    GroundingEvidenceReferenceId = groundingId,
                    ClaimType = WorkflowRunGroundingClaimType.EvidenceSupport,
                    ClaimId = "run-grounding-claim",
                    SafeSummary = "Grounding traceability only."
                }
            ]
        });

        var step = run.Steps.Single();

        var checkpoint = await checkpointStore.CreateAsync(new WorkflowCheckpointCreateRequest
        {
            WorkflowRunId = run.WorkflowRunId,
            WorkflowRunStepId = step.WorkflowRunStepId,
            ProjectId = project,
            CheckpointKey = "review-checkpoint",
            CheckpointName = "Review checkpoint facts",
            CheckpointType = WorkflowCheckpointType.ReviewSnapshot,
            Status = WorkflowCheckpointStatus.ReadyForReview,
            SubjectType = "ticket",
            SubjectId = subjectId,
            SafeSummary = "Checkpoint facts are inspectable only.",
            StateVersion = 1,
            StateJson = "{\"checkpoint\":\"safe state reference\"}",
            StateHashSha256 = new string('a', 64),
            CorrelationId = correlationId,
            CausationId = causationId,
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-read-api-test",
            MetadataVersion = 1,
            MetadataJson = "{\"note\":\"safe checkpoint metadata\"}",
            EvidenceReferences =
            [
                new WorkflowCheckpointEvidenceReferenceCreateRequest { EvidenceType = WorkflowRunEvidenceType.CriticReview, EvidenceId = criticId.ToString(), EvidenceLabel = "Critic review", SafeSummary = "Critic review supports checkpoint traceability only.", AllowedUse = WorkflowRunEvidenceAllowedUse.Traceability },
                new WorkflowCheckpointEvidenceReferenceCreateRequest { EvidenceType = WorkflowRunEvidenceType.ThoughtLedgerReference, EvidenceId = thoughtId.ToString(), EvidenceLabel = "ThoughtLedger", SafeSummary = "Thought ledger supports checkpoint traceability only.", AllowedUse = WorkflowRunEvidenceAllowedUse.Traceability, ThoughtLedgerEntryId = thoughtId },
                new WorkflowCheckpointEvidenceReferenceCreateRequest { EvidenceType = WorkflowRunEvidenceType.ApprovalDecision, EvidenceId = approvalId.ToString(), EvidenceLabel = "Approval", SafeSummary = "Approval decision remains evidence only.", AllowedUse = WorkflowRunEvidenceAllowedUse.HumanDecisionSupport, GovernanceEventId = approvalId },
                new WorkflowCheckpointEvidenceReferenceCreateRequest { EvidenceType = WorkflowRunEvidenceType.DogfoodReceipt, EvidenceId = dogfoodId.ToString(), EvidenceLabel = "Dogfood", SafeSummary = "Dogfood receipt remains evidence only.", AllowedUse = WorkflowRunEvidenceAllowedUse.AuditReference, GovernanceEventId = dogfoodId }
            ],
            GroundingReferences =
            [
                new WorkflowCheckpointGroundingReferenceCreateRequest
                {
                    GroundingReferenceId = groundingId,
                    ClaimType = WorkflowRunGroundingClaimType.DecisionTrace,
                    ClaimId = "checkpoint-grounding-claim",
                    SafeSummary = "Checkpoint grounding traceability only."
                }
            ]
        });

        return new SeededWorkflow(project, run.WorkflowRunId, step.WorkflowRunStepId, checkpoint.WorkflowCheckpointId, correlationId, subjectId);
    }

    private static async Task PoisonWorkflowTextAsync(SeededWorkflow seeded)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync("""
                IF OBJECT_ID('workflow.TR_WorkflowRun_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRun_BlockUpdateDelete ON workflow.WorkflowRun;
                IF OBJECT_ID('workflow.TR_WorkflowRunStep_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRunStep_BlockUpdateDelete ON workflow.WorkflowRunStep;
                IF OBJECT_ID('workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete ON workflow.WorkflowRunEvidenceReference;
                IF OBJECT_ID('workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete ON workflow.WorkflowRunGroundingReference;
                IF OBJECT_ID('workflow.TR_WorkflowCheckpoint_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowCheckpoint_BlockUpdateDelete ON workflow.WorkflowCheckpoint;
                IF OBJECT_ID('workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete ON workflow.WorkflowCheckpointEvidenceReference;
                IF OBJECT_ID('workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete ON workflow.WorkflowCheckpointGroundingReference;
                UPDATE workflow.WorkflowRun SET SubjectSummary = @PrivateText WHERE ProjectId = @ProjectId AND WorkflowRunId = @WorkflowRunId;
                UPDATE workflow.WorkflowRunStep SET SafeSummary = @PrivateText WHERE ProjectId = @ProjectId AND WorkflowRunId = @WorkflowRunId;
                UPDATE workflow.WorkflowRunEvidenceReference SET SafeSummary = @PrivateText WHERE ProjectId = @ProjectId AND WorkflowRunId = @WorkflowRunId;
                UPDATE workflow.WorkflowRunGroundingReference SET SafeSummary = @PrivateText WHERE ProjectId = @ProjectId AND WorkflowRunId = @WorkflowRunId;
                UPDATE workflow.WorkflowCheckpoint SET SafeSummary = @PrivateText WHERE ProjectId = @ProjectId AND WorkflowRunId = @WorkflowRunId;
                UPDATE workflow.WorkflowCheckpointEvidenceReference SET SafeSummary = @PrivateText WHERE ProjectId = @ProjectId AND WorkflowRunId = @WorkflowRunId;
                UPDATE workflow.WorkflowCheckpointGroundingReference SET SafeSummary = @PrivateText WHERE ProjectId = @ProjectId AND WorkflowRunId = @WorkflowRunId;
                """, new { seeded.ProjectId, seeded.WorkflowRunId, PrivateText });
        }
        finally
        {
            await connection.ExecuteAsync("""
                IF OBJECT_ID('workflow.TR_WorkflowRun_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRun_BlockUpdateDelete ON workflow.WorkflowRun;
                IF OBJECT_ID('workflow.TR_WorkflowRunStep_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRunStep_BlockUpdateDelete ON workflow.WorkflowRunStep;
                IF OBJECT_ID('workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete ON workflow.WorkflowRunEvidenceReference;
                IF OBJECT_ID('workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete ON workflow.WorkflowRunGroundingReference;
                IF OBJECT_ID('workflow.TR_WorkflowCheckpoint_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowCheckpoint_BlockUpdateDelete ON workflow.WorkflowCheckpoint;
                IF OBJECT_ID('workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete ON workflow.WorkflowCheckpointEvidenceReference;
                IF OBJECT_ID('workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete ON workflow.WorkflowCheckpointGroundingReference;
                """);
        }
    }

    private static async Task<IReadOnlyDictionary<string, long>> SideEffectCountsAsync()
    {
        var tables = new[]
        {
            "workflow.WorkflowRun",
            "workflow.WorkflowRunStep",
            "workflow.WorkflowRunEvidenceReference",
            "workflow.WorkflowRunGroundingReference",
            "workflow.WorkflowCheckpoint",
            "workflow.WorkflowCheckpointEvidenceReference",
            "workflow.WorkflowCheckpointGroundingReference",
            "governance.ToolRequest",
            "governance.ToolGateDecision",
            "governance.ApprovalDecision",
            "governance.PolicyDecisionEvent",
            "governance.DogfoodReceipt",
            "a2a.AgentHandoff",
            "agent.AgentLocalMemory",
            "agent.AgentMemoryImprovementProposal",
            "dbo.ToolExecutionAuditRecord",
            "dbo.AgentRunAuditEnvelope"
        };

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var table in tables)
        {
            var exists = await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END", new { TableName = table });
            counts[table] = exists == 0 ? 0 : await connection.ExecuteScalarAsync<long>($"SELECT COUNT_BIG(*) FROM {table}");
        }

        return counts;
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundaryFalse(JsonElement boundary)
    {
        Assert.IsTrue(boundary.GetProperty("readOnlyInspection").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("workflowStatusIsAction").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("evidenceIsPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("groundingIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("apiResponseStatusIsGovernance").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("releaseApproved").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("approvalSatisfied").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertAuthorityFlagsFalse(JsonElement flags)
    {
        foreach (var property in flags.EnumerateObject())
            Assert.IsFalse(property.Value.GetBoolean(), $"Authority/action flag must be false: {property.Name}");
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        foreach (var token in new[]
        {
            "PRIVATE_MARKER",
            "chain-of-thought",
            "hidden reasoning",
            "rawPrompt",
            "rawCompletion",
            "rawToolOutput",
            "scratchpad",
            "private reasoning",
            "entirePatch"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response leaked private/raw marker: {token}");
        }
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        var normalized = text.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        foreach (var token in new[]
        {
            "grantsapproval:true",
            "grantsexecution:true",
            "mutatessource:true",
            "promotesmemory:true",
            "startsworkflow:true",
            "continuesworkflow:true",
            "resumesworkflow:true",
            "retriesworkflow:true",
            "satisfiespolicy:true",
            "transfersauthority:true",
            "approvesrelease:true",
            "createsacceptedmemory:true",
            "workflowstatusisaction:true",
            "evidenceispermission:true",
            "groundingisauthority:true",
            "endpointaccessisexecutionpermission:true",
            "apiresponsestatusisgovernance:true",
            "modeloutputisauthority:true",
            "sourceapplied:true",
            "memorypromoted:true",
            "releaseapproved:true",
            "approvalsatisfied:true"
        })
        {
            Assert.IsFalse(normalized.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority flag: {token}");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record SeededWorkflow(
        Guid ProjectId,
        Guid WorkflowRunId,
        Guid WorkflowRunStepId,
        Guid WorkflowCheckpointId,
        Guid CorrelationId,
        string SubjectId);
}
