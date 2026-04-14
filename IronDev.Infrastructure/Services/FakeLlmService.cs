using IronDev.Core;

namespace IronDev.Infrastructure.Services;

public class FakeLlmService : ILLMService
{
    public Task<string> GetResponseAsync(string prompt)
    {
        return Task.FromResult($"Received prompt: {prompt}");
    }
}