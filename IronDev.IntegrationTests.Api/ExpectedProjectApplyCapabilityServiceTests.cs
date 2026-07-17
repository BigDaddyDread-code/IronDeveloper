using IronDev.Core.RunReadiness;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class ExpectedProjectApplyCapabilityServiceTests
{
    [TestMethod]
    public async Task AllowsOnlyRegisteredProject_WithOneStableEvidenceHash()
    {
        var fixture = new ExpectedProjectApplyCapabilityService();

        var beforeRegistration = await fixture.EvaluateAsync(41);
        Assert.IsFalse(beforeRegistration.IsReady);
        Assert.AreEqual(
            ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch,
            beforeRegistration.ReasonCode);

        var registeredHash = ExpectedProjectApplyCapabilityService.CreateReadinessEvidenceHash(41, "fixture-contract-a");
        fixture.ExpectProject(41, registeredHash, @"C:\fixture-sandbox\project", @"C:\fixture-sandbox");
        var first = await fixture.EvaluateAsync(41);
        var second = await fixture.EvaluateAsync(41);
        var anotherProject = await fixture.EvaluateAsync(42);

        Assert.IsTrue(first.IsReady);
        Assert.AreEqual(registeredHash, first.ReadinessEvidenceHash);
        Assert.AreEqual(first.ReadinessEvidenceHash, second.ReadinessEvidenceHash);
        Assert.IsFalse(anotherProject.IsReady);
        Assert.AreEqual(
            ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch,
            anotherProject.ReasonCode);
    }

    [TestMethod]
    public async Task ChangedEvidenceHash_PoisonsFixtureFailClosed()
    {
        var fixture = new ExpectedProjectApplyCapabilityService();
        fixture.ExpectProject(41,
            ExpectedProjectApplyCapabilityService.CreateReadinessEvidenceHash(41, "fixture-contract-a"),
            @"C:\fixture-sandbox\project", @"C:\fixture-sandbox");
        Assert.IsTrue((await fixture.EvaluateAsync(41)).IsReady);

        Assert.ThrowsException<InvalidOperationException>(() =>
            fixture.ExpectProject(41,
                ExpectedProjectApplyCapabilityService.CreateReadinessEvidenceHash(41, "fixture-contract-b"),
                @"C:\fixture-sandbox\project", @"C:\fixture-sandbox"));

        var afterHashChange = await fixture.EvaluateAsync(41);
        Assert.IsFalse(afterHashChange.IsReady);
        Assert.AreEqual(
            ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch,
            afterHashChange.ReasonCode);
    }

    [TestMethod]
    public async Task MissingHashOrConflictingSecondProject_PoisonsFixtureFailClosed()
    {
        var missingHash = new ExpectedProjectApplyCapabilityService();
        Assert.ThrowsException<InvalidOperationException>(() => missingHash.ExpectProject(
            41, string.Empty, @"C:\fixture-sandbox\project", @"C:\fixture-sandbox"));
        Assert.IsFalse((await missingHash.EvaluateAsync(41)).IsReady);

        var conflictingProject = new ExpectedProjectApplyCapabilityService();
        conflictingProject.ExpectProject(41,
            ExpectedProjectApplyCapabilityService.CreateReadinessEvidenceHash(41, "fixture-contract-a"),
            @"C:\fixture-sandbox\project", @"C:\fixture-sandbox");
        Assert.ThrowsException<InvalidOperationException>(() =>
            conflictingProject.ExpectProject(42,
                ExpectedProjectApplyCapabilityService.CreateReadinessEvidenceHash(42, "fixture-contract-a"),
                @"C:\fixture-sandbox\other", @"C:\fixture-sandbox"));

        Assert.IsFalse((await conflictingProject.EvaluateAsync(41)).IsReady);
        Assert.IsFalse((await conflictingProject.EvaluateAsync(42)).IsReady);
    }
}
