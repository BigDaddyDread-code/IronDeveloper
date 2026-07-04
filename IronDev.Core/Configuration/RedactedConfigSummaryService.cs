using System.Text.RegularExpressions;

namespace IronDev.Core.Configuration;

public sealed class RedactedConfigSummaryService
{
    public const string RedactedValue = "[REDACTED]";
    public const string UnknownValue = "Unknown";
    public const string NotConfiguredValue = "NotConfigured";
    public const string BoundaryStatement =
        "A config summary is diagnostic evidence for a human. It is not approval, authority, policy satisfaction, root safety proof, or permission to mutate anything.";
    public const string FeatureFlagBoundaryStatement =
        "A configured flag is not permission. Backend gates still decide.";

    private static readonly string[] SensitiveKeyMarkers =
    [
        "Password",
        "Pwd",
        "Secret",
        "Token",
        "ApiKey",
        "AccessKey",
        "PrivateKey",
        "Jwt",
        "Bearer",
        "Authorization",
        "ConnectionString",
        "ClientSecret"
    ];

    public RedactedConfigSummary Build(RedactedConfigSummaryRequest? request)
    {
        try
        {
            if (request is null)
                return DegradedSummary("ConfigSummaryRequestMissing");

            var values = ResolveEffectiveValues(request.Values);
            var warnings = new List<ConfigWarning>();

            return new RedactedConfigSummary
            {
                EnvironmentName = SafeStatusValue(request.EnvironmentName),
                IsDevelopment = request.IsDevelopment,
                IsProductionLike = request.IsProductionLike,
                Sources = request.Sources
                    .OrderBy(source => source.Order)
                    .Select(source => new ConfigSourceSummary(
                        SafeStatusValue(source.Name),
                        source.Status,
                        source.Order))
                    .ToArray(),
                RedactedValues = values.Values
                    .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(value => RedactConfigValue(value.Key, value.Value, value.SourceName))
                    .ToArray(),
                Database = SummarizeDatabase(values),
                Ai = SummarizeAi(values),
                Weaviate = SummarizeWeaviate(values),
                Roots = SummarizeRoots(request.Roots),
                FeatureFlags = SummarizeFeatureFlags(request.FeatureFlags),
                Warnings = warnings,
                BoundaryStatement = BoundaryStatement
            };
        }
        catch
        {
            return DegradedSummary("ConfigSummaryGenerationFailed");
        }
    }

    public static RedactedConfigValueSummary RedactConfigValue(string key, string? value, string? sourceName)
    {
        if (IsSensitiveKey(key))
        {
            return new RedactedConfigValueSummary(
                SafeKey(key),
                RedactedValue,
                RedactedConfigValueVisibility.Redacted,
                SafeStatusValue(sourceName));
        }

        return new RedactedConfigValueSummary(
            SafeKey(key),
            SafeDisplayValue(value),
            RedactedConfigValueVisibility.NonSensitive,
            SafeStatusValue(sourceName));
    }

    public static string RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NotConfiguredValue;

        var trimmed = path.Trim();
        if (LooksSensitiveValue(trimmed))
            return RedactedValue;

        var windows = Regex.Replace(
            trimmed,
            @"(?i)\b(?<drive>[A-Z]:)[\\/]+Users[\\/]+(?<user>[^\\/]+)",
            match => $"{match.Groups["drive"].Value}\\Users\\<user>");
        windows = Regex.Replace(
            windows,
            @"(?i)file:///(?<drive>[a-z]:)/Users/(?<user>[^/]+)",
            match => $"file:///{match.Groups["drive"].Value}/Users/<user>");

        var unix = Regex.Replace(
            windows,
            @"(?<![A-Za-z0-9_])/(?<root>Users|home)/(?<user>[^/]+)",
            match => $"/{match.Groups["root"].Value}/<user>");

        return LooksSensitiveValue(unix) ? RedactedValue : unix;
    }

    private static RedactedConfigSummary DegradedSummary(string warningCode) =>
        new()
        {
            EnvironmentName = UnknownValue,
            IsDevelopment = false,
            IsProductionLike = false,
            Sources = [],
            RedactedValues = [],
            Database = EmptyDatabaseSummary(),
            Ai = EmptyAiSummary(),
            Weaviate = EmptyWeaviateSummary(),
            Roots = new LocalRootConfigSummary([]),
            FeatureFlags = new FeatureFlagSummary([], FeatureFlagBoundaryStatement),
            Warnings =
            [
                new ConfigWarning(
                    warningCode,
                    "Config summary unavailable: failed to parse one or more settings. Raw values were not logged.")
            ],
            BoundaryStatement = BoundaryStatement
        };

    private static Dictionary<string, EffectiveConfigValue> ResolveEffectiveValues(IReadOnlyList<ConfigValueInput> values)
    {
        var resolved = new Dictionary<string, EffectiveConfigValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Key))
                continue;

            resolved[value.Key.Trim()] = new EffectiveConfigValue(
                value.Key.Trim(),
                value.Value,
                SafeStatusValue(value.SourceName));
        }

        return resolved;
    }

    private static DatabaseConfigSummary SummarizeDatabase(IReadOnlyDictionary<string, EffectiveConfigValue> values)
    {
        var connection = FindValue(values, "ConnectionStrings:IronDeveloperDb", "ConnectionStrings__IronDeveloperDb");
        if (string.IsNullOrWhiteSpace(connection?.Value))
            return EmptyDatabaseSummary();

        var parts = ParseConnectionString(connection.Value);
        var server = FirstPart(parts, "Server", "Data Source", "Addr", "Address", "Network Address");
        var database = FirstPart(parts, "Database", "Initial Catalog");
        var integratedSecurity = FirstPart(parts, "Integrated Security", "Trusted_Connection");
        var hasPassword = HasPart(parts, "Password", "Pwd");
        var hasUser = HasPart(parts, "User Id", "User ID", "UID");

        return new DatabaseConfigSummary
        {
            Configured = true,
            ProviderShape = "SQL Server",
            DatabaseName = SafeDerivedValue(database),
            ServerKind = ClassifyServerKind(server),
            AuthenticationMode = ClassifyAuthenticationMode(integratedSecurity, hasPassword || hasUser),
            ContainsPasswordKey = hasPassword,
            OverrideSource = SafeStatusValue(connection.SourceName)
        };
    }

    private static AiConfigSummary SummarizeAi(IReadOnlyDictionary<string, EffectiveConfigValue> values)
    {
        var provider = FindValue(values, "Ai:Provider", "Ai__Provider");
        var model = FindValue(values, "Ai:Model", "Ai__Model");
        var baseUrl = FindValue(values, "Ai:BaseUrl", "Ai__BaseUrl");
        var apiKey = FindValue(values, "Ai:ApiKey", "Ai__ApiKey", "OPENAI_API_KEY", "LOCAL_OPENAI_API_KEY");
        var endpoint = ParseEndpoint(baseUrl?.Value);

        return new AiConfigSummary
        {
            Provider = SafeDerivedValue(provider?.Value),
            Model = SafeDerivedValue(model?.Value),
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(apiKey?.Value),
            BaseUrlHost = endpoint.Host,
            BaseUrlPort = endpoint.Port,
            OverrideSource = SafeStatusValue(provider?.SourceName ?? model?.SourceName ?? baseUrl?.SourceName ?? apiKey?.SourceName)
        };
    }

    private static WeaviateConfigSummary SummarizeWeaviate(IReadOnlyDictionary<string, EffectiveConfigValue> values)
    {
        var enabled = FindValue(values, "Weaviate:Enabled", "Weaviate__Enabled");
        var endpointValue = FindValue(values, "Weaviate:Endpoint", "Weaviate__Endpoint", "Weaviate:HttpEndpoint", "Weaviate__HttpEndpoint");
        var grpcPort = FindValue(values, "Weaviate:GrpcPort", "Weaviate__GrpcPort");
        var apiKey = FindValue(values, "Weaviate:ApiKey", "Weaviate__ApiKey", "IRONDEV_WEAVIATE_API_KEY");
        var endpoint = ParseEndpoint(endpointValue?.Value);

        return new WeaviateConfigSummary
        {
            Enabled = bool.TryParse(enabled?.Value, out var isEnabled) && isEnabled,
            HttpEndpointHost = endpoint.Host,
            HttpEndpointPort = endpoint.Port,
            GrpcEndpointHost = endpoint.Host,
            GrpcEndpointPort = int.TryParse(grpcPort?.Value, out var parsedGrpcPort) ? parsedGrpcPort : null,
            AuthConfigured = !string.IsNullOrWhiteSpace(apiKey?.Value),
            OverrideSource = SafeStatusValue(enabled?.SourceName ?? endpointValue?.SourceName ?? grpcPort?.SourceName ?? apiKey?.SourceName)
        };
    }

    private static LocalRootConfigSummary SummarizeRoots(IReadOnlyList<RootConfigInput> roots) =>
        new(roots
            .Select(root =>
            {
                var configured = !string.IsNullOrWhiteSpace(root.ConfiguredPath);
                var safety = configured
                    ? root.SafetyEvaluation?.Safety ?? ConfigRootSafetyStatus.NotEvaluated
                    : ConfigRootSafetyStatus.NotConfigured;

                return new RootConfigSummary
                {
                    Kind = root.Kind,
                    ConfigKey = SafeKey(root.ConfigKey),
                    Configured = configured,
                    Safety = safety,
                    ReasonCode = configured
                        ? SafeStatusValue(root.SafetyEvaluation?.ReasonCode ?? safety.ToString())
                        : "MissingPath",
                    NextSafeAction = configured
                        ? SafeStatusValue(root.SafetyEvaluation?.NextSafeAction ?? "Root safety has not been evaluated. Run the root safety validator before writing, cleaning, executing, or treating contents as evidence.")
                        : "Configure the root only when this local capability is needed.",
                    RedactedPath = RedactPath(root.ConfiguredPath)
                };
            })
            .ToArray());

    private static FeatureFlagSummary SummarizeFeatureFlags(IReadOnlyList<FeatureFlagInput> featureFlags) =>
        new(
            featureFlags
                .Select(flag => new FeatureFlagConfigSummary(
                    SafeKey(flag.Key),
                    IsSensitiveKey(flag.Key) ? RedactedValue : SafeDisplayValue(flag.Value),
                    SafeStatusValue(flag.SourceName),
                    FeatureFlagBoundaryStatement))
                .ToArray(),
            FeatureFlagBoundaryStatement);

    private static DatabaseConfigSummary EmptyDatabaseSummary() =>
        new()
        {
            Configured = false,
            ProviderShape = NotConfiguredValue,
            DatabaseName = NotConfiguredValue,
            ServerKind = NotConfiguredValue,
            AuthenticationMode = NotConfiguredValue,
            ContainsPasswordKey = false,
            OverrideSource = NotConfiguredValue
        };

    private static AiConfigSummary EmptyAiSummary() =>
        new()
        {
            Provider = NotConfiguredValue,
            Model = NotConfiguredValue,
            ApiKeyConfigured = false,
            BaseUrlHost = NotConfiguredValue,
            BaseUrlPort = null,
            OverrideSource = NotConfiguredValue
        };

    private static WeaviateConfigSummary EmptyWeaviateSummary() =>
        new()
        {
            Enabled = false,
            HttpEndpointHost = NotConfiguredValue,
            HttpEndpointPort = null,
            GrpcEndpointHost = NotConfiguredValue,
            GrpcEndpointPort = null,
            AuthConfigured = false,
            OverrideSource = NotConfiguredValue
        };

    private static Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
                parts[key] = value;
        }

        return parts;
    }

    private static string FirstPart(IReadOnlyDictionary<string, string> parts, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parts.TryGetValue(key, out var value))
                return value;
        }

        return string.Empty;
    }

    private static bool HasPart(IReadOnlyDictionary<string, string> parts, params string[] keys) =>
        keys.Any(parts.ContainsKey);

    private static string ClassifyServerKind(string server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return UnknownValue;

        if (server.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
            return "LocalDB";

        var normalized = server.Trim();
        if (normalized is "." or "(local)" ||
            normalized.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("127.0.0.1", StringComparison.Ordinal) ||
            normalized.StartsWith("[::1]", StringComparison.Ordinal))
        {
            return "Localhost";
        }

        return "RemoteOrNamed";
    }

    private static string ClassifyAuthenticationMode(string integratedSecurity, bool hasCredentialKey)
    {
        if (!string.IsNullOrWhiteSpace(integratedSecurity) &&
            (integratedSecurity.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             integratedSecurity.Equals("sspi", StringComparison.OrdinalIgnoreCase)))
        {
            return "Integrated";
        }

        return hasCredentialKey ? "Credentialed" : UnknownValue;
    }

    private static EndpointSummary ParseEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new EndpointSummary(NotConfiguredValue, null);

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (trimmed.Contains(':', StringComparison.Ordinal) &&
                !trimmed.Contains('/', StringComparison.Ordinal))
            {
                var separator = trimmed.LastIndexOf(':');
                var host = trimmed[..separator];
                var portText = trimmed[(separator + 1)..];
                return new EndpointSummary(SafeHost(host), int.TryParse(portText, out var port) ? port : null);
            }

            return new EndpointSummary(RedactedValue, null);
        }

        return new EndpointSummary(SafeHost(uri.Host), uri.IsDefaultPort ? null : uri.Port);
    }

    private static EffectiveConfigValue? FindValue(IReadOnlyDictionary<string, EffectiveConfigValue> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value))
                return value;
        }

        return null;
    }

    private static string SafeDerivedValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return NotConfiguredValue;

        return LooksSensitiveValue(value) ? RedactedValue : value.Trim();
    }

    private static string SafeDisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return NotConfiguredValue;

        return LooksSensitiveValue(value) ? RedactedValue : value.Trim();
    }

    private static string SafeStatusValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return UnknownValue;

        return LooksSensitiveValue(value) ? RedactedValue : value.Trim();
    }

    private static string SafeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return UnknownValue;

        return key.Trim();
    }

    private static string SafeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return NotConfiguredValue;

        return LooksSensitiveValue(host) ? RedactedValue : host.Trim();
    }

    private static bool IsSensitiveKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        SensitiveKeyMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool LooksSensitiveValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Api", "Key="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Authorization=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Pass", "word="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Pwd", "="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Secret", "="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Token", "="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Client", "Secret", "="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Access", "Key="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(string.Concat("Private", "Key="), StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("sk-", StringComparison.Ordinal) ||
            trimmed.StartsWith("ghp_", StringComparison.Ordinal) ||
            trimmed.StartsWith("github_pat_", StringComparison.Ordinal) ||
            trimmed.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record EffectiveConfigValue(
        string Key,
        string? Value,
        string SourceName);

    private sealed record EndpointSummary(
        string Host,
        int? Port);
}
