using System.Text.Json;
using IronDev.Infrastructure.Services.RunReports;

public static class RunReportViewerSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"RunReportViewer-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var runsRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs");
        Directory.CreateDirectory(runsRoot);

        var validRunId = $"{runId}-valid";
        var missingEvidenceRunId = $"{runId}-missing-evidence";
        var malformedRunId = $"{runId}-malformed";

        await WriteValidFixtureAsync(Path.Combine(runsRoot, validRunId));
        await WriteMissingEvidenceFixtureAsync(Path.Combine(runsRoot, missingEvidenceRunId));
        await WriteMalformedFixtureAsync(Path.Combine(runsRoot, malformedRunId));

        var service = new FileRunReportService(runsRoot);
        var valid = await service.GetRunAsync(validRunId);
        var missingEvidence = await service.GetRunAsync(missingEvidenceRunId);
        var malformed = await service.GetRunAsync(malformedRunId);
        var recent = await service.GetRecentRunsAsync("Solitaire");

        var failures = new List<string>();
        if (valid is null)
            failures.Add("Valid fixture did not load.");
        else
        {
            if (valid.Project != "Solitaire")
                failures.Add($"Expected project Solitaire, actual {valid.Project}.");
            if (valid.Stages.All(stage => stage.AgentName != "BuilderAgent"))
                failures.Add("Expected BuilderAgent stage.");
            if (valid.Stages.All(stage => stage.AgentName != "TesterAgent"))
                failures.Add("Expected TesterAgent stage.");
            if (valid.Attempts.All(attempt => attempt.Type != "Build"))
                failures.Add("Expected build attempt.");
            if (valid.Attempts.All(attempt => attempt.Type != "Test"))
                failures.Add("Expected test attempt.");
            if (valid.Repairs.All(repair => repair.TriggerFailureClassification != "MissingProjectReference"))
                failures.Add("Expected MissingProjectReference repair.");
            if (valid.Repairs.All(repair => repair.TriggerFailureClassification != "RuleBug"))
                failures.Add("Expected RuleBug repair.");
            if (valid.RealRepoMutationCount != 0)
                failures.Add($"Expected real repo mutation count zero, actual {valid.RealRepoMutationCount}.");
            if (valid.Evidence.Count == 0)
                failures.Add("Expected evidence list.");
        }

        if (missingEvidence is null || missingEvidence.Warnings.Count == 0)
            failures.Add("Missing evidence fixture should load with a warning.");
        if (malformed is null || malformed.Status != "Invalid")
            failures.Add("Malformed JSON fixture should load as Invalid.");
        if (recent.All(run => run.RunId != validRunId))
            failures.Add("Recent Solitaire runs did not include valid fixture.");

        var result = new
        {
            Command = "run-report viewer-smoke",
            Status = failures.Count == 0 ? "Succeeded" : "Failed",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = "IronDev",
            Passed = failures.Count == 0,
            Summary = failures.Count == 0
                ? "Run report viewer service loaded valid, missing-evidence, and malformed file-backed run reports without CLI execution."
                : string.Join(" ", failures),
            Data = new
            {
                validRun = valid,
                missingEvidenceRun = missingEvidence,
                malformedRun = malformed,
                recentSolitaireCount = recent.Count
            },
            Evidence = new[]
            {
                new { Type = "RunReportFixture", Path = Path.Combine(runsRoot, validRunId), Summary = "Valid trace-backed repair-loop fixture." },
                new { Type = "RunReportFixture", Path = Path.Combine(runsRoot, missingEvidenceRunId), Summary = "Missing evidence fixture." },
                new { Type = "RunReportFixture", Path = Path.Combine(runsRoot, malformedRunId), Summary = "Malformed JSON fixture." }
            },
            Boundary = "Read/report service smoke only. No CLI process is started by the service, no builder runs execute, and no real repository writes are granted.",
            ReproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- run-report viewer-smoke --run-id {runId} --json"
        };

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task WriteValidFixtureAsync(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, "evidence"));
        await File.WriteAllTextAsync(Path.Combine(directory, "evidence", "build.log"), "build failed then passed");
        await File.WriteAllTextAsync(Path.Combine(directory, "builder-repair-loop-report.json"), """
        {
          "TraceId": "trace-run-report-viewer",
          "Title": "Solitaire Trace-Backed Disposable Repair Loop Report",
          "Project": "Solitaire",
          "Status": "Succeeded",
          "Summary": "Fixture run for service-backed Run Reports viewer.",
          "StageStatuses": [
            { "AgentName": "RetrieverAgent", "StageName": "Context", "Status": "Succeeded", "Summary": "Loaded Solitaire context." },
            { "AgentName": "BuilderAgent", "StageName": "Build", "Status": "Succeeded", "Summary": "Recorded build and repair attempts." },
            { "AgentName": "TesterAgent", "StageName": "Tests", "Status": "Succeeded", "Summary": "Recorded test attempts." }
          ],
          "BuildAttempts": [
            { "AttemptNumber": 1, "Status": "Failed", "FailureClassification": "MissingProjectReference", "Command": "dotnet build" },
            { "AttemptNumber": 3, "Status": "Succeeded", "FailureClassification": "None", "Command": "dotnet build" }
          ],
          "TestAttempts": [
            { "AttemptNumber": 2, "Status": "Failed", "FailureClassification": "RuleBug", "Command": "dotnet test" },
            { "AttemptNumber": 3, "Status": "Succeeded", "FailureClassification": "None", "Command": "dotnet test" }
          ],
          "RepairAttempts": [
            { "RepairAttemptNumber": 1, "TriggerFailureClassification": "MissingProjectReference", "PlannedFix": "Restore project reference.", "Status": "Applied", "RetryBudgetRemaining": 1 },
            { "RepairAttemptNumber": 2, "TriggerFailureClassification": "RuleBug", "PlannedFix": "Restore Klondike rule.", "Status": "Applied", "RetryBudgetRemaining": 0 }
          ],
          "RealRepoMutationCount": 0,
          "DisposableFilesChanged": 17,
          "Recommendation": "PromoteLater",
          "Boundary": "Report only. Does not approve real repo promotion."
        }
        """);
    }

    private static async Task WriteMissingEvidenceFixtureAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "builder-repair-loop-report.json"), """
        {
          "TraceId": "trace-missing-evidence",
          "Title": "Solitaire Missing Evidence Fixture",
          "Project": "Solitaire",
          "Status": "Succeeded",
          "Summary": "Fixture intentionally has no evidence files.",
          "RealRepoMutationCount": 0,
          "DisposableFilesChanged": 0,
          "Recommendation": "Continue"
        }
        """);
    }

    private static async Task WriteMalformedFixtureAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "builder-repair-loop-report.json"), "{ this is not json");
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
