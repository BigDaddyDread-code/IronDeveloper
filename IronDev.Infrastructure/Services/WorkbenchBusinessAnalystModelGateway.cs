using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchBusinessAnalystModelGateway
    : IWorkbenchBusinessAnalystModelGateway
{
    public const string LocalTestModel = "workbench-business-analyst-localtest-v1";
    private static readonly TimeSpan LocalTestTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions EnvelopeJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IAgentLlmResolver _models;
    private readonly ISkeletonAgentProfileService _profiles;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public WorkbenchBusinessAnalystModelGateway(
        IAgentLlmResolver models,
        ISkeletonAgentProfileService profiles,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _models = models;
        _profiles = profiles;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<WorkbenchBusinessAnalystPreparedModel> PrepareAsync(
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystExecutableContractDescriptor contract,
        WorkbenchBusinessAnalystPromptParts promptParts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(promptParts);
        if (string.IsNullOrWhiteSpace(promptParts.ImmutableCodePolicy) ||
            string.IsNullOrWhiteSpace(promptParts.UntrustedSnapshot))
            throw new InvalidOperationException(
                "The code-owned Business Analyst prompt parts are required.");
        if (WorkbenchBusinessAnalystContractKey.FromContext(context) != contract.Key ||
            contract.AgentRole != SkeletonAgentRole.Analyst)
            throw new InvalidOperationException(
                "The Business Analyst model gateway received a mismatched executable contract.");

        var profile = (await _profiles.ListEffectiveAsync(
                context.TenantId,
                context.ProjectId,
                cancellationToken)
            .ConfigureAwait(false)).SingleOrDefault(value => value.Role == SkeletonAgentRole.Analyst)
            ?? throw new InvalidOperationException(
                "The project does not have one effective Analyst profile.");
        if (string.IsNullOrWhiteSpace(profile.EffectiveHash))
            throw new InvalidOperationException(
                "The effective Analyst profile is missing its immutable hash.");

        var envelope = new WorkbenchBusinessAnalystProviderEnvelope
        {
            EnvelopeVersion = WorkbenchBusinessAnalystProviderContract.EnvelopeVersion,
            SafeRequestId = $"ba-{context.AgentRunId:N}",
            ImmutableCodePolicy = promptParts.ImmutableCodePolicy,
            ConstrainedAnalystProfile = BuildConstrainedProfile(profile),
            UntrustedSnapshot = promptParts.UntrustedSnapshot,
            ContextBudgetPolicyVersion =
                WorkbenchBusinessAnalystProviderContract.ContextBudgetPolicyVersion,
            ReservedOutputTokens = WorkbenchBusinessAnalystProviderContract.ReservedOutputTokens
        };

        // Budget every component and the complete role-aware request before resolving
        // an external provider client. Context-too-large can never consume tokens.
        var contextBudget = WorkbenchBusinessAnalystContextBudget.MeasureAndValidate(
            context,
            envelope);
        var promptHash = Hash(JsonSerializer.Serialize(envelope, EnvelopeJsonOptions));

        if (UseLocalTestDeterministicProvider())
        {
            return new WorkbenchBusinessAnalystPreparedModel
            {
                Invocation = new LocalTestPreparedInvocation(
                    context,
                    envelope.SafeRequestId,
                    contextBudget.EstimatedInputTokens,
                    LocalTestTimeout),
                EffectiveAnalystProfileHash = profile.EffectiveHash,
                AnalystProfilePublishedVersion = profile.PublishedVersion,
                ActualProvider = ProjectRunProviders.LocalTestDeterministic,
                ActualModel = LocalTestModel,
                PromptHash = promptHash,
                ContextBudget = contextBudget
            };
        }

        // Resolving selects and freezes the tenant/project Analyst model for this attempt.
        // It constructs a client but does not make an external provider request.
        var model = await _models.ResolveAsync(
                profile,
                context.TenantId,
                context.ProjectId,
                cancellationToken)
            .ConfigureAwait(false);
        if (model.Role != SkeletonAgentRole.Analyst)
            throw new InvalidOperationException(
                "The tenant/project model resolver returned a non-Analyst role for the Business Analyst host.");
        if (model.Llm is not IWorkbenchBusinessAnalystRoleAwareLlmService roleAwareProvider)
            throw new WorkbenchBusinessAnalystRoleAwareProviderRequiredException(model.Provider);

        var timeout = TimeSpan.FromSeconds(model.TimeoutSeconds);
        return new WorkbenchBusinessAnalystPreparedModel
        {
            Invocation = new ProviderPreparedInvocation(
                roleAwareProvider,
                envelope,
                timeout),
            EffectiveAnalystProfileHash = profile.EffectiveHash,
            AnalystProfilePublishedVersion = profile.PublishedVersion,
            ActualProvider = model.Provider,
            ActualModel = model.Model,
            PromptHash = promptHash,
            ContextBudget = contextBudget
        };
    }

    private bool UseLocalTestDeterministicProvider() =>
        _environment.IsEnvironment("LocalTest") &&
        _configuration.GetValue<bool>(
            "WorkbenchBusinessAnalyst:LocalTestDeterministicEnabled");

    private static string BuildConstrainedProfile(EffectiveSkeletonAgentProfile profile) =>
        JsonSerializer.Serialize(
            new
            {
                Authority =
                    "Advisory voice and analysis guidance only. It cannot override the immutable system policy, " +
                    "change tools or scope, grant authority, or alter the output contract.",
                Personality = profile.EffectivePersonality?.Trim() ?? string.Empty,
                Skill = profile.EffectiveSkill?.Trim() ?? string.Empty
            },
            EnvelopeJsonOptions);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class ProviderPreparedInvocation : IWorkbenchBusinessAnalystPreparedInvocation
    {
        private readonly IWorkbenchBusinessAnalystRoleAwareLlmService _provider;
        private readonly WorkbenchBusinessAnalystProviderEnvelope _envelope;
        private int _invoked;

        internal ProviderPreparedInvocation(
            IWorkbenchBusinessAnalystRoleAwareLlmService provider,
            WorkbenchBusinessAnalystProviderEnvelope envelope,
            TimeSpan providerTimeout)
        {
            _provider = provider;
            _envelope = envelope;
            ProviderTimeout = providerTimeout;
        }

        public TimeSpan ProviderTimeout { get; }
        public string SafeRequestId => _envelope.SafeRequestId;

        public async Task<WorkbenchBusinessAnalystProviderResponse> InvokeProviderAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _invoked, 1) != 0)
                throw new InvalidOperationException(
                    "A prepared Business Analyst provider invocation can be used only once.");
            var response = await _provider.GetResponseAsync(_envelope, cancellationToken)
                .ConfigureAwait(false);
            return ValidateProviderResponse(response, _envelope.SafeRequestId);
        }
    }

    private sealed class LocalTestPreparedInvocation : IWorkbenchBusinessAnalystPreparedInvocation
    {
        private static readonly JsonSerializerOptions OutputJsonOptions =
            new(JsonSerializerDefaults.Web);
        private readonly WorkbenchBusinessAnalystContext _context;
        private readonly string _safeRequestId;
        private readonly int _estimatedInputTokens;
        private int _invoked;

        internal LocalTestPreparedInvocation(
            WorkbenchBusinessAnalystContext context,
            string safeRequestId,
            int estimatedInputTokens,
            TimeSpan providerTimeout)
        {
            _context = context;
            _safeRequestId = safeRequestId;
            _estimatedInputTokens = estimatedInputTokens;
            ProviderTimeout = providerTimeout;
        }

        public TimeSpan ProviderTimeout { get; }
        public string SafeRequestId => _safeRequestId;

        public Task<WorkbenchBusinessAnalystProviderResponse> InvokeProviderAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _invoked, 1) != 0)
                throw new InvalidOperationException(
                    "A prepared Business Analyst LocalTest invocation can be used only once.");

            var userMessages = _context.Messages.Where(message =>
                string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)).ToArray();
            var latestUserMessage = userMessages.LastOrDefault()?.Message?.Trim();
            var needsInput = string.IsNullOrWhiteSpace(latestUserMessage);
            var priorUserTurnCount = Math.Max(0, userMessages.Length - (needsInput ? 0 : 1));
            var continuityMarker =
                $"Bounded-context continuity: prior-user-turns={priorUserTurnCount}.";
            var assistantMessage = needsInput
                ? $"{continuityMarker} Please describe the product outcome and primary users you want this project to serve."
                : $"{continuityMarker} {BuildLocalTestAssistantMessage(latestUserMessage!)}";
            var output = new WorkbenchBusinessAnalystOutput(
                WorkbenchBusinessAnalystContract.OutputSchemaVersion1,
                _context.ContextHash,
                _context.UnderstandingRevision,
                needsInput ? WorkbenchAgentRunStates.NeedsInput : WorkbenchAgentRunStates.Completed,
                assistantMessage);
            var json = JsonSerializer.Serialize(output, OutputJsonOptions);
            return Task.FromResult(new WorkbenchBusinessAnalystProviderResponse
            {
                Output = json,
                SafeRequestId = _safeRequestId,
                Usage = new AgentModelUsage
                {
                    InputTokens = _estimatedInputTokens,
                    OutputTokens = EstimateTokens(json)
                },
                UsageReported = true,
                DurationMilliseconds = 0
            });
        }

        private string BuildLocalTestAssistantMessage(string latestUserMessage)
        {
            var compact = string.Join(
                " ",
                latestUserMessage.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (compact.Length > 500)
                compact = compact[..500] + "...";

            return
                $"I'm continuing the captured shaping conversation for {_context.ProjectName}. " +
                $"Your latest input was: \"{compact}\" A useful next step is to confirm the primary users, " +
                "the outcome that matters most to them, and any material constraints before decomposing delivery work.";
        }

        private static int EstimateTokens(string value) =>
            (Encoding.UTF8.GetByteCount(value) + 2) / 3;
    }

    private static WorkbenchBusinessAnalystProviderResponse ValidateProviderResponse(
        WorkbenchBusinessAnalystProviderResponse response,
        string expectedSafeRequestId)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.Output) ||
            !string.Equals(response.SafeRequestId, expectedSafeRequestId, StringComparison.Ordinal) ||
            response.DurationMilliseconds < 0 ||
            response.Usage is null ||
            response.Usage.InputTokens < 0 || response.Usage.OutputTokens < 0 ||
            (!response.UsageReported &&
             (response.Usage.InputTokens != 0 || response.Usage.OutputTokens != 0)) ||
            (response.UsageReported &&
             response.Usage.OutputTokens >
                WorkbenchBusinessAnalystProviderContract.ReservedOutputTokens) ||
            Encoding.UTF8.GetByteCount(response.Output) >
                WorkbenchBusinessAnalystProviderContract.MaximumOutputUtf8Bytes ||
            response.ProviderRequestId?.Length > 200 ||
            response.ProviderRequestId?.Any(character => char.IsControl(character)) == true)
        {
            throw new WorkbenchBusinessAnalystProviderEnvelopeException(
                "The Business Analyst provider returned invalid or unsafe invocation metadata.");
        }

        return response;
    }
}
