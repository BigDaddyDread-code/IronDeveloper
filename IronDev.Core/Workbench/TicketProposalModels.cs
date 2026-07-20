using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Workbench;

public static class WorkbenchAgentInvocationKinds
{
    public const string Conversation = "Conversation";
    public const string TicketProposalGeneration = "TicketProposalGeneration";
    public const string TicketProposalRegeneration = "TicketProposalRegeneration";

    public static bool IsTicketProposal(string value) => value is
        TicketProposalGeneration or TicketProposalRegeneration;

    public static bool IsSupported(string value) =>
        value == Conversation || IsTicketProposal(value);
}

public static class TicketProposalSetStatuses
{
    public const string Ready = "Ready";
    public const string NeedsInput = "NeedsInput";
}

public static class TicketProposalIssueKinds
{
    public const string Question = "Question";
    public const string Conflict = "Conflict";
}

public static class TicketProposalIssueStatuses
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";
}

public static class TicketProposalRevisionChangeKinds
{
    public const string Generated = "Generated";
    public const string Regenerated = "Regenerated";
    public const string Edited = "Edited";
    public const string Reordered = "Reordered";
    public const string Removed = "Removed";
    public const string IssueResolved = "IssueResolved";
}

/// <summary>
/// Provider-owned proposal candidate. ProposalKey and dependency keys are local to one output;
/// durable proposal identifiers are always allocated by the server during materialization.
/// </summary>
public sealed record TicketProposalOutput(
    string ProposalKey,
    string Title,
    string Problem,
    string ProposedChange,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> Dependencies,
    int SuggestedOrder,
    IReadOnlyList<long> SourceMessageIds);

public sealed record TicketProposalIssueOutput(
    string Kind,
    string Text,
    IReadOnlyList<long> SourceMessageIds);

public sealed record TicketProposalSetOutput(
    string? SplitReason,
    IReadOnlyList<TicketProposalOutput> Proposals,
    IReadOnlyList<TicketProposalIssueOutput> OpenQuestions,
    IReadOnlyList<TicketProposalIssueOutput> PotentialConflicts,
    IReadOnlyList<long> SourceMessageIds);

public sealed record TicketProposalDocument(
    Guid TicketProposalId,
    string Title,
    string Problem,
    string ProposedChange,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<Guid> DependencyProposalIds,
    int SuggestedOrder,
    IReadOnlyList<long> SourceMessageIds);

public sealed record TicketProposalIssueDocument(
    Guid IssueId,
    string Kind,
    string Text,
    string Status,
    string? Resolution,
    IReadOnlyList<long> SourceMessageIds);

/// <summary>
/// Canonical, append-only proposal-set revision document. It is also the API read shape.
/// Actor attribution belongs to the revision row rather than this reusable aggregate snapshot.
/// </summary>
public sealed record TicketProposalSetDocument(
    Guid TicketProposalSetId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    long Revision,
    long BasedOnUnderstandingRevision,
    string Status,
    string? SplitReason,
    IReadOnlyList<TicketProposalDocument> Proposals,
    IReadOnlyList<TicketProposalIssueDocument> OpenQuestions,
    IReadOnlyList<TicketProposalIssueDocument> PotentialConflicts,
    IReadOnlyList<long> SourceMessageIds,
    Guid CreatedByAgentRunId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public static class TicketProposalSetDocumentCodec
{
    private static readonly JsonSerializerOptions StrictOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(TicketProposalSetDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, StrictOptions);
    }

    public static TicketProposalSetDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("The ticket proposal-set revision document is empty.");
        var document = JsonSerializer.Deserialize<TicketProposalSetDocument>(json, StrictOptions)
            ?? throw new InvalidOperationException("The ticket proposal-set revision document could not be read.");
        Validate(document);
        return document;
    }

    public static string ComputeHash(string canonicalJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();

    public static TicketProposalSetDocument Materialize(
        TicketProposalSetOutput output,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        long basedOnUnderstandingRevision,
        Guid agentRunId,
        Guid? existingSetId,
        long revision,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        string outcome)
    {
        ArgumentNullException.ThrowIfNull(output);
        var proposalIds = output.Proposals.ToDictionary(
            proposal => proposal.ProposalKey,
            _ => Guid.NewGuid(),
            StringComparer.Ordinal);
        var proposals = output.Proposals
            .Select(proposal => new TicketProposalDocument(
                proposalIds[proposal.ProposalKey],
                proposal.Title.Trim(),
                proposal.Problem.Trim(),
                proposal.ProposedChange.Trim(),
                proposal.AcceptanceCriteria.Select(value => value.Trim()).ToArray(),
                proposal.Dependencies.Select(key => proposalIds[key]).ToArray(),
                proposal.SuggestedOrder,
                proposal.SourceMessageIds.Distinct().Order().ToArray()))
            .OrderBy(proposal => proposal.SuggestedOrder)
            .ToArray();
        var questions = output.OpenQuestions.Select(issue => new TicketProposalIssueDocument(
            Guid.NewGuid(), TicketProposalIssueKinds.Question, issue.Text.Trim(),
            TicketProposalIssueStatuses.Open, null,
            issue.SourceMessageIds.Distinct().Order().ToArray())).ToArray();
        var conflicts = output.PotentialConflicts.Select(issue => new TicketProposalIssueDocument(
            Guid.NewGuid(), TicketProposalIssueKinds.Conflict, issue.Text.Trim(),
            TicketProposalIssueStatuses.Open, null,
            issue.SourceMessageIds.Distinct().Order().ToArray())).ToArray();

        return new TicketProposalSetDocument(
            existingSetId ?? Guid.NewGuid(),
            projectId,
            workbenchSessionId,
            leaseEpoch,
            revision,
            basedOnUnderstandingRevision,
            outcome == WorkbenchAgentRunStates.NeedsInput
                ? TicketProposalSetStatuses.NeedsInput
                : TicketProposalSetStatuses.Ready,
            string.IsNullOrWhiteSpace(output.SplitReason) ? null : output.SplitReason.Trim(),
            proposals,
            questions,
            conflicts,
            output.SourceMessageIds.Distinct().Order().ToArray(),
            agentRunId,
            createdAtUtc,
            updatedAtUtc);
    }

    public static void Validate(TicketProposalSetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.TicketProposalSetId == Guid.Empty || document.ProjectId <= 0 ||
            document.WorkbenchSessionId <= 0 || document.LeaseEpoch <= 0 ||
            document.Revision <= 0 || document.BasedOnUnderstandingRevision <= 0 ||
            document.CreatedByAgentRunId == Guid.Empty || document.CreatedAtUtc == default ||
            document.UpdatedAtUtc < document.CreatedAtUtc)
            throw new InvalidOperationException("The ticket proposal-set document identity or revision is invalid.");
        if (document.Status is not (TicketProposalSetStatuses.Ready or TicketProposalSetStatuses.NeedsInput))
            throw new InvalidOperationException("The ticket proposal-set status is invalid.");
        if (document.Proposals is null || document.OpenQuestions is null ||
            document.PotentialConflicts is null || document.SourceMessageIds is null)
            throw new InvalidOperationException("The ticket proposal-set collections are required.");
        if (document.Status == TicketProposalSetStatuses.Ready && document.Proposals.Count is < 1 or > 5)
            throw new InvalidOperationException("A ready ticket proposal set must contain one to five proposals.");
        if (document.Status == TicketProposalSetStatuses.NeedsInput && document.Proposals.Count != 0)
            throw new InvalidOperationException("A NeedsInput ticket proposal set cannot contain proposals.");

        var ids = document.Proposals.Select(value => value.TicketProposalId).ToHashSet();
        if (ids.Count != document.Proposals.Count || ids.Contains(Guid.Empty))
            throw new InvalidOperationException("Ticket proposal identifiers must be unique and non-empty.");
        if (!document.Proposals.Select(value => value.SuggestedOrder)
                .SequenceEqual(Enumerable.Range(1, document.Proposals.Count)))
            throw new InvalidOperationException("Ticket proposal order must be contiguous and canonical.");
        foreach (var proposal in document.Proposals)
        {
            if (string.IsNullOrWhiteSpace(proposal.Title) || string.IsNullOrWhiteSpace(proposal.Problem) ||
                string.IsNullOrWhiteSpace(proposal.ProposedChange) || proposal.AcceptanceCriteria.Count == 0 ||
                proposal.SourceMessageIds.Count == 0 ||
                proposal.DependencyProposalIds.Count != proposal.DependencyProposalIds.Distinct().Count() ||
                proposal.DependencyProposalIds.Any(id =>
                    id == proposal.TicketProposalId ||
                    !ids.Contains(id) ||
                    document.Proposals.Single(value => value.TicketProposalId == id).SuggestedOrder >=
                    proposal.SuggestedOrder))
                throw new InvalidOperationException("A ticket proposal is incomplete or has an invalid dependency.");
        }
        foreach (var issue in document.OpenQuestions.Concat(document.PotentialConflicts))
        {
            if (issue.IssueId == Guid.Empty || string.IsNullOrWhiteSpace(issue.Kind) ||
                string.IsNullOrWhiteSpace(issue.Text) || issue.SourceMessageIds is null ||
                issue.SourceMessageIds.Count == 0 || issue.SourceMessageIds.Any(id => id <= 0) ||
                issue.SourceMessageIds.Count != issue.SourceMessageIds.Distinct().Count() ||
                issue.Status is not (TicketProposalIssueStatuses.Open or TicketProposalIssueStatuses.Resolved) ||
                (issue.Status == TicketProposalIssueStatuses.Open && issue.Resolution is not null) ||
                (issue.Status == TicketProposalIssueStatuses.Resolved && string.IsNullOrWhiteSpace(issue.Resolution)))
                throw new InvalidOperationException("A ticket proposal issue is incomplete or invalid.");
        }
    }
}

public sealed class TicketProposalRevisionConflictException : Exception
{
    public const string ErrorCode = "ticket_proposal_revision_conflict";

    public TicketProposalRevisionConflictException(long currentRevision)
        : base("The ticket proposal set changed before this operation could start.")
    {
        CurrentRevision = currentRevision;
    }

    public long CurrentRevision { get; }
}
