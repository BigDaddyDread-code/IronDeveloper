using System.Text.Json;
using System.Text.Json.Serialization;

public static class InventoryValidateCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"InventoryValidate-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var cliPath = Path.Combine(repoRoot, "tools", "dogfood", "cli-command-inventory.json");
        var testPlanPath = Path.Combine(repoRoot, "tools", "dogfood", "test-plan-inventory.json");
        var cliDocPath = Path.Combine(repoRoot, "Docs", "CLI_COMMAND_INVENTORY.md");

        var findings = new List<InventoryValidationFinding>();
        var commands = await ReadJsonArrayAsync<CliCommandInventoryEntry>(cliPath, findings, "cli-command-inventory");
        var plans = await ReadTestPlanInventoryAsync(testPlanPath, findings);
        var docText = File.Exists(cliDocPath) ? await File.ReadAllTextAsync(cliDocPath) : string.Empty;
        if (string.IsNullOrWhiteSpace(docText))
            findings.Add(new("error", "CliInventoryDocMissing", cliDocPath, "CLI command inventory doc is missing or empty."));

        ValidateCliInventory(commands, findings, docText);
        ValidateTestPlanInventory(plans, findings);

        var errors = findings.Count(finding => string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var warnings = findings.Count(finding => string.Equals(finding.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        var traceId = Guid.NewGuid().ToString("N");
        var result = new InventoryValidateResult
        {
            Command = "inventory validate",
            Status = errors == 0 ? "Succeeded" : "Failed",
            RunId = runId,
            TraceId = traceId,
            Project = "IronDev",
            Summary = errors == 0
                ? $"Inventory validation passed with warnings={warnings}."
                : $"Inventory validation failed with errors={errors}, warnings={warnings}.",
            Data = new
            {
                cliCommandCount = commands.Count,
                testPlanCount = plans.Count,
                cleanAliases = new[]
                {
                    "test run-plan",
                    "trace build-smoke",
                    "build disposable repair",
                    "build disposable run",
                    "dogfood build solitaire-disposable-build-smoke",
                    "dogfood build disposable-apply-smoke",
                    "dogfood foundation break-test"
                }
            },
            Evidence = [
                new InventoryEvidence("JsonInventory", cliPath, "CLI command inventory parsed and validated."),
                new InventoryEvidence("JsonInventory", testPlanPath, "Test plan inventory parsed and validated."),
                new InventoryEvidence("Documentation", cliDocPath, "CLI command inventory documentation checked.")
            ],
            Warnings = findings.Where(finding => finding.Severity == "warning").ToList(),
            Errors = findings.Where(finding => finding.Severity == "error").ToList(),
            Boundary = "Inventory validation only. No commands are executed and no project files are mutated beyond this report.",
            ReproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- inventory validate --run-id {runId} --json"
        };

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return errors == 0 ? 0 : 1;
    }

    private static void ValidateCliInventory(
        IReadOnlyList<CliCommandInventoryEntry> commands,
        List<InventoryValidationFinding> findings,
        string docText)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Command))
            {
                findings.Add(new("error", "CommandNameMissing", "cli-command-inventory.json", "A command entry has no command name."));
                continue;
            }

            if (!seen.Add(command.Command))
                findings.Add(new("error", "DuplicateCommand", command.Command, "Command inventory contains a duplicate command."));
            if (string.IsNullOrWhiteSpace(command.Category))
                findings.Add(new("error", "CommandCategoryMissing", command.Command, "Command entry has no category."));
            if (string.IsNullOrWhiteSpace(command.Scope))
                findings.Add(new("warning", "CommandScopeMissing", command.Command, "Command entry has no scope."));
            if (string.IsNullOrWhiteSpace(command.Notes))
                findings.Add(new("warning", "CommandBoundaryMissing", command.Command, "Command entry should include notes or a boundary statement."));
            if (command.Command.Contains("  ", StringComparison.Ordinal))
                findings.Add(new("error", "MalformedCommandSpacing", command.Command, "Command contains repeated spaces."));
        }

        var sorted = commands.OrderBy(command => command.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.Command, StringComparer.OrdinalIgnoreCase)
            .Select(command => command.Command)
            .ToArray();
        var actual = commands.Select(command => command.Command).ToArray();
        if (!actual.SequenceEqual(sorted, StringComparer.OrdinalIgnoreCase))
            findings.Add(new("warning", "CommandInventoryOrder", "cli-command-inventory.json", "Command inventory should be sorted by category then command."));

        foreach (var command in RequiredDocumentedCommands())
        {
            if (!commands.Any(entry => string.Equals(entry.Command, command, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("error", "RequiredCommandMissing", command, "Required command or alias is missing from CLI inventory."));
            if (!docText.Contains(command, StringComparison.OrdinalIgnoreCase))
                findings.Add(new("warning", "CommandMissingFromDocs", command, "Command inventory docs do not mention this command."));
        }
    }

    private static void ValidateTestPlanInventory(
        IReadOnlyList<TestPlanInventoryEntry> plans,
        List<InventoryValidationFinding> findings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            if (string.IsNullOrWhiteSpace(plan.PlanFile))
                findings.Add(new("error", "PlanFileMissing", "test-plan-inventory.json", "A test plan entry has no plan file."));
            else if (!seen.Add(plan.PlanFile))
                findings.Add(new("error", "DuplicatePlan", plan.PlanFile, "Test plan inventory contains a duplicate plan."));
            if (string.IsNullOrWhiteSpace(plan.GoalId))
                findings.Add(new("warning", "PlanGoalMissing", plan.PlanFile ?? "unknown", "Test plan entry has no goal id."));
            if (string.IsNullOrWhiteSpace(plan.SafetyBoundary))
                findings.Add(new("warning", "PlanBoundaryMissing", plan.PlanFile ?? "unknown", "Test plan entry should include a safety boundary."));
        }
    }

    private static IReadOnlyList<string> RequiredDocumentedCommands() =>
    [
        "memory search",
        "test run-plan",
        "agent tester run-plan",
        "trace build-smoke",
        "agent builder trace-smoke",
        "build disposable repair",
        "agent builder repair-loop",
        "dogfood build solitaire-disposable-build-smoke",
        "dogfood build disposable-apply-smoke",
        "dogfood foundation break-test",
        "inventory validate"
    ];

    private static async Task<List<T>> ReadJsonArrayAsync<T>(
        string path,
        List<InventoryValidationFinding> findings,
        string name)
    {
        if (!File.Exists(path))
        {
            findings.Add(new("error", "InventoryFileMissing", path, $"{name} file does not exist."));
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<T>>(await File.ReadAllTextAsync(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return parsed ?? [];
        }
        catch (Exception ex)
        {
            findings.Add(new("error", "InventoryJsonInvalid", path, $"{name} could not parse: {ex.Message}"));
            return [];
        }
    }

    private static async Task<List<TestPlanInventoryEntry>> ReadTestPlanInventoryAsync(
        string path,
        List<InventoryValidationFinding> findings)
    {
        if (!File.Exists(path))
        {
            findings.Add(new("error", "InventoryFileMissing", path, "test-plan-inventory file does not exist."));
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                findings.Add(new("error", "InventoryJsonInvalid", path, "test-plan-inventory root must be an array."));
                return [];
            }

            var plans = new List<TestPlanInventoryEntry>();
            foreach (var element in document.RootElement.EnumerateArray())
                AddTestPlanElements(element, plans);
            return plans;
        }
        catch (Exception ex)
        {
            findings.Add(new("error", "InventoryJsonInvalid", path, $"test-plan-inventory could not parse: {ex.Message}"));
            return [];
        }
    }

    private static void AddTestPlanElements(JsonElement element, List<TestPlanInventoryEntry> plans)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in value.EnumerateArray())
                AddTestPlanElements(nested, plans);
            return;
        }

        plans.Add(new TestPlanInventoryEntry
        {
            PlanFile = ReadString(element, "planFile", "plan_file"),
            GoalId = ReadString(element, "goalId", "goal_id"),
            Project = ReadString(element, "project"),
            AgentPath = ReadString(element, "agentPath", "agent_path"),
            CliCommand = ReadString(element, "command", "cli_command"),
            RegressionGroup = ReadString(element, "regressionGroup", "regression_group"),
            SafetyBoundary = ReadString(element, "safetyBoundary", "safety_boundary"),
            Classification = ReadString(element, "kind", "classification")
        });
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
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

public sealed record InventoryValidateResult
{
    public required string Command { get; init; }
    public required string Status { get; init; }
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required string Summary { get; init; }
    public required object Data { get; init; }
    public IReadOnlyList<InventoryEvidence> Evidence { get; init; } = [];
    public IReadOnlyList<InventoryValidationFinding> Warnings { get; init; } = [];
    public IReadOnlyList<InventoryValidationFinding> Errors { get; init; } = [];
    public required string Boundary { get; init; }
    public required string ReproCommand { get; init; }
}

public sealed record InventoryEvidence(string Type, string Path, string Summary);

public sealed record InventoryValidationFinding(string Severity, string RuleId, string Target, string Message);

public sealed record CliCommandInventoryEntry(
    string Command,
    string Category,
    string Scope,
    string Implementation,
    bool? RequiresSql,
    object? RequiresWeaviate,
    string Status,
    string Notes);

public sealed class TestPlanInventoryEntry
{
    [JsonPropertyName("planFile")]
    public string? PlanFile { get; init; }

    [JsonPropertyName("goalId")]
    public string? GoalId { get; init; }

    [JsonPropertyName("project")]
    public string? Project { get; init; }

    [JsonPropertyName("agentPath")]
    public string? AgentPath { get; init; }

    [JsonPropertyName("command")]
    public string? CliCommand { get; init; }

    [JsonPropertyName("requiredServices")]
    public IReadOnlyList<string>? RequiredServices { get; init; }

    [JsonPropertyName("expectedEvidence")]
    public IReadOnlyList<string>? ExpectedEvidence { get; init; }

    [JsonPropertyName("regressionGroup")]
    public string? RegressionGroup { get; init; }

    [JsonPropertyName("safetyBoundary")]
    public string? SafetyBoundary { get; init; }

    [JsonPropertyName("kind")]
    public string? Classification { get; init; }
}
