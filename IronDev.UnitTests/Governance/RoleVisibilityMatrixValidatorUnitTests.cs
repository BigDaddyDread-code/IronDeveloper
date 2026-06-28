namespace IronDev.UnitTests.Governance;

[TestClass]
public sealed class RoleVisibilityMatrixValidatorUnitTests
{
    [TestMethod]
    public void DefaultRoleVisibilityMatrixValidatesInFastUnitLane()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.VisibilityMatrix());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix is descriptive only");
    }

    [TestMethod]
    public void UnknownRoleFailsValidation()
    {
        var result = RoleVisibilityMatrixValidator.ValidateEntry(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.VisibilityEntry() with { RoleId = "role:f01:not-in-catalog" });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleVisibilityMatrixEntryUnknownRoleId:role:f01:not-in-catalog");
        CollectionAssert.Contains(result.UnknownRoleIds.ToList(), "role:f01:not-in-catalog");
    }

    [TestMethod]
    public void UnknownSurfaceMaterialAndVisibilityLevelFailClosed()
    {
        var catalog = GovernanceValidatorTestFixtures.RoleCatalog();
        var cases = new (RoleVisibilityMatrixEntry Entry, string Issue)[]
        {
            (GovernanceValidatorTestFixtures.VisibilityEntry() with { Surface = RoleVisibilitySurface.Unknown }, "RoleVisibilityMatrixEntrySurfaceUnknown"),
            (GovernanceValidatorTestFixtures.VisibilityEntry() with { MaterialKind = RoleVisibilityMaterialKind.Unknown }, "RoleVisibilityMatrixEntryMaterialKindUnknown"),
            (GovernanceValidatorTestFixtures.VisibilityEntry() with { VisibilityLevel = RoleVisibilityLevel.Unknown }, "RoleVisibilityMatrixEntryVisibilityLevelUnknown")
        };

        foreach (var testCase in cases)
        {
            var result = RoleVisibilityMatrixValidator.ValidateEntry(catalog, testCase.Entry);

            Assert.IsFalse(result.IsValid, testCase.Issue);
            CollectionAssert.Contains(result.Issues.ToList(), testCase.Issue);
        }
    }

    [TestMethod]
    public void DuplicateRoleSurfaceMaterialFailsValidation()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.Matrix(
                GovernanceValidatorTestFixtures.VisibilityEntry(),
                GovernanceValidatorTestFixtures.VisibilityEntry()));

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(
            result.Issues.ToList(),
            "RoleVisibilityMatrixDuplicateKey:role:f01:reviewer|Proposal|ProposalSummary");
    }

    [TestMethod]
    public void RawCredentialAndPrivateMaterialCannotBeVisible()
    {
        var catalog = GovernanceValidatorTestFixtures.RoleCatalog();
        var cases = new[]
        {
            RoleVisibilityMaterialKind.RawPayload,
            RoleVisibilityMaterialKind.CredentialMaterial,
            RoleVisibilityMaterialKind.PrivateReasoning
        };

        foreach (var material in cases)
        {
            var result = RoleVisibilityMatrixValidator.ValidateEntry(catalog, GovernanceValidatorTestFixtures.VisibilityEntry() with
            {
                MaterialKind = material,
                SensitivityKind = material switch
                {
                    RoleVisibilityMaterialKind.RawPayload => RoleVisibilitySensitivityKind.RawPayload,
                    RoleVisibilityMaterialKind.CredentialMaterial => RoleVisibilitySensitivityKind.CredentialLike,
                    _ => RoleVisibilitySensitivityKind.PrivateReasoning
                },
                VisibilityLevel = RoleVisibilityLevel.SummaryOnly,
                RequiresRedaction = true,
                RequiresSeparatePolicyDecision = true
            });

            Assert.IsFalse(result.IsValid, material.ToString());
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("OverexposedMaterial", StringComparison.Ordinal)), material.ToString());
        }
    }

    [TestMethod]
    public void MissingSeparateRoleAssignmentOrVisibilityDecisionFailsClosed()
    {
        var catalog = GovernanceValidatorTestFixtures.RoleCatalog();
        var cases = new (RoleVisibilityMatrixEntry Entry, string Issue)[]
        {
            (GovernanceValidatorTestFixtures.VisibilityEntry() with { RequiresSeparateRoleAssignment = false }, "RoleVisibilityMatrixEntrySeparateRoleAssignmentRequired"),
            (GovernanceValidatorTestFixtures.VisibilityEntry() with { RequiresSeparateVisibilityDecision = false }, "RoleVisibilityMatrixEntrySeparateVisibilityDecisionRequired")
        };

        foreach (var testCase in cases)
        {
            var result = RoleVisibilityMatrixValidator.ValidateEntry(catalog, testCase.Entry);

            Assert.IsFalse(result.IsValid, testCase.Issue);
            CollectionAssert.Contains(result.Issues.ToList(), testCase.Issue);
        }
    }

    [TestMethod]
    public void VisibilityMatrixMetadataDoesNotGrantActionAuthority()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.VisibilityMatrix());

        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not access control");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not approval");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not mutation authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not workflow continuation authority");
    }
}
