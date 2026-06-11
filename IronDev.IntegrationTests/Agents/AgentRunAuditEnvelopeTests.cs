using IronDev.Core.Agents;
using Audit = IronDev.Core.Agents.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentRunAuditEnvelopeTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentRunAuditEnvelope_ContractsExistInAuditNamespace()
    {
        Assert.AreEqual(nameof(Audit.AgentRunRecord), typeof(Audit.AgentRunRecord).Name);
        Assert.AreEqual(nameof(Audit.AgentRunStatus), typeof(Audit.AgentRunStatus).Name);
        Assert.AreEqual(nameof(Audit.AgentRunTriggerType), typeof(Audit.AgentRunTriggerType).Name);
        Assert.AreEqual(nameof(Audit.AgentRunStep), typeof(Audit.AgentRunStep).Name);
        Assert.AreEqual(nameof(Audit.AgentRunStepType), typeof(Audit.AgentRunStepType).Name);
        Assert.AreEqual(nameof(Audit.AgentRunInputRef), typeof(Audit.AgentRunInputRef).Name);
        Assert.AreEqual(nameof(Audit.AgentRunOutputRef), typeof(Audit.AgentRunOutputRef).Name);
        Assert.AreEqual(nameof(Audit.AgentCapabilityUseRecord), typeof(Audit.AgentCapabilityUseRecord).Name);
        Assert.AreEqual(nameof(Audit.AgentCapabilityUseOutcome), typeof(Audit.AgentCapabilityUseOutcome).Name);
        Assert.AreEqual(nameof(Audit.AgentBoundaryDecision), typeof(Audit.AgentBoundaryDecision).Name);
        Assert.AreEqual(nameof(Audit.AgentBoundaryDecisionType), typeof(Audit.AgentBoundaryDecisionType).Name);
        Assert.AreEqual(nameof(Audit.ThoughtLedgerEntry), typeof(Audit.ThoughtLedgerEntry).Name);
        Assert.AreEqual(nameof(Audit.ThoughtLedgerEntryType), typeof(Audit.ThoughtLedgerEntryType).Name);
        Assert.AreEqual(nameof(Audit.ThoughtLedgerSafetyValidator), typeof(Audit.ThoughtLedgerSafetyValidator).Name);
        Assert.AreEqual(nameof(Audit.AgentRunAuditEnvelope), typeof(Audit.AgentRunAuditEnvelope).Name);
        Assert.AreEqual(nameof(Audit.AgentRunAuditEnvelopeValidator), typeof(Audit.AgentRunAuditEnvelopeValidator).Name);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_ValidManualCriticRunPasses()
    {
        var envelope = BuildValidEnvelope();

        AssertNoIssues(new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope));
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_RequiresRunScopeRequesterAndValidStatus()
    {
        var envelope = BuildValidEnvelope() with
        {
            Run = BuildRun() with
            {
                AgentRunId = string.Empty,
                TenantId = string.Empty,
                RequestedByUserId = string.Empty,
                RequestedByAgentId = string.Empty,
                Status = (Audit.AgentRunStatus)999,
                TriggerType = (Audit.AgentRunTriggerType)999,
                CompletedAtUtc = CreatedAt.AddMinutes(-1)
            }
        };

        var issues = new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope);

        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.RunIdRequired);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.ScopeRequired);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.RequesterRequired);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.RunStatusInvalid);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.TriggerTypeInvalid);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.CompletedBeforeCreated);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_AgentDefinitionSnapshotMustMatchRun()
    {
        var envelope = BuildValidEnvelope() with
        {
            Run = BuildRun() with { AgentId = "different-agent" }
        };

        AssertHasIssue(
            new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope),
            Audit.AgentRunAuditEnvelopeValidator.AgentDefinitionMismatch);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_BlocksRawPrivateReasoningAcrossChildren()
    {
        var envelope = BuildValidEnvelope() with
        {
            Inputs =
            [
                BuildInput() with
                {
                    ContainsRawPrivateReasoning = true,
                    Summary = "RawPrompt: hidden."
                }
            ],
            Outputs =
            [
                BuildCriticOutput() with
                {
                    Summary = "Scratchpad details."
                }
            ],
            Steps =
            [
                BuildStep() with
                {
                    Summary = "ChainOfThought: hidden."
                }
            ],
            BoundaryDecisions =
            [
                BuildBoundaryDecision() with
                {
                    Reason = "SystemPrompt contents."
                }
            ],
            ThoughtLedger =
            [
                BuildThought() with
                {
                    Summary = "RawCompletion: hidden."
                }
            ]
        };

        AssertHasIssue(
            new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope),
            Audit.AgentRunAuditEnvelopeValidator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_BlocksInputAuthorityUnlessExplicitApprovalEvidence()
    {
        var envelope = BuildValidEnvelope() with
        {
            Inputs =
            [
                BuildInput() with
                {
                    RefType = "MemorySearchResult",
                    IsAuthoritativeForAction = true
                }
            ]
        };

        AssertHasIssue(
            new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope),
            Audit.AgentRunAuditEnvelopeValidator.InputAuthorityBlocked);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_BlocksOutputAuthorityRuntimeActionsAndUnsafeShapes()
    {
        var envelope = BuildValidEnvelope() with
        {
            Outputs =
            [
                BuildCriticOutput() with
                {
                    IsReviewOnly = false,
                    CreatesAuthority = true,
                    CreatesRuntimeAction = true
                },
                BuildMemoryProposalOutput() with
                {
                    IsProposalOnly = false
                }
            ]
        };

        var issues = new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope);

        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.OutputAuthorityBlocked);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.OutputRuntimeActionBlocked);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.CriticOutputMustBeReviewOnly);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.MemoryProposalMustBeProposalOnly);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_BlocksForbiddenUndeclaredAndDangerousCapabilities()
    {
        var envelope = BuildValidEnvelope() with
        {
            CapabilityUses =
            [
                BuildCapabilityUse(AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Allowed, declared: false, forbidden: true),
                BuildCapabilityUse(AgentCapability.MutateSource, Audit.AgentCapabilityUseOutcome.Allowed, declared: false, forbidden: false)
            ]
        };

        var issues = new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope);

        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.ForbiddenCapabilityNotBlocked);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.UndeclaredCapabilityAllowed);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.DangerousCapabilityAllowed);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_CapabilitySnapshotFlagsMustMatchDefinition()
    {
        var envelope = BuildValidEnvelope() with
        {
            CapabilityUses =
            [
                BuildCapabilityUse(AgentCapability.CreateCriticFinding, Audit.AgentCapabilityUseOutcome.Allowed, declared: false, forbidden: false)
            ]
        };

        AssertHasIssue(
            new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope),
            Audit.AgentRunAuditEnvelopeValidator.CapabilitySnapshotMismatch);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_BlocksBoundaryAuthorityApprovalPolicyAndPromotionClaims()
    {
        var envelope = BuildValidEnvelope() with
        {
            BoundaryDecisions =
            [
                BuildBoundaryDecision() with
                {
                    BoundaryType = Audit.AgentBoundaryDecisionType.Capability,
                    GrantsAuthority = true,
                    GrantsHumanApproval = true,
                    GrantsPolicyApproval = true,
                    GrantsMemoryPromotion = true
                }
            ]
        };

        var issues = new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope);

        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.BoundaryAuthorityBlocked);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.BoundaryApprovalBlocked);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.BoundaryPolicyApprovalBlocked);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.BoundaryMemoryPromotionBlocked);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_BlocksChildRunIdMismatchAndDuplicateSteps()
    {
        var envelope = BuildValidEnvelope() with
        {
            Steps =
            [
                BuildStep(sequence: 1),
                BuildStep(sequence: 1) with { StepId = "step-2", AgentRunId = "other-run" }
            ]
        };

        var issues = new Audit.AgentRunAuditEnvelopeValidator().Validate(envelope);

        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.ChildRunIdMismatch);
        AssertHasIssue(issues, Audit.AgentRunAuditEnvelopeValidator.DuplicateStepSequence);
    }

    [TestMethod]
    public void AgentRunAuditEnvelope_DoesNotAddRuntimeSqlWeaviatePersistenceOrAgentWiring()
    {
        var repositoryRoot = FindRepositoryRoot();
        var auditFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Audit"), "*.cs", SearchOption.AllDirectories)
            .ToArray();

        var forbiddenTokens = new[]
        {
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "ExecuteAgentAsync",
            "RunAgentAsync",
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "Weaviate",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "AddHostedService",
            "IHostedService",
            "BackgroundService"
        };

        foreach (var file in auditFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Agent-run audit file contains forbidden runtime token '{token}': {file}");
            }
        }

        var runtimeRoots = new[]
        {
            Path.Combine("IronDev.Infrastructure", "Services", "Agents"),
            "IronDev.Api",
            "IronDev.Client",
            "tools"
        };
        var runtimeFiles = runtimeRoots
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(Path.Combine("bin"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("obj"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in runtimeFiles)
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.EndsWith("IronDev.Api/Program.cs", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("IronDev.Api/Controllers/AgentRunAuditController.cs", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("IronDev.Api/Controllers/AgentRunsV1Controller.cs", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("IronDev.Api/Controllers/ManualCriticReviewsV1Controller.cs", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("IronDev.Api/Controllers/ManualMemoryImprovementsV1Controller.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains("AgentRunAuditEnvelope", StringComparison.Ordinal),
                $"Runtime file wires AgentRunAuditEnvelope: {file}");
            Assert.IsFalse(text.Contains("ThoughtLedgerSafetyValidator", StringComparison.Ordinal),
                $"Runtime file wires ThoughtLedgerSafetyValidator: {file}");
            Assert.IsFalse(text.Contains("IronDev.Core.Agents.Audit", StringComparison.Ordinal),
                $"Runtime file references audit contracts: {file}");
        }
    }

    private static Audit.AgentRunAuditEnvelope BuildValidEnvelope() =>
        new()
        {
            Run = BuildRun(),
            AgentDefinitionSnapshot = AgentDefinitionCatalog.IndependentCriticAgent,
            Inputs =
            [
                BuildInput()
            ],
            Outputs =
            [
                BuildCriticOutput()
            ],
            Steps =
            [
                BuildStep(Audit.AgentRunStepType.Created, 1, "Manual boxed critic run created."),
                BuildStep(Audit.AgentRunStepType.CapabilityEvaluated, 2, "CreateCriticFinding capability reviewed."),
                BuildStep(Audit.AgentRunStepType.OutputRecorded, 3, "Review-only critic result recorded."),
                BuildStep(Audit.AgentRunStepType.Completed, 4, "Audit envelope completed.")
            ],
            CapabilityUses =
            [
                BuildCapabilityUse(AgentCapability.CreateCriticFinding, Audit.AgentCapabilityUseOutcome.Allowed, declared: true, forbidden: false),
                BuildCapabilityUse(AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked, declared: false, forbidden: true)
            ],
            BoundaryDecisions =
            [
                BuildBoundaryDecision()
            ],
            ThoughtLedger =
            [
                BuildThought(Audit.ThoughtLedgerEntryType.EvidenceUsed, "Reviewed the supplied source-report evidence.", ["evidence-1"]),
                BuildThought(Audit.ThoughtLedgerEntryType.Assumption, "Assumes the source-report belongs to this run.", []),
                BuildThought(Audit.ThoughtLedgerEntryType.RejectedAlternative, "Rejected direct tool execution because the boxed critic has no tool authority.", []),
                BuildThought(Audit.ThoughtLedgerEntryType.BoundaryDecision, "Boundary decision records review-only output.", ["boundary-1"]),
                BuildThought(Audit.ThoughtLedgerEntryType.OutputRationale, "Output is a critic review reference only.", ["output-1"])
            ]
        };

    private static Audit.AgentRunRecord BuildRun() =>
        new()
        {
            AgentRunId = "agent-run-1",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
            AgentName = AgentDefinitionCatalog.IndependentCriticAgent.Name,
            RequestedByUserId = "user-1",
            TriggerType = Audit.AgentRunTriggerType.ManualUserRequest,
            Status = Audit.AgentRunStatus.CompletedWithWarnings,
            RequestSummary = "Review the source report.",
            Purpose = "Capture a boxed critic audit record.",
            CreatedAtUtc = CreatedAt,
            StartedAtUtc = CreatedAt.AddSeconds(1),
            CompletedAtUtc = CreatedAt.AddSeconds(2)
        };

    private static Audit.AgentRunInputRef BuildInput() =>
        new()
        {
            InputRefId = "input-1",
            AgentRunId = "agent-run-1",
            RefType = "SourceReport",
            RefId = "source-report-1",
            Source = "workspace source-report",
            Summary = "Source report was supplied for review.",
            Sha256 = new string('a', 64)
        };

    private static Audit.AgentRunOutputRef BuildCriticOutput() =>
        new()
        {
            OutputRefId = "output-1",
            AgentRunId = "agent-run-1",
            RefType = "CriticReviewResult",
            RefId = "critic-result-1",
            Summary = "Review-only critic result recorded.",
            Sha256 = new string('b', 64),
            IsReviewOnly = true,
            EvidenceRefs = ["evidence-1"]
        };

    private static Audit.AgentRunOutputRef BuildMemoryProposalOutput() =>
        new()
        {
            OutputRefId = "output-2",
            AgentRunId = "agent-run-1",
            RefType = "MemoryImprovementProposalDraft",
            RefId = "proposal-draft-1",
            Summary = "Proposal-only memory improvement draft recorded.",
            Sha256 = new string('c', 64),
            IsProposalOnly = true,
            EvidenceRefs = ["evidence-1"]
        };

    private static Audit.AgentRunStep BuildStep(
        Audit.AgentRunStepType stepType = Audit.AgentRunStepType.Created,
        int sequence = 1,
        string summary = "Run step recorded.") =>
        new()
        {
            StepId = $"step-{sequence}",
            AgentRunId = "agent-run-1",
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = CreatedAt.AddSeconds(sequence),
            Summary = summary,
            EvidenceRefs = ["evidence-1"]
        };

    private static Audit.AgentCapabilityUseRecord BuildCapabilityUse(
        AgentCapability capability,
        Audit.AgentCapabilityUseOutcome outcome,
        bool declared,
        bool forbidden) =>
        new()
        {
            CapabilityUseId = $"capability-{capability}",
            AgentRunId = "agent-run-1",
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome}.",
            PolicyDecisionId = "policy-1",
            BoundaryDecisionId = "boundary-1",
            EvidenceRef = "evidence-1",
            WasDeclaredOnAgent = declared,
            WasForbiddenOnAgent = forbidden
        };

    private static Audit.AgentBoundaryDecision BuildBoundaryDecision() =>
        new()
        {
            BoundaryDecisionId = "boundary-1",
            AgentRunId = "agent-run-1",
            BoundaryType = Audit.AgentBoundaryDecisionType.Capability,
            Decision = "blocked",
            Reason = "Tool execution is outside the boxed critic boundary.",
            SourceRefId = "policy-1",
            EvidenceRefs = ["evidence-1"]
        };

    private static Audit.ThoughtLedgerEntry BuildThought(
        Audit.ThoughtLedgerEntryType entryType = Audit.ThoughtLedgerEntryType.EvidenceUsed,
        string summary = "Evidence was reviewed and no authority is granted by this rationale.",
        IReadOnlyList<string>? evidenceRefs = null) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{entryType}",
            AgentRunId = "agent-run-1",
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs ?? ["evidence-1"],
            RecordedAtUtc = CreatedAt.AddSeconds(1)
        };

    private static void AssertHasIssue(IReadOnlyList<AgentDefinitionValidationIssue> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues)
    {
        Assert.AreEqual(
            0,
            issues.Count,
            $"Expected no validation issues but got: {string.Join(", ", issues.Select(issue => $"{issue.Code}:{issue.Message}"))}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Infrastructure")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
