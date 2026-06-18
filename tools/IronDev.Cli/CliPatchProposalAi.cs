using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Ai;
using IronDev.Core.Governance;
using IronDev.Core.Tools;

namespace IronDev.Cli;

public static partial class IronDevCliPatchProposal
{
    private const string TaskContextMarkdownArtifactName = "task-context.md";
    private const string TaskContextJsonArtifactName = "task-context.json";
    private const string ModelRequestsArtifactName = "model-requests.jsonl";
    private const string ModelResponsesArtifactName = "model-responses.jsonl";
    private const string PatchSuggestionsArtifactName = "patch-suggestions.jsonl";
    private const string ModelEditPlanArtifactName = "model-edit-plan.json";
    private const string ModelResponseMarkdownArtifactName = "model-response.md";
    private const string AiAssistSummaryArtifactName = "ai-assist-summary.md";
    private const string TestFailureAnalysisArtifactName = "test-failure-analysis.md";
    private const string RefinementIterationsArtifactName = "refinement-iterations.jsonl";
    private const string AiReviewMarkdownArtifactName = "ai-review.md";
    private const string AiReviewJsonArtifactName = "ai-review.json";
    private const int DefaultRefinementIterations = 1;
    private const int MaxRefinementIterations = 3;

    private static readonly JsonSerializerOptions AiJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions AiJsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<int> HandlePatchAssistAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePatchAiCommand(args, allowIterations: false);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch assist", parsed.Error);

        var loaded = await LoadPatchRunForAiAsync(parsed.Run!, parsed.RunsRootPath, "patch assist", output, error, parsed.Json, cancellationToken).ConfigureAwait(false);
        if (loaded.Run is null)
            return loaded.ExitCode;

        var provider = CreatePatchModelProvider(parsed.ProviderName);
        if (provider.Provider is null)
            return WriteFailure(output, error, parsed.Json, "patch assist", provider.Error!);

        PatchAssistResult result;
        try
        {
            result = await RunPatchAssistAsync(loaded.Run, provider.Provider, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return WriteFailure(output, error, parsed.Json, "patch assist", ex.Message);
        }

        await SaveRunAsync(loaded.Run, cancellationToken).ConfigureAwait(false);
        var data = new
        {
            loaded.Run.RunId,
            loaded.Run.RunPath,
            loaded.Run.WorkspacePath,
            result.Context.ContextBundleId,
            result.Request.ModelRequestId,
            result.Response.ModelResponseId,
            appliedEditCount = result.EditResult.Results.Count(item => item.Applied),
            blockedEditCount = result.EditResult.Results.Count(item => !item.Applied),
            artifacts = loaded.Run.Artifacts,
            boundary = Boundary()
        };

        if (parsed.Json)
            WriteJsonEnvelope(output, "patch assist", "succeeded", data, []);
        else
        {
            output.WriteLine($"Patch AI assist completed: {loaded.Run.RunId}");
            output.WriteLine($"Context: {Path.Combine(loaded.Run.RunPath, TaskContextMarkdownArtifactName)}");
            output.WriteLine($"Summary: {Path.Combine(loaded.Run.RunPath, AiAssistSummaryArtifactName)}");
            output.WriteLine("Boundary: model output is proposal evidence only; source repository was not modified.");
        }

        return 0;
    }

    private static async Task<int> HandlePatchRefineAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePatchAiCommand(args, allowIterations: true);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch refine", parsed.Error);

        var maxIterations = parsed.MaxIterations ?? DefaultRefinementIterations;
        if (maxIterations < 1)
            return WriteFailure(output, error, parsed.Json, "patch refine", "--max-iterations must be at least 1.");
        if (maxIterations > MaxRefinementIterations)
            return WriteFailure(output, error, parsed.Json, "patch refine", $"--max-iterations cannot exceed {MaxRefinementIterations}.");

        var loaded = await LoadPatchRunForAiAsync(parsed.Run!, parsed.RunsRootPath, "patch refine", output, error, parsed.Json, cancellationToken).ConfigureAwait(false);
        if (loaded.Run is null)
            return loaded.ExitCode;

        var provider = CreatePatchModelProvider(parsed.ProviderName);
        if (provider.Provider is null)
            return WriteFailure(output, error, parsed.Json, "patch refine", provider.Error!);

        var iterations = new List<RefinementIterationRecord>();
        for (var index = 1; index <= maxIterations; index++)
        {
            _ = await BuildAndWriteContextBundleAsync(loaded.Run, cancellationToken).ConfigureAwait(false);
            var request = CreateModelRequest(loaded.Run, provider.Provider, PatchModelRequestKind.PatchRefinement, [TaskContextJsonArtifactName, "test-output-summary.md"]);
            await AppendAiJsonLineAsync(loaded.Run, ModelRequestsArtifactName, request, cancellationToken).ConfigureAwait(false);
            await RecordModelGovernanceEventAsync(loaded.Run, GovernedActionKind.ModelTestFailureAnalysisRequested, "Model test-failure analysis was requested for bounded refinement.", [ModelRequestsArtifactName, "test-output-summary.md"], cancellationToken).ConfigureAwait(false);

            var response = await provider.Provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            if (ModelResponseContainsUnsafeMaterial(response))
                return WriteFailure(output, error, parsed.Json, "patch refine", "model response contained unsafe or hidden reasoning material.");

            response = await PersistModelResponseAsync(loaded.Run, response, cancellationToken).ConfigureAwait(false);
            await RecordModelGovernanceEventAsync(loaded.Run, GovernedActionKind.ModelTestFailureAnalysisReceived, "Model test-failure analysis was received as proposal evidence.", [ModelResponsesArtifactName, ModelResponseMarkdownArtifactName], cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(loaded.Run.RunPath, TestFailureAnalysisArtifactName), RenderTestFailureAnalysis(loaded.Run, response), cancellationToken).ConfigureAwait(false);

            var plan = ReadEditPlan(response) with { ModelResponseId = response.ModelResponseId, RunId = loaded.Run.RunId };
            await File.WriteAllTextAsync(Path.Combine(loaded.Run.RunPath, ModelEditPlanArtifactName), JsonSerializer.Serialize(plan, AiJsonOptions), cancellationToken).ConfigureAwait(false);
            var editResult = WorkspacePatchEditor.Apply(loaded.Run.WorkspacePath, loaded.Run.SourceRepoPath, plan);
            await RecordWorkspacePatchEditGovernanceAsync(loaded.Run, editResult, cancellationToken).ConfigureAwait(false);

            var tool = await RunWorkspaceToolCommandAsync(loaded.Run, ToolRequestKind.PatchRunTest, loaded.Run.TestCommand, loaded.Run.TestProfileName, cancellationToken).ConfigureAwait(false);
            var testsPassed = tool.Result.WasExecuted && tool.Result.ExitCode == 0;
            var stopReason = testsPassed ? "TestsPassed" : !editResult.AnyApplied ? "NoSafeEditsApplied" : tool.GateDecision.Decision == WorkspaceToolGateDecisionOutcome.Block ? "ToolGateBlockedTestCommand" : index == maxIterations ? "MaxIterationsReached" : "Continue";
            var record = new RefinementIterationRecord
            {
                RefinementIterationId = $"refine_iter_{Guid.NewGuid():N}",
                RunId = loaded.Run.RunId,
                IterationNumber = index,
                ModelRequestId = request.ModelRequestId,
                ModelResponseId = response.ModelResponseId,
                SafeEditsApplied = editResult.AnyApplied,
                UnsafeEditsBlocked = editResult.AnyBlocked,
                TestCommandExecutedThroughToolGate = tool.Result.WasExecuted,
                TestsPassed = testsPassed,
                StopReason = stopReason,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            iterations.Add(record);
            await AppendAiJsonLineAsync(loaded.Run, RefinementIterationsArtifactName, record, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(loaded.Run, GovernedActionKind.PatchRefinementIterationCompleted, $"Patch refinement iteration {index} completed with stop reason {stopReason}.", [RefinementIterationsArtifactName, ToolResultsArtifactName], cancellationToken).ConfigureAwait(false);
            if (stopReason != "Continue")
                break;
        }

        await File.WriteAllTextAsync(Path.Combine(loaded.Run.RunPath, AiAssistSummaryArtifactName), RenderRefinementSummary(loaded.Run, iterations), cancellationToken).ConfigureAwait(false);
        loaded.Run.Artifacts = MergeArtifacts(loaded.Run.Artifacts, [TestFailureAnalysisArtifactName, RefinementIterationsArtifactName, AiAssistSummaryArtifactName, ModelEditPlanArtifactName]);
        await SaveRunAsync(loaded.Run, cancellationToken).ConfigureAwait(false);

        var data = new { loaded.Run.RunId, iterations = iterations.Count, lastStopReason = iterations.LastOrDefault()?.StopReason, artifacts = loaded.Run.Artifacts, boundary = Boundary() };
        if (parsed.Json)
            WriteJsonEnvelope(output, "patch refine", "succeeded", data, []);
        else
            output.WriteLine($"Patch AI refinement completed: {loaded.Run.RunId}; iterations={iterations.Count}");

        return iterations.Any(item => item.TestsPassed) ? 0 : 1;
    }
    private static async Task<int> HandlePatchReviewAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePatchAiCommand(args, allowIterations: false);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch review", parsed.Error);

        var loaded = await LoadPatchRunForAiAsync(parsed.Run!, parsed.RunsRootPath, "patch review", output, error, parsed.Json, cancellationToken).ConfigureAwait(false);
        if (loaded.Run is null)
            return loaded.ExitCode;

        var provider = CreatePatchModelProvider(parsed.ProviderName);
        if (provider.Provider is null)
            return WriteFailure(output, error, parsed.Json, "patch review", provider.Error!);

        var request = CreateModelRequest(loaded.Run, provider.Provider, PatchModelRequestKind.PatchReview, ["patch.diff", "changed-files.txt", "test-output-summary.md", ToolResultsArtifactName, GovernanceEventsArtifactName, "known-risks.md", "manual-apply-instructions.md"]);
        await AppendAiJsonLineAsync(loaded.Run, ModelRequestsArtifactName, request, cancellationToken).ConfigureAwait(false);
        await RecordModelGovernanceEventAsync(loaded.Run, GovernedActionKind.ModelPatchReviewRequested, "Model patch review was requested for review evidence.", [ModelRequestsArtifactName, "patch.diff", "changed-files.txt"], cancellationToken).ConfigureAwait(false);

        var response = await provider.Provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        if (ModelResponseContainsUnsafeMaterial(response))
            return WriteFailure(output, error, parsed.Json, "patch review", "model review contained unsafe or hidden reasoning material.");

        response = await PersistModelResponseAsync(loaded.Run, response, cancellationToken).ConfigureAwait(false);
        var review = BuildAiPatchReview(loaded.Run, response);
        await File.WriteAllTextAsync(Path.Combine(loaded.Run.RunPath, AiReviewMarkdownArtifactName), RenderAiPatchReview(review), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(loaded.Run.RunPath, AiReviewJsonArtifactName), JsonSerializer.Serialize(review, AiJsonOptions), cancellationToken).ConfigureAwait(false);
        await RecordModelGovernanceEventAsync(loaded.Run, GovernedActionKind.ModelPatchReviewReceived, "Model patch review was received as non-authoritative review evidence.", [AiReviewMarkdownArtifactName, AiReviewJsonArtifactName], cancellationToken).ConfigureAwait(false);

        loaded.Run.Artifacts = MergeArtifacts(loaded.Run.Artifacts, [AiReviewMarkdownArtifactName, AiReviewJsonArtifactName, ModelRequestsArtifactName, ModelResponsesArtifactName, ModelResponseMarkdownArtifactName]);
        await SaveRunAsync(loaded.Run, cancellationToken).ConfigureAwait(false);

        var data = new { loaded.Run.RunId, review.AiPatchReviewId, review.Verdict, review.RequiresHumanReview, artifacts = loaded.Run.Artifacts, boundary = Boundary() };
        if (parsed.Json)
            WriteJsonEnvelope(output, "patch review", "succeeded", data, []);
        else
        {
            output.WriteLine($"Patch AI review created: {loaded.Run.RunId}");
            output.WriteLine($"Review: {Path.Combine(loaded.Run.RunPath, AiReviewMarkdownArtifactName)}");
            output.WriteLine("Boundary: AI review is evidence only; human review remains required.");
        }

        return 0;
    }

    private static async Task<int> HandlePatchAiInspectAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePatchAiCommand(args, allowIterations: false);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch ai", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        if (!Directory.Exists(runPath))
            return WriteFailure(output, error, parsed.Json, "patch ai", $"run path was not found: {runPath}");

        var artifacts = new[] { TaskContextMarkdownArtifactName, TaskContextJsonArtifactName, ModelRequestsArtifactName, ModelResponsesArtifactName, PatchSuggestionsArtifactName, ModelEditPlanArtifactName, ModelResponseMarkdownArtifactName, AiAssistSummaryArtifactName, TestFailureAnalysisArtifactName, RefinementIterationsArtifactName, AiReviewMarkdownArtifactName, AiReviewJsonArtifactName };
        var existing = artifacts.Where(item => File.Exists(Path.Combine(runPath, item))).ToArray();

        if (parsed.Json)
        {
            WriteJsonEnvelope(output, "patch ai", "succeeded", new { runPath, artifacts = existing, boundary = Boundary() }, []);
            return 0;
        }

        output.WriteLine($"Patch AI artifacts: {runPath}");
        foreach (var artifact in existing)
            output.WriteLine($"- {artifact}");
        output.WriteLine("Boundary: inspection only; no model call, tool execution, source apply, or approval occurred.");
        return 0;
    }

    private static async Task<PatchAssistResult> RunPatchAssistAsync(PatchProposalRunDocument run, IPatchModelProvider provider, CancellationToken cancellationToken)
    {
        var context = await BuildAndWriteContextBundleAsync(run, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(run, GovernedActionKind.PatchContextBundleCreated, "Patch task context bundle was created for model assistance.", [TaskContextMarkdownArtifactName, TaskContextJsonArtifactName], cancellationToken).ConfigureAwait(false);

        var request = CreateModelRequest(run, provider, PatchModelRequestKind.PatchSuggestion, [TaskContextJsonArtifactName, TaskContextMarkdownArtifactName]);
        await AppendAiJsonLineAsync(run, ModelRequestsArtifactName, request, cancellationToken).ConfigureAwait(false);
        await RecordModelGovernanceEventAsync(run, GovernedActionKind.ModelPatchSuggestionRequested, "Model patch suggestion was requested.", [ModelRequestsArtifactName, TaskContextJsonArtifactName], cancellationToken).ConfigureAwait(false);

        var response = await provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        if (ModelResponseContainsUnsafeMaterial(response))
            throw new InvalidOperationException("model response contained unsafe or hidden reasoning material.");

        response = await PersistModelResponseAsync(run, response, cancellationToken).ConfigureAwait(false);
        await RecordModelGovernanceEventAsync(run, GovernedActionKind.ModelPatchSuggestionReceived, "Model patch suggestion was received as proposal evidence.", [ModelResponsesArtifactName, ModelResponseMarkdownArtifactName], cancellationToken).ConfigureAwait(false);

        var plan = ReadEditPlan(response) with { ModelResponseId = response.ModelResponseId, RunId = run.RunId };
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, ModelEditPlanArtifactName), JsonSerializer.Serialize(plan, AiJsonOptions), cancellationToken).ConfigureAwait(false);
        var editResult = WorkspacePatchEditor.Apply(run.WorkspacePath, run.SourceRepoPath, plan);
        await RecordWorkspacePatchEditGovernanceAsync(run, editResult, cancellationToken).ConfigureAwait(false);

        var suggestion = new PatchSuggestion
        {
            PatchSuggestionId = $"patch_suggestion_{Guid.NewGuid():N}",
            RunId = run.RunId,
            ModelResponseId = response.ModelResponseId,
            Summary = "Deterministic model produced a bounded workspace edit plan.",
            Assumptions = ["Disposable workspace exists.", "Human review remains required."],
            ProposedFiles = plan.Edits.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EditPlanPath = ModelEditPlanArtifactName,
            Confidence = "Low",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await AppendAiJsonLineAsync(run, PatchSuggestionsArtifactName, suggestion, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, AiAssistSummaryArtifactName), RenderAiAssistSummary(run, suggestion, editResult), cancellationToken).ConfigureAwait(false);
        run.Artifacts = MergeArtifacts(run.Artifacts, [TaskContextMarkdownArtifactName, TaskContextJsonArtifactName, ModelRequestsArtifactName, ModelResponsesArtifactName, PatchSuggestionsArtifactName, ModelEditPlanArtifactName, ModelResponseMarkdownArtifactName, AiAssistSummaryArtifactName]);
        return new PatchAssistResult(context, request, response, suggestion, plan, editResult);
    }

    private static async Task<PatchTaskContextBundle> BuildAndWriteContextBundleAsync(PatchProposalRunDocument run, CancellationToken cancellationToken)
    {
        var priorTestSummary = File.Exists(Path.Combine(run.RunPath, "test-output-summary.md")) ? "test-output-summary.md" : null;
        var knownRisks = File.Exists(Path.Combine(run.RunPath, "known-risks.md")) ? "known-risks.md" : null;
        var context = PatchTaskContextBundleBuilder.Build(run.RunId, run.TaskPath, run.SourceRepoPath, run.WorkspacePath, run.BaseBranch, run.BaseCommit, run.TestCommand, run.TestProfileName, priorTestSummary, knownRisks, ["run.json", "task.md"]);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, TaskContextMarkdownArtifactName), RenderTaskContextMarkdown(context), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, TaskContextJsonArtifactName), JsonSerializer.Serialize(context, AiJsonOptions), cancellationToken).ConfigureAwait(false);
        run.Artifacts = MergeArtifacts(run.Artifacts, [TaskContextMarkdownArtifactName, TaskContextJsonArtifactName]);
        return context;
    }

    private static ModelRequestEnvelope CreateModelRequest(PatchProposalRunDocument run, IPatchModelProvider provider, PatchModelRequestKind kind, string[] contextRefs) => new()
    {
        ModelRequestId = $"model_req_{Guid.NewGuid():N}",
        RunId = run.RunId,
        RequestKind = kind,
        ProviderName = provider.ProviderName,
        ModelName = provider.ModelName,
        PromptVersion = "patch-ai-assist-v1",
        SystemInstruction = "You are a bounded patch proposal assistant. Return proposal evidence only. Do not approve, apply source, run tools, promote memory, continue workflow, or claim release readiness.",
        UserInstruction = kind switch
        {
            PatchModelRequestKind.PatchSuggestion => "Suggest a small safe patch plan using the supplied task context.",
            PatchModelRequestKind.PatchRefinement => "Suggest bounded workspace edits after test failure evidence.",
            PatchModelRequestKind.TestFailureAnalysis => "Analyze test failure evidence without hidden reasoning.",
            PatchModelRequestKind.PatchReview => "Review the patch package for risks without approval verdicts.",
            _ => "Return bounded patch proposal evidence."
        },
        ContextRefs = contextRefs,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task<ModelResponseEnvelope> PersistModelResponseAsync(PatchProposalRunDocument run, ModelResponseEnvelope response, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, ModelResponseMarkdownArtifactName), response.ResponseText ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(response.StructuredPayloadJson))
            await File.WriteAllTextAsync(Path.Combine(run.RunPath, ModelEditPlanArtifactName), response.StructuredPayloadJson, cancellationToken).ConfigureAwait(false);

        var persisted = response with { ResponseTextPath = ModelResponseMarkdownArtifactName, StructuredPayloadPath = string.IsNullOrWhiteSpace(response.StructuredPayloadJson) ? null : ModelEditPlanArtifactName, ResponseText = null, StructuredPayloadJson = null };
        await AppendAiJsonLineAsync(run, ModelResponsesArtifactName, persisted, cancellationToken).ConfigureAwait(false);
        run.Artifacts = MergeArtifacts(run.Artifacts, [ModelResponsesArtifactName, ModelResponseMarkdownArtifactName, ModelEditPlanArtifactName]);
        return response with { ResponseTextPath = ModelResponseMarkdownArtifactName, StructuredPayloadPath = string.IsNullOrWhiteSpace(response.StructuredPayloadJson) ? null : ModelEditPlanArtifactName };
    }

    private static PatchEditPlan ReadEditPlan(ModelResponseEnvelope response)
    {
        if (string.IsNullOrWhiteSpace(response.StructuredPayloadJson))
            return new PatchEditPlan { PatchEditPlanId = $"edit_plan_{Guid.NewGuid():N}", RunId = response.RunId, ModelResponseId = response.ModelResponseId, CreatedAtUtc = DateTimeOffset.UtcNow };

        return JsonSerializer.Deserialize<PatchEditPlan>(response.StructuredPayloadJson, AiJsonOptions) ?? new PatchEditPlan { PatchEditPlanId = $"edit_plan_{Guid.NewGuid():N}", RunId = response.RunId, ModelResponseId = response.ModelResponseId, CreatedAtUtc = DateTimeOffset.UtcNow };
    }

    private static bool ModelResponseContainsUnsafeMaterial(ModelResponseEnvelope response)
    {
        if (response.HiddenChainOfThoughtStored || PatchAiTextSafety.ContainsUnsafeText(response.ResponseText))
            return true;

        if (string.IsNullOrWhiteSpace(response.StructuredPayloadJson))
            return false;

        try
        {
            var plan = JsonSerializer.Deserialize<PatchEditPlan>(response.StructuredPayloadJson, AiJsonOptions);
            if (plan is null)
                return PatchAiTextSafety.ContainsUnsafeText(response.StructuredPayloadJson);

            return plan.Edits.Any(edit =>
                PatchAiTextSafety.ContainsUnsafeText(edit.FindText) ||
                PatchAiTextSafety.ContainsUnsafeText(edit.ReplaceText) ||
                PatchAiTextSafety.ContainsUnsafeText(edit.NewContent) ||
                PatchAiTextSafety.ContainsUnsafeText(edit.Rationale) ||
                PatchAiTextSafety.ContainsUnsafeText(edit.Risk));
        }
        catch (JsonException)
        {
            return PatchAiTextSafety.ContainsUnsafeText(response.StructuredPayloadJson);
        }
    }

    private static async Task RecordWorkspacePatchEditGovernanceAsync(PatchProposalRunDocument run, WorkspacePatchEditApplicationResult editResult, CancellationToken cancellationToken)
    {
        if (editResult.Results.Length == 0)
            return;

        await RecordGovernanceEventAsync(run, GovernedActionKind.WorkspacePatchEditApplied, $"Workspace patch edit plan completed with {editResult.Results.Count(item => item.Applied)} applied edit(s) and {editResult.Results.Count(item => !item.Applied)} blocked edit(s).", [ModelEditPlanArtifactName, AiAssistSummaryArtifactName], cancellationToken).ConfigureAwait(false);
    }

    private static async Task RecordModelGovernanceEventAsync(PatchProposalRunDocument run, GovernedActionKind actionKind, string message, string[] evidenceRefs, CancellationToken cancellationToken)
    {
        await RecordGovernanceEventAsync(run, actionKind, message, evidenceRefs, cancellationToken, new GovernedActionBoundary { ModelCalled = true }).ConfigureAwait(false);
    }

    private static async Task AppendAiJsonLineAsync<T>(PatchProposalRunDocument run, string artifactName, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(run.RunPath);
        await File.AppendAllTextAsync(Path.Combine(run.RunPath, artifactName), JsonSerializer.Serialize(value, AiJsonLineOptions) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        run.Artifacts = MergeArtifacts(run.Artifacts, [artifactName]);
    }

    private static AiPatchReview BuildAiPatchReview(PatchProposalRunDocument run, ModelResponseEnvelope response)
    {
        var changedFiles = File.Exists(Path.Combine(run.RunPath, "changed-files.txt")) ? SplitLines(File.ReadAllText(Path.Combine(run.RunPath, "changed-files.txt"))) : run.ChangedFiles;
        var testsFailing = string.Equals(run.TestStatus, "Failed", StringComparison.OrdinalIgnoreCase);
        return new AiPatchReview
        {
            AiPatchReviewId = $"ai_review_{Guid.NewGuid():N}",
            RunId = run.RunId,
            ModelResponseId = response.ModelResponseId,
            PatchHash = run.PatchSha256 ?? "not-yet-packaged",
            ChangedFiles = changedFiles,
            Findings = [new AiPatchReviewFinding { Severity = "Info", Summary = "Patch package requires human review before source apply.", EvidenceRefs = ["patch.diff", "changed-files.txt", "test-output-summary.md"] }],
            Risks = testsFailing ? ["Tests are failing or incomplete."] : ["Model review does not prove correctness."],
            MissingTests = testsFailing ? ["Passing validation evidence is missing."] : [],
            BoundaryConcerns = ["AI review is not approval, release readiness, merge authority, or source apply authority."],
            Verdict = testsFailing ? AiPatchReviewVerdict.TestsFailing : AiPatchReviewVerdict.NeedsHumanReview,
            RequiresHumanReview = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static (IPatchModelProvider? Provider, string? Error) CreatePatchModelProvider(string? providerName)
    {
        var normalized = string.IsNullOrWhiteSpace(providerName) ? "deterministic" : providerName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "deterministic" => (new DeterministicPatchModelProvider(), null),
            "configured" => (null, "configured model provider is not enabled in Block AD; use --provider deterministic."),
            "disabled" => (new DisabledPatchModelProvider(), null),
            _ => (null, $"unsupported patch model provider: {providerName}")
        };
    }

    private static async Task<(PatchProposalRunDocument? Run, int ExitCode)> LoadPatchRunForAiAsync(string run, string? runsRootPath, string command, TextWriter output, TextWriter error, bool json, CancellationToken cancellationToken)
    {
        var runPath = ResolveRunPath(run, runsRootPath);
        var document = await LoadRunAsync(runPath, cancellationToken).ConfigureAwait(false);
        if (document is null)
            return (null, WriteFailure(output, error, json, command, $"run metadata was not found: {Path.Combine(runPath, "run.json")}"));
        if (!Directory.Exists(document.WorkspacePath))
            return (null, WriteFailure(output, error, json, command, $"workspace path does not exist: {document.WorkspacePath}"));
        return (document, 0);
    }

    private static ParsedPatchAiCommand ParsePatchAiCommand(string[] args, bool allowIterations)
    {
        string? run = null;
        string? runsRoot = null;
        string? provider = null;
        int? maxIterations = null;
        var json = HasJson(args);

        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--run":
                    if (!TryReadValue(args, ref index, out run)) return ParsedPatchAiCommand.Fail(json, "--run requires a value.");
                    break;
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRoot)) return ParsedPatchAiCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--provider":
                    if (!TryReadValue(args, ref index, out provider)) return ParsedPatchAiCommand.Fail(json, "--provider requires a value.");
                    break;
                case "--max-iterations":
                    if (!TryReadValue(args, ref index, out var raw) || !int.TryParse(raw, out var parsedMax)) return ParsedPatchAiCommand.Fail(json, "--max-iterations requires an integer value.");
                    maxIterations = parsedMax;
                    break;
                case "--json":
                    break;
                default:
                    return ParsedPatchAiCommand.Fail(json, $"unsupported patch AI option: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(run))
            return ParsedPatchAiCommand.Fail(json, "--run is required.");
        if (!allowIterations && maxIterations is not null)
            return ParsedPatchAiCommand.Fail(json, "--max-iterations is only supported by patch refine.");
        return new ParsedPatchAiCommand(run, runsRoot, provider, maxIterations, json, null);
    }

    private static string RenderTaskContextMarkdown(PatchTaskContextBundle context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Patch Task Context");
        builder.AppendLine();
        builder.AppendLine($"Run ID: `{context.RunId}`");
        builder.AppendLine($"Base branch: `{context.BaseBranch}`");
        builder.AppendLine($"Base commit: `{context.BaseCommit}`");
        builder.AppendLine($"Workspace: `{context.WorkspacePath}`");
        builder.AppendLine($"Source repository: `{context.SourceRepoPath}`");
        builder.AppendLine($"Test command: `{context.TestCommand}`");
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine("This context is model input evidence only. It does not approve, apply source, promote memory, continue workflow, or prove release readiness.");
        builder.AppendLine("Hidden chain-of-thought is not stored.");
        builder.AppendLine();
        builder.AppendLine("## Task");
        builder.AppendLine(context.TaskText.Trim());
        builder.AppendLine();
        builder.AppendLine("## File snapshots");
        foreach (var snapshot in context.FileSnapshots)
            builder.AppendLine($"- `{snapshot.RelativePath}` bytes={snapshot.OriginalByteCount} truncated={snapshot.Truncated} sha256={snapshot.Sha256}");
        if (context.FileSnapshotLimitHit || context.ByteLimitHit)
            builder.AppendLine("Snapshot limits were hit; context is incomplete.");
        return builder.ToString();
    }

    private static string RenderAiAssistSummary(PatchProposalRunDocument run, PatchSuggestion suggestion, WorkspacePatchEditApplicationResult editResult)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI Assist Summary");
        builder.AppendLine();
        builder.AppendLine($"Run ID: `{run.RunId}`");
        builder.AppendLine($"Suggestion: `{suggestion.PatchSuggestionId}`");
        builder.AppendLine($"Applied edits: `{editResult.Results.Count(item => item.Applied)}`");
        builder.AppendLine($"Blocked edits: `{editResult.Results.Count(item => !item.Applied)}`");
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine("Model output is proposal evidence only.");
        builder.AppendLine("Model output is not approval, policy satisfaction, release readiness, merge authority, or source apply authority.");
        builder.AppendLine("Safe edits are applied only inside the disposable workspace.");
        builder.AppendLine("Human review remains required.");
        builder.AppendLine();
        builder.AppendLine("## Edit results");
        foreach (var result in editResult.Results)
            builder.AppendLine($"- `{result.Path}` {result.Operation}: {result.Status}");
        return builder.ToString();
    }

    private static string RenderRefinementSummary(PatchProposalRunDocument run, IReadOnlyList<RefinementIterationRecord> iterations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI Refinement Summary");
        builder.AppendLine();
        builder.AppendLine($"Run ID: `{run.RunId}`");
        builder.AppendLine($"Iterations: `{iterations.Count}`");
        builder.AppendLine("Hard maximum: `3`");
        builder.AppendLine("Boundary: bounded refinement is workspace-only and test execution remains gated by ToolRequest, WorkspaceToolGate, and ToolExecutionResult.");
        foreach (var iteration in iterations)
            builder.AppendLine($"- Iteration {iteration.IterationNumber}: {iteration.StopReason}; testsPassed={iteration.TestsPassed}");
        return builder.ToString();
    }

    private static string RenderTestFailureAnalysis(PatchProposalRunDocument run, ModelResponseEnvelope response)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test Failure Analysis");
        builder.AppendLine();
        builder.AppendLine($"Run ID: `{run.RunId}`");
        builder.AppendLine($"Model response: `{response.ModelResponseId}`");
        builder.AppendLine("Deterministic model reviewed latest test evidence and suggested bounded workspace refinement.");
        builder.AppendLine();
        builder.AppendLine("Boundary: analysis is proposal evidence only. It does not rerun tests, approve source apply, or prove correctness.");
        return builder.ToString();
    }

    private static string RenderAiPatchReview(AiPatchReview review)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI Patch Review");
        builder.AppendLine();
        builder.AppendLine($"Review ID: `{review.AiPatchReviewId}`");
        builder.AppendLine($"Verdict: `{review.Verdict}`");
        builder.AppendLine($"Requires human review: `{review.RequiresHumanReview}`");
        builder.AppendLine($"Patch hash: `{review.PatchHash}`");
        builder.AppendLine();
        builder.AppendLine("## Findings");
        foreach (var finding in review.Findings)
            builder.AppendLine($"- {finding.Severity}: {finding.Summary}");
        builder.AppendLine();
        builder.AppendLine("## Risks");
        foreach (var risk in review.Risks)
            builder.AppendLine($"- {risk}");
        builder.AppendLine();
        builder.AppendLine("## Boundary concerns");
        foreach (var concern in review.BoundaryConcerns)
            builder.AppendLine($"- {concern}");
        builder.AppendLine();
        builder.AppendLine("Human review remains required. This review is not approval, release readiness, safe-to-merge authority, safe-to-deploy authority, or automatic source apply.");
        return builder.ToString();
    }

    private sealed record PatchAssistResult(PatchTaskContextBundle Context, ModelRequestEnvelope Request, ModelResponseEnvelope Response, PatchSuggestion Suggestion, PatchEditPlan EditPlan, WorkspacePatchEditApplicationResult EditResult);

    private sealed record ParsedPatchAiCommand(string? Run, string? RunsRootPath, string? ProviderName, int? MaxIterations, bool Json, string? Error)
    {
        public static ParsedPatchAiCommand Fail(bool json, string error) => new(null, null, null, null, json, error);
    }
}
