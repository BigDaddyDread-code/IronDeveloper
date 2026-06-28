namespace IronDev.UnitTests.Governance;

[TestClass]
public sealed class ForbiddenActionCatalogValidatorUnitTests
{
    [TestMethod]
    public void DefaultForbiddenActionCatalogValidatesInFastUnitLane()
    {
        var result = ForbiddenActionCatalogValidator.ValidateCatalog(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.ForbiddenActionCatalog());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        Assert.AreEqual(0, result.UnsafeRefs.Count);
    }

    [TestMethod]
    public void AllowShapedTextIsRejected()
    {
        var hostile = string.Concat("role grants ", "mutation");
        var result = ForbiddenActionCatalogValidator.ValidateEntry(
            GovernanceValidatorTestFixtures.ForbiddenActionEntry(
                GovernanceValidatorTestFixtures.Role(GovernanceRoleKind.Requester),
                RoleForbiddenActionKind.AccessGrant) with
            {
                BoundaryStatement = hostile
            });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "BoundaryStatementUnsafe");
        CollectionAssert.Contains(result.UnsafeRefs.ToList(), hostile);
    }

    [TestMethod]
    public void IsAllowedTrueFailsValidation()
    {
        var result = ForbiddenActionCatalogValidator.ValidateEntry(
            GovernanceValidatorTestFixtures.ForbiddenActionEntry(
                GovernanceValidatorTestFixtures.Role(GovernanceRoleKind.Requester),
                RoleForbiddenActionKind.AccessGrant) with
            {
                IsAllowed = true
            });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "IsAllowedMustBeFalse");
    }

    [TestMethod]
    public void GrantsAuthorityTrueFailsValidation()
    {
        var result = ForbiddenActionCatalogValidator.ValidateEntry(
            GovernanceValidatorTestFixtures.ForbiddenActionEntry(
                GovernanceValidatorTestFixtures.Role(GovernanceRoleKind.Requester),
                RoleForbiddenActionKind.AccessGrant) with
            {
                GrantsAuthority = true
            });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "GrantsAuthorityMustBeFalse");
    }

    [TestMethod]
    public void MissingKnownRoleFailsValidation()
    {
        var result = ForbiddenActionCatalogValidator.ValidateCatalog(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.ForbiddenActionCatalog(
                GovernanceValidatorTestFixtures.ForbiddenActionEntry(
                    GovernanceValidatorTestFixtures.Role(GovernanceRoleKind.Requester),
                    RoleForbiddenActionKind.AccessGrant)));

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.StartsWith("ForbiddenActionCatalogMissingRole:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DuplicateRoleActionPairFailsValidation()
    {
        var role = GovernanceValidatorTestFixtures.Role(GovernanceRoleKind.Requester);
        var result = ForbiddenActionCatalogValidator.ValidateCatalog(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.ForbiddenActionCatalog(
                GovernanceValidatorTestFixtures.ForbiddenActionEntry(role, RoleForbiddenActionKind.AccessGrant),
                GovernanceValidatorTestFixtures.ForbiddenActionEntry(role, RoleForbiddenActionKind.AccessGrant)));

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(
            result.Issues.ToList(),
            "ForbiddenActionCatalogDuplicateRoleAction:role:f01:requester|AccessGrant");
    }

    [TestMethod]
    public void CatalogOmissionDoesNotMeanAllowed()
    {
        var requester = GovernanceValidatorTestFixtures.Role(GovernanceRoleKind.Requester);
        var decision = new ForbiddenActionCatalogService().Lookup(
            GovernanceValidatorTestFixtures.RoleCatalog(),
            GovernanceValidatorTestFixtures.ForbiddenActionCatalog(),
            new ForbiddenActionLookupRequest
            {
                CorrelationId = "correlation-g02",
                RequestedRoleId = requester.RoleId,
                RequestedActionKind = RoleForbiddenActionKind.RouteGuardCreation,
                AuthoritySourceKind = ForbiddenActionAuthoritySourceKind.RoleEvidence,
                RoleCatalogEvidenceRef = "role-catalog:g02",
                ForbiddenActionCatalogEvidenceRef = "forbidden-action-catalog:g02"
            });

        Assert.AreEqual(ForbiddenActionLookupClassification.NoCatalogGrantSeparateAuthorityRequired, decision.Classification);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void CatalogEntryAuthorityFlagsMustRemainFalse()
    {
        var entry = GovernanceValidatorTestFixtures.ForbiddenActionEntry(
            GovernanceValidatorTestFixtures.Role(GovernanceRoleKind.Requester),
            RoleForbiddenActionKind.AccessGrant);

        Assert.IsFalse(entry.IsAllowed);
        Assert.IsFalse(entry.GrantsAuthority);
        Assert.IsFalse(entry.GrantsPermission);
        Assert.IsFalse(entry.SatisfiesPolicy);
        Assert.IsFalse(entry.AllowsExecution);
        Assert.IsFalse(entry.AllowsMutation);
        Assert.IsFalse(entry.AllowsWorkflowContinuation);
        Assert.IsFalse(entry.AllowsRelease);
        Assert.IsFalse(entry.AllowsDeployment);
        Assert.IsFalse(entry.BypassesRedaction);
        Assert.IsFalse(entry.DisclosesSecrets);
        Assert.IsFalse(entry.DisclosesCredentials);
        Assert.IsFalse(entry.DisclosesRawPayload);
        Assert.IsFalse(entry.DisclosesPrivateReasoning);
    }

    private static void AssertDecisionAuthorityFlagsFalse(ForbiddenActionLookupDecision decision)
    {
        Assert.IsFalse(decision.IsAllowed);
        Assert.IsFalse(decision.GrantsAuthority);
        Assert.IsFalse(decision.GrantsPermission);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.AllowsExecution);
        Assert.IsFalse(decision.AllowsMutation);
        Assert.IsFalse(decision.AllowsWorkflowContinuation);
        Assert.IsFalse(decision.AllowsMerge);
        Assert.IsFalse(decision.AllowsRelease);
        Assert.IsFalse(decision.AllowsDeployment);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesSecrets);
        Assert.IsFalse(decision.DisclosesCredentials);
        Assert.IsFalse(decision.DisclosesRawPayload);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }
}
