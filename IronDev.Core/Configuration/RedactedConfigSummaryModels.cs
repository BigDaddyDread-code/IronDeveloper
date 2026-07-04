namespace IronDev.Core.Configuration;

public enum ConfigSourceStatus
{
    Missing = 0,
    Present = 1,
    Loaded = 2,
    Available = 3
}

public enum RedactedConfigValueVisibility
{
    Redacted = 0,
    NonSensitive = 1,
    StatusOnly = 2,
    DerivedMetadata = 3
}

public enum ConfigRootKind
{
    WorkspaceRoot = 0,
    EvidenceRoot = 1,
    LogsRoot = 2,
    DisposableWorkspaceRoot = 3,
    SandboxRepositoryPath = 4,
    CanaryMeasurementRoot = 5,
    BatchMapEvidenceRoot = 6
}

public enum ConfigRootSafetyStatus
{
    NotConfigured = 0,
    NotEvaluated = 1,
    Safe = 2,
    Unsafe = 3
}

public sealed record RedactedConfigSummaryRequest
{
    public string? EnvironmentName { get; init; }
    public bool IsDevelopment { get; init; }
    public bool IsProductionLike { get; init; }
    public IReadOnlyList<ConfigSourceInput> Sources { get; init; } = [];
    public IReadOnlyList<ConfigValueInput> Values { get; init; } = [];
    public IReadOnlyList<RootConfigInput> Roots { get; init; } = [];
    public IReadOnlyList<FeatureFlagInput> FeatureFlags { get; init; } = [];
}

public sealed record ConfigSourceInput(
    string Name,
    ConfigSourceStatus Status,
    int Order);

public sealed record ConfigValueInput(
    string Key,
    string? Value,
    string? SourceName);

public sealed record RootConfigInput(
    ConfigRootKind Kind,
    string ConfigKey,
    string? ConfiguredPath,
    ConfigRootSafetyEvaluation? SafetyEvaluation = null);

public sealed record ConfigRootSafetyEvaluation(
    ConfigRootSafetyStatus Safety,
    string? ReasonCode,
    string? NextSafeAction);

public sealed record FeatureFlagInput(
    string Key,
    string? Value,
    string? SourceName);

public sealed record RedactedConfigSummary
{
    public required string EnvironmentName { get; init; }
    public required bool IsDevelopment { get; init; }
    public required bool IsProductionLike { get; init; }
    public required IReadOnlyList<ConfigSourceSummary> Sources { get; init; }
    public required IReadOnlyList<RedactedConfigValueSummary> RedactedValues { get; init; }
    public required DatabaseConfigSummary Database { get; init; }
    public required AiConfigSummary Ai { get; init; }
    public required WeaviateConfigSummary Weaviate { get; init; }
    public required LocalRootConfigSummary Roots { get; init; }
    public required FeatureFlagSummary FeatureFlags { get; init; }
    public required IReadOnlyList<ConfigWarning> Warnings { get; init; }
    public required string BoundaryStatement { get; init; }
}

public sealed record ConfigSourceSummary(
    string Name,
    ConfigSourceStatus Status,
    int Order);

public sealed record RedactedConfigValueSummary(
    string Key,
    string Value,
    RedactedConfigValueVisibility Visibility,
    string? SourceName);

public sealed record DatabaseConfigSummary
{
    public required bool Configured { get; init; }
    public required string ProviderShape { get; init; }
    public required string DatabaseName { get; init; }
    public required string ServerKind { get; init; }
    public required string AuthenticationMode { get; init; }
    public required bool ContainsPasswordKey { get; init; }
    public required string OverrideSource { get; init; }
}

public sealed record AiConfigSummary
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required bool ApiKeyConfigured { get; init; }
    public required string BaseUrlHost { get; init; }
    public required int? BaseUrlPort { get; init; }
    public required string OverrideSource { get; init; }
}

public sealed record WeaviateConfigSummary
{
    public required bool Enabled { get; init; }
    public required string HttpEndpointHost { get; init; }
    public required int? HttpEndpointPort { get; init; }
    public required string GrpcEndpointHost { get; init; }
    public required int? GrpcEndpointPort { get; init; }
    public required bool AuthConfigured { get; init; }
    public required string OverrideSource { get; init; }
}

public sealed record LocalRootConfigSummary(
    IReadOnlyList<RootConfigSummary> Entries);

public sealed record RootConfigSummary
{
    public required ConfigRootKind Kind { get; init; }
    public required string ConfigKey { get; init; }
    public required bool Configured { get; init; }
    public required ConfigRootSafetyStatus Safety { get; init; }
    public required string ReasonCode { get; init; }
    public required string NextSafeAction { get; init; }
    public required string RedactedPath { get; init; }
}

public sealed record FeatureFlagSummary(
    IReadOnlyList<FeatureFlagConfigSummary> Entries,
    string BoundaryStatement);

public sealed record FeatureFlagConfigSummary(
    string Key,
    string State,
    string SourceName,
    string BoundaryStatement);

public sealed record ConfigWarning(
    string Code,
    string Message);
