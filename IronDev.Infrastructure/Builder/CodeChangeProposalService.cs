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
using IronDev.Core.Models;

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
    private readonly ILLMService      _llm;
    private readonly ILlmTraceService _llmTraceService;

    public CodeChangeProposalService(ILLMService llm, ILlmTraceService llmTraceService)
    {
        _llm = llm;
        _llmTraceService = llmTraceService;
    }

    public async Task<CodeChangeProposal> GenerateProposalAsync(
        TicketBuildContext context,
        CancellationToken  cancellationToken = default)
    {
        var prompt = BuildPrompt(context);

        // ── Call LLM with tracing ─────────────────────────────────────────────
        var trace = new LlmTraceEntry
        {
            FeatureName = "BuildTicket",
            WorkspaceName = "Builder",
            ProjectId = context.ProjectId,
            TicketId = context.TicketId,
            PlanId = context.PlanId,
            RequestText = prompt,
            ActiveProjectName = context.ProjectName,
            ActiveProjectPath = context.ProjectPath,
            IsProposalOnly = true,
            CreatedAt = DateTime.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string rawJson;
        try
        {
            rawJson = await _llm.GetResponseAsync(prompt, cancellationToken);
            trace.WasSuccessful = true;
            trace.RawResponseText = rawJson;
        }
        catch (Exception ex)
        {
            trace.WasSuccessful = false;
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);
            throw new InvalidOperationException($"LLM call failed: {ex.Message}", ex);
        }
        finally
        {
            sw.Stop();
            trace.DurationMs = sw.ElapsedMilliseconds;
        }

        try
        {
            var proposal = ParseProposal(rawJson, context.TicketId);
            proposal.OriginalRequest = context.TicketSummary;
            
            trace.ProposedFileCount = proposal.FileChanges.Count;
            trace.ProposedFilesList = string.Join(", ", proposal.FileChanges.ConvertAll(c => c.FilePath));
            trace.ParsedResponseSummary = $"Proposed {proposal.FileChanges.Count} file changes.";
            
            _llmTraceService.AddTrace(trace);
            return proposal;
        }
        catch (Exception ex)
        {
            trace.ParsedResponseSummary = "JSON Parse Failure";
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);
            throw;
        }
    }

    // ── Prompt construction ───────────────────────────────────────────────────

    public static string BuildPrompt(TicketBuildContext ctx)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are IronDev Builder.");
        sb.AppendLine($"You are implementing a ticket for the project: {ctx.ProjectName}.");
        sb.AppendLine($"Active Target Project: {ctx.ProjectName}");
        sb.AppendLine($"Active Target Project Root: {ctx.ProjectPath}");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("- Generate a proposal only. Do not claim files were changed.");
        sb.AppendLine("- Use relative file paths only. Do not use absolute paths.");
        sb.AppendLine("- Do not use .. traversal in file paths.");
        sb.AppendLine("- Only change files relevant to the ticket.");
        sb.AppendLine("- Do not invent requirements not stated in the ticket or plan.");
        sb.AppendLine("- Follow all linked architectural decisions and project rules.");
        sb.AppendLine("- Prefer small, targeted changes.");
        sb.AppendLine("- Produce unified diffs for all changes.");
        sb.AppendLine("- Do not use markdown code fences inside JSON values (no ```diff).");
        sb.AppendLine("- Return ONLY a JSON object. No prose. No commentary.");
        sb.AppendLine();

        sb.AppendLine("TICKET:");
        sb.AppendLine($"  Title: {ctx.TicketTitle}");
        sb.AppendLine($"  Summary: {ctx.TicketSummary}");
        if (!string.IsNullOrWhiteSpace(ctx.TicketAcceptanceCriteria))
            sb.AppendLine($"  Acceptance Criteria: {ctx.TicketAcceptanceCriteria}");
        if (!string.IsNullOrWhiteSpace(ctx.TicketImplementationNotes))
            sb.AppendLine($"  Implementation Notes: {ctx.TicketImplementationNotes}");
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
            sb.AppendLine();
        }

        if (ctx.AffectedFiles.Count > 0)
        {
            sb.AppendLine("AFFECTED FILES:");
            foreach (var f in ctx.AffectedFiles)
                sb.AppendLine($"  - {f}");
            sb.AppendLine();
        }

        if (ctx.Standards.Count > 0)
        {
            sb.AppendLine("PROJECT RULES AND STANDARDS:");
            foreach (var s in ctx.Standards)
            {
                sb.AppendLine($"  - [{s.EnforcementLevel}] {s.Name}: {s.Description}");
            }
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
              "summary": "<brief description>",
              "rationale": "<why these changes were chosen>",
              "changes": [
                {
                  "filePath": "<relative path, e.g. src/Service.cs>",
                  "description": "<why this file changes>",
                  "diff": "--- a/src/Service.cs\n+++ b/src/Service.cs\n@@ ...",
                  "fullContentAfter": "<the COMPLETE content of the file after all changes are applied>",
                  "isNewFile": false,
                  "isDeletion": false
                }
              ]
            }
            """);
        sb.AppendLine("Return ONLY the JSON. No markdown.");

        return sb.ToString();
    }

    // ── JSON parsing ──────────────────────────────────────────────────────────

    public static CodeChangeProposal ParseProposal(string rawJson, long ticketId)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new InvalidOperationException("LLM returned an empty response.");

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
            TicketId  = ticketId,
            Summary   = dto.Summary   ?? "No summary provided.",
            Rationale = dto.Rationale ?? string.Empty,
            RiskNotes = "Generated in proposal-only mode.",
            TestPlan  = "Review diffs in workbench.",
        };

        foreach (var fc in dto.Changes ?? [])
        {
            proposal.FileChanges.Add(new FileChangeProposal
            {
                FilePath      = fc.FilePath      ?? string.Empty,
                ChangeReason  = fc.Description   ?? string.Empty,
                Patch         = fc.Diff          ?? string.Empty,
                FullContentAfter = fc.FullContentAfter ?? string.Empty,
            });
        }

        return proposal;
    }

    private static string StripMarkdownFences(string text)
    {
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    private sealed class ProposalJson
    {
        [JsonPropertyName("summary")]    public string?                 Summary     { get; set; }
        [JsonPropertyName("rationale")]  public string?                 Rationale   { get; set; }
        [JsonPropertyName("changes")]    public List<FileChangeJson>?   Changes     { get; set; }
    }

    private sealed class FileChangeJson
    {
        [JsonPropertyName("filePath")]    public string? FilePath    { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("diff")]        public string? Diff        { get; set; }
        [JsonPropertyName("fullContentAfter")] public string? FullContentAfter { get; set; }
        [JsonPropertyName("isNewFile")]   public bool    IsNewFile   { get; set; }
        [JsonPropertyName("isDeletion")]  public bool    IsDeletion  { get; set; }
    }
}
