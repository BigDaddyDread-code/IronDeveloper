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
    private readonly IAgentLlmClient? _llmClient;
    private readonly IAgentProcessRunner _processRunner;

    public RetrieverAgent(
        AgentDefinition definition,
        IAgentModelResolver modelResolver,
        string repoRoot,
        IAgentLlmClient? llmClient = null,
        IAgentProcessRunner? processRunner = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _repoRoot = repoRoot;
        _llmClient = llmClient;
        _processRunner = processRunner ?? new AgentProcessRunner();
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
        var liveLlmRequested = ReadBoolInput(request, "live_llm");

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

        var (exitCode, stdout, stderr, command) = await RunDotnetAsync(arguments, ct);

        var output = string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : stdout + Environment.NewLine + stderr;
        var prompt = BuildPrompt(project, query, stdout);
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var contextBundle = exitCode == 0
            ? BuildContextBundle(stdout, profile, prompt, liveLlmRequested, llmResult)
            : output;

        return new AgentResult
        {
            AgentName = AgentName,
            Status = exitCode == 0 ? AgentRunStatus.Succeeded : AgentRunStatus.Failed,
            Summary = exitCode == -1
                ? $"RetrieverAgent subprocess timed out after {SubprocessTimeoutSeconds}s."
                : ExtractSummary(stdout),
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = exitCode,
            OutputJson = contextBundle,
            CommandsRun = [command],
            EvidencePaths = [],
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static int SubprocessTimeoutSeconds =>
        int.TryParse(Environment.GetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS"), out var parsed)
            ? parsed
            : 300;

    private async Task<(int ExitCode, string Stdout, string Stderr, string Command)> RunDotnetAsync(
        string[] arguments, CancellationToken ct)
    {
        var result = await _processRunner.RunAsync("dotnet", arguments, _repoRoot, ct);
        return (result.ExitCode, result.Stdout, result.Stderr, result.Command);
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
                ResponseText = "No live model response supplied; deterministic weighted context packaging was used for this governed smoke."
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

    private static string BuildContextBundle(
        string stdout,
        ModelProfile profile,
        string prompt,
        bool liveLlmRequested,
        AgentLlmCallResult llmResult)
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
            root["LlmIntelligence"] = BuildLlmEvidence(profile, prompt, liveLlmRequested, llmResult);

            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return stdout;
        }
    }

    private static JsonObject BuildLlmEvidence(ModelProfile profile, string prompt, bool liveLlmRequested, AgentLlmCallResult result) => new()
    {
        ["modelProfile"] = profile.Name,
        ["profileProvider"] = profile.Provider,
        ["profileModel"] = profile.Model,
        ["prompt"] = prompt,
        ["invocationMode"] = result.InvocationMode,
        ["liveLlmRequested"] = liveLlmRequested,
        ["wasAttempted"] = result.WasAttempted,
        ["wasSuccessful"] = result.WasSuccessful,
        ["durationMs"] = result.DurationMs,
        ["modelSummary"] = BuildModelSummary(result),
        ["error"] = result.WasSuccessful ? string.Empty : result.ErrorMessage,
        ["boundary"] = "Live RetrieverAgent output is advisory evidence only. Deterministic memory search, project filtering, and authority ranking remain authoritative."
    };

    private static string BuildPrompt(string project, string query, string memorySearchJson) =>
        $"""
        You are RetrieverAgent for IronDev/IDA.
        Review this project-scoped memory search evidence for project '{project}' and query '{query}'.
        Return concise JSON with context risks, included-source notes, rejected-source notes, and questions for the next agent.
        Do not change ranking, override accepted memory, cross project boundaries, create tickets, mutate memory, patch files, or approve writes.
        Memory search JSON:
        {memorySearchJson}
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
            ? "Live model call did not return usable content; deterministic weighted context packaging remained in force."
            : "No live model response supplied; deterministic weighted context packaging was used for this governed smoke.";
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
