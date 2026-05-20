namespace IronDev.Core.Models;

public sealed class EmbeddingOptions
{
    public string Provider { get; set; } = "Fake";
    public string Model { get; set; } = "text-embedding-3-small";
    public int Dimensions { get; set; } = 1536;
    public string ApiKey { get; set; } = string.Empty;
}
