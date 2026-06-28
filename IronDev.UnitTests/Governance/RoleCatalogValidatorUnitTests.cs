namespace IronDev.UnitTests.Governance;

[TestClass]
public sealed class RoleCatalogValidatorUnitTests
{
    [TestMethod]
    public void DefaultRoleCatalogValidatesInFastUnitLane()
    {
        var result = RoleCatalogValidator.ValidateCatalog(GovernanceValidatorTestFixtures.RoleCatalog());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog is descriptive only");
    }

    [TestMethod]
    public void UnknownRoleKindFailsClosed()
    {
        var result = RoleCatalogValidator.ValidateEntry(GovernanceValidatorTestFixtures.ReviewerRole() with
        {
            RoleKind = GovernanceRoleKind.Unknown
        });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleCatalogEntryRoleKindUnknown");
    }

    [TestMethod]
    public void UnsafeAuthorityShapedRoleTextIsRejected()
    {
        foreach (var unsafeText in new[] { "can approve", "can execute", "authority granted", "policy satisfied" })
        {
            var result = RoleCatalogValidator.ValidateEntry(GovernanceValidatorTestFixtures.ReviewerRole() with
            {
                Description = $"Bad role text says {unsafeText}."
            });

            Assert.IsFalse(result.IsValid, unsafeText);
            CollectionAssert.Contains(result.UnsafeRoleIds.ToList(), "role:f01:reviewer");
        }
    }

    [TestMethod]
    public void DuplicateRoleIdsFailValidation()
    {
        var result = RoleCatalogValidator.ValidateCatalog(GovernanceValidatorTestFixtures.Catalog(
            GovernanceValidatorTestFixtures.ReviewerRole(),
            GovernanceValidatorTestFixtures.ReviewerRole() with { DisplayName = "Second Reviewer" }));

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleCatalogDuplicateRoleId:role:f01:reviewer");
        CollectionAssert.Contains(result.DuplicateRoleIds.ToList(), "role:f01:reviewer");
    }

    [TestMethod]
    public void RoleCatalogMetadataDoesNotGrantAuthority()
    {
        var result = RoleCatalogValidator.ValidateCatalog(GovernanceValidatorTestFixtures.RoleCatalog());

        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not authorization");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not approval");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not mutation authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not workflow continuation authority");
    }

    [TestMethod]
    public void FastUnitProjectStillReferencesOnlyCoreAndMstest()
    {
        var projectPath = Path.Combine(
            GovernanceValidatorTestFixtures.RepoRoot(),
            "IronDev.UnitTests",
            "IronDev.UnitTests.csproj");
        var project = XDocument.Load(projectPath);

        var projectReferences = project.Descendants("ProjectReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var packageReferences = project.Descendants("PackageReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.AreEqual(new[] { @"..\IronDev.Core\IronDev.Core.csproj" }, projectReferences);
        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" },
            packageReferences);

        var projectText = File.ReadAllText(projectPath);
        foreach (var forbidden in new[] { "IronDev.Api", "IronDev.Cli", "IronDev.IntegrationTests", "IronDev.Infrastructure", "SqlConnection", "DbContext", "HttpClient", "Testcontainers" })
        {
            Assert.IsFalse(projectText.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }
}
