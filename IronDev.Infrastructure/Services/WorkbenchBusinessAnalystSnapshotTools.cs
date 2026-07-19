using System.Text.Json;
using IronDev.Core.Workbench;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchBusinessAnalystProjectIdentitySnapshotTool
    : IWorkbenchBusinessAnalystSnapshotTool
{
    public WorkbenchBusinessAnalystSnapshotToolDescriptor Descriptor =>
        WorkbenchBusinessAnalystSnapshotToolDescriptors.ProjectIdentity;

    public WorkbenchBusinessAnalystSnapshotToolResult Read(
        WorkbenchBusinessAnalystContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var startedAtUtc = DateTimeOffset.UtcNow;
        return Result(JsonSerializer.Serialize(
            new
            {
                context.ProjectId,
                context.ProjectName
            },
            WorkbenchBusinessAnalystSnapshotJson.Options), startedAtUtc);
    }

    private WorkbenchBusinessAnalystSnapshotToolResult Result(
        string payloadJson,
        DateTimeOffset startedAtUtc) =>
        new()
        {
            Name = Descriptor.Name,
            Version = Descriptor.Version,
            PayloadJson = payloadJson,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
}

public sealed class WorkbenchBusinessAnalystCapturedUnderstandingSnapshotTool
    : IWorkbenchBusinessAnalystSnapshotTool
{
    public WorkbenchBusinessAnalystSnapshotToolDescriptor Descriptor =>
        WorkbenchBusinessAnalystSnapshotToolDescriptors.CapturedUnderstanding;

    public WorkbenchBusinessAnalystSnapshotToolResult Read(
        WorkbenchBusinessAnalystContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var startedAtUtc = DateTimeOffset.UtcNow;
        using var understanding = JsonDocument.Parse(context.UnderstandingJson);
        return Result(JsonSerializer.Serialize(
            new
            {
                context.UnderstandingRevision,
                Understanding = understanding.RootElement
            },
            WorkbenchBusinessAnalystSnapshotJson.Options), startedAtUtc);
    }

    private WorkbenchBusinessAnalystSnapshotToolResult Result(
        string payloadJson,
        DateTimeOffset startedAtUtc) =>
        new()
        {
            Name = Descriptor.Name,
            Version = Descriptor.Version,
            PayloadJson = payloadJson,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
}

public sealed class WorkbenchBusinessAnalystBoundedTrustedConversationSnapshotTool
    : IWorkbenchBusinessAnalystSnapshotTool
{
    public WorkbenchBusinessAnalystSnapshotToolDescriptor Descriptor =>
        WorkbenchBusinessAnalystSnapshotToolDescriptors.BoundedTrustedConversation;

    public WorkbenchBusinessAnalystSnapshotToolResult Read(
        WorkbenchBusinessAnalystContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var startedAtUtc = DateTimeOffset.UtcNow;
        return Result(JsonSerializer.Serialize(
            new
            {
                context.SourceUserMessageId,
                Messages = context.Messages.Select(message => new
                {
                    message.MessageId,
                    message.Role,
                    message.Message,
                    message.CreatedAtUtc
                })
            },
            WorkbenchBusinessAnalystSnapshotJson.Options), startedAtUtc);
    }

    private WorkbenchBusinessAnalystSnapshotToolResult Result(
        string payloadJson,
        DateTimeOffset startedAtUtc) =>
        new()
        {
            Name = Descriptor.Name,
            Version = Descriptor.Version,
            PayloadJson = payloadJson,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
}

public sealed class WorkbenchBusinessAnalystSnapshotToolCatalogue
    : IWorkbenchBusinessAnalystSnapshotToolCatalogue
{
    private readonly IReadOnlyDictionary<string, IWorkbenchBusinessAnalystSnapshotTool> _tools;

    public WorkbenchBusinessAnalystSnapshotToolCatalogue()
        : this(
        [
            new WorkbenchBusinessAnalystProjectIdentitySnapshotTool(),
            new WorkbenchBusinessAnalystCapturedUnderstandingSnapshotTool(),
            new WorkbenchBusinessAnalystBoundedTrustedConversationSnapshotTool()
        ])
    {
    }

    internal WorkbenchBusinessAnalystSnapshotToolCatalogue(
        IEnumerable<IWorkbenchBusinessAnalystSnapshotTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        try
        {
            _tools = tools.ToDictionary(tool => tool.Descriptor.Name, StringComparer.Ordinal);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "The Workbench Business Analyst snapshot tool catalogue contains duplicate names.",
                exception);
        }
    }

    public IReadOnlyList<WorkbenchBusinessAnalystSnapshotToolDescriptor> List() =>
        WorkbenchBusinessAnalystSnapshotToolNames.All
            .Where(_tools.ContainsKey)
            .Select(name => _tools[name].Descriptor)
            .ToArray();

    public IReadOnlyList<WorkbenchBusinessAnalystSnapshotToolResult> ReadAll(
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystExecutableContractDescriptor contract)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contract);

        if (WorkbenchBusinessAnalystContractKey.FromContext(context) != contract.Key)
            throw new InvalidOperationException(
                "The Workbench Business Analyst snapshot tool contract does not match the immutable run context.");

        var allowedNames = contract.SnapshotTools.Select(tool => tool.Name).ToArray();
        if (allowedNames.Distinct(StringComparer.Ordinal).Count() != allowedNames.Length ||
            allowedNames.Length != WorkbenchBusinessAnalystSnapshotToolNames.All.Count ||
            !allowedNames.SequenceEqual(WorkbenchBusinessAnalystSnapshotToolNames.All, StringComparer.Ordinal) ||
            _tools.Count != allowedNames.Length ||
            _tools.Keys.Any(name => !allowedNames.Contains(name, StringComparer.Ordinal)))
            throw new InvalidOperationException(
                "The Workbench Business Analyst snapshot tool catalogue is not the exact versioned read-only allowlist.");

        var results = new List<WorkbenchBusinessAnalystSnapshotToolResult>(allowedNames.Length);
        foreach (var allowed in contract.SnapshotTools)
        {
            EnsureReadOnly(allowed);
            if (!_tools.TryGetValue(allowed.Name, out var tool) || tool.Descriptor != allowed)
                throw new InvalidOperationException(
                    $"Snapshot tool '{allowed.Name}' is missing or does not match its executable contract.");

            var result = tool.Read(context);
            if (!string.Equals(result.Name, allowed.Name, StringComparison.Ordinal) ||
                !string.Equals(result.Version, allowed.Version, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Snapshot tool '{allowed.Name}' returned mismatched provenance.");
            using (JsonDocument.Parse(result.PayloadJson))
            {
                results.Add(result);
            }
        }

        return results;
    }

    private static void EnsureReadOnly(WorkbenchBusinessAnalystSnapshotToolDescriptor descriptor)
    {
        if (descriptor.MutatesState || descriptor.AllowsFileSystemAccess ||
            descriptor.AllowsProcessExecution || descriptor.AllowsNetworkAccess ||
            descriptor.AllowsWorkspaceMutation || descriptor.AllowsBuilderAccess ||
            descriptor.AcceptsCallerScope)
            throw new InvalidOperationException(
                $"Snapshot tool '{descriptor.Name}' is not a read-only immutable-context tool.");
    }
}

internal static class WorkbenchBusinessAnalystSnapshotToolDescriptors
{
    internal static readonly WorkbenchBusinessAnalystSnapshotToolDescriptor ProjectIdentity =
        Create(
            WorkbenchBusinessAnalystSnapshotToolNames.ProjectIdentity,
            "Reads the project identity captured in the immutable agent-run context.",
            "A JSON object containing only projectId and projectName.");

    internal static readonly WorkbenchBusinessAnalystSnapshotToolDescriptor CapturedUnderstanding =
        Create(
            WorkbenchBusinessAnalystSnapshotToolNames.CapturedUnderstanding,
            "Reads the project-understanding revision captured in the immutable agent-run context.",
            "A JSON object containing understandingRevision and understanding.");

    internal static readonly WorkbenchBusinessAnalystSnapshotToolDescriptor BoundedTrustedConversation =
        Create(
            WorkbenchBusinessAnalystSnapshotToolNames.BoundedTrustedConversation,
            "Reads the bounded same-conversation messages captured in the immutable agent-run context.",
            "A JSON object containing sourceUserMessageId and the captured ordered messages.");

    private static WorkbenchBusinessAnalystSnapshotToolDescriptor Create(
        string name,
        string description,
        string outputContract) =>
        new()
        {
            Name = name,
            Version = WorkbenchBusinessAnalystContract.ToolPolicyVersion,
            Description = description,
            OutputContract = outputContract,
            MutatesState = false,
            AllowsFileSystemAccess = false,
            AllowsProcessExecution = false,
            AllowsNetworkAccess = false,
            AllowsWorkspaceMutation = false,
            AllowsBuilderAccess = false,
            AcceptsCallerScope = false
        };
}

internal static class WorkbenchBusinessAnalystSnapshotJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
