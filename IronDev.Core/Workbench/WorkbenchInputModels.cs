namespace IronDev.Core.Workbench;

public static class WorkbenchInputKinds
{
    public const string Conversation = "Conversation";
    public const string Help = "Help";
    public const string Ticket = "Ticket";
    public const string CommandRejected = "CommandRejected";
    public const string AgentRun = "AgentRun";
}

public static class WorkbenchSlashCommands
{
    public const string Help = "/help";
    public const string Ticket = "/ticket";
}

public static class WorkbenchCommandRejectionReasons
{
    public const string UnknownCommand = "UnknownCommand";
}

public sealed record WorkbenchInputRoute(
    string Kind,
    string? RawCommandToken,
    string? NormalizedCommand,
    string? Instruction)
{
    public bool IsCommand => Kind != WorkbenchInputKinds.Conversation;
}

/// <summary>
/// Deterministic, repository-independent Workbench composer routing. Only a slash at the
/// first non-whitespace character starts a command; slash-like prose remains conversation.
/// </summary>
public static class WorkbenchInputRouter
{
    public static WorkbenchInputRoute Parse(string? composerText)
    {
        if (composerText is null)
            return new WorkbenchInputRoute(WorkbenchInputKinds.Conversation, null, null, null);

        var firstMeaningful = 0;
        while (firstMeaningful < composerText.Length && char.IsWhiteSpace(composerText[firstMeaningful]))
            firstMeaningful++;

        if (firstMeaningful >= composerText.Length || composerText[firstMeaningful] != '/')
            return new WorkbenchInputRoute(WorkbenchInputKinds.Conversation, null, null, null);

        var tokenEnd = firstMeaningful;
        while (tokenEnd < composerText.Length && !char.IsWhiteSpace(composerText[tokenEnd]))
            tokenEnd++;

        var rawToken = composerText[firstMeaningful..tokenEnd];
        var normalized = rawToken.ToLowerInvariant();
        var instruction = tokenEnd < composerText.Length
            ? composerText[tokenEnd..].Trim()
            : null;
        if (instruction?.Length == 0)
            instruction = null;

        return normalized switch
        {
            WorkbenchSlashCommands.Help => new WorkbenchInputRoute(
                WorkbenchInputKinds.Help,
                rawToken,
                WorkbenchSlashCommands.Help,
                instruction),
            WorkbenchSlashCommands.Ticket => new WorkbenchInputRoute(
                WorkbenchInputKinds.Ticket,
                rawToken,
                WorkbenchSlashCommands.Ticket,
                instruction),
            _ => new WorkbenchInputRoute(
                WorkbenchInputKinds.CommandRejected,
                rawToken,
                null,
                instruction)
        };
    }
}

public sealed record DispatchWorkbenchInputCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long? ChatSessionId,
    string ComposerText);

public sealed record DispatchWorkbenchInputResult(
    string Kind,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    string? NormalizedCommand,
    string? Instruction,
    string? Title,
    string? Message,
    bool IsReplay,
    SubmitWorkbenchAgentRunResult? AgentRun = null,
    string? RawCommandToken = null,
    string? ReasonCode = null);

public interface IWorkbenchInputService
{
    Task<DispatchWorkbenchInputResult> DispatchAsync(
        DispatchWorkbenchInputCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class WorkbenchInputValidationException : Exception
{
    public WorkbenchInputValidationException(string message) : base(message)
    {
    }
}

public sealed class WorkbenchCommandRoutingRequiredException : Exception
{
    public const string ErrorCode = "workbench_input_route_required";

    public WorkbenchCommandRoutingRequiredException()
        : base("Slash commands must be submitted through the authoritative Workbench input route.")
    {
    }
}
