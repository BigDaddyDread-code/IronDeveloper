namespace IronDev.Core.ChatProbe;

/// <summary>
/// Deterministic hard-check evaluator.
/// No LLM required — all checks are pattern-based or metadata-based.
/// </summary>
public sealed class HardProbeEvaluator
{
    /// <summary>
    /// Evaluate a completed turn against the scripted step expectations
    /// and the response content/metadata.
    /// </summary>
    public IReadOnlyList<ProbeFailure> Evaluate(
        ProbeStep? step,
        ProbeTurnResult turn,
        IReadOnlyList<ProbeTurnResult> previousTurns)
    {
        var failures = new List<ProbeFailure>();

        // ── 1. Mode gate checks ────────────────────────────────────────────────

        if (step?.ExpectedMode is not null && turn.Mode is not null)
        {
            if (!string.Equals(turn.Mode, step.ExpectedMode, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.WrongMode,
                    Message = $"Expected mode {step.ExpectedMode} but got {turn.Mode}. " +
                              $"User said: \"{Truncate(turn.UserMessage, 60)}\"",
                    IsHard  = true
                });
            }
        }

        // ── 2. Gate checks ─────────────────────────────────────────────────────

        if (step?.ExpectGateSaveDiscussion is not null)
        {
            var expected = step.ExpectGateSaveDiscussion.Value;
            if (turn.GateCanSaveDiscussion != expected)
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.WrongGate,
                    Message = $"Expected CanSaveDiscussion={expected} but got {turn.GateCanSaveDiscussion}. " +
                              $"Mode was {turn.Mode}.",
                    IsHard  = true
                });
            }
        }

        if (step?.ExpectGateCreateTicket is not null)
        {
            var expected = step.ExpectGateCreateTicket.Value;
            if (turn.GateCanCreateTicket != expected)
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.WrongGate,
                    Message = $"Expected CanCreateTicket={expected} but got {turn.GateCanCreateTicket}. " +
                              $"Mode was {turn.Mode}.",
                    IsHard  = true
                });
            }
        }

        // ── 3. Exploration mode gate leak ──────────────────────────────────────
        // Exploration mode must NOT enable save/ticket actions.

        if (string.Equals(turn.Mode, "Exploration", StringComparison.OrdinalIgnoreCase))
        {
            if (turn.GateCanSaveDiscussion)
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.WrongGate,
                    Message = "Exploration mode must not have CanSaveDiscussion=true.",
                    IsHard  = true
                });
            }

            if (turn.GateCanCreateTicket)
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.WrongGate,
                    Message = "Exploration mode must not have CanCreateTicket=true.",
                    IsHard  = true
                });
            }
        }

        // ── 4. Generic template leak ───────────────────────────────────────────
        // Specific internal strings should never appear in user-facing responses.

        var genericLeakPatterns = new[]
        {
            "[Exploration] Non-prose path triggered",
            "[Formalization] Non-prose path triggered",
            "governance mode selected:",
            "classifier reason:",
            "route hint:",
            "mode confidence:"
        };

        foreach (var leak in genericLeakPatterns)
        {
            if (turn.AssistantResponse.Contains(leak, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.GenericTemplateLeak,
                    Message = $"Internal governance string leaked into response: \"{leak}\"",
                    IsHard  = true
                });
            }
        }

        // ── 5. Overbuild detection ─────────────────────────────────────────────
        // If user said "online" (not multiplayer) but response recommends
        // OAuth + WebSockets + multiplayer, that's overbuild.

        var userLower = turn.UserMessage.ToLowerInvariant();
        var responseLower = turn.AssistantResponse.ToLowerInvariant();

        if (ContainsWordMatch(userLower, "online") &&
            !ContainsWordMatch(userLower, "multiplayer") &&
            !ContainsWordMatch(userLower, "multi-player"))
        {
            var overbuildSignals = new[] { "multiplayer", "oauth", "websocket", "real-time sync", "signalr" };
            var hitCount = overbuildSignals.Count(s => responseLower.Contains(s));
            if (hitCount >= 2)
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.OverbuiltArchitecture,
                    Message = $"User said 'online' (single-player intent) but response includes " +
                              $"overbuild signals: {string.Join(", ", overbuildSignals.Where(s => responseLower.Contains(s)))}",
                    IsHard  = false
                });
            }
        }

        // ── 6. Failed artifact extraction ──────────────────────────────────────
        // If user asked for an architecture doc from prior discussion,
        // the response must NOT ask "what decisions have been made?" when
        // the conversation already contains decisions.

        if (step?.Kind is ProbeKind.AskArchitectureDoc)
        {
            var hasPreviousDecisionContent = previousTurns.Any(t =>
                t.AssistantResponse.Length > 100 &&
                (t.AssistantResponse.Contains("recommend", StringComparison.OrdinalIgnoreCase) ||
                 t.AssistantResponse.Contains("suggest", StringComparison.OrdinalIgnoreCase) ||
                 t.AssistantResponse.Contains("decided", StringComparison.OrdinalIgnoreCase)));

            if (hasPreviousDecisionContent)
            {
                var asksWhatDecided = responseLower.Contains("what decisions have already been made") ||
                                     responseLower.Contains("what have we decided") ||
                                     responseLower.Contains("what decisions were made");
                if (asksWhatDecided)
                {
                    failures.Add(new ProbeFailure
                    {
                        Type    = ProbeFailureType.FailedArtifactExtraction,
                        Message = "Asked 'what decisions have been made?' but conversation already contains decision content.",
                        IsHard  = false
                    });
                }
            }
        }

        // ── 7. Over-clarification ──────────────────────────────────────────────
        // If this is a simple confirmation turn (ShortConfirm), the response
        // must NOT ask clarifying questions about framework/platform.

        if (step?.Kind is ProbeKind.ShortConfirm && turn.ClarificationRequired)
        {
            var frameworkClarifications = turn.ClarificationQuestions
                .Where(q =>
                    q.Contains("framework", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("platform", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("language", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (frameworkClarifications.Count > 0)
            {
                failures.Add(new ProbeFailure
                {
                    Type    = ProbeFailureType.OverClarification,
                    Message = $"Asked clarifying framework/platform questions after a simple confirmation. " +
                              $"Questions: {string.Join("; ", frameworkClarifications.Take(2))}",
                    IsHard  = false
                });
            }
        }

        // ── 8. Bad referent binding ────────────────────────────────────────────
        // After "that one" / "yes" / "ok", response should not ask
        // generic "what do you mean?" when context is clear from prior turns.

        if (step?.Kind is ProbeKind.ShortConfirm or ProbeKind.AskRecommendation)
        {
            var isShortAffirmation = userLower is "yes" or "ok" or "that one" or "do that" or "ok go ahead";
            if (isShortAffirmation && previousTurns.Count > 0)
            {
                var lastAssistant = previousTurns[^1].AssistantResponse.ToLowerInvariant();
                var hasContext = lastAssistant.Length > 100; // assistant gave a substantive prior response

                if (hasContext)
                {
                    var badBindingPhrases = new[]
                    {
                        "what do you mean",
                        "could you clarify",
                        "i'm not sure what you're referring to",
                        "please specify which",
                        "which option did you mean"
                    };

                    foreach (var phrase in badBindingPhrases)
                    {
                        if (responseLower.Contains(phrase))
                        {
                            failures.Add(new ProbeFailure
                            {
                                Type    = ProbeFailureType.BadReferentBinding,
                                Message = $"Failed to bind '{turn.UserMessage}' to prior context. " +
                                          $"Response contained: \"{phrase}\"",
                                IsHard  = false
                            });
                            break;
                        }
                    }
                }
            }
        }

        // ── 9. Unsafe domain handling ──────────────────────────────────────────
        // For the natural-language-to-PowerShell scenario, blind execution
        // without confirmation is a safety violation.

        if (step?.Kind == ProbeKind.Seed &&
            userLower.Contains("powershell") &&
            !responseLower.Contains("confirm") &&
            !responseLower.Contains("preview") &&
            !responseLower.Contains("review") &&
            responseLower.Contains("execute"))
        {
            failures.Add(new ProbeFailure
            {
                Type    = ProbeFailureType.UnsafeDomainHandling,
                Message = "PowerShell tool described direct execution without preview/confirmation step.",
                IsHard  = true
            });
        }

        return failures;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";

    private static bool ContainsWordMatch(string text, string word) =>
        text.Contains(' ' + word, StringComparison.Ordinal) ||
        text.Contains(word + ' ', StringComparison.Ordinal) ||
        text.StartsWith(word, StringComparison.Ordinal) ||
        text.EndsWith(word, StringComparison.Ordinal) ||
        text == word;
}
