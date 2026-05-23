using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public static class AgentModelDefaults
{
    public static IReadOnlyList<ModelProfile> CreateDefaultProfiles() =>
    [
        new()
        {
            Name = "cheap-runner",
            Provider = "OpenAI",
            Model = "gpt-4o-mini",
            Temperature = 0.1,
            MaxOutputTokens = 1200
        },
        new()
        {
            Name = "standard-reasoner",
            Provider = "OpenAI",
            Model = "gpt-4o",
            Temperature = 0.2,
            MaxOutputTokens = 3000
        },
        new()
        {
            Name = "strong-reasoner",
            Provider = "OpenAI",
            Model = "gpt-5.5",
            Temperature = 0.2,
            MaxOutputTokens = 5000
        },
        new()
        {
            Name = "code-builder",
            Provider = "OpenAI",
            Model = "gpt-5.5",
            Temperature = 0.1,
            MaxOutputTokens = 6000
        },
        new()
        {
            Name = "strong-reviewer",
            Provider = "OpenAI",
            Model = "gpt-5.5",
            Temperature = 0.1,
            MaxOutputTokens = 4000
        },
        new()
        {
            Name = "local-cheap-runner",
            Provider = "Ollama",
            Model = "llama3.1",
            BaseUrl = "http://localhost:11434",
            Temperature = 0.1,
            MaxOutputTokens = 1200,
            TimeoutSeconds = 90
        },
        new()
        {
            Name = "local-standard-reasoner",
            Provider = "LocalOpenAI",
            Model = "local-reasoner",
            BaseUrl = "http://localhost:1234/v1",
            Temperature = 0.2,
            MaxOutputTokens = 3000,
            TimeoutSeconds = 90
        }
    ];

    public static IReadOnlyList<AgentDefinition> CreateDefaultDefinitions() =>
    [
        new()
        {
            Name = "SupervisorAgent",
            Purpose = "Coordinate the agent workflow and decide when enough evidence exists.",
            DefaultModelProfile = "strong-reasoner",
            AllowedTools = ["agent.dispatch", "memory.search", "trace.read"]
        },
        new()
        {
            Name = "PlannerAgent",
            Purpose = "Turn vague goals into ordered implementation and validation plans.",
            DefaultModelProfile = "standard-reasoner",
            AllowedTools = ["memory.search", "ticket.read", "document.read"]
        },
        new()
        {
            Name = "ArchitectAgent",
            Purpose = "Protect architecture direction and update project decisions.",
            DefaultModelProfile = "strong-reasoner",
            AllowedTools = ["memory.search", "document.read", "document.write"]
        },
        new()
        {
            Name = "BuilderAgent",
            Purpose = "Create traceable implementation proposals and disposable-workspace build attempts from grounded context.",
            DefaultModelProfile = "code-builder",
            AllowedTools = ["repo.read", "disposable.workspace.write", "build.run", "test.run", "trace.write"]
        },
        new()
        {
            Name = "TesterAgent",
            Purpose = "Execute structured test plans and return compact evidence reports.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["cli.run", "test.plan", "logs.read"]
        },
        new()
        {
            Name = "QualityAgent",
            Purpose = "Run deterministic code quality, format, build, and package checks.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["dotnet.build", "dotnet.test", "dotnet.format", "package.audit"]
        },
        new()
        {
            Name = "RetrieverAgent",
            Purpose = "Select project memory with metadata-aware filtering and ranking.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["memory.search", "memory.trace"]
        },
        new()
        {
            Name = "CriticAgent",
            Purpose = "Challenge assumptions, detect drift, and review deeper risks.",
            DefaultModelProfile = "strong-reviewer",
            AllowedTools = ["memory.search", "trace.read", "repo.read"]
        },
        new()
        {
            Name = "SentinelAgent",
            Purpose = "Observe traces, campaigns, failures, and quality signals and raise evidence-backed insight artefacts.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["trace.read", "failure.read", "test.report.read", "insight.emit"]
        },
        new()
        {
            Name = "ResearchAgent",
            Purpose = "Package explicitly requested external evidence as read-only research without overriding project memory.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["external.evidence.read", "research.package"]
        },
        new()
        {
            Name = "ConscienceAgent",
            Purpose = "Review proposed IronDev/IDA actions against evidence and safety boundaries.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["action.review", "boundary.check", "evidence.check"]
        }
    ];
}
