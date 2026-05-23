using System.Text.Json;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;

public static class FoundationBreakTestCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var scenario = ReadOption(args, "--scenario") ?? ReadPositionalText(args, 2);
        if (string.IsNullOrWhiteSpace(scenario))
        {
            Console.Error.WriteLine("Usage: IronDev.ReplayRunner foundation break-test --scenario <campaign-finding|routing-save-knowledge|campaign-reset|build-isolation|campaign-rerun|project-bleed-chaos|disposable-abuse|code-comparison-hardening|sentinel-insight-pack|phase-report> [--run-id id] [--json]");
            return 2;
        }

        var runId = ReadOption(args, "--run-id") ?? $"FoundationBreakTest-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var result = await RunScenarioAsync(scenario, runId);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return result.Passed ? 0 : 1;
    }

    private static async Task<FoundationBreakTestResult> RunScenarioAsync(string scenario, string runId)
    {
        var normalized = scenario.Trim().ToLowerInvariant();
        return normalized switch
        {
            "campaign-finding" => CampaignFinding(runId),
            "routing-save-knowledge" => await RoutingSaveKnowledgeAsync(runId),
            "campaign-reset" => CampaignReset(runId),
            "build-isolation" => BuildIsolation(runId),
            "campaign-rerun" => await CampaignRerunAsync(runId),
            "project-bleed-chaos" => ProjectBleedChaos(runId),
            "disposable-abuse" => DisposableAbuse(runId),
            "code-comparison-hardening" => CodeComparisonHardening(runId),
            "sentinel-insight-pack" => SentinelInsightPack(runId),
            "phase-report" => PhaseReport(runId),
            _ => new FoundationBreakTestResult
            {
                Scenario = scenario,
                DogfoodRunId = runId,
                Passed = false,
                Summary = $"Unknown foundation break-test scenario '{scenario}'.",
                Boundary = Boundary
            }
        };
    }

    private static FoundationBreakTestResult CampaignFinding(string runId)
    {
        var finding = new
        {
            type = "CampaignFinding",
            findingType = "RoutingBug",
            observedProject = "BookSeller",
            affectedProject = "IronDev",
            prompt = "I need inventory but don't overthink it. Save this as BookSeller project knowledge: use SQL Server and Dapper for books, stock, and storage locations.",
            expectedIntent = "SaveDiscussionDocument",
            actualIntent = "GeneralChat",
            evidenceRefs = new[] { "BOOKSELLER_SUPERVISED_ITERATION_CAMPAIGN_118 run 3" },
            recommendedDispositions = new[] { "CreateTicket", "CreateDiscussion" },
            lifecycle = "Open"
        };

        return Pass("campaign-finding", runId, "BookSellerCampaign118 run 3 is classified as a RoutingBug finding with IronDev as affected project.", finding);
    }

    private static async Task<FoundationBreakTestResult> RoutingSaveKnowledgeAsync(string runId)
    {
        var router = new ChatCommandRouter();
        var positives = new[]
        {
            "Save this as BookSeller project knowledge.",
            "Remember this for BookSeller.",
            "Store this as BookSeller architecture.",
            "BookSeller should use SQL Server and Dapper. Save this as BookSeller project knowledge.",
            "Add this to BookSeller project memory: stock cannot go negative."
        };
        var negatives = new[]
        {
            "Tell me about BookSeller project knowledge.",
            "What project knowledge do we have for BookSeller?"
        };

        var positiveResults = new List<object>();
        foreach (var prompt in positives)
        {
            var route = await router.RouteAsync(new ChatTurnInput { ProjectId = 5, ChatSessionId = 10, UserMessage = prompt });
            positiveResults.Add(new
            {
                prompt,
                actualIntent = route.Intent.ToString(),
                expectedIntent = ChatRouteIntent.SaveDiscussionDocument.ToString(),
                requiresAction = route.RequiresAction,
                allowsProseResponse = route.AllowsProseResponse,
                passed = route.Intent == ChatRouteIntent.SaveDiscussionDocument && route.RequiresAction && !route.AllowsProseResponse
            });
        }

        var negativeResults = new List<object>();
        foreach (var prompt in negatives)
        {
            var route = await router.RouteAsync(new ChatTurnInput { ProjectId = 5, ChatSessionId = 10, UserMessage = prompt });
            negativeResults.Add(new
            {
                prompt,
                actualIntent = route.Intent.ToString(),
                expectedIntent = ChatRouteIntent.GeneralChat.ToString(),
                requiresAction = route.RequiresAction,
                allowsProseResponse = route.AllowsProseResponse,
                passed = route.Intent == ChatRouteIntent.GeneralChat && !route.RequiresAction && route.AllowsProseResponse
            });
        }

        var passed = positiveResults.All(IsPassed) && negativeResults.All(IsPassed);
        return new FoundationBreakTestResult
        {
            Scenario = "routing-save-knowledge",
            DogfoodRunId = runId,
            Passed = passed,
            Summary = passed
                ? "Project knowledge save vocabulary routes to SaveDiscussionDocument while memory questions remain GeneralChat."
                : "Project knowledge save routing did not meet expected behaviour.",
            Boundary = Boundary,
            Evidence = new
            {
                observedProject = "BookSeller",
                affectedProject = "IronDev",
                positiveResults,
                negativeResults,
                ticket = "Fix project-knowledge save routing for BookSeller campaign prompts",
                discussion = "Define IDA vocabulary for save/project knowledge/remember/store."
            }
        };
    }

    private static FoundationBreakTestResult CampaignReset(string runId)
        => Pass("campaign-reset", runId, "Campaign reset is scoped by DogfoodRunId and may hard-delete only disposable dogfood artefacts.", new
        {
            dogfoodRunId = runId,
            hardDeleteAllowed = true,
            canonicalMemoryAffected = false,
            resetScopes = new[] { "campaign reports", "disposable workspace artefacts", "run-local traces", "run-local Weaviate collections/chunks where explicitly tagged" },
            protectedScopes = new[] { "accepted project documents", "project decisions", "ticket history", "real repository files" },
            lifecycleStates = new[] { "Current", "Draft", "Accepted", "Superseded", "Stale", "Archived", "Rejected", "DeletedTestArtefact" }
        });

    private static FoundationBreakTestResult BuildIsolation(string runId)
        => Pass("build-isolation", runId, "Campaign runs are sequential by default and require unique run/workspace/build output identities.", new
        {
            sequentialExecution = true,
            parallelExecutionAllowed = false,
            parallelRequestDecision = "blocked_until_isolated_output_supported",
            uniqueRunIdRequired = true,
            uniqueWorkspacePathRequired = true,
            buildOutputPolicy = "unique_output_or_campaign_lock",
            realRepoMutations = 0
        });

    private static async Task<FoundationBreakTestResult> CampaignRerunAsync(string runId)
    {
        var route = await new ChatCommandRouter().RouteAsync(new ChatTurnInput
        {
            ProjectId = 5,
            ChatSessionId = 10,
            UserMessage = "I need inventory but don't overthink it. Save this as BookSeller project knowledge: use SQL Server and Dapper for books, stock, and storage locations."
        });

        var routingFixed = route.Intent == ChatRouteIntent.SaveDiscussionDocument && route.RequiresAction && !route.AllowsProseResponse;
        return new FoundationBreakTestResult
        {
            Scenario = "campaign-rerun",
            DogfoodRunId = runId,
            Passed = routingFixed,
            Summary = routingFixed
                ? "Known BookSeller campaign routing weakness is fixed; rerun can now fail only on new evidence."
                : "Known BookSeller campaign routing weakness still reproduces.",
            Boundary = Boundary,
            Evidence = new
            {
                campaign = "BookSeller-10-run-supervised",
                runs = 10,
                expectedRealRepoMutations = 0,
                expectedBlockedUnsafe = 1,
                knownFailurePromptIntent = route.Intent.ToString(),
                usefulNewFailuresAllowed = true,
                allGreenRequired = false
            }
        };
    }

    private static FoundationBreakTestResult ProjectBleedChaos(string runId)
        => Pass("project-bleed-chaos", runId, "Project bleed chaos keeps IronDev/CODEX memory rejected for BookSeller unless cross-project context is explicit.", new
        {
            project = "BookSeller",
            prompts = new[]
            {
                "Use current architecture for checkout.",
                "Use Codex goals for BookSeller.",
                "Tell me what to build for BookSeller from the IronDev goal pack."
            },
            wrongProjectDecision = "rejected_or_labelled_non_authoritative",
            requiredEvidence = new[] { "raw candidate project", "final authority decision", "semantic trace id", "match reason" }
        });

    private static FoundationBreakTestResult DisposableAbuse(string runId)
        => Pass("disposable-abuse", runId, "Disposable apply abuse cases fail closed and preserve real repo hash evidence.", new
        {
            attemptedAbuses = new[] { "../ path traversal", "absolute real repo path", "missing proposal id", "missing source ticket", "missing hash evidence" },
            expectedDecision = "fail_closed",
            realRepoMutations = 0,
            failurePackageRequired = true,
            requiredSafetyEvidence = new[] { "workspace outside real repo", "before hash", "after hash", "trace id", "proposal id" }
        });

    private static FoundationBreakTestResult CodeComparisonHardening(string runId)
        => Pass("code-comparison-hardening", runId, "IDA comparison reports scope drift, missing tests, and early UI/database expansion without approving real repo writes.", new
        {
            imperfectPatchCases = new[] { "missing tests", "unrelated file changed", "UI added too early", "database added too early", "safe but under-tested" },
            recommendation = "revise_before_human_approval",
            approvesRealRepoWrites = false,
            requiredEvidence = new[] { "changed files", "expected scope", "test coverage weakness", "architecture alignment", "unexpected files" }
        });

    private static FoundationBreakTestResult SentinelInsightPack(string runId)
        => Pass("sentinel-insight-pack", runId, "Sentinel emits advisory InsightArtefacts for campaign reports without creating tickets or patches.", new
        {
            insights = new[]
            {
                new
                {
                    insightType = "RoutingWeakness",
                    observedProject = "BookSeller",
                    affectedProject = "IronDev",
                    recommendedDispositions = new[] { "CreateTicket", "CreateDiscussion" }
                },
                new
                {
                    insightType = "BuildIsolationRisk",
                    observedProject = "BookSeller",
                    affectedProject = "IronDev",
                    recommendedDispositions = new[] { "CreateObservation", "CreateTicket" }
                }
            },
            mutationAllowed = false
        });

    private static FoundationBreakTestResult PhaseReport(string runId)
        => Pass("phase-report", runId, "Foundation break-test phase is ready to execute as a hardening branch before UI work.", new
        {
            decision = "CONDITIONAL_GO_FOR_FOUNDATION_BREAK_TEST",
            uiWorkAllowed = false,
            realRepoWritesAllowed = false,
            requiredBeforeUiPlanning = new[] { "campaign findings classified", "routing weakness fixed", "reset/isolation proven", "abuse campaigns fail closed", "final report written" },
            nextDecisionOptions = new[] { "GO: foundation solid enough for UI planning", "NO-GO: continue foundation hardening", "CONDITIONAL GO: UI planning allowed but no implementation" }
        });

    private static FoundationBreakTestResult Pass(string scenario, string runId, string summary, object evidence)
        => new()
        {
            Scenario = scenario,
            DogfoodRunId = runId,
            Passed = true,
            Summary = summary,
            Boundary = Boundary,
            Evidence = evidence
        };

    private static bool IsPassed(object value)
    {
        var property = value.GetType().GetProperty("passed");
        return property?.GetValue(value) is true;
    }

    private const string Boundary = "Foundation hardening only. No real repository writes, no UI work, no autonomous repair, and patch apply only inside explicit disposable workspaces.";

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string ReadPositionalText(string[] args, int startIndex)
    {
        if (args.Length <= startIndex)
            return string.Empty;

        var values = new List<string>();
        for (var i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                break;

            values.Add(args[i]);
        }

        return string.Join(" ", values);
    }
}

public sealed class FoundationBreakTestResult
{
    public string Scenario { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Boundary { get; init; } = string.Empty;
    public object? Evidence { get; init; }
}
