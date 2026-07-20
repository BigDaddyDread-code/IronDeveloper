using IronDev.Core.Agents;

namespace IronDev.Core.Workbench;

public static class WorkbenchBusinessAnalystSnapshotToolNames
{
    public const string ProjectIdentity = "workbench.project-identity.read";
    public const string CapturedUnderstanding = "workbench.captured-understanding.read";
    public const string BoundedTrustedConversation = "workbench.bounded-trusted-conversation.read";

    public static readonly IReadOnlyList<string> All =
    [
        ProjectIdentity,
        CapturedUnderstanding,
        BoundedTrustedConversation
    ];
}

public sealed record WorkbenchBusinessAnalystContractKey(
    string AgentVersion,
    string PromptVersion,
    string ToolPolicyVersion,
    int ContextSchemaVersion,
    int ContextCanonicalizationVersion,
    int OutputSchemaVersion)
{
    public static WorkbenchBusinessAnalystContractKey FromContext(
        WorkbenchBusinessAnalystContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new WorkbenchBusinessAnalystContractKey(
            context.AgentVersion,
            context.PromptVersion,
            context.ToolPolicyVersion,
            context.ContextSchemaVersion,
            context.ContextCanonicalizationVersion,
            context.OutputSchemaVersion);
    }
}

public sealed record WorkbenchBusinessAnalystSnapshotToolDescriptor
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required string OutputContract { get; init; }
    public bool MutatesState { get; init; }
    public bool AllowsFileSystemAccess { get; init; }
    public bool AllowsProcessExecution { get; init; }
    public bool AllowsNetworkAccess { get; init; }
    public bool AllowsWorkspaceMutation { get; init; }
    public bool AllowsBuilderAccess { get; init; }
    public bool AcceptsCallerScope { get; init; }
}

public sealed record WorkbenchBusinessAnalystOutputContractDescriptor
{
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<string> RequiredProperties { get; init; }
    public required IReadOnlyList<string> AllowedOutcomes { get; init; }
    public required int MaximumAssistantMessageCharacters { get; init; }
    public bool AllowsAdditionalProperties { get; init; }
}

public sealed record WorkbenchBusinessAnalystExecutableContractDescriptor
{
    public required WorkbenchBusinessAnalystContractKey Key { get; init; }
    public required SkeletonAgentRole AgentRole { get; init; }
    public required IReadOnlyList<WorkbenchBusinessAnalystSnapshotToolDescriptor> SnapshotTools { get; init; }
    public required WorkbenchBusinessAnalystOutputContractDescriptor Output { get; init; }
}

public sealed record WorkbenchBusinessAnalystSnapshotToolResult
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public interface IWorkbenchBusinessAnalystExecutableContractRegistry
{
    IReadOnlyList<WorkbenchBusinessAnalystExecutableContractDescriptor> List();

    WorkbenchBusinessAnalystExecutableContractDescriptor Resolve(
        WorkbenchBusinessAnalystContext context);
}

public interface IWorkbenchBusinessAnalystSnapshotTool
{
    WorkbenchBusinessAnalystSnapshotToolDescriptor Descriptor { get; }

    WorkbenchBusinessAnalystSnapshotToolResult Read(
        WorkbenchBusinessAnalystContext context);
}

public interface IWorkbenchBusinessAnalystSnapshotToolCatalogue
{
    IReadOnlyList<WorkbenchBusinessAnalystSnapshotToolDescriptor> List();

    IReadOnlyList<WorkbenchBusinessAnalystSnapshotToolResult> ReadAll(
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystExecutableContractDescriptor contract);
}

public interface IWorkbenchBusinessAnalystPromptBuilder
{
    WorkbenchBusinessAnalystPromptParts Build(
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystExecutableContractDescriptor contract,
        IReadOnlyList<WorkbenchBusinessAnalystSnapshotToolResult> toolResults);
}

public interface IWorkbenchBusinessAnalystModelGateway
{
    Task<WorkbenchBusinessAnalystPreparedModel> PrepareAsync(
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystExecutableContractDescriptor contract,
        WorkbenchBusinessAnalystPromptParts promptParts,
        CancellationToken cancellationToken = default);
}

public sealed record WorkbenchBusinessAnalystPreparedModel
{
    public required IWorkbenchBusinessAnalystPreparedInvocation Invocation { get; init; }
    public required string EffectiveAnalystProfileHash { get; init; }
    public long? AnalystProfilePublishedVersion { get; init; }
    public required string ActualProvider { get; init; }
    public required string ActualModel { get; init; }
    public required string PromptHash { get; init; }
    public required WorkbenchBusinessAnalystContextBudgetMeasurement ContextBudget { get; init; }
}

public sealed class WorkbenchBusinessAnalystContractNotSupportedException : Exception
{
    public WorkbenchBusinessAnalystContractNotSupportedException(
        WorkbenchBusinessAnalystContractKey key)
        : base(
            "No executable Workbench Business Analyst contract is registered for " +
            $"agent='{key.AgentVersion}', prompt='{key.PromptVersion}', " +
            $"tools='{key.ToolPolicyVersion}', context=" +
            $"{key.ContextSchemaVersion}/{key.ContextCanonicalizationVersion}, " +
            $"output={key.OutputSchemaVersion}.")
    {
        Key = key;
    }

    public WorkbenchBusinessAnalystContractKey Key { get; }
}
