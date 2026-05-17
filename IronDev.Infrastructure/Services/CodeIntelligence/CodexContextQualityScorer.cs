using System;
using System.Collections.Generic;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services.CodeIntelligence;

public sealed class CodexContextQualityScorer : ICodexContextQualityScorer
{
    public CodexContextQualityResult Score(CodexProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var score = 0;
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(snapshot.SolutionPath))
            score += 20;
        else
            reasons.Add("No solution or project path was found.");

        if (snapshot.Files.Count > 0)
            score += 20;
        else
            reasons.Add("No indexed files were available.");

        if (snapshot.Symbols.Count > 0)
            score += 30;
        else
            reasons.Add("No Roslyn semantic symbols were available.");

        if (snapshot.Decisions.Count > 0)
            score += 15;
        else
            reasons.Add("No project decisions were loaded.");

        if (snapshot.ExistingTickets.Count > 0)
            score += 10;
        else
            reasons.Add("No existing tickets were loaded.");

        if (snapshot.SemanticWarnings.Count == 0)
        {
            score += 5;
        }
        else
        {
            score -= 10;
            reasons.Add("Semantic index completed with warnings.");
            foreach (var warning in snapshot.SemanticWarnings)
                reasons.Add($"Semantic warning: {warning}");
        }

        return new CodexContextQualityResult
        {
            Score = Math.Clamp(score, 0, 100),
            MissingContextReasons = reasons
        };
    }
}
