using System.Reflection;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Collective;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CollectiveMemoryEvidenceAggregationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private readonly CollectiveMemoryEvidenceAggregator _aggregator = new();

    [TestMethod]
    public void CollectiveMemoryEvidenceAggregationContractTypesExist()
    {
        Assert.IsNotNull(typeof(CollectiveMemoryEvidenceAggregate));
        Assert.IsNotNull(typeof(CollectiveMemoryEvidenceContribution));
        Assert.IsNotNull(typeof(CollectiveMemoryContradictionContribution));
        Assert.IsNotNull(typeof(CollectiveMemoryAggregationInput));
        Assert.IsNotNull(typeof(CollectiveMemoryAggregationResult));
        Assert.IsNotNull(typeof(ICollectiveMemoryEvidenceAggregator));
        Assert.IsNotNull(typeof(CollectiveMemoryEvidenceAggregator));
    }

    [TestMethod]
    public void SupportingEvidenceIncrementsSupportCount()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim)
        ]));

        Assert.AreEqual(1, result.Aggregate.SupportingEvidenceCount);
        Assert.AreEqual(1.0m, result.Aggregate.SupportWeight);
    }

    [TestMethod]
    public void WeakSupportingEvidenceContributesHalfWeight()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-weak-1", CollectiveMemoryEvidenceContributionType.WeaklySupportsClaim)
        ]));

        Assert.AreEqual(1, result.Aggregate.WeakSupportingEvidenceCount);
        Assert.AreEqual(0.5m, result.Aggregate.SupportWeight);
    }

    [TestMethod]
    public void NeutralEvidenceContributesNoSupportOrContradictionWeight()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("neutral-1", CollectiveMemoryEvidenceContributionType.NeutralContext)
        ]));

        Assert.AreEqual(1, result.Aggregate.NeutralEvidenceCount);
        Assert.AreEqual(0m, result.Aggregate.SupportWeight);
        Assert.AreEqual(0m, result.Aggregate.ContradictionWeight);
    }

    [TestMethod]
    public void ContradictingEvidenceIncrementsContradictionCount()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("contra-1", CollectiveMemoryEvidenceContributionType.ContradictsClaim)
        ]));

        Assert.AreEqual(1, result.Aggregate.ContradictingEvidenceCount);
        Assert.AreEqual(1.0m, result.Aggregate.ContradictionWeight);
    }

    [TestMethod]
    public void WeakContradictionContributesHalfContradictionWeight()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("contra-weak-1", CollectiveMemoryEvidenceContributionType.WeaklyContradictsClaim)
        ]));

        Assert.AreEqual(1, result.Aggregate.WeakContradictingEvidenceCount);
        Assert.AreEqual(0.5m, result.Aggregate.ContradictionWeight);
    }

    [TestMethod]
    public void ExplicitContradictionContributionIncrementsContradictionWeight()
    {
        var result = _aggregator.Aggregate(BuildInput(
            [BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim)],
            [BuildContradictionContribution("explicit-contra-1", weight: 0.8m)]));

        Assert.AreEqual(1.0m, result.Aggregate.SupportWeight);
        Assert.AreEqual(0.8m, result.Aggregate.ContradictionWeight);
        CollectionAssert.Contains(result.Aggregate.ContradictionContributionIds.ToArray(), "explicit-contra-1");
    }

    [TestMethod]
    public void UniqueSourceCountIsCalculated()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-1"),
            BuildContribution("support-2", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-2"),
            BuildContribution("support-3", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-2")
        ]));

        Assert.AreEqual(2, result.Aggregate.UniqueSourceCount);
    }

    [TestMethod]
    public void UniqueSourceTypeCountIsCalculated()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceType: CollectiveMemorySourceType.RunMemoryReport),
            BuildContribution("support-2", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceType: CollectiveMemorySourceType.MemoryExecutionAudit)
        ]));

        Assert.AreEqual(2, result.Aggregate.UniqueSourceTypeCount);
    }

    [TestMethod]
    public void SingleSourceCoverageIsSingleSource()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-1")
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceCoverage.SingleSource, result.Aggregate.EvidenceCoverage);
    }

    [TestMethod]
    public void MultipleSameTypeSourcesGivesMultipleSameTypeCoverage()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-1", sourceType: CollectiveMemorySourceType.RunMemoryReport),
            BuildContribution("support-2", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-2", sourceType: CollectiveMemorySourceType.RunMemoryReport)
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceCoverage.MultipleSameTypeSources, result.Aggregate.EvidenceCoverage);
    }

    [TestMethod]
    public void MultipleSourceTypesGivesMultipleIndependentSourceTypeCoverage()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-1", sourceType: CollectiveMemorySourceType.RunMemoryReport),
            BuildContribution("support-2", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-2", sourceType: CollectiveMemorySourceType.MemoryExecutionAudit)
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes, result.Aggregate.EvidenceCoverage);
    }

    [TestMethod]
    public void StrongEvidenceQualityRequiresMultipleSourceTypesAndNoContradiction()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-1", sourceType: CollectiveMemorySourceType.RunMemoryReport),
            BuildContribution("support-2", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-2", sourceType: CollectiveMemorySourceType.MemoryExecutionAudit)
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceQuality.Strong, result.Aggregate.EvidenceQuality);
    }

    [TestMethod]
    public void ModerateEvidenceQualityHandlesLimitedSupport()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-1", sourceType: CollectiveMemorySourceType.RunMemoryReport)
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceQuality.Moderate, result.Aggregate.EvidenceQuality);
    }

    [TestMethod]
    public void HighConflictWhenContradictionWeightEqualsOrExceedsSupportWeight()
    {
        var result = _aggregator.Aggregate(BuildInput(
            [BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, weight: 0.5m)],
            [BuildContradictionContribution("contra-1", weight: 0.5m)]));

        Assert.AreEqual(CollectiveMemoryEvidenceConflictLevel.High, result.Aggregate.ConflictLevel);
    }

    [TestMethod]
    public void ReadyForHumanReviewRequiresEnoughSupportAndManageableConflict()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-1", sourceType: CollectiveMemorySourceType.RunMemoryReport),
            BuildContribution("support-2", CollectiveMemoryEvidenceContributionType.SupportsClaim, sourceId: "source-2", sourceType: CollectiveMemorySourceType.MemoryExecutionAudit)
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceReadiness.ReadyForHumanReview, result.Aggregate.Readiness);
        Assert.IsTrue(result.Aggregate.ReviewWarnings.Any(warning => warning.Contains("human review only", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void NeedsContradictionReviewWhenConflictIsMediumOrHigh()
    {
        var result = _aggregator.Aggregate(BuildInput(
            [BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim)],
            [BuildContradictionContribution("contra-1", weight: 0.8m)]));

        Assert.AreEqual(CollectiveMemoryEvidenceReadiness.NeedsContradictionReview, result.Aggregate.Readiness);
    }

    [TestMethod]
    public void NeedsMoreSourcesForSingleSourceSupport()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.WeaklySupportsClaim, sourceId: "source-1")
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceReadiness.NeedsMoreSources, result.Aggregate.Readiness);
    }

    [TestMethod]
    public void InsufficientEvidenceWhenNoSupport()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("neutral-1", CollectiveMemoryEvidenceContributionType.NeutralContext)
        ]));

        Assert.AreEqual(CollectiveMemoryEvidenceQuality.Unknown, result.Aggregate.EvidenceQuality);
        Assert.AreEqual(CollectiveMemoryEvidenceReadiness.InsufficientEvidence, result.Aggregate.Readiness);
    }

    [TestMethod]
    public void NullInputFails()
    {
        var result = _aggregator.Aggregate(null!);

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.InputRequired);
        Assert.AreEqual(CollectiveMemoryEvidenceReadiness.InsufficientEvidence, result.Aggregate.Readiness);
    }

    [TestMethod]
    public void NullCandidateFails()
    {
        var result = _aggregator.Aggregate(BuildInput() with { Candidate = null! });

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.CandidateRequired);
    }

    [TestMethod]
    public void InvalidCandidateFails()
    {
        var result = _aggregator.Aggregate(BuildInput() with
        {
            Candidate = BuildCandidate() with { Title = string.Empty }
        });

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.CandidateInvalid);
    }

    [TestMethod]
    public void MissingAggregationIdFails()
    {
        var result = _aggregator.Aggregate(BuildInput() with { AggregationId = string.Empty });

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.AggregationIdRequired);
    }

    [TestMethod]
    public void MissingEvidenceContributionsFails()
    {
        var result = _aggregator.Aggregate(BuildInput() with { EvidenceContributions = null! });

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.EvidenceContributionsRequired);
    }

    [TestMethod]
    public void ContributionMissingSourceFails()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim) with { Source = null! }
        ]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.ContributionSourceRequired);
    }

    [TestMethod]
    public void ContributionMissingEvidenceFails()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim) with { Evidence = null! }
        ]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.ContributionEvidenceRequired);
    }

    [TestMethod]
    public void ContributionWeightBelowZeroFails()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, weight: -0.1m)
        ]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.ContributionWeightOutOfRange);
    }

    [TestMethod]
    public void ContributionWeightAboveOneFails()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim, weight: 1.1m)
        ]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.ContributionWeightOutOfRange);
    }

    [TestMethod]
    public void ContradictionContributionMissingContradictionFails()
    {
        var result = _aggregator.Aggregate(BuildInput(
            [BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim)],
            [BuildContradictionContribution("contra-1") with { Contradiction = null! }]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.ContradictionRequired);
    }

    [TestMethod]
    public void ContradictionContributionWeightOutsideRangeFails()
    {
        var result = _aggregator.Aggregate(BuildInput(
            [BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim)],
            [BuildContradictionContribution("contra-1", weight: 1.1m)]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.ContradictionWeightOutOfRange);
    }

    [TestMethod]
    public void RawPromptMarkerFails()
    {
        var result = _aggregator.Aggregate(BuildInput([
            BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim) with
            {
                Summary = "RawPrompt: hidden text"
            }
        ]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void ChainOfThoughtMarkerFails()
    {
        var result = _aggregator.Aggregate(BuildInput(
            [BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim)],
            [BuildContradictionContribution("contra-1") with { Summary = "ChainOfThought: no" }]));

        AssertHasIssue(result, CollectiveMemoryEvidenceAggregator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void AggregationDoesNotMutateCandidateAuthorityStatusOrReviewState()
    {
        var candidate = BuildCandidate();
        var authority = candidate.AuthorityLevel;
        var status = candidate.Status;
        var reviewState = candidate.ReviewState;

        _ = _aggregator.Aggregate(BuildInput() with { Candidate = candidate });

        Assert.AreEqual(authority, candidate.AuthorityLevel);
        Assert.AreEqual(status, candidate.Status);
        Assert.AreEqual(reviewState, candidate.ReviewState);
    }

    [TestMethod]
    public void AggregationDoesNotCreateCollectiveMemoryItem()
    {
        var result = _aggregator.Aggregate(BuildInput());

        Assert.IsFalse(result.GetType().GetProperties().Any(property =>
            property.PropertyType == typeof(CollectiveMemoryItem)));
        Assert.IsFalse(result.Aggregate.GetType().GetProperties().Any(property =>
            property.PropertyType == typeof(CollectiveMemoryItem)));
    }

    [TestMethod]
    public void AggregationResultContainsNoAuthorityOrPromotionDecisionFields()
    {
        var forbiddenNames = new[]
        {
            "Accepted",
            "Promoted",
            "Stable",
            "Attractor",
            "Authority",
            "Promotion",
            "Retrieval"
        };

        var names = typeof(CollectiveMemoryEvidenceAggregate)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Concat(Enum.GetNames<CollectiveMemoryEvidenceReadiness>())
            .ToArray();

        foreach (var forbidden in forbiddenNames)
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"Aggregation result should not expose '{forbidden}' decision fields.");
        }
    }

    [TestMethod]
    public void CollectiveMemoryEvidenceAggregationStaticScanFindsNoActiveInfrastructure()
    {
        var forbiddenTokens = new[]
        {
            "WeaviateCollectiveMemory",
            "RuntimeCollectiveMemoryRetrieval",
            "CollectiveMemoryRuntimeRetriever",
            "CollectiveMemoryToolExecution",
            "CollectiveMemoryConscienceIntegration",
            "RetrievalBoost",
            "SqlCollectiveMemoryStabilityStore",
            "migrate_collective_memory_stability",
            "usp_CollectiveMemoryStability",
            "RuntimeCollectiveMemoryScorer",
            "AutoCollectiveMemoryRetrieval"
        };

        foreach (var file in EnumerateProductionFiles())
        {
            var text = File.ReadAllText(file);

            foreach (var forbidden in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal),
                    $"Forbidden collective memory aggregation token '{forbidden}' was found in {file}.");
            }
        }
    }

    [TestMethod]
    public void CollectiveMemoryEvidenceAggregationDatabaseScanFindsNoRetrievalAttractorOrWeaviateSql()
    {
        var databaseDirectory = Path.Combine(RepositoryRoot, "Database");

        if (!Directory.Exists(databaseDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(databaseDirectory, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            Assert.IsFalse(text.Contains("WeaviateCollectiveMemory", StringComparison.Ordinal),
                $"Collective evidence aggregation Weaviate token found in {file}.");
            Assert.IsFalse(text.Contains("RuntimeCollectiveMemoryRetrieval", StringComparison.Ordinal),
                $"Collective evidence aggregation runtime retrieval token found in {file}.");
            Assert.IsFalse(text.Contains("CollectiveMemoryRuntimeRetriever", StringComparison.Ordinal),
                $"Collective evidence aggregation runtime retriever token found in {file}.");
            Assert.IsFalse(text.Contains("CollectiveMemoryToolExecution", StringComparison.Ordinal),
                $"Collective evidence aggregation tool execution token found in {file}.");
            Assert.IsFalse(text.Contains("CollectiveMemoryConscienceIntegration", StringComparison.Ordinal),
                $"Collective evidence aggregation Conscience integration token found in {file}.");
            Assert.IsFalse(text.Contains("RetrievalBoost", StringComparison.Ordinal),
                $"Collective evidence aggregation retrieval boost token found in {file}.");
            Assert.IsFalse(text.Contains("AutoCollectiveMemoryRetrieval", StringComparison.Ordinal),
                $"Collective evidence aggregation automatic retrieval token found in {file}.");
            Assert.IsFalse(text.Contains("SqlCollectiveMemoryStabilityStore", StringComparison.Ordinal),
                $"Collective evidence aggregation stability store token found in {file}.");
            Assert.IsFalse(text.Contains("migrate_collective_memory_stability", StringComparison.Ordinal),
                $"Collective evidence aggregation stability migration token found in {file}.");
            Assert.IsFalse(text.Contains("usp_CollectiveMemoryStability", StringComparison.Ordinal),
                $"Collective evidence aggregation stability procedure token found in {file}.");
            Assert.IsFalse(text.Contains("RuntimeCollectiveMemoryScorer", StringComparison.Ordinal),
                $"Collective evidence aggregation runtime scorer token found in {file}.");
        }
    }

    [TestMethod]
    public void RuntimeServicesDoNotUseCollectiveMemoryEvidenceAggregator()
    {
        var runtimeFiles = EnumerateProductionFiles()
            .Where(file => !IsUnderDirectory(file, Path.Combine("IronDev.Core", "AgentMemory", "Collective")))
            .ToArray();

        foreach (var file in runtimeFiles)
        {
            var text = File.ReadAllText(file);

            Assert.IsFalse(text.Contains("CollectiveMemoryEvidenceAggregator", StringComparison.Ordinal),
                $"Runtime production file uses CollectiveMemoryEvidenceAggregator: {file}");
        }
    }

    [TestMethod]
    public void CollectiveMemoryEvidenceAggregatorHasNoDatabaseWeaviateOrStoreDependency()
    {
        var implementationText = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "IronDev.Core",
            "AgentMemory",
            "Collective",
            "CollectiveMemoryEvidenceAggregator.cs"));

        Assert.IsFalse(implementationText.Contains("Sql", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("Db", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("Weaviate", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(implementationText.Contains("Store", StringComparison.Ordinal));
    }

    private static CollectiveMemoryAggregationInput BuildInput(
        IReadOnlyList<CollectiveMemoryEvidenceContribution>? evidenceContributions = null,
        IReadOnlyList<CollectiveMemoryContradictionContribution>? contradictionContributions = null) =>
        new()
        {
            AggregationId = "aggregation-1",
            Candidate = BuildCandidate(),
            EvidenceContributions = evidenceContributions ??
            [
                BuildContribution("support-1", CollectiveMemoryEvidenceContributionType.SupportsClaim)
            ],
            ContradictionContributions = contradictionContributions ?? [],
            AggregatedAt = Now,
            RequestedByUserId = "user-1",
            DecisionId = "decision-aggregation-1",
            ThoughtLedgerEntryId = "thought-ledger-1",
            CorrelationId = "correlation-1"
        };

    private static CollectiveMemoryItem BuildCandidate() =>
        new()
        {
            CollectiveMemoryId = "collective-memory-1",
            Scope = BuildScope(),
            MemoryType = CollectiveMemoryType.ArchitectureDecision,
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Candidate,
            Status = CollectiveMemoryStatus.Proposed,
            ReviewState = CollectiveMemoryReviewState.NeedsHumanReview,
            Title = "SQL remains memory authority",
            Summary = "SQL is the governed source of memory authority.",
            Sources = [BuildSource("candidate-source-1")],
            EvidenceRefs = [BuildEvidence("candidate-evidence-1")],
            Confidence = 0.7m,
            CreatedAt = Now
        };

    private static CollectiveMemoryEvidenceContribution BuildContribution(
        string contributionId,
        CollectiveMemoryEvidenceContributionType contributionType,
        string? sourceId = null,
        CollectiveMemorySourceType sourceType = CollectiveMemorySourceType.RunMemoryReport,
        decimal weight = 1.0m) =>
        new()
        {
            ContributionId = contributionId,
            ContributionType = contributionType,
            Source = BuildSource(sourceId ?? $"source-{contributionId}", sourceType),
            Evidence = BuildEvidence($"evidence-{contributionId}", sourceId ?? $"source-{contributionId}"),
            Weight = weight,
            Summary = $"Evidence contribution {contributionId}.",
            ObservedAt = Now
        };

    private static CollectiveMemoryContradictionContribution BuildContradictionContribution(
        string contributionId,
        decimal weight = 1.0m) =>
        new()
        {
            ContributionId = contributionId,
            Contradiction = new CollectiveMemoryContradictionRef
            {
                ContradictionId = $"contradiction-{contributionId}",
                Source = BuildSource($"contradiction-source-{contributionId}", CollectiveMemorySourceType.CodeReviewFinding),
                Summary = "Counter-evidence exists and needs review.",
                Weight = weight,
                ObservedAt = Now
            },
            Weight = weight,
            Summary = "Explicit contradiction contribution.",
            ObservedAt = Now
        };

    private static CollectiveMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            KnowledgeDomainId = "memory-governance",
            ComponentId = "collective-memory",
            RepositoryId = "IronDeveloper"
        };

    private static CollectiveMemorySourceRef BuildSource(
        string sourceId,
        CollectiveMemorySourceType sourceType = CollectiveMemorySourceType.HumanAuthoredDecision) =>
        new()
        {
            SourceType = sourceType,
            SourceId = sourceId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            RunId = "run-1",
            AgentId = "builder-agent",
            DecisionId = "decision-1",
            EvidenceUri = $"memory://{sourceId}",
            ObservedAt = Now
        };

    private static CollectiveMemoryEvidenceRef BuildEvidence(
        string evidenceId,
        string sourceId = "source-1") =>
        new()
        {
            EvidenceId = evidenceId,
            EvidenceType = EvidenceType.RunReport,
            SourceId = sourceId,
            Summary = "Governed evidence summary.",
            Weight = 0.8m,
            CapturedAt = Now
        };

    private static void AssertHasIssue(CollectiveMemoryAggregationResult result, string code)
    {
        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected aggregation issue '{code}' but got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
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
                    relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith(Path.Combine("IronDev.Core", "AgentMemory", "Collective", "CollectiveMemoryContractValidator.cs"), StringComparison.OrdinalIgnoreCase))
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
