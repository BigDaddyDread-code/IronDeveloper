using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents;

public static class GovernedActionReviewCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var actionType = ReadOption(args, "--action-type") ?? ReadPositionalText(args, 2);
        if (string.IsNullOrWhiteSpace(actionType))
        {
            Console.Error.WriteLine("Usage: IronDev.ReplayRunner govern review --action-type <action> --observed-project <project> --affected-project <project> --evidence <text> [--requested-tools tools] [--safety-boundary-refs refs] [--run-id id] [--json]");
            return 2;
        }

        var observedProject = ReadOption(args, "--observed-project") ?? string.Empty;
        var affectedProject = ReadOption(args, "--affected-project") ?? string.Empty;
        var evidence = ReadOption(args, "--evidence") ?? string.Empty;
        var requestedTools = ReadOption(args, "--requested-tools") ?? string.Empty;
        var safetyBoundaryRefs = ReadOption(args, "--safety-boundary-refs") ?? string.Empty;
        var memoryAuthorityRefs = ReadOption(args, "--memory-authority-refs") ?? string.Empty;
        var runId = ReadOption(args, "--run-id") ?? $"GovernedActionReview-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        var modelResolver = new AgentModelResolver(AgentModelDefaults.CreateDefaultProfiles());
        var conscience = new ConscienceAgent(
            AgentModelDefaults.CreateDefaultDefinitions().Single(definition => definition.Name == "ConscienceAgent"),
            modelResolver);
        var conscienceResult = await conscience.RunAsync(new AgentRequest
        {
            AgentName = "ConscienceAgent",
            GoalId = "governed-action-review-133",
            DogfoodRunId = runId,
            Inputs = new Dictionary<string, string>
            {
                ["action_type"] = actionType,
                ["observed_project"] = observedProject,
                ["affected_project"] = affectedProject,
                ["evidence"] = evidence,
                ["requested_tools"] = requestedTools,
                ["memory_authority_refs"] = memoryAuthorityRefs,
                ["safety_boundary_refs"] = safetyBoundaryRefs
            }
        });

        var decision = ReadDecision(conscienceResult.OutputJson);
        var thoughtLedger = new ThoughtLedgerService().Explain(
            actionType,
            observedProject,
            affectedProject,
            decision,
            SplitCliList(evidence),
            SplitCliList(safetyBoundaryRefs),
            decision == "NeedsMoreEvidence" ? ["ConscienceAgent requested more evidence."] : [],
            SplitCliList(requestedTools));

        var governedReview = new
        {
            command = "govern review",
            dogfoodRunId = runId,
            actionType,
            decision,
            mutationAllowed = false,
            recommendedDisposition = BuildRecommendedDisposition(decision),
            conscience = JsonDocument.Parse(conscienceResult.OutputJson).RootElement.Clone(),
            thoughtLedger,
            boundary = "GovernedActionReview reviews and explains only. It does not execute actions, patch files, create tickets, or mutate memory."
        };

        Console.WriteLine(JsonSerializer.Serialize(governedReview, options));
        return 0;
    }

    private static string BuildRecommendedDisposition(string decision) =>
        decision switch
        {
            "Allow" => "ProceedWithinBoundary",
            "Block" => "CreateSafetyFindingOrFailurePackage",
            "NeedsMoreEvidence" => "CollectEvidenceAndReviewAgain",
            _ => "NoAction"
        };

    private static string ReadDecision(string outputJson)
    {
        using var document = JsonDocument.Parse(outputJson);
        return document.RootElement.TryGetProperty("decision", out var decision)
            ? decision.GetString() ?? "NeedsMoreEvidence"
            : "NeedsMoreEvidence";
    }

    private static IReadOnlyList<string> SplitCliList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['|', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string? ReadOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return string.Empty;

            return args[i + 1];
        }

        return null;
    }

    private static string ReadPositionalText(string[] args, int startIndex)
    {
        var parts = new List<string>();
        for (var i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                break;

            parts.Add(args[i]);
        }

        return string.Join(' ', parts).Trim();
    }
}
