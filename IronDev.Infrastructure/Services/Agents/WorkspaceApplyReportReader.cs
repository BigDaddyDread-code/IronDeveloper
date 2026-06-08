using System.Text.Json;
using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class WorkspaceApplyReportReader : IWorkspaceApplyReportReader
{
    public async Task<WorkspaceApplyReportSummary> ReadAsync(
        WorkspaceApplyReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = Path.GetFullPath(request.WorkspacePath.Trim());
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var sourceReportPath = Path.Combine(runDirectory, "source-report.json");
        var failurePackagePath = Path.Combine(runDirectory, "failure-package.json");
        var warnings = new List<string>();

        if (File.Exists(sourceReportPath))
        {
            try
            {
                using var document = await ReadJsonAsync(sourceReportPath, cancellationToken).ConfigureAwait(false);
                var sourceSummary = MapSourceReport(request, workspacePath, sourceReportPath, failurePackagePath, document.RootElement, warnings);
                if (sourceSummary.Outcome == "success")
                    return sourceSummary;

                warnings.Add("source-report.json was present but did not describe a coherent successful apply.");
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                warnings.Add($"source-report.json could not be read: {exception.Message}");
            }
        }

        if (File.Exists(failurePackagePath))
        {
            try
            {
                using var document = await ReadJsonAsync(failurePackagePath, cancellationToken).ConfigureAwait(false);
                return MapFailurePackage(request, workspacePath, failurePackagePath, document.RootElement, warnings);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                warnings.Add($"failure-package.json could not be read: {exception.Message}");
            }
        }

        if (!File.Exists(sourceReportPath) && !File.Exists(failurePackagePath))
            warnings.Add("No source-report.json or failure-package.json was found for the workspace apply run.");

        return new WorkspaceApplyReportSummary
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            Outcome = "unavailable",
            SourceReportPath = File.Exists(sourceReportPath) ? sourceReportPath : null,
            FailurePackagePath = File.Exists(failurePackagePath) ? failurePackagePath : null,
            EvidencePaths = ExistingPaths(sourceReportPath, failurePackagePath),
            Warnings = warnings
        };
    }

    private static async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static WorkspaceApplyReportSummary MapSourceReport(
        WorkspaceApplyReportRequest request,
        string workspacePath,
        string sourceReportPath,
        string failurePackagePath,
        JsonElement root,
        List<string> readerWarnings)
    {
        var data = GetDataOrRoot(root);
        var sourceRepoMutated = GetBool(data, "sourceRepoMutated") ?? false;
        var applyVerified = GetBool(data, "applyVerified") ?? false;
        var sourceMatchesWorkspace = GetBool(data, "sourceMatchesWorkspace") ?? false;
        var postApplyValidationSucceeded = GetBool(data, "postApplyValidationSucceeded") ?? false;
        var sourceStatus = GetString(root, "status");
        var coherentSuccess =
            string.Equals(sourceStatus, "succeeded", StringComparison.OrdinalIgnoreCase) ||
            (sourceRepoMutated && applyVerified && sourceMatchesWorkspace && postApplyValidationSucceeded);
        var warnings = readerWarnings
            .Concat(ReadStringArray(root, "warnings"))
            .Concat(ReadStringArray(data, "warnings"))
            .ToList();
        var failurePackageExists = File.Exists(failurePackagePath);
        if (failurePackageExists)
            warnings.Add("failure-package.json also exists; source-report.json won because it describes a coherent successful apply.");

        var deleteCount = GetInt(data, "deleteCount") ?? 0;
        if (deleteCount > 0)
            warnings.Add("Source report contains delete operations, which are outside current copy-only support.");

        return new WorkspaceApplyReportSummary
        {
            RunId = GetString(data, "runId") ?? request.RunId,
            WorkspacePath = NormalizeOrDefault(GetString(data, "workspacePath"), workspacePath),
            SourceRepo = NormalizeOptionalPath(GetString(data, "sourceRepo")),
            Outcome = coherentSuccess ? "success" : "unavailable",
            Recommendation = GetString(data, "recommendation"),
            SourceRepoMutated = sourceRepoMutated,
            ApplyVerified = applyVerified,
            SourceMatchesWorkspace = sourceMatchesWorkspace,
            PostApplyValidationSucceeded = postApplyValidationSucceeded,
            AddCount = GetInt(data, "addCount") ?? 0,
            ModifyCount = GetInt(data, "modifyCount") ?? 0,
            DeleteCount = deleteCount,
            Files = ReadFiles(data),
            SourceReportPath = sourceReportPath,
            FailurePackagePath = failurePackageExists ? failurePackagePath : null,
            EvidencePaths = ReadStringArray(data, "evidencePaths").Concat(ExistingPaths(sourceReportPath, failurePackagePath)).Distinct().ToArray(),
            RiskNotes = ReadStringArray(data, "riskNotes"),
            Errors = ReadStringArray(root, "errors").Concat(ReadStringArray(data, "errors")).Distinct().ToArray(),
            Warnings = warnings.Distinct().ToArray()
        };
    }

    private static WorkspaceApplyReportSummary MapFailurePackage(
        WorkspaceApplyReportRequest request,
        string workspacePath,
        string failurePackagePath,
        JsonElement root,
        List<string> readerWarnings)
    {
        var data = GetDataOrRoot(root);
        var warnings = readerWarnings
            .Concat(ReadStringArray(root, "warnings"))
            .Concat(ReadStringArray(data, "warnings"))
            .Concat(ReadStringArray(data, "aggregatedWarnings"))
            .Distinct()
            .ToArray();

        return new WorkspaceApplyReportSummary
        {
            RunId = GetString(data, "runId") ?? request.RunId,
            WorkspacePath = NormalizeOrDefault(GetString(data, "workspacePath"), workspacePath),
            SourceRepo = NormalizeOptionalPath(GetString(data, "sourceRepo")),
            Outcome = "failure",
            FailedStage = GetString(data, "failedStage"),
            FailureSeverity = GetString(data, "failureSeverity"),
            RecommendedNextAction = GetString(data, "recommendedNextAction"),
            SourceRepoMutated = GetBool(data, "sourceRepoMutated") ?? false,
            ApplyVerified = GetBool(data, "applyVerified") ?? false,
            PostApplyValidationSucceeded = GetBool(data, "postApplyValidationSucceeded") ?? false,
            FailurePackagePath = failurePackagePath,
            EvidencePaths = ReadStringArray(data, "evidencePaths").Concat(ExistingPaths(failurePackagePath)).Distinct().ToArray(),
            RiskNotes = ReadStringArray(data, "riskNotes"),
            Errors = ReadStringArray(root, "errors").Concat(ReadStringArray(data, "errors")).Concat(ReadStringArray(data, "aggregatedErrors")).Distinct().ToArray(),
            Warnings = warnings
        };
    }

    private static IReadOnlyList<WorkspaceApplyChangedFileSummary> ReadFiles(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return files.EnumerateArray()
            .Where(file => file.ValueKind == JsonValueKind.Object)
            .Select(file => new WorkspaceApplyChangedFileSummary
            {
                Operation = GetString(file, "operation") ?? string.Empty,
                RelativePath = GetString(file, "relativePath") ?? string.Empty,
                Applied = GetBool(file, "applied") ?? false,
                Verified = GetBool(file, "verified") ?? false
            })
            .Where(file => !string.IsNullOrWhiteSpace(file.Operation) && !string.IsNullOrWhiteSpace(file.RelativePath))
            .ToArray();
    }

    private static JsonElement GetDataOrRoot(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("data", out var data) &&
        data.ValueKind == JsonValueKind.Object
            ? data
            : root;

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static string NormalizeOrDefault(string? path, string fallback) =>
        string.IsNullOrWhiteSpace(path) ? fallback : Path.GetFullPath(path);

    private static string? NormalizeOptionalPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static IReadOnlyList<string> ExistingPaths(params string[] paths) =>
        paths.Where(File.Exists).ToArray();
}
