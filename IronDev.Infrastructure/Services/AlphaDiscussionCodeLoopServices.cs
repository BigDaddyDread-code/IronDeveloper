using System.Diagnostics;
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
            ChangeSummary = "Captured Alpha discussion.",
            CreatedBy = "IronDev Alpha"
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

public sealed class TicketFromDocumentService : ITicketFromDocumentService
{
    private const string ExpectedOutput = "Hello from IronDev Alpha";
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
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var version = await _documents.GetVersionAsync(documentVersionId, cancellationToken).ConfigureAwait(false);
        if (version is null)
            return null;

        var document = await _documents.GetDocumentAsync(version.DocumentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            return null;

        var deterministicHelloWorld = version.ContentMarkdown.Contains(ExpectedOutput, StringComparison.OrdinalIgnoreCase);
        var title = !string.IsNullOrWhiteSpace(request.RequestedTitle)
            ? request.RequestedTitle.Trim()
            : deterministicHelloWorld
                ? "Build Hello World Console App"
                : $"Implement discussion: {document.Title}";

        var ticket = deterministicHelloWorld
            ? BuildHelloWorldTicket(document.ProjectId, documentVersionId, title)
            : BuildDiscussionTicket(document.ProjectId, documentVersionId, title, version.ContentMarkdown);

        ticket.Id = await _tickets.SaveTicketAsync(ticket, cancellationToken).ConfigureAwait(false);

        await _documents.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = documentVersionId,
            LinkedEntityType = "Ticket",
            LinkedEntityId = ticket.Id,
            LinkType = "GeneratedTicket",
            CreatedBy = "IronDev Alpha"
        }, cancellationToken).ConfigureAwait(false);

        return new CreateTicketFromDocumentResponse
        {
            TicketId = ticket.Id,
            SourceDocumentVersionId = documentVersionId
        };
    }

    private static ProjectTicket BuildHelloWorldTicket(int projectId, long documentVersionId, string title) => new()
    {
        ProjectId = projectId,
        SessionId = Guid.NewGuid(),
        Title = title,
        TicketType = "Alpha",
        Priority = "High",
        Summary = "Create a tiny C# console app that prints \"Hello from IronDev Alpha\".",
        Problem = "The backend Alpha loop needs a real disposable code generation proof.",
        AcceptanceCriteria = string.Join(Environment.NewLine, [
            "- Disposable C# console project is generated.",
            "- dotnet build succeeds.",
            "- dotnet run prints \"Hello from IronDev Alpha\".",
            "- Real repository is untouched.",
            "- Run events are persisted.",
            "- Result is reviewable."
        ]),
        TechnicalNotes = "Generated deterministically from an Alpha discussion document.",
        Status = "Draft",
        Content = "Alpha Hello World disposable code generation proof.",
        IsGenerated = true,
        GenerationNote = "Source: Discussion document. Alpha deterministic ticket generator.",
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
        TechnicalNotes = "Alpha deterministic discussion ticket.",
        Status = "Draft",
        Content = content,
        IsGenerated = true,
        GenerationNote = "Source: Discussion document.",
        SourceDocumentVersionId = documentVersionId
    };
}

public sealed class TicketDebateService : ITicketDebateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ITicketService _tickets;
    private readonly IProjectDocumentService _documents;

    public TicketDebateService(
        ITicketService tickets,
        IProjectDocumentService documents)
    {
        _tickets = tickets;
        _documents = documents;
    }

    public async Task<AgentDebateResult?> RunDebateAsync(
        long ticketId,
        RunTicketDebateRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null)
            return null;

        var result = CreateDeterministicDebate(ticket);
        await _documents.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = ticket.ProjectId,
            Title = $"Agent Debate {result.DebateId} for Ticket {ticket.Id}",
            DocumentType = "AgentDebate",
            ContentMarkdown = JsonSerializer.Serialize(result, JsonOptions),
            ChangeSummary = "Captured deterministic Alpha agent debate.",
            CreatedBy = "IronDev Alpha"
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<AgentDebateResult?> GetDebateAsync(
        long ticketId,
        string debateId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || string.IsNullOrWhiteSpace(debateId))
            return null;

        var documents = await _documents.GetDocumentsAsync(new GetProjectDocumentsRequest
        {
            ProjectId = ticket.ProjectId,
            DocumentType = "AgentDebate",
            Status = "Active"
        }, cancellationToken).ConfigureAwait(false);

        foreach (var document in documents)
        {
            var version = document.CurrentVersionId.HasValue
                ? await _documents.GetVersionAsync(document.CurrentVersionId.Value, cancellationToken).ConfigureAwait(false)
                : null;
            if (version is null || !version.ContentMarkdown.Contains(debateId, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var result = JsonSerializer.Deserialize<AgentDebateResult>(version.ContentMarkdown, JsonOptions);
                if (result is not null &&
                    result.TicketId == ticketId &&
                    result.ProjectId == ticket.ProjectId &&
                    string.Equals(result.DebateId, debateId, StringComparison.OrdinalIgnoreCase))
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

    private static AgentDebateResult CreateDeterministicDebate(ProjectTicket ticket)
    {
        var debateId = $"alpha-debate-{ticket.Id}-{Guid.NewGuid():N}";
        return new AgentDebateResult
        {
            DebateId = debateId,
            ProjectId = ticket.ProjectId,
            TicketId = ticket.Id,
            Contributions =
            [
                new AgentDebateContribution
                {
                    Role = "Planner",
                    Summary = "Build the smallest possible disposable C# console app.",
                    Recommendations = ["Generate only Program.cs and a minimal SDK-style csproj."]
                },
                new AgentDebateContribution
                {
                    Role = "Builder",
                    Summary = "Generate Program.cs and HelloWorldAlpha.csproj inside the disposable workspace only.",
                    Concerns = ["No real repository mutation is allowed."],
                    Recommendations = ["Use backend-owned workspace and allowed dotnet commands."]
                },
                new AgentDebateContribution
                {
                    Role = "Tester",
                    Summary = "Run dotnet build and dotnet run, then verify the expected output.",
                    Recommendations = ["Persist stdout and stderr logs as evidence."]
                },
                new AgentDebateContribution
                {
                    Role = "Critic",
                    Summary = "Proceed only if the run stays disposable and pauses for human review.",
                    Concerns = ["Do not apply generated code to the real repository."],
                    Recommendations = ["End successful execution in PausedForApproval."]
                }
            ],
            Decision = new AgentDebateDecision
            {
                Proceed = IsHelloWorldTicket(ticket),
                RecommendedNextStep = IsHelloWorldTicket(ticket)
                    ? "Start Alpha disposable code run."
                    : "Refine the ticket before disposable execution.",
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

    private static bool IsHelloWorldTicket(ProjectTicket ticket) =>
        ($"{ticket.Title} {ticket.Summary} {ticket.AcceptanceCriteria}")
            .Contains("Hello from IronDev Alpha", StringComparison.OrdinalIgnoreCase);
}

public sealed class AlphaConsoleProjectGenerator : IAlphaConsoleProjectGenerator
{
    public async Task<GeneratedAlphaProject> GenerateAsync(
        string workspacePath,
        string expectedOutput,
        CancellationToken cancellationToken = default)
    {
        var projectPath = Path.Combine(workspacePath, "HelloWorldAlpha");
        Directory.CreateDirectory(projectPath);

        var escapedOutput = expectedOutput.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        var programText = $"Console.WriteLine(\"{escapedOutput}\");{Environment.NewLine}";
        var csprojText = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;

        var programPath = Path.Combine(projectPath, "Program.cs");
        var csprojPath = Path.Combine(projectPath, "HelloWorldAlpha.csproj");
        await File.WriteAllTextAsync(programPath, programText, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(csprojPath, csprojText, cancellationToken).ConfigureAwait(false);

        return new GeneratedAlphaProject
        {
            WorkspacePath = workspacePath,
            ProjectPath = projectPath,
            ProgramPath = programPath,
            CsprojPath = csprojPath,
            ProgramText = programText,
            CsprojText = csprojText
        };
    }
}

public sealed class AlphaHelloWorldCodeRunService : IAlphaHelloWorldCodeRunService
{
    private readonly ITicketService _tickets;
    private readonly ITicketDebateService _debates;
    private readonly IAlphaConsoleProjectGenerator _generator;
    private readonly IGovernedToolRegistry _tools;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IConfiguration _configuration;

    public AlphaHelloWorldCodeRunService(
        ITicketService tickets,
        ITicketDebateService debates,
        IAlphaConsoleProjectGenerator generator,
        IGovernedToolRegistry tools,
        IRunStore runs,
        IRunEventStore events,
        IConfiguration configuration)
    {
        _tickets = tickets;
        _debates = debates;
        _generator = generator;
        _tools = tools;
        _runs = runs;
        _events = events;
        _configuration = configuration;
    }

    public async Task<StartAlphaDisposableCodeRunResponse?> StartAsync(
        long ticketId,
        StartAlphaDisposableCodeRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null)
            return null;

        var debate = await _debates.GetDebateAsync(ticketId, request.DebateId, cancellationToken).ConfigureAwait(false);
        if (debate is null)
            return null;
        if (!debate.Decision.Proceed)
            throw new InvalidOperationException("Debate decision does not allow disposable code execution.");

        var run = await _runs.CreateAsync(new CreateRunRequest
        {
            ProjectId = ticket.ProjectId,
            TicketId = ticket.Id,
            IsDisposable = true,
            Summary = "Alpha Hello World disposable code run created."
        }, cancellationToken).ConfigureAwait(false);

        var evidenceRoot = ResolveEvidenceRoot();
        var runEvidenceRoot = Path.Combine(evidenceRoot, run.RunId);
        var evidenceDirectory = Path.Combine(runEvidenceRoot, "evidence");
        Directory.CreateDirectory(evidenceDirectory);

        await WriteEvidenceAsync(evidenceDirectory, "debate-result.json", JsonSerializer.Serialize(debate, JsonOptions), cancellationToken).ConfigureAwait(false);
        await PublishAsync(run.RunId, "RunCreated", "Alpha disposable code run created.", ticket, new Dictionary<string, string>
        {
            ["status"] = RunLifecycleState.Created.ToString(),
            ["currentNode"] = "Created"
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = run.RunId,
                State = RunLifecycleState.Running,
                Summary = "Alpha disposable code run started."
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "DebateLinked", $"Linked debate {debate.DebateId}.", ticket, new Dictionary<string, string>
            {
                ["debateId"] = debate.DebateId,
                ["status"] = RunLifecycleState.Running.ToString(),
                ["currentNode"] = "DebateLinked"
            }, cancellationToken).ConfigureAwait(false);

            var workspacePath = ResolveWorkspacePath(run.RunId);
            ValidateWorkspacePath(workspacePath);
            if (Directory.Exists(workspacePath))
                Directory.Delete(workspacePath, recursive: true);

            await PublishAsync(run.RunId, "WorkspacePreparing", "Preparing disposable Alpha workspace.", ticket, new Dictionary<string, string>
            {
                ["workspacePath"] = workspacePath,
                ["currentNode"] = "WorkspacePreparing"
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "CodeGenerationStarted", "Generating Hello World Alpha console project.", ticket, new Dictionary<string, string>
            {
                ["currentNode"] = "CodeGeneration"
            }, cancellationToken).ConfigureAwait(false);

            var generated = await _generator.GenerateAsync(workspacePath, request.ExpectedOutput, cancellationToken).ConfigureAwait(false);
            await WriteEvidenceAsync(evidenceDirectory, "Program.cs", generated.ProgramText, cancellationToken).ConfigureAwait(false);
            await WriteEvidenceAsync(evidenceDirectory, "HelloWorldAlpha.csproj", generated.CsprojText, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "WorkspaceReady", "Disposable Alpha workspace is ready.", ticket, new Dictionary<string, string>
            {
                ["workspacePath"] = generated.WorkspacePath,
                ["projectPath"] = generated.ProjectPath,
                ["currentNode"] = "WorkspaceReady"
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(run.RunId, "CodeGenerationCompleted", "Generated Program.cs and HelloWorldAlpha.csproj.", ticket, new Dictionary<string, string>
            {
                ["programPath"] = generated.ProgramPath,
                ["csprojPath"] = generated.CsprojPath,
                ["currentNode"] = "CodeGeneration"
            }, cancellationToken).ConfigureAwait(false);

            var build = await RunDotNetCommandAsync(run.RunId, ticket, generated.ProjectPath, "dotnet build", ["build", "--nologo"], evidenceDirectory, cancellationToken).ConfigureAwait(false);
            if (build.ExitCode != 0)
                throw new InvalidOperationException("dotnet build failed.");

            var runCommand = await RunDotNetCommandAsync(run.RunId, ticket, generated.ProjectPath, "dotnet run", ["run", "--no-build", "--nologo"], evidenceDirectory, cancellationToken).ConfigureAwait(false);
            if (runCommand.ExitCode != 0)
                throw new InvalidOperationException("dotnet run failed.");

            var outputVerified = runCommand.StandardOutput.Contains(request.ExpectedOutput, StringComparison.Ordinal);
            await WriteEvidenceAsync(evidenceDirectory, "output-verification.json", JsonSerializer.Serialize(new
            {
                expected = request.ExpectedOutput,
                actual = runCommand.StandardOutput,
                verified = outputVerified
            }, JsonOptions), cancellationToken).ConfigureAwait(false);
            if (!outputVerified)
                throw new InvalidOperationException("dotnet run output did not contain the expected text.");

            await PublishAsync(run.RunId, "OutputVerified", $"Verified output contains '{request.ExpectedOutput}'.", ticket, new Dictionary<string, string>
            {
                ["expectedOutput"] = request.ExpectedOutput,
                ["currentNode"] = "OutputVerification"
            }, cancellationToken).ConfigureAwait(false);

            await RunCodeStandardsAsync(run.RunId, ticket, generated, evidenceDirectory, cancellationToken).ConfigureAwait(false);

            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = run.RunId,
                State = RunLifecycleState.PausedForApproval,
                Summary = "Alpha Hello World run is ready for human review.",
                WorkspacePath = workspacePath
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(run.RunId, "RunPausedForApproval", "Alpha Hello World run paused for human review.", ticket, new Dictionary<string, string>
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
                Summary = "Alpha Hello World disposable code run failed.",
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
        return new StartAlphaDisposableCodeRunResponse
        {
            RunId = updated.RunId,
            State = updated.State.ToString(),
            IsDisposable = updated.IsDisposable
        };
    }

    private async Task RunCodeStandardsAsync(
        string runId,
        ProjectTicket ticket,
        GeneratedAlphaProject generated,
        string evidenceDirectory,
        CancellationToken cancellationToken)
    {
        await PublishAsync(runId, "CodeStandardsStarted", "Running governed code standards analysis.", ticket, new Dictionary<string, string>
        {
            ["toolName"] = CodeStandardsAnalysisTool.ToolName,
            ["currentNode"] = "CodeStandards"
        }, cancellationToken).ConfigureAwait(false);

        var patchText = $"+++ Program.cs{Environment.NewLine}{generated.ProgramText}";
        var result = await _tools.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            new GovernedToolRequest<CodeStandardsAnalysisInput>
            {
                RequestId = $"alpha-hello-world-code-standards-{runId}",
                ToolName = CodeStandardsAnalysisTool.ToolName,
                RequestedBy = "BuilderAgent",
                Input = new CodeStandardsAnalysisInput
                {
                    PatchText = patchText,
                    ChangedFiles =
                    [
                        new CodeStandardsChangedFile
                        {
                            Path = "HelloWorldAlpha/Program.cs",
                            Content = generated.ProgramText
                        },
                        new CodeStandardsChangedFile
                        {
                            Path = "HelloWorldAlpha/HelloWorldAlpha.csproj",
                            Content = generated.CsprojText
                        }
                    ]
                },
                Reason = "Run read-only code standards check over generated Alpha Hello World files."
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
        timeout.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSeconds("AlphaTimeoutSeconds", 120)));

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

    private string ResolveWorkspacePath(string runId)
    {
        var configured = _configuration["AlphaDisposableCode:WorkspaceRoot"] ??
                         _configuration["LocalTest:WorkspaceRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevAlphaWorkspaces")
            : configured;
        return Path.Combine(root, "runs", runId);
    }

    private string ResolveEvidenceRoot()
    {
        var configured = _configuration["AlphaDisposableCode:EvidenceRoot"] ??
                         _configuration["DisposableBuild:EvidenceRoot"] ??
                         _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "runs");
    }

    private void ValidateWorkspacePath(string workspacePath)
    {
        var configured = _configuration["AlphaDisposableCode:WorkspaceRoot"] ??
                         _configuration["LocalTest:WorkspaceRoot"];
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevAlphaWorkspaces")
            : configured);
        var fullWorkspace = Path.GetFullPath(workspacePath);
        var relative = Path.GetRelativePath(root, fullWorkspace);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Alpha disposable workspace path escaped the configured workspace root.");
    }

    private int ReadTimeoutSeconds(string key, int fallback)
    {
        var value = _configuration[$"AlphaDisposableCode:{key}"];
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
            ["alphaDiscussionCodeLoop"] = "true"
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
}
