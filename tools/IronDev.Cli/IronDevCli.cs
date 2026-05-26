using System.Text.Json;
using IronDev.Client;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
using IronDev.Data.Models;

namespace IronDev.Cli;

public static class IronDevCli
{
    private const string DefaultApiBaseUrl = "http://localhost:5000";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) =>
        RunAsync(args, output, error, handler: null, cancellationToken);

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage(error);
            return 2;
        }

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
            await WriteJsonOrTextAsync(output, report, HasFlag(args, "--json"), FormatRunReport(report));
            return 0;
        }
        catch (IronDevApiException ex)
        {
            WriteApiError("runs report", ex, error);
            return 1;
        }
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
        error.WriteLine("  irondev ticket create --project-id <id> --file <ticket.json> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket list --project-id <id> [--take 50] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket show --project-id <id> --ticket-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket import-github-issue --project-id <id> --file <github-issue.json> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev tickets build --project-id <id> --ticket-id <id> [--max-retries 3] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev runs status --run-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev runs report --run-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev runs stream --run-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine();
        error.WriteLine("Default API base URL: http://localhost:5000");
        error.WriteLine("Overrides: --api-base-url, IRONDEV_API_BASE_URL, irondev.cli.json");
    }

    private sealed class IronDevCliConfig
    {
        public string? ApiBaseUrl { get; init; }
        public string? ApiToken { get; init; }
    }
}
