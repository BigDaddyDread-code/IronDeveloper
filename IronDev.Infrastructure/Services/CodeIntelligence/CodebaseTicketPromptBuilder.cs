using System.Linq;
using System.Text;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services.CodeIntelligence;

/// <inheritdoc cref="ICodebaseTicketPromptBuilder"/>
public sealed class CodebaseTicketPromptBuilder : ICodebaseTicketPromptBuilder
{
    public string Build(CodebaseTicketPromptInputs inputs)
    {
        var snapshot = inputs.Snapshot;
        var ctx = new StringBuilder();

        ctx.AppendLine("PROJECT CONTEXT:");
        ctx.AppendLine($"Project: {snapshot.ProjectName}");
        ctx.AppendLine($"Solution: {snapshot.SolutionPath}");
        ctx.AppendLine($"Context quality: {snapshot.ContextQualityScore}/100");

        if (snapshot.MissingContextReasons.Count > 0)
        {
            ctx.AppendLine("Missing or weak context:");
            foreach (var reason in snapshot.MissingContextReasons.Take(8))
                ctx.AppendLine($"- {reason}");
        }

        ctx.AppendLine("\nLANGUAGE QUALITY:");
        foreach (var quality in snapshot.LanguageQuality)
        {
            ctx.AppendLine(
                $"- {quality.LanguageId}: {quality.Confidence}, " +
                $"files={quality.FileCount}, symbols={quality.SymbolCount}. {quality.Notes}");
        }

        ctx.AppendLine("\nFILES:");
        foreach (var file in snapshot.Files.Take(80))
        {
            ctx.AppendLine(
                $"- {file.FilePath} ({file.LanguageId}, symbols={file.SymbolCount}, confidence={file.Confidence})");
        }

        ctx.AppendLine("\nSYMBOLS:");
        foreach (var symbol in snapshot.Symbols.Take(140))
        {
            var location  = symbol.StartLine is null ? symbol.FilePath : $"{symbol.FilePath}:{symbol.StartLine}";
            var container = string.IsNullOrWhiteSpace(symbol.ContainerName) ? string.Empty : $"{symbol.ContainerName}.";
            var qualified = string.IsNullOrWhiteSpace(symbol.FullyQualifiedName)
                ? $"{container}{symbol.Name}"
                : symbol.FullyQualifiedName;
            ctx.AppendLine($"- {symbol.LanguageId} {symbol.Kind} {qualified} @ {location}");
        }

        if (inputs.ProjectSummary != null)
            ctx.AppendLine($"Summary: {inputs.ProjectSummary}");
        else
            ctx.AppendLine("No project summary available.");

        if (inputs.RecentDecisions.Count > 0)
        {
            ctx.AppendLine("\nRECENT ARCHITECTURAL DECISIONS:");
            foreach (var d in inputs.RecentDecisions)
                ctx.AppendLine($"- {d}");
        }

        if (snapshot.ExistingTickets.Count > 0)
        {
            ctx.AppendLine("\nEXISTING TICKETS:");
            foreach (var ticket in snapshot.ExistingTickets.Take(20))
            {
                ctx.AppendLine(
                    $"- [{ticket.Status}/{ticket.Priority}] {ticket.Title}: {ticket.SummaryPreview}");
            }
        }

        if (inputs.ProjectRules.Count > 0)
        {
            ctx.AppendLine("\nPROJECT RULES AND STANDARDS:");
            foreach (var r in inputs.ProjectRules)
                ctx.AppendLine($"- {r}");
        }

        var prompt = new StringBuilder();
        prompt.AppendLine("You are IronDev's self-dogfood planner analyzing IronDev's own codebase and history.");
        prompt.AppendLine("Based only on the provided project snapshot and project memory, identify 5-8 technical improvements,");
        prompt.AppendLine("refactoring tasks, testing gaps, UX issues, or dogfood-loop improvements that would benefit the project.");
        prompt.AppendLine();
        prompt.AppendLine("For each item, output a structured ticket draft.");
        prompt.AppendLine("Return ONLY valid JSON matching this schema:");
        prompt.AppendLine("""
        {
          "drafts": [
            {
              "title": "string",
              "category": "UX|TechDebt|Architecture|Testing|Dogfood|Performance",
              "summary": "string",
              "problem": "string",
              "proposedChange": "string",
              "whyNow": "string",
              "background": "string",
              "acceptanceCriteria": "string",
              "priority": "Low|Medium|High|Critical",
              "ticketType": "Task|Bug|Feature|Spike|Chore",
              "affectedFiles": ["actual/path/from/snapshot.cs"],
              "affectedSymbols": ["ActualSymbolFromSnapshot"],
              "dependencies": ["title of prior ticket if any"],
              "suggestedBuildOrder": 1,
              "riskLevel": "Low|Medium|High",
              "confidenceScore": 0,
              "groundingWarnings": [],
              "testSuggestions": ["specific test or build validation"],
              "unitTests": "string",
              "integrationTests": "string",
              "manualTests": "string",
              "regressionTests": "string",
              "buildValidation": "dotnet build"
            }
          ]
        }
        """);
        prompt.AppendLine();
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Do not invent files. Use files from the FILES section.");
        prompt.AppendLine("- Do not invent symbols. Prefer symbols from the SYMBOLS section; omit symbols if unsure.");
        prompt.AppendLine("- Leave groundingWarnings empty. IronDev will validate grounding after generation.");
        prompt.AppendLine("- Rank tickets by suggestedBuildOrder.");
        prompt.AppendLine("- Prefer Alpha 0.1-sized improvements over giant rewrites.");
        prompt.AppendLine("- If context quality is weak, lower confidenceScore and explain the risk in background.");
        prompt.AppendLine("- Avoid duplicating existing tickets.");
        prompt.AppendLine();
        prompt.Append(ctx);
        prompt.AppendLine();
        prompt.AppendLine("Focus on actionable, specific improvements. Avoid generic advice.");

        return prompt.ToString();
    }
}
