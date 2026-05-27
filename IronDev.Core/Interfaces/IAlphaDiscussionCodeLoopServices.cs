using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IDiscussionDocumentService
{
    Task<SaveDiscussionResponse> SaveDiscussionAsync(
        int projectId,
        SaveDiscussionRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITicketFromDocumentService
{
    Task<CreateTicketFromDocumentResponse?> CreateTicketAsync(
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITicketDebateService
{
    Task<AgentDebateResult?> RunDebateAsync(
        long ticketId,
        RunTicketDebateRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentDebateResult?> GetDebateAsync(
        long ticketId,
        string debateId,
        CancellationToken cancellationToken = default);
}

public interface IAlphaConsoleProjectGenerator
{
    Task<GeneratedAlphaProject> GenerateAsync(
        string workspacePath,
        string expectedOutput,
        CancellationToken cancellationToken = default);
}

public interface IAlphaHelloWorldCodeRunService
{
    Task<StartAlphaDisposableCodeRunResponse?> StartAsync(
        long ticketId,
        StartAlphaDisposableCodeRunRequest request,
        CancellationToken cancellationToken = default);
}
