using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var contextBundle = process.ExitCode == 0
            ? BuildContextBundle(stdout)
            : output;

        return new AgentResult
        {
            AgentName = AgentName,
            Status = process.ExitCode == 0 ? AgentRunStatus.Succeeded : AgentRunStatus.Failed,
            Summary = ExtractSummary(stdout),
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = process.ExitCode,
            OutputJson = contextBundle,
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

    private static string BuildContextBundle(string stdout)
    {
        try
        {
            var root = JsonNode.Parse(stdout)?.AsObject();
            if (root is null)
                return stdout;

            root["BundleKind"] = "RetrieverContextBundle";
            root["Boundary"] = "RetrieverAgent packages memory context only; it does not decide implementation or apply code changes.";

            var matches = root["Matches"]?.AsArray();
            if (matches is null)
                return root.ToJsonString();

            var acceptedSources = new JsonArray();
            var demotedSources = new JsonArray();
            var historicalSources = new JsonArray();

            foreach (var node in matches)
            {
                if (node is not JsonObject match)
                    continue;

                var guidance = BuildGuidance(match);
                match["Guidance"] = guidance;

                var source = new JsonObject
                {
                    ["documentTitle"] = GetString(match, "DocumentTitle"),
                    ["documentId"] = GetString(match, "DocumentId"),
                    ["documentVersionId"] = GetString(match, "DocumentVersionId"),
                    ["sourceEntityType"] = GetString(match, "SourceEntityType"),
                    ["sourceEntityId"] = GetString(match, "SourceEntityId"),
                    ["rawWeaviateRank"] = GetInt(match, "RawWeaviateRank"),
                    ["finalIronDevRank"] = GetInt(match, "FinalIronDevRank"),
                    ["authorityLevel"] = GetString(match, "AuthorityLevel"),
                    ["currentStatus"] = GetString(match, "CurrentStatus"),
                    ["guidance"] = guidance
                };

                if (string.Equals(guidance, "treat_as_historical", StringComparison.OrdinalIgnoreCase))
                {
                    historicalSources.Add(source.DeepClone());
                }
                else if (GetInt(match, "FinalIronDevRank") > GetInt(match, "RawWeaviateRank"))
                {
                    demotedSources.Add(source.DeepClone());
                }
                else
                {
                    acceptedSources.Add(source.DeepClone());
                }
            }

            root["AcceptedSources"] = acceptedSources;
            root["DemotedSources"] = demotedSources;
            root["HistoricalSources"] = historicalSources;
            root["UseGuidance"] = BuildUseGuidance(matches);

            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return stdout;
        }
    }

    private static string BuildGuidance(JsonObject match)
    {
        var finalRank = GetInt(match, "FinalIronDevRank");
        var authority = GetString(match, "AuthorityLevel");
        var currentStatus = GetString(match, "CurrentStatus");

        if (!string.Equals(currentStatus, "Current", StringComparison.OrdinalIgnoreCase))
            return "treat_as_historical";

        if (finalRank == 1 && string.Equals(authority, "Accepted", StringComparison.OrdinalIgnoreCase))
            return "use_this";

        if (string.Equals(authority, "Accepted", StringComparison.OrdinalIgnoreCase))
            return "use_as_supporting_context";

        return "review_before_use";
    }

    private static string BuildUseGuidance(JsonArray matches)
    {
        var top = matches.OfType<JsonObject>().FirstOrDefault();
        if (top is null)
            return "No memory matches returned; request more context before acting.";

        var title = GetString(top, "DocumentTitle");
        var guidance = GetString(top, "Guidance");
        var reason = GetString(top, "MatchReason");

        return $"Top source '{title}' is marked '{guidance}'. {reason}";
    }

    private static string GetString(JsonObject match, string key) =>
        match.TryGetPropertyValue(key, out var value) ? value?.GetValue<string>() ?? string.Empty : string.Empty;

    private static int GetInt(JsonObject match, string key)
    {
        if (!match.TryGetPropertyValue(key, out var value) || value is null)
            return 0;

        try
        {
            return value.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
