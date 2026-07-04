using System.Diagnostics;
using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("LocalBootstrap")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class BlockJ07DeveloperEnvironmentDoctorTests
{
    private const string ScriptRelativePath = "Scripts/local/doctor-local.ps1";
    private const string ReceiptRelativePath = "Docs/receipts/J07_DEVELOPER_ENVIRONMENT_DOCTOR.md";
    private const string BoundaryStatement = "The developer doctor is diagnostic only. It reports local readiness blockers and next safe actions; it does not create readiness, evidence, approval, authority, or permission to run/mutate/apply/release.";

    [TestMethod]
    public void J07_DoctorScript_ExistsAndIsDocumented()
    {
        Assert.IsTrue(File.Exists(RepositoryFile(ScriptRelativePath)), "J07 doctor command must exist.");

        var localDevelopment = ReadRepositoryFile("Docs/local-development.md");
        var inventory = ReadRepositoryFile("Docs/testing/INTEGRATION_TEST_CATEGORIES.md");
        var receipt = ReadRepositoryFile(ReceiptRelativePath);

        StringAssert.Contains(localDevelopment, ScriptRelativePath.Replace('/', '\\'));
        StringAssert.Contains(inventory, "## J07 Developer Environment Doctor");
        StringAssert.Contains(receipt, ScriptRelativePath);
        StringAssert.Contains(receipt, "Default invocation");
        StringAssert.Contains(receipt, BoundaryStatement);
    }

    [TestMethod]
    public void J07_DefaultMode_IsDiagnosticOnlyAndDoesNotMutate()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture);

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "artifacts")), "Doctor must not create artifact folders.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "logs")), "Doctor must not create log folders.");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "evidence.json")), "Doctor must not write evidence.");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "doctor-report.json")), "Doctor must not write reports.");

        StringAssert.Contains(result.CombinedOutput, "Doctor result: Blocked");
        StringAssert.Contains(result.CombinedOutput, "RootSafety | NotEvaluated | Blocker");
        StringAssert.Contains(result.CombinedOutput, BoundaryStatement);
        AssertChildLogOnlyUsesCheckOnly(fixture);
        AssertDoesNotContain(result.CombinedOutput, fixture.Root, "doctor output");
    }

    [TestMethod]
    public void J07_ForbiddenSwitches_ReturnUnsafeExitCodeBeforeChildChecks()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture, string.Concat("-", "Fix"));

        Assert.AreEqual(3, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "Unsafe requested option rejected");
        Assert.IsFalse(File.Exists(fixture.ChildLogPath), "Unsafe options must block before J05/J06 check-only delegation.");
    }

    [TestMethod]
    public void J07_JsonOutput_IsStableAndParseable()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture, "-Json");

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        using var json = JsonDocument.Parse(result.Stdout);
        var root = json.RootElement;

        Assert.AreEqual("Blocked", root.GetProperty("DoctorStatus").GetString());
        Assert.AreEqual("CheckOnly", root.GetProperty("Mode").GetString());
        StringAssert.Contains(root.GetProperty("BoundaryStatement").GetString(), "diagnostic only");
        Assert.IsTrue(root.GetProperty("Checks").GetArrayLength() > 8);
        StringAssert.Contains(root.GetProperty("NextSafeAction").GetString(), "J10 root safety");
    }

    [TestMethod]
    public void J07_MarkdownOutput_IsStableAndContainsOneNextSafeAction()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture, "-Markdown");

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.Stdout, "# IronDev Developer Environment Doctor");
        StringAssert.Contains(result.Stdout, "**Doctor result:** Blocked");
        StringAssert.Contains(result.Stdout, "## Next Safe Action");
        Assert.AreEqual(1, CountOccurrences(result.Stdout, "## Next Safe Action"));
    }

    [TestMethod]
    public void J07_SqlDoctor_DelegatesOnlyToJ05CheckOnly()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture);

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        var log = File.ReadAllText(fixture.ChildLogPath);

        StringAssert.Contains(log, "sql-local.ps1 -CheckOnly");
        AssertDoesNotContain(log, string.Concat("-", "Create"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "Rebuild"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "ApplyLocalDevSetup"), "child command log");
    }

    [TestMethod]
    public void J07_RemoteSqlTarget_BlocksBeforeJ05Delegation()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture, "-SqlServer", "prod-sql.company.net");

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "SqlTargetRemoteRejected");
        AssertChildLogDoesNotContain(fixture, "sql-local.ps1");
        AssertDoesNotContain(result.CombinedOutput, "prod-sql.company.net", "remote SQL rejection output");
    }

    [TestMethod]
    public void J07_UnsafeDatabaseName_BlocksBeforeJ05Delegation()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture, "-DatabaseName", "IronDeveloper_Prod");

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "DatabaseNameProductionLikeRejected");
        AssertChildLogDoesNotContain(fixture, "sql-local.ps1");
    }

    [TestMethod]
    public void J07_WeaviateDoctor_DelegatesOnlyToJ06CheckOnly()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture);

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        var log = File.ReadAllText(fixture.ChildLogPath);

        StringAssert.Contains(log, "weaviate-local.ps1 -CheckOnly");
        AssertDoesNotContain(log, string.Concat("-", "EnsureSchema"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "Rebuild"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "StartDocker"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "SeedDemo"), "child command log");
    }

    [TestMethod]
    public void J07_RemoteWeaviateEndpoint_BlocksBeforeJ06Delegation()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctor(fixture, "-WeaviateEndpoint", "https://cluster.weaviate.cloud");

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "WeaviateEndpointCloudRejected");
        AssertChildLogDoesNotContain(fixture, "weaviate-local.ps1");
        AssertDoesNotContain(result.CombinedOutput, "cluster.weaviate.cloud", "remote Weaviate rejection output");
    }

    [TestMethod]
    public void J07_LocalOverrideMissing_ReportsSingleNextSafeActionWithoutPrintingContents()
    {
        using var fixture = DoctorFixture.Create(includeLocalOverride: false);
        var result = RunDoctor(fixture);

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "LocalOverride | Missing | Warning");
        StringAssert.Contains(result.CombinedOutput, "bootstrap-local.ps1 -Prepare -CreateLocalOverride");
        AssertDoesNotContain(result.CombinedOutput, "local-override-secret", "doctor output");
    }

    [TestMethod]
    public void J07_TrackedLocalOverride_Blocks()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctorWithGitMode(fixture, "tracked");

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "TrackedLocalOverrideRejected");
        AssertDoesNotContain(result.CombinedOutput, File.ReadAllText(fixture.LocalOverridePath), "doctor output");
    }

    [TestMethod]
    public void J07_IgnoredLocalOverride_DoesNotPrintRawConfig()
    {
        using var fixture = DoctorFixture.Create();
        var result = RunDoctorWithGitMode(fixture, "ignored");

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "LocalOverride | Pass | Info | LocalOverrideIgnoredAndUntracked");
        AssertDoesNotContain(result.CombinedOutput, File.ReadAllText(fixture.LocalOverridePath), "doctor output");
        AssertDoesNotContain(result.CombinedOutput, "local-override-secret", "doctor output");
    }

    [TestMethod]
    public void J07_LocalTestUnsafeConfig_BlocksBeforeStartupRecommendation()
    {
        using var fixture = DoctorFixture.Create();
        fixture.WriteLocalTestConfig(
            databaseName: "IronDeveloper_Local",
            workspaceRoot: @"C:\IronDevWorkspaces",
            logsRoot: @"C:\IronDevLogs",
            dangerRealRepoWrites: true);

        var result = RunDoctor(fixture);

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "LocalTestDatabaseMustContainTest");
        StringAssert.Contains(result.CombinedOutput, "LocalTestWorkspaceRootMustContainTest");
        StringAssert.Contains(result.CombinedOutput, "LocalTestLogsRootMustContainTest");
        StringAssert.Contains(result.CombinedOutput, "LocalTestRealRepoWritesEnabledRejected");
        AssertDoesNotContain(result.CombinedOutput, @"C:\IronDevWorkspaces", "unsafe LocalTest output");
        AssertDoesNotContain(result.CombinedOutput, @"C:\IronDevLogs", "unsafe LocalTest output");
    }

    [TestMethod]
    public void J07_OutputDoesNotLeakSecretsEnvironmentValuesOrUserPaths()
    {
        using var fixture = DoctorFixture.Create();
        var hidden = string.Concat("hidden", "-j07", "-value");
        var fakeUserPath = string.Join(Path.DirectorySeparatorChar, "C:", "Users", "Robert", ".irondev", "logs");
        var result = RunDoctor(
            fixture,
            environment: new Dictionary<string, string?>
            {
                [string.Concat("J07_FAKE_", "TOKEN")] = hidden,
                ["J07_FAKE_PATH"] = fakeUserPath
            });

        Assert.AreEqual(2, result.ExitCode, result.CombinedOutput);
        AssertDoesNotContain(result.CombinedOutput, hidden, "doctor output");
        AssertDoesNotContain(result.CombinedOutput, "Robert", "doctor output");
        AssertDoesNotContain(result.CombinedOutput, fakeUserPath, "doctor output");
        AssertDoesNotContain(result.CombinedOutput, fixture.Root, "doctor output");
    }

    [TestMethod]
    public void J07_ApiAndUiProbes_AreGetOnlyAndLocalOnly()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        StringAssert.Contains(script, "Invoke-WebRequest");
        StringAssert.Contains(script, "-Method Get");
        StringAssert.Contains(script, "Get-LoopbackUrlClassification");
        AssertDoesNotContain(script, "-Method Post", "J07 script");
        AssertDoesNotContain(script, "-Method Put", "J07 script");
        AssertDoesNotContain(script, "-Method Patch", "J07 script");
        AssertDoesNotContain(script, "-Method Delete", "J07 script");
        AssertDoesNotContain(script, "AuthorizationHeader", "J07 script");
    }

    [TestMethod]
    public void J07_ToolchainRules_AreEncodedAsBlockers()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        foreach (var expected in new[] { "DotNetMissing", "NodeMissing", "NpmMissing", "SqlcmdMissing", "DockerMissingOptional" })
            StringAssert.Contains(script, expected);

        StringAssert.Contains(script, "Install the required .NET SDK");
        StringAssert.Contains(script, "Install Node.js/npm");
        StringAssert.Contains(script, "Install sqlcmd");
        StringAssert.Contains(script, "Install Docker only if local Weaviate setup requires it");
    }

    [TestMethod]
    public void J07_ScriptDoesNotContainForbiddenRuntimeOrMutationCommands()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        foreach (var marker in ForbiddenRuntimeMarkers())
            AssertDoesNotContain(script, marker, "J07 script");
    }

    [TestMethod]
    public void J07_ReceiptStatesDoctorBoundary()
    {
        var receipt = ReadRepositoryFile(ReceiptRelativePath);

        StringAssert.Contains(receipt, "The developer doctor reports local readiness blockers and next safe actions.");
        StringAssert.Contains(receipt, "It does not create readiness, evidence, approval, authority, service state, SQL state, Weaviate state, smoke proof, source mutation, or release permission.");
        StringAssert.Contains(receipt, "A green doctor report is a map, not permission.");
        StringAssert.Contains(receipt, "J07 may delegate to J05 and J06 only in check-only mode.");
    }

    [TestMethod]
    public void J07_NoRuntimeAuthoritySurfaceAdded()
    {
        var changedProductionFiles = CurrentChangedFiles()
            .Where(path =>
                path.StartsWith("IronDev.Core/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Api/Controllers/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("tools/IronDev.Cli/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.TauriShell/src/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, changedProductionFiles.Length, "J07 must not add production/API/CLI/frontend/workflow runtime files: " + string.Join(", ", changedProductionFiles));
    }

    private static IReadOnlyList<string> ForbiddenRuntimeMarkers() =>
    [
        "Start-Process",
        "docker compose up",
        "docker run",
        "dotnet run",
        "npm run dev",
        "tauri dev",
        "gh pr",
        "git add",
        "git " + "commit",
        "git " + "push",
        "AcceptedApprovalRecord",
        "PolicySatisfied",
        "ControlledSourceApply",
        "ControlledRollback",
        "WorkflowContinuation",
        "ReleaseExecutor",
        "DeploymentExecutor"
    ];

    private static void AssertChildLogOnlyUsesCheckOnly(DoctorFixture fixture)
    {
        var log = File.Exists(fixture.ChildLogPath) ? File.ReadAllText(fixture.ChildLogPath) : string.Empty;
        StringAssert.Contains(log, "sql-local.ps1 -CheckOnly");
        StringAssert.Contains(log, "weaviate-local.ps1 -CheckOnly");
        AssertDoesNotContain(log, string.Concat("-", "Create"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "Rebuild"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "ApplyLocalDevSetup"), "child command log");
        AssertDoesNotContain(log, string.Concat("-", "EnsureSchema"), "child command log");
    }

    private static void AssertChildLogDoesNotContain(DoctorFixture fixture, string marker)
    {
        if (!File.Exists(fixture.ChildLogPath))
            return;

        AssertDoesNotContain(File.ReadAllText(fixture.ChildLogPath), marker, "child command log");
    }

    private static CommandResult RunDoctor(
        DoctorFixture fixture,
        params string[] arguments) =>
        RunDoctor(fixture, environment: null, fakeGitMode: "ignored", arguments);

    private static CommandResult RunDoctorWithGitMode(
        DoctorFixture fixture,
        string fakeGitMode,
        params string[] arguments) =>
        RunDoctor(fixture, environment: null, fakeGitMode: fakeGitMode, arguments);

    private static CommandResult RunDoctor(
        DoctorFixture fixture,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments) =>
        RunDoctor(fixture, environment, fakeGitMode: "ignored", arguments);

    private static CommandResult RunDoctor(
        DoctorFixture fixture,
        IReadOnlyDictionary<string, string?>? environment,
        string fakeGitMode,
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

        startInfo.Environment["PATH"] = fixture.FakeBinDirectory + Path.PathSeparator + startInfo.Environment["PATH"];
        startInfo.Environment["J07_CHILD_LOG"] = fixture.ChildLogPath;
        startInfo.Environment["J07_FAKE_GIT_MODE"] = fakeGitMode;

        if (environment is not null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.IsTrue(process.WaitForExit(60_000), "J07 developer doctor script timed out.");

        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
            Assert.Inconclusive("Could not inspect J07 changed files: " + stderr);

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

    private sealed class DoctorFixture : IDisposable
    {
        private DoctorFixture(string root)
        {
            Root = root;
            ScriptPath = Path.Combine(root, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
            FakeBinDirectory = Path.Combine(root, "fake-bin");
            ChildLogPath = Path.Combine(root, "child-checks.log");
            LocalOverridePath = Path.Combine(root, "IronDev.Api", "appsettings.Development.Local.json");
            LocalTestConfigPath = Path.Combine(root, "IronDev.Api", "appsettings.LocalTest.json");
        }

        public string Root { get; }
        public string ScriptPath { get; }
        public string FakeBinDirectory { get; }
        public string ChildLogPath { get; }
        public string LocalOverridePath { get; }
        public string LocalTestConfigPath { get; }

        public static DoctorFixture Create(bool includeLocalOverride = true)
        {
            var root = Path.Combine(Path.GetTempPath(), "IronDevJ07", Guid.NewGuid().ToString("N"));
            var fixture = new DoctorFixture(root);

            Directory.CreateDirectory(Path.Combine(root, "Scripts", "local"));
            Directory.CreateDirectory(Path.Combine(root, "tools", "localtest"));
            Directory.CreateDirectory(Path.Combine(root, "IronDev.Api"));
            Directory.CreateDirectory(Path.Combine(root, "IronDev.TauriShell", "tests"));
            Directory.CreateDirectory(Path.Combine(root, "IronDev.TauriShell", "node_modules"));
            Directory.CreateDirectory(Path.Combine(root, "IronDev.Core", "Configuration"));
            Directory.CreateDirectory(fixture.FakeBinDirectory);

            File.WriteAllText(Path.Combine(root, "IronDev.slnx"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Scripts", "local", "bootstrap-local.ps1"), "Write-Host 'bootstrap placeholder'");
            File.WriteAllText(Path.Combine(root, "tools", "localtest", "reset-localtest-data.ps1"), "Write-Host 'reset placeholder'");
            File.WriteAllText(Path.Combine(root, "tools", "localtest", "start-alpha-localtest.ps1"), "Write-Host 'start placeholder'");
            File.WriteAllText(Path.Combine(root, "tools", "localtest", "Invoke-LocalTestSmoke.ps1"), "Write-Host 'smoke placeholder'");
            File.WriteAllText(Path.Combine(root, "IronDev.TauriShell", "tests", "localtest-manual-smoke.spec.ts"), "test('placeholder', () => {});");
            File.WriteAllText(Path.Combine(root, "IronDev.Core", "Configuration", "RedactedConfigSummaryModels.cs"), string.Empty);
            File.WriteAllText(Path.Combine(root, "IronDev.Core", "Configuration", "RedactedConfigSummaryService.cs"), string.Empty);
            File.WriteAllText(
                Path.Combine(root, "IronDev.TauriShell", "package.json"),
                """
                {
                  "scripts": {
                    "dev": "vite",
                    "dev:localtest": "vite --mode localtest",
                    "build": "tsc --noEmit",
                    "test": "playwright test"
                  },
                  "devDependencies": {
                    "@playwright/test": "1.0.0"
                  }
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "IronDev.Api", "appsettings.Development.Local.example.json"),
                """
                {
                  "ConnectionStrings": {
                    "IronDeveloperDb": ""
                  }
                }
                """);

            if (includeLocalOverride)
                File.WriteAllText(fixture.LocalOverridePath, "{ \"Hidden\": \"local-override-secret\" }");

            fixture.WriteLocalTestConfig("IronDeveloper_Test", @"C:\IronDevTestWorkspaces", @"C:\IronDevTestLogs", dangerRealRepoWrites: false);
            File.Copy(RepositoryFile(ScriptRelativePath), fixture.ScriptPath);
            WriteChildCheckScript(Path.Combine(root, "Scripts", "local", "sql-local.ps1"), "sql-local.ps1");
            WriteChildCheckScript(Path.Combine(root, "Scripts", "local", "weaviate-local.ps1"), "weaviate-local.ps1");
            WriteFakeCommands(fixture.FakeBinDirectory);

            return fixture;
        }

        public void WriteLocalTestConfig(string databaseName, string workspaceRoot, string logsRoot, bool dangerRealRepoWrites)
        {
            File.WriteAllText(
                LocalTestConfigPath,
                $$"""
                {
                  "ConnectionStrings": {
                    "IronDeveloperDb": "Server=(localdb)\\MSSQLLocalDB;Database={{databaseName}};Integrated Security=True;"
                  },
                  "LocalTest": {
                    "WorkspaceRoot": "{{EscapeJson(workspaceRoot)}}",
                    "LogsRoot": "{{EscapeJson(logsRoot)}}",
                    "DangerRealRepoWritesEnabled": {{dangerRealRepoWrites.ToString().ToLowerInvariant()}}
                  }
                }
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private static string EscapeJson(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        private static void WriteChildCheckScript(string path, string label)
        {
            File.WriteAllText(
                path,
                $$"""
                param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArguments)
                if (-not [string]::IsNullOrWhiteSpace($env:J07_CHILD_LOG)) {
                    Add-Content -Path $env:J07_CHILD_LOG -Value "{{label}} $($RemainingArguments -join ' ')"
                }
                if ($RemainingArguments -contains "-CheckOnly") {
                    exit 0
                }
                exit 81
                """);
        }

        private static void WriteFakeCommands(string fakeBinDirectory)
        {
            foreach (var command in new[] { "dotnet", "node", "npm", "sqlcmd", "docker" })
            {
                File.WriteAllText(
                    Path.Combine(fakeBinDirectory, command + ".cmd"),
                    """
                    @echo off
                    if "%1"=="--version" echo 10.0.100
                    exit /b 0
                    """);
            }

            File.WriteAllText(
                Path.Combine(fakeBinDirectory, "git.cmd"),
                """
                @echo off
                echo %* | findstr /C:"rev-parse" >nul && exit /b 0
                echo %* | findstr /C:"check-ignore" >nul && if /I "%J07_FAKE_GIT_MODE%"=="ignored" exit /b 0
                echo %* | findstr /C:"check-ignore" >nul && exit /b 1
                echo %* | findstr /C:"ls-files" >nul && if /I "%J07_FAKE_GIT_MODE%"=="tracked" exit /b 0
                echo %* | findstr /C:"ls-files" >nul && exit /b 1
                exit /b 0
                """);
        }
    }
}
