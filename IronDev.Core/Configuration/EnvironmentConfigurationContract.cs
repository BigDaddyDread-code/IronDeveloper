namespace IronDev.Core.Configuration;

public enum IronDevEnvironmentProfile { Unknown, DeveloperLocal, LocalTest, ContinuousIntegration, HostedAlpha, ProductionLikeTest }

public sealed record EnvironmentConfigurationIssue(string Key, string Code, string Message);

public sealed record EnvironmentConfigurationResult(IronDevEnvironmentProfile Profile, IReadOnlyList<EnvironmentConfigurationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class EnvironmentConfigurationContract
{
    private static readonly string[] UnsafeValueMarkers = ["CHANGE_ME", "CHANGEME", "YOUR_", "EXAMPLE_SECRET", "DEFAULT_SECRET"];

    public static EnvironmentConfigurationResult Validate(string? environmentName, IReadOnlyDictionary<string, string?> settings)
    {
        var profile = ResolveProfile(environmentName);
        var issues = new List<EnvironmentConfigurationIssue>();
        if (profile == IronDevEnvironmentProfile.Unknown)
            issues.Add(new("ASPNETCORE_ENVIRONMENT", "UnsupportedEnvironmentProfile", "The environment name does not map to an IronDev configuration profile."));

        Require(settings, "ConnectionStrings:IronDeveloperDb", issues);
        Require(settings, "Ai:Provider", issues);

        if (profile == IronDevEnvironmentProfile.LocalTest)
        {
            Require(settings, "LocalTest:WorkspaceRoot", issues);
            Require(settings, "LocalTest:LogsRoot", issues);
        }

        if (profile is IronDevEnvironmentProfile.HostedAlpha or IronDevEnvironmentProfile.ProductionLikeTest)
        {
            RequireAnyWithPrefix(settings, "Cors:AllowedOrigins:", "Cors:AllowedOrigins", issues);
        }

        if (ReadOptionalBoolean(settings, "Weaviate:Enabled", issues))
            Require(settings, "Weaviate:Endpoint", issues);

        issues.AddRange(ValidateAiProvider(settings));

        return new(profile, issues);
    }

    public static IReadOnlyList<EnvironmentConfigurationIssue> ValidateAiProvider(IReadOnlyDictionary<string, string?> settings)
    {
        var issues = new List<EnvironmentConfigurationIssue>();
        var aiProvider = Value(settings, "Ai:Provider");
        if (string.Equals(aiProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            RequireEither(settings, ["Ai:ApiKey", "OPENAI_API_KEY"], "Ai:ApiKey", issues);
        else if (string.Equals(aiProvider, "LocalOpenAI", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(aiProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
            Require(settings, "Ai:BaseUrl", issues);
        else if (!string.IsNullOrWhiteSpace(aiProvider) && !string.Equals(aiProvider, "Fake", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("Ai:Provider", "UnsupportedProviderConfiguration", "The configured AI provider is not supported."));

        return issues;
    }

    public static IronDevEnvironmentProfile ResolveProfile(string? environmentName) => environmentName?.Trim().ToUpperInvariant() switch
    {
        "DEVELOPMENT" or "DEVELOPERLOCAL" => IronDevEnvironmentProfile.DeveloperLocal,
        "LOCALTEST" => IronDevEnvironmentProfile.LocalTest,
        "CI" or "TEST" or "INTEGRATIONTEST" or "E2E" or "AUTOMATIONTEST" or "SMOKETEST" => IronDevEnvironmentProfile.ContinuousIntegration,
        "HOSTEDALPHA" or "STAGING" => IronDevEnvironmentProfile.HostedAlpha,
        "PRODUCTIONLIKETEST" or "PRODUCTION" => IronDevEnvironmentProfile.ProductionLikeTest,
        _ => IronDevEnvironmentProfile.Unknown
    };

    private static void Require(IReadOnlyDictionary<string, string?> settings, string key, ICollection<EnvironmentConfigurationIssue> issues)
    {
        var value = Value(settings, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new(key, "MissingCriticalConfiguration", $"Required configuration '{key}' is missing."));
            return;
        }
        if (UnsafeValueMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new(key, "UnsafePlaceholderConfiguration", $"Required configuration '{key}' contains an unsafe placeholder."));
    }

    private static void RequireEither(IReadOnlyDictionary<string, string?> settings, IReadOnlyList<string> keys, string displayKey, ICollection<EnvironmentConfigurationIssue> issues)
    {
        var configuredKey = keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(Value(settings, key)));
        if (configuredKey is null)
            issues.Add(new(displayKey, "MissingCriticalConfiguration", $"Required configuration '{displayKey}' is missing."));
        else
            RejectUnsafePlaceholder(settings, configuredKey, displayKey, issues);
    }

    private static void RequireAnyWithPrefix(IReadOnlyDictionary<string, string?> settings, string prefix, string displayKey, ICollection<EnvironmentConfigurationIssue> issues)
    {
        var configuredKey = settings.Keys.FirstOrDefault(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Value(settings, key)));
        if (configuredKey is null)
            issues.Add(new(displayKey, "MissingCriticalConfiguration", $"Required configuration '{displayKey}' is missing."));
        else
            RejectUnsafePlaceholder(settings, configuredKey, displayKey, issues);
    }

    private static void RejectUnsafePlaceholder(IReadOnlyDictionary<string, string?> settings, string key, string displayKey, ICollection<EnvironmentConfigurationIssue> issues)
    {
        var value = Value(settings, key)!;
        if (UnsafeValueMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new(displayKey, "UnsafePlaceholderConfiguration", $"Required configuration '{displayKey}' contains an unsafe placeholder."));
    }

    private static bool ReadOptionalBoolean(IReadOnlyDictionary<string, string?> settings, string key, ICollection<EnvironmentConfigurationIssue> issues)
    {
        var value = Value(settings, key);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (bool.TryParse(value, out var enabled))
            return enabled;

        issues.Add(new(key, "InvalidBooleanConfiguration", $"Configuration '{key}' must be true or false."));
        return false;
    }

    private static string? Value(IReadOnlyDictionary<string, string?> settings, string key) => settings.TryGetValue(key, out var value) ? value : null;
}
