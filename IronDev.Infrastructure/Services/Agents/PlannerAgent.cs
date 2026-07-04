using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class PlannerAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;

    public PlannerAgent(AgentDefinition definition, IAgentModelResolver modelResolver, IAgentLlmClient? llmClient = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        if (request.Inputs.TryGetValue("mode", out var mode) &&
            string.Equals(mode, "product_spike_intake", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildProductSpikeIntakeResultAsync(request, profile, ct);
        }

        var goal = RequireInput(request, "goal");
        var project = RequireInput(request, "project");
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"PlannerAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;
        var safeId = Slug(goal);
        var prompt = BuildDraftPlanPrompt(project, goal);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var plan = new
        {
            test_run_id = $"{runId}-draft",
            goal_id = $"planner-draft-{safeId}",
            project,
            description = $"Draft Test Agent plan for: {goal}",
            max_turns = 3,
            early_stop_on_failure = true,
            steps = new object[]
            {
                new
                {
                    step = 1,
                    action = "memory_search",
                    @params = new
                    {
                        project,
                        query = goal,
                        take = 5,
                        expect_project = project,
                        expect_source_present = true,
                        expect_semantic_trace_id = true,
                        expect_raw_and_final_rank = true
                    }
                },
                new
                {
                    step = 2,
                    action = "agent_quality_run_gate",
                    @params = new
                    {
                        plan_path = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                        expect_model_profile = "cheap-runner",
                        expect_build_succeeded = true,
                        expect_tests_succeeded = true,
                        expect_format_succeeded = true,
                        expect_package_audit_succeeded = true,
                        expect_code_standards_succeeded = true,
                        expect_boundary_contains = "deterministic code standards/toolchain gate"
                    }
                }
            },
            expected = new
            {
                planner_agent_draft = true,
                project_scoped_memory_first = true,
                quality_gate_included = true,
                no_builder_writes = true
            },
            planner = new
            {
                agent = AgentName,
                modelProfile = profile.Name,
                provider = profile.Provider,
                model = profile.Model,
                boundary = "038 drafts Test Agent plan JSON only; it does not execute the plan, change builder behaviour, or patch code.",
                stopConditions = new[]
                {
                    "Stop if project-scoped memory cannot be retrieved.",
                    "Stop if the deterministic quality gate fails.",
                    "Stop before any builder apply/write action."
                },
                llmIntelligence = BuildLlmEvidence(profile, prompt, liveLlmRequested, llmResult)
            }
        };

        var outputJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"PlannerAgent drafted a {project} Test Agent plan for '{goal}'.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = outputJson,
            CommandsRun = [$"planner draft-test-plan --project {project} --goal \"{goal}\""],
            EvidencePaths = [],
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<AgentResult> BuildProductSpikeIntakeResultAsync(AgentRequest request, ModelProfile profile, CancellationToken ct)
    {
        var prompt = RequireInput(request, "prompt");
        var projectInput = request.Inputs.TryGetValue("project", out var projectValue)
            ? projectValue
            : string.Empty;
        var detectedProject = string.IsNullOrWhiteSpace(projectInput)
            ? DetectProjectName(prompt)
            : projectInput.Trim();
        var normalizedPrompt = NormalizePrompt(prompt);
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"PlannerIntake-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;
        var llmPrompt = BuildProductSpikePrompt(prompt, detectedProject, normalizedPrompt);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, llmPrompt, liveLlmRequested, request, ct);

        var intake = new
        {
            intakeKind = "ProductSpikeCandidate",
            originalPrompt = prompt,
            normalizedPrompt,
            detectedProject,
            projectKnown = false,
            confidence = 0.62,
            assumptions = new[] { "The prompt looks like a new product/app spike request, not an instruction to write immediately." },
            clarifyingQuestions = new[]
            {
                "Should this become a new dogfood project fixture?",
                "What platform should the first spike target: WPF, web, console, or another UI?",
                "What is the smallest playable scope that would count as success?",
                "Should IDA create a ProductSpike document and draft ticket before any disposable workspace apply?"
            },
            recommendedNextSteps = new[]
            {
                "CreateProductSpikeDocumentDraft",
                "CreateSpikeTicketDraft",
                "RunRetrieverForProjectMemory",
                "AskConscienceBeforeDisposableApply",
                "UseDisposableWorkspaceOnly"
            },
            blockedActions = new[]
            {
                "DoNotApplyToRealRepo",
                "DoNotCreateFinalTicketsWithoutReview",
                "DoNotMutateProjectMemoryWithoutReviewedArtefact",
                "DoNotStartBuilderApplyWithoutExplicitDisposableWorkspace"
            },
            candidatePlan = new
            {
                project = detectedProject,
                firstSafePlan = "product-spike-intake-only",
                laterPlan = "disposable-workspace-spike-after-human-approval",
                requiresHumanApproval = true,
                realRepoMutationAllowed = false
            },
            planner = new
            {
                agent = AgentName,
                modelProfile = profile.Name,
                provider = profile.Provider,
                model = profile.Model,
                boundary = "137A product spike intake drafts structured next steps only; it does not create project memory, tickets, disposable workspaces, patches, or real repository writes.",
                llmIntelligence = BuildLlmEvidence(profile, llmPrompt, liveLlmRequested, llmResult)
            }
        };

        var outputJson = JsonSerializer.Serialize(intake, new JsonSerializerOptions { WriteIndented = true });
        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"PlannerAgent classified '{prompt}' as a {detectedProject} product spike intake candidate.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = outputJson,
            CommandsRun = [$"planner intake-product-spike --prompt \"{prompt}\""],
            EvidencePaths = [],
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
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
                ResponseText = "No live model response supplied; deterministic planning output was used for this governed smoke."
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

    private static object BuildLlmEvidence(ModelProfile profile, string prompt, bool liveLlmRequested, AgentLlmCallResult result) => new
    {
        modelProfile = profile.Name,
        profileProvider = profile.Provider,
        profileModel = profile.Model,
        prompt,
        invocationMode = result.InvocationMode,
        liveLlmRequested,
        wasAttempted = result.WasAttempted,
        wasSuccessful = result.WasSuccessful,
        durationMs = result.DurationMs,
        modelSummary = BuildModelSummary(result),
        error = result.WasSuccessful ? string.Empty : result.ErrorMessage
    };

    private static string BuildDraftPlanPrompt(string project, string goal) =>
        $"""
        You are PlannerAgent for IronDev/IDA.
        Draft a bounded Test Agent plan for project '{project}' and goal '{goal}'.
        Return concise JSON with planned steps, missing evidence, and stop conditions.
        Do not execute tests, create tickets, mutate memory, patch files, or approve writes.
        """;

    private static string BuildProductSpikePrompt(string prompt, string detectedProject, string normalizedPrompt) =>
        $"""
        You are PlannerAgent for IronDev/IDA.
        Classify this product-spike intake safely.
        Original prompt: {prompt}
        Normalized prompt: {normalizedPrompt}
        Detected project: {detectedProject}
        Return concise JSON with assumptions, clarifying questions, blocked actions, and next safe steps.
        Do not create project memory, create tickets, create a disposable workspace, patch files, or approve writes.
        """;

    private static bool ReadBoolInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private static string BuildModelSummary(AgentLlmCallResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ResponseText))
            return result.ResponseText;

        return result.WasAttempted
            ? "Live model call did not return usable content; deterministic planning output remained in force."
            : "No live model response supplied; deterministic planning output was used for this governed smoke.";
    }

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"PlannerAgent requires input '{key}'.");
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);

        return slug.Length > 48 ? slug[..48].Trim('-') : slug;
    }

    private static string DetectProjectName(string prompt)
    {
        // Look for a likely project name: a capitalised word that isn't a common English word.
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "I", "A", "An", "The", "Please", "Create", "Build", "Make", "Add",
            "New", "Can", "Could", "Would", "Should", "Want", "Need", "Help"
        };
        var words = prompt.Split([' ', '\t', '\r', '\n', ',', '.', '!', '?'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidate = words.FirstOrDefault(word =>
            word.Length >= 2 &&
            char.IsUpper(word[0]) &&
            !commonWords.Contains(word));

        return string.IsNullOrWhiteSpace(candidate) ? "UnspecifiedProductSpike" : candidate;
    }

    private static string NormalizePrompt(string prompt) => prompt.Trim();
}
