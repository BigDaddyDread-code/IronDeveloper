using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendConfigurationDependencyTests
{
    private const string InventoryPath = "Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md";

    [TestMethod]
    public void BackendConfigurationDependencyInventory_DocumentsRequiredSectionsAndInvariants()
    {
        var inventory = ReadRepositoryFileByRelativePath(InventoryPath);

        foreach (var expected in new[]
        {
            "PR 55 is configuration/dependency cleanup, not capability expansion.",
            "No new capability introduced.",
            "No SQL/schema/proc shape/API/CLI/UI/runtime/persistence behaviour changes.",
            "## Configuration files",
            "## Backend configuration keys",
            "## DI registration inventory",
            "## Service lifetime review",
            "## Package reference inventory",
            "## External service clients",
            "## Test-only registrations and fixtures",
            "## Known configuration/dependency debt",
            "SQL remains source of truth.",
            "Vector/index/retrieval remains lookup only.",
            "Proposal is not apply.",
            "Candidate is not memory.",
            "Retrieval match is not memory candidate.",
            "Audit is not approval.",
            "Gate is not executor.",
            "Critic is not governance.",
            "Memory safe is not approval.",
            "Tool request is request form, not execution permission.",
            "Model output is advisory only.",
            "Human review remains required for source apply and memory promotion."
        })
        {
            StringAssert.Contains(inventory, expected);
        }
    }

    [TestMethod]
    public void BackendConfigurationDependencyInventory_DocumentsConfigKeysDiPackagesAndDebt()
    {
        var inventory = ReadRepositoryFileByRelativePath(InventoryPath);

        foreach (var expected in new[]
        {
            "`ConnectionStrings:IronDeveloperDb`",
            "`Jwt:Key`",
            "`Jwt:Issuer`",
            "`Jwt:Audience`",
            "`Jwt:ExpiryMinutes`",
            "`CodeProposal:Mode`",
            "`Ai:Provider`",
            "`Ai:Model`",
            "`Ai:ApiKey`",
            "`OPENAI_API_KEY`",
            "`LOCAL_OPENAI_API_KEY`",
            "`LocalTest:WeaviatePrefix`",
            "`LocalTest:WorkspaceRoot`",
            "`LocalTest:LogsRoot`",
            "`LocalTest:DangerRealRepoWritesEnabled`",
            "`DisposableBuild:WorkspaceRoot`",
            "`DisposableBuild:EvidenceRoot`",
            "`Embedding:*`",
            "`Weaviate:*`",
            "`SemanticRanking:*`",
            "`IronDev.Api/Program.cs`",
            "`IManualIndependentCriticAgentService` -> `ManualIndependentCriticAgentService`",
            "`IManualMemoryImprovementAgentService` -> `ManualMemoryImprovementAgentService`",
            "`ManualAgentExecutionStoreValidator`",
            "`IStoredManualIndependentCriticAgentService` -> `StoredManualIndependentCriticAgentService`",
            "`IStoredManualMemoryImprovementAgentService` -> `StoredManualMemoryImprovementAgentService`",
            "`IronDev.Core/IronDev.Core.csproj`",
            "`IronDev.Infrastructure/IronDev.Infrastructure.csproj`",
            "`IronDev.Api/IronDev.Api.csproj`",
            "`IronDev.IntegrationTests/IronDev.IntegrationTests.csproj`",
            "`IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj`",
            "Broad governance/memory/architecture lanes still fail in full solution runs",
            "Legacy runtime DDL/bootstrap ownership exceptions",
            "Uncertain package references",
            "Uncertain config keys"
        })
        {
            StringAssert.Contains(inventory, expected);
        }
    }

    [TestMethod]
    public void BackendConfigurationDependency_ApiProgramRegistersStoredManualDependencies()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        foreach (var expected in new[]
        {
            "builder.Services.AddScoped<IManualIndependentCriticAgentService, ManualIndependentCriticAgentService>();",
            "builder.Services.AddScoped<IManualMemoryImprovementAgentService, ManualMemoryImprovementAgentService>();",
            "builder.Services.AddScoped<ManualAgentExecutionStoreValidator>();",
            "builder.Services.AddScoped<IStoredManualIndependentCriticAgentService, StoredManualIndependentCriticAgentService>();",
            "builder.Services.AddScoped<IStoredManualMemoryImprovementAgentService, StoredManualMemoryImprovementAgentService>();"
        })
        {
            StringAssert.Contains(program, expected);
        }
    }

    [TestMethod]
    public void BackendConfigurationDependency_StoredManualServicesConstructWithRegisteredDependencies()
    {
        var services = new ServiceCollection();
        services.AddScoped<IManualIndependentCriticAgentService, ManualIndependentCriticAgentService>();
        services.AddScoped<IManualMemoryImprovementAgentService, ManualMemoryImprovementAgentService>();
        services.AddScoped<ManualAgentExecutionStoreValidator>();
        services.AddScoped<IAgentRunAuditEnvelopeStore, TestAgentRunAuditEnvelopeStore>();
        services.AddScoped<IStoredManualIndependentCriticAgentService, StoredManualIndependentCriticAgentService>();
        services.AddScoped<IStoredManualMemoryImprovementAgentService, StoredManualMemoryImprovementAgentService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();
        Assert.IsInstanceOfType<StoredManualIndependentCriticAgentService>(
            scope.ServiceProvider.GetRequiredService<IStoredManualIndependentCriticAgentService>());
        Assert.IsInstanceOfType<StoredManualMemoryImprovementAgentService>(
            scope.ServiceProvider.GetRequiredService<IStoredManualMemoryImprovementAgentService>());
    }

    [TestMethod]
    public void BackendConfigurationDependency_DoesNotAddEndpointRuntimeSchemaOrCapabilityTokens()
    {
        var inventory = ReadRepositoryFileByRelativePath(InventoryPath);
        var forbidden = new[]
        {
            "HttpPost",
            "ControllerBase",
            "WebApplication",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.WriteAllText",
            "NOCHECK CONSTRAINT",
            "DISABLE TRIGGER",
            "CREATE OR ALTER PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "SubmitReview"
        };

        foreach (var token in forbidden)
        {
            Assert.IsFalse(
                inventory.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"PR55 configuration/dependency docs/tests must not introduce runtime/schema/capability token: {token}");
        }
    }

    [TestMethod]
    public void BackendConfigurationDependencyInventory_DoesNotContainHiddenOrBidirectionalUnicode()
    {
        var absolutePath = Path.Combine(RepositoryRoot(), InventoryPath);

        AssertAsciiBytesAndNoBom(InventoryPath, File.ReadAllBytes(absolutePath));
        AssertAsciiAndNoFormatControls(InventoryPath, File.ReadAllText(absolutePath));
    }

    private sealed class TestAgentRunAuditEnvelopeStore : IAgentRunAuditEnvelopeStore
    {
        public AgentRunAuditEnvelopeAppendResult Append(AgentRunAuditEnvelope envelope, DateTimeOffset appendedAtUtc) =>
            new()
            {
                Status = AgentRunAuditEnvelopeAppendStatus.Appended,
                AgentRunId = envelope.Run.AgentRunId,
                EnvelopeSha256 = new string('a', 64),
                Issues = []
            };
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string ReadRepositoryFileByRelativePath(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static void AssertAsciiAndNoFormatControls(string path, string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(current);
            if (current > 127 || category == System.Globalization.UnicodeCategory.Format)
                Assert.Fail($"{path} contains hidden or non-ASCII Unicode at index {index}: U+{(int)current:X4}.");
        }
    }

    private static void AssertAsciiBytesAndNoBom(string path, byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            Assert.Fail($"{path} must not contain a UTF-8 byte-order mark.");

        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 127)
                Assert.Fail($"{path} contains non-ASCII byte at offset {index}: 0x{bytes[index]:X2}.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}
