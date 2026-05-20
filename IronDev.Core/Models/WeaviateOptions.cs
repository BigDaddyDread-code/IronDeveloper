namespace IronDev.Core.Models;

public sealed class WeaviateOptions
{
    public string Endpoint { get; set; } = "http://localhost:8080";
    public int GrpcPort { get; set; } = 50051;
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string CollectionPrefix { get; set; } = "IronDevContextChunks";
}
