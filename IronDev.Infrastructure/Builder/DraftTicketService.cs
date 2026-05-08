using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Services;

namespace IronDev.Infrastructure.Builder;

/// <summary>
/// Assembles a rich ProjectContext from memory, decisions, and index data
/// before asking the LLM to produce a structured IronDev ticket.
/// </summary>
internal sealed record ProjectContext
{
    public string CodeStandards               { get; init; } = string.Empty;
    public string RecentDecisions             { get; init; } = string.Empty;
    public string RelevantImplementationPlans { get; init; } = string.Empty;
    public string CodeIndexSummary            { get; init; } = string.Empty;
    public string RelevantFiles               { get; init; } = string.Empty;
    public string RelevantSymbols             { get; init; } = string.Empty;
    public string ProjectIndexStatus          { get; init; } = string.Empty;
    public string ContextQuality              { get; init; } = "Missing";
}

/// <summary>
/// Phase 4 implementation of IDraftTicketService.
///
/// Assembles rich ProjectContext (decisions, summaries, code index) and uses
/// ILLMService to generate IronDev-specific structured ticket drafts.
/// </summary>
public sealed class DraftTicketService : IDraftTicketService
{
    private readonly ILLMService           _llm;
    private readonly IProjectMemoryService _memory;

    public DraftTicketService(ILLMService llm, IProjectMemoryService memory)
    {
        _llm    = llm;
        _memory = memory;
    }

    public async Task<DraftTicket> GenerateDraftAsync(
        int    projectId,
        string projectName,
        string proposedTitle,
        string messageText,
        string? linkedFilePaths,
        string? linkedSymbols,
        CancellationToken ct = default)
    {
        // ── Build ProjectContext ──────────────────────────────────────────────
        var decisions = await _memory.GetRecentDecisionsAsync(projectId, 8, ct);
        var summary   = await _memory.GetLatestSummaryAsync(projectId, ct);

        var recentDecisions = decisions.Count > 0
            ? string.Join("\n", decisions.Select(d => $"- {d.Title}: {d.Detail}"))
            : "No decisions recorded yet.";

        var codeIndexSummary = summary?.Summary ?? string.Empty;
        var hasIndex         = !string.IsNullOrWhiteSpace(codeIndexSummary);

        // Derive relevant files/symbols from linkedFilePaths and linkedSymbols
        // (provided by the Chat grounding pipeline via ChatTicketContext)
        var relevantFiles = !string.IsNullOrWhiteSpace(linkedFilePaths)
            ? string.Join("\n", linkedFilePaths.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(f => $"- {f.Trim()}"))
            : "(no indexed file context available)";
        var relevantSymbols = !string.IsNullOrWhiteSpace(linkedSymbols)
            ? string.Join("\n", linkedSymbols.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => $"- {s.Trim()}"))
            : "(no indexed symbol context available)";

        var contextQuality = hasIndex && !string.IsNullOrWhiteSpace(linkedFilePaths)
            ? "Indexed"
            : hasIndex ? "Limited" : "Missing";

        var context = new ProjectContext
        {
            CodeStandards               = "C# 12 / .NET 10 / WPF MVVM. Dapper for SQL. No EF Core. No Weaviate. Tenant-scoped data access.",
            RecentDecisions             = recentDecisions,
            RelevantImplementationPlans = "(not loaded in this context)",
            CodeIndexSummary            = hasIndex ? codeIndexSummary : "Project has not been indexed yet.",
            RelevantFiles               = relevantFiles,
            RelevantSymbols             = relevantSymbols,
            ProjectIndexStatus          = hasIndex ? "Ready" : "Not Indexed",
            ContextQuality              = contextQuality,
        };

        // ── Build Chat History ───────────────────────────────────────────────
        var chatHistory = string.IsNullOrWhiteSpace(proposedTitle)
            ? messageText
            : $"Proposed title: {proposedTitle}\n\n{messageText}";

        var prompt = BuildDraftPrompt(projectName, chatHistory, context);

        string rawJson;
        try
        {
            rawJson = await _llm.GetResponseAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"AI draft generation failed: {ex.Message}", ex);
        }

        var draft = ParseDraft(rawJson);

        // Enrich with context-specific data not derived from LLM
        draft.LinkedFilePaths = linkedFilePaths;
        draft.LinkedSymbols   = linkedSymbols;
        draft.IsGenerated     = true;
        draft.GenerationNote  = contextQuality == "Missing"
            ? "⚠️ Generated without indexed context. File/class names may not be accurate."
            : "Generated by AI.";

        return draft;
    }

    public async Task<DraftTicket> RegenerateTestsAsync(
        int         projectId,
        DraftTicket current,
        CancellationToken ct = default)
    {
        var prompt = BuildTestRegenPrompt(current);

        string rawJson;
        try
        {
            rawJson = await _llm.GetResponseAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"AI test regeneration failed: {ex.Message}", ex);
        }

        var testPlan = ParseTestPlan(rawJson);

        return new DraftTicket
        {
            SourceChatSessionId = current.SourceChatSessionId,
            SourceMessageId     = current.SourceMessageId,
            SourceMessageText   = current.SourceMessageText,
            Title               = current.Title,
            TicketType          = current.TicketType,
            Priority            = current.Priority,
            Status              = current.Status,
            Summary             = current.Summary,
            Background          = current.Background,
            AcceptanceCriteria  = current.AcceptanceCriteria,
            LinkedFilePaths     = current.LinkedFilePaths,
            LinkedSymbols       = current.LinkedSymbols,

            UnitTests        = testPlan.UnitTests        ?? string.Empty,
            IntegrationTests = testPlan.IntegrationTests ?? string.Empty,
            ManualTests      = testPlan.ManualTests      ?? string.Empty,
            RegressionTests  = testPlan.RegressionTests  ?? string.Empty,
            BuildValidation  = testPlan.BuildValidation  ?? string.Empty,

            IsGenerated    = true,
            GenerationNote = "Tests regenerated by AI."
        };
    }

    // ── Prompt Building ──────────────────────────────────────────────────────

    internal static string BuildDraftPrompt(
        string projectName,
        string chatHistory,
        ProjectContext context)
    {
        return $$"""
You are IronDev Architect — a senior .NET/WPF architect and persistent teammate for this exact project.

Project:
{{projectName}} — a persistent AI development cockpit for chat, project memory, structured tickets,
decisions, implementation plans, local code indexing, and safe build proposals.

You are NOT a generic coding assistant.
You are working inside this specific {{projectName}} codebase.

Current standards and rules:
{{context.CodeStandards}}

Recent decisions and project memory:
{{context.RecentDecisions}}

Relevant implementation plans, if any:
{{context.RelevantImplementationPlans}}

Indexed codebase summary:
{{context.CodeIndexSummary}}

Relevant files from the local code index:
{{context.RelevantFiles}}

Relevant symbols/classes from the local code index:
{{context.RelevantSymbols}}

Project index status:
{{context.ProjectIndexStatus}}

Context quality:
{{context.ContextQuality}}

Chat history that triggered this ticket:
{{chatHistory}}

Task:
Turn the above chat into one high-quality, actionable {{projectName}} ticket.

Critical rules:
- Never give generic advice.
- Always tie the ticket back to {{projectName}}'s actual architecture.
- Use real files/classes from the indexed codebase when available.
- Do not invent file names, class names, service names, or database tables.
- Do not assume DraftTicket is the saved ticket model. DraftTicket is only for Chat → Draft Ticket review.
  For saved ticket management, prefer ProjectTicket, TicketsWorkspaceViewModel, and TicketService when available.
- If indexed context is missing or limited, say so clearly in contextWarning. Do not pretend to have file-level context.
- Keep the ticket small enough for one feature branch.
- The ticket must be suitable for the Build This workflow.
- Include tests/validation expectations.
- Do not include Weaviate as a required dependency.

Requirements for the ticket:
- Title must be specific and actionable.
- Summary must describe the problem clearly in one line.
- Requirements must describe what the solution must do.
- Acceptance criteria must be concrete and testable.
- Implementation notes must reference real files/classes where relevant.
- Affected files must use actual paths from the indexed context when available.
- Linked symbols must use actual symbols/classes from the indexed context when available.
- Test plan must include unit, integration, UI/manual, regression, and build validation.
- Include risks and non-goals.

Return ONLY a valid JSON object.
No markdown fences.
No commentary before or after the JSON.

{
  "title": "...",
  "summary": "...",
  "requirements": ["..."],
  "acceptanceCriteria": ["..."],
  "implementationNotes": "...",
  "affectedFiles": ["path/to/real/file.cs"],
  "linkedSymbols": ["RealClassName"],
  "testPlan": {
    "unitTests": ["..."],
    "integrationTests": ["..."],
    "manualTests": ["..."],
    "regressionTests": ["..."],
    "buildValidation": [
      "dotnet build IronDev.slnx",
      "dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --settings IronDev.IntegrationTests/integration.runsettings"
    ]
  },
  "type": "Feature|TechDebt|Bug|Improvement",
  "priority": "High|Medium|Low",
  "risks": ["..."],
  "nonGoals": ["..."],
  "contextQuality": "Indexed|Limited|Missing",
  "contextWarning": ""
}

Be concise, professional, and extremely specific to this {{projectName}} codebase.
""";
    }

    private static string BuildTestRegenPrompt(DraftTicket current)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are IronDev AI, a QA engineer.");
        sb.AppendLine();
        sb.AppendLine("TICKET DETAILS:");
        sb.AppendLine($"Title: {current.Title}");
        sb.AppendLine($"Summary: {current.Summary}");
        sb.AppendLine($"Acceptance Criteria: {current.AcceptanceCriteria}");
        sb.AppendLine();
        sb.AppendLine("TASK:");
        sb.AppendLine("Generate a comprehensive test plan for this ticket.");
        sb.AppendLine("Return ONLY a JSON object matching this schema:");
        sb.AppendLine("""
            {
              "unitTests": "Markdown list",
              "integrationTests": "Markdown list",
              "manualTests": "Steps to verify",
              "regressionTests": "Potential impact areas",
              "buildValidation": "dotnet build IronDev.slnx"
            }
            """);
        return sb.ToString();
    }

    // ── JSON Parsing ──────────────────────────────────────────────────────────

    private static DraftTicket ParseDraft(string json)
    {
        var dto = JsonSerializer.Deserialize<DraftTicketJson>(StripFences(json), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to parse ticket JSON.");

        // acceptanceCriteria — accept array (new) or plain string (legacy)
        var criteria = dto.AcceptanceCriteriaArray is { Count: > 0 }
            ? string.Join("\n", dto.AcceptanceCriteriaArray.Select(c => $"- {c}"))
            : dto.AcceptanceCriteria ?? string.Empty;

        // requirements → Background; fall back to description/background legacy fields
        var background = dto.Requirements is { Count: > 0 }
            ? string.Join("\n", dto.Requirements.Select(r => $"- {r}"))
            : dto.Description ?? dto.Background ?? string.Empty;

        // Append affected files to Background
        if (dto.AffectedFiles is { Count: > 0 })
        {
            var fileList = "\n\n**Affected files:**\n" + string.Join("\n", dto.AffectedFiles.Select(f => $"- {f}"));
            background += fileList;
        }

        // Implementation notes + optional context warning
        var technicalNotes = dto.ImplementationNotes ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dto.ContextWarning))
            technicalNotes = $"⚠️ {dto.ContextWarning}\n\n{technicalNotes}".TrimEnd();

        // Flatten testPlan from new schema; fall back to flat legacy fields
        var unitTests        = dto.TestPlan?.UnitTestsList        is { Count: > 0 } ? string.Join("\n", dto.TestPlan.UnitTestsList.Select(t        => $"- {t}")) : dto.UnitTests        ?? string.Empty;
        var integrationTests = dto.TestPlan?.IntegrationTestsList is { Count: > 0 } ? string.Join("\n", dto.TestPlan.IntegrationTestsList.Select(t => $"- {t}")) : dto.IntegrationTests ?? string.Empty;
        var manualTests      = dto.TestPlan?.ManualTestsList      is { Count: > 0 } ? string.Join("\n", dto.TestPlan.ManualTestsList.Select(t      => $"- {t}")) : dto.ManualTests      ?? string.Empty;
        var regressionTests  = dto.TestPlan?.RegressionTestsList  is { Count: > 0 } ? string.Join("\n", dto.TestPlan.RegressionTestsList.Select(t  => $"- {t}")) : dto.RegressionTests  ?? string.Empty;
        var buildValidation  = dto.TestPlan?.BuildValidationList  is { Count: > 0 } ? string.Join("\n", dto.TestPlan.BuildValidationList)                         : dto.BuildValidation  ?? "dotnet build IronDev.slnx";

        return new DraftTicket
        {
            Title              = dto.Title              ?? "Untitled Ticket",
            Summary            = dto.Summary            ?? string.Empty,
            Background         = background,
            AcceptanceCriteria = criteria,
            UnitTests          = unitTests,
            IntegrationTests   = integrationTests,
            ManualTests        = manualTests,
            RegressionTests    = regressionTests,
            BuildValidation    = buildValidation,
        };
    }

    private static TestPlanJson ParseTestPlan(string json)
    {
        return JsonSerializer.Deserialize<TestPlanJson>(StripFences(json), JsonOptions)
               ?? throw new InvalidOperationException("Failed to parse test plan JSON.");
    }

    private static string StripFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var start = text.IndexOf('\n');
            var end   = text.LastIndexOf("```");
            if (start != -1 && end != -1 && end > start)
                return text.Substring(start + 1, end - start - 1).Trim();
        }
        return text;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class DraftTicketJson
    {
        public string?       Title               { get; set; }
        public string?       Summary             { get; set; }
        // New schema fields
        [JsonPropertyName("requirements")]
        public List<string>? Requirements        { get; set; }
        public string?       ImplementationNotes { get; set; }
        public string?       ContextQuality      { get; set; }
        public string?       ContextWarning      { get; set; }
        // Legacy fallback fields
        public string?       Description         { get; set; }
        public string?       Background          { get; set; }
        public string?       AcceptanceCriteria  { get; set; }
        [JsonPropertyName("acceptanceCriteria")]
        public List<string>? AcceptanceCriteriaArray { get; set; }
        public string?       Type                { get; set; }
        public string?       Priority            { get; set; }
        [JsonPropertyName("affectedFiles")]
        public List<string>? AffectedFiles       { get; set; }
        [JsonPropertyName("linkedSymbols")]
        public List<string>? LinkedSymbols       { get; set; }
        // New structured testPlan (replaces flat fields)
        [JsonPropertyName("testPlan")]
        public TestPlanJsonNested? TestPlan      { get; set; }
        // Legacy flat test fields (kept for backward compat with old LLM responses)
        public string?       UnitTests           { get; set; }
        public string?       IntegrationTests    { get; set; }
        public string?       ManualTests         { get; set; }
        public string?       RegressionTests     { get; set; }
        public string?       BuildValidation     { get; set; }
    }

    private sealed class TestPlanJsonNested
    {
        [JsonPropertyName("unitTests")]
        public List<string>? UnitTestsList        { get; set; }
        [JsonPropertyName("integrationTests")]
        public List<string>? IntegrationTestsList { get; set; }
        [JsonPropertyName("manualTests")]
        public List<string>? ManualTestsList      { get; set; }
        [JsonPropertyName("regressionTests")]
        public List<string>? RegressionTestsList  { get; set; }
        [JsonPropertyName("buildValidation")]
        public List<string>? BuildValidationList  { get; set; }
    }

    private sealed class TestPlanJson
    {
        public string? UnitTests        { get; set; }
        public string? IntegrationTests { get; set; }
        public string? ManualTests      { get; set; }
        public string? RegressionTests  { get; set; }
        public string? BuildValidation  { get; set; }
    }
}
