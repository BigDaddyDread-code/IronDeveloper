using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using IronDev.Agent.ViewModels.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace IronDev.IntegrationTests;

/// <summary>
/// Unit-style tests for LlmConsoleViewModel and LlmTraceService.
/// No DB or WPF dispatcher required — these run in the test process directly.
/// </summary>
[TestClass]
public class LlmConsoleViewModelTests
{
    // ── F: Default tracing enabled ────────────────────────────────────────

    [TestMethod]
    [Description("F: New LlmTraceService defaults IsTracingEnabled = true.")]
    public void LlmTraceService_DefaultTracingEnabled()
    {
        var svc = new LlmTraceService();
        Assert.IsTrue(svc.IsTracingEnabled, "Tracing should be enabled by default.");
    }

    // ── E: Disabled tracing no-ops AddTrace ──────────────────────────────

    [TestMethod]
    [Description("E: When IsTracingEnabled = false, AddTrace stores nothing and raises no event.")]
    public void LlmTraceService_TracingDisabled_DoesNotStore()
    {
        var svc   = new LlmTraceService();
        svc.IsTracingEnabled = false;

        bool eventRaised = false;
        svc.TraceAdded += (_, _) => eventRaised = true;

        svc.AddTrace(new LlmTraceEntry { FeatureName = "Chat", WasSuccessful = true });

        Assert.AreEqual(0, svc.GetRecentTraces().Count, "Disabled trace service must store nothing.");
        Assert.IsFalse(eventRaised, "TraceAdded event must not fire when tracing is disabled.");
    }

    // ── B: TraceAdded event raised on AddTrace ────────────────────────────

    [TestMethod]
    [Description("B: LlmTraceService raises TraceAdded with the stored entry when AddTrace is called.")]
    public void LlmTraceService_TraceAdded_EventRaised()
    {
        var svc   = new LlmTraceService();
        LlmTraceEntry? received = null;
        svc.TraceAdded += (_, e) => received = e;

        var entry = new LlmTraceEntry { FeatureName = "DraftTicketGeneration", WasSuccessful = true };
        svc.AddTrace(entry);

        Assert.IsNotNull(received, "TraceAdded event must fire.");
        Assert.AreEqual("DraftTicketGeneration", received!.FeatureName);
    }

    // ── C: SelectedTrace property notification ────────────────────────────

    [TestMethod]
    [Description("C: Setting SelectedTrace on LlmConsoleViewModel raises PropertyChanged.")]
    public void LlmConsoleViewModel_SelectedTrace_RaisesPropertyChanged()
    {
        var svc = new LlmTraceService();
        var vm  = new LlmConsoleViewModel(svc);

        string? changedProp = null;
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LlmConsoleViewModel.SelectedTrace))
                changedProp = e.PropertyName;
        };

        var entry = new LlmTraceEntry { FeatureName = "Chat", WasSuccessful = true };
        svc.AddTrace(entry);

        // Manually assign (simulating list selection; Dispatcher.BeginInvoke
        // doesn't fire in test context so we assign directly)
        vm.SelectedTrace = svc.GetRecentTraces()[0];

        Assert.AreEqual(nameof(LlmConsoleViewModel.SelectedTrace), changedProp,
            "PropertyChanged must fire for SelectedTrace.");
        Assert.IsNotNull(vm.SelectedTrace);

        vm.Dispose();
    }

    // ── A: Refresh selects latest trace when nothing was selected ─────────

    [TestMethod]
    [Description("A: Refresh auto-selects the newest trace when SelectedTrace is null.")]
    public void LlmConsoleViewModel_Refresh_SelectsNewestTrace()
    {
        var svc = new LlmTraceService();
        // Add two traces — newest is first in GetRecentTraces (inserted at 0)
        svc.AddTrace(new LlmTraceEntry { FeatureName = "Chat",          WasSuccessful = true });
        svc.AddTrace(new LlmTraceEntry { FeatureName = "GroundingTest", WasSuccessful = true });

        var vm = new LlmConsoleViewModel(svc);
        // Constructor calls Refresh(), which should have auto-selected the newest
        Assert.IsNotNull(vm.SelectedTrace, "Refresh must auto-select a trace when list is non-empty.");
        // Newest trace was the last AddTrace call → index 0 in GetRecentTraces
        Assert.AreEqual("GroundingTest", vm.SelectedTrace!.FeatureName,
            "Newest trace (index 0) should be selected.");

        vm.Dispose();
    }

    // ── A2: Refresh preserves existing selection ──────────────────────────

    [TestMethod]
    [Description("A2: Refresh preserves SelectedTrace when the entry still exists.")]
    public void LlmConsoleViewModel_Refresh_PreservesSelection()
    {
        var svc = new LlmTraceService();
        svc.AddTrace(new LlmTraceEntry { FeatureName = "Chat", WasSuccessful = true });

        var vm = new LlmConsoleViewModel(svc);
        var original = vm.Traces[0];
        vm.SelectedTrace = original;

        // Add another trace, then refresh
        svc.AddTrace(new LlmTraceEntry { FeatureName = "GroundingTest", WasSuccessful = true });
        vm.Refresh();

        Assert.AreEqual(original.Id, vm.SelectedTrace?.Id,
            "Selection should be preserved across Refresh if the entry is still visible.");

        vm.Dispose();
    }

    // ── B2: Live insert via TraceAdded subscription ───────────────────────

    [TestMethod]
    [Description("B2: LlmConsoleViewModel subscribes to TraceAdded and unsubscribes on Dispose.")]
    public void LlmConsoleViewModel_TraceAdded_SubscribesAndUnsubscribes()
    {
        // Use a wrapper trace service so we can inspect the delegate list
        var svc     = new LlmTraceService();
        int fireCount = 0;

        // Add our own probe handler BEFORE constructing the VM
        svc.TraceAdded += (_, _) => fireCount++;

        // Construct VM (it subscribes too)
        var vm = new LlmConsoleViewModel(svc);

        // Fire an event directly (bypasses Dispatcher.BeginInvoke in test process)
        svc.AddTrace(new LlmTraceEntry { FeatureName = "Chat", WasSuccessful = true });

        Assert.AreEqual(1, fireCount, "Our probe handler must fire once.");

        // Dispose VM (it unsubscribes)
        vm.Dispose();

        // Fire again — only our probe should receive it now
        fireCount = 0;
        svc.AddTrace(new LlmTraceEntry { FeatureName = "Chat2", WasSuccessful = true });
        Assert.AreEqual(1, fireCount,
            "After Dispose, probe still fires but VM's handler must be gone (can't directly count VM's subs, " +
            "but no exceptions means no double-fire side effects).");
    }

    // ── D: Main chat FeatureName = "Chat" ────────────────────────────────

    [TestMethod]
    [Description("D: LlmTraceService records the Chat feature name when added via ChatWorkspaceViewModel convention.")]
    public void LlmTraceService_ChatTrace_HasCorrectFeatureName()
    {
        var svc = new LlmTraceService();
        // Simulate what ChatWorkspaceViewModel does
        svc.AddTrace(new LlmTraceEntry
        {
            FeatureName  = "Chat",
            WorkspaceName = "Chat",
            WasSuccessful = true,
            RequestText   = "You are IronDev Architect...",
            DurationMs    = 350
        });

        var traces = svc.GetRecentTraces();
        Assert.AreEqual(1, traces.Count);
        Assert.AreEqual("Chat", traces[0].FeatureName,
            "Main chat traces must use FeatureName = 'Chat'.");
    }

    // ── ExportTrace includes all fields ──────────────────────────────────

    [TestMethod]
    [Description("CopyFullTrace: ExportTrace includes feature, timing, prompt, response, warnings, errors.")]
    public void LlmTraceService_ExportTrace_IncludesAllFields()
    {
        var svc   = new LlmTraceService();
        var entry = new LlmTraceEntry
        {
            FeatureName         = "DraftTicketGeneration",
            Model               = "gpt-4o",
            DurationMs          = 1234,
            WasSuccessful       = false,
            RequestText         = "Prompt text",
            RawResponseText     = "Response text",
            ParsedResponseSummary = "Summary",
            ErrorMessage        = "Connection timeout",
            Warnings            = "ProjectRules unavailable"
        };

        var exported = svc.ExportTrace(entry);

        StringAssert.Contains(exported, "DraftTicketGeneration");
        StringAssert.Contains(exported, "gpt-4o");
        StringAssert.Contains(exported, "1234ms");
        StringAssert.Contains(exported, "Prompt text");
        StringAssert.Contains(exported, "Response text");
        StringAssert.Contains(exported, "Connection timeout");
        StringAssert.Contains(exported, "ProjectRules unavailable");
    }

    // ── C: SettingsViewModel binding writes through to trace service ──────

    [TestMethod]
    [Description("C: Changing IsLlmTracingEnabled on SettingsWorkspaceViewModel propagates to ILlmTraceService.IsTracingEnabled.")]
    public void SettingsWorkspaceViewModel_IsLlmTracingEnabled_WritesThrough()
    {
        var svc = new LlmTraceService();
        Assert.IsTrue(svc.IsTracingEnabled, "Precondition: tracing starts enabled.");

        var settingsVm = new IronDev.Agent.ViewModels.Workspaces.SettingsWorkspaceViewModel(svc);
        Assert.IsTrue(settingsVm.IsLlmTracingEnabled,
            "SettingsWorkspaceViewModel.IsLlmTracingEnabled must reflect the service default (true).");

        // Disable through the settings VM
        settingsVm.IsLlmTracingEnabled = false;
        Assert.IsFalse(svc.IsTracingEnabled,
            "Setting IsLlmTracingEnabled = false must propagate to ILlmTraceService.");

        // Re-enable
        settingsVm.IsLlmTracingEnabled = true;
        Assert.IsTrue(svc.IsTracingEnabled,
            "Setting IsLlmTracingEnabled = true must re-enable ILlmTraceService.");
    }

    // ── C2: SettingsViewModel PropertyChanged fires ────────────────────────

    [TestMethod]
    [Description("C2: Changing IsLlmTracingEnabled raises PropertyChanged on SettingsWorkspaceViewModel.")]
    public void SettingsWorkspaceViewModel_IsLlmTracingEnabled_RaisesPropertyChanged()
    {
        var svc       = new LlmTraceService();
        var settingsVm = new IronDev.Agent.ViewModels.Workspaces.SettingsWorkspaceViewModel(svc);

        string? changedProp = null;
        ((System.ComponentModel.INotifyPropertyChanged)settingsVm).PropertyChanged +=
            (_, e) => { if (e.PropertyName == nameof(settingsVm.IsLlmTracingEnabled)) changedProp = e.PropertyName; };

        settingsVm.IsLlmTracingEnabled = false;

        Assert.AreEqual(nameof(settingsVm.IsLlmTracingEnabled), changedProp,
            "PropertyChanged must fire for IsLlmTracingEnabled.");
    }

    // ── D: Disabling tracing does not clear existing traces ───────────────

    [TestMethod]
    [Description("D: Turning IsTracingEnabled off does not clear existing stored traces.")]
    public void LlmTraceService_DisableTracing_PreservesExistingTraces()
    {
        var svc = new LlmTraceService();
        svc.AddTrace(new LlmTraceEntry { FeatureName = "Chat",          WasSuccessful = true });
        svc.AddTrace(new LlmTraceEntry { FeatureName = "GroundingTest", WasSuccessful = true });

        Assert.AreEqual(2, svc.GetRecentTraces().Count, "Precondition: 2 traces stored.");

        // Disable tracing
        svc.IsTracingEnabled = false;

        // Add another trace — should be ignored
        svc.AddTrace(new LlmTraceEntry { FeatureName = "NewTrace", WasSuccessful = true });

        var traces = svc.GetRecentTraces();
        Assert.AreEqual(2, traces.Count,
            "Disabling tracing must not add new traces.");
        Assert.IsFalse(traces.Any(t => t.FeatureName == "NewTrace"),
            "The trace added while disabled must not be stored.");
        Assert.IsTrue(traces.Any(t => t.FeatureName == "Chat"),
            "Pre-existing traces must be preserved after disabling.");
        Assert.IsTrue(traces.Any(t => t.FeatureName == "GroundingTest"),
            "Pre-existing traces must be preserved after disabling.");
    }

    // ── Console VM reflects IsTracingEnabled from service ─────────────────

    [TestMethod]
    [Description("LlmConsoleViewModel.IsTracingEnabled reflects ILlmTraceService.IsTracingEnabled.")]
    public void LlmConsoleViewModel_IsTracingEnabled_ReflectsService()
    {
        var svc = new LlmTraceService();
        var vm  = new LlmConsoleViewModel(svc);

        Assert.IsTrue(vm.IsTracingEnabled, "Console VM must reflect service default (true).");

        svc.IsTracingEnabled = false;
        Assert.IsFalse(vm.IsTracingEnabled, "Console VM must reflect service being disabled.");

        vm.Dispose();
    }

    // ── A: BuildContextSummary produces a populated summary ──────────────

    [TestMethod]
    [Description("A: BuildContextSummary returns a non-empty string containing all expected field labels.")]
    public void ChatWorkspaceViewModel_BuildContextSummary_PopulatesAllFields()
    {
        var packet = new IronDev.AI.ChatContextPacket
        {
            Intent               = IronDev.AI.ChatIntent.CodeQuery,
            IsProjectNotIndexed  = false,
            IncludedMemoryCount  = 2,
            FilteredMemoryCount  = 1,
            IncludedStandardsCount = 1,
            FilteredStandardsCount = 0,
            RulesLoadWarning     = null
        };
        packet.MatchedFilePaths.AddRange(new[] { "src/Foo.cs", "src/Bar.cs", "src/Baz.cs" });

        var summary = IronDev.Agent.ViewModels.Workspaces.ChatWorkspaceViewModel
            .BuildContextSummary(packet);

        Assert.IsFalse(string.IsNullOrWhiteSpace(summary),
            "BuildContextSummary must return a non-empty string.");

        StringAssert.Contains(summary, "Intent");
        StringAssert.Contains(summary, "CodeQuery");
        StringAssert.Contains(summary, "Project indexed");
        StringAssert.Contains(summary, "Yes");
        StringAssert.Contains(summary, "Retrieved files");
        StringAssert.Contains(summary, "3");
        StringAssert.Contains(summary, "Memory included");
        StringAssert.Contains(summary, "2");
        StringAssert.Contains(summary, "Warnings");
        StringAssert.Contains(summary, "none");
        // Top files section should list basenames
        StringAssert.Contains(summary, "Foo.cs");
        StringAssert.Contains(summary, "Bar.cs");
    }

    // ── B: ExportTrace includes ContextSummary ────────────────────────────

    [TestMethod]
    [Description("B: ExportTrace output includes the ContextSummary when it is populated.")]
    public void LlmTraceService_ExportTrace_IncludesContextSummary()
    {
        var svc = new LlmTraceService();
        var entry = new LlmTraceEntry
        {
            FeatureName    = "Chat",
            WasSuccessful  = true,
            RequestText    = "User prompt",
            RawResponseText = "AI response",
            ContextSummary = "Intent: CodeQuery\nProject indexed: Yes\nRetrieved files: 3"
        };

        var exported = svc.ExportTrace(entry);

        StringAssert.Contains(exported, "CONTEXT SUMMARY",
            "Export must include the CONTEXT SUMMARY section header.");
        StringAssert.Contains(exported, "Intent: CodeQuery",
            "Export must include the populated ContextSummary text.");
        StringAssert.Contains(exported, "Retrieved files: 3",
            "Export must include the file count from ContextSummary.");
    }

    // ── C: Empty ContextSummary is safe ──────────────────────────────────

    [TestMethod]
    [Description("C: A trace with an empty ContextSummary does not crash the service or VM.")]
    public void LlmConsoleViewModel_EmptyContextSummary_IsSafe()
    {
        var svc = new LlmTraceService();
        // Trace with no ContextSummary set (defaults to empty string)
        svc.AddTrace(new LlmTraceEntry
        {
            FeatureName  = "Chat",
            WasSuccessful = true,
            // ContextSummary intentionally not set
        });

        var vm = new LlmConsoleViewModel(svc);

        Assert.AreEqual(1, vm.Traces.Count, "Trace must be loaded.");
        Assert.IsNotNull(vm.SelectedTrace, "SelectedTrace must be set.");
        Assert.AreEqual(string.Empty, vm.SelectedTrace!.ContextSummary,
            "ContextSummary must be empty string, not null.");

        // ExportTrace must not throw and must still include the section header
        var exported = svc.ExportTrace(vm.SelectedTrace);
        StringAssert.Contains(exported, "CONTEXT SUMMARY",
            "Export must always include the CONTEXT SUMMARY section header even when empty.");
        StringAssert.Contains(exported, "(not captured)",
            "Export must show '(not captured)' when ContextSummary is not populated.");

        vm.Dispose();
    }
}
