using System.Diagnostics;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("Weaviate")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class BlockJ06LocalWeaviateBootstrapCommandTests
{
    private const string ScriptRelativePath = "Scripts/local/weaviate-local.ps1";
    private const string J04ScriptRelativePath = "Scripts/local/bootstrap-local.ps1";
    private const string J05ScriptRelativePath = "Scripts/local/sql-local.ps1";
    private const string ReceiptRelativePath = "Docs/receipts/J06_LOCAL_WEAVIATE_BOOTSTRAP.md";
    private const string BoundaryStatement = "Local Weaviate state is a disposable derived index. Rebuilding it is setup convenience, not authority, approval, evidence, or readiness.";

    [TestMethod]
    public void J06_WeaviateLocalScript_ExistsAndIsDocumented()
    {
        Assert.IsTrue(File.Exists(RepositoryFile(ScriptRelativePath)), "J06 local Weaviate command must exist.");

        var localDevelopment = ReadRepositoryFile("Docs/local-development.md");
        var receipt = ReadRepositoryFile(ReceiptRelativePath);

        StringAssert.Contains(localDevelopment, ScriptRelativePath.Replace('/', '\\'));
        StringAssert.Contains(receipt, ScriptRelativePath);
        StringAssert.Contains(receipt, "Default invocation");
        StringAssert.Contains(receipt, BoundaryStatement);
    }

    [TestMethod]
    public void J06_DefaultMode_IsCheckOnlyAndNonMutating()
    {
        using var fixture = WeaviateLocalFixture.Create();
        var result = RunWeaviateLocal(fixture);

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "artifacts")), "Check-only mode must not write artifacts.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "logs")), "Check-only mode must not write logs.");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "evidence.json")), "Check-only mode must not write evidence.");

        StringAssert.Contains(result.CombinedOutput, "Mode: CheckOnly");
        StringAssert.Contains(result.CombinedOutput, "EnsureSchema | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Rebuild | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Demo import | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Service start | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Evidence write | NotRun");
        StringAssert.Contains(result.CombinedOutput, "Action | NotRun | CheckOnly");
        AssertDoesNotContain(result.CombinedOutput, "DELETE /v1/schema", "J06 check-only output");
        AssertDoesNotContain(result.CombinedOutput, "POST /v1/schema", "J06 check-only output");
    }

    [TestMethod]
    public void J06_CheckOnly_CannotCombineWithMutationModes()
    {
        using var fixture = WeaviateLocalFixture.Create();

        var ensure = RunWeaviateLocal(fixture, "-CheckOnly", "-EnsureSchema");
        Assert.AreNotEqual(0, ensure.ExitCode);
        StringAssert.Contains(ensure.CombinedOutput, "CheckOnly cannot be combined");

        var rebuild = RunWeaviateLocal(fixture, "-CheckOnly", "-Rebuild");
        Assert.AreNotEqual(0, rebuild.ExitCode);
        StringAssert.Contains(rebuild.CombinedOutput, "CheckOnly cannot be combined");

        var bothMutationModes = RunWeaviateLocal(fixture, "-EnsureSchema", "-Rebuild");
        Assert.AreNotEqual(0, bothMutationModes.ExitCode);
        StringAssert.Contains(bothMutationModes.CombinedOutput, "EnsureSchema and Rebuild are mutually exclusive");
    }

    [DataTestMethod]
    [DataRow("http://localhost:8080")]
    [DataRow("http://127.0.0.1:8080")]
    [DataRow("http://[::1]:8080")]
    public void J06_AllowsOnlyLoopbackEndpoints(string endpoint)
    {
        using var fixture = WeaviateLocalFixture.Create();
        var result = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", endpoint, "-CollectionName", "IronDeveloper_Local");

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "WeaviateEndpointLocal");
        StringAssert.Contains(result.CombinedOutput, "Action | NotRun | CheckOnly");
    }

    [DataTestMethod]
    [DataRow("https://cluster.weaviate.cloud", "WeaviateEndpointCloudRejected")]
    [DataRow("http://prod-weaviate:8080", "WeaviateEndpointRemoteRejected")]
    [DataRow("http://staging-weaviate:8080", "WeaviateEndpointRemoteRejected")]
    [DataRow("http://10.0.0.5:8080", "WeaviateEndpointRemoteRejected")]
    [DataRow("http://192.168.1.10:8080", "WeaviateEndpointRemoteRejected")]
    [DataRow("http://example.com:8080", "WeaviateEndpointRemoteRejected")]
    [DataRow("not-a-url", "WeaviateEndpointMalformedRejected")]
    public void J06_RejectsRemoteCloudAndMalformedEndpoints(string endpoint, string expectedReason)
    {
        using var fixture = WeaviateLocalFixture.Create();
        var result = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", endpoint, "-CollectionName", "IronDeveloper_Local");

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.CombinedOutput, expectedReason);
        AssertDoesNotContain(result.CombinedOutput, endpoint, "rejected endpoint output");
    }

    [TestMethod]
    public void J06_RejectsCredentialShapedEndpointWithoutPrintingIt()
    {
        using var fixture = WeaviateLocalFixture.Create();
        var endpoint = string.Concat("http://user:", "credential", "@localhost:8080");
        var result = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", endpoint, "-CollectionName", "IronDeveloper_Local");

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.CombinedOutput, "WeaviateEndpointCredentialRejected");
        AssertDoesNotContain(result.CombinedOutput, endpoint, "credential endpoint output");
        AssertDoesNotContain(result.CombinedOutput, "user:credential@", "credential endpoint output");
    }

    [DataTestMethod]
    [DataRow("IronDeveloper_Local")]
    [DataRow("IronDeveloper_Local_Rob")]
    [DataRow("IronDeveloper_Dev")]
    [DataRow("IronDeveloper_Test")]
    [DataRow("IronDeveloper_J06_Smoke")]
    public void J06_AllowsSafeLocalCollectionNames(string collectionName)
    {
        using var fixture = WeaviateLocalFixture.Create();
        var result = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", collectionName);

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "CollectionNameSafeLocal");
        StringAssert.Contains(result.CombinedOutput, collectionName);
    }

    [DataTestMethod]
    [DataRow("IronDeveloper_Prod")]
    [DataRow("IronDeveloper_Local_Prod1")]
    [DataRow("IronDeveloper_Test_Live")]
    [DataRow("IronDeveloper_UAT")]
    [DataRow("IronDeveloper_Acceptance")]
    [DataRow("IronDeveloper_Shared")]
    [DataRow("ProductionKnowledge")]
    [DataRow("CustomerKnowledge")]
    public void J06_RejectsProductionShapedCollectionNames(string collectionName)
    {
        using var fixture = WeaviateLocalFixture.Create();
        var result = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", collectionName);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.CombinedOutput, "CollectionNameProductionLikeRejected");
    }

    [TestMethod]
    public void J06_RebuildRequiresExactConfirmationPhrase()
    {
        using var fixture = WeaviateLocalFixture.Create();

        var missing = RunWeaviateLocal(fixture, "-Rebuild", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local");
        Assert.AreNotEqual(0, missing.ExitCode);
        StringAssert.Contains(missing.CombinedOutput, "RebuildConfirmationRejected");
        AssertDoesNotContain(missing.CombinedOutput, "WeaviateUnavailable", "missing confirmation output");

        var wrong = RunWeaviateLocal(fixture, "-Rebuild", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-ConfirmRebuild", "REBUILD IronDeveloper_Test");
        Assert.AreNotEqual(0, wrong.ExitCode);
        StringAssert.Contains(wrong.CombinedOutput, "RebuildConfirmationRejected");

        var mismatched = RunWeaviateLocal(fixture, "-Rebuild", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Test", "-ConfirmRebuild", "REBUILD IronDeveloper_Local");
        Assert.AreNotEqual(0, mismatched.ExitCode);
        StringAssert.Contains(mismatched.CombinedOutput, "RebuildConfirmationRejected");
    }

    [TestMethod]
    public void J06_RebuildWithExactPhrasePassesValidationButDoesNotWildcardDelete()
    {
        using var fixture = WeaviateLocalFixture.Create();
        var result = RunWeaviateLocal(fixture, "-Rebuild", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-ConfirmRebuild", "REBUILD IronDeveloper_Local");

        Assert.AreNotEqual(0, result.ExitCode, "No local Weaviate is expected in the test fixture.");
        StringAssert.Contains(result.CombinedOutput, "Rebuild | Requested");
        StringAssert.Contains(result.CombinedOutput, "WeaviateUnavailable");
        AssertDoesNotContain(result.CombinedOutput, "RebuildConfirmationRejected", "exact confirmation output");

        var script = ReadRepositoryFile(ScriptRelativePath);
        AssertDoesNotContain(script, "/v1/schema/*", "J06 script");
        AssertDoesNotContain(script, "DELETE /v1/schema", "J06 script");
        AssertDoesNotContain(script, "delete all", "J06 script");
        AssertDoesNotContain(script, "classes.Clear", "J06 script");
    }

    [TestMethod]
    public void J06_SchemaPath_AllowsRepositoryContainedSchema()
    {
        using var fixture = WeaviateLocalFixture.Create();
        var result = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-SchemaPath", "Schemas/local-weaviate.schema.json");

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        StringAssert.Contains(result.CombinedOutput, "SchemaPathPresent");
        AssertDoesNotContain(result.CombinedOutput, fixture.Root, "schema path output");
    }

    [TestMethod]
    public void J06_SchemaPath_RejectsUnsafeLocationsAndMissingFiles()
    {
        using var fixture = WeaviateLocalFixture.Create();

        var traversal = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-SchemaPath", @"..\outside.schema.json");
        Assert.AreNotEqual(0, traversal.ExitCode);
        StringAssert.Contains(traversal.CombinedOutput, "SchemaPathTempRejected");

        var userHome = string.Join(Path.DirectorySeparatorChar, "C:", "Users", "Example", "schema.json");
        var userHomeResult = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-SchemaPath", userHome);
        Assert.AreNotEqual(0, userHomeResult.ExitCode);
        StringAssert.Contains(userHomeResult.CombinedOutput, "SchemaPathUserHomeRejected");
        AssertDoesNotContain(userHomeResult.CombinedOutput, "Example", "user home schema path output");

        var tempPath = Path.Combine(Path.GetTempPath(), "irondev-j06-outside.schema.json");
        var tempResult = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-SchemaPath", tempPath);
        Assert.AreNotEqual(0, tempResult.ExitCode);
        StringAssert.Contains(tempResult.CombinedOutput, "SchemaPathTempRejected");

        var network = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-SchemaPath", @"\\server\share\schema.json");
        Assert.AreNotEqual(0, network.ExitCode);
        StringAssert.Contains(network.CombinedOutput, "SchemaPathNetworkShareRejected");

        var missing = RunWeaviateLocal(fixture, "-CheckOnly", "-Endpoint", "http://localhost:1", "-CollectionName", "IronDeveloper_Local", "-SchemaPath", "Schemas/missing.schema.json");
        Assert.AreNotEqual(0, missing.ExitCode);
        StringAssert.Contains(missing.CombinedOutput, "SchemaPathMissing");
    }

    [TestMethod]
    public void J06_ScriptDoesNotExposeForbiddenCredentialOrServiceParameters()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        foreach (var marker in ForbiddenParameterMarkers())
            AssertDoesNotContain(script, marker, "J06 script");
    }

    [TestMethod]
    public void J06_ScriptDoesNotStartDockerLoadDemoRunSmokeOrWriteEvidence()
    {
        var script = ReadRepositoryFile(ScriptRelativePath);

        foreach (var marker in ForbiddenRuntimeMarkers())
            AssertDoesNotContain(script, marker, "J06 script");
    }

    [TestMethod]
    public void J06_J04AndJ05DoNotInvokeWeaviateLocalMutationModes()
    {
        var j04 = ReadRepositoryFile(J04ScriptRelativePath);
        var j05 = ReadRepositoryFile(J05ScriptRelativePath);

        foreach (var source in new[] { j04, j05 })
        {
            AssertDoesNotContain(source, "weaviate-local.ps1", "existing local bootstrap scripts");
            AssertDoesNotContain(source, "-EnsureSchema", "existing local bootstrap scripts");
        }
    }

    [TestMethod]
    public void J06_ReceiptStatesDerivedIndexBoundary()
    {
        var receipt = ReadRepositoryFile(ReceiptRelativePath);

        StringAssert.Contains(receipt, BoundaryStatement);
        StringAssert.Contains(receipt, "J06 does not start Docker or Weaviate.");
        StringAssert.Contains(receipt, "J06 does not load demo, BookSeller, or product data.");
        StringAssert.Contains(receipt, "J06 does not write evidence.");
        StringAssert.Contains(receipt, "J06 does not claim alpha, merge, release, or deployment readiness.");
    }

    [TestMethod]
    public void J06_NoRuntimeAuthoritySurfaceAdded()
    {
        var changedProductionFiles = CurrentChangedFiles()
            .Where(path =>
                !IsJ10RootSafetyFile(path) &&
                !IsJ09StartupSafetyFile(path) &&
                (path.StartsWith("IronDev.Core/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("IronDev.Api/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("tools/IronDev.Cli/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("IronDev.TauriShell/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.AreEqual(0, changedProductionFiles.Length, "J06 must not add production/API/CLI/frontend/workflow runtime files: " + string.Join(", ", changedProductionFiles));
    }

    private static IReadOnlyList<string> ForbiddenParameterMarkers() =>
    [
        string.Concat("-", "Api", "Key"),
        string.Concat("-", "To", "ken"),
        string.Concat("-", "Pass", "word"),
        "-ConnectionString",
        "-AuthorizationHeader",
        "-CloudUrl",
        "-StartDocker",
        "-StartService",
        "-SeedDemo",
        "-RunSmoke"
    ];

    private static IReadOnlyList<string> ForbiddenRuntimeMarkers() =>
    [
        "docker compose",
        "docker run",
        "Start-Process",
        "Scripts/weaviate-dev.ps1",
        "weaviate-dev.ps1 up",
        "Invoke-BookSeller",
        "Start-BookSeller",
        "BookSellerMvp",
        "fixtures/bookseller",
        "alpha smoke",
        "gh pr",
        "git push",
        "git commit"
    ];

    private static CommandResult RunWeaviateLocal(WeaviateLocalFixture fixture, params string[] arguments)
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.IsTrue(process.WaitForExit(60_000), "J06 local Weaviate script timed out.");

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
            Assert.Inconclusive("Could not inspect J06 changed files: " + stderr);

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

    private sealed class WeaviateLocalFixture : IDisposable
    {
        private WeaviateLocalFixture(string root)
        {
            Root = root;
            ScriptPath = Path.Combine(root, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public string Root { get; }
        public string ScriptPath { get; }

        public static WeaviateLocalFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "IronDevJ06", Guid.NewGuid().ToString("N"));
            var fixture = new WeaviateLocalFixture(root);

            Directory.CreateDirectory(Path.Combine(root, "Scripts", "local"));
            Directory.CreateDirectory(Path.Combine(root, "Schemas"));
            File.WriteAllText(Path.Combine(root, "IronDev.slnx"), string.Empty);
            File.WriteAllText(
                Path.Combine(root, "Schemas", "local-weaviate.schema.json"),
                """
                {
                  "class": "IronDeveloper_Local",
                  "vectorizer": "none",
                  "properties": [
                    { "name": "sourceRef", "dataType": [ "text" ] },
                    { "name": "summary", "dataType": [ "text" ] }
                  ]
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
