using System.Security.Cryptography;
using System.Text;
using IronDev.Core.RunReadiness;

namespace IronDev.Infrastructure.Services;

/// <summary>Pure, deterministic capability and containment policy.</summary>
public static class ProjectApplyCapabilityEvaluator
{
    public static ProjectApplyCapability Evaluate(ProjectApplyCapabilityInput input)
    {
        var result = EvaluatePreconditions(input);
        if (!result.IsReady) return result;

        var qualification = input.Qualification;
        if (!qualification.RecordPresent)
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMissing,
                "The server-owned disposable qualification record is missing for this tenant, project, and launcher session.");
        if (!qualification.RecordSignatureValid)
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationInvalid,
                "The server-owned disposable qualification record failed its integrity check.");
        if (!qualification.RecordBindingMatches)
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch,
                "The disposable qualification no longer matches this tenant, project path, sandbox root, database, or launcher session.");
        if (!qualification.MarkerPresent)
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMarkerMissing,
                "The project correlation marker for the server-owned disposable qualification is missing.");
        if (!qualification.MarkerMatches)
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMarkerMismatch,
                "The project correlation marker does not match the server-owned disposable qualification record.");

        return Finish(result with
        {
            QualificationId = qualification.QualificationId,
            QualificationFingerprint = qualification.QualificationFingerprint
        }, ProjectApplyCapabilityReasonCodes.Ready,
            "Controlled apply is available only for this server-qualified disposable project in this exact LocalTest session.");
    }

    public static ProjectApplyCapability EvaluatePreconditions(ProjectApplyCapabilityInput input)
    {
        var sandboxRoot = Normalize(input.SandboxRoot);
        var projectPath = Normalize(input.ProjectPath);
        var result = Base(input, sandboxRoot, projectPath);

        if (!input.ApplyEnabled ||
            !input.EnvironmentName.Equals("LocalTest", StringComparison.OrdinalIgnoreCase) ||
            !input.DatabaseName.Equals(input.ExpectedDatabaseName, StringComparison.Ordinal))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled,
                "Controlled sandbox apply requires the explicit LocalTest environment and IronDeveloper_Test database.");
        if (!input.LauncherCapabilityDeclared ||
            !input.SessionMode.Equals(ProjectRunPurposes.ProjectFeatureWork, StringComparison.Ordinal))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyLauncherCapabilityMissing,
                "The supported launcher did not declare controlled sandbox apply for project-feature work.");
        if (string.IsNullOrWhiteSpace(input.LauncherSessionId) ||
            !input.LauncherSessionId.Equals(input.ApiSessionId, StringComparison.Ordinal))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplySessionIdentityMismatch,
                "The launcher and API session identities do not match.");
        if (input.TenantId <= 0 || input.ProjectTenantId != input.TenantId)
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch,
                "The selected tenant does not own this project qualification boundary.");
        if (string.IsNullOrWhiteSpace(sandboxRoot) || !Directory.Exists(sandboxRoot))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplySandboxRootMissing,
                "The contracted sandbox root is missing.");
        if (IsUnsafeRoot(sandboxRoot))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplySandboxRootUnsafe,
                "The configured sandbox root is a protected root or resolves through a reparse point.");
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyPathOutsideSandbox,
                "The project path is missing, inaccessible, or outside the contracted sandbox root.");
        if (PathEquals(projectPath, sandboxRoot) || IsFileSystemRoot(projectPath))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyPathIsRoot,
                "The project path must be a strict child of the sandbox root.");
        if (!IsStrictChild(projectPath, sandboxRoot))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyPathOutsideSandbox,
                "The project path is outside the contracted sandbox root.");
        if (ContainsReparsePoint(sandboxRoot, projectPath))
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyPathReparsePoint,
                "The project path traverses a junction, symbolic link, or other reparse point.");
        if (!input.QualificationAuthorityConfigured)
            return Refuse(result, ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationAuthorityMissing,
                "The supported launcher did not provide a usable server-owned qualification authority for this API session.");

        return Finish(result with { IsReady = true, State = "Ready" },
            ProjectApplyCapabilityReasonCodes.Ready,
            "The project satisfies the non-authorizing LocalTest containment preconditions.");
    }

    public static ProjectApplyCapability Refuse(ProjectApplyCapability result, string code, string reason) =>
        Finish(result with { IsReady = false, State = "Disabled" }, code, reason);

    internal static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return string.Empty; }
    }

    internal static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()))).ToLowerInvariant();

    internal static bool ContainsAncestorReparsePoint(string path)
    {
        var current = path;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (HasReparsePoint(current)) return true;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || PathEquals(parent, current)) break;
            current = parent;
        }
        return false;
    }

    internal static bool HasReparsePoint(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path)
                ? File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                : false;
        }
        catch { return true; }
    }

    private static ProjectApplyCapability Base(ProjectApplyCapabilityInput input, string root, string projectPath) => new()
    {
        ProjectId = input.ProjectId,
        SessionMode = input.SessionMode,
        LauncherSessionId = input.LauncherSessionId,
        RepositoryCommit = input.RepositoryCommit,
        SandboxRoot = root,
        ProjectPath = projectPath,
        SandboxRootFingerprint = Fingerprint(root),
        ProjectPathFingerprint = Fingerprint(projectPath),
        QualificationId = input.Qualification.QualificationId,
        QualificationFingerprint = input.Qualification.QualificationFingerprint
    };

    private static ProjectApplyCapability Finish(ProjectApplyCapability result, string code, string reason)
    {
        var evidence = string.Join("\n", result.ProjectId, result.SessionMode, result.LauncherSessionId,
            result.RepositoryCommit, code, result.SandboxRootFingerprint, result.ProjectPathFingerprint,
            result.QualificationId, result.QualificationFingerprint);
        return result with { ReasonCode = code, Reason = reason, ReadinessEvidenceHash = Fingerprint(evidence) };
    }

    private static bool IsStrictChild(string child, string root) =>
        child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsafeRoot(string path)
    {
        if (IsFileSystemRoot(path) || ContainsAncestorReparsePoint(path)) return true;
        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Normalize);
        return protectedRoots.Any(root => PathEquals(path, root));
    }

    private static bool IsFileSystemRoot(string path) =>
        PathEquals(path, Path.GetPathRoot(path) ?? string.Empty);

    private static bool ContainsReparsePoint(string root, string child)
    {
        var current = child;
        while (IsStrictChild(current, root))
        {
            if (HasReparsePoint(current)) return true;
            current = Path.GetDirectoryName(current) ?? root;
        }
        return HasReparsePoint(root);
    }
}

public sealed record ProjectApplyCapabilityInput
{
    public int ProjectId { get; init; }
    public int TenantId { get; init; }
    public int ProjectTenantId { get; init; }
    public string EnvironmentName { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public string ExpectedDatabaseName { get; init; } = "IronDeveloper_Test";
    public bool ApplyEnabled { get; init; }
    public bool LauncherCapabilityDeclared { get; init; }
    public string LauncherSessionId { get; init; } = string.Empty;
    public string ApiSessionId { get; init; } = string.Empty;
    public string SessionMode { get; init; } = string.Empty;
    public string RepositoryCommit { get; init; } = string.Empty;
    public string SandboxRoot { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public bool QualificationAuthorityConfigured { get; init; }
    public ProjectApplyQualificationEvidence Qualification { get; init; } = new();
}

public sealed record ProjectApplyQualificationEvidence
{
    public bool RecordPresent { get; init; }
    public bool RecordSignatureValid { get; init; }
    public bool RecordBindingMatches { get; init; }
    public bool MarkerPresent { get; init; }
    public bool MarkerMatches { get; init; }
    public string QualificationId { get; init; } = string.Empty;
    public string QualificationFingerprint { get; init; } = string.Empty;
}
