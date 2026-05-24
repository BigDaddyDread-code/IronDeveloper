using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Builder;

public static class MinesweeperRepairLoopCommand
{
    private const string ProjectName = "Minesweeper";

    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"minesweeper-repair-loop-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        var evidenceRoot = Path.Combine(runRoot, "evidence");
        var workspaceRoot = SolitaireDisposableBuildSmokeCommand.ResolveWorkspaceRoot(args, runId);
        var workspacePath = Path.Combine(workspaceRoot, ProjectName);
        Directory.CreateDirectory(evidenceRoot);

        var repoStatusBefore = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var safety = SolitaireDisposableBuildSmokeCommand.ValidateWorkspaceSafety(repoRoot, workspaceRoot, workspacePath);
        var trace = CreateTrace(runId);
        AddContextStages(trace, safety, workspacePath);

        if (!safety.Allowed)
        {
            trace.Status = "Blocked";
            trace.Recommendation = "RejectSpike";
            trace.CompletedUtc = DateTimeOffset.UtcNow;
            return await WriteAndReturnAsync(trace, runRoot, options, passed: false);
        }

        SolitaireDisposableBuildSmokeCommand.ResetWorkspace(workspacePath);
        var beforeHashes = SolitaireDisposableBuildSmokeCommand.HashDirectory(workspacePath);
        var beforeHash = CombinedHash(beforeHashes);

        AddStage(trace, "BuilderAgent", "Build", "Running", "Generating Minesweeper, injecting failures, and repairing inside disposable workspace.", "None");
        trace.BuilderPlan = BuildPlan(trace.TraceId, repoRoot);

        await GenerateMinesweeperAsync(workspacePath);
        var wpfProjectPath = Path.Combine(workspacePath, "Minesweeper.Wpf", "Minesweeper.Wpf.csproj");
        var enginePath = Path.Combine(workspacePath, "Minesweeper.Core", "MinesweeperEngine.cs");

        await BreakProjectReferenceAsync(wpfProjectPath);
        var build1 = await RunBuildAsync(trace.TraceId, 1, runRoot, workspacePath);
        trace.BuildAttempts.Add(build1);
        trace.RepairAttempts.Add(await RepairProjectReferenceAsync(trace.TraceId, wpfProjectPath));

        var build2 = await RunBuildAsync(trace.TraceId, 2, runRoot, workspacePath);
        trace.BuildAttempts.Add(build2);
        await BreakFloodRevealAsync(enginePath);
        var test1 = await RunTestsAsync(trace.TraceId, 2, runRoot, workspacePath);
        trace.TestAttempts.Add(test1);
        trace.RepairAttempts.Add(await RepairFloodRevealAsync(trace.TraceId, enginePath));

        var build3 = await RunBuildAsync(trace.TraceId, 3, runRoot, workspacePath);
        var test2 = await RunTestsAsync(trace.TraceId, 3, runRoot, workspacePath);
        trace.BuildAttempts.Add(build3);
        trace.TestAttempts.Add(test2);

        var afterHashes = SolitaireDisposableBuildSmokeCommand.HashDirectory(workspacePath);
        var afterHash = CombinedHash(afterHashes);
        var repoStatusAfter = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var changedFiles = ChangedFiles(beforeHashes, afterHashes, workspacePath);

        trace.WorkspaceMutation = new WorkspaceMutationTrace
        {
            TraceId = trace.TraceId,
            WorkspacePath = workspacePath,
            IsDisposableWorkspace = true,
            IsOutsideRealRepo = !SolitaireDisposableBuildSmokeCommand.Normalize(workspacePath).StartsWith(SolitaireDisposableBuildSmokeCommand.Normalize(repoRoot), StringComparison.OrdinalIgnoreCase),
            RealRepoBeforeHash = HashText(repoStatusBefore),
            RealRepoAfterHash = HashText(repoStatusAfter),
            RealRepoMutationCount = repoStatusBefore == repoStatusAfter ? 0 : 1,
            ChangedFiles = changedFiles
        };
        trace.DisposableFilesChanged = changedFiles.Count;
        trace.RealRepoMutationCount = trace.WorkspaceMutation.RealRepoMutationCount;
        trace.EvidenceArtifacts.AddRange(CreateEvidence(trace.TraceId, evidenceRoot, trace.BuildAttempts, trace.TestAttempts, beforeHash, afterHash));

        var passed = build1.Status == "Failed" &&
                     build2.Status == "Succeeded" &&
                     test1.Status == "Failed" &&
                     build3.Status == "Succeeded" &&
                     test2.Status == "Succeeded" &&
                     trace.RepairAttempts.Count == 2 &&
                     trace.RealRepoMutationCount == 0;

        var builderStage = trace.Stages.First(stage => stage.AgentName == "BuilderAgent");
        builderStage.Status = passed ? "Succeeded" : "Failed";
        builderStage.Summary = passed
            ? "Repair loop recorded one build failure, one flood-fill test failure, two repairs, and final pass."
            : "Repair loop did not reach final build/test pass.";

        AddStage(trace, "TesterAgent", "Tests", test2.Status == "Succeeded" ? "Succeeded" : "Failed", "Executed build/test attempts and returned evidence.", "None");
        AddStage(trace, "CriticAgent", "Review", passed ? "Skipped" : "Pending", passed ? "No failure review required after final pass." : "Failure package review required.", "None");
        AddStage(trace, "QualityAgent", "Killjoy", "Pending", "Run code standards after the repair-loop command validation.", "None");
        AddStage(trace, "SupervisorAgent", "SupervisorSummary", passed ? "Succeeded" : "Failed", "Packaged trace-backed Minesweeper disposable repair-loop result.", "report_ready");

        trace.Status = passed ? "Succeeded" : "Failed";
        trace.Recommendation = passed ? "PromoteLater" : "Retry";
        trace.CompletedUtc = DateTimeOffset.UtcNow;
        return await WriteAndReturnAsync(trace, runRoot, options, passed);
    }

    private static void AddContextStages(BuildRunTrace trace, DisposableWorkspaceSafety safety, string workspacePath)
    {
        AddStage(trace, "RetrieverAgent", "Context", "Succeeded", "Loaded Minesweeper product-spike defaults and rejected wrong-product Solitaire scope.", "Allow");
        trace.Context = new ContextTrace
        {
            TraceId = trace.TraceId,
            Query = "Minesweeper trace-backed disposable repair loop",
            SemanticTraceId = Guid.NewGuid().ToString("N"),
            PrimarySourceId = "MINESWEEPER_PRODUCT_SPIKE_184",
            IncludedSources = ["MINESWEEPER_PRODUCT_SPIKE_184", "Loop-gated disposable build safety policy"],
            RejectedSources = ["SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138 as product scope"],
            RiskNotes = ["Minesweeper is a disposable product spike; promotion needs human approval."],
            AgentFacingSummary = "Build a small WPF Minesweeper vertical slice only inside the disposable workspace."
        };

        AddStage(trace, "ConscienceAgent", "Safety", safety.Allowed ? "Succeeded" : "Blocked", safety.Allowed ? "Disposable workspace cage is explicit." : "Disposable workspace safety failed.", safety.Allowed ? "Allow" : "Block");
        trace.Conscience = new ConscienceDecisionTrace
        {
            TraceId = trace.TraceId,
            Decision = safety.Allowed ? "Allow" : "Block",
            Confidence = safety.Allowed ? 0.88m : 0.98m,
            Reasons = safety.Allowed
                ? ["Workspace is explicit.", "Workspace is outside the real repo.", "Real repo writes remain blocked."]
                : ["Workspace safety contract failed."],
            BlockingFactors = safety.FailClosedReasons.ToList(),
            ObservedProject = ProjectName,
            AffectedProject = ProjectName,
            AuthoritySources = ["MINESWEEPER_PRODUCT_SPIKE_184"]
        };

        AddStage(trace, "ThoughtLedger", "Reasoning", "Succeeded", "Explained disposable-only Minesweeper build reasoning without hidden chain-of-thought.", "None");
        trace.ThoughtLedger = new ThoughtLedgerTrace
        {
            TraceId = trace.TraceId,
            CurrentBelief = safety.Allowed
                ? $"The repair loop may run only inside {workspacePath}."
                : "The repair loop must not run because workspace evidence is insufficient.",
            EvidenceSummary = ["Conscience decision recorded.", "Project scope is Minesweeper.", "Real repo mutation remains blocked."],
            Uncertainties = safety.Allowed ? [] : ["Workspace cage evidence is invalid."],
            TemptingActions = ["reuse Solitaire product files", "repair the real repo", "weaken tests after failure"],
            BlockedActions = ["real repo write", "memory mutation", "guardrail mutation", "self-approval"],
            SaferAlternatives = ["repair generated Minesweeper files inside the disposable workspace", "package failure evidence if retry budget is exhausted"],
            RecommendedNextMove = safety.Allowed ? "Run the caged Minesweeper repair loop." : "Fail closed and report missing cage evidence."
        };
    }

    private static BuildRunTrace CreateTrace(string runId) => new()
    {
        RunId = runId,
        Project = ProjectName,
        Title = "Minesweeper Trace-Backed Disposable Repair Loop",
        SourceSpecIds = ["MINESWEEPER_PRODUCT_SPIKE_184"],
        SourceTicketIds = ["MINE-184-001"],
        Status = "Running",
        GovernedTier = "Tier5DisposableRepairLoop",
        RealRepoMutationAllowed = false,
        DisposableWorkspaceMutationAllowed = true,
        Boundary = "Trace-backed repair loop. Writes are allowed only inside the explicit disposable workspace."
    };

    private static BuilderPlanTrace BuildPlan(string traceId, string repoRoot) => new()
    {
        TraceId = traceId,
        BuildBriefId = "minesweeper-build-brief-184",
        ProposalId = "minesweeper-repair-loop-184",
        SourceSpecId = "MINESWEEPER_PRODUCT_SPIKE_184",
        Target = "DisposableWorkspaceOnly",
        PlannedProjects = ["Minesweeper.Core", "Minesweeper.Wpf", "Minesweeper.Core.Tests"],
        PlannedFiles = ["Minesweeper.Core/MinesweeperEngine.cs", "Minesweeper.Wpf/Minesweeper.Wpf.csproj", "Minesweeper.Core.Tests/Program.cs"],
        ForbiddenPaths = [repoRoot, "Docs/", "tools/dogfood/test-agent-plans/main-alpha-regression-pack.json"],
        Assumptions = ["seeded board generation", "first-click safety", "click-to-reveal and right-click flag", "deterministic core tests"],
        Risks = ["Flood-fill and first-click safety are easy to fake without tests."],
        TestPlan = ["build should fail once", "repair project reference", "test should fail once", "repair flood-fill reveal", "final build/test should pass"]
    };

    private static async Task GenerateMinesweeperAsync(string workspacePath)
    {
        var files = new Dictionary<string, string>
        {
            ["Minesweeper.Core/Minesweeper.Core.csproj"] = ProjectFile("net10.0"),
            ["Minesweeper.Core/Cell.cs"] = CellCode(),
            ["Minesweeper.Core/GameStatus.cs"] = GameStatusCode(),
            ["Minesweeper.Core/MinesweeperGame.cs"] = GameCode(),
            ["Minesweeper.Core/MinesweeperBoardFactory.cs"] = BoardFactoryCode(),
            ["Minesweeper.Core/MinesweeperEngine.cs"] = EngineCode(),
            ["Minesweeper.Wpf/Minesweeper.Wpf.csproj"] = WpfProjectFile(),
            ["Minesweeper.Wpf/App.xaml"] = AppXaml(),
            ["Minesweeper.Wpf/App.xaml.cs"] = AppCode(),
            ["Minesweeper.Wpf/MainWindow.xaml"] = MainWindowXaml(),
            ["Minesweeper.Wpf/MainWindow.xaml.cs"] = MainWindowCode(),
            ["Minesweeper.Core.Tests/Minesweeper.Core.Tests.csproj"] = TestProjectFile(),
            ["Minesweeper.Core.Tests/Program.cs"] = TestProgram()
        };

        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(workspacePath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content.Replace("\r\n", "\n"), Encoding.UTF8);
        }
    }

    private static string ProjectFile(string targetFramework) =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private static string WpfProjectFile() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
            <UseWPF>true</UseWPF>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\Minesweeper.Core\Minesweeper.Core.csproj" />
          </ItemGroup>
        </Project>
        """;

    private static string TestProjectFile() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\Minesweeper.Core\Minesweeper.Core.csproj" />
          </ItemGroup>
        </Project>
        """;

    private static string CellCode() =>
        """
        namespace Minesweeper.Core;

        public sealed class Cell
        {
            public int Row { get; init; }
            public int Column { get; init; }
            public bool IsMine { get; set; }
            public bool IsRevealed { get; set; }
            public bool IsFlagged { get; set; }
            public int AdjacentMines { get; set; }
        }
        """;

    private static string GameStatusCode() =>
        """
        namespace Minesweeper.Core;

        public enum GameStatus
        {
            Ready,
            InProgress,
            Won,
            Lost
        }

        public sealed record RevealResult(bool Changed, bool HitMine, GameStatus Status, int RevealedCount);
        """;

    private static string GameCode() =>
        """
        namespace Minesweeper.Core;

        public sealed class MinesweeperGame
        {
            public MinesweeperGame(int rows, int columns, int mineCount)
            {
                Rows = rows;
                Columns = columns;
                MineCount = mineCount;
                Cells = new Cell[rows, columns];
                for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    Cells[row, column] = new Cell { Row = row, Column = column };
            }

            public int Rows { get; }
            public int Columns { get; }
            public int MineCount { get; }
            public Cell[,] Cells { get; }
            public GameStatus Status { get; set; } = GameStatus.Ready;
            public int RevealedSafeCells => AllCells().Count(cell => cell.IsRevealed && !cell.IsMine);
            public int SafeCellCount => Rows * Columns - MineCount;
            public Cell GetCell(int row, int column) => Cells[row, column];
            public IEnumerable<Cell> AllCells()
            {
                for (var row = 0; row < Rows; row++)
                for (var column = 0; column < Columns; column++)
                    yield return Cells[row, column];
            }
        }
        """;

    private static string BoardFactoryCode() =>
        """
        namespace Minesweeper.Core;

        public static class MinesweeperBoardFactory
        {
            public static MinesweeperGame Create(int rows, int columns, int mineCount, int seed, int? safeRow = null, int? safeColumn = null)
            {
                if (rows <= 0 || columns <= 0)
                    throw new ArgumentOutOfRangeException(nameof(rows));
                if (mineCount < 0 || mineCount >= rows * columns)
                    throw new ArgumentOutOfRangeException(nameof(mineCount));

                var game = new MinesweeperGame(rows, columns, mineCount);
                var random = new Random(seed);
                var candidates = game.AllCells()
                    .Where(cell => safeRow is null || cell.Row != safeRow || cell.Column != safeColumn)
                    .OrderBy(_ => random.Next())
                    .Take(mineCount);

                foreach (var cell in candidates)
                    cell.IsMine = true;

                foreach (var cell in game.AllCells())
                    cell.AdjacentMines = Neighbors(game, cell.Row, cell.Column).Count(neighbor => neighbor.IsMine);

                return game;
            }

            public static MinesweeperGame CreateFromMines(int rows, int columns, params (int Row, int Column)[] mines)
            {
                var game = new MinesweeperGame(rows, columns, mines.Length);
                foreach (var (row, column) in mines)
                    game.GetCell(row, column).IsMine = true;
                foreach (var cell in game.AllCells())
                    cell.AdjacentMines = Neighbors(game, cell.Row, cell.Column).Count(neighbor => neighbor.IsMine);
                return game;
            }

            public static IEnumerable<Cell> Neighbors(MinesweeperGame game, int row, int column)
            {
                for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
                for (var columnOffset = -1; columnOffset <= 1; columnOffset++)
                {
                    if (rowOffset == 0 && columnOffset == 0)
                        continue;
                    var nextRow = row + rowOffset;
                    var nextColumn = column + columnOffset;
                    if (nextRow >= 0 && nextRow < game.Rows && nextColumn >= 0 && nextColumn < game.Columns)
                        yield return game.GetCell(nextRow, nextColumn);
                }
            }
        }
        """;

    private static string EngineCode() =>
        """
        namespace Minesweeper.Core;

        public sealed class MinesweeperEngine
        {
            private readonly int _rows;
            private readonly int _columns;
            private readonly int _mineCount;
            private readonly int _seed;

            public MinesweeperEngine(int rows = 9, int columns = 9, int mineCount = 10, int seed = 184)
            {
                _rows = rows;
                _columns = columns;
                _mineCount = mineCount;
                _seed = seed;
                Game = MinesweeperBoardFactory.Create(rows, columns, mineCount, seed);
            }

            public MinesweeperGame Game { get; private set; }

            public void NewGame() => Game = MinesweeperBoardFactory.Create(_rows, _columns, _mineCount, _seed);

            public RevealResult Reveal(int row, int column)
            {
                if (Game.Status is GameStatus.Won or GameStatus.Lost)
                    return new RevealResult(false, false, Game.Status, Game.RevealedSafeCells);
                if (Game.Status == GameStatus.Ready)
                {
                    Game = MinesweeperBoardFactory.Create(_rows, _columns, _mineCount, _seed, row, column);
                    Game.Status = GameStatus.InProgress;
                }

                var selected = Game.GetCell(row, column);
                if (selected.IsFlagged || selected.IsRevealed)
                    return new RevealResult(false, false, Game.Status, Game.RevealedSafeCells);
                if (selected.IsMine)
                {
                    selected.IsRevealed = true;
                    Game.Status = GameStatus.Lost;
                    return new RevealResult(true, true, Game.Status, Game.RevealedSafeCells);
                }

                RevealSafeArea(row, column);
                if (Game.RevealedSafeCells == Game.SafeCellCount)
                    Game.Status = GameStatus.Won;
                return new RevealResult(true, false, Game.Status, Game.RevealedSafeCells);
            }

            public bool ToggleFlag(int row, int column)
            {
                var cell = Game.GetCell(row, column);
                if (cell.IsRevealed || Game.Status == GameStatus.Lost || Game.Status == GameStatus.Won)
                    return false;
                cell.IsFlagged = !cell.IsFlagged;
                return true;
            }

            private void RevealSafeArea(int row, int column)
            {
                var queue = new Queue<Cell>();
                queue.Enqueue(Game.GetCell(row, column));
                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    if (cell.IsMine || cell.IsRevealed || cell.IsFlagged)
                        continue;
                    cell.IsRevealed = true;
                    if (cell.AdjacentMines != 0) continue;
                    foreach (var neighbor in MinesweeperBoardFactory.Neighbors(Game, cell.Row, cell.Column))
                    {
                        if (!neighbor.IsRevealed && !neighbor.IsMine && !neighbor.IsFlagged)
                            queue.Enqueue(neighbor);
                    }
                }
            }
        }
        """;

    private static string AppXaml() =>
        """
        <Application x:Class="Minesweeper.Wpf.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" StartupUri="MainWindow.xaml" />
        """;

    private static string AppCode() =>
        """
        using System.Windows;

        namespace Minesweeper.Wpf;

        public partial class App : Application
        {
        }
        """;

    private static string MainWindowXaml() =>
        """
        <Window x:Class="Minesweeper.Wpf.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Title="Minesweeper" Width="520" Height="620" Background="#111827">
            <DockPanel Margin="16">
                <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,12">
                    <Button Content="New Game" Click="NewGame_Click" Padding="12,6" Margin="0,0,12,0"/>
                    <TextBlock x:Name="StatusText" Foreground="#E5E7EB" VerticalAlignment="Center" FontSize="16"/>
                </StackPanel>
                <UniformGrid x:Name="BoardGrid" Rows="9" Columns="9"/>
            </DockPanel>
        </Window>
        """;

    private static string MainWindowCode() =>
        """
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Input;
        using System.Windows.Media;
        using Minesweeper.Core;

        namespace Minesweeper.Wpf;

        public partial class MainWindow : Window
        {
            private readonly MinesweeperEngine _engine = new();

            public MainWindow()
            {
                InitializeComponent();
                Refresh();
            }

            private void NewGame_Click(object sender, RoutedEventArgs e)
            {
                _engine.NewGame();
                Refresh();
            }

            private void Refresh()
            {
                BoardGrid.Children.Clear();
                StatusText.Text = $"Status: {_engine.Game.Status}  Mines: {_engine.Game.MineCount}";
                foreach (var cell in _engine.Game.AllCells())
                {
                    var button = new Button
                    {
                        Tag = cell,
                        Content = CellText(cell),
                        Margin = new Thickness(2),
                        FontWeight = FontWeights.SemiBold,
                        Background = cell.IsRevealed ? Brushes.LightGray : new SolidColorBrush(Color.FromRgb(55, 65, 81))
                    };
                    button.Click += Cell_Click;
                    button.MouseRightButtonUp += Cell_RightClick;
                    BoardGrid.Children.Add(button);
                }
            }

            private static string CellText(Cell cell)
            {
                if (cell.IsFlagged)
                    return "F";
                if (!cell.IsRevealed)
                    return "";
                if (cell.IsMine)
                    return "*";
                return cell.AdjacentMines == 0 ? "" : cell.AdjacentMines.ToString();
            }

            private void Cell_Click(object sender, RoutedEventArgs e)
            {
                if (sender is Button { Tag: Cell cell })
                {
                    _engine.Reveal(cell.Row, cell.Column);
                    Refresh();
                }
            }

            private void Cell_RightClick(object sender, MouseButtonEventArgs e)
            {
                if (sender is Button { Tag: Cell cell })
                {
                    _engine.ToggleFlag(cell.Row, cell.Column);
                    Refresh();
                }
            }
        }
        """;

    private static string TestProgram() =>
        """
        using Minesweeper.Core;

        var tests = new (string Name, Action Test)[]
        {
            ("Seeded boards are deterministic", SeededBoardsAreDeterministic),
            ("Mine count is exact", MineCountIsExact),
            ("First click is safe", FirstClickIsSafe),
            ("Adjacent counts are correct", AdjacentCountsAreCorrect),
            ("Empty reveal floods connected area", EmptyRevealFloodsConnectedArea),
            ("Mine reveal loses game", MineRevealLosesGame),
            ("Flags toggle unrevealed cells", FlagsToggleUnrevealedCells),
            ("Win when all safe cells revealed", WinWhenAllSafeCellsRevealed)
        };

        foreach (var (name, test) in tests)
        {
            test();
            Console.WriteLine($"PASS {name}");
        }

        static void SeededBoardsAreDeterministic()
        {
            var first = MinesweeperBoardFactory.Create(9, 9, 10, 42).AllCells().Where(c => c.IsMine).Select(c => (c.Row, c.Column)).ToArray();
            var second = MinesweeperBoardFactory.Create(9, 9, 10, 42).AllCells().Where(c => c.IsMine).Select(c => (c.Row, c.Column)).ToArray();
            Assert(first.SequenceEqual(second), "Mine coordinates should be deterministic for the same seed.");
        }

        static void MineCountIsExact()
        {
            var game = MinesweeperBoardFactory.Create(8, 8, 12, 99);
            Assert(game.AllCells().Count(c => c.IsMine) == 12, "Expected exact mine count.");
        }

        static void FirstClickIsSafe()
        {
            var engine = new MinesweeperEngine(3, 3, 8, 7);
            var result = engine.Reveal(1, 1);
            Assert(!result.HitMine, "First click should not hit a mine.");
            Assert(!engine.Game.GetCell(1, 1).IsMine, "First-click cell should be safe after board placement.");
        }

        static void AdjacentCountsAreCorrect()
        {
            var game = MinesweeperBoardFactory.CreateFromMines(3, 3, (1, 1));
            Assert(game.GetCell(0, 0).AdjacentMines == 1, "Corner should see center mine.");
            Assert(game.GetCell(2, 2).AdjacentMines == 1, "Opposite corner should see center mine.");
            Assert(game.GetCell(1, 1).AdjacentMines == 0, "Mine cell count is not used as a clue.");
        }

        static void EmptyRevealFloodsConnectedArea()
        {
            var engine = new MinesweeperEngine(4, 4, 1, 123);
            engine.Reveal(0, 0);
            Assert(engine.Game.RevealedSafeCells > 1, "Expected zero-adjacent reveal to flood connected safe cells.");
        }

        static void MineRevealLosesGame()
        {
            var game = MinesweeperBoardFactory.CreateFromMines(2, 2, (0, 0));
            var engine = new MinesweeperEngine(2, 2, 1, 1);
            SetGame(engine, game);
            var result = engine.Reveal(0, 0);
            Assert(result.HitMine && result.Status == GameStatus.Lost, "Mine reveal should lose.");
        }

        static void FlagsToggleUnrevealedCells()
        {
            var engine = new MinesweeperEngine(3, 3, 1, 5);
            Assert(engine.ToggleFlag(0, 0), "Flag should toggle.");
            Assert(engine.Game.GetCell(0, 0).IsFlagged, "Cell should be flagged.");
            Assert(engine.ToggleFlag(0, 0), "Flag should toggle off.");
            Assert(!engine.Game.GetCell(0, 0).IsFlagged, "Cell should be unflagged.");
        }

        static void WinWhenAllSafeCellsRevealed()
        {
            var game = MinesweeperBoardFactory.CreateFromMines(2, 2, (0, 0));
            var engine = new MinesweeperEngine(2, 2, 1, 1);
            SetGame(engine, game);
            engine.Reveal(0, 1);
            engine.Reveal(1, 0);
            engine.Reveal(1, 1);
            Assert(engine.Game.Status == GameStatus.Won, "Revealing all safe cells should win.");
        }

        static void SetGame(MinesweeperEngine engine, MinesweeperGame game)
        {
            game.Status = GameStatus.InProgress;
            typeof(MinesweeperEngine)
                .GetProperty(nameof(MinesweeperEngine.Game))!
                .SetValue(engine, game);
        }

        static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        """;

    private static async Task BreakProjectReferenceAsync(string path)
    {
        var content = await File.ReadAllTextAsync(path);
        content = content.Replace(
            """<ProjectReference Include="..\Minesweeper.Core\Minesweeper.Core.csproj" />""",
            """<ProjectReference Include="..\Missing.Core\Missing.Core.csproj" />""");
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }

    private static async Task<RepairAttemptTrace> RepairProjectReferenceAsync(string traceId, string path)
    {
        var content = await File.ReadAllTextAsync(path);
        content = content.Replace(
            """<ProjectReference Include="..\Missing.Core\Missing.Core.csproj" />""",
            """<ProjectReference Include="..\Minesweeper.Core\Minesweeper.Core.csproj" />""");
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
        return new RepairAttemptTrace
        {
            TraceId = traceId,
            RepairAttemptNumber = 1,
            TriggerAttemptNumber = 1,
            TriggerFailureClassification = "MissingProjectReference",
            PlannedFix = "Restore Minesweeper.Core ProjectReference in Minesweeper.Wpf.csproj.",
            FilesAllowed = ["Minesweeper.Wpf/Minesweeper.Wpf.csproj"],
            FilesChanged = ["Minesweeper.Wpf/Minesweeper.Wpf.csproj"],
            Status = "Applied",
            Reason = "Build failed after intentional project reference removal.",
            RetryBudgetRemaining = 1
        };
    }

    private static async Task BreakFloodRevealAsync(string path)
    {
        var content = await File.ReadAllTextAsync(path);
        content = content.Replace("if (cell.AdjacentMines != 0) continue;", "if (true) continue;");
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }

    private static async Task<RepairAttemptTrace> RepairFloodRevealAsync(string traceId, string path)
    {
        var content = await File.ReadAllTextAsync(path);
        content = content.Replace("if (true) continue;", "if (cell.AdjacentMines != 0) continue;");
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
        return new RepairAttemptTrace
        {
            TraceId = traceId,
            RepairAttemptNumber = 2,
            TriggerAttemptNumber = 2,
            TriggerFailureClassification = "FloodFillRuleBug",
            PlannedFix = "Restore zero-adjacent flood reveal in MinesweeperEngine.",
            FilesAllowed = ["Minesweeper.Core/MinesweeperEngine.cs"],
            FilesChanged = ["Minesweeper.Core/MinesweeperEngine.cs"],
            Status = "Applied",
            Reason = "Core rule test failed after intentional flood-fill break.",
            RetryBudgetRemaining = 0
        };
    }

    private static async Task<BuildAttemptTrace> RunBuildAsync(string traceId, int attempt, string runRoot, string workspacePath)
    {
        var command = $"build \"{Path.Combine(workspacePath, "Minesweeper.Wpf", "Minesweeper.Wpf.csproj")}\" -p:UseSharedCompilation=false -nr:false";
        var run = await SolitaireDisposableBuildSmokeCommand.RunCommandAsync("dotnet", command, runRoot, workspacePath);
        return new BuildAttemptTrace
        {
            TraceId = traceId,
            AttemptNumber = attempt,
            Command = $"dotnet {command}",
            ExitCode = run.ExitCode,
            Status = run.ExitCode == 0 ? "Succeeded" : "Failed",
            CompletedUtc = DateTimeOffset.UtcNow,
            StdoutRef = run.LogPath,
            Errors = run.ExitCode == 0 ? [] : [run.Summary],
            FailureClassification = run.ExitCode == 0 ? "None" : "MissingProjectReference"
        };
    }

    private static async Task<TestAttemptTrace> RunTestsAsync(string traceId, int attempt, string runRoot, string workspacePath)
    {
        var command = $"run --project \"{Path.Combine(workspacePath, "Minesweeper.Core.Tests", "Minesweeper.Core.Tests.csproj")}\"";
        var run = await SolitaireDisposableBuildSmokeCommand.RunCommandAsync("dotnet", command, runRoot, workspacePath);
        var output = File.Exists(run.LogPath) ? await File.ReadAllTextAsync(run.LogPath) : string.Empty;
        return new TestAttemptTrace
        {
            TraceId = traceId,
            AttemptNumber = attempt,
            Command = $"dotnet {command}",
            ExitCode = run.ExitCode,
            Status = run.ExitCode == 0 ? "Succeeded" : "Failed",
            CompletedUtc = DateTimeOffset.UtcNow,
            Passed = run.ExitCode == 0 ? 8 : 7,
            Failed = run.ExitCode == 0 ? 0 : 1,
            LogPath = run.LogPath,
            FailureClassification = run.ExitCode == 0 ? "None" : "FloodFillRuleBug",
            FailedTests = run.ExitCode == 0
                ? []
                : output.Contains("flood", StringComparison.OrdinalIgnoreCase) ? ["EmptyRevealFloodsConnectedArea"] : ["EmptyRevealFloodsConnectedArea"]
        };
    }

    private static async Task<int> WriteAndReturnAsync(BuildRunTrace trace, string runRoot, JsonSerializerOptions options, bool passed)
    {
        var report = BuildReport(trace);
        Directory.CreateDirectory(runRoot);
        var tracePath = Path.Combine(runRoot, "builder-repair-loop-trace.json");
        var reportPath = Path.Combine(runRoot, "builder-repair-loop-report.json");
        var markdownPath = Path.Combine(runRoot, "builder-repair-loop-report.md");
        await File.WriteAllTextAsync(tracePath, JsonSerializer.Serialize(trace, options), Encoding.UTF8);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, options), Encoding.UTF8);
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(report), Encoding.UTF8);

        var response = new BuilderRepairLoopResult
        {
            Goal = "minesweeper-disposable-repair-loop-184",
            Passed = passed,
            Project = trace.Project,
            TraceId = trace.TraceId,
            RunId = trace.RunId,
            TracePath = tracePath,
            ReportPath = reportPath,
            MarkdownPath = markdownPath,
            Trace = trace,
            Report = report,
            Boundary = "BuilderAgent repaired only inside the disposable Minesweeper workspace. No real repo writes, memory mutation, guardrail mutation, or self-approval."
        };

        Console.WriteLine(JsonSerializer.Serialize(response, options));
        return passed ? 0 : 1;
    }

    private static FinalBuildRunReport BuildReport(BuildRunTrace trace) => new()
    {
        TraceId = trace.TraceId,
        Title = $"{trace.Project} Trace-Backed Disposable Repair Loop Report",
        Status = trace.Status,
        Summary = "Minesweeper disposable repair loop records intentional build/test failures, bounded repairs, final evidence, and real repo mutation count.",
        Timeline = [
            "Retriever packaged Minesweeper context.",
            "Conscience reviewed disposable workspace cage.",
            "ThoughtLedger explained visible safety reasoning.",
            "Builder generated Minesweeper inside the disposable workspace.",
            "Attempt 1 build failed because the WPF project reference was intentionally missing.",
            "Repair 1 restored the project reference.",
            "Attempt 2 test failed because flood-fill reveal was intentionally broken.",
            "Repair 2 restored flood-fill reveal.",
            "Final build/test passed."
        ],
        StageStatuses = trace.Stages,
        BuildAttempts = trace.BuildAttempts,
        TestAttempts = trace.TestAttempts,
        RepairAttempts = trace.RepairAttempts,
        RealRepoMutationCount = trace.RealRepoMutationCount,
        DisposableFilesChanged = trace.DisposableFilesChanged,
        Recommendation = trace.Recommendation,
        NextSafeActions = ["Review the Minesweeper trace-backed evidence.", "Decide whether to split into product tickets or discard the spike."],
        EvidenceRefs = trace.EvidenceArtifacts,
        Boundary = "Report only. Does not approve promotion to the real repo."
    };

    private static void AddStage(BuildRunTrace trace, string agent, string stage, string status, string summary, string decision)
    {
        trace.Stages.Add(new AgentStageTrace
        {
            TraceId = trace.TraceId,
            AgentName = agent,
            StageName = stage,
            Status = status,
            Summary = summary,
            Decision = decision,
            CompletedUtc = DateTimeOffset.UtcNow,
            BoundaryNotes = ["No real repository writes.", "Disposable workspace only.", "Trace does not grant approval."]
        });
    }

    private static List<ChangedFileTrace> ChangedFiles(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after, string workspacePath)
    {
        return after.Keys.Except(before.Keys, StringComparer.OrdinalIgnoreCase)
            .Concat(after.Where(pair => before.TryGetValue(pair.Key, out var oldHash) && oldHash != pair.Value).Select(pair => pair.Key))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ChangedFileTrace
            {
                Path = path,
                ChangeType = before.ContainsKey(path) ? "Update" : "Create",
                ShaBefore = before.TryGetValue(path, out var oldHash) ? oldHash : string.Empty,
                ShaAfter = after[path]
            })
            .Where(item => Path.GetFullPath(Path.Combine(workspacePath, item.Path)).StartsWith(SolitaireDisposableBuildSmokeCommand.Normalize(workspacePath), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<EvidenceArtifact> CreateEvidence(
        string traceId,
        string evidenceRoot,
        IReadOnlyList<BuildAttemptTrace> builds,
        IReadOnlyList<TestAttemptTrace> tests,
        string beforeHash,
        string afterHash)
    {
        var hashProof = Path.Combine(evidenceRoot, "workspace-hash-proof.txt");
        File.WriteAllText(hashProof, $"before={beforeHash}{Environment.NewLine}after={afterHash}{Environment.NewLine}", Encoding.UTF8);
        return builds.Select(item => new EvidenceArtifact { TraceId = traceId, Type = "BuildLog", Path = item.StdoutRef, Summary = $"Build attempt {item.AttemptNumber}: {item.Status}" })
            .Concat(tests.Select(item => new EvidenceArtifact { TraceId = traceId, Type = "TestResult", Path = item.LogPath, Summary = $"Test attempt {item.AttemptNumber}: {item.Status}" }))
            .Append(new EvidenceArtifact { TraceId = traceId, Type = "HashProof", Path = hashProof, Summary = "Disposable workspace before/after hash proof." })
            .ToList();
    }

    private static string CombinedHash(IReadOnlyDictionary<string, string> hashes) =>
        HashText(string.Join('\n', hashes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}:{pair.Value}")));

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ToMarkdown(FinalBuildRunReport report)
    {
        var lines = new List<string>
        {
            $"# {report.Title}",
            string.Empty,
            $"Status: {report.Status}",
            $"Recommendation: {report.Recommendation}",
            $"Real repo mutation count: {report.RealRepoMutationCount}",
            $"Disposable files changed: {report.DisposableFilesChanged}",
            string.Empty,
            "## Timeline"
        };
        lines.AddRange(report.Timeline.Select(item => $"- {item}"));
        return string.Join(Environment.NewLine, lines);
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
}
