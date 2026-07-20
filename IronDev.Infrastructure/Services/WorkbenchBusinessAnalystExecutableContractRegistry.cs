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

    private static readonly WorkbenchBusinessAnalystOutputContractDescriptor OutputV1 = new()
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
        MaximumAssistantMessageCharacters =
            WorkbenchBusinessAnalystProviderContract.MaximumAssistantMessageCharacters,
        AllowsAdditionalProperties = false
    };

    private static readonly WorkbenchBusinessAnalystOutputContractDescriptor OutputV2 = new()
    {
        SchemaVersion = WorkbenchBusinessAnalystContract.OutputSchemaVersion2,
        RequiredProperties =
        [
            "outputSchemaVersion",
            "contextHash",
            "basedOnUnderstandingRevision",
            "outcome",
            "assistantMessage",
            "understandingPatch",
            "renameProposal"
        ],
        AllowedOutcomes =
        [
            WorkbenchAgentRunStates.Completed,
            WorkbenchAgentRunStates.NeedsInput
        ],
        MaximumAssistantMessageCharacters =
            WorkbenchBusinessAnalystProviderContract.MaximumAssistantMessageCharacters,
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
                WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1,
                WorkbenchBusinessAnalystContract.PromptVersion1,
                OutputV1),
            Descriptor(
                WorkbenchBusinessAnalystContract.ContextSchemaVersion2,
                WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2,
                WorkbenchBusinessAnalystContract.PromptVersion1,
                OutputV1),
            Descriptor(
                WorkbenchBusinessAnalystContract.ContextSchemaVersion2,
                WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2,
                WorkbenchBusinessAnalystContract.PromptVersion2,
                OutputV2)
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
        int contextCanonicalizationVersion,
        string promptVersion,
        WorkbenchBusinessAnalystOutputContractDescriptor output) =>
        new()
        {
            Key = new WorkbenchBusinessAnalystContractKey(
                WorkbenchBusinessAnalystContract.AgentVersion,
                promptVersion,
                WorkbenchBusinessAnalystContract.ToolPolicyVersion,
                contextSchemaVersion,
                contextCanonicalizationVersion,
                output.SchemaVersion),
            AgentRole = SkeletonAgentRole.Analyst,
            SnapshotTools = SnapshotTools,
            Output = output
        };

}
