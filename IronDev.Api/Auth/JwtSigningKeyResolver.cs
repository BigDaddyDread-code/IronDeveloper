using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace IronDev.Api.Auth;

public enum JwtSigningKeySource
{
    Missing = 0,
    Configuration = 1,
    IronDevJwtKeyEnvironment = 2
}

public enum JwtSigningKeyLengthClassification
{
    Missing = 0,
    TooShort = 1,
    Valid = 2
}

public sealed class JwtSigningKeyResolution
{
    public JwtSigningKeyResolution(
        string key,
        JwtSigningKeySource source,
        JwtSigningKeyLengthClassification lengthClassification)
    {
        Key = key;
        Source = source;
        LengthClassification = lengthClassification;
    }

    public string Key { get; }
    public JwtSigningKeySource Source { get; }
    public JwtSigningKeyLengthClassification LengthClassification { get; }

    public override string ToString() =>
        $"JwtSigningKeyResolution {{ Source = {Source}, LengthClassification = {LengthClassification} }}";
}

public static class JwtStartupConfigurationValidator
{
    public const string StartupValidationFailedMessage =
        "JWT signing key startup validation failed. Set Jwt__Key or IRONDEV_JWT_KEY outside committed appsettings.";
    public const string StartupValidationPassedLogMessage =
        "JWT signing key startup validation passed using source {JwtSigningKeySource}.";

    public static JwtSigningKeyResolution Validate(IConfiguration configuration) =>
        Validate(configuration, Environment.GetEnvironmentVariable);

    public static JwtSigningKeyResolution Validate(
        IConfiguration configuration,
        Func<string, string?> environmentVariableReader)
    {
        try
        {
            return JwtSigningKeyResolver.ResolveWithMetadata(configuration, environmentVariableReader);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(StartupValidationFailedMessage, exception);
        }
    }
}

public static class JwtSigningKeyResolver
{
    public const int MinimumSigningKeyLength = 32;
    public const string OldCommittedPlaceholderKey =
        "irondev-super-secret-jwt-key-change-in-production-min32chars";
    public const string MissingSigningKeyMessage =
        "JWT signing key is not configured. Set Jwt__Key or IRONDEV_JWT_KEY outside committed appsettings.";
    public const string ShortSigningKeyMessage =
        "JWT signing key must be at least 32 characters.";
    public const string PlaceholderSigningKeyMessage =
        "JWT signing key cannot use the old committed placeholder.";
    public const string ForbiddenCommittedConfigSigningKeyMessage =
        "JWT signing key must not be loaded from committed appsettings.";
    public const string UnknownSigningKeySourceMessage =
        "JWT signing key source is not recognized.";

    public static string Resolve(IConfiguration configuration) =>
        Resolve(configuration, Environment.GetEnvironmentVariable);

    public static string Resolve(
        IConfiguration configuration,
        Func<string, string?> environmentVariableReader) =>
        ResolveWithMetadata(configuration, environmentVariableReader).Key;

    public static JwtSigningKeyResolution ResolveWithMetadata(IConfiguration configuration) =>
        ResolveWithMetadata(configuration, Environment.GetEnvironmentVariable);

    public static JwtSigningKeyResolution ResolveWithMetadata(
        IConfiguration configuration,
        Func<string, string?> environmentVariableReader)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentVariableReader);

        var configurationCandidate = TryGetConfigurationCandidate(configuration);
        if (configurationCandidate is not null)
        {
            ValidateKey(configurationCandidate.Value.Key);
            ValidateConfigurationSource(configurationCandidate.Value.Provider);
            return new JwtSigningKeyResolution(
                configurationCandidate.Value.Key,
                JwtSigningKeySource.Configuration,
                JwtSigningKeyLengthClassification.Valid);
        }

        var environmentKey = environmentVariableReader("IRONDEV_JWT_KEY");
        if (!string.IsNullOrWhiteSpace(environmentKey))
        {
            ValidateKey(environmentKey);
            return new JwtSigningKeyResolution(
                environmentKey,
                JwtSigningKeySource.IronDevJwtKeyEnvironment,
                JwtSigningKeyLengthClassification.Valid);
        }

        throw new InvalidOperationException(MissingSigningKeyMessage);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(MissingSigningKeyMessage);

        if (string.Equals(key, OldCommittedPlaceholderKey, StringComparison.Ordinal))
            throw new InvalidOperationException(PlaceholderSigningKeyMessage);

        if (key.Length < MinimumSigningKeyLength)
            throw new InvalidOperationException(ShortSigningKeyMessage);
    }

    private static void ValidateConfigurationSource(IConfigurationProvider provider)
    {
        var providerName = provider.GetType().Name;
        if (providerName.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("EnvironmentVariables", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (providerName.Contains("Json", StringComparison.OrdinalIgnoreCase))
        {
            if (IsCommittedAppsettingsProvider(provider))
                throw new InvalidOperationException(ForbiddenCommittedConfigSigningKeyMessage);

            return;
        }

        throw new InvalidOperationException(UnknownSigningKeySourceMessage);
    }

    private static bool IsCommittedAppsettingsProvider(IConfigurationProvider provider)
    {
        var source = provider.GetType()
            .GetProperty("Source", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(provider);
        var path = source?.GetType()
            .GetProperty("Path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(source) as string;

        if (string.IsNullOrWhiteSpace(path))
            return true;

        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "appsettings.json", StringComparison.OrdinalIgnoreCase) ||
            (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
             fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    private static JwtConfigurationCandidate? TryGetConfigurationCandidate(IConfiguration configuration)
    {
        if (configuration is not IConfigurationRoot root)
            throw new InvalidOperationException(UnknownSigningKeySourceMessage);

        foreach (var provider in root.Providers.Reverse())
        {
            if (provider.TryGet("Jwt:Key", out var key) && !string.IsNullOrWhiteSpace(key))
                return new JwtConfigurationCandidate(key, provider);
        }

        return null;
    }

    private readonly record struct JwtConfigurationCandidate(string Key, IConfigurationProvider Provider);
}
