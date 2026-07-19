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
        string codeOwnedPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(codeOwnedPrompt))
            throw new InvalidOperationException("The code-owned Business Analyst prompt is required.");
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

        var prompt = SkeletonAgentPromptComposer.Compose(profile, codeOwnedPrompt);
        var promptHash = Hash(prompt);

        if (UseLocalTestDeterministicProvider())
        {
            return new WorkbenchBusinessAnalystPreparedModel
            {
                Invocation = new LocalTestPreparedInvocation(context, LocalTestTimeout),
                EffectiveAnalystProfileHash = profile.EffectiveHash,
                AnalystProfilePublishedVersion = profile.PublishedVersion,
                ActualProvider = ProjectRunProviders.LocalTestDeterministic,
                ActualModel = LocalTestModel,
                PromptHash = promptHash
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

        var timeout = TimeSpan.FromSeconds(model.TimeoutSeconds);
        return new WorkbenchBusinessAnalystPreparedModel
        {
            Invocation = new ProviderPreparedInvocation(model.Llm, prompt, timeout),
            EffectiveAnalystProfileHash = profile.EffectiveHash,
            AnalystProfilePublishedVersion = profile.PublishedVersion,
            ActualProvider = model.Provider,
            ActualModel = model.Model,
            PromptHash = promptHash
        };
    }

    private bool UseLocalTestDeterministicProvider() =>
        _environment.IsEnvironment("LocalTest") &&
        _configuration.GetValue<bool>(
            "WorkbenchBusinessAnalyst:LocalTestDeterministicEnabled");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class ProviderPreparedInvocation : IWorkbenchBusinessAnalystPreparedInvocation
    {
        private readonly IronDev.Core.ILLMService _provider;
        private readonly string _prompt;
        private int _invoked;

        internal ProviderPreparedInvocation(
            IronDev.Core.ILLMService provider,
            string prompt,
            TimeSpan providerTimeout)
        {
            _provider = provider;
            _prompt = prompt;
            ProviderTimeout = providerTimeout;
        }

        public TimeSpan ProviderTimeout { get; }

        public Task<string> InvokeProviderAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _invoked, 1) != 0)
                throw new InvalidOperationException(
                    "A prepared Business Analyst provider invocation can be used only once.");
            return _provider.GetResponseAsync(_prompt, cancellationToken);
        }
    }

    private sealed class LocalTestPreparedInvocation : IWorkbenchBusinessAnalystPreparedInvocation
    {
        private static readonly JsonSerializerOptions OutputJsonOptions =
            new(JsonSerializerDefaults.Web);
        private readonly WorkbenchBusinessAnalystContext _context;
        private int _invoked;

        internal LocalTestPreparedInvocation(
            WorkbenchBusinessAnalystContext context,
            TimeSpan providerTimeout)
        {
            _context = context;
            ProviderTimeout = providerTimeout;
        }

        public TimeSpan ProviderTimeout { get; }

        public Task<string> InvokeProviderAsync(
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
            return Task.FromResult(JsonSerializer.Serialize(output, OutputJsonOptions));
        }

        private string BuildLocalTestAssistantMessage(string latestUserMessage)
        {
            var compact = string.Join(
                " ",
                latestUserMessage.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (compact.Length > 500)
                compact = compact[..500] + "…";

            return
                $"I’m continuing the captured shaping conversation for {_context.ProjectName}. " +
                $"Your latest input was: “{compact}” A useful next step is to confirm the primary users, " +
                "the outcome that matters most to them, and any material constraints before decomposing delivery work.";
        }
    }
}
