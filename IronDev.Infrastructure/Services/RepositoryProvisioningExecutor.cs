using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Workbench;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class RepositoryProvisioningGitRunner : IRepositoryProvisioningGitRunner
{
    private const int MaxCapturedCharacters = 64 * 1024;
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "init", "add", "commit", "rev-parse", "status", "log", "ls-files", "ls-tree", "cat-file"
    };

    private readonly string? _gitExecutable;
    private readonly bool _unsafeGitExecutableConfiguration;
    private readonly TimeSpan _timeout;

    public RepositoryProvisioningGitRunner(IConfiguration configuration)
    {
        var configuredExecutable = configuration["WorkbenchRepositoryProvisioning:GitExecutable"]?.Trim();
        try
        {
            _gitExecutable = ResolveGitExecutable(
                string.IsNullOrWhiteSpace(configuredExecutable) ? "git" : configuredExecutable,
                configuration["WorkbenchRepositorySetup:ApprovedWorkspaceRoot"]?.Trim());
        }
        catch (RepositoryProvisioningIntegrityException)
        {
            // Git is needed only by the later provisioning mutation. Keep repository setup
            // context and project shaping available, then fail the mutation with a bounded,
            // sanitized integrity result if the configured executable is unsafe.
            _unsafeGitExecutableConfiguration = true;
        }
        var seconds = configuration.GetValue<int?>("WorkbenchRepositoryProvisioning:GitTimeoutSeconds") ?? 30;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 120));
    }

    private static string? ResolveGitExecutable(string configured, string? approvedWorkspaceRoot)
    {
        if (Path.IsPathFullyQualified(configured))
            return ValidateResolvedExecutable(configured, approvedWorkspaceRoot);
        if (configured.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\']) >= 0)
            throw new RepositoryProvisioningIntegrityException(
                "The configured Git executable must be an absolute path or a simple executable name.");

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var currentDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory.GetCurrentDirectory()));
        var executableName = OperatingSystem.IsWindows() && !configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? configured + ".exe"
            : configured;
        foreach (var rawDirectory in pathValue.Split(Path.PathSeparator))
        {
            var value = rawDirectory.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
                continue;
            string directory;
            try
            {
                directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            }
            catch
            {
                continue;
            }
            if (PathWithin(directory, currentDirectory))
                continue;
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate) && !IsWithinApprovedWorkspace(candidate, approvedWorkspaceRoot))
                return ValidateResolvedExecutable(candidate, approvedWorkspaceRoot);
        }
        return null;
    }

    private static string ValidateResolvedExecutable(string candidate, string? approvedWorkspaceRoot)
    {
        var resolved = Path.GetFullPath(candidate);
        if (!Path.IsPathFullyQualified(resolved) || !File.Exists(resolved))
            throw new RepositoryProvisioningIntegrityException(
                "The controlled Git executable is not an existing absolute file.");
        if (IsWithinApprovedWorkspace(resolved, approvedWorkspaceRoot))
            throw new RepositoryProvisioningIntegrityException(
                "The controlled Git executable cannot be loaded from the repository workspace.");
        return resolved;
    }

    private static bool IsWithinApprovedWorkspace(string candidate, string? approvedWorkspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(approvedWorkspaceRoot))
            return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(approvedWorkspaceRoot));
        return PathWithin(Path.GetFullPath(candidate), root);
    }

    private static bool PathWithin(string candidate, string root) =>
        string.Equals(candidate, root, PathComparison) ||
        candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<RepositoryProvisioningGitResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        DateTime deterministicCommitTimeUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || arguments.Count == 0 ||
            !TryFindCommand(arguments, out var command) || !AllowedCommands.Contains(command))
            throw new RepositoryProvisioningIntegrityException(
                "The bounded repository Git operation was not recognized.");

        if (_unsafeGitExecutableConfiguration)
            throw new RepositoryProvisioningExecutionException(
                RepositoryProvisioningFailureCodes.GitUnavailable,
                "The controlled Git executable is unavailable.");
        if (string.IsNullOrWhiteSpace(_gitExecutable))
            throw new RepositoryProvisioningExecutionException(
                RepositoryProvisioningFailureCodes.GitUnavailable,
                "The controlled Git executable is unavailable.");

        var startInfo = new ProcessStartInfo(_gitExecutable)
        {
            WorkingDirectory = repositoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.excludesFile=" + nullDevice);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.attributesFile=" + nullDevice);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var key in startInfo.Environment.Keys
                     .Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                                   key.StartsWith("GCM_", StringComparison.OrdinalIgnoreCase) ||
                                   key.StartsWith("GH_", StringComparison.OrdinalIgnoreCase) ||
                                   key.StartsWith("SSH_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            startInfo.Environment.Remove(key);
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        startInfo.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        startInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        var normalizedCommitTimeUtc = deterministicCommitTimeUtc.Kind switch
        {
            DateTimeKind.Utc => deterministicCommitTimeUtc,
            DateTimeKind.Local => deterministicCommitTimeUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(deterministicCommitTimeUtc, DateTimeKind.Utc)
        };
        var gitDate = normalizedCommitTimeUtc.ToString("O");
        startInfo.Environment["GIT_AUTHOR_DATE"] = gitDate;
        startInfo.Environment["GIT_COMMITTER_DATE"] = gitDate;

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new RepositoryProvisioningExecutionException(
                    RepositoryProvisioningFailureCodes.GitUnavailable,
                    "The controlled Git process could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            var stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await KillAndSettleAsync(process);
                throw new RepositoryProvisioningExecutionException(
                    RepositoryProvisioningFailureCodes.GitCommandTimedOut,
                    "A controlled Git operation timed out.");
            }
            catch (OperationCanceledException)
            {
                await KillAndSettleAsync(process);
                throw;
            }
            catch (RepositoryProvisioningExecutionException)
            {
                await KillAndSettleAsync(process);
                throw;
            }

            return new RepositoryProvisioningGitResult(
                process.ExitCode,
                await stdout,
                await stderr);
        }
        catch (RepositoryProvisioningExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new RepositoryProvisioningExecutionException(
                RepositoryProvisioningFailureCodes.GitUnavailable,
                "The controlled Git executable is unavailable.",
                exception);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return result.ToString();
            if (result.Length + read > MaxCapturedCharacters)
                throw new RepositoryProvisioningExecutionException(
                    RepositoryProvisioningFailureCodes.GitCommandFailed,
                    "A controlled Git operation produced excessive output.");
            result.Append(buffer, 0, read);
        }
    }

    private static async Task KillAndSettleAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        try
        {
            using var settle = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(settle.Token);
        }
        catch (OperationCanceledException)
        {
            // Never turn a bounded Git timeout or caller cancellation into an unbounded
            // wait if the operating system cannot terminate or reap the child promptly.
        }
        catch
        {
        }
    }

    private static bool TryFindCommand(IReadOnlyList<string> arguments, out string command)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "-c")
            {
                index++;
                continue;
            }
            command = arguments[index];
            return true;
        }
        command = string.Empty;
        return false;
    }
}

public sealed class RepositoryProvisioningDirectoryCreator : IRepositoryProvisioningDirectoryCreator
{
    private const int WindowsAlreadyExists = 183;
    private const int UnixAlreadyExists = 17;

    public void CreateNew(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (CreateDirectoryWindows(path, IntPtr.Zero))
                return;
            var error = Marshal.GetLastPInvokeError();
            throw new IOException(
                error == WindowsAlreadyExists
                    ? "The exact provisioning staging directory already exists."
                    : "The provisioning staging directory could not be created atomically.",
                new System.ComponentModel.Win32Exception(error));
        }

        if (CreateDirectoryUnix(path, 448) == 0) // 0700
            return;
        var unixError = Marshal.GetLastPInvokeError();
        throw new IOException(
            unixError == UnixAlreadyExists
                ? "The exact provisioning staging directory already exists."
                : "The provisioning staging directory could not be created atomically.",
            new System.ComponentModel.Win32Exception(unixError));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryWindows(string path, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int CreateDirectoryUnix(string path, uint mode);
}

public sealed class RepositoryProvisioningExecutor : IRepositoryProvisioningExecutor
{
    private const string DefaultBranch = "main";
    private const string FixedIdentityName = "IronDev Workbench";
    private const string FixedIdentityEmail = "workbench@irondev.invalid";
    private const string MarkerName = ".irondev-provisioning-attempt.json";
    private const string PublicationEvidenceName = ".irondev-publication-evidence.json";
    private const string CanonicalGitConfig =
        "[core]\n" +
        "\trepositoryformatversion = 0\n" +
        "\tfilemode = false\n" +
        "\tbare = false\n" +
        "\tlogallrefupdates = true\n";
    private const string CanonicalHead = "ref: refs/heads/main\n";

    private readonly IRepositorySetupPathPolicy _pathPolicy;
    private readonly IRepositoryProvisioningGitRunner _git;
    private readonly IRepositoryProvisioningDirectoryCreator _directoryCreator;
    private readonly IRepositoryProvisioningFailureInjector _failureInjector;

    public RepositoryProvisioningExecutor(
        IRepositorySetupPathPolicy pathPolicy,
        IRepositoryProvisioningGitRunner git,
        IRepositoryProvisioningDirectoryCreator directoryCreator,
        IRepositoryProvisioningFailureInjector failureInjector)
    {
        _pathPolicy = pathPolicy;
        _git = git;
        _directoryCreator = directoryCreator;
        _failureInjector = failureInjector;
    }

    public async Task<RepositoryProvisioningExecutionEvidence> ExecuteOrRecoverAsync(
        RepositoryProvisioningExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var rendered = RenderAndValidate(request);
        var manifest = BuildManifest(request, rendered);
        var target = RevalidateTarget(request, requireAbsent: false);
        var root = Path.GetDirectoryName(target)!;
        var targetName = Path.GetFileName(target);
        var staging = Path.Combine(root, $".{targetName}.irondev-{request.AttemptId:N}.staging");
        EnsureDirectChild(root, staging);

        if (Directory.Exists(target))
        {
            if (!HasExactPublishedMarker(target, request))
            {
                TryCleanupOwnedStaging(root, staging, request);
                throw Failure(
                    RepositoryProvisioningFailureCodes.TargetAlreadyExists,
                    "The confirmed repository target is no longer empty.");
            }
            return await VerifyPublishedAsync(request, rendered, manifest, wasRecovered: true, cancellationToken);
        }
        if (File.Exists(target))
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw Failure(
                RepositoryProvisioningFailureCodes.TargetAlreadyExists,
                "The confirmed repository target is no longer empty.");
        }
        if (Directory.Exists(staging))
        {
            TryCleanupOwnedStaging(root, staging, request);
            if (Directory.Exists(staging))
                throw Failure(
                    RepositoryProvisioningFailureCodes.FileSystemFailed,
                    "The exact provisioning attempt has unverified staging evidence.");
        }
        if (File.Exists(staging))
            throw Failure(
                RepositoryProvisioningFailureCodes.FileSystemFailed,
                "The exact provisioning staging path is occupied by an unknown file.");

        try
        {
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.BeforeStagingCreate);
            _directoryCreator.CreateNew(staging);
            EnsureNoReparsePoints(root, staging);
            var markerJson = MarkerJson(request);
            WriteNewUtf8File(Path.Combine(staging, MarkerName), markerJson + "\n");
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.StagingCreated);

            foreach (var file in rendered.Files.OrderBy(value => value.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = ResolveSafeChild(staging, file.RelativePath);
                var parent = Path.GetDirectoryName(outputPath)!;
                Directory.CreateDirectory(parent);
                EnsureNoReparsePoints(staging, parent);
                WriteNewUtf8File(outputPath, file.Utf8Content);
            }
            EnsureNoReparsePoints(staging, staging);
            VerifyRenderedFiles(staging, rendered, manifest);
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.BundleRendered);

            var emptyTemplate = Path.Combine(staging, ".irondev-empty-git-template");
            Directory.CreateDirectory(emptyTemplate);
            await GitOk(staging,
                ["init", "--object-format=sha1", "--initial-branch=" + DefaultBranch, "--template=" + emptyTemplate],
                request.DeterministicCommitTimeUtc,
                cancellationToken);
            EnsureNoReparsePoints(staging, Path.Combine(staging, ".git"));
            Directory.Delete(emptyTemplate, recursive: false);
            WriteCanonicalGitConfig(staging);
            var emptyHooks = Path.Combine(staging, ".git", "irondev-empty-hooks");
            Directory.CreateDirectory(emptyHooks);
            VerifyGitControlPlane(staging);
            var gitMarker = Path.Combine(staging, ".git", MarkerName);
            File.Move(Path.Combine(staging, MarkerName), gitMarker);
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.GitInitialized);

            await GitOk(staging,
                ["-c", "core.autocrlf=false", "add", "--all", "--", "."],
                request.DeterministicCommitTimeUtc,
                cancellationToken);
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.GitIndexCreated);
            var message = CommitMessage(request);
            await GitOk(staging,
                [
                    "-c", "core.autocrlf=false",
                    "-c", "user.name=" + FixedIdentityName,
                    "-c", "user.email=" + FixedIdentityEmail,
                    "-c", "commit.gpgSign=false",
                    "-c", "credential.helper=",
                    "-c", "core.hooksPath=" + emptyHooks,
                    "commit", "--no-gpg-sign", "--no-verify", "--message", message
                ],
                request.DeterministicCommitTimeUtc,
                cancellationToken);
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.GitCommitted);

            await VerifyRepositoryAsync(staging, request, rendered, manifest, cancellationToken);
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.BeforePublish);

            RevalidateTarget(request, requireAbsent: true);
            EnsureNoReparsePoints(root, staging);
            var publishedAtUtc = DateTime.UtcNow;
            WriteNewUtf8File(
                Path.Combine(staging, ".git", PublicationEvidenceName),
                PublicationEvidenceJson(request, publishedAtUtc) + "\n");
            Directory.Move(staging, target);
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.AfterPublish);
            return await VerifyPublishedAsync(request, rendered, manifest, wasRecovered: false, cancellationToken);
        }
        catch (RepositoryProvisioningExecutionException)
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw;
        }
        catch (RepositoryProvisioningIntegrityException)
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw;
        }
        catch (RepositorySetupIntegrityException exception)
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw Failure(
                RepositoryProvisioningFailureCodes.TemplateIntegrityFailed,
                "The pinned repository template failed integrity validation.",
                exception);
        }
        catch (IOException exception)
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw Failure(
                RepositoryProvisioningFailureCodes.FileSystemFailed,
                "The repository could not be published safely.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw Failure(
                RepositoryProvisioningFailureCodes.FileSystemFailed,
                "The isolated repository workspace is not writable.",
                exception);
        }
        catch (OperationCanceledException)
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw;
        }
        catch (Exception exception)
        {
            TryCleanupOwnedStaging(root, staging, request);
            throw Failure(
                RepositoryProvisioningFailureCodes.UnexpectedFailure,
                "Repository provisioning stopped before it could publish safely.",
                exception);
        }
    }

    public async Task<RepositoryProvisioningPublishedInspection> InspectPublishedRepositoryForAttemptAsync(
        RepositoryProvisioningExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateRequest(request);
            var target = RevalidateTarget(request, requireAbsent: false);
            var markerInspection = InspectPublishedMarker(target, request);
            if (markerInspection == PublishedMarkerInspectionState.AbsentOrForeign)
                return new RepositoryProvisioningPublishedInspection(
                    RepositoryProvisioningPublishedInspectionState.AbsentOrForeign,
                    RepositoryProvisioningFailureCodes.TargetAlreadyExists);
            if (markerInspection == PublishedMarkerInspectionState.VerificationUnavailable)
                return new RepositoryProvisioningPublishedInspection(
                    RepositoryProvisioningPublishedInspectionState.VerificationUnavailable,
                    RepositoryProvisioningFailureCodes.FileSystemFailed);
            _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.PublishedMarkerInspected);
            var rendered = RenderAndValidate(request);
            var manifest = BuildManifest(request, rendered);
            _ = await VerifyPublishedAsync(request, rendered, manifest, wasRecovered: true, cancellationToken);
            return new RepositoryProvisioningPublishedInspection(
                RepositoryProvisioningPublishedInspectionState.Verified,
                RepositorySetupReasonCodes.Ready);
        }
        catch (RepositoryProvisioningExecutionException exception) when (
            exception.ReasonCode is RepositoryProvisioningFailureCodes.GitUnavailable or
                RepositoryProvisioningFailureCodes.GitCommandTimedOut or
                RepositoryProvisioningFailureCodes.GitCommandFailed ||
            exception.InnerException is IOException or UnauthorizedAccessException)
        {
            return new RepositoryProvisioningPublishedInspection(
                RepositoryProvisioningPublishedInspectionState.VerificationUnavailable,
                exception.ReasonCode);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException)
        {
            return new RepositoryProvisioningPublishedInspection(
                RepositoryProvisioningPublishedInspectionState.VerificationUnavailable,
                RepositoryProvisioningFailureCodes.FileSystemFailed);
        }
        catch (Exception exception)
        {
            var reason = exception is RepositoryProvisioningExecutionException provisioning
                ? provisioning.ReasonCode
                : RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid;
            return new RepositoryProvisioningPublishedInspection(
                RepositoryProvisioningPublishedInspectionState.Invalid,
                reason);
        }
    }

    private async Task<RepositoryProvisioningExecutionEvidence> VerifyPublishedAsync(
        RepositoryProvisioningExecutionRequest request,
        RepositorySetupTemplateBundle rendered,
        ProvisioningManifest manifest,
        bool wasRecovered,
        CancellationToken cancellationToken)
    {
        var target = RevalidateTarget(request, requireAbsent: false);
        var targetAttributes = GetExistingAttributes(target);
        if (targetAttributes is null || !targetAttributes.Value.HasFlag(FileAttributes.Directory))
            throw Failure(
                RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid,
                "The published repository is unavailable.");
        var markerInspection = InspectPublishedMarker(target, request);
        if (markerInspection == PublishedMarkerInspectionState.VerificationUnavailable)
            throw new IOException("Published repository ownership evidence is temporarily unavailable.");
        if (markerInspection != PublishedMarkerInspectionState.Exact)
            throw Failure(
                RepositoryProvisioningFailureCodes.TargetAlreadyExists,
                "The confirmed repository target is owned by another operation.");
        var publishedAtUtc = ReadPublishedAtUtc(target, request);
        EnsureNoReparsePoints(Path.GetDirectoryName(target)!, target);
        var (head, tree) = await VerifyRepositoryAsync(target, request, rendered, manifest, cancellationToken);
        return new RepositoryProvisioningExecutionEvidence(
            target,
            DefaultBranch,
            head,
            manifest.Json,
            manifest.Sha256,
            tree,
            publishedAtUtc,
            wasRecovered);
    }

    private async Task<(string Head, string Tree)> VerifyRepositoryAsync(
        string repository,
        RepositoryProvisioningExecutionRequest request,
        RepositorySetupTemplateBundle rendered,
        ProvisioningManifest manifest,
        CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(Path.GetDirectoryName(repository)!, repository);
        VerifyRenderedFiles(repository, rendered, manifest);
        VerifyExactWorkingTree(repository, rendered);
        VerifyGitControlPlane(repository);

        var head = (await GitOk(repository, ["rev-parse", "--verify", "HEAD"],
            request.DeterministicCommitTimeUtc, cancellationToken)).Trim();
        var tree = (await GitOk(repository, ["rev-parse", "HEAD^{tree}"],
            request.DeterministicCommitTimeUtc, cancellationToken)).Trim();
        if (!IsLowerHexObjectId(head) || !IsLowerHexObjectId(tree))
            throw InvalidPublished();
        VerifyExactRefs(repository, head);

        var rawCommit = await GitOk(repository, ["cat-file", "commit", "HEAD"],
            request.DeterministicCommitTimeUtc, cancellationToken);
        VerifyCanonicalRootCommit(rawCommit, head, tree, request);

        var status = await GitOk(repository, ["status", "--porcelain=v1", "--untracked-files=all"],
            request.DeterministicCommitTimeUtc, cancellationToken);
        if (!string.IsNullOrEmpty(status))
            throw InvalidPublished();

        var branch = (await GitOk(repository, ["rev-parse", "--abbrev-ref", "HEAD"],
            request.DeterministicCommitTimeUtc, cancellationToken)).Trim();
        if (!string.Equals(branch, DefaultBranch, StringComparison.Ordinal))
            throw InvalidPublished();

        var commitBody = await GitOk(repository, ["log", "-1", "--format=%B"],
            request.DeterministicCommitTimeUtc, cancellationToken);
        if (!HasExactTrailer(commitBody, "IronDev-Plan-Hash", request.ConfirmedPlan.PlanHash) ||
            !HasExactTrailer(commitBody, "IronDev-Provisioning-Attempt", request.AttemptId.ToString("D")) ||
            !HasExactTrailer(commitBody, "IronDev-Manifest-Sha256", manifest.Sha256))
            throw InvalidPublished();

        var tracked = await GitOk(repository, ["ls-files", "-z"],
            request.DeterministicCommitTimeUtc, cancellationToken);
        var trackedPaths = tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var expectedPaths = rendered.Files.Select(value => value.RelativePath.Replace('\\', '/')).
            OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!trackedPaths.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expectedPaths, StringComparer.Ordinal))
            throw InvalidPublished();
        await VerifyTreeAndIndexAsync(repository, request, rendered, cancellationToken);
        return (head, tree);
    }

    private async Task VerifyTreeAndIndexAsync(
        string repository,
        RepositoryProvisioningExecutionRequest request,
        RepositorySetupTemplateBundle rendered,
        CancellationToken cancellationToken)
    {
        var expectedTree = rendered.Files.Select(file =>
        {
            var bytes = RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict.GetBytes(file.Utf8Content);
            var blobId = ComputeGitObjectId("blob", bytes);
            return $"100644 blob {blobId}\t{file.RelativePath.Replace('\\', '/')}";
        }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expectedIndex = rendered.Files.Select(file =>
        {
            var bytes = RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict.GetBytes(file.Utf8Content);
            var blobId = ComputeGitObjectId("blob", bytes);
            return $"100644 {blobId} 0\t{file.RelativePath.Replace('\\', '/')}";
        }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expectedFlags = rendered.Files
            .Select(file => $"H {file.RelativePath.Replace('\\', '/')}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var tree = SplitNull(await GitOk(
            repository,
            ["ls-tree", "-r", "-z", "--full-tree", "HEAD"],
            request.DeterministicCommitTimeUtc,
            cancellationToken));
        var index = SplitNull(await GitOk(
            repository,
            ["ls-files", "--stage", "-z"],
            request.DeterministicCommitTimeUtc,
            cancellationToken));
        var flags = SplitNull(await GitOk(
            repository,
            ["ls-files", "-v", "-z"],
            request.DeterministicCommitTimeUtc,
            cancellationToken));
        if (!tree.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expectedTree, StringComparer.Ordinal) ||
            !index.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expectedIndex, StringComparer.Ordinal) ||
            !flags.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expectedFlags, StringComparer.Ordinal))
            throw InvalidPublished();
    }

    private static string[] SplitNull(string value) =>
        value.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static string ComputeGitObjectId(string kind, byte[] content)
    {
        var header = Encoding.ASCII.GetBytes($"{kind} {content.Length}\0");
        var objectBytes = new byte[header.Length + content.Length];
        Buffer.BlockCopy(header, 0, objectBytes, 0, header.Length);
        Buffer.BlockCopy(content, 0, objectBytes, header.Length, content.Length);
        return Convert.ToHexString(SHA1.HashData(objectBytes)).ToLowerInvariant();
    }

    private static void VerifyGitControlPlane(string repository)
    {
        var git = Path.Combine(repository, ".git");
        var gitAttributes = GetExistingAttributes(git);
        if (gitAttributes is null || !gitAttributes.Value.HasFlag(FileAttributes.Directory) ||
            gitAttributes.Value.HasFlag(FileAttributes.ReparsePoint))
            throw InvalidPublished();
        var forbidden = new[]
        {
            Path.Combine(git, "refs", "replace"),
            Path.Combine(git, "packed-refs"),
            Path.Combine(git, "info", "grafts"),
            Path.Combine(git, "info", "attributes"),
            Path.Combine(git, "hooks"),
            Path.Combine(git, "shallow"),
            Path.Combine(git, "commondir"),
            Path.Combine(git, "config.worktree"),
            Path.Combine(git, "worktrees"),
            Path.Combine(git, "modules"),
            Path.Combine(git, "objects", "info", "alternates"),
            Path.Combine(git, "objects", "info", "http-alternates"),
            Path.Combine(git, "MERGE_HEAD"),
            Path.Combine(git, "CHERRY_PICK_HEAD"),
            Path.Combine(git, "REVERT_HEAD"),
            Path.Combine(git, "REBASE_HEAD"),
            Path.Combine(git, "AUTO_MERGE"),
            Path.Combine(git, "ORIG_HEAD"),
            Path.Combine(git, "FETCH_HEAD"),
            Path.Combine(git, "sequencer"),
            Path.Combine(git, "rebase-merge"),
            Path.Combine(git, "rebase-apply")
        };
        if (forbidden.Any(path => GetExistingAttributes(path) is not null) ||
            Directory.EnumerateFileSystemEntries(git, "BISECT_*", SearchOption.TopDirectoryOnly).Any())
            throw InvalidPublished();

        var config = Path.Combine(git, "config");
        var head = Path.Combine(git, "HEAD");
        var emptyHooks = Path.Combine(git, "irondev-empty-hooks");
        var emptyHookAttributes = GetExistingAttributes(emptyHooks);
        if (!IsExactRegularFile(config, CanonicalGitConfig) ||
            !IsExactRegularFile(head, CanonicalHead) ||
            emptyHookAttributes is null ||
            !emptyHookAttributes.Value.HasFlag(FileAttributes.Directory) ||
            emptyHookAttributes.Value.HasFlag(FileAttributes.ReparsePoint) ||
            Directory.EnumerateFileSystemEntries(emptyHooks).Any())
            throw InvalidPublished();

        VerifyNoRecursiveReparsePoints(git);
    }

    private static void VerifyNoRecursiveReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw InvalidPublished();
                if (attributes.HasFlag(FileAttributes.Directory))
                    pending.Push(entry);
            }
        }
    }

    private static void VerifyExactRefs(string repository, string head)
    {
        var refs = Path.Combine(repository, ".git", "refs");
        var main = Path.Combine(refs, "heads", DefaultBranch);
        var refsAttributes = GetExistingAttributes(refs);
        if (refsAttributes is null || !refsAttributes.Value.HasFlag(FileAttributes.Directory) ||
            refsAttributes.Value.HasFlag(FileAttributes.ReparsePoint) ||
            !IsExactRegularFile(main, head + "\n"))
            throw InvalidPublished();

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(refs);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw InvalidPublished();
                if (attributes.HasFlag(FileAttributes.Directory))
                    pending.Push(entry);
                else
                    files.Add(Path.GetRelativePath(refs, entry).Replace('\\', '/'));
            }
        }

        if (files.Count != 1 || !string.Equals(files[0], "heads/main", StringComparison.Ordinal))
            throw InvalidPublished();
    }

    private static bool IsExactRegularFile(string path, string expected)
    {
        var attributes = GetExistingAttributes(path);
        if (attributes is null)
            return false;
        return !attributes.Value.HasFlag(FileAttributes.Directory) &&
               !attributes.Value.HasFlag(FileAttributes.ReparsePoint) &&
               string.Equals(
                   File.ReadAllText(path, RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict),
                   expected,
                   StringComparison.Ordinal);
    }

    private static void WriteCanonicalGitConfig(string repository)
    {
        var config = Path.Combine(repository, ".git", "config");
        var attributes = GetExistingAttributes(config);
        if (attributes is null || attributes.Value.HasFlag(FileAttributes.Directory) ||
            attributes.Value.HasFlag(FileAttributes.ReparsePoint))
            throw InvalidPublished();
        var bytes = RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict.GetBytes(CanonicalGitConfig);
        using var stream = new FileStream(
            config,
            FileMode.Truncate,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void VerifyCanonicalRootCommit(
        string rawCommit,
        string head,
        string tree,
        RepositoryProvisioningExecutionRequest request)
    {
        var normalizedTime = request.DeterministicCommitTimeUtc.Kind switch
        {
            DateTimeKind.Utc => request.DeterministicCommitTimeUtc,
            DateTimeKind.Local => request.DeterministicCommitTimeUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(request.DeterministicCommitTimeUtc, DateTimeKind.Utc)
        };
        var epoch = new DateTimeOffset(normalizedTime).ToUnixTimeSeconds();
        var identity = $"{FixedIdentityName} <{FixedIdentityEmail}> {epoch} +0000";
        var expected = $"tree {tree}\n" +
                       $"author {identity}\n" +
                       $"committer {identity}\n\n" +
                       CommitMessage(request) + "\n";
        if (!string.Equals(rawCommit, expected, StringComparison.Ordinal))
            throw InvalidPublished();
        var computed = ComputeGitObjectId("commit", Encoding.UTF8.GetBytes(rawCommit));
        if (!string.Equals(computed, head, StringComparison.Ordinal))
            throw InvalidPublished();
    }

    private static void VerifyRenderedFiles(
        string repository,
        RepositorySetupTemplateBundle rendered,
        ProvisioningManifest manifest)
    {
        foreach (var expected in rendered.Files)
        {
            var path = ResolveSafeChild(repository, expected.RelativePath);
            var attributes = GetExistingAttributes(path);
            if (attributes is null || attributes.Value.HasFlag(FileAttributes.Directory) ||
                attributes.Value.HasFlag(FileAttributes.ReparsePoint))
                throw InvalidPublished();
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw InvalidPublished(exception);
            }
            var expectedBytes = RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict.GetBytes(expected.Utf8Content);
            if (!bytes.AsSpan().SequenceEqual(expectedBytes))
                throw InvalidPublished();
        }

        if (!string.Equals(
                RepositorySetupCanonicalJson.Sha256(manifest.Json),
                manifest.Sha256,
                StringComparison.Ordinal))
            throw InvalidPublished();
    }

    private static void VerifyExactWorkingTree(
        string repository,
        RepositorySetupTemplateBundle rendered)
    {
        var expectedFiles = rendered.Files
            .Select(value => value.RelativePath.Replace('\\', '/'))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in expectedFiles)
        {
            var segments = file.Split('/');
            for (var length = 1; length < segments.Length; length++)
                expectedDirectories.Add(string.Join('/', segments.Take(length)));
        }

        var actualFiles = new List<string>();
        var actualDirectories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(repository);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(repository, entry).Replace('\\', '/');
                if (string.Equals(relative, ".git", StringComparison.Ordinal))
                    continue;
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw InvalidPublished();
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    actualDirectories.Add(relative);
                    pending.Push(entry);
                }
                else
                {
                    actualFiles.Add(relative);
                }
            }
        }

        if (!actualFiles.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(expectedFiles, StringComparer.Ordinal) ||
            !actualDirectories.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(expectedDirectories.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw InvalidPublished();
    }

    private static RepositorySetupTemplateBundle RenderAndValidate(RepositoryProvisioningExecutionRequest request)
    {
        if (!string.Equals(
                RepositorySetupPlanCodec.ComputeHash(request.ConfirmedPlan),
                request.ConfirmedPlan.PlanHash,
                StringComparison.Ordinal))
            throw new RepositoryProvisioningIntegrityException(
                "The immutable repository setup plan failed hash verification.");
        return RepositorySetupTemplateBundleRenderer.Render(request.TemplateBundle, request.ConfirmedPlan);
    }

    private string RevalidateTarget(RepositoryProvisioningExecutionRequest request, bool requireAbsent)
    {
        var targetName = Path.GetFileName(Path.TrimEndingDirectorySeparator(request.ConfirmedPlan.TargetPath));
        var assessment = _pathPolicy.Assess(
            request.ApprovedWorkspaceRoot,
            targetName,
            inspectEnvironment: false);
        if (assessment.IsUnsafe || !assessment.IsAvailable ||
            !PathEquals(assessment.TargetPath, request.ConfirmedPlan.TargetPath))
            throw Failure(
                RepositoryProvisioningFailureCodes.WorkspaceUnsafe,
                "The confirmed repository target no longer satisfies the isolated workspace policy.");
        var rootAttributes = GetExistingAttributes(assessment.ApprovedWorkspaceRoot);
        if (rootAttributes is null || !rootAttributes.Value.HasFlag(FileAttributes.Directory))
            throw Failure(
                RepositoryProvisioningFailureCodes.WorkspaceUnsafe,
                "The isolated repository workspace is unavailable.");
        EnsureExistingAncestryNoReparse(assessment.ApprovedWorkspaceRoot);
        if (requireAbsent && GetExistingAttributes(assessment.TargetPath) is not null)
            throw Failure(
                RepositoryProvisioningFailureCodes.TargetAlreadyExists,
                "The confirmed repository target is no longer empty.");
        return assessment.TargetPath;
    }

    private static ProvisioningManifest BuildManifest(
        RepositoryProvisioningExecutionRequest request,
        RepositorySetupTemplateBundle rendered)
    {
        var json = RepositorySetupCanonicalJson.Serialize(new
        {
            schemaVersion = 1,
            request.ConfirmedPlan.ProjectId,
            request.ConfirmedPlan.Profile.ProfileDefinitionId,
            request.ConfirmedPlan.ProfileDescriptorRevision,
            request.ConfirmedPlan.ProfileDescriptorSha256,
            request.ConfirmedPlan.TemplateBundleSha256,
            request.ConfirmedPlan.PlanHash,
            files = rendered.Files.OrderBy(value => value.Order).Select(value => new
            {
                value.Order,
                path = value.RelativePath.Replace('\\', '/'),
                sha256 = RepositorySetupCanonicalJson.Sha256(value.Utf8Content),
                utf8ByteLength = Encoding.UTF8.GetByteCount(value.Utf8Content)
            }).ToArray()
        });
        return new ProvisioningManifest(json, RepositorySetupCanonicalJson.Sha256(json));
    }

    private async Task<string> GitOk(
        string repository,
        IReadOnlyList<string> arguments,
        DateTime commitTime,
        CancellationToken cancellationToken)
    {
        var safeArguments = new List<string>
        {
            "-c", "core.autocrlf=false",
            "-c", "core.fsmonitor=false",
            "-c", "core.untrackedCache=false",
            "-c", "core.hooksPath=" + Path.Combine(repository, ".git", "irondev-empty-hooks"),
            "-c", "credential.helper=",
            "-c", "protocol.allow=never"
        };
        if (OperatingSystem.IsWindows())
        {
            // Git for Windows otherwise retains the legacy MAX_PATH ceiling. This is a
            // process-scoped command override only; it never expands durable repository
            // authority in the exact local config verified on recovery.
            safeArguments.Add("-c");
            safeArguments.Add("core.longpaths=true");
        }
        safeArguments.AddRange(arguments);
        var result = await _git.RunAsync(repository, safeArguments, commitTime, cancellationToken);
        if (result.ExitCode != 0)
            throw Failure(
                RepositoryProvisioningFailureCodes.GitCommandFailed,
                "A controlled Git operation failed.");
        return result.StandardOutput;
    }

    private static string CommitMessage(RepositoryProvisioningExecutionRequest request)
    {
        var manifest = BuildManifest(request, RenderAndValidate(request));
        return "Initialize IronDev repository\n\n" +
               $"IronDev-Plan-Hash: {request.ConfirmedPlan.PlanHash}\n" +
               $"IronDev-Provisioning-Attempt: {request.AttemptId:D}\n" +
               $"IronDev-Manifest-Sha256: {manifest.Sha256}";
    }

    private static string MarkerJson(RepositoryProvisioningExecutionRequest request) =>
        RepositorySetupCanonicalJson.Serialize(new
        {
            schemaVersion = 1,
            attemptId = request.AttemptId,
            planHash = request.ConfirmedPlan.PlanHash,
            targetPathSha256 = RepositorySetupCanonicalJson.Sha256(request.ConfirmedPlan.TargetPath)
        });

    private static string PublicationEvidenceJson(
        RepositoryProvisioningExecutionRequest request,
        DateTime publishedAtUtc) =>
        RepositorySetupCanonicalJson.Serialize(new
        {
            schemaVersion = 1,
            attemptId = request.AttemptId,
            planHash = request.ConfirmedPlan.PlanHash,
            targetPathSha256 = RepositorySetupCanonicalJson.Sha256(request.ConfirmedPlan.TargetPath),
            publishedAtUtc
        });

    private static DateTime ReadPublishedAtUtc(
        string target,
        RepositoryProvisioningExecutionRequest request)
    {
        try
        {
            var marker = Path.Combine(target, ".git", PublicationEvidenceName);
            var attributes = GetExistingAttributes(marker);
            if (attributes is null || attributes.Value.HasFlag(FileAttributes.Directory) ||
                attributes.Value.HasFlag(FileAttributes.ReparsePoint))
                throw InvalidPublished();
            var content = File.ReadAllText(marker, RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict);
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var properties = document.RootElement.EnumerateObject().ToArray();
            var expectedNames = new[]
            {
                "schemaVersion", "attemptId", "planHash", "targetPathSha256", "publishedAtUtc"
            };
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                properties.Length != expectedNames.Length ||
                !properties.Select(value => value.Name).SequenceEqual(expectedNames, StringComparer.Ordinal) ||
                properties[0].Value.ValueKind != JsonValueKind.Number ||
                properties[0].Value.GetInt32() != 1 ||
                properties[1].Value.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(properties[1].Value.GetString(), "D", out var attemptId) ||
                attemptId != request.AttemptId ||
                properties[2].Value.ValueKind != JsonValueKind.String ||
                !string.Equals(properties[2].Value.GetString(), request.ConfirmedPlan.PlanHash, StringComparison.Ordinal) ||
                properties[3].Value.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    properties[3].Value.GetString(),
                    RepositorySetupCanonicalJson.Sha256(request.ConfirmedPlan.TargetPath),
                    StringComparison.Ordinal) ||
                properties[4].Value.ValueKind != JsonValueKind.String ||
                !properties[4].Value.TryGetDateTime(out var publishedAtUtc) ||
                publishedAtUtc.Kind != DateTimeKind.Utc ||
                !string.Equals(
                    content,
                    PublicationEvidenceJson(request, publishedAtUtc) + "\n",
                    StringComparison.Ordinal))
                throw InvalidPublished();
            return publishedAtUtc;
        }
        catch (RepositoryProvisioningExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                                           FormatException or InvalidOperationException)
        {
            throw InvalidPublished(exception);
        }
    }

    private static bool HasExactPublishedMarker(
        string target,
        RepositoryProvisioningExecutionRequest request) =>
        InspectPublishedMarker(target, request) == PublishedMarkerInspectionState.Exact;

    private static PublishedMarkerInspectionState InspectPublishedMarker(
        string target,
        RepositoryProvisioningExecutionRequest request)
    {
        try
        {
            var targetAttributes = File.GetAttributes(target);
            if (!targetAttributes.HasFlag(FileAttributes.Directory) ||
                targetAttributes.HasFlag(FileAttributes.ReparsePoint))
                return PublishedMarkerInspectionState.AbsentOrForeign;
            var gitDirectory = Path.Combine(target, ".git");
            var gitAttributes = File.GetAttributes(gitDirectory);
            if (!gitAttributes.HasFlag(FileAttributes.Directory) ||
                gitAttributes.HasFlag(FileAttributes.ReparsePoint))
                return PublishedMarkerInspectionState.AbsentOrForeign;
            var marker = Path.Combine(gitDirectory, MarkerName);
            var markerAttributes = File.GetAttributes(marker);
            if (markerAttributes.HasFlag(FileAttributes.Directory) ||
                markerAttributes.HasFlag(FileAttributes.ReparsePoint))
                return PublishedMarkerInspectionState.AbsentOrForeign;
            return string.Equals(
                File.ReadAllText(marker, RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict),
                MarkerJson(request) + "\n",
                StringComparison.Ordinal)
                ? PublishedMarkerInspectionState.Exact
                : PublishedMarkerInspectionState.AbsentOrForeign;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return PublishedMarkerInspectionState.AbsentOrForeign;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException)
        {
            return PublishedMarkerInspectionState.VerificationUnavailable;
        }
    }

    private static void TryCleanupOwnedStaging(
        string root,
        string staging,
        RepositoryProvisioningExecutionRequest request)
    {
        try
        {
            if (!Directory.Exists(staging))
                return;
            EnsureDirectChild(root, staging);
            EnsureNoReparsePoints(root, staging);
            var rootMarker = Path.Combine(staging, MarkerName);
            var gitMarker = Path.Combine(staging, ".git", MarkerName);
            var marker = File.Exists(gitMarker) ? gitMarker : rootMarker;
            if (!File.Exists(marker) || File.GetAttributes(marker).HasFlag(FileAttributes.ReparsePoint) ||
                !string.Equals(
                    File.ReadAllText(marker, RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict),
                    MarkerJson(request) + "\n",
                    StringComparison.Ordinal))
                return;
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            foreach (var directory in Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories)
                         .OrderByDescending(value => value.Length))
                File.SetAttributes(directory, FileAttributes.Directory);
            File.SetAttributes(staging, FileAttributes.Directory);
            Directory.Delete(staging, recursive: true);
        }
        catch
        {
            // A cleanup failure is deliberately non-destructive. The failed attempt remains
            // durable and an unknown or changed directory is never removed.
        }
    }

    private static bool HasExactTrailer(string commitBody, string name, string expected) =>
        commitBody.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Count(line => string.Equals(line, $"{name}: {expected}", StringComparison.Ordinal)) == 1;

    private static void WriteNewUtf8File(string path, string content)
    {
        var bytes = RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict.GetBytes(content);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static string ResolveSafeChild(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') || relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new RepositoryProvisioningIntegrityException("A rendered template path is unsafe.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new RepositoryProvisioningIntegrityException("A rendered template path escaped staging.");
        return full;
    }

    private static void EnsureDirectChild(string root, string child)
    {
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(child) ?? string.Empty),
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase))
            throw new RepositoryProvisioningIntegrityException(
                "Repository staging must be a direct sibling of the confirmed target.");
    }

    private static void EnsureNoReparsePoints(string root, string target)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (!PathEquals(normalizedRoot, normalizedTarget) &&
            !normalizedTarget.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new RepositoryProvisioningIntegrityException("A repository path escaped its approved root.");

        var current = normalizedTarget;
        while (true)
        {
            var attributes = GetExistingAttributes(current);
            if (attributes is not null && attributes.Value.HasFlag(FileAttributes.ReparsePoint))
                throw Failure(
                    RepositoryProvisioningFailureCodes.WorkspaceUnsafe,
                    "The isolated repository path contains a reparse point.");
            if (PathEquals(current, normalizedRoot))
                break;
            current = Path.GetDirectoryName(current) ?? throw new RepositoryProvisioningIntegrityException(
                "The repository path has no approved ancestor.");
        }

        var targetAttributes = GetExistingAttributes(normalizedTarget);
        if (targetAttributes is null || !targetAttributes.Value.HasFlag(FileAttributes.Directory))
            return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(normalizedTarget, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                throw Failure(
                    RepositoryProvisioningFailureCodes.WorkspaceUnsafe,
                    "The isolated repository path contains a reparse point.");
        }
    }

    private static void EnsureExistingAncestryNoReparse(string path)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        while (!string.IsNullOrWhiteSpace(current))
        {
            var attributes = GetExistingAttributes(current);
            if (attributes is not null && attributes.Value.HasFlag(FileAttributes.ReparsePoint))
                throw Failure(
                    RepositoryProvisioningFailureCodes.WorkspaceUnsafe,
                    "The isolated repository root is, or is below, a reparse point.");
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || PathEquals(parent, current))
                break;
            current = parent;
        }
    }

    private static void ValidateRequest(RepositoryProvisioningExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AttemptId == Guid.Empty || string.IsNullOrWhiteSpace(request.ApprovedWorkspaceRoot) ||
            request.ConfirmedPlan.SchemaVersion != 1 ||
            request.ConfirmedPlan.State != RepositorySetupPreviewStates.ReadyForConfirmation ||
            !request.ConfirmedPlan.InitializeGit || request.ConfirmedPlan.DefaultBranch != DefaultBranch)
            throw new RepositoryProvisioningIntegrityException(
                "The immutable provisioning request is incomplete or unsupported.");
    }

    private static bool PathEquals(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

    private static FileAttributes? GetExistingAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool IsLowerHexObjectId(string value) =>
        value.Length == 40 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static RepositoryProvisioningExecutionException Failure(
        string reasonCode,
        string message,
        Exception? inner = null) => new(reasonCode, message, inner);

    private static RepositoryProvisioningExecutionException InvalidPublished(Exception? inner = null) =>
        Failure(
            RepositoryProvisioningFailureCodes.PublishedRepositoryInvalid,
            "The published repository does not match the exact provisioning attempt.",
            inner);

    private sealed record ProvisioningManifest(string Json, string Sha256);

    private enum PublishedMarkerInspectionState
    {
        AbsentOrForeign,
        Exact,
        VerificationUnavailable
    }
}
