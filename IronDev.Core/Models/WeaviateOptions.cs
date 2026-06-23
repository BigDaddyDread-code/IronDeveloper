namespace IronDev.Core.Models;

public sealed class WeaviateOptions
{
    public string Endpoint { get; set; } = "http://localhost:8080";
    public int GrpcPort { get; set; } = 50051;
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string CollectionPrefix { get; set; } = "IronDevContextChunks";
}

public enum WeaviateAuthSource
{
    Missing = 0,
    Configuration = 1,
    IronDevWeaviateApiKeyEnvironment = 2
}

public enum WeaviateEndpointClassification
{
    Missing = 0,
    Invalid = 1,
    Localhost = 2,
    Remote = 3
}

public sealed class WeaviateAuthConfigValidationResult
{
    public WeaviateAuthConfigValidationResult(
        bool enabled,
        string environmentName,
        bool productionLikeEnvironment,
        WeaviateAuthSource apiKeySource,
        WeaviateEndpointClassification endpointClassification,
        bool valid,
        IReadOnlyList<string> issues)
    {
        Enabled = enabled;
        EnvironmentName = environmentName;
        ProductionLikeEnvironment = productionLikeEnvironment;
        ApiKeySource = apiKeySource;
        EndpointClassification = endpointClassification;
        Valid = valid;
        Issues = issues;
    }

    public bool Enabled { get; }
    public string EnvironmentName { get; }
    public bool ProductionLikeEnvironment { get; }
    public WeaviateAuthSource ApiKeySource { get; }
    public WeaviateEndpointClassification EndpointClassification { get; }
    public bool Valid { get; }
    public IReadOnlyList<string> Issues { get; }

    public override string ToString() =>
        $"WeaviateAuthConfigValidationResult {{ Enabled = {Enabled}, EnvironmentName = {EnvironmentName}, ProductionLikeEnvironment = {ProductionLikeEnvironment}, ApiKeySource = {ApiKeySource}, EndpointClassification = {EndpointClassification}, Valid = {Valid}, IssueCount = {Issues.Count} }}";
}
