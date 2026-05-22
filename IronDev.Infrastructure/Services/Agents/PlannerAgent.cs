using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class PlannerAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;

    public PlannerAgent(AgentDefinition definition, IAgentModelResolver modelResolver)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
    }

    public override Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var goal = RequireInput(request, "goal");
        var project = request.Inputs.TryGetValue("project", out var projectValue) && !string.IsNullOrWhiteSpace(projectValue)
            ? projectValue
            : "BookSeller";
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"PlannerAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;
        var safeId = Slug(goal);
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
                }
            }
        };

        var outputJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult(new AgentResult
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
        });
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
}
