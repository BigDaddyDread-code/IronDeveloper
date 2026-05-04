using System;
using System.Collections.Generic;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

namespace IronDev.Agent.Services.Mock;

/// <summary>
/// Seeded in-memory decision service. No DB. Replace with real service in a later sprint.
/// </summary>
public sealed class MockDecisionShellService : IDecisionShellService
{
    private readonly List<DecisionSummary> _decisions =
    [
        new()
        {
            Id           = 1,
            Title        = "Use IronDeveloperControls as separate frozen library",
            Detail       = "Design system lives in its own repo and is consumed via project reference.",
            Rationale    = "Keeps UI primitives stable and reusable across multiple apps without coupling to workflow logic.",
            CapturedDate = DateTime.UtcNow.AddDays(-3)
        },
        new()
        {
            Id           = 2,
            Title        = "MVVM with CommunityToolkit source generators",
            Detail       = "All ViewModels derive from ObservableObject and use [ObservableProperty] / [RelayCommand].",
            Rationale    = "Reduces boilerplate, improves discoverability, and aligns with modern WPF MVVM patterns.",
            CapturedDate = DateTime.UtcNow.AddDays(-2)
        },
        new()
        {
            Id           = 3,
            Title        = "Mock services phase before real wiring",
            Detail       = "App phase 1 uses in-memory mock services registered via ConfigureMockShellServices().",
            Rationale    = "Lets the shell/workflow scaffold be built and verified without requiring a live DB or API key.",
            CapturedDate = DateTime.UtcNow.AddDays(-1)
        }
    ];

    public IReadOnlyList<DecisionSummary> GetDecisions() => _decisions.AsReadOnly();
}
