using System.Collections.Concurrent;
using IronDev.Core.Tools;

namespace IronDev.Infrastructure.Tools;

public sealed class InMemoryGovernedToolThoughtLedger : IGovernedToolThoughtLedger
{
    private readonly ConcurrentQueue<GovernedToolThoughtLedgerEntry> _entries = new();

    public IReadOnlyList<GovernedToolThoughtLedgerEntry> Entries => _entries.ToArray();

    public Task RecordAsync(
        GovernedToolThoughtLedgerEntry entry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}

internal sealed class NullGovernedToolThoughtLedger : IGovernedToolThoughtLedger
{
    public static NullGovernedToolThoughtLedger Instance { get; } = new();

    private NullGovernedToolThoughtLedger()
    {
    }

    public Task RecordAsync(
        GovernedToolThoughtLedgerEntry entry,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
