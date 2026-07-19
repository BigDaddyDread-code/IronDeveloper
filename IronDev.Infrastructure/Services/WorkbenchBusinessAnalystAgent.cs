using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Workbench;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchBusinessAnalystAgent : IWorkbenchBusinessAnalystAgent
{
    private static readonly JsonSerializerOptions AuditHashJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IWorkbenchBusinessAnalystExecutableContractRegistry _contracts;
    private readonly IWorkbenchBusinessAnalystSnapshotToolCatalogue _tools;
    private readonly IWorkbenchBusinessAnalystPromptBuilder _prompts;
    private readonly IWorkbenchBusinessAnalystModelGateway _models;
    private readonly IWorkbenchBusinessAnalystPreparationAuditStore _audit;

    public WorkbenchBusinessAnalystAgent(
        IWorkbenchBusinessAnalystExecutableContractRegistry contracts,
        IWorkbenchBusinessAnalystSnapshotToolCatalogue tools,
        IWorkbenchBusinessAnalystPromptBuilder prompts,
        IWorkbenchBusinessAnalystModelGateway models,
        IWorkbenchBusinessAnalystPreparationAuditStore audit)
    {
        _contracts = contracts;
        _tools = tools;
        _prompts = prompts;
        _models = models;
        _audit = audit;
    }

    public async Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(context);
        EnsureClaimMatchesContext(claim, context);

        var contract = _contracts.Resolve(context);
        var toolResults = _tools.ReadAll(context, contract);
        var codeOwnedPrompt = _prompts.Build(context, contract, toolResults);
        var prepared = await _models.PrepareAsync(
                context,
                contract,
                codeOwnedPrompt,
                cancellationToken)
            .ConfigureAwait(false);

        var preparedAtUtc = DateTimeOffset.UtcNow;
        var toolCalls = toolResults.Select(result => ToAudit(context, contract, result)).ToArray();
        var provenance = new WorkbenchBusinessAnalystPreparationProvenance
        {
            AgentRunId = claim.AgentRunId,
            ClaimToken = claim.ClaimToken,
            AttemptNumber = claim.AttemptCount,
            EffectiveAnalystProfileHash = prepared.EffectiveAnalystProfileHash,
            AnalystProfilePublishedVersion = prepared.AnalystProfilePublishedVersion,
            ActualProvider = prepared.ActualProvider,
            ActualModel = prepared.ActualModel,
            ProviderTimeoutSeconds = checked((int)Math.Ceiling(
                prepared.Invocation.ProviderTimeout.TotalSeconds)),
            PromptHash = prepared.PromptHash,
            ToolManifestHash =
                WorkbenchBusinessAnalystPreparationAuditCanonicalizer.ComputeToolManifestHash(contract),
            PreparedAtUtc = preparedAtUtc,
            ToolCalls = toolCalls
        };

        // Preparation audit is durable before the processor performs its final claim/fence
        // authorization. A failed or conflicting audit therefore cannot reach the provider.
        await _audit.RecordAsync(provenance, cancellationToken).ConfigureAwait(false);
        return prepared.Invocation;
    }

    private static WorkbenchBusinessAnalystToolCallAudit ToAudit(
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystExecutableContractDescriptor contract,
        WorkbenchBusinessAnalystSnapshotToolResult result)
    {
        var descriptor = contract.SnapshotTools.Single(tool =>
            string.Equals(tool.Name, result.Name, StringComparison.Ordinal));
        return new WorkbenchBusinessAnalystToolCallAudit
        {
            ToolName = result.Name,
            DefinitionVersion = descriptor.Version,
            PolicyVersion = contract.Key.ToolPolicyVersion,
            Status = WorkbenchBusinessAnalystToolCallAuditStatus.Completed,
            InputHash = Hash(JsonSerializer.Serialize(
                new
                {
                    context.AgentRunId,
                    context.ContextHash,
                    ToolName = descriptor.Name,
                    ToolVersion = descriptor.Version
                },
                AuditHashJsonOptions)),
            OutputHash = Hash(result.PayloadJson),
            SafeSummary = SafeSummary(result.Name),
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc
        };
    }

    private static string SafeSummary(string toolName) => toolName switch
    {
        WorkbenchBusinessAnalystSnapshotToolNames.ProjectIdentity =>
            "Read the immutable project identity snapshot.",
        WorkbenchBusinessAnalystSnapshotToolNames.CapturedUnderstanding =>
            "Read the immutable captured-understanding snapshot.",
        WorkbenchBusinessAnalystSnapshotToolNames.BoundedTrustedConversation =>
            "Read the immutable bounded trusted-conversation snapshot.",
        _ => throw new InvalidOperationException(
            $"Snapshot tool '{toolName}' has no bounded audit summary.")
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void EnsureClaimMatchesContext(
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext context)
    {
        if (claim.AgentRunId == Guid.Empty || claim.ClaimToken == Guid.Empty || claim.AttemptCount <= 0 ||
            claim.AgentRunId != context.AgentRunId || claim.TenantId != context.TenantId ||
            claim.ProjectId != context.ProjectId || claim.WorkbenchSessionId != context.WorkbenchSessionId ||
            claim.LeaseEpoch != context.LeaseEpoch || claim.ChatSessionId != context.ChatSessionId ||
            claim.SourceUserMessageId != context.SourceUserMessageId ||
            !string.Equals(claim.AgentVersion, context.AgentVersion, StringComparison.Ordinal) ||
            !string.Equals(claim.PromptVersion, context.PromptVersion, StringComparison.Ordinal) ||
            !string.Equals(claim.ToolPolicyVersion, context.ToolPolicyVersion, StringComparison.Ordinal) ||
            claim.ContextSchemaVersion != context.ContextSchemaVersion ||
            claim.ContextCanonicalizationVersion != context.ContextCanonicalizationVersion ||
            claim.OutputSchemaVersion != context.OutputSchemaVersion)
            throw new InvalidOperationException(
                "The Business Analyst preparation claim does not match the immutable run context.");
    }
}
