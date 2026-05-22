using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class RetrieverAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly string _repoRoot;

    public RetrieverAgent(AgentDefinition definition, IAgentModelResolver modelResolver, string repoRoot)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _repoRoot = repoRoot;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var project = RequireInput(request, "project");
        var query = RequireInput(request, "query");
        var take = request.Inputs.TryGetValue("take", out var takeValue) && int.TryParse(takeValue, out var parsedTake)
            ? parsedTake
            : 5;
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"RetrieverAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;

        var runnerProject = Path.Combine(_repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");
        var arguments = new[]
        {
            "run",
            "--no-build",
            "--project",
            runnerProject,
            "--",
            "memory",
            "search",
            query,
            "--project",
            project,
            "--take",
            take.ToString(),
            "--json",
            "--dogfood-run-id",
            runId
        };

        var command = "dotnet " + string.Join(" ", arguments.Select(QuoteIfNeeded));
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var output = string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : stdout + Environment.NewLine + stderr;

        return new AgentResult
        {
            AgentName = AgentName,
            Status = process.ExitCode == 0 ? AgentRunStatus.Succeeded : AgentRunStatus.Failed,
            Summary = ExtractSummary(stdout),
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = process.ExitCode,
            OutputJson = output,
            CommandsRun = [command],
            EvidencePaths = [],
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"RetrieverAgent requires input '{key}'.");
    }

    private static string ExtractSummary(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.TryGetProperty("Matches", out var matches) &&
                matches.ValueKind == JsonValueKind.Array &&
                matches.GetArrayLength() > 0)
            {
                var top = matches[0];
                var title = top.TryGetProperty("DocumentTitle", out var titleElement)
                    ? titleElement.GetString()
                    : "unknown";
                var finalRank = top.TryGetProperty("FinalIronDevRank", out var rankElement)
                    ? rankElement.GetInt32()
                    : 0;

                return $"RetrieverAgent top match '{title}' finalRank={finalRank}.";
            }
        }
        catch (JsonException)
        {
            // Fall through to compact raw-output summary.
        }

        return string.IsNullOrWhiteSpace(stdout)
            ? "RetrieverAgent completed with no stdout."
            : stdout.Trim().Split(Environment.NewLine).FirstOrDefault() ?? "RetrieverAgent completed.";
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
