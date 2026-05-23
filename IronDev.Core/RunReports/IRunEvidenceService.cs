namespace IronDev.Core.RunReports;

public interface IRunEvidenceService
{
    Task<IReadOnlyList<RunEvidenceItem>> GetEvidenceAsync(
        string runId,
        CancellationToken cancellationToken = default);

    Task<string?> ReadEvidenceTextAsync(
        string runId,
        string evidencePath,
        CancellationToken cancellationToken = default);
}
