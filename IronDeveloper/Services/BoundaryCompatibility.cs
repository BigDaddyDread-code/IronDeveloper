using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Client.Auth;
using IronDev.Client.Chat;
using IronDev.Client.Memory;
using IronDev.Client.Projects;
using IronDev.Client.Prompting;
using IronDev.Client.Settings;
using IronDev.Client.Tickets;
using IronDev.Client.Traces;
using IronDev.Core.Auth;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Agent.Services;

internal static class BoundaryCompatibility
{
    public static IProjectsApiClient Projects(object value) =>
        value as IProjectsApiClient ?? new ProjectAdapter(value);

    public static IMemoryApiClient Memory(object? value) =>
        value as IMemoryApiClient ?? new MemoryAdapter(value);

    public static ITicketsApiClient Tickets(object? ticket, object? build = null, object? draft = null, object? generator = null) =>
        ticket as ITicketsApiClient ?? build as ITicketsApiClient ?? draft as ITicketsApiClient ?? generator as ITicketsApiClient
        ?? new TicketAdapter(ticket, build, draft, generator);

    public static IChatApiClient Chat(object value) =>
        value as IChatApiClient ?? new ChatAdapter(value);

    public static ITraceApiClient Trace(object value) =>
        value as ITraceApiClient ?? new TraceAdapter(value);

    public static IPromptContextBuilder Prompt(object value) =>
        value as IPromptContextBuilder ?? new PromptAdapter(value);

    public static IAuthApiClient Auth(object value) =>
        value as IAuthApiClient ?? new AuthAdapter(value);

    public static ISettingsApiClient Settings(object? value) =>
        value as ISettingsApiClient ?? new SettingsAdapter();

    private static async Task<T?> InvokeAsync<T>(object? target, string methodName, params object?[] args)
    {
        if (target == null) return default;

        var method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .FirstOrDefault(m => CanCall(m, args));

        if (method == null) return default;

        var callArgs = BuildArgs(method, args);
        var result = method.Invoke(target, callArgs);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result");
            result = resultProperty?.GetValue(task);
        }

        return ConvertResult<T>(result);
    }

    private static T? ConvertResult<T>(object? result)
    {
        if (result is null) return default;
        if (result is T typed) return typed;

        var targetType = typeof(T);
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var itemType = targetType.GetGenericArguments()[0];
            if (result is System.Collections.IEnumerable enumerable)
            {
                var listType = typeof(List<>).MakeGenericType(itemType);
                var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
                foreach (var item in enumerable)
                {
                    if (item == null || itemType.IsInstanceOfType(item))
                        list.Add(item);
                }
                return (T)list;
            }
        }

        return default;
    }

    private static bool CanCall(MethodInfo method, object?[] supplied)
    {
        var parameters = method.GetParameters();
        var required = parameters.Count(p => !p.HasDefaultValue && !IsCancellationToken(p.ParameterType));
        return supplied.Length >= required && supplied.Length <= parameters.Length;
    }

    private static object?[] BuildArgs(MethodInfo method, object?[] supplied)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i < supplied.Length)
            {
                args[i] = supplied[i];
                continue;
            }

            args[i] = IsCancellationToken(parameters[i].ParameterType)
                ? CancellationToken.None
                : parameters[i].HasDefaultValue
                    ? parameters[i].DefaultValue
                    : parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType)
                        : null;
        }
        return args;
    }

    private static bool IsCancellationToken(Type type) => type == typeof(CancellationToken);

    private sealed class ProjectAdapter(object inner) : IProjectsApiClient
    {
        public async Task<int> CreateProjectAsync(Project project, CancellationToken cancellationToken = default)
            => await InvokeAsync<int>(inner, nameof(CreateProjectAsync), project, cancellationToken).ConfigureAwait(false);

        public async Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
            => await InvokeAsync<IReadOnlyList<Project>>(inner, "GetRecentProjectsAsync", cancellationToken).ConfigureAwait(false) ?? [];

        public Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default)
            => InvokeAsync<Project>(inner, nameof(GetByIdAsync), projectId, cancellationToken);

        public Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default)
            => InvokeAsync<object>(inner, nameof(UpdateLocalPathAsync), projectId, localPath, cancellationToken);

        public Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken cancellationToken = default)
            => InvokeAsync<object>(inner, nameof(MarkIndexStaleAsync), projectId, reason, cancellationToken);

        public Task SelectProjectAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<string> ExportProjectContextPackAsync(int projectId, CancellationToken cancellationToken = default)
            => await InvokeAsync<string>(inner, nameof(ExportProjectContextPackAsync), projectId, cancellationToken).ConfigureAwait(false) ?? string.Empty;
    }

    private sealed class MemoryAdapter(object? inner) : IMemoryApiClient
    {
        public Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default) =>
            InvokeAsync<ProjectSummary>(inner, nameof(GetLatestSummaryAsync), projectId, cancellationToken);

        public async Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ProjectDecision>>(inner, nameof(GetRecentDecisionsAsync), projectId, take, cancellationToken).ConfigureAwait(false) ?? [];

        public async Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SaveSummaryAsync), summary, cancellationToken).ConfigureAwait(false);

        public async Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(int projectId, string? documentType = null, string? status = null, string? searchText = null, int take = 50, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ProjectContextDocument>>(inner, nameof(GetContextDocumentsAsync), projectId, documentType, status, searchText, take, cancellationToken).ConfigureAwait(false) ?? [];

        public async Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ProjectContextDocument>>(inner, nameof(GetRelevantContextDocumentsAsync), projectId, query, take, cancellationToken).ConfigureAwait(false) ?? [];

        public Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken cancellationToken = default) =>
            InvokeAsync<ProjectContextDocument>(inner, nameof(GetContextDocumentByIdAsync), documentId, cancellationToken);

        public async Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SaveContextDocumentAsync), document, cancellationToken).ConfigureAwait(false);

        public async Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken cancellationToken = default) =>
            await InvokeAsync<bool>(inner, nameof(ArchiveContextDocumentAsync), documentId, cancellationToken).ConfigureAwait(false);

        public async Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ProjectImplementationPlan>>(inner, nameof(GetRecentPlansAsync), projectId, take, cancellationToken).ConfigureAwait(false) ?? [];

        public Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default) =>
            InvokeAsync<ProjectImplementationPlan>(inner, nameof(GetPlanByIdAsync), planId, cancellationToken);

        public Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken cancellationToken = default) =>
            InvokeAsync<ProjectImplementationPlan>(inner, nameof(GetPlanByTicketIdAsync), ticketId, cancellationToken);

        public async Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SavePlanAsync), plan, cancellationToken).ConfigureAwait(false);

        public async Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SaveDecisionAsync), decision, cancellationToken).ConfigureAwait(false);

        public async Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ProjectRule>>(inner, nameof(GetProjectRulesAsync), projectId, cancellationToken).ConfigureAwait(false) ?? [];

        public async Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SaveProjectRuleAsync), rule, cancellationToken).ConfigureAwait(false);
    }

    private sealed class TicketAdapter(object? ticket, object? build, object? draft, object? generator) : ITicketsApiClient
    {
        public async Task<long> SaveTicketAsync(ProjectTicket item, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(ticket, nameof(SaveTicketAsync), item, cancellationToken).ConfigureAwait(false);

        public async Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ProjectTicket>>(ticket, nameof(GetRecentTicketsAsync), projectId, take, cancellationToken).ConfigureAwait(false) ?? [];

        public Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken cancellationToken = default) =>
            InvokeAsync<ProjectTicket>(ticket, nameof(GetTicketByIdAsync), ticketId, cancellationToken);

        public async Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken cancellationToken = default) =>
            await InvokeAsync<bool>(ticket, nameof(ArchiveTicketAsync), ticketId, cancellationToken).ConfigureAwait(false);

        public async Task<CodebaseTicketGenerationResult> GenerateTicketsAsync(int projectId, CancellationToken cancellationToken = default) =>
            await InvokeAsync<CodebaseTicketGenerationResult>(generator, nameof(GenerateTicketsAsync), projectId, cancellationToken).ConfigureAwait(false)
            ?? new CodebaseTicketGenerationResult { Success = true };

        public async Task<TicketBuildPreview> CreateBuildPreviewAsync(int projectId, long ticketId, CancellationToken cancellationToken = default) =>
            await InvokeAsync<TicketBuildPreview>(build, nameof(CreateBuildPreviewAsync), projectId, ticketId, cancellationToken).ConfigureAwait(false)
            ?? new TicketBuildPreview { TicketId = ticketId };

        public async Task<TicketBuildResult> ApplyAndBuildAsync(TicketBuildApproval approval, CancellationToken cancellationToken = default) =>
            await InvokeAsync<TicketBuildResult>(build, nameof(ApplyAndBuildAsync), approval, cancellationToken).ConfigureAwait(false)
            ?? new TicketBuildResult { TicketId = approval.TicketId };

        public async Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default) =>
            await InvokeAsync<BuilderProposal>(build ?? ticket, nameof(GenerateProposalAsync), ticketId, ct).ConfigureAwait(false)
            ?? new BuilderProposal { TicketId = ticketId };

        public async Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
            await InvokeAsync<BuilderProposal>(build ?? ticket, nameof(GenerateProposalFromRequestAsync), projectId, request, ct).ConfigureAwait(false)
            ?? new BuilderProposal { ProjectId = projectId, OriginalRequest = request };

        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            InvokeAsync<object>(build ?? ticket, nameof(ApplyProposalAsync), proposal, ct);

        public async Task<DraftTicket> GenerateDraftAsync(int projectId, string projectName, string proposedTitle, string messageText, string? linkedFilePaths, string? linkedSymbols, long? sessionId = null, CancellationToken ct = default) =>
            await InvokeAsync<DraftTicket>(draft, nameof(GenerateDraftAsync), projectId, projectName, proposedTitle, messageText, linkedFilePaths, linkedSymbols, sessionId, ct).ConfigureAwait(false)
            ?? new DraftTicket
            {
                Title = proposedTitle,
                Summary = messageText,
                SourceChatSessionId = sessionId ?? 0,
                SourceMessageText = messageText,
                LinkedFilePaths = linkedFilePaths,
                LinkedSymbols = linkedSymbols
            };

        public async Task<DraftTicket> RegenerateTestsAsync(int projectId, DraftTicket current, CancellationToken ct = default) =>
            await InvokeAsync<DraftTicket>(draft, nameof(RegenerateTestsAsync), projectId, current, ct).ConfigureAwait(false) ?? current;

        public async Task<DraftTicket> GeneratePlanAsync(int projectId, DraftTicket current, CancellationToken ct = default) =>
            await InvokeAsync<DraftTicket>(draft, nameof(GeneratePlanAsync), projectId, current, ct).ConfigureAwait(false) ?? current;

        public async Task<BuildReadinessResult> EvaluateReadinessAsync(int projectId, long ticketId, CancellationToken cancellationToken = default) =>
            await InvokeAsync<BuildReadinessResult>(build ?? ticket, nameof(EvaluateReadinessAsync), projectId, ticketId, cancellationToken).ConfigureAwait(false)
            ?? new BuildReadinessResult();

        public async Task<BuildReadinessResult> ValidateProposalArchitectureAsync(BuilderProposal proposal, CancellationToken cancellationToken = default) =>
            await InvokeAsync<BuildReadinessResult>(build ?? ticket, nameof(ValidateProposalArchitectureAsync), proposal, cancellationToken).ConfigureAwait(false)
            ?? new BuildReadinessResult();
    }

    private sealed class ChatAdapter(object inner) : IChatApiClient
    {
        public async Task<IReadOnlyList<ProjectChatSession>> GetRecentSessionsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ProjectChatSession>>(inner, nameof(GetRecentSessionsAsync), projectId, take, cancellationToken).ConfigureAwait(false) ?? [];

        public Task<ProjectChatSession?> GetSessionByIdAsync(long sessionId, CancellationToken cancellationToken = default) =>
            InvokeAsync<ProjectChatSession>(inner, nameof(GetSessionByIdAsync), sessionId, cancellationToken);

        public async Task<long> SaveSessionAsync(ProjectChatSession session, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SaveSessionAsync), session, cancellationToken).ConfigureAwait(false);

        public Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default) =>
            InvokeAsync<object>(inner, nameof(DeleteSessionAsync), sessionId, cancellationToken);

        public async Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SaveMessageAsync), message, cancellationToken).ConfigureAwait(false);

        public async Task<long> SaveFeedbackAsync(ChatMessageFeedback feedback, CancellationToken cancellationToken = default) =>
            await InvokeAsync<long>(inner, nameof(SaveFeedbackAsync), feedback, cancellationToken).ConfigureAwait(false);

        public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(int projectId, long sessionId, int take = 50, CancellationToken cancellationToken = default) =>
            await InvokeAsync<IReadOnlyList<ChatMessage>>(inner, nameof(GetRecentMessagesAsync), projectId, sessionId, take, cancellationToken).ConfigureAwait(false) ?? [];

        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatCompletionResponse(string.Empty, null, null, null, null));
    }

    private sealed class TraceAdapter(object inner) : ITraceApiClient
    {
        public event EventHandler<LlmTraceEntry>? TraceAdded;

        public bool IsTracingEnabled
        {
            get => InvokeAsync<bool>(inner, "get_IsTracingEnabled").GetAwaiter().GetResult();
            set
            {
                var prop = inner.GetType().GetProperty(nameof(IsTracingEnabled));
                prop?.SetValue(inner, value);
            }
        }

        public void AddTrace(LlmTraceEntry trace)
        {
            _ = InvokeAsync<object>(inner, nameof(AddTrace), trace);
            TraceAdded?.Invoke(this, trace);
        }

        public IReadOnlyList<LlmTraceEntry> GetRecentTraces(int max = 100) =>
            InvokeAsync<IReadOnlyList<LlmTraceEntry>>(inner, nameof(GetRecentTraces), max).GetAwaiter().GetResult() ?? [];

        public void Clear() => _ = InvokeAsync<object>(inner, nameof(Clear));
        public string ExportTrace(LlmTraceEntry trace) => InvokeAsync<string>(inner, nameof(ExportTrace), trace).GetAwaiter().GetResult() ?? string.Empty;
        public string ExportAll() => InvokeAsync<string>(inner, nameof(ExportAll)).GetAwaiter().GetResult() ?? string.Empty;
    }

    private sealed class PromptAdapter(object inner) : IPromptContextBuilder
    {
        public async Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default) =>
            await InvokeAsync<string>(inner, nameof(BuildAsync), projectId, sessionId, userRequest, cancellationToken).ConfigureAwait(false) ?? userRequest;

        public async Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default)
        {
            var raw = await InvokeAsync<object>(inner, nameof(BuildPacketAsync), projectId, sessionId, userRequest, cancellationToken).ConfigureAwait(false);
            return MapPacket(raw, userRequest);
        }

        public async Task<PromptPreviewResult> BuildFullPromptForTestingAsync(int projectId, string userMessage, CancellationToken ct = default)
        {
            var raw = await InvokeAsync<object>(inner, nameof(BuildFullPromptForTestingAsync), projectId, userMessage, ct).ConfigureAwait(false);
            if (raw == null)
                return new PromptPreviewResult { PromptText = userMessage, DetectedIntent = ClientPromptContextBuilder.ClassifyIntent(userMessage).ToString() };

            return new PromptPreviewResult
            {
                PromptText = Read<string>(raw, nameof(PromptPreviewResult.PromptText)) ?? userMessage,
                DetectedIntent = Read<string>(raw, nameof(PromptPreviewResult.DetectedIntent)) ?? string.Empty,
                ProjectIndexStatus = Read<string>(raw, nameof(PromptPreviewResult.ProjectIndexStatus)) ?? string.Empty,
                ContextQuality = Read<string>(raw, nameof(PromptPreviewResult.ContextQuality)) ?? string.Empty,
                ContextPolluted = Read<bool>(raw, nameof(PromptPreviewResult.ContextPolluted)),
                FilteredMemoryCount = Read<int>(raw, nameof(PromptPreviewResult.FilteredMemoryCount)),
                IncludedMemoryCount = Read<int>(raw, nameof(PromptPreviewResult.IncludedMemoryCount)),
                IncludedStandardsCount = Read<int>(raw, nameof(PromptPreviewResult.IncludedStandardsCount)),
                FilteredStandardsCount = Read<int>(raw, nameof(PromptPreviewResult.FilteredStandardsCount))
            };
        }

        private static ChatContextPacket MapPacket(object? raw, string fallbackPrompt)
        {
            if (raw == null)
                return new ChatContextPacket { FormattedPrompt = fallbackPrompt, Intent = ClientPromptContextBuilder.ClassifyIntent(fallbackPrompt) };

            Enum.TryParse(Read<object>(raw, nameof(ChatContextPacket.Intent))?.ToString(), out ChatIntent intent);
            return new ChatContextPacket
            {
                FormattedPrompt = Read<string>(raw, nameof(ChatContextPacket.FormattedPrompt)) ?? fallbackPrompt,
                Intent = intent,
                IsProjectNotIndexed = Read<bool>(raw, nameof(ChatContextPacket.IsProjectNotIndexed)),
                FilteredMemoryCount = Read<int>(raw, nameof(ChatContextPacket.FilteredMemoryCount)),
                IncludedMemoryCount = Read<int>(raw, nameof(ChatContextPacket.IncludedMemoryCount)),
                IncludedStandardsCount = Read<int>(raw, nameof(ChatContextPacket.IncludedStandardsCount)),
                FilteredStandardsCount = Read<int>(raw, nameof(ChatContextPacket.FilteredStandardsCount)),
                RulesLoadWarning = Read<string>(raw, nameof(ChatContextPacket.RulesLoadWarning)),
                HostApplicationName = Read<string>(raw, nameof(ChatContextPacket.HostApplicationName)) ?? "IronDev",
                ActiveProjectName = Read<string>(raw, nameof(ChatContextPacket.ActiveProjectName)) ?? string.Empty,
                ActiveProjectPath = Read<string>(raw, nameof(ChatContextPacket.ActiveProjectPath)) ?? string.Empty,
                ActiveProjectType = Read<string>(raw, nameof(ChatContextPacket.ActiveProjectType)) ?? string.Empty,
                IsExternalProject = Read<bool>(raw, nameof(ChatContextPacket.IsExternalProject))
            };
        }
    }

    private sealed class SettingsAdapter : ISettingsApiClient;

    private sealed class AuthAdapter(object inner) : IAuthApiClient
    {
        private int _userId;
        private string _email = string.Empty;
        private string _displayName = string.Empty;
        private int? _tenantId;

        public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            var user = await InvokeAsync<object>(inner, "ValidateCredentialsAsync", request.Email, request.Password, ct).ConfigureAwait(false);
            if (user == null)
                throw new InvalidOperationException("Invalid credentials.");

            _userId = Read<int>(user, "Id");
            _email = Read<string>(user, "Email") ?? request.Email;
            _displayName = Read<string>(user, "DisplayName") ?? _email;
            return new LoginResponse("test-token", _userId, _displayName);
        }

        public async Task<IReadOnlyList<TenantDto>> GetTenantsAsync(CancellationToken ct = default)
        {
            if (_userId > 0)
                return await InvokeAsync<IReadOnlyList<TenantDto>>(inner, "GetUserTenantsAsync", _userId, ct).ConfigureAwait(false) ?? [];

            return await InvokeAsync<IReadOnlyList<TenantDto>>(inner, "GetAllActiveTenantsAsync", ct).ConfigureAwait(false) ?? [];
        }

        public Task<LoginResponse> SelectTenantAsync(SelectTenantRequest request, CancellationToken ct = default)
        {
            _tenantId = request.TenantId;
            return Task.FromResult(new LoginResponse("test-tenant-token", _userId, _displayName));
        }

        public Task<UserProfileDto> GetCurrentUserAsync(CancellationToken ct = default) =>
            Task.FromResult(new UserProfileDto(_userId, _email, _displayName, _tenantId));

        public Task LogoutAsync(CancellationToken ct = default)
        {
            _userId = 0;
            _email = string.Empty;
            _displayName = string.Empty;
            _tenantId = null;
            return Task.CompletedTask;
        }
    }

    private static T? Read<T>(object value, string name)
    {
        var property = value.GetType().GetProperty(name);
        var raw = property?.GetValue(value);
        return raw is T typed ? typed : default;
    }
}
