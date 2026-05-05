namespace IronDev.Core.Models;

public sealed class LlmOptions
{
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-4o";
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}
