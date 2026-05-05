using IronDev.Core;

namespace IronDev.Infrastructure.Services;

public class FakeLlmService : ILLMService
{
    public Task<string> GetResponseAsync(string prompt, System.Threading.CancellationToken ct = default)
    {
        throw new NotImplementedException("Real LLM must be configured. Fake implementation is disabled.");
    }
}