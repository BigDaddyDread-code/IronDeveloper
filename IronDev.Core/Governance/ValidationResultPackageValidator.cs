using System.Text.Json;

namespace IronDev.Core.Governance;

public static class ValidationResultPackageValidator
{
    public const string MarkerRelativePath = ".irondev/disposable-workspace.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ValidationResultPackageValidationResult Validate(ValidationResultPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<string>();
        var workspaceRoot = FullPathOrEmpty(request.WorkspacePath, "WorkspacePathRequired", issues);
        var outputRoot = FullPathOrEmpty(request.OutputPath, "OutputPathRequired", issues);
        var markerPath = string.IsNullOrWhiteSpace(workspaceRoot)
            ? string.Empty
            : Path.Combine(workspaceRoot, MarkerRelativePath);

        RequireText(request.OperationId, "ValidationPackageOperationIdRequired", issues);
        RequireText(request.RepoId, "ValidationPackageRepoIdRequired", issues);
        RequireText(request.Branch, "ValidationPackageBranchRequired", issues);
        RequireText(request.ProposalId, "ValidationPackageProposalIdRequired", issues);
        RequireText(request.PatchHash, "ValidationPackagePatchHashRequired", issues);
        RequireText(request.ValidationRunId, "ValidationRunIdRequired", issues);
        RequireText(request.ValidationName, "ValidationNameRequired", issues);
        if (!Enum.IsDefined(request.Outcome))
            issues.Add("ValidationOutcomeRequired");

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
            issues.Add("ValidationPackageOutputCannotBeInsideSourceRoot");
        }

        var evidenceFiles = ValidateEvidenceFiles(request.EvidenceFileNames, workspaceRoot, issues);

        return new ValidationResultPackageValidationResult
        {
            CanPackage = issues.Count == 0,
            WorkspaceRootPath = workspaceRoot,
            OutputRootPath = outputRoot,
            SourceRootPath = sourceRoot,
            Marker = marker,
            EvidenceFiles = evidenceFiles,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static IReadOnlyList<ValidationEvidenceFile> ValidateEvidenceFiles(
        IReadOnlyList<string>? evidenceFileNames,
        string workspaceRoot,
        ICollection<string> issues)
    {
        var fileNames = ValuesOrEmpty(evidenceFileNames)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (fileNames.Length == 0)
        {
            issues.Add("ValidationEvidenceFileRequired");
            return [];
        }

        var files = new List<ValidationEvidenceFile>();
        foreach (var fileName in fileNames)
        {
            if (Path.IsPathRooted(fileName) ||
                ContainsParentTraversal(fileName) ||
                string.IsNullOrWhiteSpace(workspaceRoot))
            {
                issues.Add("ValidationEvidenceFileOutsideWorkspace");
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, fileName));
            }
            catch (ArgumentException)
            {
                issues.Add("ValidationEvidenceFileOutsideWorkspace");
                continue;
            }
            catch (NotSupportedException)
            {
                issues.Add("ValidationEvidenceFileOutsideWorkspace");
                continue;
            }

            if (!IsSameOrChild(fullPath, workspaceRoot))
            {
                issues.Add("ValidationEvidenceFileOutsideWorkspace");
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                issues.Add("ValidationEvidenceFileMustBeFile");
                continue;
            }

            if (!File.Exists(fullPath))
            {
                issues.Add("ValidationEvidenceFileNotFound");
                continue;
            }

            files.Add(new ValidationEvidenceFile
            {
                FileName = NormalizeRelativePath(fileName),
                FullPath = fullPath
            });
        }

        return files;
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

    private static string NormalizeRelativePath(string path) =>
        path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool ContainsParentTraversal(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("..", StringComparison.Ordinal));

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];
}
