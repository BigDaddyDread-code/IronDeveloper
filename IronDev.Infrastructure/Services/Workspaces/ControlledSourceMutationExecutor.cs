using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workspaces;
using Microsoft.Win32.SafeHandles;

namespace IronDev.Infrastructure.Services.Workspaces;

/// <summary>
/// The only controlled-project source writer. Live capability evaluation,
/// no-follow resolution, mutation through a verified handle, and result-hash
/// verification deliberately live in this one operation.
/// </summary>
public sealed class ControlledSourceMutationExecutor(
    IProjectApplyCapabilityService applyCapability) : IControlledSourceMutationExecutor
{
    private const int BufferSize = 64 * 1024;

    public Task<ControlledSourceMutationResult> ExecuteAsync(
        ControlledSourceMutationRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(request, validateOnly: false, cancellationToken);

    public async Task<ControlledSourceMutationBatchResult> ExecuteBatchAsync(
        IReadOnlyList<ControlledSourceMutationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return new ControlledSourceMutationBatchResult
            {
                Succeeded = true,
                SourceRepoMutated = false,
                Results = []
            };
        }

        // Validate every exact no-follow path and approved hash before the first
        // mutation. ExecuteCoreAsync repeats all validation during the write, so
        // this preserves all-or-nothing behavior for already-invalid batches
        // without turning validation into reusable authority.
        for (var index = 0; index < requests.Count; index++)
        {
            var validation = await ExecuteCoreAsync(
                requests[index], validateOnly: true, cancellationToken).ConfigureAwait(false);
            if (!validation.Succeeded)
            {
                return new ControlledSourceMutationBatchResult
                {
                    Succeeded = false,
                    SourceRepoMutated = false,
                    Results = [],
                    FailureOperationIndex = index,
                    FailureEvidence = validation.Evidence
                };
            }
        }

        var results = new List<ControlledSourceMutationResult>(requests.Count);
        var sourceRepoMutated = false;
        for (var index = 0; index < requests.Count; index++)
        {
            var result = await ExecuteCoreAsync(
                requests[index], validateOnly: false, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            sourceRepoMutated |= result.Evidence.SourceRepoMutated;
            if (!result.Succeeded)
            {
                return new ControlledSourceMutationBatchResult
                {
                    Succeeded = false,
                    SourceRepoMutated = sourceRepoMutated,
                    Results = results,
                    FailureOperationIndex = index,
                    FailureEvidence = result.Evidence
                };
            }
        }

        return new ControlledSourceMutationBatchResult
        {
            Succeeded = true,
            SourceRepoMutated = sourceRepoMutated,
            Results = results
        };
    }

    private async Task<ControlledSourceMutationResult> ExecuteCoreAsync(
        ControlledSourceMutationRequest request,
        bool validateOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return Refuse(request, ControlledSourceMutationReasonCodes.PlatformUnsupported,
                "Controlled source mutation requires a supported no-follow filesystem implementation; this platform is unsupported.");
        }

        if (request.ProjectId <= 0 ||
            string.IsNullOrWhiteSpace(request.RunId) ||
            string.IsNullOrWhiteSpace(request.ApplyAttemptId) ||
            string.IsNullOrWhiteSpace(request.ExpectedReadinessEvidenceHash))
        {
            return Refuse(request, ControlledSourceMutationReasonCodes.CapabilityContextMissing,
                "Controlled source mutation requires a project, run, apply attempt, and run-start readiness evidence hash.");
        }

        var operation = request.OperationKind.Trim().ToLowerInvariant();
        if (operation is not ("add" or "modify"))
        {
            return Refuse(request, ControlledSourceMutationReasonCodes.OperationUnsupported,
                $"Controlled source mutation does not support operation '{request.OperationKind}'.");
        }

        string sandboxRoot;
        string projectRoot;
        string workspaceRoot;
        string relativePath;
        try
        {
            sandboxRoot = NormalizeRoot(request.QualifiedSandboxRoot);
            projectRoot = NormalizeRoot(request.QualifiedProjectRoot);
            workspaceRoot = NormalizeRoot(request.QualifiedWorkspaceRoot);
            relativePath = NormalizeRelativePath(request.RelativePath);
            RequireStrictChild(sandboxRoot, projectRoot, "The qualified project root must be a strict child of the qualified sandbox root.");

            var expectedWorkspaceSource = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
            if (!PathEquals(expectedWorkspaceSource, request.WorkspaceSourcePath))
                throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.WorkspacePathUnsafe,
                    "The workspace source path does not match its qualified root and relative operation path.");
        }
        catch (NoFollowViolationException exception)
        {
            return Refuse(request, exception.ReasonCode, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return Refuse(request, ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                $"Controlled source mutation received an invalid qualified path: {exception.Message}");
        }

        var sourceRepoMutated = false;
        string? actualSourceHashBefore = null;
        string? actualWorkspaceHashBefore = null;
        string? actualSourceHashAfter = null;
        ProjectApplyCapability? liveCapability = null;

        try
        {
            using var workspaceChain = OpenNoFollowChain(workspaceRoot,
                ControlledSourceMutationReasonCodes.WorkspacePathUnsafe);
            var workspaceFileName = workspaceChain.OpenExistingParents(relativePath);
            using var workspaceHandle = workspaceChain.OpenExistingFile(
                workspaceFileName,
                FileAccess.Read,
                ControlledSourceMutationReasonCodes.WorkspacePathUnsafe);
            await using var workspaceStream = new FileStream(
                workspaceHandle,
                FileAccess.Read,
                BufferSize,
                isAsync: false);

            actualWorkspaceHashBefore = await ComputeSha256Async(workspaceStream, cancellationToken).ConfigureAwait(false);
            if (!HashEquals(actualWorkspaceHashBefore, request.ExpectedWorkspaceHash))
            {
                return Refuse(request, ControlledSourceMutationReasonCodes.WorkspaceHashMismatch,
                    "The no-follow workspace source hash no longer matches the approved apply evidence.",
                    actualSourceHashBefore, actualWorkspaceHashBefore);
            }

            using var destinationChain = OpenNoFollowChain(sandboxRoot,
                ControlledSourceMutationReasonCodes.DestinationPathUnsafe);
            destinationChain.OpenExistingRelative(Path.GetRelativePath(sandboxRoot, projectRoot));
            if (!PathEquals(destinationChain.CurrentPath, projectRoot))
            {
                throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                    "The verified project handle does not resolve to the qualified project root.");
            }

            var destinationFileName = destinationChain.OpenExistingParentPrefix(relativePath);
            SafeFileHandle? destinationHandle = null;
            FileStream? destinationStream = null;
            try
            {
                if (operation == "modify")
                {
                    if (destinationChain.HasMissingParents)
                    {
                        return Refuse(request, ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                            "A modify operation cannot create missing destination directories.",
                            actualSourceHashBefore, actualWorkspaceHashBefore);
                    }

                    destinationHandle = destinationChain.OpenExistingFile(
                        destinationFileName,
                        FileAccess.ReadWrite,
                        ControlledSourceMutationReasonCodes.DestinationPathUnsafe);
                    destinationStream = new FileStream(destinationHandle, FileAccess.ReadWrite, BufferSize, isAsync: false);
                    destinationHandle = null; // FileStream owns the handle.
                    actualSourceHashBefore = await ComputeSha256Async(destinationStream, cancellationToken).ConfigureAwait(false);
                    if (!HashEquals(actualSourceHashBefore, request.ExpectedSourceHash))
                    {
                        return Refuse(request, ControlledSourceMutationReasonCodes.SourceHashMismatch,
                            "The no-follow source hash no longer matches the approved apply evidence.",
                            actualSourceHashBefore, actualWorkspaceHashBefore);
                    }
                }
                else if (!destinationChain.HasMissingParents && destinationChain.NodeExists(destinationFileName))
                {
                    return Refuse(request, ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                        "An add operation cannot replace an existing destination node.",
                        actualSourceHashBefore, actualWorkspaceHashBefore);
                }

                if (validateOnly)
                {
                    return new ControlledSourceMutationResult
                    {
                        Succeeded = true,
                        Evidence = Evidence(request, ControlledSourceMutationReasonCodes.Validated,
                            "The exact no-follow operation path and approved hashes were validated without mutation.",
                            applied: false, sourceRepoMutated: false,
                            actualSourceHashBefore, actualWorkspaceHashBefore, actualSourceHashAfter: null,
                            liveReadinessEvidenceHash: string.Empty, changedBindings: [], nextSafeAction: string.Empty)
                    };
                }

                // This is the authority transition. The verified workspace file and
                // all existing destination ancestors are already held without delete
                // sharing. No source directory or file has been created or changed.
                liveCapability = await applyCapability.EvaluateAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
                var changedBindings = ChangedBindings(request, liveCapability);
                if (!liveCapability.IsReady ||
                    !string.Equals(liveCapability.ReadinessEvidenceHash, request.ExpectedReadinessEvidenceHash, StringComparison.Ordinal) ||
                    changedBindings.Count > 0)
                {
                    return Refuse(request, ControlledSourceMutationReasonCodes.CapabilityChangedBeforeMutation,
                        BuildCapabilityChangedReason(liveCapability, changedBindings),
                        actualSourceHashBefore, actualWorkspaceHashBefore, liveCapability, changedBindings);
                }

                workspaceChain.VerifyFileStillBound(workspaceStream.SafeFileHandle, request.WorkspaceSourcePath);
                if (destinationStream is not null)
                {
                    destinationChain.VerifyFileStillBound(
                        destinationStream.SafeFileHandle,
                        Path.Combine(projectRoot, relativePath));
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (operation == "add")
                {
                    sourceRepoMutated = destinationChain.CreateAndVerifyMissingParents();
                    destinationFileName = Path.GetFileName(relativePath);
                    destinationHandle = destinationChain.CreateNewFile(
                        destinationFileName,
                        ControlledSourceMutationReasonCodes.DestinationPathUnsafe);
                    destinationStream = new FileStream(destinationHandle, FileAccess.ReadWrite, BufferSize, isAsync: false);
                    destinationHandle = null;
                    sourceRepoMutated = true;
                }

                workspaceStream.Position = 0;
                destinationStream!.Position = 0;
                destinationStream.SetLength(0);
                sourceRepoMutated = true;
                await workspaceStream.CopyToAsync(destinationStream, BufferSize, cancellationToken).ConfigureAwait(false);
                await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                destinationStream.Flush(flushToDisk: true);

                actualSourceHashAfter = await ComputeSha256Async(destinationStream, cancellationToken).ConfigureAwait(false);
                if (!HashEquals(actualSourceHashAfter, request.ExpectedWorkspaceHash))
                {
                    return Refuse(request, ControlledSourceMutationReasonCodes.ResultHashMismatch,
                        "The source file hash after mutation does not match the approved workspace hash.",
                        actualSourceHashBefore, actualWorkspaceHashBefore, liveCapability,
                        sourceRepoMutated: true, actualSourceHashAfter: actualSourceHashAfter);
                }

                return new ControlledSourceMutationResult
                {
                    Succeeded = true,
                    Evidence = Evidence(request, ControlledSourceMutationReasonCodes.Applied,
                        "The live capability matched run-start evidence and the verified-handle mutation completed.",
                        applied: true, sourceRepoMutated: true,
                        actualSourceHashBefore, actualWorkspaceHashBefore, actualSourceHashAfter,
                        liveCapability.ReadinessEvidenceHash, [], string.Empty)
                };
            }
            finally
            {
                if (destinationStream is not null)
                    await destinationStream.DisposeAsync().ConfigureAwait(false);
                destinationHandle?.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NoFollowViolationException exception)
        {
            return Refuse(request, exception.ReasonCode, exception.Message,
                actualSourceHashBefore, actualWorkspaceHashBefore, liveCapability,
                sourceRepoMutated: sourceRepoMutated, actualSourceHashAfter: actualSourceHashAfter);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            return Refuse(request, ControlledSourceMutationReasonCodes.PlatformUnsupported,
                $"The platform cannot provide the required no-follow mutation semantics: {exception.Message}",
                actualSourceHashBefore, actualWorkspaceHashBefore, liveCapability,
                sourceRepoMutated: sourceRepoMutated, actualSourceHashAfter: actualSourceHashAfter);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return Refuse(request, ControlledSourceMutationReasonCodes.Failed,
                $"Controlled source mutation failed closed: {exception.Message}",
                actualSourceHashBefore, actualWorkspaceHashBefore, liveCapability,
                sourceRepoMutated: sourceRepoMutated, actualSourceHashAfter: actualSourceHashAfter);
        }
    }

    private static ControlledSourceMutationResult Refuse(
        ControlledSourceMutationRequest request,
        string reasonCode,
        string reason,
        string? actualSourceHashBefore = null,
        string? actualWorkspaceHashBefore = null,
        ProjectApplyCapability? liveCapability = null,
        IReadOnlyList<string>? changedBindings = null,
        bool sourceRepoMutated = false,
        string? actualSourceHashAfter = null) =>
        new()
        {
            Succeeded = false,
            Evidence = Evidence(request, reasonCode, reason, applied: false, sourceRepoMutated,
                actualSourceHashBefore, actualWorkspaceHashBefore, actualSourceHashAfter,
                liveCapability?.ReadinessEvidenceHash ?? string.Empty,
                changedBindings ?? [],
                liveCapability?.NextSafeAction ?? ProjectApplyCapabilityCommands.RestartInSandboxApplyMode)
        };

    private static ControlledSourceMutationEvidence Evidence(
        ControlledSourceMutationRequest request,
        string reasonCode,
        string reason,
        bool applied,
        bool sourceRepoMutated,
        string? actualSourceHashBefore,
        string? actualWorkspaceHashBefore,
        string? actualSourceHashAfter,
        string liveReadinessEvidenceHash,
        IReadOnlyList<string> changedBindings,
        string nextSafeAction) =>
        new()
        {
            ReasonCode = reasonCode,
            Reason = reason,
            Applied = applied,
            SourceRepoMutated = sourceRepoMutated,
            ProjectRoot = request.QualifiedProjectRoot,
            RelativePath = request.RelativePath,
            OperationKind = request.OperationKind,
            PreviousReadinessEvidenceHash = request.ExpectedReadinessEvidenceHash,
            LiveReadinessEvidenceHash = liveReadinessEvidenceHash,
            NextSafeAction = nextSafeAction,
            ActualSourceHashBefore = actualSourceHashBefore,
            ActualWorkspaceHashBefore = actualWorkspaceHashBefore,
            ActualSourceHashAfter = actualSourceHashAfter,
            ChangedBindings = changedBindings
        };

    private static List<string> ChangedBindings(
        ControlledSourceMutationRequest request,
        ProjectApplyCapability live)
    {
        var changed = new List<string>();
        AddChanged(changed, "readinessEvidenceHash", request.ExpectedReadinessEvidenceHash, live.ReadinessEvidenceHash);
        AddChanged(changed, "launcherSessionId", request.ExpectedLauncherSessionId, live.LauncherSessionId);
        AddChanged(changed, "sandboxRoot", NormalizeRoot(request.QualifiedSandboxRoot), NormalizeRoot(live.SandboxRoot), path: true);
        AddChanged(changed, "projectPath", NormalizeRoot(request.QualifiedProjectRoot), NormalizeRoot(live.ProjectPath), path: true);
        AddChanged(changed, "sandboxRootFingerprint", request.ExpectedSandboxRootFingerprint, live.SandboxRootFingerprint);
        AddChanged(changed, "projectPathFingerprint", request.ExpectedProjectPathFingerprint, live.ProjectPathFingerprint);
        AddChanged(changed, "qualificationId", request.ExpectedQualificationId, live.QualificationId);
        AddChanged(changed, "qualificationFingerprint", request.ExpectedQualificationFingerprint, live.QualificationFingerprint);
        if (!live.IsReady)
            changed.Add($"capability:{live.ReasonCode}");
        return changed.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void AddChanged(
        List<string> changed,
        string binding,
        string expected,
        string actual,
        bool path = false)
    {
        var equal = path
            ? PathEquals(expected, actual)
            : string.Equals(expected, actual, StringComparison.Ordinal);
        if (!equal) changed.Add(binding);
    }

    private static string BuildCapabilityChangedReason(
        ProjectApplyCapability live,
        IReadOnlyList<string> changedBindings)
    {
        var bindings = changedBindings.Count == 0 ? "unknown binding" : string.Join(", ", changedBindings);
        return $"Controlled apply capability changed before source mutation ({bindings}). " +
               $"Live reason: {live.ReasonCode}. {live.Reason} Exact safe action: {live.NextSafeAction}";
    }

    private static async Task<string> ComputeSha256Async(FileStream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool HashEquals(string? actual, string? expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        !string.IsNullOrWhiteSpace(expected) &&
        string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                "A qualified filesystem root is missing.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                "A controlled mutation path must be non-empty and relative.");

        var segments = path.Replace('/', Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment =>
                segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.EndsWith(' ') || segment.EndsWith('.')))
        {
            throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                "A controlled mutation path contains an unsafe segment.");
        }

        if (segments.Any(segment =>
                segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".irondev", StringComparison.OrdinalIgnoreCase)))
        {
            throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                "Controlled mutation cannot target reserved repository evidence paths.");
        }

        return Path.Combine(segments);
    }

    private static void RequireStrictChild(string root, string child, string message)
    {
        var prefix = root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!child.StartsWith(prefix, comparison))
            throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe, message);
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static INoFollowDirectoryChain OpenNoFollowChain(string root, string reasonCode)
    {
        if (OperatingSystem.IsWindows())
            return WindowsNoFollowDirectoryChain.Open(root, reasonCode);
        if (OperatingSystem.IsLinux())
            return LinuxNoFollowDirectoryChain.Open(root, reasonCode);
        throw new PlatformNotSupportedException("No controlled source-mutation filesystem implementation is available.");
    }

    private interface INoFollowDirectoryChain : IDisposable
    {
        string CurrentPath { get; }
        bool HasMissingParents { get; }
        void OpenExistingRelative(string relativeDirectory);
        string OpenExistingParents(string relativeFilePath);
        string OpenExistingParentPrefix(string relativeFilePath);
        bool CreateAndVerifyMissingParents();
        bool NodeExists(string fileName);
        SafeFileHandle OpenExistingFile(string fileName, FileAccess access, string reasonCode);
        SafeFileHandle CreateNewFile(string fileName, string reasonCode);
        void VerifyFileStillBound(SafeFileHandle handle, string expectedPath);
    }

    private sealed class WindowsNoFollowDirectoryChain : INoFollowDirectoryChain
    {
        private const uint FileReadAttributes = 0x0080;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagWriteThrough = 0x80000000;
        private const int CreateNew = 1;
        private const int OpenExisting = 3;
        private const int ErrorFileNotFound = 2;
        private const int ErrorPathNotFound = 3;
        private const int ErrorAlreadyExists = 183;

        private readonly List<SafeFileHandle> _directoryHandles = [];
        private readonly string _reasonCode;
        private readonly Queue<string> _missingParents = new();

        private WindowsNoFollowDirectoryChain(string root, string reasonCode)
        {
            _reasonCode = reasonCode;
            CurrentPath = root;
            _directoryHandles.Add(OpenAndVerifyDirectory(root, reasonCode));
        }

        public string CurrentPath { get; private set; }
        public bool HasMissingParents => _missingParents.Count > 0;

        public static WindowsNoFollowDirectoryChain Open(string root, string reasonCode) =>
            new(root, reasonCode);

        public void OpenExistingRelative(string relativeDirectory)
        {
            foreach (var segment in Segments(relativeDirectory))
                OpenExistingDirectory(segment);
        }

        public string OpenExistingParents(string relativeFilePath)
        {
            var segments = Segments(relativeFilePath);
            if (segments.Length == 0)
                throw Violation("The controlled file path is empty.");
            foreach (var segment in segments[..^1])
                OpenExistingDirectory(segment);
            return segments[^1];
        }

        public string OpenExistingParentPrefix(string relativeFilePath)
        {
            var segments = Segments(relativeFilePath);
            if (segments.Length == 0)
                throw Violation("The controlled file path is empty.");

            var missing = false;
            foreach (var segment in segments[..^1])
            {
                if (missing)
                {
                    _missingParents.Enqueue(segment);
                    continue;
                }

                var candidate = Path.Combine(CurrentPath, segment);
                if (!Directory.Exists(candidate))
                {
                    if (File.Exists(candidate) || NodeExists(segment))
                        throw Violation($"Destination parent '{segment}' is not a directory.");
                    _missingParents.Enqueue(segment);
                    missing = true;
                    continue;
                }

                OpenExistingDirectory(segment);
            }

            return segments[^1];
        }

        public bool CreateAndVerifyMissingParents()
        {
            var created = false;
            while (_missingParents.Count > 0)
            {
                var segment = _missingParents.Dequeue();
                var candidate = Path.Combine(CurrentPath, segment);
                if (!CreateDirectoryW(candidate, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorAlreadyExists)
                        throw new Win32Exception(error, $"Could not create destination directory '{segment}'.");
                }
                else
                {
                    created = true;
                }

                OpenExistingDirectory(segment);
            }

            return created;
        }

        public bool NodeExists(string fileName)
        {
            try
            {
                using var handle = OpenNode(Path.Combine(CurrentPath, fileName), FileReadAttributes, FileShare.ReadWrite, OpenExisting,
                    FileFlagOpenReparsePoint | FileFlagBackupSemantics, allowMissing: true);
                return handle is not null;
            }
            catch (Win32Exception exception)
            {
                throw Violation($"Destination node could not be inspected without following reparse points: {exception.Message}");
            }
        }

        public SafeFileHandle OpenExistingFile(string fileName, FileAccess access, string reasonCode)
        {
            var desiredAccess = access switch
            {
                FileAccess.Read => GenericRead | FileReadAttributes,
                FileAccess.ReadWrite => GenericRead | GenericWrite | FileReadAttributes,
                _ => throw new NotSupportedException($"File access '{access}' is not supported.")
            };
            var share = access == FileAccess.Read ? FileShare.Read : FileShare.Read;
            var path = Path.Combine(CurrentPath, fileName);
            try
            {
                // Hold a no-delete, attribute-only handle while opening the data
                // handle so the exact destination cannot be replaced between
                // reparse inspection and use.
                using var guard = OpenNode(path, FileReadAttributes, FileShare.ReadWrite, OpenExisting,
                    FileFlagOpenReparsePoint | FileFlagBackupSemantics, allowMissing: false)!;
                VerifyFile(guard, path, reasonCode);
                var handle = OpenNode(path, desiredAccess, share, OpenExisting, FileFlagOpenReparsePoint, allowMissing: false)!;
                VerifyFile(handle, path, reasonCode);
                return handle;
            }
            catch (NoFollowViolationException)
            {
                throw;
            }
            catch (Win32Exception exception)
            {
                throw new NoFollowViolationException(reasonCode,
                    $"File could not be opened through its verified no-follow handle: {exception.Message}");
            }
        }

        public SafeFileHandle CreateNewFile(string fileName, string reasonCode)
        {
            var path = Path.Combine(CurrentPath, fileName);
            try
            {
                var handle = OpenNode(path, GenericRead | GenericWrite | FileReadAttributes, FileShare.Read,
                    CreateNew, FileFlagOpenReparsePoint | FileFlagWriteThrough, allowMissing: false)!;
                VerifyFile(handle, path, reasonCode);
                return handle;
            }
            catch (NoFollowViolationException)
            {
                throw;
            }
            catch (Win32Exception exception)
            {
                throw new NoFollowViolationException(reasonCode,
                    $"Destination file could not be created through its verified parent handle: {exception.Message}");
            }
        }

        public void VerifyFileStillBound(SafeFileHandle handle, string expectedPath) =>
            VerifyFile(handle, expectedPath, _reasonCode);

        private void OpenExistingDirectory(string segment)
        {
            var candidate = Path.Combine(CurrentPath, segment);
            var handle = OpenAndVerifyDirectory(candidate, _reasonCode);
            _directoryHandles.Add(handle);
            CurrentPath = candidate;
        }

        private static SafeFileHandle OpenAndVerifyDirectory(string path, string reasonCode)
        {
            SafeFileHandle handle;
            try
            {
                handle = OpenNode(path, FileReadAttributes, FileShare.ReadWrite, OpenExisting,
                    FileFlagOpenReparsePoint | FileFlagBackupSemantics, allowMissing: false)!;
            }
            catch (Win32Exception exception)
            {
                throw new NoFollowViolationException(reasonCode,
                    $"Directory could not be opened without following reparse points: {exception.Message}");
            }
            try
            {
                var info = GetAttributeInfo(handle);
                if (((FileAttributes)info.FileAttributes).HasFlag(FileAttributes.ReparsePoint))
                    throw new NoFollowViolationException(reasonCode, $"Path component is a reparse point: {path}");
                if (!((FileAttributes)info.FileAttributes).HasFlag(FileAttributes.Directory))
                    throw new NoFollowViolationException(reasonCode, $"Path component is not a directory: {path}");
                VerifyFinalPath(handle, path, reasonCode);
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static void VerifyFile(SafeFileHandle handle, string path, string reasonCode)
        {
            var info = GetAttributeInfo(handle);
            var attributes = (FileAttributes)info.FileAttributes;
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new NoFollowViolationException(reasonCode, $"File path is a symbolic link or reparse point: {path}");
            if (attributes.HasFlag(FileAttributes.Directory))
                throw new NoFollowViolationException(reasonCode, $"File path resolves to a directory: {path}");
            VerifyFinalPath(handle, path, reasonCode);
        }

        private static void VerifyFinalPath(SafeFileHandle handle, string expectedPath, string reasonCode)
        {
            var buffer = new StringBuilder(512);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not resolve verified path '{expectedPath}'.");
            if (length >= buffer.Capacity)
            {
                buffer = new StringBuilder((int)length + 1);
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not resolve verified path '{expectedPath}'.");
            }

            var finalPath = NormalizeFinalPath(buffer.ToString());
            if (!PathEquals(finalPath, expectedPath))
                throw new NoFollowViolationException(reasonCode,
                    $"Verified handle resolved outside its expected path. Expected '{expectedPath}', resolved '{finalPath}'.");
        }

        private static SafeFileHandle? OpenNode(
            string path,
            uint desiredAccess,
            FileShare share,
            int creationDisposition,
            uint flags,
            bool allowMissing)
        {
            var handle = CreateFileW(path, desiredAccess, share, IntPtr.Zero, creationDisposition, flags, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;

            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (allowMissing && error is ErrorFileNotFound or ErrorPathNotFound)
                return null;
            throw new Win32Exception(error, $"Could not open controlled path without following reparse points: {path}");
        }

        private static FileAttributeTagInfo GetAttributeInfo(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileInfoByHandleClass.FileAttributeTagInfo,
                    out var info,
                    (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect controlled path attributes.");
            }
            return info;
        }

        private NoFollowViolationException Violation(string message) => new(_reasonCode, message);

        private static string[] Segments(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                    "A no-follow traversal path must be relative.");
            var segments = relativePath.Replace('/', Path.DirectorySeparatorChar)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".."))
                throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                    "A no-follow traversal path cannot contain parent traversal.");
            return segments;
        }

        private static string NormalizeFinalPath(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path[8..];
            if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return path[4..];
            return path;
        }

        public void Dispose()
        {
            for (var index = _directoryHandles.Count - 1; index >= 0; index--)
                _directoryHandles[index].Dispose();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            int creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryW(string pathName, IntPtr securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathLength,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            FileInfoByHandleClass fileInformationClass,
            out FileAttributeTagInfo fileInformation,
            uint bufferSize);

        private enum FileInfoByHandleClass
        {
            FileAttributeTagInfo = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAttributeTagInfo
        {
            public uint FileAttributes;
            public uint ReparseTag;
        }
    }

    /// <summary>
    /// Linux equivalent of the Windows handle walk. Every child is opened or
    /// created relative to the already verified parent descriptor, with
    /// O_NOFOLLOW. /proc/self/fd is used to re-verify the live binding before
    /// mutation, so rename/replacement races fail closed.
    /// </summary>
    private sealed class LinuxNoFollowDirectoryChain : INoFollowDirectoryChain
    {
        private const int O_RDONLY = 0;
        private const int O_RDWR = 2;
        private const int O_CREAT = 0x40;
        private const int O_EXCL = 0x80;
        private const int O_DIRECTORY = 0x10000;
        private const int O_NOFOLLOW = 0x20000;
        private const int O_CLOEXEC = 0x80000;
        private const int O_PATH = 0x200000;
        private const int ENOENT = 2;
        private const int EEXIST = 17;
        private const uint S_IFMT = 0xF000;
        private const uint S_IFDIR = 0x4000;
        private const uint S_IFREG = 0x8000;

        private readonly List<SafeFileHandle> _directoryHandles = [];
        private readonly Queue<string> _missingParents = new();
        private readonly string _reasonCode;

        private LinuxNoFollowDirectoryChain(string root, string reasonCode)
        {
            _reasonCode = reasonCode;
            CurrentPath = root;
            var rootFd = open(root, O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC, 0);
            if (rootFd < 0)
                throw Violation($"Qualified root could not be opened without following links: {LinuxError()}");
            var handle = new SafeFileHandle((IntPtr)rootFd, ownsHandle: true);
            try
            {
                VerifyDirectoryStillBound(handle, root);
                _directoryHandles.Add(handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public string CurrentPath { get; private set; }
        public bool HasMissingParents => _missingParents.Count > 0;

        public static LinuxNoFollowDirectoryChain Open(string root, string reasonCode) =>
            new(root, reasonCode);

        public void OpenExistingRelative(string relativeDirectory)
        {
            foreach (var segment in Segments(relativeDirectory))
                OpenExistingDirectory(segment);
        }

        public string OpenExistingParents(string relativeFilePath)
        {
            var segments = Segments(relativeFilePath);
            if (segments.Length == 0) throw Violation("The controlled file path is empty.");
            foreach (var segment in segments[..^1])
                OpenExistingDirectory(segment);
            return segments[^1];
        }

        public string OpenExistingParentPrefix(string relativeFilePath)
        {
            var segments = Segments(relativeFilePath);
            if (segments.Length == 0) throw Violation("The controlled file path is empty.");
            var missing = false;
            foreach (var segment in segments[..^1])
            {
                if (missing)
                {
                    _missingParents.Enqueue(segment);
                    continue;
                }

                var fd = openat(CurrentFd, segment, O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC, 0);
                if (fd < 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == ENOENT)
                    {
                        _missingParents.Enqueue(segment);
                        missing = true;
                        continue;
                    }
                    throw Violation($"Destination parent '{segment}' could not be opened without following links: {LinuxError(error)}");
                }

                AddVerifiedDirectory(segment, new SafeFileHandle((IntPtr)fd, ownsHandle: true));
            }
            return segments[^1];
        }

        public bool CreateAndVerifyMissingParents()
        {
            var created = false;
            while (_missingParents.Count > 0)
            {
                VerifyDirectoryStillBound(CurrentHandle, CurrentPath);
                var segment = _missingParents.Dequeue();
                if (mkdirat(CurrentFd, segment, Convert.ToUInt32("700", 8)) != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != EEXIST)
                        throw Violation($"Destination directory '{segment}' could not be created through its verified parent: {LinuxError(error)}");
                }
                else
                {
                    created = true;
                }
                OpenExistingDirectory(segment);
            }
            return created;
        }

        public bool NodeExists(string fileName)
        {
            var fd = openat(CurrentFd, fileName, O_PATH | O_NOFOLLOW | O_CLOEXEC, 0);
            if (fd >= 0)
            {
                new SafeFileHandle((IntPtr)fd, ownsHandle: true).Dispose();
                return true;
            }
            var error = Marshal.GetLastPInvokeError();
            if (error == ENOENT) return false;
            throw Violation($"Destination node could not be inspected without following links: {LinuxError(error)}");
        }

        public SafeFileHandle OpenExistingFile(string fileName, FileAccess access, string reasonCode)
        {
            VerifyDirectoryStillBound(CurrentHandle, CurrentPath);
            var flags = (access == FileAccess.Read ? O_RDONLY : O_RDWR) | O_NOFOLLOW | O_CLOEXEC;
            var fd = openat(CurrentFd, fileName, flags, 0);
            if (fd < 0)
                throw new NoFollowViolationException(reasonCode,
                    $"File could not be opened without following links: {LinuxError()}");
            var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
            try
            {
                VerifyFileStillBound(handle, Path.Combine(CurrentPath, fileName));
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public SafeFileHandle CreateNewFile(string fileName, string reasonCode)
        {
            VerifyDirectoryStillBound(CurrentHandle, CurrentPath);
            var fd = openat(CurrentFd, fileName,
                O_RDWR | O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC,
                Convert.ToUInt32("600", 8));
            if (fd < 0)
                throw new NoFollowViolationException(reasonCode,
                    $"Destination file could not be created through its verified parent: {LinuxError()}");
            var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
            try
            {
                VerifyFileStillBound(handle, Path.Combine(CurrentPath, fileName));
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public void VerifyFileStillBound(SafeFileHandle handle, string expectedPath)
        {
            VerifyFinalPath(handle, expectedPath);
            var mode = Mode(handle);
            if ((mode & S_IFMT) != S_IFREG)
                throw Violation($"Verified file handle is not a regular no-follow file: {expectedPath}");
        }

        private int CurrentFd => CurrentHandle.DangerousGetHandle().ToInt32();
        private SafeFileHandle CurrentHandle => _directoryHandles[^1];

        private void OpenExistingDirectory(string segment)
        {
            VerifyDirectoryStillBound(CurrentHandle, CurrentPath);
            var fd = openat(CurrentFd, segment, O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC, 0);
            if (fd < 0)
                throw Violation($"Directory component '{segment}' could not be opened without following links: {LinuxError()}");
            AddVerifiedDirectory(segment, new SafeFileHandle((IntPtr)fd, ownsHandle: true));
        }

        private void AddVerifiedDirectory(string segment, SafeFileHandle handle)
        {
            var expected = Path.Combine(CurrentPath, segment);
            try
            {
                VerifyDirectoryStillBound(handle, expected);
                _directoryHandles.Add(handle);
                CurrentPath = expected;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private void VerifyDirectoryStillBound(SafeFileHandle handle, string expectedPath)
        {
            VerifyFinalPath(handle, expectedPath);
            var mode = Mode(handle);
            if ((mode & S_IFMT) != S_IFDIR)
                throw Violation($"Verified directory handle is not a normal directory: {expectedPath}");
        }

        private uint Mode(SafeFileHandle handle)
        {
            if (fstat(handle.DangerousGetHandle().ToInt32(), out var stat) != 0)
                throw Violation($"Verified handle metadata could not be read: {LinuxError()}");
            return stat.Mode;
        }

        private void VerifyFinalPath(SafeFileHandle handle, string expectedPath)
        {
            var buffer = new byte[4096];
            var length = readlink(ProcFdPath(handle), buffer, (nuint)buffer.Length);
            if (length < 0)
                throw Violation($"Verified handle path could not be resolved: {LinuxError()}");
            var finalPath = Encoding.UTF8.GetString(buffer, 0, (int)length);
            if (!PathEquals(finalPath, expectedPath))
                throw Violation($"Verified handle changed binding. Expected '{expectedPath}', resolved '{finalPath}'.");
        }

        private static string ProcFdPath(SafeFileHandle handle) =>
            $"/proc/self/fd/{handle.DangerousGetHandle().ToInt64()}";

        private NoFollowViolationException Violation(string message) => new(_reasonCode, message);

        private static string[] Segments(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                    "A no-follow traversal path must be relative.");
            var segments = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".."))
                throw new NoFollowViolationException(ControlledSourceMutationReasonCodes.DestinationPathUnsafe,
                    "A no-follow traversal path cannot contain parent traversal.");
            return segments;
        }

        private static string LinuxError(int? error = null) =>
            new Win32Exception(error ?? Marshal.GetLastPInvokeError()).Message;

        public void Dispose()
        {
            for (var index = _directoryHandles.Count - 1; index >= 0; index--)
                _directoryHandles[index].Dispose();
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int open(string pathName, int flags, uint mode);

        [DllImport("libc", SetLastError = true)]
        private static extern int openat(int directoryFd, string pathName, int flags, uint mode);

        [DllImport("libc", SetLastError = true)]
        private static extern int mkdirat(int directoryFd, string pathName, uint mode);

        [DllImport("libc", SetLastError = true)]
        private static extern nint readlink(string pathName, byte[] buffer, nuint bufferSize);

        [DllImport("libc", SetLastError = true)]
        private static extern int fstat(int fileDescriptor, out LinuxStat stat);

        [StructLayout(LayoutKind.Sequential)]
        private struct LinuxStat
        {
            public ulong Device;
            public ulong Inode;
            public ulong HardLinkCount;
            public uint Mode;
            public uint UserId;
            public uint GroupId;
            public int Padding;
            public ulong RawDevice;
            public long Size;
            public long BlockSize;
            public long BlockCount;
            public LinuxTimespec AccessTime;
            public LinuxTimespec ModificationTime;
            public LinuxTimespec StatusChangeTime;
            public long Reserved0;
            public long Reserved1;
            public long Reserved2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LinuxTimespec
        {
            public long Seconds;
            public long Nanoseconds;
        }
    }

    private sealed class NoFollowViolationException(string reasonCode, string message) : IOException(message)
    {
        public string ReasonCode { get; } = reasonCode;
    }
}
