namespace IronDev.Core.Models;

public sealed record ProjectToolCatalogueResponse(
    int ProjectId,
    string ProjectName,
    IReadOnlyList<ProjectToolSummary> Tools,
    string Boundary);

public sealed record ProjectToolSummary(
    string ToolId,
    string DisplayName,
    string Category,
    string Description,
    string RegistrationStatus,
    string ConnectionStatus,
    string ProjectUseStatus,
    string DirectInvocationStatus,
    string HealthStatus,
    string EffectiveScopeSummary,
    string Boundary);

public sealed record ProjectToolDetailResponse(
    int ProjectId,
    string ProjectName,
    string ToolId,
    string DisplayName,
    string Category,
    string Description,
    string DefinitionVersion,
    string RegistrationStatus,
    string ConnectionStatus,
    string ProjectUseStatus,
    string DirectInvocationStatus,
    string HealthStatus,
    string EffectiveScopeSummary,
    ProjectToolCapabilities Capabilities,
    string InputContract,
    string OutputContract,
    IReadOnlyList<string> AllowedCallers,
    IReadOnlyList<string> EvidenceKinds,
    string Boundary);

public sealed record ProjectToolCapabilities(
    bool MutatesState,
    bool AllowsNestedCalls,
    bool AllowsFileWrites,
    bool AllowsProcessExecution,
    bool AllowsNetworkAccess,
    bool AllowsWorkspaceMutation);
