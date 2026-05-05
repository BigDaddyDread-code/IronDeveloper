using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Builder;

/// <summary>
/// Phase 3 implementation of ICodeChangeProposalService.
///
/// Builds a strict builder prompt from TicketBuildContext,
/// calls ILLMService.GetResponseAsync, and parses the returned JSON
/// into a CodeChangeProposal.
///
/// Contract:
///   - No files written.
///   - No dotnet build.
///   - No Weaviate calls.
///   - Returns an empty-FileChanges proposal if the LLM returns no changes.
///   - Throws InvalidOperationException with a clear message on invalid JSON.
/// </summary>
public sealed class CodeChangeProposalService : ICodeChangeProposalService
{
    private readonly ILLMService _llm;

    public CodeChangeProposalService(ILLMService llm)
    {
        _llm = llm;
    }

    public async Task<CodeChangeProposal> GenerateProposalAsync(
        TicketBuildContext context,
        CancellationToken  cancellationToken = default)
    {
        var prompt = BuildPrompt(context);

        string rawJson;
        try
        {
            rawJson = await _llm.GetResponseAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"LLM call failed: {ex.Message}", ex);
        }

        return ParseProposal(rawJson, context.TicketId);
    }

    // ── Prompt construction ───────────────────────────────────────────────────

    public static string BuildPrompt(TicketBuildContext ctx)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are IronDev Builder.");
        sb.AppendLine($"You are implementing a ticket for the project: {ctx.ProjectName}.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("- Only change files relevant to the ticket.");
        sb.AppendLine("- Do not invent requirements not stated in the ticket or plan.");
        sb.AppendLine("- Follow all linked architectural decisions.");
        sb.AppendLine("- Prefer small, targeted changes.");
        sb.AppendLine("- Do not replace whole files unless absolutely necessary.");
        sb.AppendLine("- Return ONLY a JSON object. No markdown fences. No prose. No commentary.");
        sb.AppendLine("- Include a BeforeSnippet and AfterSnippet for each file change.");
        sb.AppendLine("- Include a unified diff Patch if possible.");
        sb.AppendLine("- Include risk notes and a test plan.");
        sb.AppendLine("- Do not claim the build passed.");
        sb.AppendLine();

        sb.AppendLine("TICKET:");
        sb.AppendLine($"  Title: {ctx.TicketTitle}");
        sb.AppendLine($"  Summary: {ctx.TicketSummary}");
        if (!string.IsNullOrWhiteSpace(ctx.TicketAcceptanceCriteria))
            sb.AppendLine($"  Acceptance Criteria: {ctx.TicketAcceptanceCriteria}");
        if (!string.IsNullOrWhiteSpace(ctx.TicketImplementationNotes))
            sb.AppendLine($"  Implementation Notes: {ctx.TicketImplementationNotes}");
        if (!string.IsNullOrWhiteSpace(ctx.TicketBackground))
            sb.AppendLine($"  Background: {ctx.TicketBackground}");
        if (!string.IsNullOrWhiteSpace(ctx.TicketProblem))
            sb.AppendLine($"  Problem: {ctx.TicketProblem}");
        if (!string.IsNullOrWhiteSpace(ctx.TicketTestPlan))
        {
            sb.AppendLine("  Test Plan:");
            sb.AppendLine(ctx.TicketTestPlan);
        }
        sb.AppendLine();

        if (ctx.PlanTitle != null)
        {
            sb.AppendLine("LINKED IMPLEMENTATION PLAN:");
            sb.AppendLine($"  Title: {ctx.PlanTitle}");
            if (!string.IsNullOrWhiteSpace(ctx.PlanGoal))
                sb.AppendLine($"  Goal: {ctx.PlanGoal}");
            if (!string.IsNullOrWhiteSpace(ctx.PlanSteps))
            {
                sb.AppendLine("  Proposed Steps:");
                sb.AppendLine(ctx.PlanSteps);
            }
            if (!string.IsNullOrWhiteSpace(ctx.PlanAffectedFiles))
                sb.AppendLine($"  Affected Context: {ctx.PlanAffectedFiles}");
            if (!string.IsNullOrWhiteSpace(ctx.PlanRisksNotes))
                sb.AppendLine($"  Risks: {ctx.PlanRisksNotes}");
            sb.AppendLine();
        }

        if (ctx.AffectedFiles.Count > 0)
        {
            sb.AppendLine("AFFECTED FILES:");
            foreach (var f in ctx.AffectedFiles)
                sb.AppendLine($"  - {f}");
            sb.AppendLine();
        }

        if (ctx.Decisions.Count > 0)
        {
            sb.AppendLine("ARCHITECTURAL DECISIONS TO FOLLOW:");
            foreach (var d in ctx.Decisions)
                sb.AppendLine($"  - {d}");
            sb.AppendLine();
        }

        if (ctx.RetrievedSnippets.Count > 0)
        {
            sb.AppendLine("RELEVANT CODE CONTEXT:");
            foreach (var s in ctx.RetrievedSnippets)
            {
                sb.AppendLine(s);
                sb.AppendLine();
            }
        }

        sb.AppendLine("EXPECTED OUTPUT FORMAT:");
        sb.AppendLine("Return a single JSON object matching this schema exactly:");
        sb.AppendLine("""
            {
              "ticketId": <number>,
              "summary": "<brief description of what changes and why>",
              "riskNotes": "<risk assessment>",
              "testPlan": "<how to verify the change>",
              "fileChanges": [
                {
                  "filePath": "<relative file path>",
                  "changeReason": "<why this file needs to change>",
                  "beforeSnippet": "<exact text to be replaced>",
                  "afterSnippet": "<replacement text>",
                  "patch": "<unified diff, or empty string if not available>"
                }
              ]
            }
            """);
        sb.AppendLine("Return ONLY the JSON. No markdown. No explanation.");

        return sb.ToString();
    }

    // ── JSON parsing ──────────────────────────────────────────────────────────

    public static CodeChangeProposal ParseProposal(string rawJson, long ticketId)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new InvalidOperationException("LLM returned an empty response.");

        // Strip markdown fences if the model wraps output anyway
        var json = StripMarkdownFences(rawJson.Trim());

        ProposalJson dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProposalJson>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Deserialized proposal was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"AI proposal failed: invalid JSON — {ex.Message}", ex);
        }

        var proposal = new CodeChangeProposal
        {
            TicketId  = dto.TicketId > 0 ? dto.TicketId : ticketId,
            Summary   = dto.Summary   ?? "No summary provided.",
            RiskNotes = dto.RiskNotes ?? "Not specified.",
            TestPlan  = dto.TestPlan  ?? "Not specified.",
        };

        foreach (var fc in dto.FileChanges ?? [])
        {
            proposal.FileChanges.Add(new FileChangeProposal
            {
                FilePath      = fc.FilePath      ?? string.Empty,
                ChangeReason  = fc.ChangeReason  ?? string.Empty,
                BeforeSnippet = fc.BeforeSnippet ?? string.Empty,
                AfterSnippet  = fc.AfterSnippet  ?? string.Empty,
                Patch         = fc.Patch         ?? string.Empty,
            });
        }

        return proposal;
    }

    private static string StripMarkdownFences(string text)
    {
        // Handle ```json ... ``` or ``` ... ```
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
            if (text.EndsWith("```", StringComparison.Ordinal))
                text = text[..^3].TrimEnd();
        }
        return text;
    }

    // ── JSON DTO (case-insensitive deserialization) ───────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    private sealed class ProposalJson
    {
        [JsonPropertyName("ticketId")]   public long                    TicketId    { get; set; }
        [JsonPropertyName("summary")]    public string?                 Summary     { get; set; }
        [JsonPropertyName("riskNotes")]  public string?                 RiskNotes   { get; set; }
        [JsonPropertyName("testPlan")]   public string?                 TestPlan    { get; set; }
        [JsonPropertyName("fileChanges")]public List<FileChangeJson>?   FileChanges { get; set; }
    }

    private sealed class FileChangeJson
    {
        [JsonPropertyName("filePath")]      public string? FilePath      { get; set; }
        [JsonPropertyName("changeReason")]  public string? ChangeReason  { get; set; }
        [JsonPropertyName("beforeSnippet")] public string? BeforeSnippet { get; set; }
        [JsonPropertyName("afterSnippet")]  public string? AfterSnippet  { get; set; }
        [JsonPropertyName("patch")]         public string? Patch         { get; set; }
    }
}
