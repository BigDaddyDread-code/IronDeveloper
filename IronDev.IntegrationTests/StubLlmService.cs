using IronDev.Core;

namespace IronDev.IntegrationTests;

internal sealed class StubLlmService : ILLMService
{
    private readonly Queue<string> _responses;

    public StubLlmService(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public List<string> ReceivedPrompts { get; } = [];

    public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
    {
        ReceivedPrompts.Add(prompt);
        if (prompt.Contains("You are the Context Agent route judge", StringComparison.Ordinal) && _responses.Count == 0)
            return Task.FromResult("INVALID_JSON");

        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : """{"isSufficient":true,"confidence":8,"reason":"Stub fallback.","requestedContext":{"codeSearchQueries":[],"clarificationQuestions":[]}}""";

        return Task.FromResult(response);
    }
}
