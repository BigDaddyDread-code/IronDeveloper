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
using IronDev.Core.Models;
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
    private readonly ILlmTraceService      _llmTraceService;

    public DraftTicketService(ILLMService llm, IProjectMemoryService memory, ILlmTraceService llmTraceService)
    {
        _llm    = llm;
        _memory = memory;
        _llmTraceService = llmTraceService;
    }

    public async Task<DraftTicket> GenerateDraftAsync(
        int    projectId,
        string projectName,
        string proposedTitle,
        string messageText,
        string? linkedFilePaths,
        string? linkedSymbols,
        long?   sessionId = null,
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
            CodeStandards               = "C# 12 / .NET 10 API/client/Core/Infrastructure boundary. Tauri shell for current UI. Dapper for SQL. No EF Core. Tenant-scoped data access.",
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

        // ── Call LLM with tracing ─────────────────────────────────────────────
        var trace = new LlmTraceEntry
        {
            FeatureName = "DraftTicketGeneration",
            WorkspaceName = "Architect",
            ProjectId = projectId,
            ChatSessionId = sessionId?.ToString(),
            TraceGroupId = sessionId?.ToString(),
            RequestText = prompt,
            CurrentUserMessage = messageText,
            CreatedAt = DateTime.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string rawJson;
        try
        {
            rawJson = await _llm.GetResponseAsync(prompt, ct);
            trace.WasSuccessful = true;
            trace.RawResponseText = rawJson;
        }
        catch (Exception ex)
        {
            trace.WasSuccessful = false;
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);
            throw new InvalidOperationException($"AI draft generation failed: {ex.Message}", ex);
        }
        finally
        {
            sw.Stop();
            trace.DurationMs = sw.ElapsedMilliseconds;
        }

        // ── Diagnostics before deserialisation ──────────────────────────────
        System.Diagnostics.Trace.WriteLine("[DraftTicketService.GenerateDraftAsync] LLM response received.");
        System.Diagnostics.Trace.WriteLine($"[DraftTicketService] Response empty: {string.IsNullOrWhiteSpace(rawJson)}");
        var preview = rawJson?.Length > 300 ? rawJson[..300] + "..." : rawJson ?? "(null)";
        System.Diagnostics.Trace.WriteLine($"[DraftTicketService] Raw preview: {preview}");

        DraftTicket draft;
        try
        {
            draft = ParseDraft(rawJson ?? string.Empty);
            trace.ParsedResponseSummary = $"Generated ticket: {draft.Title}";
            _llmTraceService.AddTrace(trace);
        }
        catch (Exception ex)
        {
            trace.ParsedResponseSummary = "JSON Parse Failure";
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);

            System.Diagnostics.Trace.WriteLine(
                $"[DraftTicketService] Deserialisation failed — target: DraftTicketJson — error: {ex.Message}");
            throw new InvalidOperationException(
                $"Draft ticket JSON deserialization failed: {ex.Message}. " +
                $"Raw response preview: {preview}", ex);
        }

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

        // ── Call LLM with tracing ─────────────────────────────────────────────
        var trace = new LlmTraceEntry
        {
            FeatureName = "GenerateTests",
            WorkspaceName = "Architect",
            ProjectId = projectId,
            ChatSessionId = current.SourceChatSessionId.ToString(),
            TraceGroupId = current.SourceChatSessionId.ToString(),
            RequestText = prompt,
            CreatedAt = DateTime.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string rawJson;
        try
        {
            rawJson = await _llm.GetResponseAsync(prompt, ct);
            trace.WasSuccessful = true;
            trace.RawResponseText = rawJson;
        }
        catch (Exception ex)
        {
            trace.WasSuccessful = false;
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);
            throw new InvalidOperationException($"AI test regeneration failed: {ex.Message}", ex);
        }
        finally
        {
            sw.Stop();
            trace.DurationMs = sw.ElapsedMilliseconds;
        }

        TestPlanJson testPlan;
        try
        {
            testPlan = ParseTestPlan(rawJson);
            trace.ParsedResponseSummary = "Tests generated.";
            _llmTraceService.AddTrace(trace);
        }
        catch (Exception ex)
        {
            trace.ParsedResponseSummary = "JSON Parse Failure";
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);
            throw;
        }

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
            ImplementationPlan  = current.ImplementationPlan,

            UnitTests        = testPlan.UnitTests        ?? string.Empty,
            IntegrationTests = testPlan.IntegrationTests ?? string.Empty,
            ManualTests      = testPlan.ManualTests      ?? string.Empty,
            RegressionTests  = testPlan.RegressionTests  ?? string.Empty,
            BuildValidation  = testPlan.BuildValidation  ?? string.Empty,

            IsGenerated    = true,
            GenerationNote = "Tests regenerated by AI."
        };
    }

    public async Task<DraftTicket> GeneratePlanAsync(
        int         projectId,
        DraftTicket current,
        CancellationToken ct = default)
    {
        var prompt = BuildPlanPrompt(current);

        // ── Call LLM with tracing ─────────────────────────────────────────────
        var trace = new LlmTraceEntry
        {
            FeatureName = "GenerateImplementationPlan",
            WorkspaceName = "Architect",
            ProjectId = projectId,
            ChatSessionId = current.SourceChatSessionId.ToString(),
            TraceGroupId = current.SourceChatSessionId.ToString(),
            RequestText = prompt,
            CreatedAt = DateTime.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string rawJson;
        try
        {
            rawJson = await _llm.GetResponseAsync(prompt, ct);
            trace.WasSuccessful = true;
            trace.RawResponseText = rawJson;
        }
        catch (Exception ex)
        {
            trace.WasSuccessful = false;
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);
            throw new InvalidOperationException($"AI plan generation failed: {ex.Message}", ex);
        }
        finally
        {
            sw.Stop();
            trace.DurationMs = sw.ElapsedMilliseconds;
        }

        PlanJson planData;
        try
        {
            planData = ParsePlan(rawJson);
            trace.ParsedResponseSummary = "Implementation plan generated.";
            _llmTraceService.AddTrace(trace);
        }
        catch (Exception ex)
        {
            trace.ParsedResponseSummary = "JSON Parse Failure";
            trace.ErrorMessage = ex.Message;
            _llmTraceService.AddTrace(trace);
            throw;
        }

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

            UnitTests        = current.UnitTests,
            IntegrationTests = current.IntegrationTests,
            ManualTests      = current.ManualTests,
            RegressionTests  = current.RegressionTests,
            BuildValidation  = current.BuildValidation,

            ImplementationPlan = planData.ImplementationPlan ?? string.Empty,

            IsGenerated    = true,
            GenerationNote = "Implementation plan generated by AI."
        };
    }


    // ── Prompt Building ──────────────────────────────────────────────────────

    internal static string BuildDraftPrompt(
        string projectName,
        string chatHistory,
        ProjectContext context)
    {
        return $$"""
You are IronDev Architect — a senior .NET/API/client architect and persistent teammate for this exact project.

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
  For saved ticket management, prefer ProjectTicket, TicketsController, IronDev.Client ticket methods, Tauri ticket components, and TicketService when available.
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
  "implementationPlan": ["step 1", "step 2"],
  "codeContext": {
    "affectedFiles": ["path/to/real/file.cs"],
    "linkedSymbols": ["ClassName", "MethodName"],
    "summary": "Technical overview of why these files are affected.",
    "notes": "Any specific implementation constraints."
  },
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

    internal static string BuildPlanPrompt(DraftTicket current)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are IronDev AI, a technical architect.");
        sb.AppendLine();
        sb.AppendLine("TICKET DETAILS:");
        sb.AppendLine($"Title: {current.Title}");
        sb.AppendLine($"Summary: {current.Summary}");
        sb.AppendLine($"Acceptance Criteria: {current.AcceptanceCriteria}");
        if (!string.IsNullOrWhiteSpace(current.LinkedFilePaths))
            sb.AppendLine($"Linked Files: {current.LinkedFilePaths}");
        if (!string.IsNullOrWhiteSpace(current.UnitTests) || !string.IsNullOrWhiteSpace(current.IntegrationTests))
        {
            sb.AppendLine();
            sb.AppendLine("TEST PLAN:");
            if (!string.IsNullOrWhiteSpace(current.UnitTests))        sb.AppendLine($"Unit Tests: {current.UnitTests}");
            if (!string.IsNullOrWhiteSpace(current.IntegrationTests)) sb.AppendLine($"Integration Tests: {current.IntegrationTests}");
            if (!string.IsNullOrWhiteSpace(current.ManualTests))      sb.AppendLine($"Manual Tests: {current.ManualTests}");
        }
        sb.AppendLine();
        sb.AppendLine("TASK:");
        sb.AppendLine("Generate a concrete technical implementation plan for this ticket.");
        sb.AppendLine("Break it down into small, actionable steps.");
        sb.AppendLine("Return ONLY a JSON object matching this schema:");
        sb.AppendLine("""
            {
              "implementationPlan": ["step 1", "step 2", "..."]
            }
            """);
        return sb.ToString();
    }

    // ── JSON Parsing ──────────────────────────────────────────────────────────

    private static DraftTicket ParseDraft(string json)
    {
        var dto = JsonSerializer.Deserialize<DraftTicketJson>(StripFences(json), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to parse ticket JSON.");

        // ── acceptanceCriteria — accepts string, array, or legacy acceptanceCriteriaText ─
        var criteria = ResolveStringOrArray(dto.AcceptanceCriteriaRaw, prefix: "- ")
                       ?? dto.AcceptanceCriteriaText
                       ?? string.Empty;

        // ── requirements → Background; fall back to implementationPlan[], description, background ─
        var background = dto.Requirements is { Count: > 0 }
            ? string.Join("\n", dto.Requirements.Select(r => $"- {r}"))
            : dto.Description ?? dto.Background ?? string.Empty;

        // implementationPlan (new schema: array)
        var implementationPlan = ResolveStringOrArray(dto.ImplementationPlanRaw, prefix: "- ");

        // If background is empty, use implementation plan as fallback summary
        if (string.IsNullOrWhiteSpace(background) && !string.IsNullOrWhiteSpace(implementationPlan))
        {
             background = implementationPlan;
        }

        // ── affectedFiles — from codeContext.affectedFiles or top-level affectedFiles ─
        var affectedFilesList = dto.CodeContext?.AffectedFiles ?? dto.AffectedFiles;
        var linkedFilePaths = affectedFilesList is { Count: > 0 }
            ? string.Join("\n", affectedFilesList.Distinct())
            : null;

        // ── linkedSymbols — from codeContext.linkedSymbols or top-level linkedSymbols ─
        var linkedSymbolsList = dto.CodeContext?.LinkedSymbols ?? dto.LinkedSymbolsList;
        var linkedSymbolsText = linkedSymbolsList is { Count: > 0 }
            ? string.Join("\n", linkedSymbolsList.Distinct())
            : null;

        // codeContext.summary or codeContext.notes → append to background
        var contextSummary = dto.CodeContext?.Summary ?? dto.CodeContext?.Notes;
        if (!string.IsNullOrWhiteSpace(contextSummary))
            background = $"{background.Trim()}\n\n**Code Context Summary:** {contextSummary.Trim()}".Trim();

        // Implementation notes + optional context warning
        var implementationNotes = dto.ImplementationNotes ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dto.ContextWarning))
            implementationNotes = $"⚠️ {dto.ContextWarning}\n\n{implementationNotes}".TrimEnd();

        // Flatten testPlan from new schema; fall back to flat legacy fields; then to tests[] array
        var unitTests        = dto.TestPlan?.UnitTestsList        is { Count: > 0 } ? string.Join("\n", dto.TestPlan.UnitTestsList.Select(t        => $"- {t}")) : dto.UnitTests        ?? string.Empty;
        var integrationTests = dto.TestPlan?.IntegrationTestsList is { Count: > 0 } ? string.Join("\n", dto.TestPlan.IntegrationTestsList.Select(t => $"- {t}")) : dto.IntegrationTests ?? string.Empty;
        var manualTests      = dto.TestPlan?.ManualTestsList      is { Count: > 0 } ? string.Join("\n", dto.TestPlan.ManualTestsList.Select(t      => $"- {t}")) : dto.ManualTests      ?? string.Empty;
        var regressionTests  = dto.TestPlan?.RegressionTestsList  is { Count: > 0 } ? string.Join("\n", dto.TestPlan.RegressionTestsList.Select(t  => $"- {t}")) : dto.RegressionTests  ?? string.Empty;
        var buildValidation  = dto.TestPlan?.BuildValidationList  is { Count: > 0 } ? string.Join("\n", dto.TestPlan.BuildValidationList)                         : dto.BuildValidation  ?? "dotnet build IronDev.slnx";

        // tests[] (new flat schema) → fold into manualTests if no testPlan
        if (string.IsNullOrWhiteSpace(manualTests) && dto.TestsList is { Count: > 0 })
            manualTests = string.Join("\n", dto.TestsList.Select(t => $"- {t}"));

        // Resolve type: prefer explicit ticketType, then type, then default
        var ticketType = NormaliseTicketType(dto.TicketType ?? dto.Type);
        var priority   = NormalisePriority(dto.Priority);
        var status     = dto.Status ?? "Draft";

        var title = dto.Title ?? "Untitled Ticket";
        title = CleanupTypos(title);

        return new DraftTicket
        {
            Title              = title,
            TicketType         = ticketType,
            Priority           = priority,
            Status             = status,
            Summary            = dto.Summary            ?? string.Empty,
            Background         = background,
            AcceptanceCriteria = criteria,
            ImplementationPlan = implementationPlan,
            LinkedFilePaths    = linkedFilePaths,
            LinkedSymbols      = linkedSymbolsText,
            UnitTests          = unitTests,
            IntegrationTests   = integrationTests,
            ManualTests        = manualTests,
            RegressionTests    = regressionTests,
            BuildValidation    = buildValidation,
        };
    }

    /// <summary>
    /// Resolves a JSON field that can be either a string or a string array
    /// (deserialized as <see cref="System.Text.Json.JsonElement"/> when using object?)
    /// into a flat string.  Returns null if the value is null/empty.
    /// </summary>
    private static string? ResolveStringOrArray(object? raw, string prefix = "")
    {
        if (raw is null) return null;

        // System.Text.Json deserialises into JsonElement when the target is object
        if (raw is System.Text.Json.JsonElement elem)
        {
            if (elem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = elem.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            if (elem.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var items = elem.EnumerateArray()
                               .Select(e => e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null)
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .Select(s => $"{prefix}{s}")
                               .ToList();
                return items.Count > 0 ? string.Join("\n", items) : null;
            }
        }

        if (raw is string str)
            return string.IsNullOrWhiteSpace(str) ? null : str;

        return null;
    }

    private static readonly string[] _validTypes = ["Task", "Bug", "Feature", "Spike", "Chore"];

    /// <summary>Normalise LLM-returned type string to a value accepted by the TypeOptions dropdown.</summary>
    private static string NormaliseTicketType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Task";
        // Direct match (case-insensitive)
        var match = _validTypes.FirstOrDefault(t => string.Equals(t, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;
        // Map common LLM synonyms
        var lower = raw.Trim().ToLowerInvariant();
        if (lower.Contains("bug") || lower.Contains("fix"))          return "Bug";
        if (lower.Contains("feature") || lower.Contains("improve"))  return "Feature";
        if (lower.Contains("spike") || lower.Contains("research"))   return "Spike";
        if (lower.Contains("chore") || lower.Contains("maintenance")) return "Chore";
        if (lower.Contains("tech") || lower.Contains("debt") || lower.Contains("refactor")) return "Chore";
        return "Task";
    }

    private static readonly string[] _validPriorities = ["Low", "Medium", "High", "Critical"];

    /// <summary>Normalise LLM-returned priority string.</summary>
    private static string NormalisePriority(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Medium";
        var match = _validPriorities.FirstOrDefault(p => string.Equals(p, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? "Medium";
    }

    private static TestPlanJson ParseTestPlan(string json)
    {
        return JsonSerializer.Deserialize<TestPlanJson>(StripFences(json), JsonOptions)
               ?? throw new InvalidOperationException("Failed to parse test plan JSON.");
    }

    private static PlanJson ParsePlan(string json)
    {
        var dto = JsonSerializer.Deserialize<PlanJson>(StripFences(json), JsonOptions)
               ?? throw new InvalidOperationException("Failed to parse plan JSON.");
        
        var plan = ResolveStringOrArray(dto.ImplementationPlanRaw, prefix: "- ");
        return new PlanJson { ImplementationPlan = plan };
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

        // Requirements — array (new schema) or plain string (legacy background field)
        [JsonPropertyName("requirements")]
        public List<string>? Requirements        { get; set; }

        public string?       ImplementationNotes { get; set; }
        public string?       ContextQuality      { get; set; }
        public string?       ContextWarning      { get; set; }

        // Legacy fallback fields
        public string?       Description         { get; set; }
        public string?       Background          { get; set; }

        // AcceptanceCriteria — disambiguated to avoid duplicate JSON property name collision.
        // System.Text.Json (PropertyNameCaseInsensitive=true) would map BOTH
        //   "public string? AcceptanceCriteria" and
        //   "[JsonPropertyName("acceptanceCriteria")] public List<string>? AcceptanceCriteriaArray"
        // to the same key "acceptanceCriteria" — a fatal duplicate that throws
        // "The JSON property name for '...AcceptanceCriteriaArray' collides with another property".
        // Give the string variant a distinct name so it is only used for compatibility string-shape responses.
        [JsonPropertyName("acceptanceCriteriaText")]
        public string?       AcceptanceCriteriaText  { get; set; }

        [JsonPropertyName("acceptanceCriteria")]
        public object?       AcceptanceCriteriaRaw   { get; set; }  // accepts both string and array

        // type / ticketType
        [JsonPropertyName("type")]
        public string?       Type                { get; set; }
        [JsonPropertyName("ticketType")]
        public string?       TicketType          { get; set; }

        public string?       Priority            { get; set; }
        public string?       Status              { get; set; }

        [JsonPropertyName("affectedFiles")]
        public List<string>? AffectedFiles       { get; set; }

        [JsonPropertyName("linkedSymbols")]
        public List<string>? LinkedSymbolsList   { get; set; }

        // implementationPlan — array (new schema) or compatibility string shape
        [JsonPropertyName("implementationPlan")]
        public object?       ImplementationPlanRaw { get; set; }

        // tests — array (new schema)
        [JsonPropertyName("tests")]
        public List<string>? TestsList           { get; set; }

        // codeContext object (new schema)
        [JsonPropertyName("codeContext")]
        public CodeContextJson? CodeContext      { get; set; }

        // New structured testPlan (replaces flat fields)
        [JsonPropertyName("testPlan")]
        public TestPlanJsonNested? TestPlan      { get; set; }

        // Compatibility flat test fields kept for older structured responses.
        public string?       UnitTests           { get; set; }
        public string?       IntegrationTests    { get; set; }
        public string?       ManualTests         { get; set; }
        public string?       RegressionTests     { get; set; }
        public string?       BuildValidation     { get; set; }
    }

    private sealed class CodeContextJson
    {
        [JsonPropertyName("affectedFiles")]
        public List<string>? AffectedFiles { get; set; }
        [JsonPropertyName("linkedSymbols")]
        public List<string>? LinkedSymbols { get; set; }

        [JsonPropertyName("summary")]
        public string?       Summary       { get; set; }

        [JsonPropertyName("notes")]
        public string?       Notes         { get; set; }
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

    private sealed class PlanJson
    {
        [JsonPropertyName("implementationPlan")]
        public object? ImplementationPlanRaw { get; set; }

        [JsonIgnore]
        public string? ImplementationPlan { get; set; }
    }

    private static string CleanupTypos(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        
        // Simple cleanup for known project entities
        return text
            .Replace("ProjectTickts", "ProjectTickets")
            .Replace("ProjectTickt", "ProjectTicket")
            .Replace("TicketServce", "TicketService")
            .Replace("TicketSerivce", "TicketService")
            .Replace("TicketsWorkspaceVM", "TicketsWorkspace")
            .Replace("IronDevControls", "IronDeveloperControls");
    }
}
