using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents.WorkspaceApply;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class CriticAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;
    private readonly IAgentWorkspaceApplyContextService? _workspaceApplyContextService;

    public CriticAgent(
        AgentDefinition definition,
        IAgentModelResolver modelResolver,
        IAgentLlmClient? llmClient = null,
        IAgentWorkspaceApplyContextService? workspaceApplyContextService = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
        _workspaceApplyContextService = workspaceApplyContextService;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var packagePath = RequireInput(request, "package_path");

        if (!File.Exists(packagePath))
            throw new FileNotFoundException("CriticAgent failure package not found.", packagePath);

        await using var stream = File.OpenRead(packagePath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var package = document.RootElement.Clone();

        var failureReason = ReadString(package, "FailureReason");
        var expectedJson = ReadString(package, "ExpectedJson");
        var actualJson = ReadString(package, "ActualJson");
        var reproCommand = ReadString(package, "ReproCommand");
        var validationCommand = ReadString(package, "ValidationCommand");
        var evidencePaths = ReadStringArray(package, "EvidencePaths");
        var likelyAreas = ReadStringArray(package, "LikelyAreas");
        var safetyRules = ReadStringArray(package, "SafetyRules");
        var prompt = BuildPrompt(failureReason, expectedJson, actualJson, reproCommand, validationCommand, likelyAreas, safetyRules);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var evidenceSufficient =
            !string.IsNullOrWhiteSpace(failureReason) &&
            !string.IsNullOrWhiteSpace(expectedJson) &&
            !string.IsNullOrWhiteSpace(actualJson) &&
            evidencePaths.Count > 0 &&
            !string.IsNullOrWhiteSpace(reproCommand) &&
            !string.IsNullOrWhiteSpace(validationCommand);
        var actionable = evidenceSufficient && likelyAreas.Count > 0 && safetyRules.Count > 0;
        var recommendation = actionable
            ? "fix_with_smallest_evidence_backed_patch"
            : evidenceSufficient
                ? "ask_for_likely_area"
                : "request_more_evidence";
        var workspaceApplyContext = await BuildWorkspaceApplyContextSummaryAsync(request, ct).ConfigureAwait(false);

        var review = new
        {
            packagePath,
            dogfoodRunId = ReadString(package, "DogfoodRunId"),
            scenarioId = ReadString(package, "ScenarioId"),
            goalId = ReadString(package, "GoalId"),
            failureReason,
            expectedJsonPresent = !string.IsNullOrWhiteSpace(expectedJson),
            actualJsonPresent = !string.IsNullOrWhiteSpace(actualJson),
            evidenceSufficient,
            actionable,
            recommendation,
            likelyAreas,
            evidencePaths,
            safetyRules,
            workspaceApplyContext,
            llmIntelligence = new
            {
                modelProfile = profile.Name,
                profileProvider = profile.Provider,
                profileModel = profile.Model,
                prompt,
                invocationMode = llmResult.InvocationMode,
                liveLlmRequested,
                wasAttempted = llmResult.WasAttempted,
                wasSuccessful = llmResult.WasSuccessful,
                durationMs = llmResult.DurationMs,
                modelSummary = BuildModelSummary(llmResult),
                error = llmResult.WasSuccessful ? string.Empty : llmResult.ErrorMessage
            },
            risks = BuildRisks(package, evidenceSufficient, actionable),
            boundary = "CriticAgent reviews failure-package and workspace apply context evidence only. Live LLM output is advisory evidence and does not patch code, run tests, create tickets, mutate memory, approve writes, or execute workspace commands."
        };

        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = actionable
                ? $"CriticAgent found actionable failure package for {review.goalId}."
                : $"CriticAgent needs more evidence for {review.goalId}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(review, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"critic review-failure --package {QuoteIfNeeded(packagePath)}"],
            EvidencePaths = evidencePaths,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<object?> BuildWorkspaceApplyContextSummaryAsync(AgentRequest request, CancellationToken ct)
    {
        var workspacePath = ReadOptionalInput(request, "workspace_apply_workspace_path");
        if (string.IsNullOrWhiteSpace(workspacePath))
            return null;

        var runId = ReadOptionalInput(request, "workspace_apply_run_id") ??
            request.DogfoodRunId ??
            request.GoalId ??
            "unknown-run";
        var projectId = ReadOptionalInput(request, "project") ??
            request.AgentName ??
            AgentName;
        var contextRequest = new AgentWorkspaceApplyContextRequest
        {
            ProjectId = projectId,
            RunId = runId,
            WorkspacePath = workspacePath
        };

        try
        {
            var service = _workspaceApplyContextService ?? new AgentWorkspaceApplyContextService();
            var context = await service.CreateAsync(contextRequest, ct).ConfigureAwait(false);
            return BuildWorkspaceApplyContextSummary(context);
        }
        catch (Exception exception)
        {
            return BuildUnavailableWorkspaceApplyContextSummary(
                contextRequest,
                $"Workspace apply context could not be produced: {exception.Message}");
        }
    }

    private static object BuildWorkspaceApplyContextSummary(AgentWorkspaceApplyContext context)
    {
        var report = context.WorkspaceApply;
        var recommendation = context.WorkspaceApplyRecommendation;
        var actionRequest = context.WorkspaceApplyActionRequest;
        var actionReview = context.WorkspaceApplyActionReview;
        var policyContext = context.WorkspaceApplyPolicyContext;
        var outcome = report?.Outcome ?? (context.ContextAvailable ? "unknown" : "unavailable");
        var warnings = MergeDistinct(
            context.Warnings,
            report?.Warnings,
            recommendation?.Warnings,
            actionRequest?.Warnings,
            actionReview?.Warnings,
            policyContext?.Warnings);

        if (!context.ContextAvailable)
        {
            warnings = MergeDistinct(
                warnings,
                ["Workspace apply context is unavailable; no usable source-report or failure-package was found."]);
        }

        return new
        {
            available = context.ContextAvailable,
            outcome,
            failedStage = report?.FailedStage,
            failureSeverity = report?.FailureSeverity,
            recommendedAction = recommendation?.RecommendedAction,
            requestedAction = actionRequest?.RequestedAction,
            reviewStatus = actionReview?.ReviewStatus,
            policyDecision = policyContext?.Decision,
            riskTier = policyContext?.RiskTier,
            sourceRepoMayBeMutated = actionReview?.SourceRepoMayBeMutated ?? report?.SourceRepoMutated ?? false,
            executionAllowedByThisAgent = false,
            approvalAllowedByThisAgent = false,
            sourceMutationAllowedByThisAgent = false,
            evidencePaths = MergeDistinct(
                context.EvidencePaths,
                report?.EvidencePaths,
                recommendation?.EvidencePaths,
                actionRequest?.EvidencePaths,
                actionReview?.EvidencePaths,
                policyContext?.EvidencePaths),
            warnings,
            riskNotes = MergeDistinct(
                report?.RiskNotes,
                recommendation?.RiskNotes,
                actionRequest?.RiskNotes,
                actionReview?.RiskNotes,
                policyContext?.RiskNotes),
            interpretation = BuildWorkspaceApplyInterpretation(context)
        };
    }

    private static object BuildUnavailableWorkspaceApplyContextSummary(
        AgentWorkspaceApplyContextRequest request,
        string warning)
    {
        return new
        {
            available = false,
            outcome = "unavailable",
            failedStage = (string?)null,
            failureSeverity = (string?)null,
            recommendedAction = WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport,
            requestedAction = WorkspaceApplyRequestedActions.NoActionAvailable,
            reviewStatus = WorkspaceApplyActionReviewStatuses.BlockedForEvidence,
            policyDecision = ProjectApprovalDecisions.ApprovalRequired,
            riskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
            sourceRepoMayBeMutated = false,
            executionAllowedByThisAgent = false,
            approvalAllowedByThisAgent = false,
            sourceMutationAllowedByThisAgent = false,
            evidencePaths = Array.Empty<string>(),
            warnings = new[] { warning, "Workspace apply context is unavailable; do not infer success." },
            riskNotes = Array.Empty<string>(),
            interpretation = new[]
            {
                $"No usable workspace apply report was available for run '{request.RunId}'.",
                "CriticAgent cannot approve, execute, or mutate source."
            }
        };
    }

    private static IReadOnlyList<string> BuildWorkspaceApplyInterpretation(AgentWorkspaceApplyContext context)
    {
        var report = context.WorkspaceApply;
        if (!context.ContextAvailable || string.Equals(report?.Outcome, "unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "No usable source-report or failure-package evidence was available.",
                "Do not infer success from missing workspace apply context.",
                "CriticAgent cannot approve, execute, or mutate source."
            ];
        }

        if (string.Equals(report?.Outcome, "success", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Source report exists.",
                report.ApplyVerified ? "Apply was verified." : "Apply verification was not proven.",
                report.PostApplyValidationSucceeded ? "Post-apply validation passed." : "Post-apply validation was not proven.",
                "Human review is still required before commit or pull request.",
                "CriticAgent cannot approve, execute, or mutate source."
            ];
        }

        return
        [
            $"Workspace apply outcome is {report?.Outcome ?? "unknown"}.",
            string.IsNullOrWhiteSpace(report?.FailedStage) ? "Failed stage was not reported." : $"Failed stage: {report.FailedStage}.",
            string.IsNullOrWhiteSpace(report?.FailureSeverity) ? "Failure severity was not reported." : $"Failure severity: {report.FailureSeverity}.",
            report?.SourceRepoMutated == true ? "Source repository may already have been mutated; review source before retry." : "No source mutation was reported.",
            "Do not retry automatically.",
            "CriticAgent cannot approve, execute, or mutate source."
        ];
    }

    private async Task<AgentLlmCallResult> ResolveLlmResultAsync(
        ModelProfile profile,
        string prompt,
        bool liveLlmRequested,
        AgentRequest request,
        CancellationToken ct)
    {
        if (request.Inputs.TryGetValue("llm_response", out var providedResponse) &&
            !string.IsNullOrWhiteSpace(providedResponse))
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "provided_llm_response",
                ResponseText = providedResponse
            };
        }

        if (!liveLlmRequested)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "llm_ready_deterministic_fallback",
                ResponseText = "No live model response supplied; deterministic failure-package review was used for this governed smoke."
            };
        }

        if (_llmClient is null)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = false,
                InvocationMode = "live_model_requested_without_client_fallback",
                ErrorMessage = "No governed agent LLM client was configured."
            };
        }

        return await _llmClient.CompleteAsync(profile, prompt, ct);
    }

    private static string BuildPrompt(
        string failureReason,
        string expectedJson,
        string actualJson,
        string reproCommand,
        string validationCommand,
        IReadOnlyList<string> likelyAreas,
        IReadOnlyList<string> safetyRules) =>
        $"""
        You are CriticAgent for IronDev/IDA.
        Review this failure package and return concise JSON with evidence gaps, likely risk areas, and next safe investigation steps.
        Failure reason: {failureReason}
        Expected JSON: {expectedJson}
        Actual JSON: {actualJson}
        Repro command: {reproCommand}
        Validation command: {validationCommand}
        Likely areas: {string.Join("; ", likelyAreas)}
        Safety rules: {string.Join("; ", safetyRules)}
        Do not suggest weakening assertions. Do not patch code, create tickets, mutate memory, or approve writes.
        """;

    private static bool ReadBoolInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private static string? ReadOptionalInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static IReadOnlyList<string> MergeDistinct(params IEnumerable<string>?[] values)
    {
        var merged = new List<string>();
        foreach (var value in values)
        {
            if (value is null)
                continue;

            merged.AddRange(value.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        return merged
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildModelSummary(AgentLlmCallResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ResponseText))
            return result.ResponseText;

        return result.WasAttempted
            ? "Live model call did not return usable content; deterministic failure-package review remained in force."
            : "No live model response supplied; deterministic failure-package review was used for this governed smoke.";
    }

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return Path.GetFullPath(value);

        throw new InvalidOperationException($"CriticAgent requires input '{key}'.");
    }

    private static IReadOnlyList<string> BuildRisks(JsonElement package, bool evidenceSufficient, bool actionable)
    {
        var risks = new List<string>();
        if (!evidenceSufficient)
            risks.Add("Failure package is missing expected/actual/evidence/repro data.");
        if (!actionable)
            risks.Add("Codex should not patch until likely area and safety rules are present.");
        if (ReadString(package, "Prompt").Contains("memory search", StringComparison.OrdinalIgnoreCase))
            risks.Add("Memory-search failures may reflect retrieval/ranking/test assertion issues; inspect trace before patching.");

        return risks.Count == 0 ? ["No immediate evidence gaps detected."] : risks;
    }

    private static string ReadString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
