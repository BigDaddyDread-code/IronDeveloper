using System.Text.Json;

public static class MemoryTriageCommand
{
    public static int Handle(string[] args, JsonSerializerOptions options)
    {
        var message = ReadOption(args, "--message") ?? ReadPositionalText(args, 2);
        if (string.IsNullOrWhiteSpace(message))
        {
            Console.Error.WriteLine("Usage: IronDev.ReplayRunner memory triage <message> [--project BookSeller] [--run-id id] [--json]");
            return 2;
        }

        var project = ReadOption(args, "--project") ?? DetectProject(message) ?? "IronDev";
        var runId = ReadOption(args, "--run-id") ?? $"MemoryTriage-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var result = Classify(message, project, runId);

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return 0;
    }

    private static object Classify(string message, string project, string runId)
    {
        var normalized = message.ToLowerInvariant();
        var explicitProject = DetectProject(message) ?? project;
        var evidence = new List<string>();
        var recommendedArtifacts = new List<string>();

        var asksAboutKnowledge =
            ContainsAny(normalized, "tell me about", "what project knowledge", "what do we know", "show me") &&
            ContainsAny(normalized, "project knowledge", "knowledge", "memory");

        if (asksAboutKnowledge)
        {
            evidence.Add("question_about_existing_memory");
            return NewResult(
                message,
                runId,
                shouldSave: false,
                scope: "None",
                project: explicitProject,
                memoryType: "MemoryQuery",
                authority: "None",
                confidence: 0.89m,
                reason: "The message asks to retrieve or discuss existing memory, not create new memory.",
                recommendedArtifacts: [],
                evidence,
                requiresClarification: false);
        }

        var routeFinding =
            ContainsAny(normalized, "wrong route", "routed wrong", "bad route", "expected", "actual", "generalchat", "savedocument", "save discussion") ||
            (ContainsAny(normalized, "ticket", "prompt", "campaign") && ContainsAny(normalized, "route", "routing", "wrong"));

        if (routeFinding)
        {
            evidence.Add("routing_failure_language");
            recommendedArtifacts.Add("CampaignFinding");
            recommendedArtifacts.Add("BugTicket");
            recommendedArtifacts.Add("DiscussionDocument");

            return NewResult(
                message,
                runId,
                shouldSave: true,
                scope: "Global",
                project: "IronDev",
                memoryType: "RoutingFinding",
                authority: "Proposed",
                confidence: 0.86m,
                reason: "The message describes an observed routing failure that can affect all projects.",
                recommendedArtifacts,
                evidence,
                requiresClarification: false);
        }

        var safetyRule = ContainsAny(normalized, "no real repo writes", "outside disposable", "must not write", "fail closed", "approval gate");
        if (safetyRule)
        {
            evidence.Add("safety_boundary_language");
            recommendedArtifacts.Add("DecisionCandidate");
            recommendedArtifacts.Add("CodeStandardsRule");

            return NewResult(
                message,
                runId,
                shouldSave: true,
                scope: "Global",
                project: "IronDev",
                memoryType: "SafetyDecisionCandidate",
                authority: "Proposed",
                confidence: 0.9m,
                reason: "The message states a safety boundary that should apply across IDA/IronDev runs.",
                recommendedArtifacts,
                evidence,
                requiresClarification: false);
        }

        var saveIntent = ContainsAny(normalized, "save this", "remember this", "store this", "add this to project", "project knowledge", "make this a decision", "save that");
        if (saveIntent)
        {
            evidence.Add("memory_save_language");
            recommendedArtifacts.Add("ProjectDocument");
            recommendedArtifacts.Add("DiscussionDocument");

            if (ContainsAny(normalized, "architecture", "decision", "dapper", "sql server", "database"))
                recommendedArtifacts.Add("DecisionCandidate");

            return NewResult(
                message,
                runId,
                shouldSave: true,
                scope: "Project",
                project: explicitProject,
                memoryType: "ProjectKnowledgeInstruction",
                authority: "Proposed",
                confidence: 0.84m,
                reason: "The message asks IDA to persist project knowledge rather than merely answer in chat.",
                recommendedArtifacts,
                evidence,
                requiresClarification: string.Equals(explicitProject, "IronDev", StringComparison.OrdinalIgnoreCase) && !message.Contains("IronDev", StringComparison.OrdinalIgnoreCase));
        }

        var projectRequirement =
            !string.IsNullOrWhiteSpace(explicitProject) &&
            ContainsAny(normalized, "must", "should", "cannot", "can't", "need", "needs", "use ") &&
            ContainsAny(normalized, "stock", "inventory", "book", "bookseller", "sql server", "dapper", "database", "checkout", "search");

        if (projectRequirement)
        {
            evidence.Add("project_requirement_language");
            recommendedArtifacts.Add("ProjectDocument");
            recommendedArtifacts.Add("Requirement");

            return NewResult(
                message,
                runId,
                shouldSave: true,
                scope: "Project",
                project: explicitProject,
                memoryType: "ProjectRequirement",
                authority: "Proposed",
                confidence: 0.78m,
                reason: "The message appears to define a project-specific product or architecture requirement.",
                recommendedArtifacts,
                evidence,
                requiresClarification: false);
        }

        evidence.Add("no_durable_signal");
        return NewResult(
            message,
            runId,
            shouldSave: false,
            scope: "None",
            project: explicitProject,
            memoryType: "ConversationChatter",
            authority: "None",
            confidence: 0.62m,
            reason: "The message does not contain a clear durable rule, finding, decision, or project requirement.",
            recommendedArtifacts: [],
            evidence,
            requiresClarification: false);
    }

    private static object NewResult(
        string message,
        string runId,
        bool shouldSave,
        string scope,
        string project,
        string memoryType,
        string authority,
        decimal confidence,
        string reason,
        IReadOnlyList<string> recommendedArtifacts,
        IReadOnlyList<string> evidence,
        bool requiresClarification)
    {
        return new
        {
            command = "memory triage",
            dogfoodRunId = runId,
            message,
            shouldSave,
            scope,
            project,
            memoryType,
            authority,
            confidence,
            reason,
            recommendedArtifacts,
            evidence,
            requiresClarification,
            boundary = "Memory triage classifies whether context should become memory; it does not persist documents, create tickets, or apply fixes."
        };
    }

    private static string? DetectProject(string message)
    {
        if (message.Contains("BookSeller", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("book seller", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bookstore", StringComparison.OrdinalIgnoreCase))
            return "BookSeller";

        if (message.Contains("IronDev", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("IDA", StringComparison.OrdinalIgnoreCase))
            return "IronDev";

        return null;
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string ReadPositionalText(string[] args, int startIndex)
    {
        if (args.Length <= startIndex)
            return string.Empty;

        var values = new List<string>();
        for (var i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                break;

            values.Add(args[i]);
        }

        return string.Join(" ", values);
    }
}
