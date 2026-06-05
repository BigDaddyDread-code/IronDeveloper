using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Chat;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class Slice1GovernanceBoundaryTests
{
    [TestMethod]
    public void Slice1_01_ClassifierIsTheChatModeAuthorityForChatRequestFlow()
    {
        var root = FindRepoRoot();

        var sourceFiles = EnumerateProductionSourceFiles(
            root,
            Path.Combine(root, "IronDev.Core"),
            Path.Combine(root, "IronDev.Infrastructure"),
            Path.Combine(root, "IronDev.Api"));

        var newModeDecisionFiles = sourceFiles
            .Where(file => Regex.IsMatch(ReadFile(file), @"new\s+ChatModeDecision\s*\("))
            .Select(file => Path.GetRelativePath(root, file))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var allowedChatModeDecisionFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("IronDev.Core", "Chat", "ChatGovernanceModels.cs"),
            Path.Combine("IronDev.Api", "Controllers", "ChatController.cs"),
            Path.Combine("IronDev.Infrastructure", "Services", "ChatTurnPersistenceService.cs"),
            Path.Combine("IronDev.Infrastructure", "Services", "LlmChatModeClassifier.cs")
        };

        Assert.IsTrue(newModeDecisionFiles.Length > 0, "Expected ChatModeDecision construction sites to be discoverable.");
        var unexpected = newModeDecisionFiles.Where(path => !allowedChatModeDecisionFiles.Contains(path)).ToArray();
        Assert.AreEqual(0, unexpected.Length, "Unexpected ChatModeDecision constructor usage outside approved files: " + string.Join(", ", unexpected));

        var service = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"));
        Assert.AreEqual(1, Regex.Matches(service, @"_modeClassifier\.ClassifyAsync\(").Count, "ProjectChatResponseService must call classifier exactly once.");
        Assert.IsFalse(service.Contains("new ChatModeDecision("), "ProjectChatResponseService must not construct ChatModeDecision directly.");
        Assert.IsFalse(service.Contains("ChatModeDecision("), "ProjectChatResponseService must rely on _modeClassifier output.");
    }

    [TestMethod]
    public void Slice1_02_ChatGovernanceGateMustBeTheOnlyActionDerivationPoint()
    {
        var root = FindRepoRoot();

        var model = ReadFile(Path.Combine(root, "IronDev.Core", "Chat", "ChatGovernanceModels.cs"));
        StringAssert.Contains(model, "public static ChatGovernanceGate FromDecision(ChatModeDecision decision)");

        var producerLines = Regex.Matches(
            model,
            @"Can(?:SaveDiscussion|CreateTicket|ViewSources|CopyMarkdown)\s*:\s*formalization");
        Assert.AreEqual(4, producerLines.Count, "Gate action derivation must only be centralized in ChatGovernanceGate.FromDecision.");

        var responseService = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"));
        StringAssert.Contains(responseService, "var gate = ChatGovernanceGate.FromDecision(modeDecision)");

        var clientFiles = new[]
        {
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "chatGovernanceGate.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatMessage.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatSuggestedActions.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "useProjectChat.ts")
        };

        foreach (var clientFile in clientFiles)
        {
            Assert.IsTrue(File.Exists(clientFile), $"Missing UI source file: {clientFile}");
            var text = ReadFile(clientFile);
            Assert.IsFalse(text.Contains("mode === 'Formalization'"), $"UI must not infer actions from mode literals in {clientFile}");
            Assert.IsFalse(text.Contains("mode === \"Formalization\""), $"UI must not infer actions from mode literals in {clientFile}");
        }
    }

    [TestMethod]
    public void Slice1_03_ChatContextStateIsTheOnlyMemoryEvidentiaryPathToClassifier()
    {
        var root = FindRepoRoot();
        var service = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"));

        StringAssert.Contains(service, "var chatContextState = _contextStateCompiler.Compile(context, normalizedPrompt, recentSummary)");
        StringAssert.Contains(service, "ContextState: chatContextState");
        StringAssert.Contains(service, "var modeDecision = await _modeClassifier.ClassifyAsync(");
        Assert.IsFalse(service.Contains("new ChatContextState("), "ProjectChatResponseService should consume context state from compiler.");
        Assert.IsFalse(service.Contains("_routeJudge"), "Route judge output cannot construct mode context directly.");

        var compiler = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatContextStateCompiler.cs"));
        StringAssert.Contains(compiler, "Origin: ChatContextStateOrigin.ProjectChatResponseCompiler");
        StringAssert.Contains(compiler, "EpisodicMemoryEnabled: false,");
        StringAssert.Contains(compiler, "SemanticEvidence:");
    }

    [TestMethod]
    public void Slice1_04_NoMemoryModelContainsGovernanceSuggestionFields()
    {
        var bannedSuffixes = new[] { "SuggestedMode", "SuggestedAction" };
        var bannedPrefixes = new[] { "ShouldShow", "Auto" };

        var explicitMemoryBoundaryTypes = new[] { typeof(MemoryEvidence), typeof(AvailableSkillHint) };
        var discoveredMemoryModelTypes = typeof(ChatContextState).Assembly
            .GetTypes()
            .Where(type => !type.IsEnum && !type.IsInterface)
            .Where(type => type.FullName != null && type.FullName.Contains("Memory", StringComparison.OrdinalIgnoreCase));

        var memoryModelTypes = explicitMemoryBoundaryTypes
            .Concat(discoveredMemoryModelTypes)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var type in memoryModelTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsBannedGovernanceFieldName(property.Name, bannedSuffixes, bannedPrefixes))
                    violations.Add($"{type.FullName}.{property.Name}");
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsBannedGovernanceFieldName(field.Name, bannedSuffixes, bannedPrefixes))
                    violations.Add($"{type.FullName}.{field.Name}");
            }
        }

        Assert.AreEqual(0, violations.Count, "Governance-suggestion fields found in memory models: " + string.Join(", ", violations));
    }

    [TestMethod]
    public async Task Slice1_05_EpisodicMemoryIsAlwaysDisabledForClassification()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.79,
              "reason": "Exploration remains default."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var request = BuildRequest(
            "can we add telemetry to this app?",
            contextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "trusted compiler context",
                CurrentUserMessage: "can we add telemetry to this app?",
                RecentTurns: [new RecentChatTurn("user", "can we add telemetry to this app?")],
                ActiveArtifact: new ActiveArtifactContext(ArtifactType: "Decision", ArtifactId: "77", Title: "Old telemetry decision", Summary: "Decide later."),
                SemanticEvidence: [new MemoryEvidence(SourceId: "old-77", SourceType: "Decision", Title: "Old telemetry decision", Excerpt: "Persist telemetry early.", IsCurrent: true, AuthorityLevel: "Accepted", UsedFor: "ContextOnly")],
                AvailableSkillHints: [new AvailableSkillHint("CreateTicket", "CreateTicket", "Only available for committed governance paths.")],
                EpisodicMemoryEnabled: true,
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler));

        var decision = await classifier.ClassifyAsync(request);
        var prompt = llm.ReceivedPrompts.Single();

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        StringAssert.Contains(prompt, "Episodic memory enabled: False");
        StringAssert.Contains(prompt, "Context evidence trust: trusted-compiler");
        StringAssert.Contains(prompt, "FromChatContextState true");
    }

    [TestMethod]
    public void Slice1_06_GovernanceTraceRecordsMemoryEvidenceUsedInClassification()
    {
        var builder = new ProjectChatResponseMetadataBuilder();

        var context = new ProjectChatContextPipelineResult(
            new Project
            {
                Id = 99,
                Name = "TraceProject",
                Description = "Project for Slice 1 trace validation"
            },
            new List<ProjectTicket>(),
            new List<ProjectDecision>
            {
                new() { Id = 21, Title = "Auth decision", Detail = "Use OAuth for authentication.", Status = "Accepted" }
            },
            new List<ProjectRule>(),
            new List<ProjectContextDocument>(),
            new ContextAgentRouteDecision
            {
                RequestKind = ContextRequestKind.GeneralChat,
                Confidence = 0.81,
                Reason = "Route test setup",
                ContextModeHint = "Exploration",
                AllowTicketCreation = false,
                NeedsClarification = false,
                EvidenceUsed = ["Decision lookup"],
                AllowConflictAssessment = false,
                AllowDeepLookup = true
            },
            new List<string> { "Route signal: general-chat" },
            new ContextAgentResult
            {
                ContextSummary = "Decision context assembled for trace test",
                WasSuccessful = true
            });

        var trace = builder.Build(
            context,
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.82, "Trace test"),
            "trace-group",
            new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "Trace summary",
                CurrentUserMessage: "Talk through auth options.",
                RecentTurns: [],
                ActiveArtifact: new ActiveArtifactContext(
                    ArtifactType: "Decision",
                    ArtifactId: "21",
                    Title: "Auth decision",
                    Summary: "Use OAuth for authentication."),
                SemanticEvidence:
                [
                    new MemoryEvidence(
                        SourceId: "decision-21",
                        SourceType: "Decision",
                        Title: "Auth decision",
                        Excerpt: "Use OAuth for authentication.",
                        IsCurrent: true,
                        AuthorityLevel: "Accepted",
                        UsedFor: "ContextOnly"),
                    new MemoryEvidence(
                        SourceId: "ticket-42",
                        SourceType: "Ticket",
                        Title: "Auth follow-up",
                        Excerpt: "Track implementation details.",
                        IsCurrent: false,
                        AuthorityLevel: "Draft",
                        UsedFor: "ContextOnly")
                ],
                AvailableSkillHints: [],
                EpisodicMemoryEnabled: false,
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler,
                ClassifiedClarification: ChatClarificationState.None));

        var reasoning = string.Join(" | ", trace.ReasoningTrace);

        StringAssert.Contains(reasoning, "Memory evidence consumed by classifier");
        StringAssert.Contains(reasoning, "SourceId=decision-21");
        StringAssert.Contains(reasoning, "SourceType=Decision");
        StringAssert.Contains(reasoning, "AuthorityLevel=Accepted");
        StringAssert.Contains(reasoning, "FromChatContextState=True");
        StringAssert.Contains(reasoning, "SourceId=ticket-42");
        StringAssert.Contains(reasoning, "SourceType=Ticket");
        StringAssert.Contains(reasoning, "AuthorityLevel=Draft");
        StringAssert.Contains(reasoning, "Episodic memory disabled for this classification.");
    }

    [TestMethod]
    public async Task Slice1_07_OldSemanticMemoryDoesNotForceFormalization()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.83,
              "reason": "User is exploratory and has not asked for durable work."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest(
            "the idea sounds interesting, what should we do next?",
            contextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "old decision notes are present",
                CurrentUserMessage: "the idea sounds interesting, what should we do next?",
                SemanticEvidence:
                [
                    new MemoryEvidence(
                        SourceId: "arch-001",
                        SourceType: "Decision",
                        Title: "Accepted Decision Archive",
                        Excerpt: "This decision was accepted last quarter and should be the base architecture.",
                        IsCurrent: false,
                        AuthorityLevel: "Accepted",
                        UsedFor: "PolicyEnforcement")
                ],
                AvailableSkillHints: [new AvailableSkillHint("CreateTicket", "CreateTicket", "Available on commit path.")],
                EpisodicMemoryEnabled: true,
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler),
            routeKind: ContextRequestKind.CreateTicket,
            allowTicketCreation: true,
            contextModeHint: "Formalization"));

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        var prompt = llm.ReceivedPrompts.Single();
        StringAssert.Contains(prompt, "SourceId=arch-001");
        StringAssert.Contains(prompt, "IsCurrent=False");
        StringAssert.Contains(prompt, "Authority=Accepted");
        StringAssert.Contains(prompt, "RequestKind=CreateTicket");
        StringAssert.Contains(prompt, "ContextModeHint=Formalization");
        Assert.IsFalse(ChatGovernanceGate.FromDecision(decision).ShowGovernanceActions);
    }

    [TestMethod]
    public async Task Slice1_08_SkillExistenceDoesNotRevealGovernanceActions()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.86,
              "reason": "No durable action requested."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest(
            "this sounds cool, can you help me think through it?",
            contextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "No formalization intent.",
                CurrentUserMessage: "this sounds cool, can you help me think through it?",
                AvailableSkillHints:
                [
                    new AvailableSkillHint(
                        SkillId: "CreateTicketSkill",
                        DisplayName: "CreateTicket",
                        CapabilitySummary: "Offers ticket creation workflow support.")
                ],
                EpisodicMemoryEnabled: true,
                Origin: ChatContextStateOrigin.ExternalInput),
            routeKind: ContextRequestKind.CreateTicket,
            allowTicketCreation: true,
            contextModeHint: "Formalization"));

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);

        var gate = ChatGovernanceGate.FromDecision(decision);
        Assert.IsFalse(gate.ShowGovernanceActions, "Skill hints must remain advisory only.");
        Assert.IsFalse(gate.CanSaveDiscussion);
        Assert.IsFalse(gate.CanCreateTicket);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "Context-sourced skill hints allowed: False");
    }

    [TestMethod]
    public void Slice2_ClarificationStateCannotRevealGovernanceActions()
    {
        var clarification = new ChatClarificationState(
            true,
            ChatClarificationKind.GovernanceIntent,
            ["Do you want to keep exploring, or turn this into a ticket?"],
            "The user may need help choosing whether to commit.");

        var contextState = new ChatContextState(
            RequiresClarification: false,
            ClarificationQuestions: Array.Empty<string>(),
            ContextSummary: "The user is still exploring.",
            CurrentUserMessage: "not sure yet, keep talking it through",
            Origin: ChatContextStateOrigin.ProjectChatResponseCompiler,
            ClassifiedClarification: clarification);

        var decision = new ChatModeDecision(
            ChatGovernanceMode.Exploration,
            0.91,
            "The user is still exploring and did not request a durable governance action.");

        var gate = ChatGovernanceGate.FromDecision(decision);
        var classified = contextState.ClassifiedClarification;

        Assert.IsNotNull(classified);
        Assert.IsTrue(classified.Required);
        Assert.AreEqual(ChatClarificationKind.GovernanceIntent, classified.Kind);
        Assert.IsFalse(gate.ShowGovernanceActions, "Clarification must not reveal governance actions when mode is Exploration.");
        Assert.IsFalse(gate.CanSaveDiscussion);
        Assert.IsFalse(gate.CanCreateTicket);
        Assert.IsFalse(gate.CanViewSources);
        Assert.IsFalse(gate.CanCopyMarkdown);
        Assert.AreEqual(0, gate.GovernanceActions.Count);
    }

    [TestMethod]
    public void Slice2_ModeClassifierDoesNotReferenceClassifiedClarification()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "LlmChatModeClassifier.cs"));

        Assert.IsFalse(
            source.Contains("ClassifiedClarification", StringComparison.Ordinal),
            "Mode classifier must not read classified clarification output.");
        Assert.IsFalse(
            source.Contains("ChatClarificationState", StringComparison.Ordinal),
            "Mode classifier must not accept or inspect ChatClarificationState.");
    }

    [TestMethod]
    public async Task Slice1_09_OldAcceptedDecisionIsEvidenceNotAnAuthority()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.82,
              "reason": "No commitment text in the user request."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var contextEvidence = new MemoryEvidence(
            SourceId: "old-accepted-01",
            SourceType: "Decision",
            Title: "Archive decision",
            Excerpt: "Team already accepted this in a prior cycle.",
            IsCurrent: false,
            AuthorityLevel: "Accepted",
            UsedFor: "ContextOnly");

        var decision = await classifier.ClassifyAsync(BuildRequest(
            "I am open to options for the dashboard charts.",
            contextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "historical context only",
                CurrentUserMessage: "I am open to options for the dashboard charts.",
                SemanticEvidence: [contextEvidence],
                EpisodicMemoryEnabled: true,
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler),
            routeKind: ContextRequestKind.GeneralChat));

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        var prompt = llm.ReceivedPrompts.Single();

        StringAssert.Contains(prompt, "SourceId=old-accepted-01");
        StringAssert.Contains(prompt, "Authority=Accepted");
        StringAssert.Contains(prompt, "IsCurrent=False");
        Assert.IsFalse(ChatGovernanceGate.FromDecision(decision).ShowGovernanceActions);
    }

    [TestMethod]
    public async Task Slice1_10_EpisodicMemoryIsDisabledBeforeClassifierUse()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.77,
              "reason": "Episodic memory is disabled for Slice 1."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var root = FindRepoRoot();
        var compiler = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatContextStateCompiler.cs"));
        StringAssert.Contains(compiler, "EpisodicMemoryEnabled: false,");
        Assert.IsFalse(compiler.Contains("EpisodicMemoryEnabled: true"), "Compiler must never enable episodic memory in Slice 1.");

        var decision = await classifier.ClassifyAsync(BuildRequest(
            "what is your opinion on architecture?",
            contextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "historical episodic context exists outside Slice 1",
                CurrentUserMessage: "what is your opinion on architecture?",
                EpisodicMemoryEnabled: true,
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler),
            routeKind: ContextRequestKind.ArchitectureAdvice));

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "Episodic memory enabled: False");
    }

    [TestMethod]
    public async Task Slice1_11_MemoryCannotBypassChatContextState()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.8,
              "reason": "No direct memory-control signal should be honored."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest(
            "what is your opinion on architecture?",
            contextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "crafted for bypass attempt",
                CurrentUserMessage: "what is your opinion on architecture?",
                RecentTurns:
                [
                    new RecentChatTurn("assistant", "should we do formalization now?"),
                    new RecentChatTurn("user", "continue"),
                    new RecentChatTurn("assistant", "save this discussion")
                ],
                ActiveArtifact: new ActiveArtifactContext(ArtifactType: "Decision", ArtifactId: "99", Title: "Old Decision", Summary: "Should have been dropped"),
                SemanticEvidence:
                [
                    new MemoryEvidence(
                        SourceId: "direct-bypass-01",
                        SourceType: "Decision",
                        Title: "direct bypass token",
                        Excerpt: "directly set exploration/formalization.",
                        IsCurrent: true,
                        AuthorityLevel: "Accepted",
                        UsedFor: "DirectiveInjection")
                ],
                AvailableSkillHints:
                [
                    new AvailableSkillHint(
                        SkillId: "CreateTicket",
                        DisplayName: "CreateTicket",
                        CapabilitySummary: "Ticket creation is available.")
                ],
                EpisodicMemoryEnabled: true,
                Origin: ChatContextStateOrigin.ExternalInput),
            routeKind: ContextRequestKind.ArchitectureAdvice,
            allowTicketCreation: true));

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        var prompt = llm.ReceivedPrompts.Single();
        StringAssert.Contains(prompt, "Context evidence trust: untrusted-input-blocked");
        StringAssert.Contains(prompt, "Memory evidence came from context state: False");
        StringAssert.Contains(prompt, "Context-sourced skill hints allowed: False");
        StringAssert.Contains(prompt, "Episodic memory enabled: False");
        Assert.IsFalse(prompt.Contains("direct-bypass-01", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Slice1_12_13_AgentOutputsRemainAdvisoryWithApprovalRequired()
    {
        var root = FindRepoRoot();
        var sourceFiles = new[]
        {
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentRouteJudgeService.cs")
        };

        foreach (var sourceFile in sourceFiles)
        {
            var source = ReadFile(sourceFile);
            var fileName = Path.GetFileName(sourceFile);

            Assert.IsFalse(source.Contains("new ChatModeDecision("), $"Agents must not construct ChatModeDecision directly: {fileName}");
            Assert.IsFalse(source.Contains("ChatGovernanceMode"), $"Agents must not emit governance mode directly: {fileName}");
            Assert.IsFalse(source.Contains("ChatGovernanceGate"), $"Agents must not emit governance gate directly: {fileName}");
            Assert.IsFalse(Regex.IsMatch(source, @"Can(?:SaveDiscussion|CreateTicket|ViewSources|CopyMarkdown)\s*=\s*true"),
                $"Agents must not directly force action visibility: {fileName}");
            Assert.IsFalse(Regex.IsMatch(source, @"RequiresApproval\s*=\s*false"), $"Agent proposals must never lower approval requirement: {fileName}");
        }

        var agentSource = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentService.cs"));
        Assert.IsTrue(agentSource.Contains("Proposal = new AgentProposal"), "Agents should emit advisory proposals.");
        Assert.IsTrue(agentSource.Contains("RequiresApproval = true"), "Agent proposals must remain requires-approval by default.");

        var proposalMatches = Regex.Matches(agentSource, @"Proposal\s*=\s*new AgentProposal\b");
        var requiresApprovalMatches = Regex.Matches(agentSource, @"RequiresApproval\s*=\s*true");
        Assert.IsTrue(proposalMatches.Count <= requiresApprovalMatches.Count,
            "Each agent proposal block should include an explicit RequiresApproval assignment.");
    }

    private static string FindRepoRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("IRONDEV_REPO_ROOT"),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var current = new DirectoryInfo(candidate!);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root from current context.");
    }

    private static string ReadFile(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Expected file missing: {path}");
        return File.ReadAllText(path);
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles(string root, params string[] directories)
    {
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return path;
            }
        }
    }

    private static bool IsBannedGovernanceFieldName(string name, IReadOnlyList<string> bannedSuffixes, IReadOnlyList<string> bannedPrefixes)
    {
        foreach (var prefix in bannedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        foreach (var suffix in bannedSuffixes)
        {
            if (name.Contains(suffix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static ChatModeClassificationRequest BuildRequest(
        string userMessage,
        ChatContextState? contextState = null,
        ContextRequestKind routeKind = ContextRequestKind.GeneralChat,
        string contextModeHint = "Exploration",
        bool allowTicketCreation = false,
        ChatGovernanceMode? explicitMode = null,
        bool contextRequiresClarification = false,
        string recentConversationSummary = "")
    {
        var routeDecision = new ContextAgentRouteDecision
        {
            OriginalUserRequest = userMessage,
            EffectiveWorkText = userMessage,
            RequestKind = routeKind,
            Confidence = 0.65,
            Reason = "Slice-1 governance boundary test route.",
            ContextModeHint = contextModeHint,
            AllowTicketCreation = allowTicketCreation
        };

        return new ChatModeClassificationRequest(
            UserMessage: userMessage,
            RecentConversationSummary: recentConversationSummary,
            RouteHint: routeDecision,
            ProjectSummary: "Slice-1 governance test project",
            ContextRequiresClarification: contextRequiresClarification,
            ExplicitMode: explicitMode,
            ContextState: contextState);
    }
}
