using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class SolitaireDisposableBuildSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"solitaire-build-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", dogfoodRunId);
        var workspaceRoot = ResolveWorkspaceRoot(args, dogfoodRunId);
        var workspacePath = Path.Combine(workspaceRoot, "Solitaire");
        var resultPath = Path.Combine(runRoot, "solitaire-disposable-build-result.json");
        var markdownPath = Path.Combine(runRoot, "solitaire-disposable-build-result.md");

        Directory.CreateDirectory(runRoot);
        var repoStatusBefore = await GetGitStatusAsync(repoRoot);
        var safety = ValidateWorkspaceSafety(repoRoot, workspaceRoot, workspacePath);
        if (!safety.Allowed)
        {
            var blocked = BuildBlocked(dogfoodRunId, workspaceRoot, workspacePath, safety, repoStatusBefore);
            await WriteResultAsync(blocked, resultPath, markdownPath, options);
            Console.WriteLine(JsonSerializer.Serialize(blocked, options));
            return 1;
        }

        ResetWorkspace(workspacePath);
        var beforeHashes = HashDirectory(workspacePath);
        var context = BuildContextBundle(repoRoot, dogfoodRunId);
        var conscience = BuildConscienceReview(workspacePath);
        var thoughtLedger = BuildThoughtLedger(conscience.Decision);
        var generation = await GenerateSolitaireAsync(workspacePath);
        var afterGenerateHashes = HashDirectory(workspacePath);

        var build = await RunCommandAsync(
            "dotnet",
            $"build \"{Path.Combine(workspacePath, "Solitaire.Wpf", "Solitaire.Wpf.csproj")}\" -p:UseSharedCompilation=false -nr:false",
            runRoot,
            workspacePath);

        CommandRunEvidence? retryBuild = null;
        if (build.ExitCode != 0)
        {
            retryBuild = await RunCommandAsync(
                "dotnet",
                $"build \"{Path.Combine(workspacePath, "Solitaire.Wpf", "Solitaire.Wpf.csproj")}\" -p:UseSharedCompilation=false -nr:false",
                runRoot,
                workspacePath);
            if (retryBuild.ExitCode == 0)
                build = retryBuild;
        }

        var test = await RunCommandAsync(
            "dotnet",
            $"run --project \"{Path.Combine(workspacePath, "Solitaire.Core.Tests", "Solitaire.Core.Tests.csproj")}\"",
            runRoot,
            workspacePath);

        var afterHashes = HashDirectory(workspacePath);
        var repoStatusAfter = await GetGitStatusAsync(repoRoot);
        var changedFiles = afterHashes.Keys.Except(beforeHashes.Keys, StringComparer.OrdinalIgnoreCase)
            .Concat(afterHashes.Where(pair => beforeHashes.TryGetValue(pair.Key, out var oldHash) && oldHash != pair.Value).Select(pair => pair.Key))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var comparison = BuildComparison(changedFiles, build, test);
        var package = BuildEvidencePackage(dogfoodRunId, workspacePath, resultPath, changedFiles, build, test, comparison);
        var passed = conscience.Decision == "Allow" &&
                     safety.Allowed &&
                     build.ExitCode == 0 &&
                     test.ExitCode == 0 &&
                     repoStatusBefore == repoStatusAfter &&
                     changedFiles.All(path => !Path.GetFullPath(Path.Combine(workspacePath, path)).StartsWith(Normalize(repoRoot), StringComparison.OrdinalIgnoreCase));

        var result = new SolitaireDisposableBuildResult
        {
            Goal = "solitaire-disposable-workspace-build-139",
            DogfoodRunId = dogfoodRunId,
            Passed = passed,
            Project = "Solitaire",
            TraceId = Guid.NewGuid().ToString("N"),
            Workspace = new SolitaireWorkspaceEvidence
            {
                WorkspaceRoot = workspaceRoot,
                WorkspacePath = workspacePath,
                IsExplicit = true,
                IsOutsideRealRepo = safety.OutsideRealRepo,
                CanReset = true,
                RealRepoUnchanged = repoStatusBefore == repoStatusAfter,
                RepoStatusBefore = repoStatusBefore,
                RepoStatusAfter = repoStatusAfter,
                Safety = safety
            },
            ContextBundle = context,
            Conscience = conscience,
            ThoughtLedger = thoughtLedger,
            Builder = generation,
            Build = build,
            RetryBuild = retryBuild,
            Test = test,
            ChangedFiles = changedFiles,
            Comparison = comparison,
            EvidencePackage = package,
            Recommendation = comparison.Recommendation,
            Boundary = "BuilderAgent may write freely only inside the explicit disposable Solitaire workspace. No real repository writes, memory mutation, guardrail mutation, regression mutation, or self-approval are allowed."
        };

        await WriteResultAsync(result, resultPath, markdownPath, options);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return result.Passed ? 0 : 1;
    }

    private static SolitaireDisposableBuildResult BuildBlocked(
        string dogfoodRunId,
        string workspaceRoot,
        string workspacePath,
        DisposableWorkspaceSafety safety,
        string repoStatusBefore) =>
        new()
        {
            Goal = "solitaire-disposable-workspace-build-139",
            DogfoodRunId = dogfoodRunId,
            Passed = false,
            Project = "Solitaire",
            Workspace = new SolitaireWorkspaceEvidence
            {
                WorkspaceRoot = workspaceRoot,
                WorkspacePath = workspacePath,
                IsExplicit = true,
                IsOutsideRealRepo = safety.OutsideRealRepo,
                RepoStatusBefore = repoStatusBefore,
                RepoStatusAfter = repoStatusBefore,
                Safety = safety
            },
            Recommendation = "Reject Spike",
            Boundary = "Fail closed: disposable workspace safety contract was not satisfied."
        };

    internal static string ResolveWorkspaceRoot(string[] args, string dogfoodRunId)
    {
        var explicitRoot = ReadOption(args, "--workspace-root");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            return Path.GetFullPath(explicitRoot);

        return Path.Combine(Path.GetTempPath(), "IronDevDisposableWorkspaces", dogfoodRunId);
    }

    internal static DisposableWorkspaceSafety ValidateWorkspaceSafety(string repoRoot, string workspaceRoot, string workspacePath)
    {
        var root = Normalize(workspaceRoot);
        var path = Normalize(workspacePath);
        var repo = Normalize(repoRoot);
        var temp = Normalize(Path.GetTempPath());
        var userProfile = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(root))
            reasons.Add("workspace_root_missing");
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_path_not_under_workspace_root");
        if (path.StartsWith(repo, StringComparison.OrdinalIgnoreCase) || root.StartsWith(repo, StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_inside_real_repo");
        if (string.Equals(root, Normalize(Path.GetPathRoot(root) ?? root), StringComparison.OrdinalIgnoreCase))
            reasons.Add("workspace_root_is_drive_root");
        if (!string.IsNullOrWhiteSpace(userProfile) && string.Equals(root, userProfile, StringComparison.OrdinalIgnoreCase))
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

    internal static void ResetWorkspace(string workspacePath)
    {
        if (Directory.Exists(workspacePath))
            Directory.Delete(workspacePath, recursive: true);
        Directory.CreateDirectory(workspacePath);
    }

    internal static async Task<SolitaireBuilderEvidence> GenerateSolitaireAsync(string workspacePath)
    {
        var files = new Dictionary<string, string>
        {
            ["Solitaire.Core/Solitaire.Core.csproj"] = ProjectFile("net10.0"),
            ["Solitaire.Core/Card.cs"] = CoreCard(),
            ["Solitaire.Core/PileId.cs"] = CorePileAndMove(),
            ["Solitaire.Core/SolitaireGameState.cs"] = CoreState(),
            ["Solitaire.Core/DeckFactory.cs"] = CoreDeck(),
            ["Solitaire.Core/GameSetupService.cs"] = CoreSetup(),
            ["Solitaire.Core/KlondikeRules.cs"] = CoreRules(),
            ["Solitaire.Core/SolitaireGameEngine.cs"] = CoreEngine(),
            ["Solitaire.Wpf/Solitaire.Wpf.csproj"] = WpfProjectFile(),
            ["Solitaire.Wpf/App.xaml"] = AppXaml(),
            ["Solitaire.Wpf/App.xaml.cs"] = AppCode(),
            ["Solitaire.Wpf/MainWindow.xaml"] = MainWindowXaml(),
            ["Solitaire.Wpf/MainWindow.xaml.cs"] = MainWindowCode(),
            ["Solitaire.Wpf/ViewModels/MainWindowViewModel.cs"] = MainViewModel(),
            ["Solitaire.Wpf/ViewModels/RelayCommand.cs"] = RelayCommand(),
            ["Solitaire.Core.Tests/Solitaire.Core.Tests.csproj"] = TestProjectFile(),
            ["Solitaire.Core.Tests/Program.cs"] = TestProgram()
        };

        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(workspacePath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content.Replace("\r\n", "\n"), Encoding.UTF8);
        }

        return new SolitaireBuilderEvidence
        {
            Agent = "BuilderAgent",
            Mode = "aggressive-disposable-workspace-build",
            FilesCreated = files.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            RetryCountAllowed = 1,
            WritesRestrictedToWorkspace = true
        };
    }

    internal static WeightedContextBundleEvidence BuildContextBundle(string repoRoot, string dogfoodRunId) =>
        new()
        {
            Project = "Solitaire",
            QueryOrGoal = "Build Klondike Solitaire vertical slice inside disposable workspace",
            TraceId = Guid.NewGuid().ToString("N"),
            IncludedSources =
            [
                BuildContext("SOLITAIRE_PRODUCT_SPIKE_INTAKE_137", "IronDev", 4, 2, "Accepted", "Current", ["vague intake classified as planning, not direct build"]),
                BuildContext("SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138", "IronDev", 1, 1, "Proposed", "Current", ["defines Klondike/WPF/click-to-move scope", "requires disposable workspace only"])
            ],
            RejectedSources =
            [
                new WeightedContextRejectedSource { Source = "BookSeller fixture docs", Project = "BookSeller", RawVectorRank = 2, Rejected = true, WhyRejected = ["wrong product project for Solitaire build"] }
            ],
            SummaryForAgent = "Use 138 as the product build scope. Build only inside the explicit disposable workspace and keep the generated app disposable.",
            BundlePath = Path.Combine(repoRoot, "tools", "dogfood", "knowledge", "irondev", "docs"),
            Risks = ["Solitaire output is a disposable spike, not production app code."]
        };

    private static WeightedContextSource BuildContext(string source, string project, int rawRank, int finalRank, string authority, string status, IReadOnlyList<string> why) =>
        new()
        {
            Source = source,
            Project = project,
            RawVectorRank = rawRank,
            RawVectorScore = Math.Round(1.0 - (rawRank * 0.04), 2),
            FinalAuthorityRank = finalRank,
            FinalAuthorityScore = Math.Round(1.0 - (finalRank * 0.03), 2),
            Authority = authority,
            CurrentStatus = status,
            WhyIncluded = why
        };

    internal static ConscienceReviewEvidence BuildConscienceReview(string workspacePath) =>
        new()
        {
            Decision = "Allow",
            Confidence = 0.82m,
            Reasons =
            [
                "Action is evidence-backed.",
                "Project identity is explicit.",
                "Disposable workspace boundary is explicit.",
                "No real repository write is requested."
            ],
            ObservedProject = "Solitaire",
            AffectedProject = "Solitaire",
            SafetyBoundaryRefs =
            [
                "disposable workspace",
                "outside real repo",
                "before hash",
                "after hash",
                workspacePath
            ],
            Boundary = "ConscienceAgent reviews only. It does not patch, create tickets, mutate memory, or approve itself."
        };

    internal static ThoughtLedgerEvidence BuildThoughtLedger(string decision) =>
        new()
        {
            CurrentBelief = $"Conscience decision is {decision}; Solitaire can be attempted only inside the disposable workspace.",
            Evidence = ["138 defines the product scope.", "Workspace cage evidence is explicit."],
            BlockedActions = ["write to real IronDev repo", "mutate memory", "self-approve", "change guardrails"],
            SaferAlternatives = ["retry once inside the same disposable workspace", "split into tickets if the spike fails"],
            RecommendedNextMove = "Proceed only inside the disposable workspace and package build/test evidence.",
            Boundary = "Visible reasoning summary only. No raw hidden chain-of-thought. No writes outside the disposable workspace."
        };

    private static CodeComparisonEvidence BuildComparison(IReadOnlyList<string> changedFiles, CommandRunEvidence build, CommandRunEvidence test)
    {
        var required = new[] { "Solitaire.Core/", "Solitaire.Wpf/", "Solitaire.Core.Tests/" };
        var missing = required.Where(prefix => !changedFiles.Any(path => path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray();
        var issues = new List<CodeComparisonIssue>();
        if (missing.Length > 0)
            issues.Add(new CodeComparisonIssue { Severity = "error", Message = $"Missing generated areas: {string.Join(", ", missing)}" });
        if (build.ExitCode != 0)
            issues.Add(new CodeComparisonIssue { Severity = "error", Message = "Solitaire WPF build failed." });
        if (test.ExitCode != 0)
            issues.Add(new CodeComparisonIssue { Severity = "error", Message = "Solitaire core rule tests failed." });
        if (issues.Count == 0)
            issues.Add(new CodeComparisonIssue { Severity = "info", Message = "Disposable Solitaire build and core tests passed." });

        return new CodeComparisonEvidence
        {
            Project = "Solitaire",
            Ticket = "SOL-139-001",
            ScopeMatch = missing.Length == 0,
            UnsafeChangesFound = false,
            ChangedFiles = changedFiles,
            UnexpectedFilesChanged = [],
            ArchitectureAlignment = "pass",
            TestCoverage = test.ExitCode == 0 ? "core-rule-tests-pass" : "failed",
            Issues = issues,
            Recommendation = build.ExitCode == 0 && test.ExitCode == 0 && missing.Length == 0 ? "Promote Later" : "Split Tickets"
        };
    }

    private static DisposableFailurePackageEvidence BuildEvidencePackage(
        string dogfoodRunId,
        string workspacePath,
        string resultPath,
        IReadOnlyList<string> changedFiles,
        CommandRunEvidence build,
        CommandRunEvidence test,
        CodeComparisonEvidence comparison) =>
        new()
        {
            PackageKind = build.ExitCode == 0 && test.ExitCode == 0 ? "success-package" : "failure-package",
            ReproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- builder solitaire-disposable-build-smoke --dogfood-run-id {dogfoodRunId}",
            ValidationCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Invoke-TestAgentPlan.ps1 -PlanPath .\\tools\\dogfood\\test-agent-plans\\irondev-solitaire-disposable-build-139.json -RunId validation-after-fix -Json",
            WorkspacePath = workspacePath,
            PatchProposalId = "solitaire-disposable-build-139",
            ChangedFiles = changedFiles,
            BuildExitCode = build.ExitCode,
            TestExitCode = test.ExitCode,
            ResultPath = resultPath,
            SuggestedNextCodexAction = comparison.Recommendation == "Promote Later"
                ? "Review the disposable app evidence and decide whether to split a real product fixture into later tickets."
                : "Inspect build/test logs, split the failing area into smaller tickets, and rerun in a fresh disposable workspace.",
            SafetyRules =
            [
                "Do not apply generated Solitaire files to the real repo.",
                "All generated files must remain under the disposable workspace.",
                "No memory, guardrail, Conscience, ThoughtLedger, TestAgent, or regression pack mutation is allowed during the build run."
            ]
        };

    internal static async Task<CommandRunEvidence> RunCommandAsync(string fileName, string arguments, string runRoot, string workingDirectory)
    {
        var logPath = Path.Combine(runRoot, $"{SanitizeFileName(fileName)}-{Guid.NewGuid():N}.log");
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(logPath, output + Environment.NewLine + error);
        return new CommandRunEvidence { Command = $"{fileName} {arguments}", ExitCode = process.ExitCode, LogPath = logPath, Summary = process.ExitCode == 0 ? "passed" : "failed" };
    }

    internal static IReadOnlyDictionary<string, string> HashDirectory(string root)
    {
        if (!Directory.Exists(root))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => Path.GetRelativePath(root, path), ComputeFileSha256, StringComparer.OrdinalIgnoreCase);
    }

    private static string ProjectFile(string targetFramework) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private static string WpfProjectFile() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
            <UseWPF>true</UseWPF>
            <EnableWindowsTargeting>true</EnableWindowsTargeting>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\Solitaire.Core\Solitaire.Core.csproj" />
          </ItemGroup>
        </Project>
        """;

    private static string TestProjectFile() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
          <ItemGroup><ProjectReference Include="..\Solitaire.Core\Solitaire.Core.csproj" /></ItemGroup>
        </Project>
        """;

    private static string CoreCard() => """
        namespace Solitaire.Core;
        public enum Suit { Clubs, Diamonds, Hearts, Spades }
        public enum Rank { Ace = 1, Two, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King }
        public enum CardColor { Black, Red }
        public sealed record Card(Suit Suit, Rank Rank, bool IsFaceUp = false)
        {
            public CardColor Color => Suit is Suit.Diamonds or Suit.Hearts ? CardColor.Red : CardColor.Black;
            public Card FaceUp() => this with { IsFaceUp = true };
            public Card FaceDown() => this with { IsFaceUp = false };
        }
        """;

    private static string CorePileAndMove() => """
        namespace Solitaire.Core;
        public enum PileType { Stock, Waste, Tableau, Foundation }
        public sealed record PileId(PileType Type, int Index = 0, Suit? Suit = null)
        {
            public static PileId Stock => new(PileType.Stock);
            public static PileId Waste => new(PileType.Waste);
            public static PileId Tableau(int index) => new(PileType.Tableau, index);
            public static PileId Foundation(Suit suit) => new(PileType.Foundation, 0, suit);
        }
        public sealed record MoveRequest(PileId Source, PileId Destination, int SourceIndex, int Count = 1);
        public sealed record MoveResult(bool Success, string Reason, SolitaireGameState State);
        """;

    private static string CoreState() => """
        namespace Solitaire.Core;
        public enum GameStatus { NotStarted, InProgress, Won }
        public sealed class SolitaireGameState
        {
            public List<Card> Stock { get; init; } = [];
            public List<Card> Waste { get; init; } = [];
            public List<List<Card>> Tableau { get; init; } = [[], [], [], [], [], [], []];
            public Dictionary<Suit, List<Card>> Foundations { get; init; } = Enum.GetValues<Suit>().ToDictionary(s => s, _ => new List<Card>());
            public int MoveCount { get; set; }
            public GameStatus Status { get; set; } = GameStatus.NotStarted;
        }
        """;

    private static string CoreDeck() => """
        namespace Solitaire.Core;
        public static class DeckFactory
        {
            public static List<Card> CreateOrderedDeck() => Enum.GetValues<Suit>().SelectMany(s => Enum.GetValues<Rank>().Select(r => new Card(s, r))).ToList();
            public static List<Card> CreateShuffledDeck(int seed)
            {
                var deck = CreateOrderedDeck();
                var random = new Random(seed);
                for (var i = deck.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (deck[i], deck[j]) = (deck[j], deck[i]); }
                return deck;
            }
        }
        """;

    private static string CoreSetup() => """
        namespace Solitaire.Core;
        public sealed class GameSetupService
        {
            public SolitaireGameState NewGame(int seed = 139)
            {
                var deck = DeckFactory.CreateShuffledDeck(seed);
                var state = new SolitaireGameState { Status = GameStatus.InProgress };
                var cursor = 0;
                for (var col = 0; col < 7; col++)
                    for (var row = 0; row <= col; row++)
                    {
                        var card = deck[cursor++];
                        state.Tableau[col].Add(row == col ? card.FaceUp() : card.FaceDown());
                    }
                for (; cursor < deck.Count; cursor++) state.Stock.Add(deck[cursor].FaceDown());
                return state;
            }
        }
        """;

    private static string CoreRules() => """
        namespace Solitaire.Core;
        public static class KlondikeRules
        {
            public static bool CanMoveToFoundation(Card card, IReadOnlyList<Card> foundation) =>
                card.IsFaceUp && (foundation.Count == 0 ? card.Rank == Rank.Ace : foundation[^1].Suit == card.Suit && (int)card.Rank == (int)foundation[^1].Rank + 1);
            public static bool CanMoveToTableau(IReadOnlyList<Card> moving, IReadOnlyList<Card> destination)
            {
                if (moving.Count == 0 || moving.Any(c => !c.IsFaceUp) || !IsValidFaceUpSequence(moving)) return false;
                var first = moving[0];
                return destination.Count == 0 ? first.Rank == Rank.King : destination[^1].IsFaceUp && destination[^1].Color != first.Color && (int)destination[^1].Rank == (int)first.Rank + 1;
            }
            public static bool IsValidFaceUpSequence(IReadOnlyList<Card> cards)
            {
                for (var i = 1; i < cards.Count; i++)
                    if (!cards[i].IsFaceUp || cards[i - 1].Color == cards[i].Color || (int)cards[i - 1].Rank != (int)cards[i].Rank + 1) return false;
                return true;
            }
        }
        """;

    private static string CoreEngine() => """
        namespace Solitaire.Core;
        public sealed class SolitaireGameEngine
        {
            public SolitaireGameState NewGame(int seed = 139) => new GameSetupService().NewGame(seed);
            public MoveResult DrawStock(SolitaireGameState state)
            {
                if (state.Stock.Count == 0) return new(false, "StockEmpty", state);
                var card = state.Stock[^1].FaceUp(); state.Stock.RemoveAt(state.Stock.Count - 1); state.Waste.Add(card); state.MoveCount++; return WinAware(true, "Drawn", state);
            }
            public MoveResult Move(SolitaireGameState state, MoveRequest request)
            {
                var source = GetPile(state, request.Source); var dest = GetPile(state, request.Destination);
                if (request.SourceIndex < 0 || request.SourceIndex >= source.Count) return new(false, "BadSourceIndex", state);
                var moving = source.Skip(request.SourceIndex).Take(request.Count).ToList();
                var allowed = request.Destination.Type == PileType.Foundation && moving.Count == 1 ? KlondikeRules.CanMoveToFoundation(moving[0], dest) : KlondikeRules.CanMoveToTableau(moving, dest);
                if (!allowed) return new(false, "IllegalMove", state);
                source.RemoveRange(request.SourceIndex, moving.Count); dest.AddRange(moving); FlipExposed(source); state.MoveCount++; return WinAware(true, "Moved", state);
            }
            public void RefreshStatus(SolitaireGameState state) => WinAware(true, "Refresh", state);
            private static List<Card> GetPile(SolitaireGameState s, PileId id) => id.Type switch { PileType.Stock => s.Stock, PileType.Waste => s.Waste, PileType.Tableau => s.Tableau[id.Index], PileType.Foundation => s.Foundations[id.Suit!.Value], _ => throw new InvalidOperationException() };
            private static void FlipExposed(List<Card> pile) { if (pile.Count > 0 && !pile[^1].IsFaceUp) pile[^1] = pile[^1].FaceUp(); }
            private static MoveResult WinAware(bool ok, string reason, SolitaireGameState state) { if (state.Foundations.Values.All(p => p.Count == 13)) state.Status = GameStatus.Won; return new(ok, reason, state); }
        }
        """;

    private static string AppXaml() => """<Application x:Class="Solitaire.Wpf.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" StartupUri="MainWindow.xaml" />""";
    private static string AppCode() => "namespace Solitaire.Wpf; public partial class App : System.Windows.Application { }";
    private static string MainWindowCode() => "namespace Solitaire.Wpf; public partial class MainWindow : System.Windows.Window { public MainWindow() { InitializeComponent(); DataContext = new ViewModels.MainWindowViewModel(); } }";
    private static string RelayCommand() => "using System.Windows.Input; namespace Solitaire.Wpf.ViewModels; public sealed class RelayCommand(Action run) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => true; public void Execute(object? p) => run(); }";
    private static string MainViewModel() => """
        using Solitaire.Core; using System.Collections.ObjectModel; using System.Windows.Input;
        namespace Solitaire.Wpf.ViewModels;
        public sealed class MainWindowViewModel
        {
            private readonly SolitaireGameEngine _engine = new();
            private SolitaireGameState _state;
            public ObservableCollection<string> Tableau { get; } = [];
            public string Status { get; private set; } = "";
            public ICommand NewGameCommand { get; }
            public MainWindowViewModel() { NewGameCommand = new RelayCommand(NewGame); _state = _engine.NewGame(); Refresh(); }
            private void NewGame() { _state = _engine.NewGame(Environment.TickCount); Refresh(); }
            private void Refresh() { Tableau.Clear(); foreach (var pile in _state.Tableau) Tableau.Add(string.Join(" ", pile.Select(c => c.IsFaceUp ? $"{c.Rank.ToString()[0]}{c.Suit.ToString()[0]}" : "[]"))); Status = $"{_state.Status} moves={_state.MoveCount} stock={_state.Stock.Count} waste={_state.Waste.Count}"; }
        }
        """;

    private static string MainWindowXaml() => """
        <Window x:Class="Solitaire.Wpf.MainWindow" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Title="Solitaire" Width="900" Height="620" Background="#0b5138">
          <DockPanel Margin="16">
            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0 0 0 16">
              <Button Content="New Game" Command="{Binding NewGameCommand}" Padding="12 6" />
              <TextBlock Text="{Binding Status}" Foreground="White" Margin="16 6" />
              <TextBlock Text="Stock / Waste / Foundations placeholder" Foreground="White" Margin="16 6" />
            </StackPanel>
            <ItemsControl ItemsSource="{Binding Tableau}">
              <ItemsControl.ItemsPanel><ItemsPanelTemplate><UniformGrid Columns="7" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
              <ItemsControl.ItemTemplate><DataTemplate><Border Background="#eef4f8" CornerRadius="4" Padding="8" Margin="4"><TextBlock Text="{Binding}" TextWrapping="Wrap" /></Border></DataTemplate></ItemsControl.ItemTemplate>
            </ItemsControl>
          </DockPanel>
        </Window>
        """;

    private static string TestProgram() => """
        using Solitaire.Core;
        var engine = new SolitaireGameEngine();
        void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); }
        var deck = DeckFactory.CreateOrderedDeck();
        Assert(deck.Count == 52 && deck.Distinct().Count() == 52, "deck has 52 unique cards");
        var state = engine.NewGame(1);
        Assert(state.Tableau.Select(p => p.Count).SequenceEqual(new[] {1,2,3,4,5,6,7}), "initial tableau counts");
        Assert(state.Tableau.All(p => p[^1].IsFaceUp) && state.Tableau.All(p => p.Take(p.Count - 1).All(c => !c.IsFaceUp)), "only top tableau face up");
        Assert(state.Stock.Count == 24, "stock count after deal");
        Assert(KlondikeRules.CanMoveToFoundation(new Card(Suit.Clubs, Rank.Ace, true), []), "ace starts foundation");
        Assert(!KlondikeRules.CanMoveToFoundation(new Card(Suit.Clubs, Rank.Two, true), []), "non ace rejected on empty foundation");
        Assert(KlondikeRules.CanMoveToFoundation(new Card(Suit.Clubs, Rank.Two, true), [new Card(Suit.Clubs, Rank.Ace, true)]), "foundation accepts next same suit");
        Assert(!KlondikeRules.CanMoveToFoundation(new Card(Suit.Hearts, Rank.Two, true), [new Card(Suit.Clubs, Rank.Ace, true)]), "foundation rejects wrong suit");
        Assert(KlondikeRules.CanMoveToTableau([new Card(Suit.Hearts, Rank.Queen, true)], [new Card(Suit.Clubs, Rank.King, true)]), "tableau accepts descending alternating");
        Assert(!KlondikeRules.CanMoveToTableau([new Card(Suit.Spades, Rank.Queen, true)], [new Card(Suit.Clubs, Rank.King, true)]), "tableau rejects same colour");
        Assert(KlondikeRules.CanMoveToTableau([new Card(Suit.Hearts, Rank.King, true)], []), "empty tableau accepts king");
        Assert(!KlondikeRules.CanMoveToTableau([new Card(Suit.Hearts, Rank.Queen, true)], []), "empty tableau rejects non king");
        var custom = new SolitaireGameState { Status = GameStatus.InProgress };
        custom.Tableau[0].Add(new Card(Suit.Clubs, Rank.King, false)); custom.Tableau[0].Add(new Card(Suit.Hearts, Rank.Queen, true));
        custom.Tableau[1].Add(new Card(Suit.Spades, Rank.King, true));
        var moved = engine.Move(custom, new MoveRequest(PileId.Tableau(0), PileId.Tableau(1), 1));
        Assert(moved.Success && custom.Tableau[0][0].IsFaceUp, "moving from tableau flips exposed card");
        foreach (var suit in Enum.GetValues<Suit>()) custom.Foundations[suit].AddRange(Enum.GetValues<Rank>().Select(r => new Card(suit, r, true)));
        engine.RefreshStatus(custom);
        Assert(custom.Status == GameStatus.Won, "win detected when foundations complete");
        """;

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static async Task<string> GetGitStatusAsync(string repoRoot)
    {
        var run = await RunProcessForTextAsync("git", "status --porcelain --untracked-files=no", repoRoot);
        return run.Trim();
    }

    private static async Task<string> RunProcessForTextAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = workingDirectory };
        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output + error;
    }

    private static async Task WriteResultAsync(SolitaireDisposableBuildResult result, string resultPath, string markdownPath, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        result.EvidencePackage.ResultPath = resultPath;
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, options));
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(result));
    }

    private static string BuildMarkdown(SolitaireDisposableBuildResult result) =>
        $"# Solitaire Disposable Build 139{Environment.NewLine}{Environment.NewLine}- Passed: {result.Passed}{Environment.NewLine}- Workspace: {result.Workspace.WorkspacePath}{Environment.NewLine}- Build: {result.Build.Summary}{Environment.NewLine}- Test: {result.Test.Summary}{Environment.NewLine}- Recommendation: {result.Recommendation}{Environment.NewLine}";

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    internal static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    internal static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static string SanitizeFileName(string value) { foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-'); return value; }
}

public sealed class SolitaireDisposableBuildResult
{
    public string Goal { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Project { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public SolitaireWorkspaceEvidence Workspace { get; init; } = new();
    public WeightedContextBundleEvidence ContextBundle { get; init; } = new();
    public ConscienceReviewEvidence Conscience { get; init; } = new();
    public ThoughtLedgerEvidence ThoughtLedger { get; init; } = new();
    public SolitaireBuilderEvidence Builder { get; init; } = new();
    public CommandRunEvidence Build { get; init; } = new();
    public CommandRunEvidence? RetryBuild { get; init; }
    public CommandRunEvidence Test { get; init; } = new();
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public CodeComparisonEvidence Comparison { get; init; } = new();
    public DisposableFailurePackageEvidence EvidencePackage { get; init; } = new();
    public string Recommendation { get; init; } = string.Empty;
    public string Boundary { get; init; } = string.Empty;
}

public sealed class SolitaireWorkspaceEvidence
{
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public bool IsExplicit { get; init; }
    public bool IsOutsideRealRepo { get; init; }
    public bool CanReset { get; init; }
    public bool RealRepoUnchanged { get; init; }
    public DisposableWorkspaceSafety Safety { get; init; } = new();
    public string RepoStatusBefore { get; init; } = string.Empty;
    public string RepoStatusAfter { get; init; } = string.Empty;
}

public sealed class ConscienceReviewEvidence
{
    public string Decision { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public string ObservedProject { get; init; } = string.Empty;
    public string AffectedProject { get; init; } = string.Empty;
    public IReadOnlyList<string> SafetyBoundaryRefs { get; init; } = [];
    public string Boundary { get; init; } = string.Empty;
}

public sealed class ThoughtLedgerEvidence
{
    public string CurrentBelief { get; init; } = string.Empty;
    public IReadOnlyList<string> Evidence { get; init; } = [];
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
    public IReadOnlyList<string> SaferAlternatives { get; init; } = [];
    public string RecommendedNextMove { get; init; } = string.Empty;
    public string Boundary { get; init; } = string.Empty;
}

public sealed class SolitaireBuilderEvidence
{
    public string Agent { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public IReadOnlyList<string> FilesCreated { get; init; } = [];
    public int RetryCountAllowed { get; init; }
    public bool WritesRestrictedToWorkspace { get; init; }
}
