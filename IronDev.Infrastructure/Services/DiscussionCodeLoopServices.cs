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

public sealed class DiscussionDocumentService : IDiscussionDocumentService
{
    private readonly IProjectDocumentService _documents;

    public DiscussionDocumentService(IProjectDocumentService documents)
    {
        _documents = documents;
    }

    public async Task<SaveDiscussionResponse> SaveDiscussionAsync(
        int projectId,
        SaveDiscussionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Discussion title is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Discussion content is required.", nameof(request));

        var document = await _documents.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = projectId,
            Title = request.Title.Trim(),
            DocumentType = "Discussion",
            ContentMarkdown = request.Content,
            ChangeSummary = "Captured discussion.",
            CreatedBy = "IronDev"
        }, cancellationToken).ConfigureAwait(false);

        if (!document.CurrentVersionId.HasValue)
            throw new InvalidOperationException("Discussion document was created without a current version.");

        return new SaveDiscussionResponse
        {
            DocumentId = document.Id,
            DocumentVersionId = document.CurrentVersionId.Value
        };
    }
}

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

internal static class DiscussionCodeScenarioCatalog
{
    public static ScenarioDefinition? Match(string text)
    {
        foreach (var scenario in All)
        {
            if (scenario.MatchTerms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return scenario;
        }

        return null;
    }

    public static ScenarioDefinition? Get(string scenarioId) =>
        All.FirstOrDefault(item => string.Equals(item.Scenario.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    public static readonly IReadOnlyList<ScenarioDefinition> All =
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

public sealed class TicketFromDocumentService : ITicketFromDocumentService
{
    private readonly IProjectDocumentService _documents;
    private readonly ITicketService _tickets;

    public TicketFromDocumentService(
        IProjectDocumentService documents,
        ITicketService tickets)
    {
        _documents = documents;
        _tickets = tickets;
    }

    public async Task<CreateTicketFromDocumentResponse?> CreateTicketAsync(
        int projectId,
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var version = await _documents.GetVersionAsync(documentVersionId, cancellationToken).ConfigureAwait(false);
        if (version is null)
            return null;

        var document = await _documents.GetDocumentAsync(version.DocumentId, cancellationToken).ConfigureAwait(false);
        if (document is null || document.ProjectId != projectId)
            return null;

        var scenario = DiscussionCodeScenarioCatalog.Match(version.ContentMarkdown);
        var title = !string.IsNullOrWhiteSpace(request.RequestedTitle)
            ? request.RequestedTitle.Trim()
            : scenario is not null
                ? scenario.Title
                : $"Implement discussion: {document.Title}";

        var ticket = scenario is not null
            ? BuildScenarioTicket(document.ProjectId, documentVersionId, title, scenario)
            : BuildDiscussionTicket(document.ProjectId, documentVersionId, title, version.ContentMarkdown);

        ticket.Id = await _tickets.SaveTicketAsync(ticket, cancellationToken).ConfigureAwait(false);

        await _documents.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = documentVersionId,
            LinkedEntityType = "Ticket",
            LinkedEntityId = ticket.Id,
            LinkType = "GeneratedTicket",
            CreatedBy = "IronDev"
        }, cancellationToken).ConfigureAwait(false);

        return new CreateTicketFromDocumentResponse
        {
            TicketId = ticket.Id,
            SourceDocumentVersionId = documentVersionId
        };
    }

    private static ProjectTicket BuildScenarioTicket(
        int projectId,
        long documentVersionId,
        string title,
        ScenarioDefinition scenario) => new()
    {
        ProjectId = projectId,
        SessionId = Guid.NewGuid(),
        Title = title,
        TicketType = "Scenario",
        Priority = "High",
        Summary = scenario.Summary,
        Problem = scenario.Problem,
        AcceptanceCriteria = string.Join(Environment.NewLine, scenario.AcceptanceCriteria),
        TechnicalNotes = $"Generated deterministically from a discussion document using scenario fixture '{scenario.Scenario.ScenarioId}'.",
        Status = "Draft",
        Content = scenario.Summary,
        IsGenerated = true,
        GenerationNote = $"Source: Discussion document. Deterministic scenario fixture: {scenario.Scenario.ScenarioId}.",
        SourceDocumentVersionId = documentVersionId
    };

    private static ProjectTicket BuildDiscussionTicket(int projectId, long documentVersionId, string title, string content) => new()
    {
        ProjectId = projectId,
        SessionId = Guid.NewGuid(),
        Title = title,
        TicketType = "Discussion",
        Priority = "Medium",
        Summary = content,
        Problem = "Generated from discussion document.",
        AcceptanceCriteria = "- Discussion document is linked as ticket source.",
        TechnicalNotes = "Deterministic discussion ticket.",
        Status = "Draft",
        Content = content,
        IsGenerated = true,
        GenerationNote = "Source: Discussion document.",
        SourceDocumentVersionId = documentVersionId
    };
}

public sealed class TicketReviewService : ITicketReviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ITicketService _tickets;
    private readonly IProjectDocumentService _documents;

    public TicketReviewService(
        ITicketService tickets,
        IProjectDocumentService documents)
    {
        _tickets = tickets;
        _documents = documents;
    }

    public async Task<TicketReviewResult?> ReviewAsync(
        int projectId,
        long ticketId,
        RunTicketReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var result = CreateDeterministicReview(ticket);
        await _documents.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = ticket.ProjectId,
            Title = $"Ticket Review {result.ReviewId} for Ticket {ticket.Id}",
            DocumentType = "TicketReview",
            ContentMarkdown = JsonSerializer.Serialize(result, JsonOptions),
            ChangeSummary = "Captured deterministic ticket review.",
            CreatedBy = "IronDev"
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<TicketReviewResult?> GetReviewAsync(
        int projectId,
        long ticketId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId || string.IsNullOrWhiteSpace(reviewId))
            return null;

        var documents = await _documents.GetDocumentsAsync(new GetProjectDocumentsRequest
        {
            ProjectId = ticket.ProjectId,
            DocumentType = "TicketReview",
            Status = "Active"
        }, cancellationToken).ConfigureAwait(false);

        foreach (var document in documents)
        {
            var version = document.CurrentVersionId.HasValue
                ? await _documents.GetVersionAsync(document.CurrentVersionId.Value, cancellationToken).ConfigureAwait(false)
                : null;
            if (version is null || !version.ContentMarkdown.Contains(reviewId, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var result = JsonSerializer.Deserialize<TicketReviewResult>(version.ContentMarkdown, JsonOptions);
                if (result is not null &&
                    result.TicketId == ticketId &&
                    result.ProjectId == projectId &&
                    string.Equals(result.ReviewId, reviewId, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static TicketReviewResult CreateDeterministicReview(ProjectTicket ticket)
    {
        var reviewId = $"ticket-review-{ticket.Id}-{Guid.NewGuid():N}";
        var scenario = DiscussionCodeScenarioCatalog.Match($"{ticket.Title} {ticket.Summary} {ticket.AcceptanceCriteria}");
        return new TicketReviewResult
        {
            ReviewId = reviewId,
            ProjectId = ticket.ProjectId,
            TicketId = ticket.Id,
            ScenarioId = scenario?.Scenario.ScenarioId ?? "none",
            Contributions =
            [
                new TicketReviewContribution
                {
                    Role = "Plan",
                    Summary = "Use the smallest deterministic code proposal that proves the disposable backend path.",
                    Recommendations = ["Generate only the scenario files and a minimal SDK-style csproj."]
                },
                new TicketReviewContribution
                {
                    Role = "Proposal",
                    Summary = "Create generated files in the backend-owned disposable workspace only.",
                    Concerns = ["No real repository mutation is allowed."],
                    Recommendations = ["Use a code proposal and backend-owned command profile."]
                },
                new TicketReviewContribution
                {
                    Role = "Validation",
                    Summary = "Run dotnet build and dotnet run, then verify the expected output.",
                    Recommendations = ["Persist stdout and stderr logs as evidence."]
                },
                new TicketReviewContribution
                {
                    Role = "Governance",
                    Summary = "Proceed only if the run stays disposable and pauses for human review.",
                    Concerns = ["Do not apply generated code to the real repository."],
                    Recommendations = ["End successful execution in PausedForApproval."]
                }
            ],
            Decision = new TicketReviewDecision
            {
                Proceed = scenario is not null,
                RecommendedNextStep = scenario is not null
                    ? "Start disposable code run from a generated code proposal."
                    : "Refine the ticket before disposable code execution.",
                Guardrails =
                [
                    "Generate code only in the disposable workspace.",
                    "Do not mutate the real repository.",
                    "Use backend-owned dotnet build and run commands.",
                    "Persist evidence and pause for human review."
                ]
            }
        };
    }
}

public sealed class DeterministicCodeProposalGenerator : ICodeProposalGenerator
{
    public Task<CodeProposal> GenerateAsync(
        TicketReviewResult review,
        string expectedOutput,
        CancellationToken cancellationToken = default)
    {
        var scenario = DiscussionCodeScenarioCatalog.Get(review.ScenarioId);
        if (scenario is null)
            throw new InvalidOperationException($"No deterministic code proposal scenario is registered for '{review.ScenarioId}'.");

        var defaultOutput = TryGetFirstExpectedOutput(scenario.Scenario.Verifications);
        var output = string.IsNullOrWhiteSpace(expectedOutput) ? defaultOutput : expectedOutput;
        var programText = string.Equals(output, defaultOutput, StringComparison.Ordinal)
            ? scenario.ProgramText
            : BuildSingleWriteLineProgram(output);
        var verifications = string.IsNullOrWhiteSpace(expectedOutput)
            ? scenario.Scenario.Verifications
            :
            [
                new ScenarioVerification
                {
                    Kind = "StdoutContains",
                    Description = "Output contains requested text.",
                    Parameters = new Dictionary<string, string>
                    {
                        ["expected"] = output
                    }
                }
            ];

        return Task.FromResult(new CodeProposal
        {
            ProposalId = $"code-proposal-{review.TicketId}-{Guid.NewGuid():N}",
            ProjectId = review.ProjectId,
            TicketId = review.TicketId,
            ReviewId = review.ReviewId,
            ScenarioId = review.ScenarioId,
            ExpectedOutput = output,
            Files =
            [
                CreateFile($"{scenario.ProjectDirectory}/Program.cs", programText),
                CreateFile($"{scenario.ProjectDirectory}/{scenario.ProjectFileName}", scenario.ProjectFileText)
            ],
            RunProfile = new CodeRunProfile
            {
                RuntimeProfileId = scenario.Scenario.RuntimeProfileId,
                WorkingDirectory = scenario.ProjectDirectory,
                BuildCommand = "dotnet build --nologo",
                RunCommand = "dotnet run --no-build --nologo"
            },
            Verifications = verifications
        });
    }

    private static string TryGetFirstExpectedOutput(IReadOnlyList<ScenarioVerification> verifications) =>
        verifications
            .Select(item => item.Parameters.TryGetValue("expected", out var expected) ? expected : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static string BuildSingleWriteLineProgram(string expectedOutput)
    {
        var escapedOutput = expectedOutput.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"Console.WriteLine(\"{escapedOutput}\");{Environment.NewLine}";
    }

    private static GeneratedCodeFile CreateFile(string relativePath, string content) => new()
    {
        RelativePath = relativePath,
        Content = content,
        Sha256 = ComputeSha256(content)
    };

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class DisposableCodeRunService : IDisposableCodeRunService
{
    private readonly ITicketService _tickets;
    private readonly ITicketReviewService _reviews;
    private readonly ICodeProposalGenerator _proposalGenerator;
    private readonly IGovernedToolRegistry _tools;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IConfiguration _configuration;

    public DisposableCodeRunService(
        ITicketService tickets,
        ITicketReviewService reviews,
        ICodeProposalGenerator proposalGenerator,
        IGovernedToolRegistry tools,
        IRunStore runs,
        IRunEventStore events,
        IConfiguration configuration)
    {
        _tickets = tickets;
        _reviews = reviews;
        _proposalGenerator = proposalGenerator;
        _tools = tools;
        _runs = runs;
        _events = events;
        _configuration = configuration;
    }

    public async Task<StartDisposableCodeRunResponse?> StartAsync(
        int projectId,
        long ticketId,
        StartDisposableCodeRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var review = await _reviews.GetReviewAsync(projectId, ticketId, request.ReviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
            return null;
        if (!review.Decision.Proceed)
            throw new InvalidOperationException("Ticket review decision does not allow disposable code execution.");

        var proposal = await _proposalGenerator.GenerateAsync(review, request.ExpectedOutput, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(request.ScenarioId) &&
            !string.Equals(proposal.ScenarioId, request.ScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Code proposal scenario does not match the requested scenario.");
        }

        var run = await _runs.CreateAsync(new CreateRunRequest
        {
            ProjectId = ticket.ProjectId,
            TicketId = ticket.Id,
            IsDisposable = true,
            Summary = "Disposable code run created from code proposal."
        }, cancellationToken).ConfigureAwait(false);

        var evidenceRoot = ResolveEvidenceRoot();
        var runEvidenceRoot = Path.Combine(evidenceRoot, run.RunId);
        var evidenceDirectory = Path.Combine(runEvidenceRoot, "evidence");
        Directory.CreateDirectory(evidenceDirectory);

        await WriteEvidenceAsync(evidenceDirectory, "ticket-review.json", JsonSerializer.Serialize(review, JsonOptions), cancellationToken).ConfigureAwait(false);
        await WriteEvidenceAsync(evidenceDirectory, "code-proposal.json", JsonSerializer.Serialize(proposal, JsonOptions), cancellationToken).ConfigureAwait(false);
        await PublishAsync(run.RunId, "RunCreated", "Disposable code run created.", ticket, new Dictionary<string, string>
        {
            ["status"] = RunLifecycleState.Created.ToString(),
            ["currentNode"] = "Created",
            ["proposalId"] = proposal.ProposalId
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = run.RunId,
                State = RunLifecycleState.Running,
                Summary = "Disposable code run started."
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "ReviewLinked", $"Linked ticket review {review.ReviewId}.", ticket, new Dictionary<string, string>
            {
                ["reviewId"] = review.ReviewId,
                ["status"] = RunLifecycleState.Running.ToString(),
                ["currentNode"] = "ReviewLinked"
            }, cancellationToken).ConfigureAwait(false);

            var workspacePath = ResolveWorkspacePath(run.RunId);
            ValidateWorkspacePath(workspacePath);
            if (Directory.Exists(workspacePath))
                Directory.Delete(workspacePath, recursive: true);

            await PublishAsync(run.RunId, "WorkspacePreparing", "Preparing disposable code workspace.", ticket, new Dictionary<string, string>
            {
                ["workspacePath"] = workspacePath,
                ["currentNode"] = "WorkspacePreparing"
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "CodeGenerationStarted", "Materialising generated files from code proposal.", ticket, new Dictionary<string, string>
            {
                ["proposalId"] = proposal.ProposalId,
                ["currentNode"] = "CodeGeneration"
            }, cancellationToken).ConfigureAwait(false);

            var materializedFiles = await MaterializeProposalAsync(workspacePath, proposal, evidenceDirectory, cancellationToken).ConfigureAwait(false);
            var projectPath = Path.Combine(workspacePath, proposal.RunProfile.WorkingDirectory);

            await PublishAsync(run.RunId, "WorkspaceReady", "Disposable code workspace is ready.", ticket, new Dictionary<string, string>
            {
                ["workspacePath"] = workspacePath,
                ["projectPath"] = projectPath,
                ["currentNode"] = "WorkspaceReady"
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(run.RunId, "CodeGenerationCompleted", "Generated files were written to the disposable workspace.", ticket, new Dictionary<string, string>
            {
                ["fileCount"] = proposal.Files.Count.ToString(),
                ["currentNode"] = "CodeGeneration"
            }, cancellationToken).ConfigureAwait(false);

            var build = await RunDotNetCommandAsync(run.RunId, ticket, projectPath, "dotnet build", ["build", "--nologo"], evidenceDirectory, cancellationToken).ConfigureAwait(false);
            if (build.ExitCode != 0)
                throw new InvalidOperationException("dotnet build failed.");

            var verificationResults = await RunScenarioVerificationsAsync(
                run.RunId,
                ticket,
                proposal,
                projectPath,
                evidenceDirectory,
                cancellationToken).ConfigureAwait(false);
            await WriteEvidenceAsync(evidenceDirectory, "output-verification.json", JsonSerializer.Serialize(verificationResults, JsonOptions), cancellationToken).ConfigureAwait(false);

            var failedVerification = verificationResults.FirstOrDefault(item => !item.Verified);
            if (failedVerification is not null)
                throw new InvalidOperationException($"Scenario verification failed: {failedVerification.Description}");

            await RunCodeStandardsAsync(run.RunId, ticket, proposal, materializedFiles, evidenceDirectory, cancellationToken).ConfigureAwait(false);

            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = run.RunId,
                State = RunLifecycleState.PausedForApproval,
                Summary = "Disposable code run is ready for human review.",
                WorkspacePath = workspacePath
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(run.RunId, "RunPausedForApproval", "Disposable code run paused for human review.", ticket, new Dictionary<string, string>
            {
                ["status"] = RunLifecycleState.PausedForApproval.ToString(),
                ["currentNode"] = "HumanReview"
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = run.RunId,
                State = RunLifecycleState.Failed,
                Summary = "Disposable code run failed.",
                FailureReason = ex.Message
            }, CancellationToken.None).ConfigureAwait(false);
            await PublishAsync(run.RunId, "RunFailed", ex.Message, ticket, new Dictionary<string, string>
            {
                ["status"] = RunLifecycleState.Failed.ToString(),
                ["failureReason"] = ex.Message,
                ["currentNode"] = "Failed"
            }, CancellationToken.None).ConfigureAwait(false);
        }

        var updated = await _runs.GetAsync(run.RunId, cancellationToken).ConfigureAwait(false) ?? run;
        return new StartDisposableCodeRunResponse
        {
            RunId = updated.RunId,
            State = updated.State.ToString(),
            IsDisposable = updated.IsDisposable
        };
    }

    private static async Task<IReadOnlyList<GeneratedCodeFile>> MaterializeProposalAsync(
        string workspacePath,
        CodeProposal proposal,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workspacePath);
        foreach (var file in proposal.Files)
        {
            var targetPath = Path.GetFullPath(Path.Combine(workspacePath, file.RelativePath));
            var relative = Path.GetRelativePath(workspacePath, targetPath);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidOperationException("Generated file path escaped the disposable workspace.");

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, file.Content, cancellationToken).ConfigureAwait(false);
            var evidenceName = file.RelativePath.Replace('/', '-').Replace('\\', '-');
            await WriteEvidenceAsync(evidenceDirectory, evidenceName, file.Content, cancellationToken).ConfigureAwait(false);
        }

        return proposal.Files;
    }

    private async Task<IReadOnlyList<ScenarioVerificationCapture>> RunScenarioVerificationsAsync(
        string runId,
        ProjectTicket ticket,
        CodeProposal proposal,
        string projectPath,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        var verifications = proposal.Verifications.Count > 0
            ? proposal.Verifications
            :
            [
                new ScenarioVerification
                {
                    Kind = "StdoutContains",
                    Description = "Output contains expected text.",
                    Parameters = new Dictionary<string, string>
                    {
                        ["expected"] = proposal.ExpectedOutput
                    }
                }
            ];

        var results = new List<ScenarioVerificationCapture>();
        foreach (var verification in verifications)
        {
            var result = verification.Kind switch
            {
                "StdoutContains" => await RunStdoutContainsVerificationAsync(runId, ticket, projectPath, verification, evidenceDirectory, cancellationToken).ConfigureAwait(false),
                "CommandExitZero" => await RunCommandExitZeroVerificationAsync(runId, ticket, projectPath, verification, evidenceDirectory, cancellationToken).ConfigureAwait(false),
                "HttpGetEquals" => await RunHttpGetEqualsVerificationAsync(runId, ticket, projectPath, verification, evidenceDirectory, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported scenario verification kind: {verification.Kind}.")
            };

            results.Add(result);
            await PublishAsync(runId, result.Verified ? "OutputVerified" : "OutputVerificationFailed", result.Description, ticket, new Dictionary<string, string>
            {
                ["verificationKind"] = result.Kind,
                ["expectedOutput"] = result.Expected,
                ["verified"] = result.Verified.ToString().ToLowerInvariant(),
                ["currentNode"] = "OutputVerification"
            }, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private async Task<ScenarioVerificationCapture> RunStdoutContainsVerificationAsync(
        string runId,
        ProjectTicket ticket,
        string projectPath,
        ScenarioVerification verification,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        var expected = RequiredParameter(verification, "expected");
        var commandArguments = SplitArguments(OptionalParameter(verification, "arguments"));
        var arguments = new List<string> { "run", "--no-build" };
        if (commandArguments.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(commandArguments);
        }

        var displayName = commandArguments.Count == 0
            ? "dotnet run"
            : $"dotnet run -- {string.Join(' ', commandArguments)}";
        var runCommand = await RunDotNetCommandAsync(runId, ticket, projectPath, displayName, arguments, evidenceDirectory, cancellationToken).ConfigureAwait(false);
        var verified = runCommand.ExitCode == 0 &&
                       runCommand.StandardOutput.Contains(expected, StringComparison.Ordinal);
        return new ScenarioVerificationCapture(
            verification.Kind,
            verification.Description,
            expected,
            runCommand.StandardOutput,
            verified,
            runCommand.ExitCode);
    }

    private async Task<ScenarioVerificationCapture> RunCommandExitZeroVerificationAsync(
        string runId,
        ProjectTicket ticket,
        string projectPath,
        ScenarioVerification verification,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        var commandArguments = SplitArguments(OptionalParameter(verification, "arguments"));
        var arguments = new List<string> { "run", "--no-build" };
        if (commandArguments.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(commandArguments);
        }

        var displayName = commandArguments.Count == 0
            ? "dotnet run"
            : $"dotnet run -- {string.Join(' ', commandArguments)}";
        var runCommand = await RunDotNetCommandAsync(runId, ticket, projectPath, displayName, arguments, evidenceDirectory, cancellationToken).ConfigureAwait(false);
        return new ScenarioVerificationCapture(
            verification.Kind,
            verification.Description,
            "exit code 0",
            runCommand.StandardOutput,
            runCommand.ExitCode == 0,
            runCommand.ExitCode);
    }

    private async Task<ScenarioVerificationCapture> RunHttpGetEqualsVerificationAsync(
        string runId,
        ProjectTicket ticket,
        string projectPath,
        ScenarioVerification verification,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        var path = RequiredParameter(verification, "path");
        var expected = RequiredParameter(verification, "expected");
        var port = GetFreeTcpPort();
        var url = $"http://127.0.0.1:{port}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSeconds("WebTimeoutSeconds", 30)));

        await PublishAsync(runId, "CommandStarted", $"CommandStarted: dotnet run web {url}", ticket, new Dictionary<string, string>
        {
            ["command"] = "dotnet run web",
            ["fileName"] = "dotnet",
            ["url"] = url,
            ["currentNode"] = "HttpVerification"
        }, cancellationToken).ConfigureAwait(false);

        var started = DateTimeOffset.UtcNow;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = projectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in new[] { "run", "--no-build", "--no-launch-profile", "--urls", url })
            process.StartInfo.ArgumentList.Add(argument);

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        string actual = string.Empty;
        var verified = false;
        var exitCode = -1;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var target = new Uri(new Uri(url), path);
            for (var attempt = 0; attempt < 30 && !timeout.IsCancellationRequested; attempt++)
            {
                try
                {
                    actual = await http.GetStringAsync(target, timeout.Token).ConfigureAwait(false);
                    verified = string.Equals(actual.Trim(), expected, StringComparison.Ordinal);
                    if (verified)
                        break;
                }
                catch when (!timeout.IsCancellationRequested)
                {
                    await Task.Delay(500, timeout.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
            catch
            {
            }
        }

        var completed = DateTimeOffset.UtcNow;
        var stdoutPath = await WriteEvidenceAsync(evidenceDirectory, "dotnet-run-web.stdout.log", stdout.ToString(), CancellationToken.None).ConfigureAwait(false);
        var stderrPath = await WriteEvidenceAsync(evidenceDirectory, "dotnet-run-web.stderr.log", stderr.ToString(), CancellationToken.None).ConfigureAwait(false);
        await WriteEvidenceAsync(evidenceDirectory, "http-verification.json", JsonSerializer.Serialize(new
        {
            url = $"{url}{path}",
            expected,
            actual,
            verified
        }, JsonOptions), CancellationToken.None).ConfigureAwait(false);

        await PublishAsync(runId, "CommandCompleted", $"CommandCompleted: dotnet run web verification stopped with code {exitCode}.", ticket, new Dictionary<string, string>
        {
            ["command"] = "dotnet run web",
            ["fileName"] = "dotnet",
            ["exitCode"] = exitCode.ToString(),
            ["durationMs"] = ((long)(completed - started).TotalMilliseconds).ToString(),
            ["stdoutPath"] = stdoutPath,
            ["stderrPath"] = stderrPath,
            ["currentNode"] = "HttpVerification"
        }, CancellationToken.None).ConfigureAwait(false);

        return new ScenarioVerificationCapture(
            verification.Kind,
            verification.Description,
            expected,
            actual,
            verified,
            exitCode);
    }

    private async Task RunCodeStandardsAsync(
        string runId,
        ProjectTicket ticket,
        CodeProposal proposal,
        IReadOnlyList<GeneratedCodeFile> generatedFiles,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        await PublishAsync(runId, "CodeStandardsStarted", "Running governed code standards analysis.", ticket, new Dictionary<string, string>
        {
            ["toolName"] = CodeStandardsAnalysisTool.ToolName,
            ["currentNode"] = "CodeStandards"
        }, cancellationToken).ConfigureAwait(false);

        var patchText = string.Join(Environment.NewLine, generatedFiles.Select(file =>
            $"+++ {file.RelativePath}{Environment.NewLine}{file.Content}"));
        var result = await _tools.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            new GovernedToolRequest<CodeStandardsAnalysisInput>
            {
                RequestId = $"disposable-code-standards-{runId}",
                ToolName = CodeStandardsAnalysisTool.ToolName,
                RequestedBy = "BuilderAgent",
                Input = new CodeStandardsAnalysisInput
                {
                    PatchText = patchText,
                    ChangedFiles = generatedFiles.Select(file => new CodeStandardsChangedFile
                    {
                        Path = file.RelativePath,
                        Content = file.Content
                    }).ToArray()
                },
                Reason = "Run read-only code standards check over generated disposable code proposal files."
            },
            cancellationToken).ConfigureAwait(false);

        var evidencePath = await WriteEvidenceAsync(evidenceDirectory, "code-standards.json", JsonSerializer.Serialize(result, JsonOptions), cancellationToken).ConfigureAwait(false);
        await PublishAsync(runId, "CodeStandardsCompleted", result.Summary, ticket, new Dictionary<string, string>
        {
            ["status"] = result.Status.ToString(),
            ["toolName"] = CodeStandardsAnalysisTool.ToolName,
            ["evidencePath"] = evidencePath,
            ["currentNode"] = "CodeStandards"
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandCapture> RunDotNetCommandAsync(
        string runId,
        ProjectTicket ticket,
        string workingDirectory,
        string displayName,
        IReadOnlyList<string> arguments,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        await PublishAsync(runId, "CommandStarted", $"CommandStarted: {displayName}", ticket, new Dictionary<string, string>
        {
            ["command"] = displayName,
            ["fileName"] = "dotnet",
            ["currentNode"] = displayName
        }, cancellationToken).ConfigureAwait(false);

        var started = DateTimeOffset.UtcNow;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSeconds("TimeoutSeconds", 120)));

        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var completed = DateTimeOffset.UtcNow;
        var exitCode = timedOut ? -1 : process.ExitCode;
        var safeName = displayName.Replace(" ", "-", StringComparison.OrdinalIgnoreCase);
        var stdoutPath = await WriteEvidenceAsync(evidenceDirectory, $"{safeName}.stdout.log", stdout.ToString(), CancellationToken.None).ConfigureAwait(false);
        var stderrText = stderr.ToString() + (timedOut ? $"{Environment.NewLine}Command timed out." : string.Empty);
        var stderrPath = await WriteEvidenceAsync(evidenceDirectory, $"{safeName}.stderr.log", stderrText, CancellationToken.None).ConfigureAwait(false);

        await PublishAsync(runId, "CommandCompleted", $"CommandCompleted: {displayName} exited with code {exitCode}.", ticket, new Dictionary<string, string>
        {
            ["command"] = displayName,
            ["fileName"] = "dotnet",
            ["exitCode"] = exitCode.ToString(),
            ["durationMs"] = ((long)(completed - started).TotalMilliseconds).ToString(),
            ["timedOut"] = timedOut.ToString().ToLowerInvariant(),
            ["stdoutPath"] = stdoutPath,
            ["stderrPath"] = stderrPath,
            ["currentNode"] = displayName
        }, CancellationToken.None).ConfigureAwait(false);

        return new CommandCapture(exitCode, stdout.ToString(), stderrText);
    }

    private static string RequiredParameter(ScenarioVerification verification, string name)
    {
        var value = OptionalParameter(verification, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Scenario verification '{verification.Description}' is missing required parameter '{name}'.");

        return value;
    }

    private static string? OptionalParameter(ScenarioVerification verification, string name) =>
        verification.Parameters.TryGetValue(name, out var value) ? value : null;

    private static IReadOnlyList<string> SplitArguments(string? arguments) =>
        string.IsNullOrWhiteSpace(arguments)
            ? []
            : arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private string ResolveWorkspacePath(string runId)
    {
        var configured = _configuration["DisposableCodeRun:WorkspaceRoot"] ??
                         _configuration["AlphaDisposableCode:WorkspaceRoot"] ??
                         _configuration["LocalTest:WorkspaceRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableCodeWorkspaces")
            : configured;
        return Path.Combine(root, "runs", runId);
    }

    private string ResolveEvidenceRoot()
    {
        var configured = _configuration["DisposableCodeRun:EvidenceRoot"] ??
                         _configuration["AlphaDisposableCode:EvidenceRoot"] ??
                         _configuration["DisposableBuild:EvidenceRoot"] ??
                         _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "runs");
    }

    private void ValidateWorkspacePath(string workspacePath)
    {
        var configured = _configuration["DisposableCodeRun:WorkspaceRoot"] ??
                         _configuration["AlphaDisposableCode:WorkspaceRoot"] ??
                         _configuration["LocalTest:WorkspaceRoot"];
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableCodeWorkspaces")
            : configured);
        var fullWorkspace = Path.GetFullPath(workspacePath);
        var relative = Path.GetRelativePath(root, fullWorkspace);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Disposable workspace path escaped the configured workspace root.");
    }

    private int ReadTimeoutSeconds(string key, int fallback)
    {
        var value = _configuration[$"DisposableCodeRun:{key}"] ?? _configuration[$"AlphaDisposableCode:{key}"];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static async Task<string> WriteEvidenceAsync(
        string evidenceDirectory,
        string fileName,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(evidenceDirectory);
        var path = Path.Combine(evidenceDirectory, fileName);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private Task PublishAsync(
        string runId,
        string eventType,
        string message,
        ProjectTicket ticket,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, string>(payload, StringComparer.OrdinalIgnoreCase)
        {
            ["projectId"] = ticket.ProjectId.ToString(),
            ["ticketId"] = ticket.Id.ToString(),
            ["disposableRun"] = "true",
            ["discussionToCodeLoop"] = "true"
        };

        return _events.PublishAsync(new RunEventDto
        {
            RunId = runId,
            EventType = eventType,
            Message = message,
            Payload = merged
        }, cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record CommandCapture(int ExitCode, string StandardOutput, string StandardError);

    private sealed record ScenarioVerificationCapture(
        string Kind,
        string Description,
        string Expected,
        string Actual,
        bool Verified,
        int ExitCode);
}

public sealed class RunReviewPackageService : IRunReviewPackageService
{
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IRunEvidenceService _evidence;

    public RunReviewPackageService(
        IRunStore runs,
        IRunEventStore events,
        IRunEvidenceService evidence)
    {
        _runs = runs;
        _events = events;
        _evidence = evidence;
    }

    public async Task<RunReviewPackage?> GetReviewPackageAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var evidence = await _evidence.GetEvidenceAsync(runId, cancellationToken).ConfigureAwait(false);
        var generatedFiles = await LoadGeneratedFilesAsync(runId, evidence, cancellationToken).ConfigureAwait(false);
        var commandEvidence = events
            .Where(item => item.EventType == "CommandCompleted")
            .Select(item => new CommandEvidence
            {
                Command = TryPayload(item, "command") ?? "unknown",
                ExitCode = TryPayload(item, "exitCode"),
                StdoutPath = TryPayload(item, "stdoutPath"),
                StderrPath = TryPayload(item, "stderrPath"),
                DurationMs = TryPayload(item, "durationMs")
            })
            .ToArray();

        var outputVerifications = await LoadOutputVerificationsAsync(runId, evidence, cancellationToken).ConfigureAwait(false);
        var outputVerification = outputVerifications.FirstOrDefault()
                                 ?? new OutputVerificationEvidence { Expected = string.Empty, Verified = false };
        var codeStandards = await LoadCodeStandardsAsync(runId, evidence, cancellationToken).ConfigureAwait(false);

        return new RunReviewPackage
        {
            RunId = run.RunId,
            ProjectId = projectId,
            TicketId = ticketId,
            State = run.State.ToString(),
            GeneratedFiles = generatedFiles,
            CommandEvidence = commandEvidence,
            OutputVerification = outputVerification,
            OutputVerifications = outputVerifications,
            CodeStandards = codeStandards,
            FileSetHash = ComputeFileSetHash(generatedFiles),
            Risks = BuildRisks(run, codeStandards, outputVerifications),
            HumanReviewChecklist =
            [
                "Inspect generated files and file hashes.",
                "Confirm dotnet build and dotnet run command evidence.",
                "Confirm output verification matches the ticket acceptance criteria.",
                "Confirm no generated code was applied to the real repository.",
                "Approve or reject the exact package before any future apply step."
            ],
            Events = events.Select(item => new RunEventSummary
            {
                EventType = item.EventType,
                Message = item.Message,
                TimestampUtc = item.TimestampUtc
            }).ToArray()
        };
    }

    private static async Task<IReadOnlyList<GeneratedCodeFile>> LoadGeneratedFilesAsync(
        string runId,
        IReadOnlyList<RunEvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var generated = new List<GeneratedCodeFile>();
        foreach (var item in evidence.Where(item =>
                     item.Path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) ||
                     item.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var content = await File.ReadAllTextAsync(item.Path, cancellationToken).ConfigureAwait(false);
            generated.Add(new GeneratedCodeFile
            {
                RelativePath = Path.GetFileName(item.Path),
                Content = content,
                Sha256 = ComputeSha256(content)
            });
        }

        return generated;
    }

    private async Task<IReadOnlyList<OutputVerificationEvidence>> LoadOutputVerificationsAsync(
        string runId,
        IReadOnlyList<RunEvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var item = evidence.FirstOrDefault(item => item.Path.EndsWith("output-verification.json", StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return [];

        var text = await _evidence.ReadEvidenceTextAsync(runId, item.Path, cancellationToken).ConfigureAwait(false)
                   ?? await File.ReadAllTextAsync(item.Path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(element => MapOutputVerification(element, item.Path)).ToArray();
        }

        return [MapOutputVerification(root, item.Path)];
    }

    private static OutputVerificationEvidence MapOutputVerification(JsonElement root, string evidencePath) => new()
    {
        Expected = root.TryGetProperty("expected", out var expected) ? expected.GetString() ?? string.Empty : string.Empty,
        Actual = root.TryGetProperty("actual", out var actual) ? actual.GetString() ?? string.Empty : string.Empty,
        Verified = root.TryGetProperty("verified", out var verified) && verified.GetBoolean(),
        EvidencePath = evidencePath
    };

    private async Task<CodeStandardsEvidence> LoadCodeStandardsAsync(
        string runId,
        IReadOnlyList<RunEvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var item = evidence.FirstOrDefault(item => item.Path.EndsWith("code-standards.json", StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return new CodeStandardsEvidence();

        var text = await _evidence.ReadEvidenceTextAsync(runId, item.Path, cancellationToken).ConfigureAwait(false)
                   ?? await File.ReadAllTextAsync(item.Path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return new CodeStandardsEvidence
        {
            Status = root.TryGetProperty("status", out var status) ? status.ToString() : "Unknown",
            Summary = root.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : "Code standards result was captured.",
            EvidencePath = item.Path
        };
    }

    private static IReadOnlyList<string> BuildRisks(
        RunRecord run,
        CodeStandardsEvidence codeStandards,
        IReadOnlyList<OutputVerificationEvidence> outputVerifications)
    {
        var risks = new List<string>
        {
            "This is a deterministic scenario fixture running through the reusable code proposal pipeline.",
            "Generated code remains in a disposable workspace until a separate human-approved apply path exists."
        };
        if (run.State == RunLifecycleState.Failed)
            risks.Add(run.FailureReason ?? "The disposable run failed.");
        if (outputVerifications.Count == 0 || outputVerifications.Any(item => !item.Verified))
            risks.Add("Expected output was not verified.");
        if (!string.Equals(codeStandards.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(codeStandards.Status, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            risks.Add("Code standards did not report a clean success state.");
        }

        return risks;
    }

    private static string ComputeFileSetHash(IReadOnlyList<GeneratedCodeFile> files)
    {
        var value = string.Join('\n', files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => $"{file.RelativePath}:{file.Sha256}"));
        return ComputeSha256(value);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? TryPayload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) ? value : null;
}
