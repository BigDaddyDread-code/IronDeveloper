using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Tools;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectToolCatalogueService : IProjectToolCatalogueService
{
    private const string CatalogueBoundary =
        "The project tool catalogue is read-only registration and capability disclosure. " +
        "Registration is not project enablement, invocation authority, approval, or permission to mutate state.";

    private readonly IProjectService _projects;
    private readonly IGovernedToolRegistry _tools;

    public ProjectToolCatalogueService(IProjectService projects, IGovernedToolRegistry tools)
    {
        _projects = projects;
        _tools = tools;
    }

    public async Task<ProjectToolCatalogueResponse?> GetCatalogueAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var tools = _tools.ListTools().Select(ToSummary).ToArray();
        return new ProjectToolCatalogueResponse(project.Id, project.Name, tools, CatalogueBoundary);
    }

    public async Task<ProjectToolDetailResponse?> GetToolAsync(
        int projectId,
        string toolId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var definition = _tools.ListTools().SingleOrDefault(tool =>
            string.Equals(tool.Name, toolId, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            return null;

        var states = StateFor(definition);
        return new ProjectToolDetailResponse(
            project.Id,
            project.Name,
            definition.Name,
            definition.DisplayName,
            definition.Category,
            definition.Description,
            definition.DefinitionVersion,
            states.Registration,
            states.Connection,
            states.ProjectUse,
            states.DirectInvocation,
            states.Health,
            EffectiveScopeSummary(definition),
            new ProjectToolCapabilities(
                definition.MutatesState,
                definition.AllowsNestedCalls,
                definition.AllowsFileWrites,
                definition.AllowsProcessExecution,
                definition.AllowsNetworkAccess,
                definition.AllowsWorkspaceMutation),
            definition.InputType.Name,
            definition.OutputType.Name,
            definition.AllowedCallers,
            definition.EvidenceKinds,
            definition.Boundary);
    }

    private static ProjectToolSummary ToSummary(GovernedToolDefinition definition)
    {
        var states = StateFor(definition);
        return new ProjectToolSummary(
            definition.Name,
            definition.DisplayName,
            definition.Category,
            definition.Description,
            states.Registration,
            states.Connection,
            states.ProjectUse,
            states.DirectInvocation,
            states.Health,
            EffectiveScopeSummary(definition),
            definition.Boundary);
    }

    private static ToolStates StateFor(GovernedToolDefinition definition) =>
        new(
            Registration: "Registered",
            Connection: definition.ConnectionRequirement == GovernedToolConnectionRequirement.None
                ? "Not required"
                : "Setup required",
            ProjectUse: "Governed workflows only",
            DirectInvocation: "Not implemented",
            Health: "Not checked");

    private static string EffectiveScopeSummary(GovernedToolDefinition definition)
    {
        var capabilities = new List<string>();
        if (definition.MutatesState) capabilities.Add("state mutation");
        if (definition.AllowsFileWrites) capabilities.Add("file writes");
        if (definition.AllowsProcessExecution) capabilities.Add("process execution");
        if (definition.AllowsNetworkAccess) capabilities.Add("network access");
        if (definition.AllowsWorkspaceMutation) capabilities.Add("workspace mutation");

        return capabilities.Count == 0
            ? "Read-only. No state, file, process, network, or workspace mutation."
            : $"Declared scope includes {string.Join(", ", capabilities)}.";
    }

    private sealed record ToolStates(
        string Registration,
        string Connection,
        string ProjectUse,
        string DirectInvocation,
        string Health);
}
