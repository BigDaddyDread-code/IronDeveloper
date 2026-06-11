using System.Reflection;
using System.Text.Json;
using IronDev.Client;

namespace IronDev.Cli;

public sealed record IronDevApiOptions(
    string? ApiBaseUrl,
    string? Token,
    string Output,
    bool Verbose,
    IReadOnlyList<IronDevCliError> Errors);

public sealed record IronDevCliError(string Code, string Message);

public static class IronDevCliFoundation
{
    public const int Success = 0;
    public const int ConfigError = 2;
    public const int UsageError = 3;
    public const int ApiFailure = 4;
    public const int ConnectionFailure = 6;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static bool IsHelp(string[] args) =>
        args.Length == 1 &&
        (StringEquals(args[0], "--help") ||
         StringEquals(args[0], "-h") ||
         StringEquals(args[0], "help"));

    public static bool IsVersion(string[] args) =>
        args.Length == 1 &&
        (StringEquals(args[0], "--version") ||
         StringEquals(args[0], "version"));

    public static bool IsConfigShow(string[] args) =>
        args.Length >= 2 &&
        StringEquals(args[0], "config") &&
        StringEquals(args[1], "show");

    public static bool IsApiPing(string[] args) =>
        args.Length >= 2 &&
        StringEquals(args[0], "api") &&
        StringEquals(args[1], "ping");

    public static int WriteVersion(TextWriter output)
    {
        var version = typeof(IronDevCliFoundation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
            version = typeof(IronDevCliFoundation).Assembly.GetName().Version?.ToString() ?? "unknown";

        output.WriteLine(version);
        return Success;
    }

    public static Task<int> HandleConfigShowAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = ResolveOptions(args, environment);
        if (options.Errors.Count > 0)
            return Task.FromResult(WriteFailure("config show", options.Output, output, error, ConfigError, options.Errors));

        var data = new
        {
            apiBaseUrl = options.ApiBaseUrl,
            apiBaseUrlConfigured = !string.IsNullOrWhiteSpace(options.ApiBaseUrl),
            tokenConfigured = !string.IsNullOrWhiteSpace(options.Token),
            output = options.Output,
            verbose = options.Verbose,
            durable = false
        };

        if (IsJson(options.Output))
        {
            WriteEnvelope(output, "config show", "succeeded", data, Array.Empty<string>(), Array.Empty<IronDevCliError>());
            return Task.FromResult(Success);
        }

        output.WriteLine("IronDev CLI configuration");
        output.WriteLine($"  API base URL: {DisplayValue(options.ApiBaseUrl)}");
        output.WriteLine($"  Token configured: {(!string.IsNullOrWhiteSpace(options.Token)).ToString().ToLowerInvariant()}");
        output.WriteLine($"  Output: {options.Output}");
        output.WriteLine($"  Verbose: {options.Verbose.ToString().ToLowerInvariant()}");
        return Task.FromResult(Success);
    }

    public static async Task<int> HandleApiPingAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var options = ResolveOptions(args, environment);
        if (options.Errors.Count > 0)
            return WriteFailure("api ping", options.Output, output, error, ConfigError, options.Errors);

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            return WriteFailure(
                "api ping",
                options.Output,
                output,
                error,
                ConfigError,
                new[]
                {
                    new IronDevCliError(
                        "IRONDEV_CLI_API_BASE_URL_REQUIRED",
                        "API base URL is required for api ping. Set IRONDEV_API_BASE_URL or pass --api-base-url.")
                });
        }

        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out _))
        {
            return WriteFailure(
                "api ping",
                options.Output,
                output,
                error,
                ConfigError,
                new[]
                {
                    new IronDevCliError(
                        "IRONDEV_CLI_API_BASE_URL_INVALID",
                        "API base URL must be an absolute URL.")
                });
        }

        try
        {
            var client = IronDevApiClientFactory.Create(options.ApiBaseUrl, options.Token, handler);
            var response = await client.PingAsync(cancellationToken).ConfigureAwait(false);

            var data = new
            {
                apiBaseUrl = options.ApiBaseUrl.TrimEnd('/'),
                endpoint = "/health",
                statusCode = response.StatusCode,
                apiStatus = response.Status
            };

            if (response.IsSuccess)
            {
                if (IsJson(options.Output))
                {
                    WriteEnvelope(output, "api ping", "succeeded", data, response.Warnings, Array.Empty<IronDevCliError>());
                }
                else
                {
                    output.WriteLine($"IronDev API reachable: {options.ApiBaseUrl.TrimEnd('/')}/health");
                    foreach (var warning in response.Warnings)
                        output.WriteLine($"Warning: {warning}");
                }

                return Success;
            }

            var errors = response.Errors
                .Select(item => new IronDevCliError(item.Code, item.Message))
                .ToArray();

            return WriteFailure("api ping", options.Output, output, error, ApiFailure, errors, response.Warnings, data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return WriteFailure(
                "api ping",
                options.Output,
                output,
                error,
                ConnectionFailure,
                new[]
                {
                    new IronDevCliError(
                        "IRONDEV_CLI_API_CONNECTION_FAILED",
                        $"Could not reach the configured IronDev API: {ex.Message}")
                });
        }
    }

    public static IronDevApiOptions ResolveOptions(
        string[] args,
        IReadOnlyDictionary<string, string?> environment)
    {
        var errors = new List<IronDevCliError>();
        var flagApiBaseUrl = TryGetOption(args, "--api-base-url", out var apiBaseUrlValue) ? apiBaseUrlValue : null;
        var flagToken = TryGetOption(args, "--token", out var tokenValue) ? tokenValue : null;
        var flagOutput = TryGetOption(args, "--output", out var outputValue) ? outputValue : null;
        var verbose = args.Any(arg => StringEquals(arg, "--verbose"));

        var apiBaseUrl = FirstNonBlank(flagApiBaseUrl, GetEnvironmentValue(environment, "IRONDEV_API_BASE_URL"));
        var token = FirstNonBlank(flagToken, GetEnvironmentValue(environment, "IRONDEV_API_TOKEN"));
        var output = FirstNonBlank(
            args.Any(arg => StringEquals(arg, "--json")) ? "json" : null,
            flagOutput,
            GetEnvironmentValue(environment, "IRONDEV_OUTPUT"),
            "text")!;

        output = output.Trim().ToLowerInvariant();
        if (!IsText(output) && !IsJson(output))
        {
            errors.Add(new IronDevCliError(
                "IRONDEV_CLI_OUTPUT_INVALID",
                "Output must be either text or json."));
            output = "text";
        }

        return new IronDevApiOptions(apiBaseUrl, token, output, verbose, errors);
    }

    private static int WriteFailure(
        string command,
        string outputMode,
        TextWriter output,
        TextWriter error,
        int exitCode,
        IReadOnlyList<IronDevCliError> errors,
        IReadOnlyList<string>? warnings = null,
        object? data = null)
    {
        if (IsJson(outputMode))
        {
            WriteEnvelope(output, command, "failed", data, warnings ?? Array.Empty<string>(), errors);
        }
        else
        {
            foreach (var item in errors)
                error.WriteLine($"{item.Code}: {item.Message}");

            foreach (var warning in warnings ?? Array.Empty<string>())
                error.WriteLine($"Warning: {warning}");
        }

        return exitCode;
    }

    private static void WriteEnvelope<T>(
        TextWriter output,
        string command,
        string status,
        T? data,
        IReadOnlyList<string> warnings,
        IReadOnlyList<IronDevCliError> errors)
    {
        var envelope = new
        {
            ok = errors.Count == 0,
            command,
            status,
            data,
            warnings,
            errors
        };

        output.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static bool TryGetOption(string[] args, string name, out string? value)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!StringEquals(args[i], name))
                continue;

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = null;
                return true;
            }

            value = args[i + 1];
            return true;
        }

        value = null;
        return false;
    }

    private static string? GetEnvironmentValue(IReadOnlyDictionary<string, string?> environment, string key)
    {
        if (environment.TryGetValue(key, out var value))
            return value;

        foreach (var item in environment)
        {
            if (StringEquals(item.Key, key))
                return item.Value;
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool StringEquals(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsText(string? output)
        => StringEquals(output, "text");

    private static bool IsJson(string? output)
        => StringEquals(output, "json");

    private static string DisplayValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<not configured>" : value;
}
