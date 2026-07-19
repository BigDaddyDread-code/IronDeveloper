using IronDev.Core.Agents;
using IronDev.Core.Workbench;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchBusinessAnalystExecutableContractRegistry
    : IWorkbenchBusinessAnalystExecutableContractRegistry
{
    private static readonly IReadOnlyList<WorkbenchBusinessAnalystSnapshotToolDescriptor> SnapshotTools =
    [
        WorkbenchBusinessAnalystSnapshotToolDescriptors.ProjectIdentity,
        WorkbenchBusinessAnalystSnapshotToolDescriptors.CapturedUnderstanding,
        WorkbenchBusinessAnalystSnapshotToolDescriptors.BoundedTrustedConversation
    ];

    private static readonly WorkbenchBusinessAnalystOutputContractDescriptor Output = new()
    {
        SchemaVersion = WorkbenchBusinessAnalystContract.OutputSchemaVersion1,
        RequiredProperties =
        [
            "outputSchemaVersion",
            "contextHash",
            "basedOnUnderstandingRevision",
            "outcome",
            "assistantMessage"
        ],
        AllowedOutcomes =
        [
            WorkbenchAgentRunStates.Completed,
            WorkbenchAgentRunStates.NeedsInput
        ],
        MaximumAssistantMessageCharacters = 100_000,
        AllowsAdditionalProperties = false
    };

    private readonly IReadOnlyDictionary<WorkbenchBusinessAnalystContractKey,
        WorkbenchBusinessAnalystExecutableContractDescriptor> _contracts;

    public WorkbenchBusinessAnalystExecutableContractRegistry()
    {
        var descriptors = new[]
        {
            Descriptor(
                WorkbenchBusinessAnalystContract.ContextSchemaVersion1,
                WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1),
            Descriptor(
                WorkbenchBusinessAnalystContract.ContextSchemaVersion2,
                WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2)
        };
        _contracts = descriptors.ToDictionary(descriptor => descriptor.Key);
    }

    public IReadOnlyList<WorkbenchBusinessAnalystExecutableContractDescriptor> List() =>
        _contracts.Values
            .OrderBy(contract => contract.Key.ContextSchemaVersion)
            .ThenBy(contract => contract.Key.ContextCanonicalizationVersion)
            .ToArray();

    public WorkbenchBusinessAnalystExecutableContractDescriptor Resolve(
        WorkbenchBusinessAnalystContext context)
    {
        var key = WorkbenchBusinessAnalystContractKey.FromContext(context);
        return _contracts.TryGetValue(key, out var descriptor)
            ? descriptor
            : throw new WorkbenchBusinessAnalystContractNotSupportedException(key);
    }

    private static WorkbenchBusinessAnalystExecutableContractDescriptor Descriptor(
        int contextSchemaVersion,
        int contextCanonicalizationVersion) =>
        new()
        {
            Key = new WorkbenchBusinessAnalystContractKey(
                WorkbenchBusinessAnalystContract.AgentVersion,
                WorkbenchBusinessAnalystContract.PromptVersion,
                WorkbenchBusinessAnalystContract.ToolPolicyVersion,
                contextSchemaVersion,
                contextCanonicalizationVersion,
                WorkbenchBusinessAnalystContract.OutputSchemaVersion1),
            AgentRole = SkeletonAgentRole.Analyst,
            SnapshotTools = SnapshotTools,
            Output = Output
        };

}
