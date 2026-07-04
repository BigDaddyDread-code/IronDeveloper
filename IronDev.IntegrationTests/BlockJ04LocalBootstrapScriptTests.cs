using System.Diagnostics;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("LocalBootstrap")]
public sealed class BlockJ04LocalBootstrapScriptTests
{
    private const string ScriptRelativePath = "Scripts/local/bootstrap-local.ps1";
    private const string LocalOverrideRelativePath = "IronDev.Api/appsettings.Development.Local.json";
    private const string LocalOverrideExampleRelativePath = "IronDev.Api/appsettings.Development.Local.example.json";
    private const string BoundaryStatement = "The local bootstrap script prepares local convenience. It is not evidence, approval, root safety proof, policy satisfaction, or permission to mutate source, SQL, Weaviate, evidence, or sandbox repositories.";

    [TestMethod]
    public void J04_ScriptExistsAndDocsReferenceIt()
    {
        Assert.IsTrue(File.Exists(RepositoryFile(ScriptRelativePath)), "J04 bootstrap script must exist.");

        var localDevelopment = ReadRepositoryFile("Docs/local-development.md");
        var inventory = ReadRepositoryFile("Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md");
        var receipt = ReadRepositoryFile("Docs/receipts/J04_LOCAL_BOOTSTRAP_SCRIPT.md");

        StringAssert.Contains(localDevelopment, ScriptRelativePath.Replace('/', '\\'));
        StringAssert.Contains(inventory, ScriptRelativePath);
        StringAssert.Contains(receipt, ScriptRelativePath);
        StringAssert.Contains(receipt, "Default invocation");
    }

    [TestMethod]
    public void J04_DefaultModeIsCheckOnlyAndDoesNotCreateOrRunBootstrapActions()
    {
        using var fixture = BootstrapFixture.Create();
        var result = RunBootstrap(fixture.ScriptPath, fixture.Root);

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        Assert.IsFalse(File.Exists(fixture.LocalOverridePath), "Check-only mode must not create local override files.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "artifacts")), "Check-only mode must not create artifact folders.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "logs")), "Check-only mode must not create log folders.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "ProjectData")), "Check-only mode must not create runtime data folders.");

        StringAssert.Contains(result.CombinedOutput, "Mode: CheckOnly");
        StringAssert.Contains(result.CombinedOutput, "Local override | Missing");
        StringAssert.Contains(result.CombinedOutput, "SQL bootstrap | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Weaviate bootstrap | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Config summary | Unavailable");
        StringAssert.Contains(result.CombinedOutput, "Root safety | NotEvaluated");
        AssertDoesNotContain(result.CombinedOutput, "dotnet restore", "check-only output");
        AssertDoesNotContain(result.CombinedOutput, "npm install completed", "check-only output");
    }

    [TestMethod]
    public void J04_CreateLocalOverrideDoesNotOverwriteExistingFile()
    {
        using var fixture = BootstrapFixture.Create();
        const string existingContents = "existing-local-override";
        File.WriteAllText(fixture.LocalOverridePath, existingContents);

        var result = RunBootstrap(fixture.ScriptPath, fixture.Root, "-Prepare", "-CreateLocalOverride", "-NonInteractive");

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        Assert.AreEqual(existingContents, File.ReadAllText(fixture.LocalOverridePath));
        StringAssert.Contains(result.CombinedOutput, "Local override | AlreadyPresent");
    }

    [TestMethod]
    public void J04_CreateLocalOverrideCopiesExampleOnlyWhenExplicit()
    {
        using var fixture = BootstrapFixture.Create();

        var checkOnly = RunBootstrap(fixture.ScriptPath, fixture.Root);
        Assert.AreEqual(0, checkOnly.ExitCode, checkOnly.CombinedOutput);
        Assert.IsFalse(File.Exists(fixture.LocalOverridePath), "Default check must not copy the local override example.");

        var prepare = RunBootstrap(fixture.ScriptPath, fixture.Root, "-Prepare", "-CreateLocalOverride", "-NonInteractive");

        Assert.AreEqual(0, prepare.ExitCode, prepare.CombinedOutput);
        Assert.IsTrue(File.Exists(fixture.LocalOverridePath), "Explicit prepare/create should copy the local override example.");
        Assert.AreEqual(File.ReadAllText(fixture.LocalOverrideExamplePath), File.ReadAllText(fixture.LocalOverridePath));
        StringAssert.Contains(prepare.CombinedOutput, "Local override | Created");
    }

    [TestMethod]
    public void J04_CheckOnlyOutputDoesNotEmitSensitiveValuesOrUserLocalPaths()
    {
        using var fixture = BootstrapFixture.Create();
        var hiddenValue = "hidden-j04-value";
        var fakeUserPath = string.Join(Path.DirectorySeparatorChar, "C:", "Users", "Robert", ".irondev", "logs");

        var result = RunBootstrap(
            fixture.ScriptPath,
            fixture.Root,
            environment: new Dictionary<string, string?>
            {
                [string.Concat("J04_FAKE_", "TOKEN")] = hiddenValue,
                ["J04_FAKE_PATH"] = fakeUserPath
            });

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        AssertDoesNotContain(result.CombinedOutput, hiddenValue, "check-only output");
        AssertDoesNotContain(result.CombinedOutput, "Robert", "check-only output");
        AssertDoesNotContain(result.CombinedOutput, fakeUserPath, "check-only output");
        AssertDoesNotContain(result.CombinedOutput, fixture.Root, "check-only output");
        AssertDoesNotContain(result.CombinedOutput, File.ReadAllText(fixture.LocalOverrideExamplePath), "check-only output");
    }

    [TestMethod]
    public void J04_ScriptContainsNoCommittedSecretsMachineNamesOrLocalPaths()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);
        var docs = string.Join(
            Environment.NewLine,
            ReadRepositoryFile("Docs/local-development.md"),
            ReadRepositoryFile("Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md"),
            ReadRepositoryFile("Docs/receipts/J04_LOCAL_BOOTSTRAP_SCRIPT.md"));
        var combined = string.Join(Environment.NewLine, script, docs);

        foreach (var marker in ForbiddenCommittedMarkers())
            AssertDoesNotContain(combined, marker, "J04 source/docs");
    }

    [TestMethod]
    public void J04_ScriptDoesNotContainForbiddenBootstrapCommands()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        foreach (var marker in ForbiddenBootstrapCommands())
            AssertDoesNotContain(script, marker, "J04 bootstrap script");

        StringAssert.Contains(script, "if ($RestoreDotNet)");
        StringAssert.Contains(script, "if ($InstallFrontend)");
        StringAssert.Contains(script, "if ($CreateLocalOverride)");
        StringAssert.Contains(script, "CheckOnly cannot be combined");
        StringAssert.Contains(script, "requires Prepare");
    }

    [TestMethod]
    public void J04_NonAuthorityLanguageIsPrintedAndDocumented()
    {
        using var fixture = BootstrapFixture.Create();
        var result = RunBootstrap(fixture.ScriptPath, fixture.Root);
        var localDevelopment = ReadRepositoryFile("Docs/local-development.md");
        var inventory = ReadRepositoryFile("Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md");

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, BoundaryStatement);
        StringAssert.Contains(localDevelopment, BoundaryStatement);
        StringAssert.Contains(inventory, BoundaryStatement);
        StringAssert.Contains(result.CombinedOutput, "SQL bootstrap | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Weaviate bootstrap | NotRun");
    }

    [TestMethod]
    public void J04_ReceiptStatesBootstrapBoundary()
    {
        var receipt = ReadRepositoryFile("Docs/receipts/J04_LOCAL_BOOTSTRAP_SCRIPT.md");

        StringAssert.Contains(receipt, "No switches defaults to check-only mode.");
        StringAssert.Contains(receipt, "It must not overwrite an existing local override.");
        StringAssert.Contains(receipt, "J04 must not fake a config summary.");
        StringAssert.Contains(receipt, "J04 must not fake root safety.");
        StringAssert.Contains(receipt, BoundaryStatement);
        StringAssert.Contains(receipt, "a green setup check is still not authority");
    }

    [TestMethod]
    public void J04_NoRuntimeAuthoritySurfaceAdded()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);
        var changedProductionFiles = CurrentChangedFiles()
            .Where(path =>
                path.StartsWith("IronDev.Core/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("tools/IronDev.Cli/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.TauriShell/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, changedProductionFiles.Length, "J04 must not add production/API/CLI/frontend runtime files: " + string.Join(", ", changedProductionFiles));

        foreach (var marker in RuntimeAuthorityMarkers())
            AssertDoesNotContain(script, marker, "J04 bootstrap script");
    }

    private static IReadOnlyList<string> ForbiddenCommittedMarkers() =>
    [
        "DESKTOP" + "-",
        "LAPTOP" + "-",
        "ROB" + "-PC",
        "ROB" + "-",
        "KFA" + "0H13",
        "SQL" + "EXPRESS",
        "Pass" + "word=",
        "Pwd" + "=",
        "User Id" + "=sa",
        "sk-" + "live",
        "ghp" + "_"
    ];

    private static IReadOnlyList<string> ForbiddenBootstrapCommands() =>
    [
        "sqlcmd",
        "docker compose",
        "weaviate-dev.ps1",
        "Database/local_dev_setup.sql",
        "Database\\local_dev_setup.sql",
        "CREATE DATABASE",
        "dotnet ef",
        "dotnet run",
        "npm run dev",
        "Start-Process",
        "gh pr",
        "git add",
        "git commit",
        "git push"
    ];

    private static IReadOnlyList<string> RuntimeAuthorityMarkers() =>
    [
        "ControllerBase",
        "WebApplication",
        "IHostedService",
        "BackgroundService",
        "AcceptedApprovalRecord",
        "PolicySatisfied",
        "ControlledSourceApply",
        "ControlledRollback",
        "WorkflowContinuation",
        "ReleaseExecutor",
        "DeploymentExecutor"
    ];

    private static CommandResult RunBootstrap(
        string scriptPath,
        string workingDirectory,
        params string[] arguments) =>
        RunBootstrap(scriptPath, workingDirectory, environment: null, arguments);

    private static CommandResult RunBootstrap(
        string scriptPath,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("powershell")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (environment is not null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.IsTrue(process.WaitForExit(60_000), "J04 bootstrap script timed out.");

        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private static IReadOnlyList<string> CurrentChangedFiles()
    {
        return GitFileList(["diff", "--name-only", "origin/main...HEAD"])
            .Concat(GitFileList(["diff", "--name-only"]))
            .Concat(GitFileList(["diff", "--cached", "--name-only"]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> GitFileList(IReadOnlyList<string> arguments)
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
            Assert.Inconclusive("Could not inspect J04 changed files: " + stderr);

        return stdout
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
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

    private sealed class BootstrapFixture : IDisposable
    {
        private BootstrapFixture(string root)
        {
            Root = root;
            ScriptPath = Path.Combine(root, "Scripts", "local", "bootstrap-local.ps1");
            LocalOverrideExamplePath = Path.Combine(root, LocalOverrideExampleRelativePath.Replace('/', Path.DirectorySeparatorChar));
            LocalOverridePath = Path.Combine(root, LocalOverrideRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public string Root { get; }
        public string ScriptPath { get; }
        public string LocalOverrideExamplePath { get; }
        public string LocalOverridePath { get; }

        public static BootstrapFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "IronDevJ04", Guid.NewGuid().ToString("N"));
            var fixture = new BootstrapFixture(root);

            Directory.CreateDirectory(Path.Combine(root, "Scripts", "local"));
            Directory.CreateDirectory(Path.Combine(root, "IronDev.Api"));
            Directory.CreateDirectory(Path.Combine(root, "IronDev.TauriShell"));
            File.WriteAllText(Path.Combine(root, "IronDev.slnx"), string.Empty);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "appsettings.Development.Local.json" + Environment.NewLine);
            File.WriteAllText(Path.Combine(root, "IronDev.TauriShell", "package.json"), "{ \"name\": \"j04-fixture\" }");
            File.WriteAllText(
                fixture.LocalOverrideExamplePath,
                """
                {
                  "ConnectionStrings": {
                    "IronDeveloperDb": ""
                  },
                  "Ai": {
                    "Provider": "",
                    "Model": "",
                    "ApiKey": ""
                  },
                  "Weaviate": {
                    "Enabled": false,
                    "HttpEndpoint": "",
                    "GrpcEndpoint": ""
                  }
                }
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
