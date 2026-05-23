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
            root["WeightedContextBundle"] = BuildWeightedContextBundle(root, matches);

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

    private static JsonObject BuildWeightedContextBundle(JsonObject root, JsonArray matches)
    {
        var project = root["Project"]?.DeepClone() ?? new JsonObject();
        var query = GetString(root, "Query");
        var traceId = GetString(root, "SemanticTraceId");
        var includedSources = new JsonArray();
        var rejectedSources = new JsonArray();
        var riskNotes = new JsonArray();

        foreach (var node in matches)
        {
            if (node is not JsonObject match)
                continue;

            var title = GetString(match, "DocumentTitle");
            var currentStatus = GetString(match, "CurrentStatus");
            var authority = GetString(match, "AuthorityLevel");
            var finalRank = GetInt(match, "FinalIronDevRank");
            var rawRank = GetInt(match, "RawWeaviateRank");
            var guidance = GetString(match, "Guidance");
            var source = new JsonObject
            {
                ["documentTitle"] = title,
                ["documentId"] = GetString(match, "DocumentId"),
                ["documentVersionId"] = GetString(match, "DocumentVersionId"),
                ["sourceEntityType"] = GetString(match, "SourceEntityType"),
                ["sourceEntityId"] = GetString(match, "SourceEntityId"),
                ["rawVectorRank"] = rawRank,
                ["rawVectorScore"] = GetDouble(match, "RawVectorScore"),
                ["finalAuthorityRank"] = finalRank,
                ["finalAuthorityScore"] = GetDouble(match, "FinalAuthorityScore"),
                ["authorityLevel"] = authority,
                ["currentStatus"] = currentStatus,
                ["semanticTraceId"] = traceId,
                ["whyIncluded"] = BuildIncludedReasons(match),
                ["whyRejected"] = new JsonArray(),
                ["riskNotes"] = BuildSourceRiskNotes(match)
            };

            if (string.Equals(guidance, "treat_as_historical", StringComparison.OrdinalIgnoreCase))
            {
                source["whyRejected"] = new JsonArray("source is not current", "historical context only");
                rejectedSources.Add(source);
                continue;
            }

            if (finalRank > 0 && finalRank <= 3)
            {
                includedSources.Add(source);
            }
            else
            {
                source["whyRejected"] = new JsonArray("outside top authority-ranked context window");
                rejectedSources.Add(source);
            }
        }

        if (includedSources.Count == 0)
            riskNotes.Add("No included accepted current sources were returned; request more evidence before acting.");

        if (rejectedSources.Count == 0)
            riskNotes.Add("No rejected sources were visible in this result set; project filtering may have removed wrong-project candidates before packaging.");

        return new JsonObject
        {
            ["bundleKind"] = "WeightedContextBundle",
            ["project"] = project,
            ["query"] = query,
            ["semanticTraceId"] = traceId,
            ["includedSources"] = includedSources,
            ["rejectedSources"] = rejectedSources,
            ["riskNotes"] = riskNotes,
            ["summaryForAgent"] = BuildWeightedSummary(includedSources, rejectedSources),
            ["boundary"] = "Weighted context is evidence for other agents; RetrieverAgent does not decide implementation or apply changes."
        };
    }

    private static JsonArray BuildIncludedReasons(JsonObject match)
    {
        var reasons = new JsonArray();
        if (string.Equals(GetString(match, "AuthorityLevel"), "Accepted", StringComparison.OrdinalIgnoreCase))
            reasons.Add("accepted project memory");

        if (string.Equals(GetString(match, "CurrentStatus"), "Current", StringComparison.OrdinalIgnoreCase))
            reasons.Add("current source");

        var rawRank = GetInt(match, "RawWeaviateRank");
        var finalRank = GetInt(match, "FinalIronDevRank");
        if (rawRank > 0 && finalRank > 0 && finalRank < rawRank)
            reasons.Add("authority ranking promoted over raw vector rank");
        else if (rawRank > 0)
            reasons.Add("returned by real memory search");

        var reason = GetString(match, "MatchReason");
        if (!string.IsNullOrWhiteSpace(reason))
            reasons.Add(reason);

        return reasons;
    }

    private static JsonArray BuildSourceRiskNotes(JsonObject match)
    {
        var riskNotes = new JsonArray();

        if (!string.Equals(GetString(match, "AuthorityLevel"), "Accepted", StringComparison.OrdinalIgnoreCase))
            riskNotes.Add("source is not accepted authority");

        if (!string.Equals(GetString(match, "CurrentStatus"), "Current", StringComparison.OrdinalIgnoreCase))
            riskNotes.Add("source is stale or historical");

        if (GetInt(match, "RawWeaviateRank") < GetInt(match, "FinalIronDevRank"))
            riskNotes.Add("raw vector rank was stronger than final authority rank; review before using as primary context");

        return riskNotes;
    }

    private static string BuildWeightedSummary(JsonArray includedSources, JsonArray rejectedSources)
    {
        var top = includedSources.OfType<JsonObject>().FirstOrDefault();
        if (top is null)
            return "No safe primary context source was included; request more evidence before planning or building.";

        var title = GetString(top, "documentTitle");
        return $"Use '{title}' as primary weighted context. Included sources={includedSources.Count}; rejected sources={rejectedSources.Count}.";
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

    private static double GetDouble(JsonObject match, string key)
    {
        if (!match.TryGetPropertyValue(key, out var value) || value is null)
            return 0;

        try
        {
            return value.GetValue<double>();
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
