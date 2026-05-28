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
        int projectId,
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITicketReviewService
{
    Task<TicketReviewResult?> ReviewAsync(
        int projectId,
        long ticketId,
        RunTicketReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<TicketReviewResult?> GetReviewAsync(
        int projectId,
        long ticketId,
        string reviewId,
        CancellationToken cancellationToken = default);
}

public interface ICodeProposalGenerator
{
    Task<CodeProposal> GenerateAsync(
        TicketReviewResult review,
        string expectedOutput,
        CancellationToken cancellationToken = default);
}

public interface ICodeProposalValidator
{
    CodeProposalValidationResult Validate(CodeProposal proposal);
}

public interface ICodeRunProfileCatalog
{
    CodeRunProfileDefinition? GetProfile(string runtimeProfileId);
    IReadOnlyList<CodeRunProfileDefinition> GetProfiles();
}

public interface IDisposableCodeRunService
{
    Task<StartDisposableCodeRunResponse?> StartAsync(
        int projectId,
        long ticketId,
        StartDisposableCodeRunRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRunReviewPackageService
{
    Task<RunReviewPackage?> GetReviewPackageAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default);
}

public interface IBuildScenarioCatalog
{
    Task<IReadOnlyList<BuildScenario>> GetScenariosAsync(
        int projectId,
        CancellationToken cancellationToken = default);
}
