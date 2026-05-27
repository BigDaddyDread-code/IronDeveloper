using System.Diagnostics;
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
            CreatedBy = "IronDev"
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
        TicketType = "Scenario",
        Priority = "High",
        Summary = "Create a tiny C# console app that prints \"Hello from IronDev Alpha\".",
        Problem = "The backend discussion-to-code loop needs a real disposable code generation proof.",
        AcceptanceCriteria = string.Join(Environment.NewLine, [
            "- Disposable C# console project is generated.",
            "- dotnet build succeeds.",
            "- dotnet run prints \"Hello from IronDev Alpha\".",
            "- Real repository is untouched.",
            "- Run events are persisted.",
            "- Result is reviewable."
        ]),
        TechnicalNotes = "Generated deterministically from a discussion document using the Hello World scenario fixture.",
        Status = "Draft",
        Content = "Hello World disposable code generation proof.",
        IsGenerated = true,
        GenerationNote = "Source: Discussion document. Deterministic Hello World scenario fixture.",
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
        var helloWorld = IsHelloWorldTicket(ticket);
        return new TicketReviewResult
        {
            ReviewId = reviewId,
            ProjectId = ticket.ProjectId,
            TicketId = ticket.Id,
            ScenarioId = helloWorld ? "hello-world-alpha" : "none",
            Contributions =
            [
                new TicketReviewContribution
                {
                    Role = "Planner",
                    Summary = "Use the smallest deterministic code proposal that proves the disposable backend path.",
                    Recommendations = ["Generate only Program.cs and a minimal SDK-style csproj for the scenario."]
                },
                new TicketReviewContribution
                {
                    Role = "Builder",
                    Summary = "Create generated files in the backend-owned disposable workspace only.",
                    Concerns = ["No real repository mutation is allowed."],
                    Recommendations = ["Use a code proposal and backend-owned command profile."]
                },
                new TicketReviewContribution
                {
                    Role = "Tester",
                    Summary = "Run dotnet build and dotnet run, then verify the expected output.",
                    Recommendations = ["Persist stdout and stderr logs as evidence."]
                },
                new TicketReviewContribution
                {
                    Role = "Critic",
                    Summary = "Proceed only if the run stays disposable and pauses for human review.",
                    Concerns = ["Do not apply generated code to the real repository."],
                    Recommendations = ["End successful execution in PausedForApproval."]
                }
            ],
            Decision = new TicketReviewDecision
            {
                Proceed = helloWorld,
                RecommendedNextStep = helloWorld
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

    private static bool IsHelloWorldTicket(ProjectTicket ticket) =>
        ($"{ticket.Title} {ticket.Summary} {ticket.AcceptanceCriteria}")
            .Contains("Hello from IronDev Alpha", StringComparison.OrdinalIgnoreCase);
}

public sealed class HelloWorldCodeProposalGenerator : ICodeProposalGenerator
{
    public Task<CodeProposal> GenerateAsync(
        TicketReviewResult review,
        string expectedOutput,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(review.ScenarioId, "hello-world-alpha", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"No deterministic code proposal scenario is registered for '{review.ScenarioId}'.");

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

        return Task.FromResult(new CodeProposal
        {
            ProposalId = $"code-proposal-{review.TicketId}-{Guid.NewGuid():N}",
            ProjectId = review.ProjectId,
            TicketId = review.TicketId,
            ReviewId = review.ReviewId,
            ScenarioId = review.ScenarioId,
            ExpectedOutput = expectedOutput,
            Files =
            [
                CreateFile("HelloWorldAlpha/Program.cs", programText),
                CreateFile("HelloWorldAlpha/HelloWorldAlpha.csproj", csprojText)
            ],
            RunProfile = new CodeRunProfile
            {
                WorkingDirectory = "HelloWorldAlpha",
                BuildCommand = "dotnet build --nologo",
                RunCommand = "dotnet run --no-build --nologo"
            }
        });
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
        if (!string.Equals(proposal.ScenarioId, request.ScenarioId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Code proposal scenario does not match the requested scenario.");

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

            var runCommand = await RunDotNetCommandAsync(run.RunId, ticket, projectPath, "dotnet run", ["run", "--no-build", "--nologo"], evidenceDirectory, cancellationToken).ConfigureAwait(false);
            if (runCommand.ExitCode != 0)
                throw new InvalidOperationException("dotnet run failed.");

            var outputVerified = runCommand.StandardOutput.Contains(proposal.ExpectedOutput, StringComparison.Ordinal);
            await WriteEvidenceAsync(evidenceDirectory, "output-verification.json", JsonSerializer.Serialize(new
            {
                expected = proposal.ExpectedOutput,
                actual = runCommand.StandardOutput,
                verified = outputVerified
            }, JsonOptions), cancellationToken).ConfigureAwait(false);
            if (!outputVerified)
                throw new InvalidOperationException("dotnet run output did not contain the expected text.");

            await PublishAsync(run.RunId, "OutputVerified", $"Verified output contains '{proposal.ExpectedOutput}'.", ticket, new Dictionary<string, string>
            {
                ["expectedOutput"] = proposal.ExpectedOutput,
                ["currentNode"] = "OutputVerification"
            }, cancellationToken).ConfigureAwait(false);

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

        var outputVerification = await LoadOutputVerificationAsync(runId, evidence, cancellationToken).ConfigureAwait(false);
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
            CodeStandards = codeStandards,
            FileSetHash = ComputeFileSetHash(generatedFiles),
            Risks = BuildRisks(run, codeStandards, outputVerification),
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

    private async Task<OutputVerificationEvidence> LoadOutputVerificationAsync(
        string runId,
        IReadOnlyList<RunEvidenceItem> evidence,
        CancellationToken cancellationToken)
    {
        var item = evidence.FirstOrDefault(item => item.Path.EndsWith("output-verification.json", StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return new OutputVerificationEvidence { Expected = string.Empty, Verified = false };

        var text = await _evidence.ReadEvidenceTextAsync(runId, item.Path, cancellationToken).ConfigureAwait(false)
                   ?? await File.ReadAllTextAsync(item.Path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return new OutputVerificationEvidence
        {
            Expected = root.TryGetProperty("expected", out var expected) ? expected.GetString() ?? string.Empty : string.Empty,
            Actual = root.TryGetProperty("actual", out var actual) ? actual.GetString() ?? string.Empty : string.Empty,
            Verified = root.TryGetProperty("verified", out var verified) && verified.GetBoolean(),
            EvidencePath = item.Path
        };
    }

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
        OutputVerificationEvidence outputVerification)
    {
        var risks = new List<string>
        {
            "This is a deterministic Hello World scenario fixture, not a general code generation engine.",
            "Generated code remains in a disposable workspace until a separate human-approved apply path exists."
        };
        if (run.State == RunLifecycleState.Failed)
            risks.Add(run.FailureReason ?? "The disposable run failed.");
        if (!outputVerification.Verified)
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
