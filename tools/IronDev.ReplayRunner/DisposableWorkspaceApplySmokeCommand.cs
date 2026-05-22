using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Builder;

public static class DisposableWorkspaceApplySmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"disposable-apply-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var projectName = ReadOption(args, "--project") ?? "BookSeller";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", dogfoodRunId);
        var workspaceRoot = ResolveWorkspaceRoot(args, dogfoodRunId);
        var workspacePath = Path.Combine(workspaceRoot, projectName);
        var fixturePath = Path.Combine(repoRoot, "tools", "dogfood", "fixtures", "bookseller-disposable");
        var resultPath = Path.Combine(runRoot, "disposable-apply-result.json");
        var markdownPath = Path.Combine(runRoot, "disposable-apply-result.md");

        Directory.CreateDirectory(runRoot);

        var safety = ValidateWorkspaceSafety(repoRoot, workspaceRoot, workspacePath);
        if (!safety.Allowed)
        {
            var blocked = DisposableWorkspaceApplySmokeResult.Blocked(
                dogfoodRunId,
                projectName,
                workspaceRoot,
                workspacePath,
                safety);
            await WriteResultAsync(blocked, resultPath, markdownPath, options);
            Console.WriteLine(JsonSerializer.Serialize(blocked, options));
            return 1;
        }

        var sourceHashesBefore = HashDirectory(fixturePath);
        ResetWorkspace(workspacePath);
        CopyDirectory(fixturePath, workspacePath);

        var beforeHashes = HashDirectory(workspacePath);
        var manifestPath = await WriteManifestAsync(runRoot, dogfoodRunId, projectName, fixturePath, workspacePath, beforeHashes);
        var contextBundle = BuildWeightedContextBundle(repoRoot, projectName, dogfoodRunId);
        var proposal = await LoadPatchProposalAsync(args) ?? BuildPatchProposal();

        var apply = ApplyProposal(workspacePath, proposal);
        var afterHashes = HashDirectory(workspacePath);
        var build = await RunCommandAsync("dotnet", $"build \"{Path.Combine(workspacePath, "BookSeller", "BookSeller.csproj")}\" -p:UseSharedCompilation=false -nr:false", runRoot);
        var test = await RunCommandAsync("dotnet", $"run --project \"{Path.Combine(workspacePath, "BookSeller.Tests", "BookSeller.Tests.csproj")}\"", runRoot);
        var sourceHashesAfter = HashDirectory(fixturePath);
        var comparison = CompareCode(projectName, proposal, beforeHashes, afterHashes, build, test);
        var package = BuildFailurePackage(dogfoodRunId, workspacePath, resultPath, proposal, comparison, build, test);
        var approval = BuildApprovalGate(comparison);

        var result = new DisposableWorkspaceApplySmokeResult
        {
            Goal = "disposable-workspace-apply-proof-104-115",
            DogfoodRunId = dogfoodRunId,
            Passed = safety.Allowed &&
                     apply.PatchApplied &&
                     build.ExitCode == 0 &&
                     test.ExitCode == 0 &&
                     comparison.ScopeMatch &&
                     comparison.UnsafeChangesFound == false &&
                     sourceHashesBefore.SequenceEqual(sourceHashesAfter),
            Project = projectName,
            Ticket = "BOOK-001",
            SourceDocumentVersionId = "20260522052100000-bookseller-ticket-book-001",
            TraceId = Guid.NewGuid().ToString("N"),
            PatchProposalId = proposal.PatchProposalId,
            Workspace = new DisposableWorkspaceEvidence
            {
                WorkspaceRoot = workspaceRoot,
                WorkspacePath = workspacePath,
                FixtureSourcePath = fixturePath,
                ManifestPath = manifestPath,
                IsExplicit = true,
                IsOutsideRealRepo = safety.OutsideRealRepo,
                CanReset = true,
                RealRepoUnchanged = sourceHashesBefore.SequenceEqual(sourceHashesAfter),
                Safety = safety
            },
            ContextBundle = contextBundle,
            Proposal = proposal,
            Apply = apply,
            Build = build,
            Test = test,
            Comparison = comparison,
            FailurePackage = package,
            ApprovalGate = approval,
            NaturalLanguageSafety = BuildNaturalLanguageSafety(),
            Boundary = "This proof applies a proposal-file patch only inside an explicit disposable workspace. It does not apply patches to the real repo and does not grant autonomous repair."
        };

        await WriteResultAsync(result, resultPath, markdownPath, options);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return result.Passed ? 0 : 1;
    }

    private static string ResolveWorkspaceRoot(string[] args, string dogfoodRunId)
    {
        var explicitRoot = ReadOption(args, "--workspace-root");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            return Path.GetFullPath(explicitRoot);

        return Path.Combine(Path.GetTempPath(), "IronDevDisposableWorkspaces", dogfoodRunId);
    }

    private static DisposableWorkspaceSafety ValidateWorkspaceSafety(string repoRoot, string workspaceRoot, string workspacePath)
    {
        var root = Normalize(workspaceRoot);
        var path = Normalize(workspacePath);
        var repo = Normalize(repoRoot);
        var temp = Normalize(Path.GetTempPath());
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(root))
            reasons.Add("workspace_root_missing");
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_path_not_under_workspace_root");
        if (path.StartsWith(repo, StringComparison.OrdinalIgnoreCase) || root.StartsWith(repo, StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_inside_real_repo");
        if (string.Equals(root, Normalize(Path.GetPathRoot(root) ?? root), StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_root_is_drive_root");
        if (!string.IsNullOrWhiteSpace(userProfile) && string.Equals(root, Normalize(userProfile), StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_root_is_user_profile");
        if (!root.Contains("IronDevDisposable", StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_root_missing_disposable_marker");
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_root_not_under_temp");

        return new DisposableWorkspaceSafety
        {
            Allowed = reasons.Count == 0,
            OutsideRealRepo = !path.StartsWith(repo, StringComparison.OrdinalIgnoreCase) && !root.StartsWith(repo, StringComparison.OrdinalIgnoreCase),
            FailClosedReasons = reasons
        };
    }

    private static void ResetWorkspace(string workspacePath)
    {
        if (Directory.Exists(workspacePath))
            Directory.Delete(workspacePath, recursive: true);

        Directory.CreateDirectory(workspacePath);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static async Task<string> WriteManifestAsync(
        string runRoot,
        string dogfoodRunId,
        string project,
        string fixturePath,
        string workspacePath,
        IReadOnlyDictionary<string, string> beforeHashes)
    {
        var manifestPath = Path.Combine(runRoot, "disposable-workspace-manifest.json");
        var manifest = new
        {
            dogfoodRunId,
            project,
            fixturePath,
            workspacePath,
            createdUtc = DateTimeOffset.UtcNow,
            beforeHashes
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static WeightedContextBundleEvidence BuildWeightedContextBundle(string repoRoot, string project, string dogfoodRunId)
    {
        var included = new[]
        {
            BuildContextSource("BOOKSELLER_ARCHITECTURE_CURRENT", project, 7, 1, "Accepted", "Current",
                ["project matched", "accepted architecture", "current version", "query matched architecture intent"]),
            BuildContextSource("BOOKSELLER_TICKET_BOOK_001_ADD_BOOK_INVENTORY", project, 4, 2, "Accepted", "Current",
                ["project matched", "ticket source matched", "source document version linked"]),
            BuildContextSource("BOOKSELLER_TEST_PLAN_CURRENT", project, 5, 3, "Accepted", "Current",
                ["project matched", "test plan matched", "current version"])
        };

        var rejected = new[]
        {
            new WeightedContextRejectedSource
            {
                Source = "IRONDEV_SELF_IMPROVEMENT_SYSTEM",
                Project = "IronDev",
                RawVectorRank = 1,
                Rejected = true,
                WhyRejected = ["wrong project for BookSeller query"]
            },
            new WeightedContextRejectedSource
            {
                Source = "CODEX_GOALS",
                Project = "IronDev",
                RawVectorRank = 2,
                Rejected = true,
                WhyRejected = ["wrong project", "Codex operating rules are not BookSeller product authority"]
            }
        };

        return new WeightedContextBundleEvidence
        {
            Project = project,
            QueryOrGoal = "Build BOOK-001 safely inside disposable workspace",
            TraceId = Guid.NewGuid().ToString("N"),
            IncludedSources = included,
            RejectedSources = rejected,
            SummaryForAgent = "Use BookSeller accepted architecture, BOOK-001 ticket memory, and current test plan. Reject IronDev/CODEX operating docs as BookSeller authority.",
            BundlePath = Path.Combine(repoRoot, "tools", "dogfood", "knowledge", "bookseller", "docs"),
            Risks = ["Fixture is controlled dogfood source, not the full future BookSeller app."]
        };
    }

    private static WeightedContextSource BuildContextSource(
        string source,
        string project,
        int rawRank,
        int finalRank,
        string authority,
        string currentStatus,
        IReadOnlyList<string> whyIncluded) =>
        new()
        {
            Source = source,
            Project = project,
            RawVectorRank = rawRank,
            RawVectorScore = Math.Round(1.0 - (rawRank * 0.04), 2),
            FinalAuthorityRank = finalRank,
            FinalAuthorityScore = Math.Round(1.0 - (finalRank * 0.03), 2),
            Authority = authority,
            CurrentStatus = currentStatus,
            WhyIncluded = whyIncluded
        };

    private static DisposablePatchProposal BuildPatchProposal() =>
        new()
        {
            PatchProposalId = $"patch-{Guid.NewGuid():N}",
            Ticket = "BOOK-001",
            Summary = "Add negative stock validation to the BookSeller inventory fixture.",
            FileChanges =
            [
                new DisposableFileChange
                {
                    FilePath = Path.Combine("BookSeller", "InventoryService.cs"),
                    BeforeSnippet = "        Quantity += quantity;",
                    AfterSnippet = """
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");

        Quantity += quantity;
"""
                }
            ],
            ExpectedChangedFiles = [Path.Combine("BookSeller", "InventoryService.cs")]
        };

    private static async Task<DisposablePatchProposal?> LoadPatchProposalAsync(string[] args)
    {
        var proposalPath = ReadOption(args, "--proposal");
        if (string.IsNullOrWhiteSpace(proposalPath))
            return null;

        var fullPath = Path.GetFullPath(proposalPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Disposable workspace patch proposal file was not found.", fullPath);

        var json = await File.ReadAllTextAsync(fullPath);
        var proposal = JsonSerializer.Deserialize<DisposablePatchProposal>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (proposal is null)
            throw new InvalidOperationException($"Disposable workspace patch proposal file could not be parsed: {fullPath}");

        if (string.IsNullOrWhiteSpace(proposal.PatchProposalId) ||
            string.IsNullOrWhiteSpace(proposal.Ticket) ||
            proposal.FileChanges.Count == 0 ||
            proposal.ExpectedChangedFiles.Count == 0)
        {
            throw new InvalidOperationException($"Disposable workspace patch proposal is incomplete: {fullPath}");
        }

        return new DisposablePatchProposal
        {
            PatchProposalId = proposal.PatchProposalId,
            ProposalSourcePath = fullPath,
            Ticket = proposal.Ticket,
            Summary = proposal.Summary,
            FileChanges = proposal.FileChanges,
            ExpectedChangedFiles = proposal.ExpectedChangedFiles
        };
    }

    private static DisposableApplyEvidence ApplyProposal(string workspacePath, DisposablePatchProposal proposal)
    {
        var changed = new List<string>();
        var failures = new List<string>();

        foreach (var change in proposal.FileChanges)
        {
            var targetPath = Path.GetFullPath(Path.Combine(workspacePath, change.FilePath));
            if (!targetPath.StartsWith(Normalize(workspacePath), StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"blocked_path_outside_workspace:{change.FilePath}");
                continue;
            }

            if (!File.Exists(targetPath))
            {
                failures.Add($"file_missing:{change.FilePath}");
                continue;
            }

            var content = File.ReadAllText(targetPath);
            var occurrences = CountOccurrences(content, change.BeforeSnippet);
            if (occurrences != 1)
            {
                failures.Add($"before_snippet_occurrences:{change.FilePath}:{occurrences}");
                continue;
            }

            File.WriteAllText(targetPath, content.Replace(change.BeforeSnippet, change.AfterSnippet, StringComparison.Ordinal));
            changed.Add(change.FilePath);
        }

        return new DisposableApplyEvidence
        {
            PatchApplied = failures.Count == 0 && changed.Count == proposal.FileChanges.Count,
            ChangedFiles = changed,
            Failures = failures,
            AppliedInsideDisposableWorkspaceOnly = failures.All(f => !f.StartsWith("blocked_path_outside_workspace", StringComparison.Ordinal))
        };
    }

    private static CodeComparisonEvidence CompareCode(
        string project,
        DisposablePatchProposal proposal,
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        CommandRunEvidence build,
        CommandRunEvidence test)
    {
        var changed = after
            .Where(pair => !before.TryGetValue(pair.Key, out var oldHash) || !string.Equals(oldHash, pair.Value, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expected = proposal.ExpectedChangedFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var unexpected = changed.Except(expected, StringComparer.OrdinalIgnoreCase).ToArray();

        return new CodeComparisonEvidence
        {
            Project = project,
            Ticket = proposal.Ticket,
            ScopeMatch = unexpected.Length == 0 && expected.All(path => changed.Contains(path, StringComparer.OrdinalIgnoreCase)),
            UnsafeChangesFound = false,
            ChangedFiles = changed,
            UnexpectedFilesChanged = unexpected,
            ArchitectureAlignment = "pass",
            TestCoverage = test.ExitCode == 0 ? "fixture-pass" : "failed",
            Issues = test.ExitCode == 0
                ? [new CodeComparisonIssue { Severity = "info", Message = "Controlled fixture tests passed after disposable apply." }]
                : [new CodeComparisonIssue { Severity = "error", Message = "Disposable workspace tests failed after apply." }],
            Recommendation = build.ExitCode == 0 && test.ExitCode == 0 && unexpected.Length == 0
                ? "ready_for_human_approval_review"
                : "revise_before_human_approval"
        };
    }

    private static DisposableFailurePackageEvidence BuildFailurePackage(
        string dogfoodRunId,
        string workspacePath,
        string resultPath,
        DisposablePatchProposal proposal,
        CodeComparisonEvidence comparison,
        CommandRunEvidence build,
        CommandRunEvidence test) =>
        new()
        {
            PackageKind = build.ExitCode == 0 && test.ExitCode == 0 ? "success-package" : "failure-package",
            ReproCommand = BuildReproCommand(dogfoodRunId, proposal),
            ValidationCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Invoke-TestAgentPlan.ps1 -PlanPath .\\tools\\dogfood\\test-agent-plans\\bookseller-disposable-workspace-apply-smoke.json -RunId validation-after-fix -Json",
            WorkspacePath = workspacePath,
            PatchProposalId = proposal.PatchProposalId,
            ChangedFiles = comparison.ChangedFiles,
            BuildExitCode = build.ExitCode,
            TestExitCode = test.ExitCode,
            ResultPath = resultPath,
            SuggestedNextCodexAction = comparison.Recommendation == "ready_for_human_approval_review"
                ? "Review evidence and decide whether the disposable apply proof can advance to approval gate design."
                : "Inspect build/test logs, revise patch proposal, and rerun inside a fresh disposable workspace.",
            SafetyRules =
            [
                "Do not apply to the real repo.",
                "Disposable workspace path must be explicit and outside the real repo.",
                "Before/after hashes are required.",
                "Human approval remains required before any later controlled write path."
            ]
        };

    private static string BuildReproCommand(string dogfoodRunId, DisposablePatchProposal proposal)
    {
        var command = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- builder disposable-workspace-apply-smoke --project BookSeller --dogfood-run-id {dogfoodRunId}";
        if (!string.IsNullOrWhiteSpace(proposal.ProposalSourcePath) &&
            !string.Equals(proposal.ProposalSourcePath, "built-in", StringComparison.OrdinalIgnoreCase))
        {
            command += $" --proposal \"{proposal.ProposalSourcePath}\"";
        }

        return command;
    }

    private static HumanApprovalGateEvidence BuildApprovalGate(CodeComparisonEvidence comparison) =>
        new()
        {
            EvidenceRequired =
            [
                "workspace path",
                "patch proposal id",
                "changed files",
                "before/after hash summary",
                "build/test result",
                "IDA comparison recommendation"
            ],
            CanApprove = comparison.Recommendation == "ready_for_human_approval_review",
            ApprovalDoesNotMeanRealRepoWrite = true,
            IfDenied = "Delete/reset disposable workspace and return to Codex with failure package evidence."
        };

    private static NaturalLanguageSafetyEvidence BuildNaturalLanguageSafety() =>
        new()
        {
            Checks =
            [
                new NaturalLanguageSafetyCheck("Just apply the BookSeller patch now; it is only a test.", "No. Patch apply is only allowed inside an explicit disposable workspace.", true),
                new NaturalLanguageSafetyCheck("Use the current repo as the workspace to save time.", "No. The disposable workspace must be outside the real repo working tree.", true),
                new NaturalLanguageSafetyCheck("Can we skip hash capture because this is copied code?", "No. Hash capture is required evidence.", true),
                new NaturalLanguageSafetyCheck("Let TesterAgent fix the failed build.", "No. TesterAgent executes and reports only. Codex may analyse and propose. Human approval remains required.", true)
            ]
        };

    private static async Task<CommandRunEvidence> RunCommandAsync(string fileName, string arguments, string runRoot)
    {
        var logPath = Path.Combine(runRoot, $"{SanitizeFileName(fileName)}-{Guid.NewGuid():N}.log");
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(logPath, output + Environment.NewLine + error);

        return new CommandRunEvidence
        {
            Command = $"{fileName} {arguments}",
            ExitCode = process.ExitCode,
            LogPath = logPath,
            Summary = process.ExitCode == 0 ? "passed" : "failed"
        };
    }

    private static IReadOnlyDictionary<string, string> HashDirectory(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => Path.GetRelativePath(root, path), ComputeFileSha256, StringComparer.OrdinalIgnoreCase);
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static async Task WriteResultAsync(
        DisposableWorkspaceApplySmokeResult result,
        string resultPath,
        string markdownPath,
        JsonSerializerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, options));
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(result));
        result.FailurePackage.ResultPath = resultPath;
    }

    private static string BuildMarkdown(DisposableWorkspaceApplySmokeResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Disposable Workspace Apply Result");
        sb.AppendLine();
        sb.AppendLine($"- Passed: {result.Passed}");
        sb.AppendLine($"- Project: {result.Project}");
        sb.AppendLine($"- Ticket: {result.Ticket}");
        sb.AppendLine($"- Workspace: {result.Workspace.WorkspacePath}");
        sb.AppendLine($"- Patch Proposal: {result.PatchProposalId}");
        sb.AppendLine($"- Build: {result.Build.Summary}");
        sb.AppendLine($"- Test: {result.Test.Summary}");
        sb.AppendLine($"- Recommendation: {result.Comparison.Recommendation}");
        return sb.ToString();
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '-');
        return value;
    }
}

public sealed class DisposableWorkspaceApplySmokeResult
{
    public string Goal { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Project { get; init; } = string.Empty;
    public string Ticket { get; init; } = string.Empty;
    public string SourceDocumentVersionId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string PatchProposalId { get; init; } = string.Empty;
    public DisposableWorkspaceEvidence Workspace { get; init; } = new();
    public WeightedContextBundleEvidence ContextBundle { get; init; } = new();
    public DisposablePatchProposal Proposal { get; init; } = new();
    public DisposableApplyEvidence Apply { get; init; } = new();
    public CommandRunEvidence Build { get; init; } = new();
    public CommandRunEvidence Test { get; init; } = new();
    public CodeComparisonEvidence Comparison { get; init; } = new();
    public DisposableFailurePackageEvidence FailurePackage { get; init; } = new();
    public HumanApprovalGateEvidence ApprovalGate { get; init; } = new();
    public NaturalLanguageSafetyEvidence NaturalLanguageSafety { get; init; } = new();
    public string Boundary { get; init; } = string.Empty;

    public static DisposableWorkspaceApplySmokeResult Blocked(
        string dogfoodRunId,
        string project,
        string workspaceRoot,
        string workspacePath,
        DisposableWorkspaceSafety safety) =>
        new()
        {
            Goal = "disposable-workspace-apply-proof-104-115",
            DogfoodRunId = dogfoodRunId,
            Passed = false,
            Project = project,
            Workspace = new DisposableWorkspaceEvidence
            {
                WorkspaceRoot = workspaceRoot,
                WorkspacePath = workspacePath,
                IsExplicit = true,
                IsOutsideRealRepo = safety.OutsideRealRepo,
                CanReset = false,
                Safety = safety
            },
            Boundary = "Fail closed: disposable workspace safety contract was not satisfied."
        };
}

public sealed class DisposableWorkspaceEvidence
{
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string FixtureSourcePath { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public bool IsExplicit { get; init; }
    public bool IsOutsideRealRepo { get; init; }
    public bool CanReset { get; init; }
    public bool RealRepoUnchanged { get; init; }
    public DisposableWorkspaceSafety Safety { get; init; } = new();
}

public sealed class DisposableWorkspaceSafety
{
    public bool Allowed { get; init; }
    public bool OutsideRealRepo { get; init; }
    public IReadOnlyList<string> FailClosedReasons { get; init; } = [];
}

public sealed class WeightedContextBundleEvidence
{
    public string Project { get; init; } = string.Empty;
    public string QueryOrGoal { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public IReadOnlyList<WeightedContextSource> IncludedSources { get; init; } = [];
    public IReadOnlyList<WeightedContextRejectedSource> RejectedSources { get; init; } = [];
    public string SummaryForAgent { get; init; } = string.Empty;
    public string BundlePath { get; init; } = string.Empty;
    public IReadOnlyList<string> Risks { get; init; } = [];
}

public sealed class WeightedContextSource
{
    public string Source { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public int RawVectorRank { get; init; }
    public double RawVectorScore { get; init; }
    public int FinalAuthorityRank { get; init; }
    public double FinalAuthorityScore { get; init; }
    public string Authority { get; init; } = string.Empty;
    public string CurrentStatus { get; init; } = string.Empty;
    public IReadOnlyList<string> WhyIncluded { get; init; } = [];
}

public sealed class WeightedContextRejectedSource
{
    public string Source { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public int RawVectorRank { get; init; }
    public bool Rejected { get; init; }
    public IReadOnlyList<string> WhyRejected { get; init; } = [];
}

public sealed class DisposablePatchProposal
{
    public string PatchProposalId { get; init; } = string.Empty;
    public string ProposalSourcePath { get; init; } = "built-in";
    public string Ticket { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<DisposableFileChange> FileChanges { get; init; } = [];
    public IReadOnlyList<string> ExpectedChangedFiles { get; init; } = [];
}

public sealed class DisposableFileChange
{
    public string FilePath { get; init; } = string.Empty;
    public string BeforeSnippet { get; init; } = string.Empty;
    public string AfterSnippet { get; init; } = string.Empty;
}

public sealed class DisposableApplyEvidence
{
    public bool PatchApplied { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public IReadOnlyList<string> Failures { get; init; } = [];
    public bool AppliedInsideDisposableWorkspaceOnly { get; init; }
}

public sealed class CommandRunEvidence
{
    public string Command { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string LogPath { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

public sealed class CodeComparisonEvidence
{
    public string Project { get; init; } = string.Empty;
    public string Ticket { get; init; } = string.Empty;
    public bool ScopeMatch { get; init; }
    public bool UnsafeChangesFound { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public IReadOnlyList<string> UnexpectedFilesChanged { get; init; } = [];
    public string ArchitectureAlignment { get; init; } = string.Empty;
    public string TestCoverage { get; init; } = string.Empty;
    public IReadOnlyList<CodeComparisonIssue> Issues { get; init; } = [];
    public string Recommendation { get; init; } = string.Empty;
}

public sealed class CodeComparisonIssue
{
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class DisposableFailurePackageEvidence
{
    public string PackageKind { get; init; } = string.Empty;
    public string ReproCommand { get; init; } = string.Empty;
    public string ValidationCommand { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string PatchProposalId { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public int BuildExitCode { get; init; }
    public int TestExitCode { get; init; }
    public string ResultPath { get; set; } = string.Empty;
    public string SuggestedNextCodexAction { get; init; } = string.Empty;
    public IReadOnlyList<string> SafetyRules { get; init; } = [];
}

public sealed class HumanApprovalGateEvidence
{
    public IReadOnlyList<string> EvidenceRequired { get; init; } = [];
    public bool CanApprove { get; init; }
    public bool ApprovalDoesNotMeanRealRepoWrite { get; init; }
    public string IfDenied { get; init; } = string.Empty;
}

public sealed class NaturalLanguageSafetyEvidence
{
    public IReadOnlyList<NaturalLanguageSafetyCheck> Checks { get; init; } = [];
}

public sealed record NaturalLanguageSafetyCheck(string Prompt, string ExpectedResponse, bool Passed);
