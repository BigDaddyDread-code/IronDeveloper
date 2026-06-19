using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Client;
using IronDev.Core.Agents;
using IronDev.Core.ChatProbe;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Workspaces;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services.Agents;
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Services.Workspaces;

namespace IronDev.Cli;

public static class IronDevCli
{
    private const string DefaultApiBaseUrl = "http://localhost:5000";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private const string AgentRunSupervisorCommand = "agent run supervisor";
    private const string WorkspaceCheckCommand = "workspace check";
    private const string WorkspacePrepareCommand = "workspace prepare";
    private const string WorkspaceRunCommand = "workspace run";
    private const string WorkspaceValidateCommand = "workspace validate";
    private const string WorkspaceDiffCommand = "workspace diff";
    private const string WorkspacePromotionPackageCommand = "workspace promotion-package";
    private const string WorkspacePromotionApprovalCommand = "workspace promotion-approval";
    private const string WorkspaceApplyPreflightCommand = "workspace apply-preflight";
    private const string WorkspaceApplyDryRunCommand = "workspace apply-dry-run";
    private const string WorkspaceApplyCopyCommand = "workspace apply-copy";
    private const string WorkspaceApplyVerifyCommand = "workspace apply-verify";
    private const string WorkspacePostApplyValidateCommand = "workspace post-apply-validate";
    private const string WorkspaceSourceReportCommand = "workspace source-report";
    private const string WorkspaceFailurePackageCommand = "workspace failure-package";

    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) =>
        RunAsync(args, output, error, handler: null, supervisorAgentRunService: null, workspaceReadinessService: null, workspacePrepareService: null, workspaceCommandService: null, workspaceValidationService: null, workspaceDiffService: null, cancellationToken);

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        return await RunAsync(args, output, error, handler, supervisorAgentRunService: null, workspaceReadinessService: null, workspacePrepareService: null, workspaceCommandService: null, workspaceValidationService: null, workspaceDiffService: null, cancellationToken);
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        ISupervisorAgentRunService? supervisorAgentRunService,
        CancellationToken cancellationToken)
    {
        return await RunAsync(args, output, error, handler, supervisorAgentRunService, workspaceReadinessService: null, workspacePrepareService: null, workspaceCommandService: null, workspaceValidationService: null, workspaceDiffService: null, cancellationToken);
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        ISupervisorAgentRunService? supervisorAgentRunService,
        IDisposableWorkspaceReadinessService? workspaceReadinessService,
        IDisposableWorkspacePrepareService? workspacePrepareService,
        CancellationToken cancellationToken)
    {
        return await RunAsync(
            args,
            output,
            error,
            handler,
            supervisorAgentRunService,
            workspaceReadinessService,
            workspacePrepareService,
            workspaceCommandService: null,
            workspaceValidationService: null,
            workspaceDiffService: null,
            cancellationToken);
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        ISupervisorAgentRunService? supervisorAgentRunService,
        IDisposableWorkspaceReadinessService? workspaceReadinessService,
        IDisposableWorkspacePrepareService? workspacePrepareService,
        IDisposableWorkspaceCommandService? workspaceCommandService,
        CancellationToken cancellationToken)
    {
        return await RunAsync(
            args,
            output,
            error,
            handler,
            supervisorAgentRunService,
            workspaceReadinessService,
            workspacePrepareService,
            workspaceCommandService,
            workspaceValidationService: null,
            workspaceDiffService: null,
            cancellationToken);
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        ISupervisorAgentRunService? supervisorAgentRunService,
        IDisposableWorkspaceReadinessService? workspaceReadinessService,
        IDisposableWorkspacePrepareService? workspacePrepareService,
        IDisposableWorkspaceCommandService? workspaceCommandService,
        IDisposableWorkspaceValidationService? workspaceValidationService,
        CancellationToken cancellationToken)
    {
        return await RunAsync(
            args,
            output,
            error,
            handler,
            supervisorAgentRunService,
            workspaceReadinessService,
            workspacePrepareService,
            workspaceCommandService,
            workspaceValidationService,
            workspaceDiffService: null,
            cancellationToken);
    }

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        ISupervisorAgentRunService? supervisorAgentRunService,
        IDisposableWorkspaceReadinessService? workspaceReadinessService,
        IDisposableWorkspacePrepareService? workspacePrepareService,
        IDisposableWorkspaceCommandService? workspaceCommandService,
        IDisposableWorkspaceValidationService? workspaceValidationService,
        IDisposableWorkspaceDiffService? workspaceDiffService,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage(error);
            return 2;
        }


        if (IronDevCliFoundation.IsHelp(args))
        {
            PrintUsage(output);
            return 0;
        }

        if (IronDevCliFoundation.IsVersion(args))
            return IronDevCliFoundation.WriteVersion(output);

        if (IronDevCliFoundation.IsConfigShow(args))
            return await IronDevCliFoundation.HandleConfigShowAsync(args, output, error, ReadEnvironment(), cancellationToken).ConfigureAwait(false);

        if (IronDevCliFoundation.IsApiPing(args))
            return await IronDevCliFoundation.HandleApiPingAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);


        if (IronDevCliAgentRuns.IsAgentRunsCommand(args))
            return await IronDevCliAgentRuns.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliManualCritic.IsCriticCommand(args))
            return await IronDevCliManualCritic.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliMemoryImprovements.IsMemoryImprovementsCommand(args))
            return await IronDevCliMemoryImprovements.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliToolRequests.IsToolRequestsCommand(args))
            return await IronDevCliToolRequests.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliDogfoodLoops.IsDogfoodLoopsCommand(args))
            return await IronDevCliDogfoodLoops.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliApplyPreview.IsApplyPreviewCommand(args))
            return await IronDevCliApplyPreview.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliWorkflowContinuation.IsWorkflowContinuationCommand(args))
            return await IronDevCliWorkflowContinuation.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliReleaseGate.IsReleaseGateCommand(args))
            return await IronDevCliReleaseGate.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliGovernanceInspection.IsGovernanceCommand(args))
            return await IronDevCliGovernanceInspection.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliPlanning.IsPlanCommand(args))
            return await IronDevCliPlanning.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliProductHardening.IsProductHardeningCommand(args))
            return await IronDevCliProductHardening.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliCommitPackage.IsCommitPackageCommand(args))
            return await IronDevCliCommitPackage.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliPullRequest.IsPullRequestCommand(args))
            return await IronDevCliPullRequest.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliFeedback.IsFeedbackCommand(args))
            return await IronDevCliFeedback.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliFeedbackPatch.IsFeedbackPatchCommand(args))
            return await IronDevCliFeedbackPatch.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliPrUpdatePackage.IsPrUpdateCommand(args))
            return await IronDevCliPrUpdatePackage.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliPrBranchUpdate.IsPrBranchUpdateCommand(args))
            return await IronDevCliPrBranchUpdate.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliReadyForReview.IsReadyCommand(args))
            return await IronDevCliReadyForReview.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliMergeRelease.IsMergeReleaseCommand(args))
            return await IronDevCliMergeRelease.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliValidation.IsValidationCommand(args))
            return await IronDevCliValidation.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliWorkflowInspection.IsWorkflowInspectCommand(args))
            return await IronDevCliWorkflowInspection.HandleAsync(args, output, error, ReadEnvironment(), handler, cancellationToken).ConfigureAwait(false);

        if (IronDevCliPatchProposal.IsPatchCommand(args))
            return await IronDevCliPatchProposal.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliMemory.IsMemoryCommand(args))
            return await IronDevCliMemory.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IronDevCliSourceApply.IsSourceApplyCommand(args))
            return await IronDevCliSourceApply.HandleAsync(args, output, error, cancellationToken).ConfigureAwait(false);

        if (IsCommand(args, "ticket", "create"))
            return await HandleTicketCreateAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "ticket", "list"))
            return await HandleTicketListAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "ticket", "show"))
            return await HandleTicketShowAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "ticket", "import-github-issue"))
            return await HandleTicketImportGithubIssueAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "ticket", "build") || IsCommand(args, "tickets", "build"))
            return await HandleTicketBuildAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "runs", "status"))
            return await HandleRunStatusAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "runs", "report"))
            return await HandleRunReportAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "runs", "stream"))
            return await HandleRunStreamAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "workspace", "check"))
            return await HandleWorkspaceCheckAsync(args, output, error, workspaceReadinessService, cancellationToken);
        if (IsCommand(args, "workspace", "prepare"))
            return await HandleWorkspacePrepareAsync(args, output, error, workspacePrepareService, cancellationToken);
        if (IsCommand(args, "workspace", "run"))
            return await HandleWorkspaceRunAsync(args, output, error, workspaceCommandService, cancellationToken);
        if (IsCommand(args, "workspace", "validate"))
            return await HandleWorkspaceValidateAsync(args, output, error, workspaceValidationService, cancellationToken);
        if (IsCommand(args, "workspace", "diff"))
            return await HandleWorkspaceDiffAsync(args, output, error, workspaceDiffService, cancellationToken);
        if (IsCommand(args, "workspace", "promotion-package"))
            return await HandleWorkspacePromotionPackageAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "promotion-approval"))
            return await HandleWorkspacePromotionApprovalAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "apply-preflight"))
            return await HandleWorkspaceApplyPreflightAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "apply-dry-run"))
            return await HandleWorkspaceApplyDryRunAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "apply-copy"))
            return await HandleWorkspaceApplyCopyAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "apply-verify"))
            return await HandleWorkspaceApplyVerifyAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "post-apply-validate"))
            return await HandleWorkspacePostApplyValidateAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "source-report"))
            return await HandleWorkspaceSourceReportAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "workspace", "failure-package"))
            return await HandleWorkspaceFailurePackageAsync(args, output, error, cancellationToken);
        if (args.Length >= 3 &&
            string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "run", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[2], "supervisor"))
            return await HandleAgentRunSupervisorAsync(args, output, error, handler, supervisorAgentRunService, cancellationToken);
        if (IsCommand(args, "exercise", "chat-to-build"))
            return await HandleExerciseChatToBuildAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "scenario", "list"))
            return await HandleScenarioListAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "scenario", "run"))
            return await HandleScenarioRunAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "scenario", "report"))
            return await HandleScenarioReportAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "chat-probe", "run"))
            return await HandleChatProbeRunAsync(args, output, error, handler, cancellationToken);
        if (IsCommand(args, "chat-probe", "list-scenarios"))
            return await HandleChatProbeListScenariosAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "chat-probe", "list-personas"))
            return await HandleChatProbeListPersonasAsync(args, output, error, cancellationToken);
        if (IsCommand(args, "chat-probe", "export-failures"))
            return await HandleChatProbeExportFailuresAsync(args, output, error, cancellationToken);

        error.WriteLine($"Unknown command: {string.Join(' ', args)}");
        PrintUsage(error);
        return 2;
    }

    public static string ResolveApiBaseUrl(
        string? argumentValue,
        IReadOnlyDictionary<string, string?> environment,
        string? configPath = null)
    {
        if (!string.IsNullOrWhiteSpace(argumentValue))
            return NormalizeBaseUrl(argumentValue);

        if (environment.TryGetValue("IRONDEV_API_BASE_URL", out var envValue) &&
            !string.IsNullOrWhiteSpace(envValue))
        {
            return NormalizeBaseUrl(envValue);
        }

        var config = ReadConfig(configPath);
        if (!string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            return NormalizeBaseUrl(config.ApiBaseUrl);

        return DefaultApiBaseUrl;
    }

    private static async Task<int> HandleTicketCreateAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        var json = HasFlag(args, "--json");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        var file = GetOption(args, "--file");
        if (string.IsNullOrWhiteSpace(file))
        {
            error.WriteLine("Missing required option: --file <ticket.json>");
            return 2;
        }

        if (!File.Exists(file))
        {
            error.WriteLine($"Ticket file not found: {file}");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        CreateProjectTicketRequest request;
        try
        {
            await using var stream = File.OpenRead(file);
            request = await JsonSerializer.DeserializeAsync<CreateProjectTicketRequest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Ticket file did not contain a ticket payload.");
        }
        catch (JsonException ex)
        {
            error.WriteLine($"Ticket file is not valid JSON: {ex.Message}");
            return 2;
        }

        try
        {
            var saved = await client.CreateTicketAsync(projectId, request, cancellationToken);
            if (json)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(saved, JsonOptions));
            }
            else
            {
                await output.WriteLineAsync($"Created IronDev ticket {saved.Id}: {saved.Title}");
            }

            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("ticket create", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleTicketListAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        var take = TryGetIntOption(args, "--take", out var parsedTake) ? parsedTake : 50;
        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var tickets = await client.GetTicketsAsync(projectId, take, cancellationToken);
            await WriteJsonOrTextAsync(output, tickets, HasFlag(args, "--json"), $"Found {tickets.Count} IronDev tickets.");
            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("ticket list", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleTicketShowAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        if (!TryGetLongOption(args, "--ticket-id", out var ticketId))
        {
            error.WriteLine("Missing or invalid required option: --ticket-id <id>");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var ticket = await client.GetProjectTicketAsync(projectId, ticketId, cancellationToken);
            if (ticket is null)
            {
                error.WriteLine("IronDev.Api returned an empty ticket response.");
                return 1;
            }

            await WriteJsonOrTextAsync(output, ticket, HasFlag(args, "--json"), $"{ticket.Id}: {ticket.Title}");
            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("ticket show", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleTicketImportGithubIssueAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        var file = GetOption(args, "--file");
        if (string.IsNullOrWhiteSpace(file))
        {
            error.WriteLine("Missing required option: --file <github-issue.json>");
            return 2;
        }

        if (!File.Exists(file))
        {
            error.WriteLine($"GitHub issue import file not found: {file}");
            return 2;
        }

        ImportExternalTicketRequest request;
        try
        {
            await using var stream = File.OpenRead(file);
            request = await JsonSerializer.DeserializeAsync<ImportExternalTicketRequest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Import file did not contain a ticket payload.");
        }
        catch (JsonException ex)
        {
            error.WriteLine($"GitHub issue import file is not valid JSON: {ex.Message}");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var saved = await client.ImportExternalTicketAsync(projectId, request, cancellationToken);
            await WriteJsonOrTextAsync(output, saved, HasFlag(args, "--json"), $"Imported GitHub issue as IronDev ticket {saved.Id}: {saved.Title}");
            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("ticket import-github-issue", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleTicketBuildAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        if (!TryGetLongOption(args, "--ticket-id", out var ticketId))
        {
            error.WriteLine("Missing or invalid required option: --ticket-id <id>");
            return 2;
        }

        var maxRetries = TryGetIntOption(args, "--max-retries", out var parsedMaxRetries) ? parsedMaxRetries : 3;
        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var run = await client.StartTicketBuildRunAsync(
                projectId,
                ticketId,
                new StartTicketBuildRunRequest { MaxRetries = maxRetries },
                cancellationToken);
            await WriteJsonOrTextAsync(output, run, HasFlag(args, "--json"), FormatTicketBuildRun(run));
            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("ticket build", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleRunStatusAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        var runId = GetOption(args, "--run-id");
        if (string.IsNullOrWhiteSpace(runId))
        {
            error.WriteLine("Missing required option: --run-id <id>");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var status = await client.GetRunAsync(runId, cancellationToken);
            await WriteJsonOrTextAsync(output, status, HasFlag(args, "--json"), FormatRunStatus(status));
            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("runs status", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleRunReportAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        if (string.IsNullOrWhiteSpace(runId))
        {
            error.WriteLine("Missing required option: --run-id <id>");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var report = await client.GetRunReportAsync(runId, cancellationToken);
            var envelope = RunReportContractMapper.MapFromApiReport(report);

            if (json)
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            else
                await WriteJsonOrTextAsync(output, report, json, FormatRunReport(report));

            return RunReportContractMapper.MapToReadResult(envelope).ExitCode;
        }
        catch (IronDevApiException ex)
        {
            if (json)
            {
                var failure = RunReportContractMapper.MapFromApiFailure(runId, ex.StatusCode, ex.ResponseBody);
                await output.WriteLineAsync(JsonSerializer.Serialize(
                    RunReportContractMapper.ToEnvelope(failure),
                    JsonOptions));
                return failure.ExitCode;
            }

            WriteApiError("runs report", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleWorkspaceCheckAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IDisposableWorkspaceReadinessService? workspaceReadinessService,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var sourceRepo = GetOption(args, "--source-repo");
        var workspaceRoot = GetOption(args, "--workspace-root");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(sourceRepo))
            validationErrors.Add("Missing required option: --source-repo <path>");
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            validationErrors.Add("Missing required option: --workspace-root <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceReadinessEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceCheckCommand,
                    TraceId = null,
                    Summary = "Disposable workspace readiness check could not be started.",
                    Data = new DisposableWorkspaceReadinessData
                    {
                        RunId = runId ?? string.Empty,
                        SourceRepo = sourceRepo ?? string.Empty,
                        WorkspaceRoot = workspaceRoot ?? string.Empty,
                        WorkspacePath = string.Empty,
                        SourceRepoExists = false,
                        WorkspaceRootExists = false,
                        WorkspacePathExists = false,
                        IsInsideSourceRepo = false,
                        GitStatusClean = false,
                        CanCreateWorkspaceDirectory = false,
                        Checks = [],
                        Ready = false,
                        SourceRepoIsGitRepo = false,
                        WorkspaceRootSameAsSourceRepo = false,
                        WorkspaceRootUnderGitDirectory = false,
                        WorkspacePathEscapedWorkspaceRoot = false
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        workspaceReadinessService ??= new DisposableWorkspaceReadinessService();
        var result = await workspaceReadinessService.CheckAsync(
            new DisposableWorkspaceReadinessRequest
            {
                RunId = runId!,
                SourceRepo = sourceRepo!,
                WorkspaceRoot = workspaceRoot!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceReadinessEnvelope
        {
            Status = result.Status,
            Command = WorkspaceCheckCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspacePromotionApprovalAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");
        var decision = GetOption(args, "--decision");
        var approvedBy = GetOption(args, "--approved-by");
        var reason = GetOption(args, "--reason");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspacePromotionApprovalEnvelope
                {
                    Status = "failed",
                    Command = WorkspacePromotionApprovalCommand,
                    TraceId = null,
                    Summary = "Workspace promotion approval evidence could not be started.",
                    Data = new DisposableWorkspacePromotionApprovalData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        Decision = decision ?? string.Empty,
                        ApprovedBy = approvedBy ?? string.Empty,
                        Reason = reason ?? string.Empty,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        PromotionPackagePath = string.Empty,
                        PromotionPackageSha256 = string.Empty,
                        ApprovalEvidencePath = null,
                        AllowsApply = false,
                        RequiresSeparateApplyCommand = false,
                        EvidencePaths = [],
                        Errors = validationErrors,
                        Warnings = []
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        var service = new DisposableWorkspacePromotionApprovalService();
        var result = await service.CreateAsync(
            new DisposableWorkspacePromotionApprovalRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!,
                Decision = decision ?? string.Empty,
                ApprovedBy = approvedBy ?? string.Empty,
                Reason = reason ?? string.Empty
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspacePromotionApprovalEnvelope
        {
            Status = result.Status,
            Command = WorkspacePromotionApprovalCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceApplyPreflightAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceApplyPreflightEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceApplyPreflightCommand,
                    TraceId = null,
                    Summary = "Workspace apply preflight could not be started.",
                    Data = new DisposableWorkspaceApplyPreflightData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        ApprovalDecision = string.Empty,
                        ApprovedBy = string.Empty,
                        ApprovalReason = string.Empty,
                        PromotionPackagePath = string.Empty,
                        PromotionPackageSha256 = string.Empty,
                        ApprovalPromotionPackageSha256 = string.Empty,
                        PromotionPackageHashMatchesApproval = false,
                        Recommendation = "not_ready_missing_evidence",
                        ValidationSucceeded = false,
                        DiffChanged = false,
                        ReadyForApply = false,
                        CanApplyNow = false,
                        RequiresSeparateApplyCommand = false,
                        Errors = validationErrors,
                        Blockers = validationErrors
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            }
            else
            {
                foreach (var validationError in validationErrors)
                    error.WriteLine(validationError);
                PrintUsage(error);
            }

            return 2;
        }

        var service = new DisposableWorkspaceApplyPreflightService();
        var result = await service.CheckAsync(
            new DisposableWorkspaceApplyPreflightRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceApplyPreflightEnvelope
        {
            Status = result.Status,
            Command = WorkspaceApplyPreflightCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceApplyDryRunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceApplyDryRunEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceApplyDryRunCommand,
                    TraceId = null,
                    Summary = "Workspace apply dry run could not be started.",
                    Data = new DisposableWorkspaceApplyDryRunData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        ReadyForApply = false,
                        CanApplyNow = false,
                        RequiresSeparateApplyCommand = false,
                        Recommendation = "not_ready_missing_arguments",
                        Blockers = validationErrors,
                        Errors = validationErrors
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            }
            else
            {
                foreach (var validationError in validationErrors)
                    error.WriteLine(validationError);
                PrintUsage(error);
            }

            return 2;
        }

        var service = new DisposableWorkspaceApplyDryRunService();
        var result = await service.CheckAsync(
            new DisposableWorkspaceApplyDryRunRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceApplyDryRunEnvelope
        {
            Status = result.Status,
            Command = WorkspaceApplyDryRunCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceApplyCopyAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceApplyCopyEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceApplyCopyCommand,
                    TraceId = null,
                    Summary = "Workspace apply copy could not be started.",
                    Data = new DisposableWorkspaceApplyCopyData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        Applied = false,
                        SourceRepoMutated = false,
                        Blockers = validationErrors,
                        Errors = validationErrors
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            }
            else
            {
                foreach (var validationError in validationErrors)
                    error.WriteLine(validationError);
                PrintUsage(error);
            }

            return 2;
        }

        var service = new DisposableWorkspaceApplyCopyService();
        var result = await service.ApplyAsync(
            new DisposableWorkspaceApplyCopyRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceApplyCopyEnvelope
        {
            Status = result.Status,
            Command = WorkspaceApplyCopyCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceApplyVerifyAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceApplyVerifyEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceApplyVerifyCommand,
                    TraceId = null,
                    Summary = "Workspace apply verification could not be started.",
                    Data = new DisposableWorkspaceApplyVerifyData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        Verified = false,
                        SourceMatchesWorkspace = false,
                        Blockers = validationErrors,
                        Errors = validationErrors
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            }
            else
            {
                foreach (var validationError in validationErrors)
                    error.WriteLine(validationError);
                PrintUsage(error);
            }

            return 2;
        }

        var service = new DisposableWorkspaceApplyVerifyService();
        var result = await service.VerifyAsync(
            new DisposableWorkspaceApplyVerifyRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceApplyVerifyEnvelope
        {
            Status = result.Status,
            Command = WorkspaceApplyVerifyCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspacePostApplyValidateAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");
        var profileId = GetOption(args, "--profile");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");
        if (string.IsNullOrWhiteSpace(profileId))
            validationErrors.Add("Missing required option: --profile <profile-id>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspacePostApplyValidationEnvelope
                {
                    Status = "failed",
                    Command = WorkspacePostApplyValidateCommand,
                    TraceId = null,
                    Summary = "Workspace post-apply validation could not be started.",
                    Data = new DisposableWorkspacePostApplyValidationData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        ProfileId = profileId ?? string.Empty,
                        ValidationRunId = string.Empty,
                        ValidationWorkspacePath = string.Empty,
                        ValidationWorkspacePrepared = false,
                        ValidationStatus = "failed",
                        ValidationSucceeded = false,
                        Blockers = validationErrors,
                        Errors = validationErrors
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            }
            else
            {
                foreach (var validationError in validationErrors)
                    error.WriteLine(validationError);
                PrintUsage(error);
            }

            return 2;
        }

        var service = new DisposableWorkspacePostApplyValidationService(
            new DisposableWorkspacePrepareService(new DisposableWorkspaceReadinessService()),
            new DisposableWorkspaceValidationService(new DisposableWorkspaceCommandService()));
        var result = await service.ValidateAsync(
            new DisposableWorkspacePostApplyValidationRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!,
                ProfileId = profileId!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspacePostApplyValidationEnvelope
        {
            Status = result.Status,
            Command = WorkspacePostApplyValidateCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceSourceReportAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceSourceReportEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceSourceReportCommand,
                    TraceId = null,
                    Summary = "Workspace source change report could not be started.",
                    Data = new DisposableWorkspaceSourceReportData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        SourceRepoMutated = false,
                        ApplyVerified = false,
                        SourceMatchesWorkspace = false,
                        PostApplyValidationSucceeded = false,
                        PostApplyValidationStatus = "failed",
                        Recommendation = "blocked",
                        Blockers = validationErrors,
                        Errors = validationErrors
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            }
            else
            {
                foreach (var validationError in validationErrors)
                    error.WriteLine(validationError);
                PrintUsage(error);
            }

            return 2;
        }

        var service = new DisposableWorkspaceSourceReportService();
        var result = await service.CreateAsync(
            new DisposableWorkspaceSourceReportRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceSourceReportEnvelope
        {
            Status = result.Status,
            Command = WorkspaceSourceReportCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceFailurePackageAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");
        var failedStage = GetOption(args, "--failed-stage");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");
        if (string.IsNullOrWhiteSpace(failedStage))
            validationErrors.Add("Missing required option: --failed-stage <stage>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceFailurePackageEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceFailurePackageCommand,
                    TraceId = null,
                    Summary = "Workspace failure package could not be started.",
                    Data = new DisposableWorkspaceFailurePackageData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        FailedStage = failedStage ?? string.Empty,
                        SourceRepoMutated = false,
                        ApplyCopyAttempted = false,
                        ApplyCopySucceeded = false,
                        ApplyVerified = false,
                        PostApplyValidationSucceeded = false,
                        FailureSeverity = "blocked",
                        RecommendedNextAction = "inspect_evidence_before_retry",
                        Errors = validationErrors
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            }
            else
            {
                foreach (var validationError in validationErrors)
                    error.WriteLine(validationError);
                PrintUsage(error);
            }

            return 2;
        }

        var service = new DisposableWorkspaceFailurePackageService();
        var result = await service.CreateAsync(
            new DisposableWorkspaceFailurePackageRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!,
                FailedStage = failedStage!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceFailurePackageEnvelope
        {
            Status = result.Status,
            Command = WorkspaceFailurePackageCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspacePrepareAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IDisposableWorkspacePrepareService? workspacePrepareService,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var sourceRepo = GetOption(args, "--source-repo");
        var workspaceRoot = GetOption(args, "--workspace-root");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(sourceRepo))
            validationErrors.Add("Missing required option: --source-repo <path>");
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            validationErrors.Add("Missing required option: --workspace-root <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspacePrepareEnvelope
                {
                    Status = "failed",
                    Command = WorkspacePrepareCommand,
                    TraceId = null,
                    Summary = "Disposable workspace prepare could not be started.",
                    Data = new DisposableWorkspacePrepareData
                    {
                        RunId = runId ?? string.Empty,
                        SourceRepo = sourceRepo ?? string.Empty,
                        WorkspaceRoot = workspaceRoot ?? string.Empty,
                        WorkspacePath = string.Empty,
                        ReadinessStatus = "not_available",
                        Prepared = false,
                        PreparationMethod = "copy",
                        FilesCopied = 0,
                        MetadataPath = null,
                        SourceRepoMutated = false,
                        Checks = []
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        workspacePrepareService ??= new DisposableWorkspacePrepareService(
            new DisposableWorkspaceReadinessService());
        var result = await workspacePrepareService.PrepareAsync(
            new DisposableWorkspacePrepareRequest
            {
                RunId = runId!,
                SourceRepo = sourceRepo!,
                WorkspaceRoot = workspaceRoot!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspacePrepareEnvelope
        {
            Status = result.Status,
            Command = WorkspacePrepareCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceRunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IDisposableWorkspaceCommandService? workspaceCommandService,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");
        var commandId = GetOption(args, "--command");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");
        if (string.IsNullOrWhiteSpace(commandId))
            validationErrors.Add("Missing required option: --command <command-id>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceCommandEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceRunCommand,
                    TraceId = null,
                    Summary = "Workspace command could not be started.",
                    Data = new DisposableWorkspaceCommandData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        CommandId = commandId ?? string.Empty,
                        WorkingDirectory = workspacePath ?? string.Empty,
                        ExitCode = -1,
                        Succeeded = false,
                        StdoutPath = null,
                        StderrPath = null,
                        CommandMetadataPath = null,
                        EvidencePaths = [],
                        Errors = validationErrors,
                        Warnings = []
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        workspaceCommandService ??= new DisposableWorkspaceCommandService();
        var result = await workspaceCommandService.RunAsync(
            new DisposableWorkspaceCommandRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!,
                CommandId = commandId!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceCommandEnvelope
        {
            Status = result.Status,
            Command = WorkspaceRunCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceValidateAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IDisposableWorkspaceValidationService? workspaceValidationService,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");
        var profileId = GetOption(args, "--profile");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");
        if (string.IsNullOrWhiteSpace(profileId))
            validationErrors.Add("Missing required option: --profile <profile-id>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceValidationEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceValidateCommand,
                    TraceId = null,
                    Summary = "Workspace validation could not be started.",
                    Data = new DisposableWorkspaceValidationData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        ProfileId = profileId ?? string.Empty,
                        Status = "failed",
                        Succeeded = false,
                        Steps = [],
                        EvidencePaths = [],
                        ValidationMetadataPath = null,
                        Errors = validationErrors,
                        Warnings = []
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        workspaceValidationService ??= new DisposableWorkspaceValidationService(
            new DisposableWorkspaceCommandService());
        var result = await workspaceValidationService.ValidateAsync(
            new DisposableWorkspaceValidationRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!,
                ProfileId = profileId!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceValidationEnvelope
        {
            Status = result.Status,
            Command = WorkspaceValidateCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspaceDiffAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IDisposableWorkspaceDiffService? workspaceDiffService,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspaceDiffEnvelope
                {
                    Status = "failed",
                    Command = WorkspaceDiffCommand,
                    TraceId = null,
                    Summary = "Workspace diff could not be started.",
                    Data = new DisposableWorkspaceDiffData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        Changed = false,
                        AddedFiles = [],
                        ModifiedFiles = [],
                        DeletedFiles = [],
                        UnchangedFileCount = 0,
                        DiffMetadataPath = null,
                        EvidencePaths = [],
                        Errors = validationErrors,
                        Warnings = []
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        workspaceDiffService ??= new DisposableWorkspaceDiffService();
        var result = await workspaceDiffService.DiffAsync(
            new DisposableWorkspaceDiffRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspaceDiffEnvelope
        {
            Status = result.Status,
            Command = WorkspaceDiffCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleWorkspacePromotionPackageAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var json = HasFlag(args, "--json");
        var runId = GetOption(args, "--run-id");
        var workspacePath = GetOption(args, "--workspace-path");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");
        if (string.IsNullOrWhiteSpace(workspacePath))
            validationErrors.Add("Missing required option: --workspace-path <path>");

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var envelope = new DisposableWorkspacePromotionPackageEnvelope
                {
                    Status = "failed",
                    Command = WorkspacePromotionPackageCommand,
                    TraceId = null,
                    Summary = "Workspace promotion package could not be started.",
                    Data = new DisposableWorkspacePromotionPackageData
                    {
                        RunId = runId ?? string.Empty,
                        WorkspacePath = workspacePath ?? string.Empty,
                        SourceRepo = string.Empty,
                        ValidationStatus = "not_available",
                        ValidationSucceeded = false,
                        DiffChanged = false,
                        AddedFiles = [],
                        ModifiedFiles = [],
                        DeletedFiles = [],
                        RequiresHumanApproval = true,
                        CanApplyToSourceRepo = false,
                        AutoPromotionAllowed = false,
                        Recommendation = "not_ready_missing_evidence",
                        RiskNotes = ["Promotion package evidence is missing or unsafe."],
                        WorkspaceMetadataPath = null,
                        ValidationMetadataPath = null,
                        DiffMetadataPath = null,
                        PromotionPackagePath = null,
                        EvidencePaths = [],
                        Errors = validationErrors,
                        Warnings = []
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        var service = new DisposableWorkspacePromotionPackageService();
        var result = await service.CreateAsync(
            new DisposableWorkspacePromotionPackageRequest
            {
                RunId = runId!,
                WorkspacePath = workspacePath!
            },
            cancellationToken);

        var resultEnvelope = new DisposableWorkspacePromotionPackageEnvelope
        {
            Status = result.Status,
            Command = WorkspacePromotionPackageCommand,
            TraceId = null,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(resultEnvelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(resultEnvelope.Summary);
        foreach (var resultError in resultEnvelope.Errors)
            error.WriteLine(resultError);
        foreach (var warning in resultEnvelope.Warnings)
            error.WriteLine($"Warning: {warning}");

        return result.ExitCode;
    }

    private static async Task<int> HandleAgentRunSupervisorAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        ISupervisorAgentRunService? supervisorAgentRunService,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        var json = HasFlag(args, "--json");

        var project = GetOption(args, "--project");
        var query = GetOption(args, "--query");
        var planPath = GetOption(args, "--plan");
        var runId = GetOption(args, "--run-id");
        var liveLlmRaw = GetOption(args, "--live-llm");

        var validationErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
            validationErrors.Add("Missing required option: --project <name>");
        if (string.IsNullOrWhiteSpace(query))
            validationErrors.Add("Missing required option: --query <text>");
        if (string.IsNullOrWhiteSpace(planPath))
            validationErrors.Add("Missing required option: --plan <path>");
        if (string.IsNullOrWhiteSpace(runId))
            validationErrors.Add("Missing required option: --run-id <id>");

        bool liveLlm;
        if (liveLlmRaw is null)
        {
            liveLlm = false;
        }
        else if (bool.TryParse(liveLlmRaw, out liveLlm))
        {
            // Value accepted.
        }
        else
        {
            validationErrors.Add("Invalid option value for --live-llm; expected true or false.");
        }

        if (validationErrors.Count > 0)
        {
            if (json)
            {
                var failedEnvelope = new AgentRunSupervisorContractEnvelope
                {
                    Status = "failed",
                    Command = AgentRunSupervisorCommand,
                    TraceId = null,
                    Summary = "Supervisor run could not be started.",
                    Data = new AgentRunSupervisorContractData
                    {
                        Agent = "SupervisorAgent",
                        RunId = runId ?? string.Empty,
                        Project = project ?? string.Empty,
                        Query = query ?? string.Empty,
                        PlanPath = planPath ?? string.Empty,
                        AgentStatus = "Failed",
                        ExitCode = 1,
                        Decision = "not_available",
                        DecisionReason = "Missing required options.",
                        Tester = new AgentRunSupervisorTesterData
                        {
                            RunId = string.Empty,
                            CommandStatus = "not_available",
                            RunStatus = "not_available",
                            Governance = new AgentRunSupervisorGovernanceData
                            {
                                Decision = "not_available",
                                ApprovalDecision = "not_available",
                                BlockedReason = null,
                                RequiresHumanApproval = false
                            },
                            Warnings = []
                        },
                        EvidencePaths = [],
                        CommandsRun = [],
                        Warnings = []
                    },
                    Errors = validationErrors,
                    Warnings = []
                };
                await output.WriteLineAsync(JsonSerializer.Serialize(failedEnvelope, JsonOptions));
                return 2;
            }

            foreach (var errorMessage in validationErrors)
                error.WriteLine(errorMessage);

            PrintUsage(error);
            return 2;
        }

        supervisorAgentRunService ??= await CreateSupervisorAgentRunServiceAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (supervisorAgentRunService is null)
            return 1;

        var result = await supervisorAgentRunService.RunAsync(
            new SupervisorAgentRunRequest
            {
                Project = project!,
                Query = query!,
                PlanPath = planPath!,
                RunId = runId!,
                LiveLlm = liveLlm
            },
            cancellationToken);

        var envelope = new AgentRunSupervisorContractEnvelope
        {
            Status = result.Status,
            Command = AgentRunSupervisorCommand,
            TraceId = result.TraceId,
            Summary = result.Summary,
            Data = result.Data,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions));
            return result.ExitCode;
        }

        await output.WriteLineAsync(envelope.Summary);
        if (envelope.Errors.Count > 0)
        {
            foreach (var summaryError in envelope.Errors)
                error.WriteLine(summaryError);
        }

        if (result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.Summary))
            error.WriteLine(result.Summary);

        return result.ExitCode;
    }

    private static async Task<ISupervisorAgentRunService?> CreateSupervisorAgentRunServiceAsync(
        string[] args,
        string apiBaseUrl,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return null;

        return new SupervisorAgentRunService(
            new AgentModelResolver(),
            FindRepositoryRoot(),
            new RunReportContractReader(client));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
                File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
                File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static async Task<int> HandleRunStreamAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        var runId = GetOption(args, "--run-id");
        if (string.IsNullOrWhiteSpace(runId))
        {
            error.WriteLine("Missing required option: --run-id <id>");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            await foreach (var runEvent in client.StreamRunEventsAsync(runId, cancellationToken))
            {
                await WriteJsonOrTextAsync(output, runEvent, HasFlag(args, "--json"), FormatRunEvent(runEvent));
            }

            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("runs stream", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleExerciseChatToBuildAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        string? input;
        try
        {
            input = await ResolveExerciseInputAsync(args, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine(ex.Message);
            return 2;
        }
        if (string.IsNullOrWhiteSpace(input))
        {
            error.WriteLine("Missing required option: --input <text> or --file <path>");
            return 2;
        }

        var title = GetOption(args, "--title");
        if (string.IsNullOrWhiteSpace(title))
            title = "CLI chat-to-build exercise";

        var scenarioId = GetOption(args, "--scenario-id") ?? string.Empty;
        var expectedOutput = GetOption(args, "--expected-output") ?? string.Empty;
        var reportRoot = GetOption(args, "--report-dir") ?? Path.Combine(Path.GetTempPath(), "IronDev", "process-proof");
        var reportDirectory = Path.Combine(reportRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(reportDirectory);

        var repoRoot = ResolveRepoRoot(GetOption(args, "--repo-root"));
        var beforeStatus = repoRoot is null ? null : await ReadGitStatusAsync(repoRoot, cancellationToken);
        var problems = new List<string>();
        var startedUtc = DateTimeOffset.UtcNow;

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        SaveDiscussionResponse? discussion = null;
        CreateTicketFromDocumentResponse? ticket = null;
        RunTicketReviewResponse? review = null;
        StartDisposableCodeRunResponse? run = null;
        RunStatusDto? runStatus = null;
        RunReviewPackage? reviewPackage = null;

        try
        {
            discussion = await client.SaveDiscussionAsync(
                projectId,
                new SaveDiscussionRequest { Title = title, Content = input },
                cancellationToken);

            ticket = await client.CreateTicketFromDocumentAsync(
                projectId,
                discussion.DocumentVersionId,
                new CreateTicketFromDocumentRequest(),
                cancellationToken);

            review = await client.ReviewTicketAsync(
                projectId,
                ticket.TicketId,
                new RunTicketReviewRequest { UseLiveModel = false },
                cancellationToken);

            run = await client.StartDisposableCodeRunAsync(
                projectId,
                ticket.TicketId,
                new StartDisposableCodeRunRequest
                {
                    ReviewId = review.ReviewId,
                    ScenarioId = string.IsNullOrWhiteSpace(scenarioId) ? review.Result.ScenarioId : scenarioId,
                    ExpectedOutput = expectedOutput
                },
                cancellationToken);

            runStatus = await client.GetRunAsync(run.RunId, cancellationToken);
            reviewPackage = await client.GetRunReviewPackageAsync(projectId, ticket.TicketId, run.RunId, cancellationToken);
        }
        catch (IronDevApiException ex)
        {
            problems.Add($"IronDev.Api request failed: {(int)ex.StatusCode} {ex.StatusCode}");
            if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
                problems.Add(ex.ResponseBody);
        }
        catch (HttpRequestException ex)
        {
            problems.Add($"HTTP request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            problems.Add($"Request timed out or was cancelled: {ex.Message}");
        }

        var afterStatus = repoRoot is null ? null : await ReadGitStatusAsync(repoRoot, cancellationToken);
        var mutationCheck = BuildRepoMutationCheck(repoRoot, beforeStatus, afterStatus);
        if (mutationCheck.Checked && !mutationCheck.Passed)
            problems.Add("Active repository status changed during the exercise.");

        if (review is not null && !review.Result.Decision.Proceed)
            problems.Add("Ticket review did not approve proceeding.");
        if (run is not null && !string.Equals(run.State, "PausedForApproval", StringComparison.OrdinalIgnoreCase))
            problems.Add($"Disposable run ended in state '{run.State}', not PausedForApproval.");
        if (reviewPackage is null)
            problems.Add("Run review package was not fetched.");
        else
        {
            if (reviewPackage.GeneratedFiles.Count == 0)
                problems.Add("Review package did not include generated files.");
            if (reviewPackage.CommandEvidence.Count == 0)
                problems.Add("Review package did not include command evidence.");
            if (reviewPackage.OutputVerifications.Count == 0 && !reviewPackage.OutputVerification.Verified)
                problems.Add("Review package did not include a verified output check.");
            if (reviewPackage.OutputVerifications.Count > 0 && reviewPackage.OutputVerifications.Any(verification => !verification.Verified))
                problems.Add("One or more output verifications failed.");
        }

        var report = new ChatToBuildProofReport
        {
            StartedUtc = startedUtc,
            CompletedUtc = DateTimeOffset.UtcNow,
            ApiBaseUrl = apiBaseUrl,
            ProjectId = projectId,
            Input = input,
            DiscussionTitle = title,
            DocumentId = discussion?.DocumentId,
            DocumentVersionId = discussion?.DocumentVersionId,
            TicketId = ticket?.TicketId,
            SourceDocumentVersionId = ticket?.SourceDocumentVersionId,
            ReviewId = review?.ReviewId,
            ScenarioId = review?.Result.ScenarioId ?? scenarioId,
            ReviewProceed = review?.Result.Decision.Proceed,
            ReviewRecommendedNextStep = review?.Result.Decision.RecommendedNextStep,
            RunId = run?.RunId,
            RunState = run?.State,
            RunStatus = runStatus?.Status,
            ReviewPackageAvailable = reviewPackage is not null,
            GeneratedFileCount = reviewPackage?.GeneratedFiles.Count ?? 0,
            CommandEvidenceCount = reviewPackage?.CommandEvidence.Count ?? 0,
            OutputVerificationCount = reviewPackage?.OutputVerifications.Count > 0
                ? reviewPackage.OutputVerifications.Count
                : reviewPackage?.OutputVerification.Verified == true ? 1 : 0,
            AllOutputVerified = reviewPackage is null
                ? false
                : reviewPackage.OutputVerifications.Count > 0
                    ? reviewPackage.OutputVerifications.All(verification => verification.Verified)
                    : reviewPackage.OutputVerification.Verified,
            CodeStandardsStatus = reviewPackage?.CodeStandards.Status,
            FileSetHash = reviewPackage?.FileSetHash,
            EventCount = reviewPackage?.Events.Count ?? 0,
            MutationCheck = mutationCheck,
            Problems = problems
        };

        var jsonPath = Path.Combine(reportDirectory, "report.json");
        var markdownPath = Path.Combine(reportDirectory, "report.md");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdownReport(report), cancellationToken);

        if (HasFlag(args, "--json"))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            await output.WriteLineAsync(FormatExerciseReport(report, reportDirectory));
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private static async Task<int> HandleScenarioListAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var scenarios = await client.GetBuildScenariosAsync(projectId, cancellationToken);
            if (HasFlag(args, "--json"))
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(scenarios, JsonOptions));
            }
            else
            {
                foreach (var scenario in scenarios.OrderBy(item => item.ScenarioId, StringComparer.OrdinalIgnoreCase))
                    await output.WriteLineAsync($"{scenario.ScenarioId}: {scenario.Name} ({scenario.RuntimeProfileId})");
            }

            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("scenario list", ex, error);
            return 1;
        }
    }

    private static async Task<int> HandleScenarioRunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        var scenarioId = GetScenarioIdArgument(args);
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            error.WriteLine("Missing required scenario id: irondev scenario run <scenario-id> --project-id <id>");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        BuildScenario? scenario;
        try
        {
            var scenarios = await client.GetBuildScenariosAsync(projectId, cancellationToken);
            scenario = scenarios.FirstOrDefault(item => string.Equals(item.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("scenario run", ex, error);
            return 1;
        }

        if (scenario is null)
        {
            error.WriteLine($"Unknown scenario id: {scenarioId}");
            return 2;
        }

        var forwarded = BuildScenarioExerciseArgs(args, projectId, apiBaseUrl, scenario);
        return await HandleExerciseChatToBuildAsync(forwarded, output, error, handler, cancellationToken);
    }

    private static async Task<int> HandleScenarioReportAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        if (!TryGetLongOption(args, "--ticket-id", out var ticketId))
        {
            error.WriteLine("Missing or invalid required option: --ticket-id <id>");
            return 2;
        }

        var runId = GetRunIdArgument(args);
        if (string.IsNullOrWhiteSpace(runId))
        {
            error.WriteLine("Missing required run id: irondev scenario report <run-id> --project-id <id> --ticket-id <id>");
            return 2;
        }

        var client = await CreateReadyApiClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (client is null)
            return 1;

        try
        {
            var package = await client.GetRunReviewPackageAsync(projectId, ticketId, runId, cancellationToken);
            await WriteJsonOrTextAsync(
                output,
                package,
                HasFlag(args, "--json"),
                $"{package.RunId}: {package.State}; files={package.GeneratedFiles.Count}; commands={package.CommandEvidence.Count}; verifications={package.OutputVerifications.Count}");
            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("scenario report", ex, error);
            return 1;
        }
    }

    private static async Task<IIronDevApiClient?> CreateReadyApiClientAsync(
        string[] args,
        string apiBaseUrl,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var token = ResolveToken(GetOption(args, "--token"), ReadEnvironment(), GetOption(args, "--config"));
        var client = IronDevApiClientFactory.Create(apiBaseUrl, token, handler);
        var health = await CheckHealthAsync(client, cancellationToken);
        if (!health)
        {
            error.WriteLine($"IronDev.Api is not reachable at {apiBaseUrl}.");
            error.WriteLine("Start it with:");
            error.WriteLine("dotnet run --project IronDev.Api");
            return null;
        }

        return client;
    }

    private static async Task WriteJsonOrTextAsync<T>(TextWriter output, T value, bool json, string text)
    {
        if (json)
            await output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions));
        else
            await output.WriteLineAsync(text);
    }

    private static async Task<bool> CheckHealthAsync(IIronDevApiClient client, CancellationToken cancellationToken)
    {
        try
        {
            return await client.CheckHealthAsync(cancellationToken);
        }
        catch (IronDevApiException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static void WriteApiError(string operation, IronDevApiException ex, TextWriter error)
    {
        var prefix = $"IronDev.Api {operation} failed with {(int)ex.StatusCode} {ex.StatusCode}.";
        if ((int)ex.StatusCode == 401)
            prefix += $"{Environment.NewLine}Authenticate through IronDev.Api and provide a tenant-scoped JWT with --token or IRONDEV_API_TOKEN.";

        error.WriteLine(string.IsNullOrWhiteSpace(ex.ResponseBody) ? prefix : $"{prefix}{Environment.NewLine}{ex.ResponseBody}");
    }

    private static string FormatRunStatus(RunStatusDto status) =>
        $"{status.RunId}: {status.Status} - {status.Title}";

    private static string FormatRunReport(RunReportDto report)
    {
        var detail = report.Report;
        if (detail is null)
            return FormatRunStatus(report.Status);

        return $"{report.Status.RunId}: {report.Status.Status} - {detail.Summary}";
    }

    private static string FormatRunEvent(RunEventDto runEvent) =>
        $"{runEvent.TimestampUtc:O} {runEvent.EventType} {runEvent.RunId}: {runEvent.Message}";

    private static string FormatTicketBuildRun(TicketBuildRunDto run) =>
        $"{run.RunId}: {run.Status} at {run.CurrentNode} for ticket {run.TicketId}";

    private static string FormatExerciseReport(ChatToBuildProofReport report, string reportDirectory)
    {
        var status = report.Problems.Count == 0 ? "PASS" : "FAIL";
        var builder = new StringBuilder();
        builder.AppendLine($"{status} chat-to-build process exercise");
        builder.AppendLine($"  Project: {report.ProjectId}");
        builder.AppendLine($"  Document: {report.DocumentId?.ToString() ?? "n/a"} / version {report.DocumentVersionId?.ToString() ?? "n/a"}");
        builder.AppendLine($"  Ticket: {report.TicketId?.ToString() ?? "n/a"}");
        builder.AppendLine($"  Review: {report.ReviewId ?? "n/a"} proceed={report.ReviewProceed?.ToString() ?? "n/a"}");
        builder.AppendLine($"  Scenario: {report.ScenarioId ?? "n/a"}");
        builder.AppendLine($"  Run: {report.RunId ?? "n/a"} state={report.RunState ?? "n/a"} status={report.RunStatus ?? "n/a"}");
        builder.AppendLine($"  Evidence: files={report.GeneratedFileCount}, commands={report.CommandEvidenceCount}, verifications={report.OutputVerificationCount}, events={report.EventCount}");
        builder.AppendLine($"  Repo untouched: {(report.MutationCheck.Checked ? report.MutationCheck.Passed.ToString() : "not checked")}");
        builder.AppendLine($"  Report: {reportDirectory}");

        if (report.Problems.Count > 0)
        {
            builder.AppendLine("  Problems:");
            foreach (var problem in report.Problems)
                builder.AppendLine($"    - {problem}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildMarkdownReport(ChatToBuildProofReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# IronDev Chat-To-Build Proof Report");
        builder.AppendLine();
        builder.AppendLine($"**Result:** {(report.Problems.Count == 0 ? "PASS" : "FAIL")}");
        builder.AppendLine();
        builder.AppendLine("## Input");
        builder.AppendLine();
        builder.AppendLine($"- Project: `{report.ProjectId}`");
        builder.AppendLine($"- Title: {report.DiscussionTitle}");
        builder.AppendLine($"- Scenario: `{report.ScenarioId ?? "n/a"}`");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(report.Input.Trim());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Created");
        builder.AppendLine();
        builder.AppendLine($"- Document: `{report.DocumentId?.ToString() ?? "n/a"}`");
        builder.AppendLine($"- Document version: `{report.DocumentVersionId?.ToString() ?? "n/a"}`");
        builder.AppendLine($"- Ticket: `{report.TicketId?.ToString() ?? "n/a"}`");
        builder.AppendLine($"- Review: `{report.ReviewId ?? "n/a"}`");
        builder.AppendLine($"- Run: `{report.RunId ?? "n/a"}`");
        builder.AppendLine();
        builder.AppendLine("## Plan");
        builder.AppendLine();
        builder.AppendLine($"- Proceed: `{report.ReviewProceed?.ToString() ?? "n/a"}`");
        builder.AppendLine($"- Recommended next step: {report.ReviewRecommendedNextStep ?? "n/a"}");
        builder.AppendLine();
        builder.AppendLine("## Execution");
        builder.AppendLine();
        builder.AppendLine($"- Run state: `{report.RunState ?? "n/a"}`");
        builder.AppendLine($"- Run status: `{report.RunStatus ?? "n/a"}`");
        builder.AppendLine($"- Generated files: `{report.GeneratedFileCount}`");
        builder.AppendLine($"- Command evidence: `{report.CommandEvidenceCount}`");
        builder.AppendLine($"- Output verifications: `{report.OutputVerificationCount}`");
        builder.AppendLine($"- All output verified: `{report.AllOutputVerified}`");
        builder.AppendLine($"- Code standards: `{report.CodeStandardsStatus ?? "n/a"}`");
        builder.AppendLine($"- Events: `{report.EventCount}`");
        builder.AppendLine();
        builder.AppendLine("## Safety");
        builder.AppendLine();
        builder.AppendLine($"- Repo mutation check: `{(report.MutationCheck.Checked ? report.MutationCheck.Passed ? "passed" : "failed" : "not checked")}`");
        builder.AppendLine($"- Repo root: `{report.MutationCheck.RepositoryRoot ?? "n/a"}`");
        builder.AppendLine($"- Mutation note: {report.MutationCheck.Message}");
        builder.AppendLine();
        builder.AppendLine("## Problems Found");
        builder.AppendLine();
        if (report.Problems.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var problem in report.Problems)
                builder.AppendLine($"- {problem}");
        }

        return builder.ToString();
    }

    private static async Task<string?> ResolveExerciseInputAsync(string[] args, CancellationToken cancellationToken)
    {
        var input = GetOption(args, "--input");
        if (!string.IsNullOrWhiteSpace(input))
            return input;

        var file = GetOption(args, "--file");
        if (string.IsNullOrWhiteSpace(file))
            return null;

        return File.Exists(file)
            ? await File.ReadAllTextAsync(file, cancellationToken)
            : throw new FileNotFoundException($"Exercise input file was not found: {file}", file);
    }

    private static string? GetScenarioIdArgument(string[] args) =>
        args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2]
            : GetOption(args, "--scenario-id");

    private static string? GetRunIdArgument(string[] args) =>
        args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2]
            : GetOption(args, "--run-id");

    private static string[] BuildScenarioExerciseArgs(
        string[] originalArgs,
        int projectId,
        string apiBaseUrl,
        BuildScenario scenario)
    {
        var forwarded = new List<string>
        {
            "exercise",
            "chat-to-build",
            "--project-id",
            projectId.ToString(),
            "--input",
            scenario.DiscussionText,
            "--title",
            $"{scenario.Name} {DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            "--scenario-id",
            scenario.ScenarioId,
            "--api-base-url",
            apiBaseUrl
        };

        foreach (var option in new[] { "--token", "--config", "--report-dir", "--repo-root" })
        {
            var value = GetOption(originalArgs, option);
            if (!string.IsNullOrWhiteSpace(value))
            {
                forwarded.Add(option);
                forwarded.Add(value);
            }
        }

        if (HasFlag(originalArgs, "--json"))
            forwarded.Add("--json");

        return forwarded.ToArray();
    }

    private static string? ResolveRepoRoot(string? requestedRoot)
    {
        if (!string.IsNullOrWhiteSpace(requestedRoot))
            return Directory.Exists(requestedRoot) ? Path.GetFullPath(requestedRoot) : null;

        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    private static async Task<string?> ReadGitStatusAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("status");
        startInfo.ArgumentList.Add("--porcelain");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? await stdoutTask : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    private static RepoMutationCheck BuildRepoMutationCheck(string? repoRoot, string? beforeStatus, string? afterStatus)
    {
        if (repoRoot is null)
        {
            return new RepoMutationCheck
            {
                Checked = false,
                Passed = false,
                Message = "No Git repository root was found from the current directory. Use --repo-root to enable this check."
            };
        }

        if (beforeStatus is null || afterStatus is null)
        {
            return new RepoMutationCheck
            {
                Checked = false,
                Passed = false,
                RepositoryRoot = repoRoot,
                Message = "Git status could not be read before and after the exercise."
            };
        }

        var passed = string.Equals(beforeStatus, afterStatus, StringComparison.Ordinal);
        return new RepoMutationCheck
        {
            Checked = true,
            Passed = passed,
            RepositoryRoot = repoRoot,
            BeforeStatus = beforeStatus,
            AfterStatus = afterStatus,
            Message = passed
                ? "Git status was unchanged during the exercise."
                : "Git status changed during the exercise."
        };
    }

    private static string? ResolveToken(
        string? argumentValue,
        IReadOnlyDictionary<string, string?> environment,
        string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(argumentValue))
            return argumentValue;

        if (environment.TryGetValue("IRONDEV_API_TOKEN", out var envToken) &&
            !string.IsNullOrWhiteSpace(envToken))
        {
            return envToken;
        }

        return ReadConfig(configPath).ApiToken;
    }

    private static IronDevCliConfig ReadConfig(string? configPath)
    {
        var path = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(Environment.CurrentDirectory, "irondev.cli.json")
            : configPath;

        if (!File.Exists(path))
            return new IronDevCliConfig();

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("IronDev", out var ironDev))
            return new IronDevCliConfig();

        return new IronDevCliConfig
        {
            ApiBaseUrl = TryGetString(ironDev, "ApiBaseUrl"),
            ApiToken = TryGetString(ironDev, "ApiToken")
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyDictionary<string, string?> ReadEnvironment() =>
        Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);

    private static string NormalizeBaseUrl(string value) =>
        value.Trim().TrimEnd('/');

    private static bool IsCommand(string[] args, string first, string second) =>
        args.Length >= 2 &&
        string.Equals(args[0], first, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[1], second, StringComparison.OrdinalIgnoreCase);

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetIntOption(string[] args, string name, out int value) =>
        int.TryParse(GetOption(args, name), out value) && value > 0;

    private static bool TryGetLongOption(string[] args, string name, out long value) =>
        long.TryParse(GetOption(args, name), out value) && value > 0;

    private static void PrintUsage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  irondev --help");
        error.WriteLine("  irondev --version");
        error.WriteLine("  irondev config show [--api-base-url <url>] [--token <token>] [--output text|json]");
        error.WriteLine("  irondev api ping --api-base-url <url> [--token <token>] [--output text|json]");
        error.WriteLine("  irondev agent-runs list --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev agent-runs get <agentRunId> --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev agent-runs audit <agentRunId> --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev critic review create --project-id <id> --target-agent-run-id <id> [--review-kind <kind>] [--focus <text>] [--reason <text>] [--evidence-ref <ref>] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev critic review get <agentRunId> --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev memory-improvements create --project-id <id> --target-agent-run-id <id> [--focus <text>] [--reason <text>] [--evidence-ref <ref>] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev memory-improvements get <agentRunId> --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev tool-requests create --project-id <id> --request-kind <kind> --tool-kind <kind> --run-id <id> --reason <text> [--summary <text>] [--evidence-ref <ref>] [--input-ref <ref>] [--policy-ref <ref>] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev tool-requests get <toolRequestId> --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev dogfood-loops create --project-id <id> --summary <text> --goal <text> [--observation <text>] [--blocked-reason <text>] [--agent-run-id <id>] [--critic-review-run-id <id>] [--memory-improvement-run-id <id>] [--tool-request-id <id>] [--tool-gate-decision-id <id>] [--evidence-ref <ref>] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev dogfood-loops get <dogfoodLoopId> --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev workflow inspect runs --project <id> [--take 100] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev workflow inspect run --project <id> --run <workflowRunId> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev workflow inspect steps --project <id> --run <workflowRunId> [--take 100] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev workflow inspect step --project <id> --run <workflowRunId> --step <workflowRunStepId> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev workflow inspect checkpoints --project <id> --run <workflowRunId> [--take 100] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev workflow inspect checkpoint --project <id> --run <workflowRunId> --checkpoint <workflowCheckpointId> [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev workflow apply-preview --workflow-run <workflowRunId> --workflow-step <workflowStepId> [--controlled-apply-plan <id>] [--take-dry-runs 10] [--no-dry-runs] [--output text|json] [--api-base-url <url>] [--token <token>]");
        error.WriteLine("  irondev governance inventory [--json]");
        error.WriteLine("  irondev governance classify --action <action-kind> [--json]");
        error.WriteLine("  irondev patch start --repo <repo-path> --task <task-file> (--test <command> | --test-profile <name>) [--allow <glob>] [--forbid <glob>] [--runs-root <path>] [--workspace-root <path>] [--run-id <id>] [--json]");
        error.WriteLine("  irondev patch finish --run <run-id-or-path> [--runs-root <path>] [--test <command> | --test-profile <name>] [--skip-test] [--json]");
        error.WriteLine("  irondev patch test --run <run-id-or-path> [--runs-root <path>] [--test <command> | --test-profile <name>] [--json]");
        error.WriteLine("  irondev patch tools --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch assist --run <run-id-or-path> [--provider deterministic|configured] [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch refine --run <run-id-or-path> [--max-iterations <n>] [--provider deterministic|configured] [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch review --run <run-id-or-path> [--provider deterministic|configured] [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch ai --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch status --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch list [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch cleanup --run <run-id-or-path> [--runs-root <path>] (--delete-workspace | --delete-run) [--json]");
        error.WriteLine("  irondev patch cleanup --older-than-days <n> --delete-workspaces [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch governance --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch governance --inventory [--json]");
        error.WriteLine("  irondev memory propose --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev memory proposals --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev memory promote --proposal <proposal-id-or-file> --conscience-decision <decision.json> --thought-ledger-ref <ref> [--memory-root <path>] [--runs-root <path>] [--scope run|project|portable] [--json]");
        error.WriteLine("  irondev memory list [--memory-root <path>] [--json]");
        error.WriteLine("  irondev memory show --key <memory-key> [--memory-root <path>] [--json]");
        error.WriteLine("  irondev memory plan --run <run-id-or-path> --memory-root <path> --project <id> --task <task.md-or-text> [--json]");
        error.WriteLine("  irondev plan memory-context --run <run-id-or-path> --task <task.md> [--project-id <id>] [--memory-root <path>] [--json]");
        error.WriteLine("  irondev plan context --run <run-id-or-path> --task <task.md> [--json]");
        error.WriteLine("  irondev plan propose --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev plan review --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev plan status --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev product-hardening dogfood --run <path> --project <id> --task <task.md-or-text> [--simulate-missing-artifact <name>] [--json]");
        error.WriteLine("  irondev commit-package request --run <run-id-or-path> --source-repo <path> [--json]");
        error.WriteLine("  irondev commit-package manifest --run <run-id-or-path> --source-repo <path> [--json]");
        error.WriteLine("  irondev commit-package evidence --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev commit-package message --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev commit-package review --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev commit-package status --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev pull-request request --run <run-id-or-path> --repo <owner/name> --base <branch> --head <branch> --expected-head <sha> [--json]");
        error.WriteLine("  irondev pull-request validate --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev pull-request text --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev pull-request gate --run <run-id-or-path> --decision <decision.json> --thought-ledger-ref <ref> [--json]");
        error.WriteLine("  irondev pull-request create-draft --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev pull-request status --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback request --run <run-id-or-path> --repo <owner/name> --pr <number> --expected-head <sha> [--json]");
        error.WriteLine("  irondev feedback ci --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback review --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback classify --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback plan --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback readiness --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback package --pr <number> --head <sha> --out <path> [--run <run-id-or-path>] [--repo <owner/name>] [--json]");
        error.WriteLine("  irondev feedback package --from-receipt <validation-receipt.json> --out <path> [--json]");
        error.WriteLine("  irondev feedback package --status --package <feedback-remediation-package.json> [--json]");
        error.WriteLine("  irondev feedback package --records --package <feedback-remediation-package.json> [--json]");
        error.WriteLine("  irondev feedback status --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev feedback-patch propose --package <feedback-package.json> --out <path> [--pr <number>] [--head <sha>] [--base <sha>] [--candidate <id>] [--json]");
        error.WriteLine("  irondev feedback-patch inspect --proposal <proposal.json> [--json]");
        error.WriteLine("  irondev feedback-patch status --proposal <proposal.json> [--json]");
        error.WriteLine("  irondev feedback-patch records --proposal <proposal.json> [--json]");
        error.WriteLine("  irondev pr-update package --pr <number> --proposal <proposal.json> --validation <receipt.json> --out <path> --repo <owner/name> --pr-url <url> --state <open|closed> --draft <true|false> --target-branch <branch> --expected-head <sha> --base-branch <branch> --base-sha <sha> [--source-apply <receipt.json>] [--expected-post-update-head <sha>] [--expected-diff-hash <sha256:...>] [--commit-allowed] [--push-allowed] [--target-remote origin] [--json]");
        error.WriteLine("  irondev pr-update inspect --package <package.json> [--json]");
        error.WriteLine("  irondev pr-update status --package <package.json> [--json]");
        error.WriteLine("  irondev pr-update records --package <package.json> [--json]");
        error.WriteLine("  irondev pr-branch-update execute --package <package.json> --workspace <repo-path> --out <path> [--remote origin] [--expected-pr <number>] [--json]");
        error.WriteLine("  irondev pr-branch-update status --receipt <receipt.json> [--json]");
        error.WriteLine("  irondev pr-branch-update records --receipt <receipt.json> [--json]");
        error.WriteLine("  irondev pr-branch-update rollback-plan --receipt <receipt.json> [--json]");
        error.WriteLine("  irondev ready package --pr <number> --repo <owner/name> --state <open|closed> --draft <true|false> --head <sha> --observed-head <sha> --base <branch> --base-sha <sha> --branch <branch> --as-receipt <receipt.json> --validation <receipt.json> --phase-receipt <receipt.md> --out <path> [--json]");
        error.WriteLine("  irondev ready inspect --package <ready-package.json> [--json]");
        error.WriteLine("  irondev ready status --package <ready-package.json> [--json]");
        error.WriteLine("  irondev ready records --package <ready-package.json> [--json]");
        error.WriteLine("  irondev ready execute --package <ready-for-review-package.json> --repo <owner/name> --pr <number> --observed-head <sha> --out <path> [--json]");
        error.WriteLine("  irondev ready execution-status --receipt <ready-for-review-execution-receipt.json> [--json]");
        error.WriteLine("  irondev ready execution-records --receipt <ready-for-review-execution-receipt.json> [--json]");
        error.WriteLine("  irondev merge-release request --run <run-id-or-path> --repo <owner/name> --pr <number> --expected-head <sha> [--json]");
        error.WriteLine("  irondev merge-release merge-evidence --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev merge-release release-evidence --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev merge-release boundary-map --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev merge-release records --run <run-id-or-path> [--reviewed-by <name>] [--json]");
        error.WriteLine("  irondev merge-release status --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev validate plan [--changed-files <path>] [--base <ref>] [--head <ref>] [--phase <name>] [--block <name>] [--out <path>] [--json]");
        error.WriteLine("  irondev validate run --lane <known-lane> --artifacts <dir> [--timeout-seconds <n>] [--cwd <path>] [--json]");
        error.WriteLine("  irondev validate run --ad-hoc --artifacts <dir> --command <exe> [--arg <arg>]... [--timeout-seconds <n>] [--cwd <path>] [--json]");
        error.WriteLine("  irondev validate lanes [--json]");
        error.WriteLine("  irondev validate receipt (--path <receipt.json> | --artifacts <dir> [--last]) [--json]");
        error.WriteLine("  irondev validate inventory [--json]");
        error.WriteLine("  irondev source-apply approval-template --run <run-id-or-path> --out <approval.json> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply prepare --run <run-id-or-path> [--approval <approval.json>] [--apply-root <path>] [--runs-root <path>] [--json]");
        error.WriteLine("  irondev source-apply status --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev ticket create --project-id <id> --file <ticket.json> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket list --project-id <id> [--take 50] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket show --project-id <id> --ticket-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket import-github-issue --project-id <id> --file <github-issue.json> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev tickets build --project-id <id> --ticket-id <id> [--max-retries 3] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev runs status --run-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev runs report --run-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev runs stream --run-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev workspace check --run-id <id> --source-repo <path> --workspace-root <path> [--json]");
        error.WriteLine("  irondev workspace prepare --run-id <id> --source-repo <path> --workspace-root <path> [--json]");
        error.WriteLine("  irondev workspace run --run-id <id> --workspace-path <path> --command <dotnet-build|dotnet-test> [--json]");
        error.WriteLine("  irondev workspace validate --run-id <id> --workspace-path <path> --profile <dotnet-build-test> [--json]");
        error.WriteLine("  irondev workspace diff --run-id <id> --workspace-path <path> [--json]");
        error.WriteLine("  irondev workspace promotion-package --run-id <id> --workspace-path <path> [--json]");
        error.WriteLine("  irondev workspace promotion-approval --run-id <id> --workspace-path <path> --decision <approved|rejected> --approved-by <name-or-id> --reason <text> [--json]");
        error.WriteLine("  irondev workspace apply-preflight --run-id <id> --workspace-path <path> [--json]");
        error.WriteLine("  irondev workspace apply-dry-run --run-id <id> --workspace-path <path> [--json]");
        error.WriteLine("  irondev workspace apply-copy --run-id <id> --workspace-path <path> [--json]");
        error.WriteLine("  irondev workspace apply-verify --run-id <id> --workspace-path <path> [--json]");
        error.WriteLine("  irondev workspace post-apply-validate --run-id <id> --workspace-path <path> --profile <dotnet-build-test> [--json]");
        error.WriteLine("  irondev workspace source-report --run-id <id> --workspace-path <path> [--json]");
        error.WriteLine("  irondev workspace failure-package --run-id <id> --workspace-path <path> --failed-stage <stage> [--json]");
        error.WriteLine("  irondev agent run supervisor --project <name> --query <text> --plan <path> --run-id <id> [--live-llm true|false] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev exercise chat-to-build --project-id <id> (--input <text> | --file <path>) [--title <title>] [--scenario-id <id>] [--expected-output <text>] [--report-dir <path>] [--repo-root <path>] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev scenario list --project-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev scenario run <scenario-id> --project-id <id> [--report-dir <path>] [--repo-root <path>] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev scenario report <run-id> --project-id <id> --ticket-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine();
        error.WriteLine("  irondev chat-probe run --project-id <id> [--count 10] [--seed 42] [--scenario <id>] [--persona <name>] [--report-dir <path>] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev chat-probe list-scenarios [--json]");
        error.WriteLine("  irondev chat-probe list-personas [--json]");
        error.WriteLine("  irondev chat-probe export-failures --run-id <id> [--output-dir <path>] [--json]");
        error.WriteLine();
        error.WriteLine("Default API base URL: http://localhost:5000");
        error.WriteLine("Overrides: --api-base-url, IRONDEV_API_BASE_URL, irondev.cli.json");
    }

    // ?????? Chat Probe CLI handlers ????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????

    private static readonly JsonSerializerOptions ProbeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    private static string DefaultProbeReportDir =>
        Path.Combine("tools", "dogfood", "chat-probe-runs");

    private static async Task<int> HandleChatProbeRunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = ResolveApiBaseUrl(GetOption(args, "--api-base-url"), ReadEnvironment(), GetOption(args, "--config"));
        var token      = ResolveToken(GetOption(args, "--token"), ReadEnvironment(), GetOption(args, "--config"));
        var json       = HasFlag(args, "--json");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
        {
            error.WriteLine("Missing or invalid required option: --project-id <id>");
            return 2;
        }

        var scenarioId = GetOption(args, "--scenario");
        var personaName = GetOption(args, "--persona");
        var reportDir  = GetOption(args, "--report-dir") ?? DefaultProbeReportDir;
        var seed       = TryGetIntOption(args, "--seed", out var parsedSeed) ? parsedSeed : 0;
        var count      = TryGetIntOption(args, "--count", out var parsedCount) ? parsedCount : 10;

        // Resolve scenario(s)
        IReadOnlyList<ProbeScenario> scenarios;
        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var single = ProbeScenarioCatalog.GetById(scenarioId);
            if (single is null)
            {
                error.WriteLine($"Unknown scenario: {scenarioId}. Run 'irondev chat-probe list-scenarios' to see available scenarios.");
                return 2;
            }
            scenarios = [single];
            count = 1;
        }
        else
        {
            scenarios = ProbeScenarioCatalog.GetBatch(count, seed).Select(b => b.Scenario).ToList();
        }

        // Resolve persona (optional ??? null = rotate from seed)
        PersonaProfile? fixedPersona = null;
        if (!string.IsNullOrWhiteSpace(personaName))
        {
            fixedPersona = PersonaEngine.ParseName(personaName);
            if (fixedPersona is null)
            {
                error.WriteLine($"Unknown persona: {personaName}. Run 'irondev chat-probe list-personas' to see available personas.");
                return 2;
            }
        }

        // Build chat session adapter (wraps IChatApiClient into the IChatProbeSession port)
        var chatClient  = ChatApiClientFactory.Create(apiBaseUrl, token, handler);
        var probeSession = new ChatProbeSessionAdapter(chatClient);

        var driver   = new ChatProbeDriver();
        var writer   = new ProbeTranscriptWriter();
        var options  = new ProbeRunOptions();
        var batchId  = $"batch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var runs     = new List<ProbeRunResult>();
        var startedAt = DateTimeOffset.UtcNow;

        if (!json)
        {
            output.WriteLine($"Running {scenarios.Count} chat probe(s)...");
            output.WriteLine($"Report dir: {Path.GetFullPath(reportDir)}");
            output.WriteLine();
        }

        for (var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var persona  = fixedPersona ?? PersonaEngine.GetFromSeed(seed + i);

            if (!json)
                output.WriteLine($"[{i + 1}/{scenarios.Count}] {scenario.Name} / {persona.Name}");

            try
            {
                var result = await driver.RunAsync(probeSession, projectId, scenario, persona, options, cancellationToken);
                runs.Add(result);
                await writer.WriteRunAsync(result, Path.Combine(reportDir, batchId, "runs"), cancellationToken);

                if (!json)
                {
                    var icon = result.Outcome switch
                    {
                        ProbeRunOutcome.Pass     => "???",
                        ProbeRunOutcome.SoftFail => "???",
                        ProbeRunOutcome.HardFail => "???",
                        _                        => "?"
                    };
                    output.WriteLine($"  {icon} {result.Outcome} ??? {result.AllFailures.Count} failure(s)");
                }
            }
            catch (Exception ex)
            {
                error.WriteLine($"  Run failed with exception: {ex.Message}");
            }
        }

        // Build batch summary
        var failureCounts = runs
            .SelectMany(r => r.AllFailures)
            .GroupBy(f => f.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var batch = new ProbeBatchSummary
        {
            BatchId      = batchId,
            TotalRuns    = runs.Count,
            Passed       = runs.Count(r => r.Outcome == ProbeRunOutcome.Pass),
            SoftFailed   = runs.Count(r => r.Outcome == ProbeRunOutcome.SoftFail),
            HardFailed   = runs.Count(r => r.Outcome == ProbeRunOutcome.HardFail),
            FailureCounts = failureCounts,
            Runs         = runs,
            StartedUtc   = startedAt,
            CompletedUtc = DateTimeOffset.UtcNow
        };

        // Write batch summary files
        await writer.WriteBatchAsync(batch, reportDir, cancellationToken);

        if (json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(batch, ProbeJsonOptions));
        }
        else
        {
            output.WriteLine();
            output.WriteLine($"Runs:      {batch.TotalRuns}");
            output.WriteLine($"Pass:      {batch.Passed}");
            output.WriteLine($"Soft fail: {batch.SoftFailed}");
            output.WriteLine($"Hard fail: {batch.HardFailed}");

            if (batch.FailureCounts.Count > 0)
            {
                output.WriteLine();
                output.WriteLine("Top failures:");
                output.WriteLine(batch.FormatTopFailures());
            }

            output.WriteLine();
            output.WriteLine($"Reports: {Path.GetFullPath(Path.Combine(reportDir, batchId))}");
        }

        return batch.HardFailed > 0 ? 1 : 0;
    }

    private static Task<int> HandleChatProbeListScenariosAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var scenarios = ProbeScenarioCatalog.GetAll();
        if (HasFlag(args, "--json"))
        {
            var simple = scenarios.Select(s => new
            {
                s.ScenarioId,
                s.Name,
                Category     = s.Category.ToString(),
                s.ProjectIdea,
                StepCount    = s.Steps.Count
            });
            output.WriteLine(JsonSerializer.Serialize(simple, ProbeJsonOptions));
        }
        else
        {
            output.WriteLine($"Chat probe scenarios ({scenarios.Count}):");
            output.WriteLine();
            foreach (var s in scenarios)
                output.WriteLine($"  {s.ScenarioId,-30} {s.Category,-14} {s.Steps.Count} steps  ??? {s.ProjectIdea}");
        }

        return Task.FromResult(0);
    }

    private static Task<int> HandleChatProbeListPersonasAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var personas = PersonaEngine.GetAll();
        if (HasFlag(args, "--json"))
        {
            var simple = personas.Select(p => new
            {
                Id          = p.Id.ToString(),
                p.Name,
                p.Description
            });
            output.WriteLine(JsonSerializer.Serialize(simple, ProbeJsonOptions));
        }
        else
        {
            output.WriteLine($"Chat probe personas ({personas.Count}):");
            output.WriteLine();
            foreach (var p in personas)
                output.WriteLine($"  {p.Id,-20} {p.Name,-20} ??? {p.Description}");
        }

        return Task.FromResult(0);
    }

    private static async Task<int> HandleChatProbeExportFailuresAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var runId     = GetOption(args, "--run-id");
        var outputDir = GetOption(args, "--output-dir")
            ?? Path.Combine("tests", "chat-regressions");

        if (string.IsNullOrWhiteSpace(runId))
        {
            error.WriteLine("Missing required option: --run-id <id>");
            return 2;
        }

        // Search for the run result in the default report directory
        var reportRoot = GetOption(args, "--report-dir") ?? DefaultProbeReportDir;
        var resultPath = Directory.GetFiles(reportRoot, "result.json", SearchOption.AllDirectories)
            .FirstOrDefault(f => f.Contains(runId, StringComparison.OrdinalIgnoreCase));

        if (resultPath is null)
        {
            error.WriteLine($"Run result not found for run-id: {runId}");
            error.WriteLine($"Looked in: {Path.GetFullPath(reportRoot)}");
            return 1;
        }

        ProbeRunResult? run;
        try
        {
            var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
            run = JsonSerializer.Deserialize<ProbeRunResult>(json, ProbeJsonOptions);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Failed to read run result: {ex.Message}");
            return 1;
        }

        if (run is null)
        {
            error.WriteLine("Run result file is empty or invalid.");
            return 1;
        }

        var exporter = new ProbeRegressionExporter();
        await exporter.ExportRunAsync(run, outputDir, cancellationToken);

        if (HasFlag(args, "--json"))
            await output.WriteLineAsync(JsonSerializer.Serialize(new { exported = true, runId, outputDir }, ProbeJsonOptions));
        else
            output.WriteLine($"Exported regression fixture to: {Path.GetFullPath(outputDir)}");

        return 0;
    }

    // ?????? End chat-probe handlers ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????

    private sealed record ChatToBuildProofReport
    {
        public required DateTimeOffset StartedUtc { get; init; }
        public required DateTimeOffset CompletedUtc { get; init; }
        public required string ApiBaseUrl { get; init; }
        public required int ProjectId { get; init; }
        public required string Input { get; init; }
        public required string DiscussionTitle { get; init; }
        public long? DocumentId { get; init; }
        public long? DocumentVersionId { get; init; }
        public long? TicketId { get; init; }
        public long? SourceDocumentVersionId { get; init; }
        public string? ReviewId { get; init; }
        public string? ScenarioId { get; init; }
        public bool? ReviewProceed { get; init; }
        public string? ReviewRecommendedNextStep { get; init; }
        public string? RunId { get; init; }
        public string? RunState { get; init; }
        public string? RunStatus { get; init; }
        public bool ReviewPackageAvailable { get; init; }
        public int GeneratedFileCount { get; init; }
        public int CommandEvidenceCount { get; init; }
        public int OutputVerificationCount { get; init; }
        public bool AllOutputVerified { get; init; }
        public string? CodeStandardsStatus { get; init; }
        public string? FileSetHash { get; init; }
        public int EventCount { get; init; }
        public required RepoMutationCheck MutationCheck { get; init; }
        public IReadOnlyList<string> Problems { get; init; } = [];
    }

    private sealed record RepoMutationCheck
    {
        public bool Checked { get; init; }
        public bool Passed { get; init; }
        public string? RepositoryRoot { get; init; }
        public string? BeforeStatus { get; init; }
        public string? AfterStatus { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    private sealed class IronDevCliConfig
    {
        public string? ApiBaseUrl { get; init; }
        public string? ApiToken { get; init; }
    }

}
