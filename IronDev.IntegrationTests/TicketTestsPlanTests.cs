using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Agent.ViewModels.Workspaces;

namespace IronDev.IntegrationTests;

/// <summary>
/// Unit tests for the TicketsWorkspaceViewModel Tests tab:
/// - TechnicalNotes serialization/deserialization into sub-fields
/// - Backward compatibility (legacy plain-text TechnicalNotes)
/// - Round-trip save/load
/// - Build This still works after Tests tab is populated
/// No DB, no LLM, no DI required.
/// </summary>
[TestClass]
public class TicketTestsPlanTests
{
    private static TicketsWorkspaceViewModel CreateVm()
        => new(null!, null!, new StubOrchestrator());

    // ── Parsing: structured TechnicalNotes → sub-fields ─────────────────────

    [TestMethod]
    [Description("Structured TechnicalNotes with all sections parse correctly.")]
    public void SyncTechnicalNotesToTests_AllSections_ParsedCorrectly()
    {
        var vm = CreateVm();
        vm.EditTechnicalNotes =
            "## Unit Tests\nVerify Foo returns true.\n" +
            "## Integration Tests\nEnd-to-end login flow.\n" +
            "## UI / Manual Tests\nOpen chat screen.\n" +
            "## Regression Tests\nExisting tests still pass.\n" +
            "## Build Validation\ndotnet build passes.";

        Assert.AreEqual("Verify Foo returns true.",    vm.EditTestsUnitTests);
        Assert.AreEqual("End-to-end login flow.",      vm.EditTestsIntegrationTests);
        Assert.AreEqual("Open chat screen.",           vm.EditTestsManualTests);
        Assert.AreEqual("Existing tests still pass.",  vm.EditTestsRegressionTests);
        Assert.AreEqual("dotnet build passes.",        vm.EditTestsBuildValidation);
    }

    [TestMethod]
    [Description("Legacy TechnicalNotes without section headers appears under Unit Tests only.")]
    public void SyncTechnicalNotesToTests_LegacyPlainText_GoesToUnitTests()
    {
        var vm = CreateVm();
        vm.EditTechnicalNotes = "Run the existing test suite.";

        Assert.AreEqual("Run the existing test suite.", vm.EditTestsUnitTests);
        Assert.AreEqual(string.Empty, vm.EditTestsIntegrationTests);
        Assert.AreEqual(string.Empty, vm.EditTestsManualTests);
        Assert.AreEqual(string.Empty, vm.EditTestsRegressionTests);
        Assert.AreEqual(string.Empty, vm.EditTestsBuildValidation);
    }

    [TestMethod]
    [Description("Empty TechnicalNotes clears all sub-fields.")]
    public void SyncTechnicalNotesToTests_EmptyValue_ClearsSubFields()
    {
        var vm = CreateVm();
        vm.EditTechnicalNotes = "## Unit Tests\nSomething.";
        vm.EditTechnicalNotes = string.Empty;

        Assert.AreEqual(string.Empty, vm.EditTestsUnitTests);
        Assert.AreEqual(string.Empty, vm.EditTestsIntegrationTests);
    }

    // ── Serialization: sub-fields → TechnicalNotes ───────────────────────────

    [TestMethod]
    [Description("Setting EditTestsUnitTests updates EditTechnicalNotes.")]
    public void EditTestsUnitTests_SetValue_UpdatesTechnicalNotes()
    {
        var vm = CreateVm();
        vm.EditTestsUnitTests = "Assert Foo is true.";

        StringAssert.Contains(vm.EditTechnicalNotes, "## Unit Tests");
        StringAssert.Contains(vm.EditTechnicalNotes, "Assert Foo is true.");
    }

    [TestMethod]
    [Description("Setting multiple sub-fields produces all section headers in TechnicalNotes.")]
    public void EditTestsSubFields_MultipleSet_AllSectionsPresent()
    {
        var vm = CreateVm();
        vm.EditTestsUnitTests        = "Unit A";
        vm.EditTestsIntegrationTests = "Integration B";
        vm.EditTestsBuildValidation  = "Build C";

        StringAssert.Contains(vm.EditTechnicalNotes, "## Unit Tests");
        StringAssert.Contains(vm.EditTechnicalNotes, "## Integration Tests");
        StringAssert.Contains(vm.EditTechnicalNotes, "## Build Validation");
        StringAssert.Contains(vm.EditTechnicalNotes, "Unit A");
        StringAssert.Contains(vm.EditTechnicalNotes, "Integration B");
        StringAssert.Contains(vm.EditTechnicalNotes, "Build C");
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Round-trip: write sub-fields, read back from TechnicalNotes, re-parse correctly.")]
    public void TestsSubFields_RoundTrip_ParsesBackCorrectly()
    {
        var vm1 = CreateVm();
        vm1.EditTestsUnitTests       = "Check return value.";
        vm1.EditTestsRegressionTests = "All 89 tests pass.";

        // Simulate reload: second VM reads the serialized TechnicalNotes
        var vm2 = CreateVm();
        vm2.EditTechnicalNotes = vm1.EditTechnicalNotes;

        Assert.AreEqual("Check return value.", vm2.EditTestsUnitTests);
        Assert.AreEqual("All 89 tests pass.",  vm2.EditTestsRegressionTests);
        Assert.AreEqual(string.Empty,          vm2.EditTestsIntegrationTests);
    }

    // ── FullTestPlan ──────────────────────────────────────────────────────────

    [TestMethod]
    [Description("FullTestPlan returns the current TechnicalNotes value.")]
    public void FullTestPlan_ReturnsCurrentTechnicalNotes()
    {
        var vm = CreateVm();
        vm.EditTestsUnitTests = "Verify login.";

        Assert.AreEqual(vm.EditTechnicalNotes, vm.FullTestPlan);
        StringAssert.Contains(vm.FullTestPlan, "Verify login.");
    }

    // ── Build This interop ────────────────────────────────────────────────────

    [TestMethod]
    [Description("CanBuildTicket is unaffected by Tests tab field changes.")]
    public void CanBuildTicket_UnchangedAfterSettingTestFields()
    {
        var vm = CreateVm();

        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, @"C:\repo\test");
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);

        vm.EditTitle = "Fix Header";
        vm.HasDetail = true;
        vm.IsEditing = true;
        vm.EditId    = 1;
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new IronDev.Agent.Models.TicketItem { Id = 1, Title = "Fix Header" });

        vm.EditTestsUnitTests        = "TestA";
        vm.EditTestsIntegrationTests = "TestB";
        vm.EditTestsBuildValidation  = "dotnet build";

        Assert.IsTrue(vm.CanBuildTicket,
            "CanBuildTicket must remain true after populating test fields.");
    }

    [TestMethod]
    [Description("Build This command still completes after Tests tab is populated.")]
    public async System.Threading.Tasks.Task BuildSelectedTicket_AfterTestsPopulated_Succeeds()
    {
        var vm = CreateVm();

        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, @"C:\repo\test");
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);

        vm.EditTitle = "Fix Header";
        vm.HasDetail = true;
        vm.IsEditing = true;
        vm.EditId    = 1;
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new IronDev.Agent.Models.TicketItem { Id = 1, Title = "Fix Header" });

        vm.EditTestsUnitTests        = "Verify Foo.";
        vm.EditTestsIntegrationTests = "Login flow.";

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasBuildPreview, "Build preview should be set after Build This.");
        Assert.IsFalse(vm.IsBuildingTicket, "IsBuildingTicket must be false after completion.");
    }
}
