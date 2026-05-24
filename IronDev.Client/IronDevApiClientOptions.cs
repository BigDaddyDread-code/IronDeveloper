namespace IronDev.Client;

public sealed class IronDevApiClientOptions
{
    public Uri BaseAddress { get; set; } = new("https://localhost:5001/");
}
