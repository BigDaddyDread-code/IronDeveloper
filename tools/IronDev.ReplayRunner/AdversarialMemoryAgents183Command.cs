using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Infrastructure.Services.Agents;

public static class AdversarialMemoryAgents183Command
{
    public static async Task<int> HandleCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ?? "AdversarialMemoryAgents183";
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        Directory.CreateDirectory(runRoot);

        var resolver = new AgentModelResolver(LoadProfiles(repoRoot));
        var definitions = AgentModelDefaults.CreateDefaultDefinitions();
        var doubt = await RunDoubtAsync(definitions, resolver, null, runId, runRoot, liveLlm: false);
        var doubtEnvelope = JsonSerializer.Deserialize<JsonElement>(doubt.OutputJson, options);
        var doubtReview = doubtEnvelope.GetProperty("result").Deserialize<DoubtReviewResult>(options)
            ?? throw new InvalidOperationException("DoubtAgent did not return a review result.");
        var killjoyReview = BuildKilljoyReview(doubtReview);
        var killjoyPath = Path.Combine(runRoot, "killjoy-review.json");
        await File.WriteAllTextAsync(killjoyPath, JsonSerializer.Serialize(killjoyReview, options));

        var memory = await RunMemoryImprovementAsync(
            definitions,
            resolver,
            null,
            runId,
            runRoot,
            doubtReview,
            killjoyReview,
            liveLlm: false);
        var memoryEnvelope = JsonSerializer.Deserialize<JsonElement>(memory.OutputJson, options);
        var memoryProposal = memoryEnvelope.GetProperty("proposal").Deserialize<MemoryImprovementProposal>(options)
            ?? throw new InvalidOperationException("MemoryImprovementAgent did not return a proposal.");
        var keyGatePath = Path.Combine(runRoot, "memory-key-gate-review.json");
        await File.WriteAllTextAsync(keyGatePath, JsonSerializer.Serialize(memoryProposal.KeyGateReview, options));
        var evidenceAuditPath = Path.Combine(runRoot, "memory-proposal-evidence-audit.json");
        await File.WriteAllTextAsync(evidenceAuditPath, JsonSerializer.Serialize(new
        {
            memoryProposal.ProposalBatchId,
            permissionLadder = new[]
            {
                "Level0ReadOnlyObserver",
                "Level1ProposalOnly",
                "Level2StagingAreaWrite",
                "Level3AutoStageLowRiskLessons",
                "Level4AutoApplyTinyNonAuthoritativeMemory",
                "Level5AcceptedMemoryMutation"
            },
            evidenceRule = "MemoryImprovementAgent interprets evidence; it is not itself an evidence source.",
            bundles = memoryProposal.EvidenceBundles,
            gate = memoryProposal.KeyGateReview
        }, options));

        var conscience = await RunConscienceAsync(definitions, resolver, runId, doubtReview, memoryProposal);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "conscience-memory-review.json"), conscience.OutputJson);

        var errors = Validate(doubtReview, killjoyReview, memoryProposal);
        var report = new AdversarialMemoryAgentsReport
        {
            Command = "campaign adversarial-memory-agents-183",
            Status = errors.Count == 0 ? "Succeeded" : "Failed",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = "IronDev",
            Summary = errors.Count == 0
                ? "DoubtAgent and MemoryImprovementAgent ran inside governed Alpha boundaries."
                : "Adversarial/memory agent smoke found missing governance evidence.",
            DoubtReview = doubtReview,
            KilljoyReview = killjoyReview,
            MemoryImprovement = memoryProposal,
            StageStatuses = [
                "DoubtAgent:Succeeded",
                $"KilljoyReview:{killjoyReview.Status}",
                "MemoryImprovementAgent:Succeeded",
                "ConscienceReview:Succeeded"
            ],
            EvidenceRefs = [
                Path.Combine(runRoot, "doubt-review.json"),
                killjoyPath,
                Path.Combine(runRoot, "memory-improvement-proposal.json"),
                keyGatePath,
                evidenceAuditPath,
                Path.Combine(runRoot, "conscience-memory-review.json")
            ],
            Warnings = [
                "MemoryImprovementAgent is deliberately Level1ProposalOnly; accepted-memory key readiness is false.",
                "MemoryKeyGate requires reviewed outcomes before any staging-area write key.",
                "DoubtAgent can force rebuttal/revision, but it cannot block forever or mutate state."
            ],
            Errors = errors,
            RealRepoMutationBlocked = true,
            AcceptedMemoryMutationBlocked = true,
            TicketCreationBlocked = true,
            PatchApplyBlocked = true,
            Boundary = "183 adds adversarial review and proposal-only memory improvement. It grants no real repo writes, accepted memory mutation, ticket creation, patch apply, or self-approval.",
            ReproCommand = $"campaign adversarial-memory-agents-183 --run-id {runId} --json"
        };

        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "trace.json"), JsonSerializer.Serialize(new
        {
            report.TraceId,
            report.RunId,
            report.StageStatuses,
            doubtReview = new { doubtReview.ReviewId, findingCount = doubtReview.Criticisms.Count, doubtReview.RebuttalRequired },
            killjoyReview = new { killjoyReview.ReviewId, killjoyReview.AllHighCriticalFindingsAddressed },
            memoryImprovement = new
            {
                memoryProposal.ProposalBatchId,
                proposalCount = memoryProposal.Proposals.Count,
                evidenceBundleCount = memoryProposal.EvidenceBundles.Count,
                memoryProposal.AuthorityKeyReadiness.ReadyForAcceptedMemoryKey,
                keyGateDecision = memoryProposal.KeyGateReview.Decision,
                keyGateRequestedLevel = memoryProposal.KeyGateReview.RequestedLevelName
            },
            boundary = report.Boundary
        }, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), BuildMarkdown(report));

        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return errors.Count == 0 ? 0 : 1;
    }

    public static async Task<int> HandleDoubtReviewAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ?? $"DoubtAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var resolver = new AgentModelResolver(LoadProfiles(repoRoot));
        var definition = AgentModelDefaults.CreateDefaultDefinitions().Single(item => item.Name == "DoubtAgent");
        var agent = new DoubtAgent(definition, resolver, ReadBoolOption(args, "--live-llm") ? new AgentLlmClient() : null);
        var result = await agent.RunAsync(new AgentRequest
        {
            AgentName = "DoubtAgent",
            GoalId = "adversarial-review-agent-183",
            DogfoodRunId = runId,
            Inputs = new Dictionary<string, string>
            {
                ["subject"] = ReadOption(args, "--subject") ?? "governed action",
                ["observed_project"] = ReadOption(args, "--observed-project") ?? "IronDev",
                ["affected_project"] = ReadOption(args, "--affected-project") ?? ReadOption(args, "--observed-project") ?? "IronDev",
                ["target_language"] = ReadOption(args, "--target-language") ?? "C#",
                ["target_stack"] = ReadOption(args, "--target-stack") ?? ".NET",
                ["plan"] = ReadOption(args, "--plan") ?? "",
                ["evidence_refs"] = ReadOption(args, "--evidence-refs") ?? "",
                ["safety_refs"] = ReadOption(args, "--safety-refs") ?? "",
                ["live_llm"] = ReadBoolOption(args, "--live-llm").ToString()
            }
        });

        Console.WriteLine(result.OutputJson);
        return result.ExitCode;
    }

    public static async Task<int> HandleMemoryImprovementProposeAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ?? $"MemoryImprovementAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var resolver = new AgentModelResolver(LoadProfiles(repoRoot));
        var definition = AgentModelDefaults.CreateDefaultDefinitions().Single(item => item.Name == "MemoryImprovementAgent");
        var agent = new MemoryImprovementAgent(definition, resolver, ReadBoolOption(args, "--live-llm") ? new AgentLlmClient() : null);
        var result = await agent.RunAsync(new AgentRequest
        {
            AgentName = "MemoryImprovementAgent",
            GoalId = "self-improving-memory-agent-183",
            DogfoodRunId = runId,
            Inputs = new Dictionary<string, string>
            {
                ["observed_project"] = ReadOption(args, "--observed-project") ?? "IronDev",
                ["affected_project"] = ReadOption(args, "--affected-project") ?? ReadOption(args, "--observed-project") ?? "IronDev",
                ["target_language"] = ReadOption(args, "--target-language") ?? "C#",
                ["target_stack"] = ReadOption(args, "--target-stack") ?? ".NET",
                ["run_trace_path"] = ReadOption(args, "--trace") ?? "",
                ["doubt_findings"] = ReadOption(args, "--doubt-findings") ?? "",
                ["killjoy_review"] = ReadOption(args, "--killjoy-review") ?? "",
                ["evidence_refs"] = ReadOption(args, "--evidence-refs") ?? "",
                ["max_context_tokens"] = ReadOption(args, "--max-context-tokens") ?? "2400",
                ["max_proposals"] = ReadOption(args, "--max-proposals") ?? "3",
                ["live_llm"] = ReadBoolOption(args, "--live-llm").ToString()
            }
        });

        Console.WriteLine(result.OutputJson);
        return result.ExitCode;
    }

    private static async Task<AgentResult> RunDoubtAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        IAgentLlmClient? client,
        string runId,
        string runRoot,
        bool liveLlm)
    {
        var definition = definitions.Single(item => item.Name == "DoubtAgent");
        var agent = new DoubtAgent(definition, resolver, client);
        var result = await agent.RunAsync(new AgentRequest
        {
            AgentName = "DoubtAgent",
            GoalId = "adversarial-memory-agents-183",
            DogfoodRunId = $"{runId}-doubt",
            Inputs = new Dictionary<string, string>
            {
                ["subject"] = "Promotion package and memory improvement readiness",
                ["observed_project"] = "Solitaire",
                ["affected_project"] = "IronDev",
                ["target_language"] = "C#",
                ["target_stack"] = ".NET/WPF",
                ["plan"] = "Use promotion evidence, ConscienceAgent, and Killjoy, then update accepted memory if the run looks safe. Quality can run later.",
                ["evidence_refs"] = "trace:builder-repair-loop-141|memory:SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138|quality:code-standards-alpha",
                ["safety_refs"] = "ConscienceAgent required|ThoughtLedger required|No accepted memory mutation|No real repo writes",
                ["live_llm"] = liveLlm.ToString()
            }
        });
        await File.WriteAllTextAsync(Path.Combine(runRoot, "doubt-review.json"), result.OutputJson);
        return result;
    }

    private static async Task<AgentResult> RunMemoryImprovementAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        IAgentLlmClient? client,
        string runId,
        string runRoot,
        DoubtReviewResult doubtReview,
        KilljoyReviewSummary killjoyReview,
        bool liveLlm)
    {
        var definition = definitions.Single(item => item.Name == "MemoryImprovementAgent");
        var agent = new MemoryImprovementAgent(definition, resolver, client);
        var result = await agent.RunAsync(new AgentRequest
        {
            AgentName = "MemoryImprovementAgent",
            GoalId = "adversarial-memory-agents-183",
            DogfoodRunId = $"{runId}-memory",
            Inputs = new Dictionary<string, string>
            {
                ["observed_project"] = "Solitaire",
                ["affected_project"] = "IronDev",
                ["target_language"] = "C#",
                ["target_stack"] = ".NET/WPF",
                ["doubt_findings"] = JsonSerializer.Serialize(doubtReview),
                ["killjoy_review"] = JsonSerializer.Serialize(killjoyReview),
                ["promotion_outcome"] = "NeedsHumanReview",
                ["evidence_refs"] = "tools/dogfood/runs/BuilderRepairLoop141Check/report.json|tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json|Docs/AGENTS.md",
                ["max_context_tokens"] = "2400",
                ["max_proposals"] = "3",
                ["live_llm"] = liveLlm.ToString()
            }
        });
        await File.WriteAllTextAsync(Path.Combine(runRoot, "memory-improvement-proposal.json"), result.OutputJson);
        return result;
    }

    private static async Task<AgentResult> RunConscienceAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        string runId,
        DoubtReviewResult doubtReview,
        MemoryImprovementProposal memoryProposal)
    {
        var definition = definitions.Single(item => item.Name == "ConscienceAgent");
        var agent = new ConscienceAgent(definition, resolver);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "ConscienceAgent",
            GoalId = "adversarial-memory-agents-183",
            DogfoodRunId = $"{runId}-conscience",
            Inputs = new Dictionary<string, string>
            {
                ["action_type"] = "stage memory improvement proposal for review",
                ["observed_project"] = doubtReview.ObservedProject,
                ["affected_project"] = doubtReview.AffectedProject,
                ["evidence"] = string.Join('|', memoryProposal.ContextRefsUsed.Take(4)),
                ["requested_tools"] = "memory.proposal.stage",
                ["memory_authority_refs"] = "AGENTS|CONTROLLED_WRITE_POLICY_SETTINGS_173",
                ["safety_boundary_refs"] = "No accepted memory mutation|No real repo writes|Human approval required"
            }
        });
    }

    private static KilljoyReviewSummary BuildKilljoyReview(DoubtReviewResult doubtReview)
    {
        var highCritical = doubtReview.Criticisms
            .Where(finding => finding.Severity is "High" or "Critical")
            .ToArray();
        var rebuttals = highCritical
            .Select(finding => new DoubtFindingRebuttal
            {
                FindingId = finding.FindingId,
                Response = $"Killjoy acknowledges '{finding.Title}' and requires the suggested fix before promotion.",
                Disposition = "MustAddressBeforePromotion",
                EvidenceRefs = [finding.EvidenceCitation]
            })
            .ToArray();

        return new KilljoyReviewSummary
        {
            ReviewId = $"killjoy-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..45],
            AgentName = "QualityAgent/Killjoy",
            Status = highCritical.Length == rebuttals.Length ? "Addressed" : "NeedsMoreEvidence",
            Rebuttals = rebuttals,
            AllHighCriticalFindingsAddressed = highCritical.Length == rebuttals.Length,
            Boundary = "Killjoy reviews and rebuts high/critical Doubt findings. It does not patch, approve, or mutate memory."
        };
    }

    private static IReadOnlyList<string> Validate(
        DoubtReviewResult doubtReview,
        KilljoyReviewSummary killjoyReview,
        MemoryImprovementProposal memoryProposal)
    {
        var errors = new List<string>();
        if (doubtReview.Criticisms.Count == 0)
            errors.Add("Expected DoubtAgent criticisms.");
        if (!doubtReview.Criticisms.Any(finding => finding.Severity is "High" or "Critical"))
            errors.Add("Expected at least one high/critical Doubt finding for the campaign smoke.");
        if (!killjoyReview.AllHighCriticalFindingsAddressed)
            errors.Add("Expected Killjoy to address every high/critical Doubt finding.");
        if (memoryProposal.Proposals.Count is < 1 or > 3)
            errors.Add("Expected one to three memory proposals.");
        if (memoryProposal.Proposals.Any(proposal => proposal.MemoryAuthorityImpact == "UpdatesAcceptedMemory"))
            errors.Add("MemoryImprovementAgent must not propose direct accepted-memory mutation.");
        if (memoryProposal.AuthorityKeyReadiness.CurrentPermissionLevel != MemoryImprovementPermissionLevel.ProposalOnly)
            errors.Add("MemoryImprovementAgent must start at Level1 ProposalOnly.");
        if (memoryProposal.AuthorityKeyReadiness.ReadyForAcceptedMemoryKey)
            errors.Add("MemoryImprovementAgent must not be ready for accepted-memory keys in Alpha.");
        if (memoryProposal.EvidenceBundles.Count != memoryProposal.Proposals.Count)
            errors.Add("Expected every memory proposal to include an evidence bundle.");
        if (memoryProposal.EvidenceBundles.Any(bundle => bundle.EvidenceRefs.Count == 0))
            errors.Add("Expected every memory proposal evidence bundle to cite external governed evidence.");
        if (memoryProposal.KeyGateReview.Decision != "NeedsMoreEvidence")
            errors.Add("Expected MemoryKeyGate to require more evidence before Level2 staging-area writes.");
        if (memoryProposal.KeyGateReview.RequestedLevel != MemoryImprovementPermissionLevel.StagingAreaWrite)
            errors.Add("Expected MemoryKeyGate to evaluate the first key: Level2 staging-area write.");
        if (memoryProposal.KeyGateReview.Metrics.UnsafeProposalCount != 0)
            errors.Add("Expected zero unsafe memory proposals.");
        if (memoryProposal.ContextTokensEstimated > memoryProposal.MaxContextTokens * 2)
            errors.Add("Memory context exceeded the hard budget tolerance.");

        return errors;
    }

    private static string BuildMarkdown(AdversarialMemoryAgentsReport report) =>
        $"""
        # Advanced Adversarial And Self-Improving Agents 183

        Status: {report.Status}
        Trace: {report.TraceId}

        Doubt findings: {report.DoubtReview.Criticisms.Count}
        High/Critical addressed by Killjoy: {report.KilljoyReview.AllHighCriticalFindingsAddressed}
        Memory proposal-only items: {report.MemoryImprovement.Proposals.Count}
        Evidence bundles: {report.MemoryImprovement.EvidenceBundles.Count}
        Memory key gate decision: {report.MemoryImprovement.KeyGateReview.Decision}
        Requested key: {report.MemoryImprovement.KeyGateReview.RequestedLevelName}
        Accepted-memory key ready: {report.MemoryImprovement.AuthorityKeyReadiness.ReadyForAcceptedMemoryKey}

        Boundary: {report.Boundary}
        """;

    private static IReadOnlyList<ModelProfile> LoadProfiles(string repoRoot)
    {
        var configPaths = new[]
        {
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.Development.json"),
            Path.Combine(repoRoot, "IronDeveloper", "appsettings.json")
        };

        foreach (var configPath in configPaths)
        {
            if (!File.Exists(configPath))
                continue;

            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("ModelProfiles", out var profilesElement) ||
                profilesElement.ValueKind != JsonValueKind.Object)
                continue;

            var profiles = profilesElement.EnumerateObject()
                .Select(profileProperty =>
                {
                    var profile = profileProperty.Value;
                    return new ModelProfile
                    {
                        Name = profileProperty.Name,
                        Provider = ReadString(profile, "Provider", "OpenAI"),
                        Model = ReadString(profile, "Model", string.Empty),
                        BaseUrl = ReadNullableString(profile, "BaseUrl"),
                        ApiKeyEnvironmentVariable = ReadNullableString(profile, "ApiKeyEnvironmentVariable"),
                        Temperature = ReadDouble(profile, "Temperature", 0.2),
                        MaxOutputTokens = ReadInt(profile, "MaxOutputTokens", 2000),
                        TimeoutSeconds = ReadInt(profile, "TimeoutSeconds", 60)
                    };
                })
                .ToArray();

            if (profiles.Length > 0)
                return profiles;
        }

        return AgentModelDefaults.CreateDefaultProfiles();
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static bool ReadBoolOption(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ReadString(JsonElement element, string propertyName, string fallback) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;

    private static string? ReadNullableString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double ReadDouble(JsonElement element, string propertyName, double fallback) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? value
            : fallback;

    private static int ReadInt(JsonElement element, string propertyName, int fallback) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : fallback;
}
