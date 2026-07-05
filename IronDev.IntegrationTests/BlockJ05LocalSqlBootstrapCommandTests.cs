using System.Diagnostics;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("LocalSql")]
public sealed class BlockJ05LocalSqlBootstrapCommandTests
{
    private const string ScriptRelativePath = "Scripts/local/sql-local.ps1";
    private const string J04ScriptRelativePath = "Scripts/local/bootstrap-local.ps1";
    private const string BoundaryStatement = "The local SQL command may create or rebuild a developer-local database. It is not evidence, approval, root safety proof, policy satisfaction, schema authority, or permission to mutate source, workflows, evidence, or shared SQL targets.";

    [TestMethod]
    public void J05_SqlLocalScript_ExistsAndIsDocumented()
    {
        Assert.IsTrue(File.Exists(RepositoryFile(ScriptRelativePath)), "J05 local SQL command must exist.");

        var localDevelopment = ReadRepositoryFile("Docs/local-development.md");
        var inventory = ReadRepositoryFile("Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md");
        var receipt = ReadRepositoryFile("Docs/receipts/J05_LOCAL_SQL_BOOTSTRAP_REBUILD_COMMAND.md");

        StringAssert.Contains(localDevelopment, ScriptRelativePath.Replace('/', '\\'));
        StringAssert.Contains(inventory, ScriptRelativePath);
        StringAssert.Contains(receipt, ScriptRelativePath);
        StringAssert.Contains(receipt, "Default invocation");
    }

    [TestMethod]
    public void J05_DefaultMode_IsCheckOnlyAndNonMutating()
    {
        using var fixture = SqlLocalFixture.Create();
        var result = RunSqlLocal(fixture);

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Check-only mode must not invoke sqlcmd.");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "IronDev.Api", "appsettings.Development.Local.json")), "J05 must not write local overrides.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "artifacts")), "J05 check-only mode must not write artifacts.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "logs")), "J05 check-only mode must not write logs.");

        StringAssert.Contains(result.CombinedOutput, "Mode: CheckOnly");
        StringAssert.Contains(result.CombinedOutput, "Create | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Rebuild | NotRun");
        StringAssert.Contains(result.CombinedOutput, "ApplyLocalDevSetup | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Action | NotRun | CheckOnly");
        AssertDoesNotContain(result.CombinedOutput, "CREATE DATABASE", "check-only output");
        AssertDoesNotContain(result.CombinedOutput, "DROP DATABASE", "check-only output");
    }

    [TestMethod]
    public void J05_CreateAndRebuild_RequireExplicitModes()
    {
        using var fixture = SqlLocalFixture.Create();

        var applyWithoutMode = RunSqlLocal(fixture, "-ApplyLocalDevSetup");
        Assert.AreNotEqual(0, applyWithoutMode.ExitCode);
        StringAssert.Contains(applyWithoutMode.CombinedOutput, "ApplyLocalDevSetup requires Create or Rebuild");

        var checkOnlyWithCreate = RunSqlLocal(fixture, "-CheckOnly", "-Create");
        Assert.AreNotEqual(0, checkOnlyWithCreate.ExitCode);
        StringAssert.Contains(checkOnlyWithCreate.CombinedOutput, "CheckOnly cannot be combined");

        var rebuildWithoutConfirm = RunSqlLocal(
            fixture,
            "-Rebuild",
            "-ServerInstance",
            @"(localdb)\MSSQLLocalDB",
            "-DatabaseName",
            "IronDeveloper_Local");
        Assert.AreNotEqual(0, rebuildWithoutConfirm.ExitCode);
        StringAssert.Contains(rebuildWithoutConfirm.CombinedOutput, "RebuildConfirmationRejected");

        var wrongConfirm = RunSqlLocal(
            fixture,
            "-Rebuild",
            "-ServerInstance",
            @"(localdb)\MSSQLLocalDB",
            "-DatabaseName",
            "IronDeveloper_Local",
            "-ConfirmRebuild",
            "REBUILD IronDeveloper_Prod");
        Assert.AreNotEqual(0, wrongConfirm.ExitCode);
        StringAssert.Contains(wrongConfirm.CombinedOutput, "RebuildConfirmationRejected");

        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Invalid explicit-mode calls must not invoke sqlcmd.");
    }

    [DataTestMethod]
    [DataRow("prod-sql.company.net", "SqlTargetRemoteRejected")]
    [DataRow("10.1.2.3", "SqlTargetRemoteRejected")]
    [DataRow("tcp:prod.database.windows.net", "SqlTargetAzureRejected")]
    [DataRow("myserver.database.windows.net", "SqlTargetAzureRejected")]
    [DataRow("sql-prod", "SqlTargetUnknownRejected")]
    public void J05_RejectsRemoteOrUnknownSqlTargets(string serverInstance, string expectedReason)
    {
        using var fixture = SqlLocalFixture.Create();
        var result = RunSqlLocal(
            fixture,
            "-Create",
            "-ServerInstance",
            serverInstance,
            "-DatabaseName",
            "IronDeveloper_Local");

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.CombinedOutput, expectedReason);
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Rejected SQL targets must not invoke sqlcmd.");
        AssertDoesNotContain(result.CombinedOutput, serverInstance, "rejected-target output");
    }

    [DataTestMethod]
    [DataRow(@"(localdb)\MSSQLLocalDB")]
    [DataRow(".")]
    [DataRow("(local)")]
    [DataRow("localhost")]
    [DataRow("localhost,1433")]
    [DataRow("127.0.0.1")]
    [DataRow(@"localhost\IronDevLocal")]
    public void J05_AllowsOnlyLocalSqlTargets(string serverInstance)
    {
        using var fixture = SqlLocalFixture.Create();
        var result = RunSqlLocal(fixture, "-CheckOnly", "-ServerInstance", serverInstance, "-DatabaseName", "IronDeveloper_Local");

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "SqlTargetLocal");
        StringAssert.Contains(result.CombinedOutput, "DatabaseNameSafeLocal");
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Classification-only local target checks must not invoke sqlcmd.");
    }

    [DataTestMethod]
    [DataRow("master", "DatabaseNameSystemRejected")]
    [DataRow("model", "DatabaseNameSystemRejected")]
    [DataRow("msdb", "DatabaseNameSystemRejected")]
    [DataRow("tempdb", "DatabaseNameSystemRejected")]
    [DataRow("IronDeveloper", "DatabaseNameProductionLikeRejected")]
    [DataRow("IronDeveloper_Prod", "DatabaseNameProductionLikeRejected")]
    [DataRow("IronDeveloper_Live", "DatabaseNameProductionLikeRejected")]
    [DataRow("Production", "DatabaseNameProductionLikeRejected")]
    [DataRow("Prod", "DatabaseNameProductionLikeRejected")]
    [DataRow("Live", "DatabaseNameProductionLikeRejected")]
    [DataRow("Accept", "DatabaseNameProductionLikeRejected")]
    [DataRow("IronDeveloper_Local;DROP DATABASE master", "DatabaseNameUnsafeCharactersRejected")]
    public void J05_RejectsUnsafeDatabaseNames(string databaseName, string expectedReason)
    {
        using var fixture = SqlLocalFixture.Create();
        var result = RunSqlLocal(
            fixture,
            "-Create",
            "-ServerInstance",
            @"(localdb)\MSSQLLocalDB",
            "-DatabaseName",
            databaseName);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.CombinedOutput, expectedReason);
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Unsafe database names must not invoke sqlcmd.");
    }

    [DataTestMethod]
    [DataRow("IronDeveloper_Local")]
    [DataRow("IronDeveloper_Local_DevA")]
    [DataRow("IronDeveloper_Dev")]
    [DataRow("IronDeveloper_Test")]
    [DataRow("IronDeveloper_J05_123456")]
    public void J05_AllowsSafeLocalDatabaseNames(string databaseName)
    {
        using var fixture = SqlLocalFixture.Create();
        var result = RunSqlLocal(fixture, "-CheckOnly", "-ServerInstance", @"(localdb)\MSSQLLocalDB", "-DatabaseName", databaseName);

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "DatabaseNameSafeLocal");
        StringAssert.Contains(result.CombinedOutput, databaseName);
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Check-only database-name classification must not invoke sqlcmd.");
    }

    [TestMethod]
    public void J05_RebuildRequiresExactConfirmationPhrase()
    {
        using var fixture = SqlLocalFixture.Create();
        var wrong = RunSqlLocal(
            fixture,
            "-Rebuild",
            "-ServerInstance",
            @"(localdb)\MSSQLLocalDB",
            "-DatabaseName",
            "IronDeveloper_Local",
            "-ConfirmRebuild",
            "DROP IronDeveloper_Local");

        Assert.AreNotEqual(0, wrong.ExitCode);
        StringAssert.Contains(wrong.CombinedOutput, "RebuildConfirmationRejected");
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Wrong confirmation must block before sqlcmd.");

        var accepted = RunSqlLocal(
            fixture,
            "-Rebuild",
            "-ServerInstance",
            @"(localdb)\MSSQLLocalDB",
            "-DatabaseName",
            "IronDeveloper_Local",
            "-ConfirmRebuild",
            "REBUILD IronDeveloper_Local");

        Assert.AreEqual(0, accepted.ExitCode, accepted.CombinedOutput);
        StringAssert.Contains(accepted.CombinedOutput, "Rebuild result | Completed");
        Assert.IsTrue(File.Exists(fixture.SqlLogPath), "Exact confirmation with fake sqlcmd should reach the guarded SQL call.");
    }

    [DataTestMethod]
    [DataRow(@"..\outside.sql", "SetupScriptOutsideRepositoryRejected")]
    [DataRow("https://example.invalid/setup.sql", "SetupScriptRemoteRejected")]
    public void J05_SetupScriptPath_CannotEscapeRepository(string setupScript, string expectedReason)
    {
        using var fixture = SqlLocalFixture.Create();
        var result = RunSqlLocal(
            fixture,
            "-Create",
            "-ApplyLocalDevSetup",
            "-ServerInstance",
            @"(localdb)\MSSQLLocalDB",
            "-DatabaseName",
            "IronDeveloper_Local",
            "-SetupScript",
            setupScript);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.CombinedOutput, expectedReason);
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Rejected setup script paths must block before sqlcmd.");
    }

    [TestMethod]
    public void J05_SetupScriptPath_RejectsAbsoluteUserLocalPaths()
    {
        using var fixture = SqlLocalFixture.Create();
        var userPath = string.Join(Path.DirectorySeparatorChar, "C:", "Users", "Example", "setup.sql");
        var result = RunSqlLocal(
            fixture,
            "-Create",
            "-ApplyLocalDevSetup",
            "-ServerInstance",
            @"(localdb)\MSSQLLocalDB",
            "-DatabaseName",
            "IronDeveloper_Local",
            "-SetupScript",
            userPath);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.CombinedOutput, "SetupScriptOutsideRepositoryRejected");
        AssertDoesNotContain(result.CombinedOutput, "Example", "setup-script rejection output");
        AssertDoesNotContain(result.CombinedOutput, userPath, "setup-script rejection output");
        Assert.IsFalse(File.Exists(fixture.SqlLogPath), "Rejected user-local setup paths must block before sqlcmd.");
    }

    [TestMethod]
    public void J05_Output_RedactsSecretsAndPaths()
    {
        using var fixture = SqlLocalFixture.Create();
        var hiddenToken = "hidden-j05-token-value";
        var hiddenPassword = "hidden-j05-password-value";
        var hiddenConnection = string.Concat("Server=prod;", "User Id=", "s", "a;", "Pass", "word=", hiddenPassword);
        var fakeUserPath = string.Join(Path.DirectorySeparatorChar, "C:", "Users", "Example", ".irondev", "sql");

        var result = RunSqlLocal(
            fixture,
            environment: new Dictionary<string, string?>
            {
                [string.Concat("J05_FAKE_", "TOKEN")] = hiddenToken,
                [string.Concat("J05_FAKE_", "PASSWORD")] = hiddenPassword,
                [string.Concat("J05_FAKE_CONNECTION_", "STRING")] = hiddenConnection,
                ["J05_FAKE_PATH"] = fakeUserPath
            });

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        AssertDoesNotContain(result.CombinedOutput, hiddenToken, "J05 output");
        AssertDoesNotContain(result.CombinedOutput, hiddenPassword, "J05 output");
        AssertDoesNotContain(result.CombinedOutput, hiddenConnection, "J05 output");
        AssertDoesNotContain(result.CombinedOutput, "Example", "J05 output");
        AssertDoesNotContain(result.CombinedOutput, fakeUserPath, "J05 output");
        AssertDoesNotContain(result.CombinedOutput, fixture.Root, "J05 output");
    }

    [TestMethod]
    public void J05_ScriptDoesNotContainForbiddenAuthorityOrProductFlowCommands()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        foreach (var marker in ForbiddenAuthorityAndProductFlowMarkers())
            AssertDoesNotContain(script, marker, "J05 local SQL script");
    }

    [TestMethod]
    public void J05_ScriptDoesNotInvokeWeaviateDockerFrontendOrApiStart()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        foreach (var marker in ForbiddenServiceStartMarkers())
            AssertDoesNotContain(script, marker, "J05 local SQL script");
    }

    [TestMethod]
    public void J05_J04BootstrapDoesNotInvokeSqlLocalAutomatically()
    {
        var j04 = ReadRepositoryFile(J04ScriptRelativePath);

        AssertDoesNotContain(j04, "sql-local.ps1", "J04 bootstrap script");
        AssertDoesNotContain(j04, "Scripts/local/sql-local.ps1", "J04 bootstrap script");
    }

    [TestMethod]
    public void J05_ReceiptStatesSqlBoundary()
    {
        var receipt = ReadRepositoryFile("Docs/receipts/J05_LOCAL_SQL_BOOTSTRAP_REBUILD_COMMAND.md");

        StringAssert.Contains(receipt, BoundaryStatement);
        StringAssert.Contains(receipt, "A successful SQL bootstrap means a local database was prepared. It does not mean the alpha loop has passed.");
        StringAssert.Contains(receipt, "There is no `-Force` mode in J05.");
        StringAssert.Contains(receipt, "J05 does not add SQL username, password, raw connection-string, or credential parameters.");
    }

    [TestMethod]
    public void J05_NoRuntimeAuthoritySurfaceAdded()
    {
        var changedProductionFiles = CurrentChangedFiles()
            .Where(path =>
                !IsJ10RootSafetyFile(path) &&
                !IsJ09StartupSafetyFile(path) &&
                (path.StartsWith("IronDev.Core/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("IronDev.Api/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("tools/IronDev.Cli/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("IronDev.TauriShell/", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.AreEqual(0, changedProductionFiles.Length, "J05 must not add production/API/CLI/frontend runtime files: " + string.Join(", ", changedProductionFiles));
    }

    private static IReadOnlyList<string> ForbiddenAuthorityAndProductFlowMarkers() =>
    [
        "ControlledSourceApply",
        "AcceptedApproval",
        "WorkflowContinuation",
        "CriticReview",
        "ReleaseExecutor",
        "DeploymentExecutor",
        "skeleton-runs",
        "gh pr",
        "git push",
        "git commit"
    ];

    private static IReadOnlyList<string> ForbiddenServiceStartMarkers() =>
    [
        "docker compose",
        "weaviate",
        "weaviate-dev.ps1",
        "npm run dev",
        "dotnet run",
        "Start-Process"
    ];

    private static CommandResult RunSqlLocal(SqlLocalFixture fixture, params string[] arguments) =>
        RunSqlLocal(fixture, environment: null, arguments);

    private static CommandResult RunSqlLocal(
        SqlLocalFixture fixture,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("powershell")
        {
            WorkingDirectory = fixture.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(fixture.ScriptPath);

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment["PATH"] = fixture.FakeSqlDirectory + Path.PathSeparator + startInfo.Environment["PATH"];
        startInfo.Environment["J05_SQLCMD_LOG"] = fixture.SqlLogPath;

        if (environment is not null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.IsTrue(process.WaitForExit(60_000), "J05 local SQL script timed out.");

        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private static IReadOnlyList<string> CurrentChangedFiles()
    {
        return GitFileList(["diff", "--name-only", "origin/main...HEAD"])
            .Concat(GitFileList(["diff", "--name-only"]))
            .Concat(GitFileList(["diff", "--cached", "--name-only"]))
            .Concat(GitStatusFiles())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsJ10RootSafetyFile(string path) =>
        path.Equals("IronDev.Core/Configuration/LocalRootSafetyModels.cs", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("IronDev.Core/Configuration/LocalRootSafetyValidator.cs", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("IronDev.Core/Configuration/RedactedConfigSummaryModels.cs", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("IronDev.Core/Configuration/ReleaseRootSafetyGateModels.cs", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("IronDev.Core/Configuration/ReleaseRootSafetyGate.cs", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("IronDev.Infrastructure/Services/Workspaces/DisposableWorkspaceExecutionService.cs", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("IronDev.Infrastructure/Services/Workspaces/DisposableWorkspaceReadinessService.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsJ09StartupSafetyFile(string path) =>
        path.Equals("IronDev.Api/Program.cs", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> GitStatusFiles()
    {
        var lines = GitOutput(["status", "--porcelain"]);
        return lines
            .Select(line => line.Length > 3 ? line[3..] : string.Empty)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
    }

    private static IReadOnlyList<string> GitFileList(IReadOnlyList<string> arguments) =>
        GitOutput(arguments)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();

    private static IReadOnlyList<string> GitOutput(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            Assert.Inconclusive("Could not inspect J05 changed files: " + stderr);

        return stdout.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(RepositoryFile(relativePath));

    private static string RepositoryFile(string relativePath) =>
        Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static void AssertDoesNotContain(string text, string marker, string sourceName)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{marker}'.");
    }

    private sealed record CommandResult(int ExitCode, string Stdout, string Stderr)
    {
        public string CombinedOutput => string.Join(Environment.NewLine, Stdout, Stderr);
    }

    private sealed class SqlLocalFixture : IDisposable
    {
        private SqlLocalFixture(string root)
        {
            Root = root;
            ScriptPath = Path.Combine(root, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
            FakeSqlDirectory = Path.Combine(root, "fake-sql");
            SqlLogPath = Path.Combine(root, "sqlcmd.log");
        }

        public string Root { get; }
        public string ScriptPath { get; }
        public string FakeSqlDirectory { get; }
        public string SqlLogPath { get; }

        public static SqlLocalFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "IronDevJ05", Guid.NewGuid().ToString("N"));
            var fixture = new SqlLocalFixture(root);

            Directory.CreateDirectory(Path.Combine(root, "Scripts", "local"));
            Directory.CreateDirectory(Path.Combine(root, "Database"));
            Directory.CreateDirectory(Path.Combine(root, "IronDev.Api"));
            Directory.CreateDirectory(fixture.FakeSqlDirectory);
            File.WriteAllText(Path.Combine(root, "IronDev.slnx"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Database", "local_dev_setup.sql"), "SELECT 1;" + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(fixture.FakeSqlDirectory, "sqlcmd.cmd"),
                """
                @echo off
                if not "%J05_SQLCMD_LOG%"=="" echo sqlcmd %*>>"%J05_SQLCMD_LOG%"
                exit /b 0
                """);
            File.Copy(RepositoryFile(ScriptRelativePath), fixture.ScriptPath);

            return fixture;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
