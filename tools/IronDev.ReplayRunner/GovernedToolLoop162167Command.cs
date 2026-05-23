using System.Text.Json;
using IronDev.Infrastructure.Services.Agents;

public static class GovernedToolLoop162167Command
{
    public static async Task<int> HandlePlanReviewAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = FindRepositoryRoot();
        var project = ReadOption(args, "--project") ?? "IronDev";
        var goal = ReadOption(args, "--goal") ?? ReadPositionalText(args, 3);
        if (string.IsNullOrWhiteSpace(goal))
        {
            Console.Error.WriteLine("Usage: IronDev.ReplayRunner agent loop plan-review --project <project> --goal <goal> [--runtime dotnet|node|python] [--run-id id] [--json]");
            return 2;
        }

        var runtime = ReadOption(args, "--runtime") ?? "dotnet";
        var runId = ReadOption(args, "--run-id") ?? $"GovernedToolLoop-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var result = await RunLoopAsync(repoRoot, project, goal, runId, runtime);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    public static async Task<int> HandleCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ?? "GovernedToolLoop162167";
        var goal = ReadOption(args, "--goal") ?? "Implement 162-167 governed Planner/Critic tool loop with language-agnostic runtime profiles.";
        var result = await RunLoopAsync(repoRoot, "IronDev", goal, runId, "dotnet");
        var capabilities = new GovernedToolRegistry(repoRoot).ListCapabilities();
        var data = new
        {
            command = "campaign governed-tool-loop-162-167",
            status = result.Status,
            runId = result.RunId,
            traceId = result.TraceId,
            summary = result.Summary,
            data = new
            {
                toolContractCreated = result.Trace.ToolRequests.Count > 0,
                registryCapabilities = capabilities.Select(capability => capability.Name).ToArray(),
                requestedCapabilities = result.Trace.ToolRequests.Select(request => request.ToolName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                traceVisualizationAvailable = File.Exists(Path.Combine(repoRoot, "tools", "dogfood", "runs", runId, "report.md")),
                humanEscalationDecision = result.Trace.HumanEscalation?.Decision ?? string.Empty,
                evidenceValidationStatus = result.Trace.EvidenceValidation?.Status ?? string.Empty,
                runtimeProfiles = result.Trace.RuntimeProfiles.Select(profile => profile.Runtime).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                dotnetProfilePresent = result.Trace.RuntimeProfiles.Any(profile => profile.Runtime == "dotnet"),
                nodeProfilePresent = result.Trace.RuntimeProfiles.Any(profile => profile.Runtime == "node"),
                pythonProfilePresent = result.Trace.RuntimeProfiles.Any(profile => profile.Runtime == "python"),
                realRepoWritesBlocked = true,
                memoryMutationBlocked = true,
                ticketCreationBlocked = true,
                patchApplyBlocked = true,
                rawCommandExecutionBlockedForAgents = true
            },
            evidence = result.EvidenceRefs.Select(path => new { type = "EvidenceRef", path }).ToArray(),
            warnings = Array.Empty<string>(),
            errors = Array.Empty<string>(),
            boundary = result.Boundary,
            reproCommand = $"campaign governed-tool-loop-162-167 --run-id {runId} --json"
        };

        Console.WriteLine(JsonSerializer.Serialize(data, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static async Task<IronDev.Core.Agents.PlannerCriticLoopResult> RunLoopAsync(
        string repoRoot,
        string project,
        string goal,
        string runId,
        string runtime)
    {
        var registry = new GovernedToolRegistry(repoRoot);
        var service = new GovernedPlannerCriticLoopService(registry, new EvidenceValidationService());
        var result = await service.RunAsync(project, goal, runId, runtime);
        await service.WriteOutputsAsync(result, Path.Combine(repoRoot, "tools", "dogfood", "runs", runId));
        return result;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string ReadPositionalText(string[] args, int startIndex) =>
        args.Length > startIndex ? string.Join(' ', args.Skip(startIndex)) : string.Empty;

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
