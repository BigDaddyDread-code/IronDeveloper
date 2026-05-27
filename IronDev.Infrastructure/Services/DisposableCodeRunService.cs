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
public sealed class DisposableCodeRunService : IDisposableCodeRunService
{
    private readonly ITicketService _tickets;
    private readonly ITicketReviewService _reviews;
    private readonly ICodeProposalGenerator _proposalGenerator;
    private readonly ICodeProposalValidator _proposalValidator;
    private readonly IGovernedToolRegistry _tools;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IConfiguration _configuration;

    public DisposableCodeRunService(
        ITicketService tickets,
        ITicketReviewService reviews,
        ICodeProposalGenerator proposalGenerator,
        ICodeProposalValidator proposalValidator,
        IGovernedToolRegistry tools,
        IRunStore runs,
        IRunEventStore events,
        IConfiguration configuration)
    {
        _tickets = tickets;
        _reviews = reviews;
        _proposalGenerator = proposalGenerator;
        _proposalValidator = proposalValidator;
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
        await PublishAsync(run.RunId, "RunCreated", "Disposable code run created.", ticket, new Dictionary<string, string>
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
                Summary = "Disposable code run started."
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "ReviewLinked", $"Linked ticket review {review.ReviewId}.", ticket, new Dictionary<string, string>
            {
                ["reviewId"] = review.ReviewId,
                ["status"] = RunLifecycleState.Running.ToString(),
                ["currentNode"] = "ReviewLinked"
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "CodeProposalStarted", "Generating code proposal.", ticket, new Dictionary<string, string>
            {
                ["reviewId"] = review.ReviewId,
                ["currentNode"] = "CodeProposal"
            }, cancellationToken).ConfigureAwait(false);

            var proposal = await _proposalGenerator.GenerateAsync(review, request.ExpectedOutput, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(request.ScenarioId) &&
                !string.Equals(proposal.ScenarioId, request.ScenarioId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Code proposal scenario does not match the requested scenario.");
            }

            var validation = _proposalValidator.Validate(proposal);
            await WriteEvidenceAsync(evidenceDirectory, "code-proposal.json", JsonSerializer.Serialize(proposal, JsonOptions), cancellationToken).ConfigureAwait(false);
            await WriteEvidenceAsync(evidenceDirectory, "code-proposal-validation.json", JsonSerializer.Serialize(validation, JsonOptions), cancellationToken).ConfigureAwait(false);
            await PublishAsync(run.RunId, validation.IsValid ? "CodeProposalValidated" : "CodeProposalRejected", validation.IsValid ? "Code proposal passed validation." : "Code proposal failed validation.", ticket, new Dictionary<string, string>
            {
                ["proposalId"] = proposal.ProposalId,
                ["runtimeProfileId"] = proposal.RunProfile.RuntimeProfileId,
                ["errorCount"] = validation.Errors.Count.ToString(),
                ["warningCount"] = validation.Warnings.Count.ToString(),
                ["currentNode"] = "CodeProposal"
            }, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
                throw new InvalidOperationException($"Code proposal failed validation: {string.Join("; ", validation.Errors)}");

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

