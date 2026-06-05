namespace IronDev.Core.ChatProbe;

/// <summary>
/// Orchestrates a single probe conversation through the real IronDev chat API
/// via the <see cref="IChatProbeSession"/> port.
/// Creates a fresh chat session, drives scripted steps via the persona,
/// injects adaptive probes, evaluates each turn, and returns the full run result.
/// </summary>
public sealed class ChatProbeDriver
{
    private readonly HardProbeEvaluator _evaluator = new();
    private readonly AdaptiveProbeLogic _adaptive   = new();

    public async Task<ProbeRunResult> RunAsync(
        IChatProbeSession session,
        int projectId,
        ProbeScenario scenario,
        PersonaProfile persona,
        ProbeRunOptions options,
        CancellationToken ct)
    {
        var runId     = $"chat-probe-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{scenario.ScenarioId}-{persona.Id}";
        var startedAt = DateTimeOffset.UtcNow;
        var turns     = new List<ProbeTurnResult>();
        string? errorMessage = null;

        // ── Create a fresh session ────────────────────────────────────────────

        long sessionId;
        try
        {
            sessionId = await session.OpenSessionAsync(
                projectId,
                $"[ChatProbe] {scenario.Name} / {persona.Name}",
                ct);
        }
        catch (Exception ex)
        {
            return new ProbeRunResult
            {
                RunId        = runId,
                ScenarioId   = scenario.ScenarioId,
                Persona      = persona.Id,
                ProjectIdea  = scenario.ProjectIdea,
                Outcome      = ProbeRunOutcome.HardFail,
                StartedUtc   = startedAt,
                CompletedUtc = DateTimeOffset.UtcNow,
                ErrorMessage = $"Failed to create chat session: {ex.Message}"
            };
        }

        // ── Drive scripted steps ──────────────────────────────────────────────

        var totalTurns       = 0;
        var adaptiveUsed     = 0;
        var hardFailDetected = false;

        foreach (var step in scenario.Steps.OrderBy(s => s.Order))
        {
            if (totalTurns >= options.MaxTurns)
                break;

            if (options.StopOnHardFailure && hardFailDetected)
                break;

            // Apply persona transform
            var transformedMessage = persona.TextTransform(step.UserMessage);

            // Send the scripted message
            var turnResult = await SendTurnAsync(
                session, projectId, sessionId, transformedMessage,
                step, turnNumber: totalTurns + 1, isAdaptive: false, turns, ct);

            turns.Add(turnResult);
            totalTurns++;

            if (turnResult.Failures.Any(f => f.IsHard))
                hardFailDetected = true;

            if (options.StopOnHardFailure && hardFailDetected)
                break;

            // ── Adaptive probe injection ──────────────────────────────────────
            if (adaptiveUsed < options.MaxAdaptiveProbes && totalTurns < options.MaxTurns)
            {
                var adaptiveMessage = _adaptive.GetAdaptiveProbe(
                    transformedMessage,
                    turnResult.AssistantResponse,
                    turns,
                    adaptiveUsed,
                    options);

                if (adaptiveMessage is not null)
                {
                    var adaptiveTurn = await SendTurnAsync(
                        session, projectId, sessionId, adaptiveMessage,
                        step: null, turnNumber: totalTurns + 1, isAdaptive: true, turns, ct);

                    turns.Add(adaptiveTurn);
                    adaptiveUsed++;
                    totalTurns++;

                    if (adaptiveTurn.Failures.Any(f => f.IsHard))
                        hardFailDetected = true;
                }
            }
        }

        // ── Compute outcome ───────────────────────────────────────────────────

        var allFailures = turns.SelectMany(t => t.Failures).ToList();
        var outcome = allFailures.Any(f => f.IsHard)
            ? ProbeRunOutcome.HardFail
            : allFailures.Count > 0
                ? ProbeRunOutcome.SoftFail
                : ProbeRunOutcome.Pass;

        return new ProbeRunResult
        {
            RunId        = runId,
            ScenarioId   = scenario.ScenarioId,
            Persona      = persona.Id,
            ProjectIdea  = scenario.ProjectIdea,
            Outcome      = outcome,
            Turns        = turns,
            AllFailures  = allFailures,
            StartedUtc   = startedAt,
            CompletedUtc = DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<ProbeTurnResult> SendTurnAsync(
        IChatProbeSession session,
        int projectId,
        long sessionId,
        string userMessage,
        ProbeStep? step,
        int turnNumber,
        bool isAdaptive,
        IReadOnlyList<ProbeTurnResult> previousTurns,
        CancellationToken ct)
    {
        // Save user message
        try
        {
            await session.AppendMessageAsync(projectId, sessionId, "user", userMessage, ct);
        }
        catch
        {
            // Non-fatal: continue without persisting user message
        }

        // Call chat completion
        ChatProbeCompletionResult? completionResult = null;
        string assistantText = string.Empty;
        Exception? callError = null;

        try
        {
            completionResult = await session.CompleteAsync(projectId, sessionId, userMessage, ct);
            assistantText    = completionResult.Response;
        }
        catch (Exception ex)
        {
            callError    = ex;
            assistantText = $"[ERROR: {ex.Message}]";
        }

        // Save assistant message
        if (completionResult is not null)
        {
            try
            {
                await session.AppendMessageAsync(projectId, sessionId, "assistant", assistantText, ct);
            }
            catch
            {
                // Non-fatal
            }
        }

        // Extract governance metadata
        var governanceActions = completionResult?.GovernanceActions ?? [];
        var canSaveDiscussion = governanceActions.Any(a => a.Contains("Discussion", StringComparison.OrdinalIgnoreCase));
        var canCreateTicket   = governanceActions.Any(a => a.Contains("Ticket", StringComparison.OrdinalIgnoreCase));

        // Build partial turn result for evaluation
        var partialTurn = new ProbeTurnResult
        {
            TurnNumber            = turnNumber,
            UserMessage           = userMessage,
            AssistantResponse     = assistantText,
            Mode                  = completionResult?.Mode,
            GovernanceActions     = governanceActions,
            GateCanSaveDiscussion = canSaveDiscussion,
            GateCanCreateTicket   = canCreateTicket,
            DogfoodTraceId        = completionResult?.DogfoodTraceId,
            WasAdaptiveProbe      = isAdaptive
        };

        // Run hard evaluator
        var failures = callError is not null
            ? [new ProbeFailure
              {
                  Type    = ProbeFailureType.WrongMode,
                  Message = $"API call failed: {callError.Message}",
                  IsHard  = true
              }]
            : _evaluator.Evaluate(step, partialTurn, previousTurns);

        // Return completed turn
        return new ProbeTurnResult
        {
            TurnNumber            = partialTurn.TurnNumber,
            UserMessage           = partialTurn.UserMessage,
            AssistantResponse     = partialTurn.AssistantResponse,
            Mode                  = partialTurn.Mode,
            GovernanceActions     = partialTurn.GovernanceActions,
            GateCanSaveDiscussion = partialTurn.GateCanSaveDiscussion,
            GateCanCreateTicket   = partialTurn.GateCanCreateTicket,
            DogfoodTraceId        = partialTurn.DogfoodTraceId,
            WasAdaptiveProbe      = partialTurn.WasAdaptiveProbe,
            Failures              = failures
        };
    }
}
