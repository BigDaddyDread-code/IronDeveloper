using System.Text.Json;

namespace IronDev.Core.Governance;

public static class DisposableWorkspacePatchPackageValidator
{
    public const string MarkerRelativePath = ".irondev/disposable-workspace.json";
    public const string PatchRelativePath = "patch.diff";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static DisposableWorkspacePatchPackageValidationResult Validate(DisposableWorkspacePatchPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<string>();
        var workspaceRoot = FullPathOrEmpty(request.WorkspacePath, "WorkspacePathRequired", issues);
        var outputRoot = FullPathOrEmpty(request.OutputPath, "OutputPathRequired", issues);
        var markerPath = string.IsNullOrWhiteSpace(workspaceRoot)
            ? string.Empty
            : Path.Combine(workspaceRoot, MarkerRelativePath);
        var patchPath = string.IsNullOrWhiteSpace(workspaceRoot)
            ? string.Empty
            : Path.Combine(workspaceRoot, PatchRelativePath);

        RequireText(request.OperationId, "PatchPackageOperationIdRequired", issues);
        RequireText(request.RepoId, "PatchPackageRepoIdRequired", issues);
        RequireText(request.Branch, "PatchPackageBranchRequired", issues);
        RequireText(request.ProposalId, "PatchPackageProposalIdRequired", issues);
        RequireText(request.TaskSummary, "PatchPackageTaskSummaryRequired", issues);
        if (ValuesOrEmpty(request.AllowedPathGlobs).Any(value => !string.IsNullOrWhiteSpace(value)))
            issues.Add("AllowedPathGlobsUnsupportedInPatchPackageSlice");
        if (ValuesOrEmpty(request.ForbiddenPathGlobs).Any(value => !string.IsNullOrWhiteSpace(value)))
            issues.Add("ForbiddenPathGlobsUnsupportedInPatchPackageSlice");

        var marker = ReadMarker(markerPath, issues);
        var sourceRoot = marker is null
            ? string.Empty
            : FullPathOrEmpty(marker.SourceRoot, "DisposableWorkspaceSourceRootRequired", issues);

        if (marker is not null)
        {
            if (!marker.Disposable)
                issues.Add("DisposableWorkspaceMarkerMustBeDisposable");
            if (!EqualsTrimmed(marker.RepoId, request.RepoId))
                issues.Add("DisposableWorkspaceRepoMismatch");
            if (!EqualsTrimmed(marker.Branch, request.Branch))
                issues.Add("DisposableWorkspaceBranchMismatch");
            if (!EqualsTrimmed(marker.CreatedFor, "proposal-only"))
                issues.Add("DisposableWorkspaceCreatedForProposalOnlyRequired");
            if (string.IsNullOrWhiteSpace(marker.WorkspaceId))
                issues.Add("DisposableWorkspaceIdRequired");
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot) &&
            !string.IsNullOrWhiteSpace(sourceRoot))
        {
            if (SamePath(workspaceRoot, sourceRoot))
                issues.Add("DisposableWorkspaceCannotEqualSourceRoot");
            else if (IsSameOrChild(workspaceRoot, sourceRoot))
                issues.Add("DisposableWorkspaceCannotBeInsideSourceRoot");
        }

        if (!string.IsNullOrWhiteSpace(outputRoot) &&
            !string.IsNullOrWhiteSpace(sourceRoot) &&
            IsSameOrChild(outputRoot, sourceRoot))
        {
            issues.Add("PatchPackageOutputCannotBeInsideSourceRoot");
        }

        if (string.IsNullOrWhiteSpace(patchPath) || !File.Exists(patchPath))
            issues.Add("PatchDiffRequired");

        return new DisposableWorkspacePatchPackageValidationResult
        {
            CanPackage = issues.Count == 0,
            WorkspaceRootPath = workspaceRoot,
            OutputRootPath = outputRoot,
            PatchPath = patchPath,
            SourceRootPath = sourceRoot,
            Marker = marker,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static DisposableWorkspaceMarker? ReadMarker(string markerPath, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(markerPath) || !File.Exists(markerPath))
        {
            issues.Add("DisposableWorkspaceMarkerRequired");
            return null;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<DisposableWorkspaceMarker>(File.ReadAllText(markerPath), JsonOptions);
            if (marker is null)
                issues.Add("DisposableWorkspaceMarkerInvalid");
            return marker;
        }
        catch (JsonException)
        {
            issues.Add("DisposableWorkspaceMarkerInvalid");
            return null;
        }
    }

    private static string FullPathOrEmpty(string path, string issue, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            issues.Add(issue);
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            issues.Add(issue);
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            issues.Add(issue);
            return string.Empty;
        }
    }

    private static void RequireText(string value, string issue, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static bool EqualsTrimmed(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChild(string path, string parent)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedParent = NormalizePath(parent);
        return normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];
}
