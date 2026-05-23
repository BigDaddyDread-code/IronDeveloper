using System.Diagnostics;
using System.Text;
using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class GovernedToolRegistry
{
    private readonly string _repoRoot;
    private readonly string _runnerProject;
    private readonly IReadOnlyList<AgentToolCapability> _capabilities;
    private readonly IReadOnlyList<ProjectRuntimeProfile> _runtimeProfiles;

    public GovernedToolRegistry(string repoRoot)
    {
        _repoRoot = repoRoot;
        _runnerProject = Path.Combine(_repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");
        _capabilities = CreateCapabilities();
        _runtimeProfiles = CreateRuntimeProfiles();
    }

    public IReadOnlyList<AgentToolCapability> ListCapabilities() => _capabilities;

    public IReadOnlyList<ProjectRuntimeProfile> ListRuntimeProfiles() => _runtimeProfiles;

    public bool TryGetCapability(string name, out AgentToolCapability capability)
    {
        capability = _capabilities.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? new AgentToolCapability { Name = name, Description = "Unknown tool capability." };
        return !string.Equals(capability.Description, "Unknown tool capability.", StringComparison.Ordinal);
    }

    public ProjectRuntimeProfile ResolveRuntimeProfile(string project, string runtime)
    {
        return _runtimeProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Project, project, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
            ?? _runtimeProfiles.First(profile => string.Equals(profile.Runtime, runtime, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AgentToolResult> RunAsync(AgentToolRequest request, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;
        if (!TryGetCapability(request.ToolName, out var capability))
        {
            return Blocked(request, started, $"Unknown governed tool capability '{request.ToolName}'.");
        }

        if (request.RequiresMutation || capability.RequiresMutation)
        {
            return Blocked(request, started, $"Tool '{request.ToolName}' requires mutation and is not allowed in the 162-167 reasoning loop.");
        }

        return request.ToolName.ToLowerInvariant() switch
        {
            "memory.search" => await RunMemorySearchAsync(request, capability, started, ct),
            "code.search" => await RunCodeSearchAsync(request, capability, started, ct),
            "trace.read" => RunTraceRead(request, capability, started),
            "failure.latest" => RunFailureLatest(request, capability, started),
            "test.run-plan" => await RunTestPlanAsync(request, capability, started, ct),
            "quality.run-gate" => await RunQualityGateAsync(request, capability, started, ct),
            "project.build" => RunProjectBuildProfile(request, capability, started),
            _ => Blocked(request, started, $"Tool '{request.ToolName}' is registered but has no runner.")
        };
    }

    private async Task<AgentToolResult> RunMemorySearchAsync(
        AgentToolRequest request,
        AgentToolCapability capability,
        DateTimeOffset started,
        CancellationToken ct)
    {
        var query = ReadParameter(request, "query", request.Goal);
        var indexPath = Path.Combine(_repoRoot, "tools", "dogfood", "knowledge", request.Project.ToLowerInvariant(), "knowledge-index.json");
        if (!File.Exists(indexPath) && string.Equals(request.Project, "IronDev", StringComparison.OrdinalIgnoreCase))
            indexPath = Path.Combine(_repoRoot, "tools", "dogfood", "knowledge", "irondev", "knowledge-index.json");

        if (!File.Exists(indexPath))
            return Failed(request, started, $"Knowledge index not found for project {request.Project}.", capability.Boundary);

        var text = await File.ReadAllTextAsync(indexPath, ct);
        var documents = System.Text.Json.JsonSerializer.Deserialize<List<KnowledgeIndexEntry>>(text) ?? [];
        var queryTokens = Tokenize(query);
        var matches = documents
            .Where(doc => string.Equals(doc.Project, request.Project, StringComparison.OrdinalIgnoreCase))
            .Select(doc => new
            {
                doc,
                score = Score($"{doc.Id} {doc.Title} {doc.DocumentType}", queryTokens)
            })
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.doc.Title, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        var summary = matches.Length == 0
            ? $"No project-scoped memory matches found for '{query}'."
            : $"Found {matches.Length} project-scoped memory match(es); top={matches[0].doc.Title}.";

        return Succeeded(
            request,
            started,
            summary,
            new Dictionary<string, string>
            {
                ["query"] = query,
                ["topTitle"] = matches.FirstOrDefault()?.doc.Title ?? string.Empty,
                ["topDocumentId"] = matches.FirstOrDefault()?.doc.Id ?? string.Empty,
                ["matchCount"] = matches.Length.ToString(),
                ["projectScope"] = request.Project
            },
            matches.Select(item => item.doc.Path).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
            capability.Boundary);
    }

    private Task<AgentToolResult> RunCodeSearchAsync(
        AgentToolRequest request,
        AgentToolCapability capability,
        DateTimeOffset started,
        CancellationToken ct)
    {
        var query = ReadParameter(request, "query", request.Goal);
        var tokens = Tokenize(query);
        var files = Directory.EnumerateFiles(_repoRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsSearchableFile)
            .Take(2000)
            .Select(path => new
            {
                path,
                score = Score(Path.GetFileName(path), tokens) + Score(SafeRead(path), tokens)
            })
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.path, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return Task.FromResult(Succeeded(
            request,
            started,
            files.Length == 0 ? $"No code/docs matches found for '{query}'." : $"Found {files.Length} code/docs match(es).",
            new Dictionary<string, string>
            {
                ["query"] = query,
                ["matchCount"] = files.Length.ToString(),
                ["topPath"] = files.FirstOrDefault()?.path ?? string.Empty
            },
            files.Select(item => item.path).ToArray(),
            capability.Boundary));
    }

    private AgentToolResult RunTraceRead(AgentToolRequest request, AgentToolCapability capability, DateTimeOffset started)
    {
        var runsRoot = Path.Combine(_repoRoot, "tools", "dogfood", "runs");
        var reports = Directory.Exists(runsRoot)
            ? Directory.EnumerateFiles(runsRoot, "report.json", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(runsRoot, "build-run-report.json", SearchOption.AllDirectories))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .ToArray()
            : [];

        return Succeeded(
            request,
            started,
            $"Trace reader found {reports.Length} recent report file(s).",
            new Dictionary<string, string>
            {
                ["reportCount"] = reports.Length.ToString(),
                ["latestReport"] = reports.FirstOrDefault() ?? string.Empty
            },
            reports,
            capability.Boundary);
    }

    private AgentToolResult RunFailureLatest(AgentToolRequest request, AgentToolCapability capability, DateTimeOffset started)
    {
        var runsRoot = Path.Combine(_repoRoot, "tools", "dogfood", "runs");
        var failures = Directory.Exists(runsRoot)
            ? Directory.EnumerateFiles(runsRoot, "*failure*.json", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .ToArray()
            : [];

        return Succeeded(
            request,
            started,
            failures.Length == 0 ? "No recent failure package found." : $"Found {failures.Length} recent failure package(s).",
            new Dictionary<string, string>
            {
                ["failureCount"] = failures.Length.ToString(),
                ["latestFailure"] = failures.FirstOrDefault() ?? string.Empty
            },
            failures,
            capability.Boundary);
    }

    private async Task<AgentToolResult> RunTestPlanAsync(
        AgentToolRequest request,
        AgentToolCapability capability,
        DateTimeOffset started,
        CancellationToken ct)
    {
        var planPath = ReadParameter(request, "plan_path", "tools/dogfood/test-agent-plans/irondev-thought-ledger-132.json");
        var runId = ReadParameter(request, "run_id", $"{request.RequestId}-test");
        var run = await RunDotnetAsync([
            "run", "--no-build", "--project", _runnerProject, "--",
            "test", "run-plan",
            "--plan", planPath,
            "--run-id", runId,
            "--json"
        ], ct);

        return ProcessBackedResult(request, capability, started, run, $"Test plan {planPath} exit={run.ExitCode}.");
    }

    private async Task<AgentToolResult> RunQualityGateAsync(
        AgentToolRequest request,
        AgentToolCapability capability,
        DateTimeOffset started,
        CancellationToken ct)
    {
        var planPath = ReadParameter(request, "plan_path", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json");
        var runId = ReadParameter(request, "run_id", $"{request.RequestId}-quality");
        var run = await RunDotnetAsync([
            "run", "--no-build", "--project", _runnerProject, "--",
            "agent", "quality", "run-gate",
            "--plan", planPath,
            "--run-id", runId,
            "--json"
        ], ct);

        return ProcessBackedResult(request, capability, started, run, $"Quality gate {planPath} exit={run.ExitCode}.");
    }

    private AgentToolResult RunProjectBuildProfile(AgentToolRequest request, AgentToolCapability capability, DateTimeOffset started)
    {
        var profile = ResolveRuntimeProfile(request.Project, request.Runtime);
        return Succeeded(
            request,
            started,
            $"Resolved {profile.Runtime} build profile for {request.Project}; execution is not automatic in 162-167.",
            new Dictionary<string, string>
            {
                ["runtime"] = profile.Runtime,
                ["buildCommand"] = profile.BuildCommand,
                ["testCommand"] = profile.TestCommand,
                ["sourceRoot"] = profile.SourceRoot
            },
            [],
            capability.Boundary);
    }

    private AgentToolResult ProcessBackedResult(
        AgentToolRequest request,
        AgentToolCapability capability,
        DateTimeOffset started,
        ProcessRun run,
        string summary)
    {
        var data = new Dictionary<string, string>
        {
            ["exitCode"] = run.ExitCode.ToString(),
            ["stdoutSnippet"] = Truncate(run.Stdout, 800),
            ["stderrSnippet"] = Truncate(run.Stderr, 800)
        };

        return run.ExitCode == 0
            ? Succeeded(request, started, summary, data, [], capability.Boundary)
            : Failed(request, started, summary, capability.Boundary, data);
    }

    private async Task<ProcessRun> RunDotnetAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start dotnet process.");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return new ProcessRun(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static AgentToolResult Succeeded(
        AgentToolRequest request,
        DateTimeOffset started,
        string summary,
        IReadOnlyDictionary<string, string> data,
        IReadOnlyList<string> evidenceRefs,
        string boundary) =>
        new()
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            Status = "Succeeded",
            Summary = summary,
            ExitCode = 0,
            Data = data,
            EvidenceRefs = evidenceRefs,
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = boundary
        };

    private static AgentToolResult Failed(
        AgentToolRequest request,
        DateTimeOffset started,
        string summary,
        string boundary,
        IReadOnlyDictionary<string, string>? data = null) =>
        new()
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            Status = "Failed",
            Summary = summary,
            ExitCode = 1,
            Data = data ?? new Dictionary<string, string>(),
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = boundary
        };

    private static AgentToolResult Blocked(AgentToolRequest request, DateTimeOffset started, string summary) =>
        new()
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            Status = "Blocked",
            Summary = summary,
            ExitCode = 1,
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = "Governed tool runner failed closed."
        };

    private static IReadOnlyList<AgentToolCapability> CreateCapabilities() =>
    [
        new()
        {
            Name = "memory.search",
            Description = "Read project-scoped accepted memory and return ranked evidence.",
            EvidenceKinds = ["MemoryMatch", "DocumentRef"],
            SupportedRuntimes = ["dotnet", "node", "python"],
            Boundary = "Read-only project memory search. Does not mutate memory or override ranking."
        },
        new()
        {
            Name = "code.search",
            Description = "Search repository code/docs for relevant terms.",
            EvidenceKinds = ["CodeRef", "DocumentRef"],
            SupportedRuntimes = ["dotnet", "node", "python"],
            Boundary = "Read-only code/docs search. Does not edit files."
        },
        new()
        {
            Name = "trace.read",
            Description = "Read recent file-backed run reports and traces.",
            EvidenceKinds = ["TraceRef", "ReportRef"],
            SupportedRuntimes = ["dotnet", "node", "python"],
            Boundary = "Read-only trace inspection."
        },
        new()
        {
            Name = "failure.latest",
            Description = "Find recent failure packages.",
            EvidenceKinds = ["FailurePackage"],
            SupportedRuntimes = ["dotnet", "node", "python"],
            Boundary = "Read-only failure package discovery."
        },
        new()
        {
            Name = "test.run-plan",
            Description = "Run an approved Test Agent plan through the C# runner.",
            EvidenceKinds = ["TestReport"],
            SupportedRuntimes = ["dotnet"],
            Boundary = "Executes deterministic test plans only; TesterAgent still does not repair."
        },
        new()
        {
            Name = "quality.run-gate",
            Description = "Run deterministic quality gate evidence.",
            EvidenceKinds = ["QualityReport"],
            SupportedRuntimes = ["dotnet"],
            Boundary = "Runs quality checks and reports; does not refactor or override standards."
        },
        new()
        {
            Name = "project.build",
            Description = "Resolve language/runtime build command from project profile.",
            RequiresHumanApproval = true,
            EvidenceKinds = ["RuntimeProfile"],
            SupportedRuntimes = ["dotnet", "node", "python"],
            Boundary = "162-167 resolves build profile only. Build execution needs an explicit later approval path."
        }
    ];

    private static IReadOnlyList<ProjectRuntimeProfile> CreateRuntimeProfiles() =>
    [
        new()
        {
            Project = "IronDev",
            Runtime = "dotnet",
            SourceRoot = ".",
            BuildCommand = "dotnet build",
            TestCommand = "dotnet test",
            SupportedTools = ["memory.search", "code.search", "trace.read", "failure.latest", "test.run-plan", "quality.run-gate", "project.build"],
            Boundary = ".NET is the first supported runtime adapter, not the only allowed runtime model."
        },
        new()
        {
            Project = "GenericNode",
            Runtime = "node",
            SourceRoot = ".",
            BuildCommand = "npm run build",
            TestCommand = "npm test",
            SupportedTools = ["memory.search", "code.search", "trace.read", "failure.latest", "project.build"],
            Boundary = "Node support is a runtime profile contract in 162-167; execution adapters come later."
        },
        new()
        {
            Project = "GenericPython",
            Runtime = "python",
            SourceRoot = ".",
            BuildCommand = "python -m compileall .",
            TestCommand = "pytest",
            SupportedTools = ["memory.search", "code.search", "trace.read", "failure.latest", "project.build"],
            Boundary = "Python support is a runtime profile contract in 162-167; execution adapters come later."
        }
    ];

    private static string ReadParameter(AgentToolRequest request, string key, string fallback) =>
        request.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static IReadOnlyList<string> Tokenize(string value) =>
        value.Split([' ', '\t', '\r', '\n', '.', ',', '-', '_', '/', '\\', ':', ';', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int Score(string value, IReadOnlyList<string> tokens) =>
        tokens.Count(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool IsSearchableFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/tools/dogfood/runs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".md" or ".json" or ".csproj";
    }

    private static string SafeRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length > 256_000 ? string.Empty : File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ProcessRun(int ExitCode, string Stdout, string Stderr);

    private sealed class KnowledgeIndexEntry
    {
        public string Id { get; init; } = string.Empty;
        public string Project { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string DocumentType { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }
}
