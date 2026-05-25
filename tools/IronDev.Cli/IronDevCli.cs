using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IronDev.Core.Models;
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

        using var http = await CreateReadyHttpClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (http is null)
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

        using var response = await http.PostAsJsonAsync(
            $"/api/projects/{projectId}/tickets",
            request,
            JsonOptions,
            cancellationToken);

        if (!await EnsureApiSuccessAsync(response, "ticket create", error, cancellationToken))
            return 1;

        var saved = await response.Content.ReadFromJsonAsync<ProjectTicket>(JsonOptions, cancellationToken);
        if (saved is null)
        {
            error.WriteLine("IronDev.Api returned an empty ticket response.");
            return 1;
        }

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
        using var http = await CreateReadyHttpClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (http is null)
            return 1;

        using var response = await http.GetAsync($"/api/projects/{projectId}/tickets?take={take}", cancellationToken);
        if (!await EnsureApiSuccessAsync(response, "ticket list", error, cancellationToken))
            return 1;

        var tickets = await response.Content.ReadFromJsonAsync<IReadOnlyList<ProjectTicket>>(JsonOptions, cancellationToken)
            ?? [];
        await WriteJsonOrTextAsync(output, tickets, HasFlag(args, "--json"), $"Found {tickets.Count} IronDev tickets.");
        return 0;
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

        using var http = await CreateReadyHttpClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (http is null)
            return 1;

        using var response = await http.GetAsync($"/api/projects/{projectId}/tickets/{ticketId}", cancellationToken);
        if (!await EnsureApiSuccessAsync(response, "ticket show", error, cancellationToken))
            return 1;

        var ticket = await response.Content.ReadFromJsonAsync<ProjectTicket>(JsonOptions, cancellationToken);
        if (ticket is null)
        {
            error.WriteLine("IronDev.Api returned an empty ticket response.");
            return 1;
        }

        await WriteJsonOrTextAsync(output, ticket, HasFlag(args, "--json"), $"{ticket.Id}: {ticket.Title}");
        return 0;
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

        using var http = await CreateReadyHttpClientAsync(args, apiBaseUrl, error, handler, cancellationToken);
        if (http is null)
            return 1;

        using var response = await http.PostAsJsonAsync(
            $"/api/projects/{projectId}/tickets/import-external",
            request,
            JsonOptions,
            cancellationToken);

        if (!await EnsureApiSuccessAsync(response, "ticket import-github-issue", error, cancellationToken))
            return 1;

        var saved = await response.Content.ReadFromJsonAsync<ProjectTicket>(JsonOptions, cancellationToken);
        if (saved is null)
        {
            error.WriteLine("IronDev.Api returned an empty ticket response.");
            return 1;
        }

        await WriteJsonOrTextAsync(output, saved, HasFlag(args, "--json"), $"Imported GitHub issue as IronDev ticket {saved.Id}: {saved.Title}");
        return 0;
    }

    private static async Task<HttpClient?> CreateReadyHttpClientAsync(
        string[] args,
        string apiBaseUrl,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var http = CreateHttpClient(apiBaseUrl, handler);
        var health = await CheckHealthAsync(http, cancellationToken);
        if (!health)
        {
            error.WriteLine($"IronDev.Api is not reachable at {apiBaseUrl}.");
            error.WriteLine("Start it with:");
            error.WriteLine("dotnet run --project IronDev.Api");
            http.Dispose();
            return null;
        }

        var token = ResolveToken(GetOption(args, "--token"), ReadEnvironment(), GetOption(args, "--config"));
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return http;
    }

    private static async Task<bool> EnsureApiSuccessAsync(
        HttpResponseMessage response,
        string operation,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return true;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        error.WriteLine(BuildApiErrorMessage(operation, response.StatusCode, body));
        return false;
    }

    private static async Task WriteJsonOrTextAsync<T>(TextWriter output, T value, bool json, string text)
    {
        if (json)
            await output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions));
        else
            await output.WriteLineAsync(text);
    }

    private static HttpClient CreateHttpClient(string apiBaseUrl, HttpMessageHandler? handler)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.BaseAddress = new Uri(apiBaseUrl);
        http.Timeout = TimeSpan.FromSeconds(15);
        return http;
    }

    private static async Task<bool> CheckHealthAsync(HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
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

    private static string BuildApiErrorMessage(string operation, HttpStatusCode statusCode, string body)
    {
        var prefix = $"IronDev.Api {operation} failed with {(int)statusCode} {statusCode}.";
        if (statusCode == HttpStatusCode.Unauthorized)
            prefix += $"{Environment.NewLine}Authenticate through IronDev.Api and provide a tenant-scoped JWT with --token or IRONDEV_API_TOKEN.";

        return string.IsNullOrWhiteSpace(body) ? prefix : $"{prefix}{Environment.NewLine}{body}";
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
        error.WriteLine("  irondev ticket create --project-id <id> --file <ticket.json> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket list --project-id <id> [--take 50] [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket show --project-id <id> --ticket-id <id> [--json] [--api-base-url <url>] [--token <jwt>]");
        error.WriteLine("  irondev ticket import-github-issue --project-id <id> --file <github-issue.json> [--json] [--api-base-url <url>] [--token <jwt>]");
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
