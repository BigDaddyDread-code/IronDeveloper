using IronDev.Core.Configuration;

namespace IronDev.UnitTests;

[TestClass]
public sealed class EnvironmentConfigurationContractTests
{
    [TestMethod]
    public void LocalTest_requires_database_and_isolated_roots()
    {
        var result = EnvironmentConfigurationContract.Validate("LocalTest", Settings(("ConnectionStrings:IronDeveloperDb", "Server=(localdb);Database=IronDeveloper_Test"), ("Ai:Provider", "Fake"), ("LocalTest:WorkspaceRoot", "C:\\IronDevTestWorkspaces")));
        CollectionAssert.Contains(result.Issues.Select(issue => issue.Key).ToList(), "LocalTest:LogsRoot");
    }

    [TestMethod]
    public void Hosted_alpha_contract_requires_explicit_cors_origin()
    {
        var result = EnvironmentConfigurationContract.Validate("HostedAlpha", Settings(("ConnectionStrings:IronDeveloperDb", "Server=sql;Database=IronDevAlpha"), ("Ai:Provider", "Fake")));
        CollectionAssert.AreEquivalent(new[] { "Cors:AllowedOrigins" }, result.Issues.Select(issue => issue.Key).ToArray());
    }

    [TestMethod]
    public void Enabled_providers_require_their_critical_configuration()
    {
        var result = EnvironmentConfigurationContract.Validate("Development", Settings(("ConnectionStrings:IronDeveloperDb", "Server=.;Database=IronDevDev"), ("Weaviate:Enabled", "true"), ("Ai:Provider", "OpenAI")));
        CollectionAssert.AreEquivalent(new[] { "Weaviate:Endpoint", "Ai:ApiKey" }, result.Issues.Select(issue => issue.Key).ToArray());
    }

    [TestMethod]
    public void Unsafe_placeholder_is_rejected_instead_of_inferred()
    {
        var result = EnvironmentConfigurationContract.Validate("CI", Settings(("ConnectionStrings:IronDeveloperDb", "Server=YOUR_SERVER;Database=IronDev_CI"), ("Ai:Provider", "Fake")));
        Assert.AreEqual("UnsafePlaceholderConfiguration", result.Issues.Single().Code);
    }

    [TestMethod]
    public void Missing_or_unknown_provider_is_rejected_instead_of_using_runtime_fallbacks()
    {
        var missing = EnvironmentConfigurationContract.Validate("Development", Settings(("ConnectionStrings:IronDeveloperDb", "Server=.;Database=IronDevDev")));
        var unknown = EnvironmentConfigurationContract.Validate("Development", Settings(("ConnectionStrings:IronDeveloperDb", "Server=.;Database=IronDevDev"), ("Ai:Provider", "SurpriseProvider")));

        Assert.AreEqual("MissingCriticalConfiguration", missing.Issues.Single(issue => issue.Key == "Ai:Provider").Code);
        Assert.AreEqual("UnsupportedProviderConfiguration", unknown.Issues.Single(issue => issue.Key == "Ai:Provider").Code);
    }

    [TestMethod]
    public void Provider_secret_placeholders_and_invalid_boolean_flags_fail_closed()
    {
        var result = EnvironmentConfigurationContract.Validate("Development", Settings(
            ("ConnectionStrings:IronDeveloperDb", "Server=.;Database=IronDevDev"),
            ("Ai:Provider", "OpenAI"),
            ("OPENAI_API_KEY", "CHANGE_ME"),
            ("Weaviate:Enabled", "sometimes")));

        CollectionAssert.AreEquivalent(
            new[] { "UnsafePlaceholderConfiguration", "InvalidBooleanConfiguration" },
            result.Issues.Select(issue => issue.Code).ToArray());
    }

    [TestMethod]
    public void Hosted_profile_accepts_any_real_origin_and_unknown_environment_names_are_rejected()
    {
        var hosted = EnvironmentConfigurationContract.Validate("HostedAlpha", Settings(
            ("ConnectionStrings:IronDeveloperDb", "Server=sql;Database=IronDevAlpha"),
            ("Ai:Provider", "Fake"),
            ("Cors:AllowedOrigins:1", "https://alpha.irondev.example")));
        var unknown = EnvironmentConfigurationContract.Validate("HostedAlphaa", Settings(
            ("ConnectionStrings:IronDeveloperDb", "Server=sql;Database=IronDevAlpha"),
            ("Ai:Provider", "Fake")));

        Assert.IsTrue(hosted.IsValid);
        Assert.AreEqual("UnsupportedEnvironmentProfile", unknown.Issues.Single(issue => issue.Key == "ASPNETCORE_ENVIRONMENT").Code);
    }

    [TestMethod]
    public void Global_provider_contract_matches_runtime_dispatch_options()
    {
        Assert.AreEqual(0, EnvironmentConfigurationContract.ValidateAiProvider(Settings(("Ai:Provider", "Fake"))).Count);
        Assert.AreEqual(0, EnvironmentConfigurationContract.ValidateAiProvider(Settings(("Ai:Provider", "OpenAI"), ("Ai:ApiKey", "external-key"))).Count);
        Assert.AreEqual(0, EnvironmentConfigurationContract.ValidateAiProvider(Settings(("Ai:Provider", "LocalOpenAI"), ("Ai:BaseUrl", "http://localhost:11434"))).Count);
        Assert.AreEqual(0, EnvironmentConfigurationContract.ValidateAiProvider(Settings(("Ai:Provider", "Ollama"), ("Ai:BaseUrl", "http://localhost:11434"))).Count);
        Assert.AreEqual("UnsupportedProviderConfiguration", EnvironmentConfigurationContract.ValidateAiProvider(Settings(("Ai:Provider", "Custom"))).Single().Code);
    }

    private static IReadOnlyDictionary<string, string?> Settings(params (string Key, string? Value)[] values) => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);
}
