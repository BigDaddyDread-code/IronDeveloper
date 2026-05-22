using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class SupervisorAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly string _repoRoot;

    public SupervisorAgent(AgentDefinition definition, IAgentModelResolver modelResolver, string repoRoot)
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
        var planPath = RequireInput(request, "plan_path");
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"SupervisorAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;

        var memory = await RunDotnetAsync([
            "run",
            "--no-build",
            "--project",
            RunnerProjectPath(),
            "--",
            "agent",
            "retriever",
            "search",
            "--project",
            project,
            "--query",
            query,
            "--take",
            "5",
            "--run-id",
            runId,
            "--json"
        ], ct);

        var tests = memory.ExitCode == 0
            ? await RunDotnetAsync([
                "run",
                "--no-build",
                "--project",
                RunnerProjectPath(),
                "--",
                "agent",
                "tester",
                "run-plan",
                "--plan",
                planPath,
                "--run-id",
                $"{runId}-tester",
                "--json"
            ], ct)
            : CommandRun.Skipped("TesterAgent skipped because RetrieverAgent failed.");

        var memoryJson = TryParse(memory.Stdout);
        var testJson = TryParse(tests.Stdout);
        var memorySucceeded = memory.ExitCode == 0 && ReadString(memoryJson, "status") == "Succeeded";
        var testsSucceeded = tests.ExitCode == 0 && ReadString(testJson, "status") == "Succeeded";
        var status = memorySucceeded && testsSucceeded ? AgentRunStatus.Succeeded : AgentRunStatus.Failed;
        var topMemoryTitle = ReadString(memoryJson, "contextPackage", "Matches", "0", "DocumentTitle");
        var testSummary = ReadString(testJson, "summary");

        var handoff = new
        {
            goalId = request.GoalId,
            dogfoodRunId = runId,
            project,
            query,
            planPath,
            supervisor = new
            {
                agent = AgentName,
                modelProfile = profile.Name,
                provider = profile.Provider,
                model = profile.Model,
                decision = status == AgentRunStatus.Succeeded ? "ready_for_codex_review" : "needs_repair_package"
            },
            memory = new
            {
                succeeded = memorySucceeded,
                topTitle = topMemoryTitle,
                semanticTraceId = ReadString(memoryJson, "contextPackage", "SemanticTraceId"),
                contextPackage = ReadElement(memoryJson, "contextPackage")
            },
            tester = new
            {
                succeeded = testsSucceeded,
                summary = testSummary,
                report = ReadElement(testJson, "report")
            },
            codexHandoff = new
            {
                observedFailure = status == AgentRunStatus.Succeeded ? "" : "Supervisor loop did not complete cleanly.",
                evidence = new[]
                {
                    "RetrieverAgent returned project memory context.",
                    "TesterAgent executed the selected validation plan."
                },
                recommendedNextAction = status == AgentRunStatus.Succeeded
                    ? "Codex may inspect the compact handoff and choose the next scoped improvement."
                    : "Generate a failure package from the failed Test Agent run before patching.",
                boundary = "025 proves memory-to-test orchestration only; it does not change builder behaviour or apply code patches."
            }
        };

        var outputJson = JsonSerializer.Serialize(handoff, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return new AgentResult
        {
            AgentName = AgentName,
            Status = status,
            Summary = status == AgentRunStatus.Succeeded
                ? $"SupervisorAgent retrieved '{topMemoryTitle}' and TesterAgent reported: {testSummary}"
                : "SupervisorAgent loop failed before producing a clean Codex handoff.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = status == AgentRunStatus.Succeeded ? 0 : 1,
            OutputJson = outputJson,
            CommandsRun = [memory.Command, tests.Command],
            EvidencePaths = ExtractEvidencePaths(testJson),
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private string RunnerProjectPath() =>
        Path.Combine(_repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");

    private async Task<CommandRun> RunDotnetAsync(string[] arguments, CancellationToken ct)
    {
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

        return new CommandRun(command, process.ExitCode, stdout, stderr);
    }

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"SupervisorAgent requires input '{key}'.");
    }

    private static JsonElement? TryParse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ReadElement(JsonElement? root, params string[] path)
    {
        if (root is null)
            return null;

        var current = root.Value;
        foreach (var segment in path)
        {
            if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength())
                    return null;
                current = current[index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.Clone();
    }

    private static string ReadString(JsonElement? root, params string[] path)
    {
        var value = ReadElement(root, path);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() ?? string.Empty : string.Empty;
    }

    private static IReadOnlyList<string> ExtractEvidencePaths(JsonElement? testJson)
    {
        var report = ReadElement(testJson, "report");
        var evidence = ReadElement(report, "evidence");
        if (evidence is null || evidence.Value.ValueKind != JsonValueKind.Array)
            return [];

        return evidence.Value.EnumerateArray()
            .Select(item => item.TryGetProperty("path", out var path) ? path.GetString() : null)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private sealed record CommandRun(string Command, int ExitCode, string Stdout, string Stderr)
    {
        public static CommandRun Skipped(string reason) => new(reason, 1, string.Empty, reason);
    }
}
