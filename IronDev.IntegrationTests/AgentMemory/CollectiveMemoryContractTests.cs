using System.Reflection;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Collective;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CollectiveMemoryContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private readonly CollectiveMemoryContractValidator _validator = new();

    [TestMethod]
    public void CollectiveMemoryContractTypesExist()
    {
        Assert.IsNotNull(typeof(CollectiveMemoryItem));
        Assert.IsNotNull(typeof(CollectiveMemoryScope));
        Assert.IsNotNull(typeof(CollectiveMemoryType));
        Assert.IsNotNull(typeof(CollectiveMemoryAuthorityLevel));
        Assert.IsNotNull(typeof(CollectiveMemoryStatus));
        Assert.IsNotNull(typeof(CollectiveMemorySourceRef));
        Assert.IsNotNull(typeof(CollectiveMemoryEvidenceRef));
        Assert.IsNotNull(typeof(CollectiveMemoryContradictionRef));
        Assert.IsNotNull(typeof(CollectiveMemoryQuery));
        Assert.IsNotNull(typeof(ICollectiveMemoryContractValidator));
    }

    [TestMethod]
    public void CollectiveMemoryScopeHasNoAgentOrRunOwnership()
    {
        var properties = typeof(CollectiveMemoryScope)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.Contains(properties, nameof(CollectiveMemoryScope.TenantId));
        CollectionAssert.Contains(properties, nameof(CollectiveMemoryScope.ProjectId));
        CollectionAssert.DoesNotContain(properties, "AgentId");
        CollectionAssert.DoesNotContain(properties, "RunId");
        CollectionAssert.DoesNotContain(properties, "CampaignId");
    }

    [TestMethod]
    public void CollectiveMemoryContractsDoNotReferenceWeaviate()
    {
        var collectiveFiles = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "IronDev.Core", "AgentMemory", "Collective"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in collectiveFiles)
        {
            var text = File.ReadAllText(file);

            Assert.IsFalse(text.Contains("Weaviate", StringComparison.OrdinalIgnoreCase),
                $"Collective memory contract file references Weaviate: {file}");
        }
    }

    [TestMethod]
    public void CollectiveMemoryContractsExposeNoWritePromotionOrRetrievalMethods()
    {
        var methodNames = typeof(ICollectiveMemoryContractValidator)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { nameof(ICollectiveMemoryContractValidator.Validate) }, methodNames);

        var forbiddenMethodNames = new[] { "Create", "Save", "Write", "Promote", "Retrieve", "Search", "Query", "Store" };

        foreach (var forbidden in forbiddenMethodNames)
        {
            CollectionAssert.DoesNotContain(methodNames, forbidden);
        }
    }

    [TestMethod]
    public void CollectiveMemoryForbiddenInfrastructureTypesDoNotExist()
    {
        var forbiddenTypeNames = new[]
        {
            "ICollectiveMemoryRetrievalService",
            "CollectiveMemoryRetrievalService",
            "WeaviateCollectiveMemory",
            "CollectiveMemoryAttractor"
        };

        var assemblies = new[]
        {
            typeof(CollectiveMemoryItem).Assembly,
            Assembly.Load("IronDev.Infrastructure")
        };

        var typeNames = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.Name)
            .ToArray();

        foreach (var forbidden in forbiddenTypeNames)
        {
            CollectionAssert.DoesNotContain(typeNames, forbidden);
        }
    }

    [TestMethod]
    public void CollectiveMemoryStaticScanFindsNoActiveInfrastructure()
    {
        var forbiddenTokens = new[]
        {
            "ICollectiveMemoryRetrievalService",
            "CollectiveMemoryRetrievalService",
            "WeaviateCollectiveMemory",
            "CollectiveMemoryAttractor",
            "AttractorScore",
            "StabilityScore",
            "RetrievalBoost"
        };

        var productionFiles = EnumerateProductionFiles();

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);

            foreach (var forbidden in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal),
                    $"Forbidden collective memory infrastructure token '{forbidden}' was found in {file}.");
            }
        }
    }

    [TestMethod]
    public void CollectiveMemoryDatabaseScanFindsNoRetrievalOrAttractorSql()
    {
        var databaseDirectory = Path.Combine(RepositoryRoot, "Database");

        if (!Directory.Exists(databaseDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(databaseDirectory, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            Assert.IsFalse(text.Contains("ICollectiveMemoryRetrievalService", StringComparison.Ordinal),
                $"Collective memory retrieval token found in {file}.");
            Assert.IsFalse(text.Contains("CollectiveMemoryRetrievalService", StringComparison.Ordinal),
                $"Collective memory retrieval token found in {file}.");
            Assert.IsFalse(text.Contains("WeaviateCollectiveMemory", StringComparison.Ordinal),
                $"Collective memory Weaviate token found in {file}.");
            Assert.IsFalse(text.Contains("CollectiveMemoryAttractor", StringComparison.Ordinal),
                $"Collective memory attractor token found in {file}.");
            Assert.IsFalse(text.Contains("AttractorScore", StringComparison.Ordinal),
                $"Collective memory attractor score token found in {file}.");
            Assert.IsFalse(text.Contains("StabilityScore", StringComparison.Ordinal),
                $"Collective memory stability score token found in {file}.");
            Assert.IsFalse(text.Contains("RetrievalBoost", StringComparison.Ordinal),
                $"Collective memory retrieval boost token found in {file}.");
        }
    }

    [TestMethod]
    public void CollectiveMemoryItemIsNotUsedByRuntimeServices()
    {
        var runtimeFiles = EnumerateProductionFiles()
            .Where(file => !IsUnderDirectory(file, Path.Combine("IronDev.Core", "AgentMemory", "Collective")))
            .Where(file => !file.EndsWith(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlCollectiveMemoryStore.cs"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlCollectiveMemoryPromotionService.cs"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in runtimeFiles)
        {
            var text = File.ReadAllText(file);

            Assert.IsFalse(text.Contains("CollectiveMemoryItem", StringComparison.Ordinal),
                $"Runtime production file uses CollectiveMemoryItem: {file}");
        }
    }

    [TestMethod]
    public void ValidCandidateCollectiveMemoryPasses()
    {
        var result = _validator.Validate(BuildItem());

        Assert.AreEqual(0, result.Count, string.Join(", ", result.Select(issue => issue.Code)));
    }

    [TestMethod]
    public void MissingScopeFails()
    {
        var result = _validator.Validate(BuildItem() with { Scope = null! });

        AssertHasIssue(result, CollectiveMemoryContractValidator.ScopeRequired);
    }

    [TestMethod]
    public void MissingTenantFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            Scope = BuildScope() with { TenantId = string.Empty }
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.TenantRequired);
    }

    [TestMethod]
    public void MissingProjectFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            Scope = BuildScope() with { ProjectId = string.Empty }
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.ProjectRequired);
    }

    [TestMethod]
    public void MissingSourceFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            Sources = []
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.SourceRequired);
    }

    [TestMethod]
    public void MissingEvidenceFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            EvidenceRefs = []
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.EvidenceRequired);
    }

    [TestMethod]
    public void AcceptedWithoutReviewDateFails()
    {
        var result = _validator.Validate(BuildAcceptedItem() with { LastReviewedAt = null });

        AssertHasIssue(result, CollectiveMemoryContractValidator.AcceptedReviewDateRequired);
    }

    [TestMethod]
    public void AcceptedWithoutDecisionIdFails()
    {
        var result = _validator.Validate(BuildAcceptedItem() with { DecisionId = null });

        AssertHasIssue(result, CollectiveMemoryContractValidator.AcceptedDecisionRequired);
    }

    [TestMethod]
    public void AcceptedWithReviewStateNoneFails()
    {
        var result = _validator.Validate(BuildAcceptedItem() with
        {
            ReviewState = CollectiveMemoryReviewState.None
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.AcceptedReviewStateRequired);
    }

    [TestMethod]
    public void ActiveRejectedMemoryFails()
    {
        var result = _validator.Validate(BuildRejectedItem() with
        {
            Status = CollectiveMemoryStatus.Active
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.RejectedActiveConflict);
    }

    [DataTestMethod]
    [DataRow("-0.01")]
    [DataRow("1.01")]
    public void InvalidConfidenceFails(string confidenceText)
    {
        var confidence = decimal.Parse(confidenceText, System.Globalization.CultureInfo.InvariantCulture);
        var result = _validator.Validate(BuildItem() with { Confidence = confidence });

        AssertHasIssue(result, CollectiveMemoryContractValidator.ConfidenceOutOfRange);
    }

    [TestMethod]
    public void InvalidEvidenceWeightFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            EvidenceRefs = [BuildEvidence() with { Weight = 1.1m }]
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.EvidenceWeightOutOfRange);
    }

    [TestMethod]
    public void InvalidContradictionWeightFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            Contradictions = [BuildContradiction() with { Weight = -0.1m }]
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.ContradictionWeightOutOfRange);
    }

    [TestMethod]
    public void InvalidJsonFails()
    {
        var result = _validator.Validate(BuildItem() with { CollectiveMemoryJson = "{ invalid json" });

        AssertHasIssue(result, CollectiveMemoryContractValidator.InvalidCollectiveMemoryJson);
    }

    [TestMethod]
    public void RawPromptMarkerFails()
    {
        var result = _validator.Validate(BuildItem() with { Summary = "RawPrompt: hidden prompt text" });

        AssertHasIssue(result, CollectiveMemoryContractValidator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void ChainOfThoughtMarkerFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            CollectiveMemoryJson = "{\"ChainOfThought\":\"do not store this\"}"
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void EvidenceRefMissingEvidenceIdFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            EvidenceRefs = [BuildEvidence() with { EvidenceId = string.Empty }]
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.EvidenceIdRequired);
    }

    [TestMethod]
    public void EvidenceRefMissingSourceIdFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            EvidenceRefs = [BuildEvidence() with { SourceId = string.Empty }]
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.EvidenceSourceIdRequired);
    }

    [TestMethod]
    public void EvidenceRefInvalidEvidenceTypeFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            EvidenceRefs = [BuildEvidence() with { EvidenceType = (EvidenceType)999 }]
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.InvalidEnumValue);
    }

    [TestMethod]
    public void ContradictionRefMissingSourceFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            Contradictions = [BuildContradiction() with { Source = null! }]
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.ContradictionSourceRequired);
    }

    [TestMethod]
    public void ContradictionRefMissingSummaryFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            Contradictions = [BuildContradiction() with { Summary = string.Empty }]
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.ContradictionSummaryRequired);
    }

    [TestMethod]
    public void RejectedMemoryMayOmitEvidenceOnlyWithReviewExplanation()
    {
        var result = _validator.Validate(BuildRejectedItem() with
        {
            EvidenceRefs = []
        });

        Assert.AreEqual(0, result.Count, string.Join(", ", result.Select(issue => issue.Code)));
    }

    [TestMethod]
    public void RejectedMemoryWithoutReviewExplanationFails()
    {
        var result = _validator.Validate(BuildItem() with
        {
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Rejected,
            Status = CollectiveMemoryStatus.Rejected,
            ReviewState = CollectiveMemoryReviewState.None,
            DecisionId = null,
            EvidenceRefs = [],
            Contradictions = []
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.RejectedExplanationRequired);
        AssertHasIssue(result, CollectiveMemoryContractValidator.EvidenceRequired);
    }

    [TestMethod]
    public void InvalidEnumValuesFail()
    {
        var result = _validator.Validate(BuildItem() with
        {
            MemoryType = (CollectiveMemoryType)999,
            AuthorityLevel = (CollectiveMemoryAuthorityLevel)999,
            Status = (CollectiveMemoryStatus)999,
            ReviewState = (CollectiveMemoryReviewState)999
        });

        AssertHasIssue(result, CollectiveMemoryContractValidator.InvalidEnumValue);
    }

    private static CollectiveMemoryItem BuildItem() =>
        new()
        {
            CollectiveMemoryId = "collective-memory-1",
            Scope = BuildScope(),
            MemoryType = CollectiveMemoryType.ArchitectureDecision,
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Candidate,
            Status = CollectiveMemoryStatus.Proposed,
            ReviewState = CollectiveMemoryReviewState.NeedsHumanReview,
            Title = "Weaviate is retrieval acceleration only",
            Summary = "SQL remains the governed source of authority for memory.",
            Sources = [BuildSource()],
            EvidenceRefs = [BuildEvidence()],
            Confidence = 0.7m,
            CreatedAt = Now,
            CollectiveMemoryJson = "{\"claim\":\"SQL remains authority\"}"
        };

    private static CollectiveMemoryItem BuildAcceptedItem() =>
        BuildItem() with
        {
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Accepted,
            Status = CollectiveMemoryStatus.Active,
            ReviewState = CollectiveMemoryReviewState.ApprovedForAcceptance,
            DecisionId = "decision-1",
            LastReviewedAt = Now
        };

    private static CollectiveMemoryItem BuildRejectedItem() =>
        BuildItem() with
        {
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Rejected,
            Status = CollectiveMemoryStatus.Rejected,
            ReviewState = CollectiveMemoryReviewState.RejectedByReview,
            DecisionId = "decision-reject-1",
            Contradictions = [BuildContradiction()]
        };

    private static CollectiveMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            KnowledgeDomainId = "memory-governance",
            ComponentId = "agent-memory",
            RepositoryId = "IronDeveloper"
        };

    private static CollectiveMemorySourceRef BuildSource() =>
        new()
        {
            SourceType = CollectiveMemorySourceType.HumanAuthoredDecision,
            SourceId = "decision-source-1",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            DecisionId = "decision-1",
            ObservedAt = Now
        };

    private static CollectiveMemoryEvidenceRef BuildEvidence() =>
        new()
        {
            EvidenceId = "evidence-1",
            EvidenceType = EvidenceType.DocumentReference,
            SourceId = "docs-memory-governance",
            Summary = "Architecture note states SQL is the memory authority source.",
            Weight = 0.8m,
            CapturedAt = Now
        };

    private static CollectiveMemoryContradictionRef BuildContradiction() =>
        new()
        {
            ContradictionId = "contradiction-1",
            Source = BuildSource() with { SourceId = "review-finding-1", SourceType = CollectiveMemorySourceType.CodeReviewFinding },
            Summary = "Reviewer found this candidate was not supported by current implementation.",
            Weight = 0.9m,
            ObservedAt = Now
        };

    private static void AssertHasIssue(IReadOnlyList<CollectiveMemoryValidationIssue> issues, string code)
    {
        Assert.IsTrue(issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static IEnumerable<string> EnumerateProductionFiles()
    {
        var productionRoots = new[]
        {
            "IronDev.Core",
            "IronDev.Infrastructure",
            "IronDev.Api",
            "IronDev.Client",
            "tools"
        };

        foreach (var root in productionRoots)
        {
            var path = Path.Combine(RepositoryRoot, root);

            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file);

                if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static bool IsUnderDirectory(string file, string relativeDirectory)
    {
        var expectedPrefix = relativeDirectory
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var relative = Path.GetRelativePath(RepositoryRoot, file);

        return relative.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IronDev.slnx")))
                return current;

            var parent = Directory.GetParent(current);

            if (parent is null)
                break;

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root from test base directory.");
    }
}
