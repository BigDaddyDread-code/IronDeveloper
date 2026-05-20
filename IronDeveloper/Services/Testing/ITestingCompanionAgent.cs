using IronDev.Core.Testing;

namespace IronDev.Agent.Services.Testing;

public interface ITestingCompanionAgent
{
    TestRun? CurrentRun { get; }
    IReadOnlyList<TestMoment> CurrentMoments { get; }

    Task<TestRun> StartSessionAsync(StartTestRunRequest request, CancellationToken ct = default);
    Task AttachToProcessAsync(Guid testRunId, int processId, CancellationToken ct = default);
    Task<BrokenMomentCaptureDraft> BeginMarkMomentAsync(Guid testRunId, string? activeWorkspace, CancellationToken ct = default);
    Task<TestMoment> SaveMarkedMomentAsync(Guid testRunId, SaveMarkedMomentRequest request, CancellationToken ct = default);
    Task<TestRunReport> EndSessionAndGenerateReportAsync(Guid testRunId, CancellationToken ct = default);
    Task<IReadOnlyList<TestMoment>> LoadPersistedMomentsAsync(string? projectPath, int take = 25, CancellationToken ct = default);
    Task<string> BuildCombinedPromptAsync(string? projectPath, IReadOnlyList<TestMoment> moments, CancellationToken ct = default);
}
