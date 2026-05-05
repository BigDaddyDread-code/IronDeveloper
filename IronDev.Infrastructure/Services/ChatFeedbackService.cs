using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

/// <summary>
/// Persists and retrieves Chat message feedback for prompt adaptation.
/// </summary>
public interface IChatFeedbackService
{
    /// <summary>Saves a Useful/Weak rating for an assistant message.</summary>
    Task<long> SaveFeedbackAsync(ChatMessageFeedback feedback, CancellationToken ct = default);

    /// <summary>
    /// Returns a plain-text "response preferences" block derived from recent feedback,
    /// ready for injection into the prompt context builder.
    /// Returns an empty string if no meaningful preference can be derived.
    /// </summary>
    Task<string> GetProjectFeedbackSummaryAsync(int projectId, CancellationToken ct = default);
}

public sealed class ChatFeedbackService : IChatFeedbackService
{
    private readonly IDbConnectionFactory  _db;
    private readonly ICurrentTenantContext _tenant;

    // How many recent feedback rows to inspect when building the preference summary
    private const int PreferenceLookback = 30;

    public ChatFeedbackService(IDbConnectionFactory db, ICurrentTenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<long> SaveFeedbackAsync(ChatMessageFeedback feedback, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.ChatMessageFeedback
                (TenantId, ProjectId, ChatSessionId, ChatMessageId, Rating, Reason, Comment)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ProjectId, @ChatSessionId, @ChatMessageId, @Rating, @Reason, @Comment);
            """;

        feedback.TenantId = _tenant.TenantId;

        using var con = _db.CreateConnection();
        return await con.QuerySingleAsync<long>(new CommandDefinition(
            sql,
            new
            {
                feedback.TenantId,
                feedback.ProjectId,
                feedback.ChatSessionId,
                feedback.ChatMessageId,
                feedback.Rating,
                feedback.Reason,
                feedback.Comment
            },
            cancellationToken: ct));
    }

    public async Task<string> GetProjectFeedbackSummaryAsync(int projectId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Rating, Reason
            FROM dbo.ChatMessageFeedback
            WHERE TenantId  = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY CreatedDate DESC;
            """;

        using var con = _db.CreateConnection();
        var rows = (await con.QueryAsync<(string Rating, string? Reason)>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = PreferenceLookback },
            cancellationToken: ct))).ToList();

        if (rows.Count == 0)
            return string.Empty;

        var prefs = new List<string>();

        // ── Negative signal rules ──────────────────────────────────────────────
        var weakReasons = rows
            .Where(r => r.Rating == "Weak" && r.Reason != null)
            .Select(r => r.Reason!)
            .ToList();

        if (weakReasons.Contains("Too generic"))
            prefs.Add("Avoid generic advice. Use actual project files and classes when available.");
        if (weakReasons.Contains("Wrong files/classes"))
            prefs.Add("Only mention files and classes that appear in retrieved context. Do not invent file names.");
        if (weakReasons.Contains("Missed project context"))
            prefs.Add("Always use the provided code snippets, decisions, and tickets before generalising.");
        if (weakReasons.Contains("Too verbose"))
            prefs.Add("Be concise. Prefer short, direct answers over lengthy explanations.");
        if (weakReasons.Contains("Not actionable"))
            prefs.Add("Make answers actionable. Include specific steps, file names, or code references.");

        // ── Positive signal rules ──────────────────────────────────────────────
        var usefulReasons = rows
            .Where(r => r.Rating == "Useful" && r.Reason != null)
            .Select(r => r.Reason!)
            .ToList();

        if (usefulReasons.Contains("Good file grounding"))
            prefs.Add("Continue giving exact affected files and symbols.");
        if (usefulReasons.Contains("Good ticket detail"))
            prefs.Add("Continue producing detailed, specific ticket content with acceptance criteria.");
        if (usefulReasons.Contains("Good implementation guidance"))
            prefs.Add("Continue providing concrete implementation steps tied to this codebase.");
        if (usefulReasons.Contains("Clear answer"))
            prefs.Add("Continue giving clear, concise answers.");

        if (prefs.Count == 0)
            return string.Empty;

        return "Project response preferences (derived from user feedback):\n"
             + string.Join("\n", prefs.Select(p => $"- {p}"));
    }
}
