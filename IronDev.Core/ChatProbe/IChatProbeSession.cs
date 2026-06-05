namespace IronDev.Core.ChatProbe;

/// <summary>
/// Port interface that lets <see cref="ChatProbeDriver"/> send chat messages
/// and read responses without depending on IronDev.Client.
/// Adapter implementations live in IronDev.Client or the CLI layer.
/// </summary>
public interface IChatProbeSession
{
    /// <summary>Open a fresh session and return its session ID.</summary>
    Task<long> OpenSessionAsync(int projectId, string title, CancellationToken ct);

    /// <summary>Append a user or assistant message to the session store.</summary>
    Task AppendMessageAsync(int projectId, long sessionId, string role, string content, CancellationToken ct);

    /// <summary>
    /// Send the prompt and return a completion result.
    /// The implementation must call the real chat completion endpoint.
    /// </summary>
    Task<ChatProbeCompletionResult> CompleteAsync(int projectId, long sessionId, string prompt, CancellationToken ct);
}

/// <summary>Result projected from the raw ChatCompletionResponse for probe use.</summary>
public sealed record ChatProbeCompletionResult
{
    public required string Response { get; init; }
    public string? Mode { get; init; }
    public IReadOnlyList<string> GovernanceActions { get; init; } = [];
    public string? DogfoodTraceId { get; init; }
}
