using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Infrastructure.Services;

public interface IProjectApplyQualificationStore
{
    bool IsAuthorityConfigured(string sessionId);
    ProjectApplyQualificationEvidence Read(ProjectApplyCapabilityInput input);
    Task<ProjectApplyQualificationEvidence> IssueAsync(
        ProjectApplyCapabilityInput input,
        int qualifyingActorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>Signed server-record persistence and non-secret Git correlation only.</summary>
public sealed class ProjectApplyQualificationStore : IProjectApplyQualificationStore
{
    public const string DisposableMarkerFileName = ".irondev-disposable-sandbox";
    public const int QualificationContractVersion = 1;
    private const string QualificationDirectoryName = "project-apply-qualifications";
    private const string QualificationSigningKeyEnvironmentVariable = "IRONDEV_LOCALTEST_QUALIFICATION_KEY";
    private const string ApiLogPathEnvironmentVariable = "IRONDEV_LOCALTEST_API_LOG_PATH";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public bool IsAuthorityConfigured(string sessionId) =>
        TryGetAuthority(sessionId, out _, out _);

    public ProjectApplyQualificationEvidence Read(ProjectApplyCapabilityInput input)
    {
        if (!TryGetAuthority(input.ApiSessionId, out var storeRoot, out var signingKey))
            return new ProjectApplyQualificationEvidence();

        var recordPath = RecordPath(storeRoot, input.TenantId, input.ProjectId);
        var recordPresent = File.Exists(recordPath);
        var record = ReadJson<QualificationRecord>(recordPath);
        if (record is null)
            return new ProjectApplyQualificationEvidence { RecordPresent = recordPresent };

        var signatureValid = SignatureIsValid(record, signingKey);
        var bindingMatches = signatureValid && BindingMatches(record, input);
        var markerPath = MarkerPath(input.ProjectPath);
        var markerPresent = markerPath is not null && File.Exists(markerPath);
        var marker = markerPath is null ? null : ReadJson<QualificationMarker>(markerPath);
        return new ProjectApplyQualificationEvidence
        {
            RecordPresent = recordPresent,
            RecordSignatureValid = signatureValid,
            RecordBindingMatches = bindingMatches,
            MarkerPresent = markerPresent,
            MarkerMatches = marker is not null &&
                            marker.ContractVersion == QualificationContractVersion &&
                            marker.QualificationId.Equals(record.QualificationId, StringComparison.Ordinal) &&
                            marker.RecordFingerprint.Equals(record.RecordFingerprint, StringComparison.Ordinal),
            QualificationId = record.QualificationId,
            QualificationFingerprint = record.RecordFingerprint
        };
    }

    public async Task<ProjectApplyQualificationEvidence> IssueAsync(
        ProjectApplyCapabilityInput input,
        int qualifyingActorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthority(input.ApiSessionId, out var storeRoot, out var signingKey))
            throw new IOException("The server-owned qualification authority is unavailable.");
        var markerPath = MarkerPath(input.ProjectPath)
            ?? throw new IOException("A safe Git metadata directory is required for qualification correlation.");

        Directory.CreateDirectory(storeRoot);
        var recordPath = RecordPath(storeRoot, input.TenantId, input.ProjectId);
        var record = ReadJson<QualificationRecord>(recordPath);
        if (record is null || !SignatureIsValid(record, signingKey) || !BindingMatches(record, input))
        {
            record = CreateRecord(input, qualifyingActorUserId, signingKey);
            await WriteJsonAtomicallyAsync(recordPath, record, cancellationToken).ConfigureAwait(false);
        }

        var marker = new QualificationMarker
        {
            ContractVersion = QualificationContractVersion,
            QualificationId = record.QualificationId,
            RecordFingerprint = record.RecordFingerprint
        };
        if (ReadJson<QualificationMarker>(markerPath) != marker)
            await WriteJsonAtomicallyAsync(markerPath, marker, cancellationToken).ConfigureAwait(false);

        return Read(input);
    }

    private static QualificationRecord CreateRecord(
        ProjectApplyCapabilityInput input,
        int qualifyingActorUserId,
        byte[] signingKey)
    {
        var record = new QualificationRecord
        {
            ContractVersion = QualificationContractVersion,
            QualificationId = Guid.NewGuid().ToString("N"),
            TenantId = input.TenantId,
            ProjectId = input.ProjectId,
            CanonicalProjectPathHash = ProjectApplyCapabilityEvaluator.Fingerprint(
                ProjectApplyCapabilityEvaluator.Normalize(input.ProjectPath)),
            SandboxRootHash = ProjectApplyCapabilityEvaluator.Fingerprint(
                ProjectApplyCapabilityEvaluator.Normalize(input.SandboxRoot)),
            DatabaseName = input.DatabaseName,
            LauncherSessionId = input.LauncherSessionId,
            QualifyingActorUserId = qualifyingActorUserId,
            QualifiedAtUtc = DateTimeOffset.UtcNow
        };
        var fingerprint = ProjectApplyCapabilityEvaluator.Fingerprint(CanonicalPayload(record));
        return record with
        {
            RecordFingerprint = fingerprint,
            Signature = Sign(CanonicalPayload(record) + "\n" + fingerprint, signingKey)
        };
    }

    private static bool SignatureIsValid(QualificationRecord record, byte[] signingKey)
    {
        if (record.ContractVersion != QualificationContractVersion ||
            string.IsNullOrWhiteSpace(record.QualificationId) ||
            record.TenantId <= 0 || record.ProjectId <= 0 || record.QualifyingActorUserId <= 0 ||
            string.IsNullOrWhiteSpace(record.CanonicalProjectPathHash) ||
            string.IsNullOrWhiteSpace(record.SandboxRootHash) ||
            string.IsNullOrWhiteSpace(record.DatabaseName) ||
            string.IsNullOrWhiteSpace(record.LauncherSessionId) ||
            record.QualifiedAtUtc == default ||
            string.IsNullOrWhiteSpace(record.RecordFingerprint) ||
            string.IsNullOrWhiteSpace(record.Signature))
            return false;

        var fingerprint = ProjectApplyCapabilityEvaluator.Fingerprint(CanonicalPayload(record));
        if (!FixedTimeEquals(record.RecordFingerprint, fingerprint)) return false;
        return FixedTimeEquals(record.Signature, Sign(CanonicalPayload(record) + "\n" + fingerprint, signingKey));
    }

    private static bool BindingMatches(QualificationRecord record, ProjectApplyCapabilityInput input) =>
        record.ContractVersion == QualificationContractVersion &&
        record.TenantId == input.TenantId &&
        record.ProjectId == input.ProjectId &&
        record.CanonicalProjectPathHash.Equals(
            ProjectApplyCapabilityEvaluator.Fingerprint(ProjectApplyCapabilityEvaluator.Normalize(input.ProjectPath)),
            StringComparison.Ordinal) &&
        record.SandboxRootHash.Equals(
            ProjectApplyCapabilityEvaluator.Fingerprint(ProjectApplyCapabilityEvaluator.Normalize(input.SandboxRoot)),
            StringComparison.Ordinal) &&
        record.DatabaseName.Equals(input.DatabaseName, StringComparison.Ordinal) &&
        record.LauncherSessionId.Equals(input.LauncherSessionId, StringComparison.Ordinal);

    private static string CanonicalPayload(QualificationRecord record) => string.Join("\n",
        record.ContractVersion.ToString(CultureInfo.InvariantCulture),
        record.QualificationId,
        record.TenantId.ToString(CultureInfo.InvariantCulture),
        record.ProjectId.ToString(CultureInfo.InvariantCulture),
        record.CanonicalProjectPathHash,
        record.SandboxRootHash,
        record.DatabaseName,
        record.LauncherSessionId,
        record.QualifyingActorUserId.ToString(CultureInfo.InvariantCulture),
        record.QualifiedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private static bool TryGetAuthority(string sessionId, out string storeRoot, out byte[] signingKey)
    {
        storeRoot = string.Empty;
        signingKey = [];
        try
        {
            var apiLogPath = Environment.GetEnvironmentVariable(ApiLogPathEnvironmentVariable)?.Trim();
            var encodedKey = Environment.GetEnvironmentVariable(QualificationSigningKeyEnvironmentVariable)?.Trim();
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(apiLogPath) || string.IsNullOrWhiteSpace(encodedKey))
                return false;

            var sessionRoot = ProjectApplyCapabilityEvaluator.Normalize(Path.GetDirectoryName(Path.GetFullPath(apiLogPath)));
            if (string.IsNullOrWhiteSpace(sessionRoot) ||
                !Directory.Exists(sessionRoot) ||
                !Path.GetFileName(sessionRoot).Equals(sessionId, StringComparison.Ordinal) ||
                ProjectApplyCapabilityEvaluator.ContainsAncestorReparsePoint(sessionRoot))
                return false;

            signingKey = Convert.FromBase64String(encodedKey);
            if (signingKey.Length < 32)
            {
                signingKey = [];
                return false;
            }

            storeRoot = Path.Combine(sessionRoot, QualificationDirectoryName);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or IOException or UnauthorizedAccessException)
        {
            storeRoot = string.Empty;
            signingKey = [];
            return false;
        }
    }

    private static string RecordPath(string storeRoot, int tenantId, int projectId) =>
        Path.Combine(storeRoot, $"tenant-{tenantId}-project-{projectId}.json");

    private static string? MarkerPath(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return null;
        var gitMetadata = Path.Combine(ProjectApplyCapabilityEvaluator.Normalize(projectPath), ".git");
        return Directory.Exists(gitMetadata) && !ProjectApplyCapabilityEvaluator.HasReparsePoint(gitMetadata)
            ? Path.Combine(gitMetadata, DisposableMarkerFileName)
            : null;
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new IOException("Qualification path has no parent directory.");
        Directory.CreateDirectory(directory);
        if (ProjectApplyCapabilityEvaluator.ContainsAncestorReparsePoint(directory) ||
            (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))
            throw new IOException("Qualification evidence cannot be written through a reparse point.");

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string Sign(string value, byte[] signingKey) =>
        Convert.ToHexString(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record QualificationRecord
    {
        public int ContractVersion { get; init; }
        public string QualificationId { get; init; } = string.Empty;
        public int TenantId { get; init; }
        public int ProjectId { get; init; }
        public string CanonicalProjectPathHash { get; init; } = string.Empty;
        public string SandboxRootHash { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public string LauncherSessionId { get; init; } = string.Empty;
        public int QualifyingActorUserId { get; init; }
        public DateTimeOffset QualifiedAtUtc { get; init; }
        public string RecordFingerprint { get; init; } = string.Empty;
        public string Signature { get; init; } = string.Empty;
    }

    private sealed record QualificationMarker
    {
        public int ContractVersion { get; init; }
        public string QualificationId { get; init; } = string.Empty;
        public string RecordFingerprint { get; init; } = string.Empty;
    }
}
