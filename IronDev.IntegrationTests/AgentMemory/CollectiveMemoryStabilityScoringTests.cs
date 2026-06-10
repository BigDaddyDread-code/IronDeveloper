using System.Reflection;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Collective;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CollectiveMemoryStabilityScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private readonly CollectiveMemoryStabilityScorer _scorer = new();

    [TestMethod]
    public void CollectiveMemoryStabilityScoringContractTypesExist()
    {
        Assert.IsNotNull(typeof(CollectiveMemoryStabilityBand));
        Assert.IsNotNull(typeof(CollectiveMemoryAttractorSignalType));
        Assert.IsNotNull(typeof(CollectiveMemoryAttractorSignal));
        Assert.IsNotNull(typeof(CollectiveMemoryStabilityInput));
        Assert.IsNotNull(typeof(CollectiveMemoryStabilityBreakdown));
        Assert.IsNotNull(typeof(CollectiveMemoryScoringIssue));
        Assert.IsNotNull(typeof(CollectiveMemoryStabilityScore));
        Assert.IsNotNull(typeof(ICollectiveMemoryStabilityScorer));
        Assert.IsNotNull(typeof(CollectiveMemoryStabilityScorer));
    }

    [TestMethod]
    public void StrongEvidenceContributesEvidenceSupportScore()
    {
        var result = _scorer.Score(BuildInput(aggregate: BuildAggregate(quality: CollectiveMemoryEvidenceQuality.Strong)));

        Assert.AreEqual(1.0m, result.Breakdown.EvidenceSupportScore);
        Assert.IsTrue(result.Signals.Any(signal => signal.SignalType == CollectiveMemoryAttractorSignalType.EvidenceSupport));
    }

    [TestMethod]
    public void MultipleIndependentSourcesContributeSourceDiversityScore()
    {
        var result = _scorer.Score(BuildInput(aggregate: BuildAggregate(coverage: CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes)));

        Assert.AreEqual(1.0m, result.Breakdown.SourceDiversityScore);
        Assert.IsTrue(result.Signals.Any(signal => signal.SignalType == CollectiveMemoryAttractorSignalType.SourceDiversity));
    }

    [TestMethod]
    public void AcceptedAuthorityContributesAuthorityScore()
    {
        var result = _scorer.Score(BuildInput(memory: BuildAcceptedMemory()));

        Assert.AreEqual(1.0m, result.Breakdown.AuthorityScore);
        Assert.IsTrue(result.Signals.Any(signal => signal.SignalType == CollectiveMemoryAttractorSignalType.AcceptanceAuthority));
    }

    [TestMethod]
    public void RecentConfirmationContributesRecencyScore()
    {
        var result = _scorer.Score(BuildInput(memory: BuildAcceptedMemory() with { LastConfirmedAt = Now.AddDays(-3) }));

        Assert.AreEqual(1.0m, result.Breakdown.RecencyScore);
        Assert.IsTrue(result.Signals.Any(signal => signal.SignalType == CollectiveMemoryAttractorSignalType.RecentConfirmation));
    }

    [TestMethod]
    public void HighContradictionCreatesContradictionPenalty()
    {
        var result = _scorer.Score(BuildInput(aggregate: BuildAggregate(conflict: CollectiveMemoryEvidenceConflictLevel.High)));

        Assert.AreEqual(1.0m, result.Breakdown.ContradictionPenalty);
        Assert.AreEqual(CollectiveMemoryStabilityBand.Unstable, result.Band);
        Assert.IsTrue(result.Signals.Any(signal => signal.SignalType == CollectiveMemoryAttractorSignalType.ContradictionPressure && signal.IsNegative));
    }

    [TestMethod]
    public void RejectedMemoryIsUnstable()
    {
        var result = _scorer.Score(BuildInput(memory: BuildRejectedMemory()));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unstable, result.Band);
        Assert.AreEqual(1.0m, result.Breakdown.LifecyclePenalty);
    }

    [TestMethod]
    public void InvalidatedMemoryIsUnstable()
    {
        var result = _scorer.Score(BuildInput(memory: BuildAcceptedMemory() with { Status = CollectiveMemoryStatus.Invalidated }));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unstable, result.Band);
        Assert.AreEqual(1.0m, result.Breakdown.LifecyclePenalty);
    }

    [TestMethod]
    public void ExpiredMemoryAddsExpiryPenalty()
    {
        var result = _scorer.Score(BuildInput(memory: BuildAcceptedMemory() with { ExpiresAt = Now.AddDays(-1) }));

        Assert.AreEqual(1.0m, result.Breakdown.ExpiryPenalty);
        Assert.IsTrue(result.Signals.Any(signal => signal.SignalType == CollectiveMemoryAttractorSignalType.ExpiryPressure && signal.IsNegative));
    }

    [TestMethod]
    public void HighConflictCannotBecomeStronglyStable()
    {
        var result = _scorer.Score(BuildInput(
            memory: BuildAcceptedMemory() with { LastConfirmedAt = Now },
            aggregate: BuildAggregate(conflict: CollectiveMemoryEvidenceConflictLevel.High)));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unstable, result.Band);
        Assert.AreNotEqual(CollectiveMemoryStabilityBand.StronglyStable, result.Band);
    }

    [TestMethod]
    public void AcceptedReviewedMemoryWithMediumConflictIsStableNotAcceptedByScoring()
    {
        var memory = BuildAcceptedMemory() with { LastConfirmedAt = Now };
        var result = _scorer.Score(BuildInput(
            memory: memory,
            aggregate: BuildAggregate(conflict: CollectiveMemoryEvidenceConflictLevel.Medium),
            events: [BuildEvent(CollectiveMemoryEventType.Accepted)]));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Stable, result.Band);
        Assert.AreEqual(CollectiveMemoryAuthorityLevel.Accepted, memory.AuthorityLevel);
    }

    [TestMethod]
    public void CandidateWithLimitedSupportIsEmerging()
    {
        var result = _scorer.Score(BuildInput(
            memory: BuildCandidateMemory() with { Status = CollectiveMemoryStatus.Active },
            aggregate: BuildAggregate(
                quality: CollectiveMemoryEvidenceQuality.Moderate,
                coverage: CollectiveMemoryEvidenceCoverage.SingleSource)));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Emerging, result.Band);
    }

    [TestMethod]
    public void ValidationErrorsReturnUnknownBand()
    {
        var result = _scorer.Score(BuildInput(memory: BuildCandidateMemory() with { Title = string.Empty }));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        Assert.IsTrue(result.HasErrors);
        AssertHasIssue(result, CollectiveMemoryContractValidator.TitleRequired);
    }

    [TestMethod]
    public void ScoreIsClampedToZeroWhenPenaltiesExceedPositiveSignals()
    {
        var result = _scorer.Score(BuildInput(
            memory: BuildRejectedMemory() with { ExpiresAt = Now.AddDays(-1) },
            aggregate: BuildAggregate(
                quality: CollectiveMemoryEvidenceQuality.Unknown,
                coverage: CollectiveMemoryEvidenceCoverage.None,
                conflict: CollectiveMemoryEvidenceConflictLevel.High)));

        Assert.AreEqual(0m, result.Score);
        Assert.IsTrue(result.Signals.All(signal => signal.Weight is >= 0m and <= 1m));
    }

    [TestMethod]
    public void ScoringIsDeterministicForSameInput()
    {
        var input = BuildInput(memory: BuildAcceptedMemory() with { LastConfirmedAt = Now });

        var first = _scorer.Score(input);
        var second = _scorer.Score(input);

        Assert.AreEqual(first.Score, second.Score);
        Assert.AreEqual(first.Band, second.Band);
        CollectionAssert.AreEqual(
            first.Signals.Select(signal => $"{signal.SignalType}:{signal.Weight}:{signal.IsNegative}").ToArray(),
            second.Signals.Select(signal => $"{signal.SignalType}:{signal.Weight}:{signal.IsNegative}").ToArray());
    }

    [TestMethod]
    public void ScoringDoesNotMutateMemoryAuthorityStatusOrReviewState()
    {
        var memory = BuildCandidateMemory();
        var authority = memory.AuthorityLevel;
        var status = memory.Status;
        var reviewState = memory.ReviewState;

        _ = _scorer.Score(BuildInput(memory: memory));

        Assert.AreEqual(authority, memory.AuthorityLevel);
        Assert.AreEqual(status, memory.Status);
        Assert.AreEqual(reviewState, memory.ReviewState);
    }

    [TestMethod]
    public void StabilityScoreDoesNotCreateCollectiveMemoryItemOrPromotionResult()
    {
        var result = _scorer.Score(BuildInput());

        Assert.IsFalse(result.GetType().GetProperties().Any(property =>
            property.PropertyType == typeof(CollectiveMemoryItem)));
        Assert.IsFalse(result.GetType().GetProperties().Any(property =>
            property.PropertyType.Name.Contains("Promotion", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void StabilityScoreExposesNoRetrievalPromotionOrAcceptanceDecisionFields()
    {
        var forbiddenNames = new[]
        {
            "RetrievalBoost",
            "PromotionDecision",
            "AcceptedDecision",
            "Promote",
            "Retrieve"
        };

        var names = typeof(CollectiveMemoryStabilityScore)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Concat(typeof(CollectiveMemoryStabilityBreakdown).GetProperties().Select(property => property.Name))
            .Concat(typeof(CollectiveMemoryAttractorSignal).GetProperties().Select(property => property.Name))
            .ToArray();

        foreach (var forbidden in forbiddenNames)
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"Stability score should not expose '{forbidden}' decision fields.");
        }
    }

    [TestMethod]
    public void CollectiveMemoryStabilityScorerHasNoDatabaseWeaviateStoreOrRuntimeDependency()
    {
        var implementationText = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "IronDev.Core",
            "AgentMemory",
            "Collective",
            "CollectiveMemoryStabilityScorer.cs"));

        Assert.IsFalse(implementationText.Contains("Sql", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("Db", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("Weaviate", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(implementationText.Contains("Store", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("Conscience", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("Tool", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("AgentSkillExecution", StringComparison.Ordinal));
        Assert.IsFalse(implementationText.Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CollectiveMemoryStabilityScoringDatabaseScanFindsNoSqlMigrationOrProcedure()
    {
        var databaseDirectory = Path.Combine(RepositoryRoot, "Database");

        if (!Directory.Exists(databaseDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(databaseDirectory, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            Assert.IsFalse(text.Contains("migrate_collective_memory_stability", StringComparison.Ordinal),
                $"Collective memory stability migration token found in {file}.");
            Assert.IsFalse(text.Contains("usp_CollectiveMemoryStability", StringComparison.Ordinal),
                $"Collective memory stability procedure token found in {file}.");
            Assert.IsFalse(text.Contains("SqlCollectiveMemoryStabilityStore", StringComparison.Ordinal),
                $"Collective memory stability store token found in {file}.");
        }
    }

    [TestMethod]
    public void RuntimeServicesDoNotUseCollectiveMemoryStabilityScorer()
    {
        foreach (var file in EnumerateProductionFiles().Where(file => !IsUnderDirectory(file, Path.Combine("IronDev.Core", "AgentMemory", "Collective"))))
        {
            if (file.EndsWith(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlCollectiveMemoryRetrievalService.cs"), StringComparison.OrdinalIgnoreCase))
                continue;

            var text = File.ReadAllText(file);

            Assert.IsFalse(text.Contains("ICollectiveMemoryStabilityScorer", StringComparison.Ordinal),
                $"Runtime production file uses stability scorer interface: {file}");
            Assert.IsFalse(text.Contains("CollectiveMemoryStabilityScorer", StringComparison.Ordinal),
                $"Runtime production file uses stability scorer: {file}");
            Assert.IsFalse(text.Contains("CollectiveMemoryStabilityScore", StringComparison.Ordinal),
                $"Runtime production file uses stability score: {file}");
        }
    }

    [TestMethod]
    public void NullInputReturnsUnknownWithIssue()
    {
        var result = _scorer.Score(null);

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryStabilityScorer.InputRequired);
    }

    [TestMethod]
    public void MissingStabilityRunIdReturnsUnknownWithIssue()
    {
        var result = _scorer.Score(BuildInput() with { StabilityRunId = string.Empty });

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryStabilityScorer.StabilityRunIdRequired);
    }

    [TestMethod]
    public void NullMemoryReturnsUnknownWithIssue()
    {
        var result = _scorer.Score(BuildInput() with { Memory = null! });

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryStabilityScorer.MemoryRequired);
    }

    [TestMethod]
    public void InvalidMemoryReturnsUnknownWithValidatorIssue()
    {
        var result = _scorer.Score(BuildInput(memory: BuildCandidateMemory() with { Summary = string.Empty }));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryContractValidator.SummaryRequired);
    }

    [TestMethod]
    public void NullEvidenceAggregateReturnsUnknownWithIssue()
    {
        var result = _scorer.Score(BuildInput() with { EvidenceAggregate = null! });

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryStabilityScorer.EvidenceAggregateRequired);
    }

    [TestMethod]
    public void AggregateMemoryIdMismatchReturnsUnknownWithIssue()
    {
        var result = _scorer.Score(BuildInput(aggregate: BuildAggregate(collectiveMemoryId: "different-memory")));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryStabilityScorer.AggregateMemoryIdMismatch);
    }

    [TestMethod]
    public void AggregateScopeMismatchReturnsUnknownWithIssue()
    {
        var result = _scorer.Score(BuildInput(aggregate: BuildAggregate(scope: BuildScope() with { ProjectId = "other-project" })));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryStabilityScorer.AggregateScopeMismatch);
    }

    [TestMethod]
    public void DefaultEvaluatedAtReturnsUnknownWithIssue()
    {
        var result = _scorer.Score(BuildInput() with { EvaluatedAt = default });

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryStabilityScorer.EvaluatedAtRequired);
    }

    [TestMethod]
    public void RawPromptMarkerReturnsUnknownWithValidatorIssue()
    {
        var result = _scorer.Score(BuildInput(memory: BuildCandidateMemory() with { Summary = "RawPrompt: hidden prompt" }));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryContractValidator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void ChainOfThoughtMarkerReturnsUnknownWithValidatorIssue()
    {
        var result = _scorer.Score(BuildInput(memory: BuildCandidateMemory() with
        {
            CollectiveMemoryJson = "{\"ChainOfThought\":\"hidden reasoning\"}"
        }));

        Assert.AreEqual(CollectiveMemoryStabilityBand.Unknown, result.Band);
        AssertHasIssue(result, CollectiveMemoryContractValidator.RawPrivateReasoningBlocked);
    }

    private static CollectiveMemoryStabilityInput BuildInput(
        CollectiveMemoryItem? memory = null,
        CollectiveMemoryEvidenceAggregate? aggregate = null,
        IReadOnlyList<CollectiveMemoryEventRecord>? events = null) =>
        new()
        {
            StabilityRunId = "stability-run-1",
            Memory = memory ?? BuildCandidateMemory(),
            EvidenceAggregate = aggregate ?? BuildAggregate(),
            Events = events ?? [],
            EvaluatedAt = Now,
            RequestedByUserId = "human-reviewer-1",
            DecisionId = "decision-stability-1",
            CorrelationId = "correlation-stability-1"
        };

    private static CollectiveMemoryItem BuildCandidateMemory() =>
        new()
        {
            CollectiveMemoryId = "collective-memory-1",
            Scope = BuildScope(),
            MemoryType = CollectiveMemoryType.ArchitectureDecision,
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Candidate,
            Status = CollectiveMemoryStatus.Proposed,
            ReviewState = CollectiveMemoryReviewState.NeedsHumanReview,
            Title = "SQL remains memory authority",
            Summary = "SQL remains the governed source of memory authority.",
            Sources = [BuildSource("source-1", CollectiveMemorySourceType.RunMemoryReport)],
            EvidenceRefs = [BuildEvidence("evidence-1", "source-1")],
            Confidence = 0.7m,
            CreatedAt = Now.AddDays(-20),
            CollectiveMemoryJson = "{\"claim\":\"SQL remains memory authority\"}"
        };

    private static CollectiveMemoryItem BuildAcceptedMemory() =>
        BuildCandidateMemory() with
        {
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Accepted,
            Status = CollectiveMemoryStatus.Active,
            ReviewState = CollectiveMemoryReviewState.ApprovedForAcceptance,
            DecisionId = "decision-accepted-1",
            LastReviewedAt = Now.AddDays(-5),
            LastConfirmedAt = Now.AddDays(-4),
            Sources =
            [
                BuildSource("source-1", CollectiveMemorySourceType.RunMemoryReport),
                BuildSource("source-2", CollectiveMemorySourceType.MemoryExecutionAudit)
            ],
            EvidenceRefs =
            [
                BuildEvidence("evidence-1", "source-1"),
                BuildEvidence("evidence-2", "source-2")
            ]
        };

    private static CollectiveMemoryItem BuildRejectedMemory() =>
        BuildCandidateMemory() with
        {
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Rejected,
            Status = CollectiveMemoryStatus.Rejected,
            ReviewState = CollectiveMemoryReviewState.RejectedByReview,
            DecisionId = "decision-rejected-1",
            Contradictions =
            [
                new CollectiveMemoryContradictionRef
                {
                    ContradictionId = "contradiction-1",
                    Source = BuildSource("review-finding-1", CollectiveMemorySourceType.CodeReviewFinding),
                    Summary = "Reviewer found this candidate unsupported.",
                    Weight = 0.9m,
                    ObservedAt = Now
                }
            ]
        };

    private static CollectiveMemoryEvidenceAggregate BuildAggregate(
        string collectiveMemoryId = "collective-memory-1",
        CollectiveMemoryScope? scope = null,
        CollectiveMemoryEvidenceQuality quality = CollectiveMemoryEvidenceQuality.Strong,
        CollectiveMemoryEvidenceCoverage coverage = CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes,
        CollectiveMemoryEvidenceConflictLevel conflict = CollectiveMemoryEvidenceConflictLevel.None) =>
        new()
        {
            AggregationId = "aggregation-1",
            CollectiveMemoryId = collectiveMemoryId,
            Scope = scope ?? BuildScope(),
            SupportingEvidenceCount = quality == CollectiveMemoryEvidenceQuality.Unknown ? 0 : 2,
            WeakSupportingEvidenceCount = 0,
            NeutralEvidenceCount = 0,
            ContradictingEvidenceCount = conflict == CollectiveMemoryEvidenceConflictLevel.None ? 0 : 1,
            WeakContradictingEvidenceCount = 0,
            UniqueSourceCount = coverage == CollectiveMemoryEvidenceCoverage.SingleSource ? 1 : 2,
            UniqueSourceTypeCount = coverage == CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes ? 2 : 1,
            SupportWeight = quality == CollectiveMemoryEvidenceQuality.Unknown ? 0m : 2.0m,
            ContradictionWeight = conflict == CollectiveMemoryEvidenceConflictLevel.None ? 0m : 1.0m,
            EvidenceQuality = quality,
            EvidenceCoverage = coverage,
            ConflictLevel = conflict,
            Readiness = conflict is CollectiveMemoryEvidenceConflictLevel.Medium or CollectiveMemoryEvidenceConflictLevel.High
                ? CollectiveMemoryEvidenceReadiness.NeedsContradictionReview
                : CollectiveMemoryEvidenceReadiness.ReadyForHumanReview,
            AggregatedAt = Now,
            EvidenceContributionIds = ["support-1", "support-2"],
            ContradictionContributionIds = conflict == CollectiveMemoryEvidenceConflictLevel.None ? [] : ["contradiction-1"],
            ReviewWarnings = ["Ready for human review only; this does not grant authority."]
        };

    private static CollectiveMemoryEventRecord BuildEvent(CollectiveMemoryEventType eventType) =>
        new()
        {
            CollectiveMemoryEventId = $"event-{eventType.ToString().ToLowerInvariant()}-1",
            CollectiveMemoryId = "collective-memory-1",
            EventType = eventType,
            Reason = "Governed collective memory event.",
            CreatedAt = Now.AddDays(-1)
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
        CollectiveMemorySourceType sourceType) =>
        new()
        {
            SourceType = sourceType,
            SourceId = sourceId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            RunId = "run-1",
            AgentId = "builder-agent",
            DecisionId = "decision-source-1",
            EvidenceUri = $"memory://{sourceId}",
            ObservedAt = Now
        };

    private static CollectiveMemoryEvidenceRef BuildEvidence(string evidenceId, string sourceId) =>
        new()
        {
            EvidenceId = evidenceId,
            EvidenceType = EvidenceType.RunReport,
            SourceId = sourceId,
            Summary = "Governed memory evidence.",
            Weight = 0.8m,
            CapturedAt = Now
        };

    private static void AssertHasIssue(CollectiveMemoryStabilityScore result, string code)
    {
        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected stability issue '{code}' but got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static IEnumerable<string> EnumerateProductionFiles()
    {
        foreach (var root in new[] { "IronDev.Core", "IronDev.Infrastructure", "IronDev.Api", "IronDev.Client", "tools" })
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
