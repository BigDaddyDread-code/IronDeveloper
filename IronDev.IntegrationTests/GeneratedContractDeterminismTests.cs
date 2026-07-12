using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class GeneratedContractDeterminismTests
{
    [TestMethod]
    public void Generator_IsPinnedTwiceGeneratedAndCiEnforced()
    {
        var root = FindRepositoryRoot();
        var generator = Read(root, "tools", "contracts", "update-openapi-contract.ps1");
        var ci = Read(root, "Scripts", "ci", "run-frontend-contract-ci.ps1");
        var package = JsonDocument.Parse(Read(root, "IronDev.TauriShell", "package.json"));
        var sdk = JsonDocument.Parse(Read(root, "global.json"));

        StringAssert.Contains(generator, "[switch]$VerifyDeterminism");
        StringAssert.Contains(generator, "Regenerate OpenAPI and TypeScript contracts again");
        StringAssert.Contains(generator, "$firstOpenApiHash -ne $secondOpenApiHash");
        StringAssert.Contains(generator, "$firstTypesHash -ne $secondTypesHash");
        StringAssert.Contains(generator, "Generated API contracts are nondeterministic");
        StringAssert.Contains(ci, "-Check -VerifyDeterminism");

        var rootElement = package.RootElement;
        Assert.AreEqual("npm@11.13.0", rootElement.GetProperty("packageManager").GetString());
        Assert.AreEqual("7.13.0", rootElement.GetProperty("devDependencies").GetProperty("openapi-typescript").GetString());
        Assert.AreEqual("10.0.301", sdk.RootElement.GetProperty("sdk").GetProperty("version").GetString());
        Assert.AreEqual("disable", sdk.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString());
    }

    [TestMethod]
    public void PlannedRoutes_AreExplicit501Contracts()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(Read(root, "IronDev.TauriShell", "openapi", "irondev-api.openapi.json"));
        var paths = document.RootElement.GetProperty("paths");

        AssertExplicit501(paths.GetProperty("/api/tenants/{tenantId}/users/invite").GetProperty("post"));
        AssertExplicit501(paths.GetProperty("/api/projects/{projectId}/authority/intervention-dial").GetProperty("get"));
    }

    private static void AssertExplicit501(JsonElement operation)
    {
        var responses = operation.GetProperty("responses");
        Assert.IsTrue(responses.TryGetProperty("501", out var response));
        Assert.IsFalse(responses.TryGetProperty("200", out _));
        var schema = response.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        StringAssert.EndsWith(schema.GetProperty("$ref").GetString(), "/PlannedSurfaceEnvelope");
    }

    private static string Read(string root, params string[] path) =>
        File.ReadAllText(Path.Combine([root, .. path]));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
