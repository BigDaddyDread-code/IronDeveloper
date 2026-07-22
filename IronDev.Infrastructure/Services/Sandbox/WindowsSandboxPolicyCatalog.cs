using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using IronDev.Core.Sandbox;

namespace IronDev.Infrastructure.Services.Sandbox;

public sealed record WindowsSandboxOptions
{
    public bool Enabled { get; init; }
    public string RuntimeExecutablePath { get; init; } = string.Empty;
    public string ContainerImageDigestReference { get; init; } = string.Empty;
    public string OfflineFeedPath { get; init; } = string.Empty;
    public string OfflineFeedManifestSha256 { get; init; } = string.Empty;
}

public sealed record SandboxOfflineFeedManifest
{
    public required int SchemaVersion { get; init; }
    public IReadOnlyList<SandboxOfflineFeedPackage> Packages { get; init; } = [];
}

public sealed record SandboxOfflineFeedPackage
{
    public required string RelativePath { get; init; }
    public required long LengthBytes { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record WindowsSandboxHostPlatform(bool IsWindows, Architecture Architecture)
{
    public static WindowsSandboxHostPlatform Detect() =>
        new(OperatingSystem.IsWindows(), RuntimeInformation.OSArchitecture);
}

/// <summary>
/// Resolves the only v0.1 sandbox policy. Any missing, mutable, unsupported, or unsafe
/// input produces an unavailable capability and no executable policy.
/// </summary>
public sealed class WindowsSandboxPolicyCatalog : ISandboxRuntimePolicyCatalog
{
    public const string OfflineFeedManifestFileName = "irondev-offline-feed.manifest.json";

    private static readonly string[] EnvironmentAllowList =
    [
        "DOTNET_CLI_TELEMETRY_OPTOUT",
        "DOTNET_NOLOGO",
        "NUGET_HTTP_CACHE_PATH",
        "NUGET_PACKAGES",
        "TEMP",
        "TMP"
    ];

    private readonly WindowsSandboxOptions _options;
    private readonly WindowsSandboxHostPlatform _host;

    public WindowsSandboxPolicyCatalog(WindowsSandboxOptions options)
        : this(options, WindowsSandboxHostPlatform.Detect())
    {
    }

    public WindowsSandboxPolicyCatalog(
        WindowsSandboxOptions options,
        WindowsSandboxHostPlatform host)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public SandboxPolicyResolution Resolve(SandboxExecutionProfileBinding profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!_options.Enabled)
            return Unavailable(SandboxCapabilityStates.Disabled, SandboxReasonCodes.Disabled,
                "The production sandbox is disabled in this environment.");

        if (!_host.IsWindows || _host.Architecture != Architecture.X64)
            return Unavailable(SandboxCapabilityStates.UnsupportedHost, SandboxReasonCodes.WindowsX64Required,
                "The v0.1 production sandbox requires a Windows x64 host.");

        if (!WindowsSandboxPathValidator.TryNormalizeExistingFile(
                _options.RuntimeExecutablePath,
                out _,
                out var runtimeFailure))
        {
            return Unavailable(SandboxCapabilityStates.Unavailable, SandboxReasonCodes.RuntimeUnavailable,
                $"The trusted sandbox runtime is unavailable: {runtimeFailure}");
        }

        if (!TryNormalizeProfile(profile, out var normalizedProfile, out var profileFailure))
            return Unavailable(SandboxCapabilityStates.Unavailable, SandboxReasonCodes.ProfileInvalid, profileFailure);

        if (!TryParseDigestReference(
                _options.ContainerImageDigestReference,
                out var configuredImageRepository,
                out var imageDigest))
        {
            return Unavailable(SandboxCapabilityStates.Unavailable, SandboxReasonCodes.ImageNotDigestPinned,
                "The reviewed release image must use an exact sha256 digest reference; the planning tag is not execution authority.");
        }

        if (!TryParseImageRepository(normalizedProfile.ExecutionImageReference, out var profileImageRepository) ||
            !string.Equals(configuredImageRepository, profileImageRepository, StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable(SandboxCapabilityStates.Unavailable, SandboxReasonCodes.ImageNotDigestPinned,
                "The reviewed release image repository does not match the bound profile image repository.");
        }

        if (!TryResolveOfflineFeed(out var feedPath, out var feedHash, out var feedState, out var feedReason, out var feedFailure))
            return Unavailable(feedState, feedReason, feedFailure);

        var resources = SandboxResourcePolicy.WorkbenchV01;
        var policy = new SandboxRuntimePolicy
        {
            SchemaVersion = 2,
            PolicyVersion = SandboxPolicyVersions.WorkbenchV01,
            IsolationMode = SandboxIsolationModes.HcsHyperV,
            ProfileDefinitionId = normalizedProfile.ProfileDefinitionId,
            ProfileDescriptorRevision = normalizedProfile.ProfileDescriptorRevision,
            DescriptorSha256 = normalizedProfile.DescriptorSha256,
            TemplateBundleSha256 = normalizedProfile.TemplateBundleSha256,
            ToolchainManifestId = normalizedProfile.ToolchainManifestId,
            ContainerImageReference = $"{configuredImageRepository.ToLowerInvariant()}@sha256:{imageDigest}",
            ContainerImageDigest = imageDigest,
            OfflineFeedPath = feedPath,
            OfflineFeedManifestSha256 = feedHash,
            RepositoryInputReadOnly = true,
            OfflineFeedReadOnly = true,
            TrustedSupervisorVersion = WindowsJobSupervisorContract.Version,
            TrustedSupervisorSha256 = WindowsJobSupervisorContract.Sha256,
            Resources = resources,
            Restore = Command(SandboxExecutionStage.Restore, normalizedProfile.RestoreCommand, resources.RestoreTimeoutSeconds),
            Build = Command(SandboxExecutionStage.Build, normalizedProfile.BuildCommand, resources.BuildTimeoutSeconds),
            Test = Command(SandboxExecutionStage.Test, normalizedProfile.TestCommand, resources.TestTimeoutSeconds),
            EnvironmentAllowList = EnvironmentAllowList,
            PolicySha256 = string.Empty
        };

        var policyHash = SandboxRuntimePolicyCodec.ComputeHash(policy);
        policy = policy with { PolicySha256 = policyHash };

        return new SandboxPolicyResolution(
            new SandboxCapability(
                SandboxCapabilityStates.Available,
                SandboxReasonCodes.Ready,
                "The production sandbox policy is configured. Runtime controls must still pass pre-execution inspection.",
                SandboxPolicyVersions.WorkbenchV01,
                policyHash),
            policy);
    }

    private bool TryResolveOfflineFeed(
        out string canonicalPath,
        out string manifestHash,
        out string capabilityState,
        out string reasonCode,
        out string failure)
    {
        canonicalPath = string.Empty;
        manifestHash = string.Empty;
        capabilityState = SandboxCapabilityStates.Unavailable;
        reasonCode = SandboxReasonCodes.OfflineFeedUnavailable;
        failure = string.Empty;

        try
        {
            manifestHash = SandboxCanonicalJson.NormalizeSha256(
                _options.OfflineFeedManifestSha256,
                nameof(_options.OfflineFeedManifestSha256));
        }
        catch (SandboxContractValidationException)
        {
            failure = "A reviewed offline-feed manifest hash is required.";
            return false;
        }

        if (!WindowsSandboxPathValidator.TryNormalizeExistingDirectory(
                _options.OfflineFeedPath,
                out canonicalPath,
                out var pathFailure))
        {
            capabilityState = pathFailure.Contains("reparse", StringComparison.OrdinalIgnoreCase)
                ? SandboxCapabilityStates.Unsafe
                : SandboxCapabilityStates.Unavailable;
            reasonCode = capabilityState == SandboxCapabilityStates.Unsafe
                ? SandboxReasonCodes.UnsafeHostPath
                : SandboxReasonCodes.OfflineFeedUnavailable;
            failure = $"The curated offline feed is unavailable: {pathFailure}";
            return false;
        }

        var address = Path.GetFileName(canonicalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(address, manifestHash, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(address, $"sha256-{manifestHash}", StringComparison.OrdinalIgnoreCase))
        {
            reasonCode = SandboxReasonCodes.OfflineFeedIntegrityFailed;
            failure = "The curated offline feed directory is not addressed by its manifest hash.";
            return false;
        }

        var manifestPath = Path.Combine(canonicalPath, OfflineFeedManifestFileName);
        if (!WindowsSandboxPathValidator.TryNormalizeExistingFile(manifestPath, out manifestPath, out pathFailure))
        {
            capabilityState = pathFailure.Contains("reparse", StringComparison.OrdinalIgnoreCase)
                ? SandboxCapabilityStates.Unsafe
                : SandboxCapabilityStates.Unavailable;
            reasonCode = capabilityState == SandboxCapabilityStates.Unsafe
                ? SandboxReasonCodes.UnsafeHostPath
                : SandboxReasonCodes.OfflineFeedUnavailable;
            failure = $"The curated offline-feed manifest is unavailable: {pathFailure}";
            return false;
        }

        try
        {
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, manifestHash, StringComparison.Ordinal))
            {
                reasonCode = SandboxReasonCodes.OfflineFeedIntegrityFailed;
                failure = "The curated offline-feed manifest does not match its reviewed hash.";
                return false;
            }

            var manifest = JsonSerializer.Deserialize<SandboxOfflineFeedManifest>(
                manifestBytes,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNameCaseInsensitive = true
                });
            if (manifest is null || manifest.SchemaVersion != 1 || manifest.Packages is null || manifest.Packages.Count == 0)
                throw new JsonException("A schema-v1 manifest with at least one package is required.");
            if (!TryVerifyFeedFiles(canonicalPath, manifest, out failure))
            {
                reasonCode = SandboxReasonCodes.OfflineFeedIntegrityFailed;
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            reasonCode = SandboxReasonCodes.OfflineFeedIntegrityFailed;
            failure = "The curated offline-feed manifest could not be verified.";
            return false;
        }

        // The catalog makes this feed available only through the runtime's fixed read-only
        // mount. Host directory attributes are not treated as an isolation boundary.
        return true;
    }

    private static bool TryNormalizeProfile(
        SandboxExecutionProfileBinding profile,
        out SandboxExecutionProfileBinding normalized,
        out string failure)
    {
        normalized = profile;
        failure = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(profile.ProfileDefinitionId) || profile.ProfileDescriptorRevision <= 0)
                throw new SandboxContractValidationException("A profile id and positive descriptor revision are required.");
            if (string.IsNullOrWhiteSpace(profile.ToolchainManifestId))
                throw new SandboxContractValidationException("A pinned toolchain manifest id is required.");
            if (string.IsNullOrWhiteSpace(profile.RestoreCommand) ||
                string.IsNullOrWhiteSpace(profile.BuildCommand) ||
                string.IsNullOrWhiteSpace(profile.TestCommand))
                throw new SandboxContractValidationException("The profile must supply fixed restore, build, and test commands.");

            normalized = profile with
            {
                ProfileDefinitionId = profile.ProfileDefinitionId.Trim(),
                DescriptorSha256 = SandboxCanonicalJson.NormalizeSha256(profile.DescriptorSha256, nameof(profile.DescriptorSha256)),
                TemplateBundleSha256 = SandboxCanonicalJson.NormalizeSha256(profile.TemplateBundleSha256, nameof(profile.TemplateBundleSha256)),
                ToolchainManifestId = profile.ToolchainManifestId.Trim(),
                ExecutionImageReference = profile.ExecutionImageReference.Trim(),
                RestoreCommand = profile.RestoreCommand.Trim(),
                BuildCommand = profile.BuildCommand.Trim(),
                TestCommand = profile.TestCommand.Trim()
            };
            return true;
        }
        catch (SandboxContractValidationException exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static bool TryParseDigestReference(string reference, out string repository, out string digest)
    {
        repository = string.Empty;
        digest = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        const string marker = "@sha256:";
        var markerIndex = reference.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0 || markerIndex + marker.Length + 64 != reference.Length)
            return false;

        try
        {
            repository = reference[..markerIndex].Trim();
            if (string.IsNullOrWhiteSpace(repository) || repository.Any(char.IsWhiteSpace))
                return false;
            digest = SandboxCanonicalJson.NormalizeSha256(reference[(markerIndex + marker.Length)..], nameof(reference));
            return true;
        }
        catch (SandboxContractValidationException)
        {
            return false;
        }
    }

    private static bool TryParseImageRepository(string reference, out string repository)
    {
        repository = string.Empty;
        if (string.IsNullOrWhiteSpace(reference) || reference.Any(char.IsWhiteSpace))
            return false;

        var normalized = reference.Trim();
        var digestIndex = normalized.LastIndexOf('@');
        if (digestIndex >= 0)
            normalized = normalized[..digestIndex];

        var lastSlash = normalized.LastIndexOf('/');
        var tagIndex = normalized.LastIndexOf(':');
        if (tagIndex > lastSlash)
            normalized = normalized[..tagIndex];
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        repository = normalized;
        return true;
    }

    private static bool TryVerifyFeedFiles(
        string feedRoot,
        SandboxOfflineFeedManifest manifest,
        out string failure)
    {
        failure = string.Empty;
        if (manifest.Packages.Count > 10_000)
        {
            failure = "The curated offline-feed manifest exceeds the bounded package count.";
            return false;
        }

        var rootPrefix = feedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
        var packagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (package is null || package.LengthBytes < 0 ||
                !TryNormalizeRelativePath(package.RelativePath, out var relativePath))
            {
                failure = "The curated offline-feed manifest contains an unsafe package path or length.";
                return false;
            }
            if (!packagePaths.Add(relativePath))
            {
                failure = "The curated offline-feed manifest contains duplicate package paths.";
                return false;
            }

            string expectedHash;
            try
            {
                expectedHash = SandboxCanonicalJson.NormalizeSha256(package.Sha256, nameof(package.Sha256));
            }
            catch (SandboxContractValidationException)
            {
                failure = "The curated offline-feed manifest contains an invalid package hash.";
                return false;
            }

            var packagePath = Path.GetFullPath(Path.Combine(feedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!packagePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                !WindowsSandboxPathValidator.TryNormalizeExistingFile(packagePath, out packagePath, out _))
            {
                failure = "A curated offline-feed package is missing, outside the feed, or uses a reparse point.";
                return false;
            }

            var file = new FileInfo(packagePath);
            if (file.Length != package.LengthBytes)
            {
                failure = "A curated offline-feed package length does not match its manifest entry.";
                return false;
            }

            using var packageStream = File.OpenRead(packagePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(packageStream)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                failure = "A curated offline-feed package does not match its manifest hash.";
                return false;
            }
        }

        if (!TryInspectFeedTree(feedRoot, packagePaths, out failure))
            return false;

        var nugetConfigurationPath = Path.Combine(feedRoot, "NuGet.Config");
        if (!WindowsSandboxPathValidator.TryNormalizeExistingFile(
                nugetConfigurationPath,
                out nugetConfigurationPath,
                out _))
        {
            failure = "The curated offline feed requires a non-reparse NuGet.Config at its root.";
            return false;
        }
        if (!TryValidateNugetConfiguration(nugetConfigurationPath, out failure))
            return false;

        return true;
    }

    private static bool TryValidateNugetConfiguration(string path, out string failure)
    {
        failure = string.Empty;
        try
        {
            var raw = File.ReadAllText(path);
            string[] forbiddenMarkers =
            [
                "packageSourceCredentials", "ClearTextPassword", "<Password", "<Username",
                "apiKey", "token", "http://", "https://", "http_proxy", "https_proxy"
            ];
            if (forbiddenMarkers.Any(marker => raw.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                failure = "NuGet.Config contains credentials, proxy settings, or a network package source.";
                return false;
            }

            using var stringReader = new StringReader(raw);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var configuration = XDocument.Load(xmlReader, LoadOptions.None);
            var root = configuration.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "configuration", StringComparison.OrdinalIgnoreCase))
            {
                failure = "NuGet.Config must have a configuration root.";
                return false;
            }

            var packageSources = root.Elements()
                .SingleOrDefault(element => string.Equals(
                    element.Name.LocalName,
                    "packageSources",
                    StringComparison.OrdinalIgnoreCase));
            var sourceDirectives = packageSources?.Elements().ToArray() ?? [];
            if (packageSources is null || sourceDirectives.Length < 2 ||
                !string.Equals(sourceDirectives[0].Name.LocalName, "clear", StringComparison.OrdinalIgnoreCase) ||
                sourceDirectives.Count(element =>
                    string.Equals(element.Name.LocalName, "clear", StringComparison.OrdinalIgnoreCase)) != 1)
            {
                failure = "NuGet.Config must clear inherited package sources before declaring the offline source.";
                return false;
            }

            var sources = sourceDirectives
                .Where(element => string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (sources.Length == 0 || sources.Any(source =>
                    string.IsNullOrWhiteSpace(source.Attribute("key")?.Value) ||
                    !string.Equals(
                        source.Attribute("value")?.Value.TrimEnd('\\'),
                        @"C:\IronDev\Feed",
                        StringComparison.OrdinalIgnoreCase)))
            {
                failure = "NuGet.Config may contain only the sandbox-local C:\\IronDev\\Feed package source.";
                return false;
            }

            if (sourceDirectives.Any(element =>
                    !string.Equals(element.Name.LocalName, "clear", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase)))
            {
                failure = "NuGet.Config contains an unsupported package-source directive.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
        {
            failure = "NuGet.Config could not be parsed as the fixed offline-only configuration.";
            return false;
        }
    }

    private static bool TryInspectFeedTree(
        string feedRoot,
        IReadOnlySet<string> packagePaths,
        out string failure)
    {
        failure = string.Empty;
        var pending = new Stack<string>();
        pending.Push(feedRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    failure = "The curated offline feed contains a reparse point.";
                    return false;
                }
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                    continue;
                }

                var relativePath = Path.GetRelativePath(feedRoot, entry).Replace('\\', '/');
                if (entry.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                    !packagePaths.Contains(relativePath))
                {
                    failure = "The curated offline feed contains an unmanifested package.";
                    return false;
                }

                if (string.Equals(Path.GetFileName(entry), "NuGet.Config", StringComparison.OrdinalIgnoreCase) ||
                    entry.EndsWith(".nuget.config", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(
                            Path.GetFullPath(entry),
                            Path.Combine(feedRoot, "NuGet.Config"),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failure = "The curated offline feed contains an unexpected nested package configuration.";
                        return false;
                    }

                    var configuration = File.ReadAllText(entry);
                    string[] forbiddenMarkers =
                    [
                        "packageSourceCredentials", "ClearTextPassword", "<Password", "<Username",
                        "apiKey", "token", "http://", "https://", "http_proxy", "https_proxy"
                    ];
                    if (forbiddenMarkers.Any(marker =>
                            configuration.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                    {
                        failure = "The curated offline feed contains a credential-bearing or network package configuration.";
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool TryNormalizeRelativePath(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathFullyQualified(value))
            return false;

        normalized = value.Trim().Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment =>
                segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return false;
        normalized = string.Join('/', segments);
        return true;
    }

    private static SandboxCommandPolicy Command(SandboxExecutionStage stage, string command, int timeoutSeconds) =>
        new(stage, command, SandboxCanonicalJson.Sha256(command), timeoutSeconds);

    private static SandboxPolicyResolution Unavailable(string state, string reasonCode, string message) =>
        new(
            new SandboxCapability(
                state,
                reasonCode,
                message,
                SandboxPolicyVersions.WorkbenchV01,
                PolicySha256: null),
            Policy: null);
}

/// <summary>
/// Rejects relative, root, missing, and reparse-point paths. The runtime repeats this
/// check immediately before every mount/copy so a catalog result is never a TOCTOU grant.
/// </summary>
public static class WindowsSandboxPathValidator
{
    public static bool TryNormalizeExistingDirectory(
        string path,
        out string canonicalPath,
        out string failure) =>
        TryNormalizeExisting(path, FileAttributes.Directory, out canonicalPath, out failure);

    public static bool TryNormalizeExistingFile(
        string path,
        out string canonicalPath,
        out string failure) =>
        TryNormalizeExisting(path, expectedAttributes: null, out canonicalPath, out failure);

    private static bool TryNormalizeExisting(
        string path,
        FileAttributes? expectedAttributes,
        out string canonicalPath,
        out string failure)
    {
        canonicalPath = string.Empty;
        failure = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            failure = "an absolute path is required";
            return false;
        }

        try
        {
            canonicalPath = Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(canonicalPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(canonicalPath) || string.Equals(canonicalPath, root, StringComparison.OrdinalIgnoreCase))
            {
                failure = "a filesystem root is never an approved sandbox path";
                return false;
            }

            if (!File.Exists(canonicalPath) && !Directory.Exists(canonicalPath))
            {
                failure = "the path does not exist";
                return false;
            }

            var finalAttributes = File.GetAttributes(canonicalPath);
            if (expectedAttributes == FileAttributes.Directory && !finalAttributes.HasFlag(FileAttributes.Directory))
            {
                failure = "the path is not a directory";
                return false;
            }
            if (expectedAttributes is null && finalAttributes.HasFlag(FileAttributes.Directory))
            {
                failure = "the path is not a file";
                return false;
            }

            var current = canonicalPath;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    failure = "reparse points are not approved sandbox paths";
                    return false;
                }

                var parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            canonicalPath = string.Empty;
            failure = "the path could not be safely inspected";
            return false;
        }
    }
}
