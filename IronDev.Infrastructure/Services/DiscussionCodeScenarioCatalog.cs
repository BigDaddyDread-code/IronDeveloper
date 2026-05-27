using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Tools;
using IronDev.Data.Models;
using IronDev.Infrastructure.Tools.CodeStandards;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;
internal sealed record ScenarioDefinition(
    BuildScenario Scenario,
    string Title,
    string Summary,
    string Problem,
    string ProjectDirectory,
    string ProjectFileName,
    string ProgramText,
    string ProjectFileText,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> MatchTerms);

public sealed class DiscussionCodeScenarioCatalog : IBuildScenarioCatalog
{
    public Task<IReadOnlyList<BuildScenario>> GetScenariosAsync(
        int projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BuildScenario>>(All.Select(item => item.Scenario).ToArray());

    internal ScenarioDefinition? Match(string text)
    {
        foreach (var scenario in All)
        {
            if (scenario.MatchTerms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return scenario;
        }

        return null;
    }

    internal ScenarioDefinition? Get(string scenarioId) =>
        All.FirstOrDefault(item => string.Equals(item.Scenario.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    private static readonly IReadOnlyList<ScenarioDefinition> All =
    [
        new ScenarioDefinition(
            Scenario: new BuildScenario
            {
                ScenarioId = "console.hello-world",
                Name = "Hello World console",
                DiscussionText = "Create a tiny C# console application that prints \"Hello from IronDev Alpha\".",
                RuntimeProfileId = "dotnet.console",
                Verifications =
                [
                    new ScenarioVerification
                    {
                        Kind = "StdoutContains",
                        Description = "Output contains expected greeting.",
                        Parameters = new Dictionary<string, string>
                        {
                            ["expected"] = "Hello from IronDev Alpha"
                        }
                    }
                ]
            },
            Title: "Build Hello World Console App",
            Summary: "Create a tiny C# console app that prints \"Hello from IronDev Alpha\".",
            Problem: "The backend discussion-to-code loop needs a real disposable code generation proof.",
            ProjectDirectory: "HelloWorldAlpha",
            ProjectFileName: "HelloWorldAlpha.csproj",
            ProgramText: "Console.WriteLine(\"Hello from IronDev Alpha\");" + Environment.NewLine,
            ProjectFileText: DotNetConsoleProjectFile,
            AcceptanceCriteria:
            [
                "- Disposable C# console project is generated.",
                "- dotnet build succeeds.",
                "- dotnet run prints \"Hello from IronDev Alpha\".",
                "- Real repository is untouched.",
                "- Run events are persisted.",
                "- Result is reviewable."
            ],
            MatchTerms: ["Hello from IronDev Alpha"]),
        new ScenarioDefinition(
            Scenario: new BuildScenario
            {
                ScenarioId = "console.calculator",
                Name = "Calculator console",
                DiscussionText = """
                    Create a C# console calculator that supports add and subtract commands.

                    Examples:
                    calc add 2 3 should print 5
                    calc subtract 10 4 should print 6
                    """,
                RuntimeProfileId = "dotnet.console",
                Verifications =
                [
                    new ScenarioVerification
                    {
                        Kind = "StdoutContains",
                        Description = "Add command prints 5.",
                        Parameters = new Dictionary<string, string>
                        {
                            ["arguments"] = "add 2 3",
                            ["expected"] = "5"
                        }
                    },
                    new ScenarioVerification
                    {
                        Kind = "StdoutContains",
                        Description = "Subtract command prints 6.",
                        Parameters = new Dictionary<string, string>
                        {
                            ["arguments"] = "subtract 10 4",
                            ["expected"] = "6"
                        }
                    }
                ]
            },
            Title: "Build Calculator Console App",
            Summary: "Create a C# console calculator that supports add and subtract commands.",
            Problem: "The reusable chat-to-build spine needs a second deterministic scenario without a new product service.",
            ProjectDirectory: "CalculatorConsole",
            ProjectFileName: "CalculatorConsole.csproj",
            ProgramText: """
                if (args.Length != 3)
                {
                    Console.Error.WriteLine("Usage: calc add|subtract <left> <right>");
                    Environment.Exit(2);
                }

                var command = args[0].ToLowerInvariant();
                var left = int.Parse(args[1]);
                var right = int.Parse(args[2]);

                var result = command switch
                {
                    "add" => left + right,
                    "subtract" => left - right,
                    _ => throw new InvalidOperationException($"Unknown command: {command}")
                };

                Console.WriteLine(result);
                """,
            ProjectFileText: DotNetConsoleProjectFile,
            AcceptanceCriteria:
            [
                "- Disposable C# console project is generated.",
                "- dotnet build succeeds.",
                "- dotnet run -- add 2 3 prints 5.",
                "- dotnet run -- subtract 10 4 prints 6.",
                "- Real repository is untouched.",
                "- Run events are persisted.",
                "- Result is reviewable."
            ],
            MatchTerms: ["calculator", "console"]),
        new ScenarioDefinition(
            Scenario: new BuildScenario
            {
                ScenarioId = "aspnet.health-api",
                Name = "Tiny ASP.NET health API",
                DiscussionText = "Create a minimal ASP.NET Core API with a GET /health endpoint that returns \"healthy\".",
                RuntimeProfileId = "dotnet.web",
                Verifications =
                [
                    new ScenarioVerification
                    {
                        Kind = "HttpGetEquals",
                        Description = "GET /health returns healthy.",
                        Parameters = new Dictionary<string, string>
                        {
                            ["path"] = "/health",
                            ["expected"] = "healthy"
                        }
                    }
                ]
            },
            Title: "Build Tiny ASP.NET Health API",
            Summary: "Create a minimal ASP.NET Core API with a GET /health endpoint that returns \"healthy\".",
            Problem: "The reusable chat-to-build spine needs to prove long-running process handling and HTTP verification.",
            ProjectDirectory: "HealthApi",
            ProjectFileName: "HealthApi.csproj",
            ProgramText: """
                var builder = WebApplication.CreateBuilder(args);
                var app = builder.Build();

                app.MapGet("/health", () => Results.Text("healthy", "text/plain"));

                app.Run();
                """,
            ProjectFileText: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """,
            AcceptanceCriteria:
            [
                "- Generated ASP.NET Core project is created.",
                "- dotnet build succeeds.",
                "- Server starts in the disposable workspace.",
                "- GET /health returns healthy.",
                "- Server process is stopped cleanly.",
                "- Run events and HTTP verification evidence are persisted.",
                "- Result is reviewable."
            ],
            MatchTerms: ["ASP.NET", "health"])
    ];

    private const string DotNetConsoleProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;
}

