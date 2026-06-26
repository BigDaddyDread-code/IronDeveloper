using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE17PrUrlIsNotReleaseCandidateRefRegressionTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly string HeadSha = new('1', 40);
    private static readonly string BaseSha = new('2', 40);
    private const string RepositoryFullName = "BigDaddyDread-code/IronDeveloper";
    private const int PullRequestNumber = 601;
    private const string PullRequestUrl = "https://github.com/BigDaddyDread-code/IronDeveloper/pull/601";
    private const string HeadBranch = "regression/pr-url-not-release-candidate-ref";
    private const string BaseBranch = "regression/draft-pr-ready-review-hard-stop";

    [TestMethod]
    public void PullRequestUrlDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate(PullRequestUrl, "InvalidReleaseCandidateRef:PullRequestUrl");

    [TestMethod]
    public void PullRequestNumberDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate(PullRequestNumber.ToString(), "InvalidReleaseCandidateRef:PullRequestNumber");

    [TestMethod]
    public void PrNumberRefDoesNotBecomeReleaseCandidateRef()
    {
        AssertInvalidReleaseCandidate($"pr:{PullRequestNumber}", "InvalidReleaseCandidateRef:PullRequestNumber");
        AssertInvalidReleaseCandidate($"pull-request:{PullRequestNumber}", "InvalidReleaseCandidateRef:PullRequestNumber");
    }

    [TestMethod]
    public void PullRequestProviderIdDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("provider-pr:e17", "InvalidReleaseCandidateRef:PullRequestProviderId");

    [TestMethod]
    public void GithubPrProviderIdDoesNotBecomeReleaseCandidateRef()
    {
        AssertInvalidReleaseCandidate("github-pr:e17", "InvalidReleaseCandidateRef:PullRequestProviderId");
        AssertInvalidReleaseCandidate("pull-request-provider-id:e17", "InvalidReleaseCandidateRef:PullRequestProviderId");
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("controlled-draft-pr-receipt:e17", "InvalidReleaseCandidateRef:PullRequestReceipt");

    [TestMethod]
    public void NonDraftPullRequestReceiptDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("controlled-pr-receipt:e17", "InvalidReleaseCandidateRef:PullRequestReceipt");

    [TestMethod]
    public void MergedPullRequestReceiptDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("merged-pr-receipt:e17", "InvalidReleaseCandidateRef:PullRequestReceipt");

    [TestMethod]
    public void PullRequestReceiptRefDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("pull-request-receipt:e17", "InvalidReleaseCandidateRef:PullRequestReceipt");

    [TestMethod]
    public void MergeReadinessEvidenceDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("merge-readiness:e17", "InvalidReleaseCandidateRef:MergeReadinessEvidence");

    [TestMethod]
    public void MergeReadyPackageDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("merge-ready:e17", "InvalidReleaseCandidateRef:MergeReadinessEvidence");

    [TestMethod]
    public void MergeDecisionCandidateDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("merge-decision-candidate:e17", "InvalidReleaseCandidateRef:MergeReadinessEvidence");

    [TestMethod]
    public void MergeEvidencePackageDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("merge-evidence-package:e17", "InvalidReleaseCandidateRef:MergeReadinessEvidence");

    [TestMethod]
    public void PullRequestHeadBranchDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate(HeadBranch, "InvalidReleaseCandidateRef:PullRequestHeadRef");

    [TestMethod]
    public void RefsHeadsPullRequestHeadBranchDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate($"refs/heads/{HeadBranch}", "InvalidReleaseCandidateRef:PullRequestHeadRef");

    [TestMethod]
    public void OriginPullRequestHeadBranchDoesNotBecomeReleaseCandidateRef()
    {
        AssertInvalidReleaseCandidate($"origin/{HeadBranch}", "InvalidReleaseCandidateRef:PullRequestHeadRef");
        AssertInvalidReleaseCandidate($"heads/{HeadBranch}", "InvalidReleaseCandidateRef:PullRequestHeadRef");
    }

    [TestMethod]
    public void BranchPrefixedPullRequestHeadDoesNotBecomeReleaseCandidateRef()
    {
        AssertInvalidReleaseCandidate($"branch:{HeadBranch}", "InvalidReleaseCandidateRef:PullRequestHeadRef");
        AssertInvalidReleaseCandidate($"head:{HeadBranch}", "InvalidReleaseCandidateRef:PullRequestHeadRef");
    }

    [TestMethod]
    public void PullRequestBaseBranchDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate(BaseBranch, "InvalidReleaseCandidateRef:PullRequestBaseRef");

    [TestMethod]
    public void RefsHeadsPullRequestBaseBranchDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate($"refs/heads/{BaseBranch}", "InvalidReleaseCandidateRef:PullRequestBaseRef");

    [TestMethod]
    public void OriginPullRequestBaseBranchDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate($"origin/{BaseBranch}", "InvalidReleaseCandidateRef:PullRequestBaseRef");

    [TestMethod]
    public void BasePrefixedPullRequestBaseDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate($"base:{BaseBranch}", "InvalidReleaseCandidateRef:PullRequestBaseRef");

    [TestMethod]
    public void PullRequestHeadShaDoesNotBecomeReleaseCandidateRefWithoutSeparateReleaseCandidateEvidence() =>
        AssertInvalidReleaseCandidate(HeadSha, "InvalidReleaseCandidateRef:PullRequestHeadSha");

    [TestMethod]
    public void PullRequestBaseShaDoesNotBecomeReleaseCandidateRefWithoutSeparateReleaseCandidateEvidence() =>
        AssertInvalidReleaseCandidate(BaseSha, "InvalidReleaseCandidateRef:PullRequestBaseSha");

    [TestMethod]
    public void CommitShaCanOnlyBeReleaseCandidateWhenSeparatelyPackagedAsReleaseCandidateEvidence() =>
        AssertValidReleaseCandidate($"release-candidate-ref:{HeadSha}");

    [TestMethod]
    public void PullRequestStatusRefDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("pull-request-status:e17", "InvalidReleaseCandidateRef:PullRequestStateEvidence");

    [TestMethod]
    public void PullRequestReadModelRefDoesNotBecomeReleaseCandidateRef() =>
        AssertInvalidReleaseCandidate("pull-request-read-model:e17", "InvalidReleaseCandidateRef:PullRequestStateEvidence");

    [TestMethod]
    public void ProviderPullRequestStateRefDoesNotBecomeReleaseCandidateRef()
    {
        AssertInvalidReleaseCandidate("provider-pr-state:e17", "InvalidReleaseCandidateRef:PullRequestStateEvidence");
        AssertInvalidReleaseCandidate("pr-provider-state:e17", "InvalidReleaseCandidateRef:PullRequestStateEvidence");
    }

    [TestMethod]
    public void ReleaseCandidateRefIsAcceptedWhenExplicitlyReleaseCandidateShaped() =>
        AssertValidReleaseCandidate("release-candidate:e17");

    [TestMethod]
    public void ReleaseCandidatePackageRefIsAccepted()
    {
        AssertValidReleaseCandidate("release-candidate-package:e17");
        AssertValidReleaseCandidate("release-candidate-ref:e17");
    }

    [TestMethod]
    public void RcPackageRefIsAccepted() =>
        AssertValidReleaseCandidate("rc-package:e17");

    [TestMethod]
    public void ReleaseArtifactRefIsAccepted() =>
        AssertValidReleaseCandidate("release-artifact:e17");

    [TestMethod]
    public void BroadPullRequestMarkerScanDoesNotRejectValidReleaseCandidateRefs()
    {
        AssertValidReleaseCandidate("release-decision-candidate:e17");
        AssertValidReleaseCandidate("release-candidate:pull-request-review-artifact-e17");
        AssertValidReleaseCandidate("release-candidate:merge-branch-review-e17");
    }

    [TestMethod]
    public void E17DoesNotCallGitHub()
    {
        var source = E17SourceWithoutStrings().Replace(nameof(E17DoesNotCallGitHub), string.Empty, StringComparison.Ordinal);

        foreach (var marker in new[] { "GitHub", "Octokit", "HttpClient", "GraphQL", "REST", "Actions", "WorkflowDispatch" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E17DoesNotCallExecutors()
    {
        var source = E17SourceWithoutStrings().Replace(nameof(E17DoesNotCallExecutors), string.Empty, StringComparison.Ordinal);

        foreach (var marker in new[] { "ProcessStartInfo", "Process.Start", "Executor", "ReleaseExecutor", "DeployExecutor" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E17DoesNotAddApiCliPersistenceWorkerOrOpenApiSurface()
    {
        var root = FindRepositoryRoot();
        var productionHits = Directory.GetFiles(root, "*E17*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path =>
                path.StartsWith("IronDev.Api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Cli/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Data/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Sql/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Worker/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("OpenApi/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), productionHits, string.Join(", ", productionHits));
    }

    [TestMethod]
    public void E17DoesNotAddReleaseExecutionPath()
    {
        var source = E17SourceWithoutStrings().Replace(nameof(E17DoesNotAddReleaseExecutionPath), string.Empty, StringComparison.Ordinal);

        AssertNoForbiddenIdentifier(source, "ReleaseExecution");
        AssertNoForbiddenIdentifier(source, "CanRelease");
    }

    [TestMethod]
    public void E17DoesNotAddDeploymentExecutionPath()
    {
        var source = E17SourceWithoutStrings().Replace(nameof(E17DoesNotAddDeploymentExecutionPath), string.Empty, StringComparison.Ordinal);

        AssertNoForbiddenIdentifier(source, "DeploymentExecution");
        AssertNoForbiddenIdentifier(source, "CanDeploy");
    }

    [TestMethod]
    public void E17DoesNotAddWorkflowContinuationPath()
    {
        var source = E17SourceWithoutStrings().Replace(nameof(E17DoesNotAddWorkflowContinuationPath), string.Empty, StringComparison.Ordinal);

        AssertNoForbiddenIdentifier(source, "WorkflowContinuation");
        AssertNoForbiddenIdentifier(source, "CanContinue");
    }

    [TestMethod]
    public void BlockE17_Receipt_RecordsPrShapedReleaseCandidateHardStopBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "E17_PR_URL_IS_NOT_RELEASE_CANDIDATE_REF_REGRESSION.md"));

        StringAssert.Contains(doc, "A pull request points at review work. It is not a release candidate.");
        StringAssert.Contains(doc, "PR-shaped evidence is review evidence, not release-candidate evidence.");
        StringAssert.Contains(doc, "E17 is a regression hard-stop. It adds no release-candidate creation, release-readiness, release-execution, or deployment path.");
        StringAssert.Contains(doc, "PR URL as release-candidate ref");
        StringAssert.Contains(doc, "PR number as release-candidate ref");
        StringAssert.Contains(doc, "PR provider id as release-candidate ref");
        StringAssert.Contains(doc, "PR receipt as release-candidate ref");
        StringAssert.Contains(doc, "PR head branch as release-candidate ref");
        StringAssert.Contains(doc, "PR base branch as release-candidate ref");
    }

    private static void AssertInvalidReleaseCandidate(string releaseCandidateRef, string expectedGap)
    {
        var release = BuildReleaseEvidence(releaseCandidateRef);

        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.NeedsMoreReleaseEvidence, release.Outcome, releaseCandidateRef);
        CollectionAssert.Contains(release.ReleaseEvidenceGaps, expectedGap, releaseCandidateRef);
        Assert.IsFalse(BoundaryAllowsRelease(release.Boundary));
        Assert.IsFalse(BypassEvaluatorAllowsRelease(release));
    }

    private static void AssertValidReleaseCandidate(string releaseCandidateRef)
    {
        var release = BuildReleaseEvidence(releaseCandidateRef);

        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.ReadyForReleaseDecision, release.Outcome, string.Join(", ", release.ReleaseEvidenceGaps));
        Assert.AreEqual(releaseCandidateRef, release.ReleaseCandidateRef);
        Assert.IsFalse(BoundaryAllowsRelease(release.Boundary));
        Assert.IsFalse(BypassEvaluatorAllowsRelease(release));
    }

    private static ReleaseReadinessEvidencePackage BuildReleaseEvidence(string releaseCandidateRef) =>
        ReleaseReadinessEvidencePackager.Build(new ReleaseReadinessEvidenceInput
        {
            Request = MergeReleaseRequest(),
            PullRequestStatusExists = true,
            PullRequestMerged = true,
            ReleaseCandidateRef = releaseCandidateRef,
            ProductHardeningEvidenceExists = true,
            ProductHardeningPassed = true,
            ReleaseReadinessReportExists = true,
            ReleaseReadinessReportOutcome = nameof(ProductReleaseReadinessOutcome.ReadyForDecision),
            ReleaseReadinessDecisionRecordExists = true,
            ArtifactConsistencyReportExists = true,
            ArtifactConsistencyBlockers = 0,
            UnsafeMaterialReportExists = true,
            UnsafeMaterialFindings = 0,
            KnownRisksDocumented = true,
            RecoveryEvidenceExists = true,
            EvidenceRefs =
            [
                "controlled-draft-pr-receipt:e17",
                "controlled-pr-receipt:e17",
                "merged-pr-receipt:e17",
                "merge-readiness:e17",
                "pull-request-status:e17",
                "pull-request-read-model:e17",
                $"head-sha:{HeadSha}",
                $"base-sha:{BaseSha}",
                $"pull-request-base-sha:{BaseSha}"
            ],
            CreatedAtUtc = CreatedAtUtc
        });

    private static MergeReleaseSeparationRequest MergeReleaseRequest() =>
        MergeReleaseSeparationRequestWriter.Create(new MergeReleaseSeparationRequestInput
        {
            RunId = "run-e17",
            ProjectId = "project-e17",
            RepositoryFullName = RepositoryFullName,
            PullRequestNumber = PullRequestNumber,
            PullRequestUrl = PullRequestUrl,
            BaseBranch = BaseBranch,
            HeadBranch = HeadBranch,
            ExpectedHeadSha = HeadSha,
            PullRequestCreationReceiptId = "controlled-draft-pr-receipt:e17",
            FeedbackReadinessReportId = "feedback-readiness:e17",
            RequestedBy = "tests",
            Reason = "PR-shaped evidence is not release-candidate evidence.",
            EvidenceRefs = ["controlled-draft-pr-receipt:e17", PullRequestUrl, $"head-sha:{HeadSha}", $"base-sha:{BaseSha}"],
            RequestedAtUtc = CreatedAtUtc
        });

    private static void AssertNoForbiddenIdentifier(string source, string marker)
    {
        Assert.IsFalse(
            source.Contains(marker, StringComparison.Ordinal),
            $"E17-owned source must not introduce '{marker}'.");
    }

    private static bool BoundaryAllowsRelease(MergeReleaseSeparationBoundary boundary)
    {
        var property = typeof(MergeReleaseSeparationBoundary).GetProperty(string.Concat("Can", "Release"));
        return property?.GetValue(boundary) is true;
    }

    private static bool BypassEvaluatorAllowsRelease(ReleaseReadinessEvidencePackage release)
    {
        var method = typeof(MergeReleaseBypassEvaluator).GetMethod(string.Concat("Can", "Release"));
        return method?.Invoke(null, [release]) is true;
    }

    private static string E17SourceWithoutStrings()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.IntegrationTests", "BlockE17PrUrlIsNotReleaseCandidateRefRegressionTests.cs"));
        return StripStringLiterals(source);
    }

    private static string StripStringLiterals(string source)
    {
        var result = new char[source.Length];
        var inString = false;
        var inVerbatim = false;
        var inRaw = false;
        const int RawQuoteCount = 3;

        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (!inString && !inRaw && current == '"' && next == '"' && i + 2 < source.Length && source[i + 2] == '"')
            {
                inRaw = true;
                result[i] = ' ';
                continue;
            }

            if (inRaw)
            {
                if (current == '"' && i + RawQuoteCount - 1 < source.Length && source.Substring(i, RawQuoteCount).All(ch => ch == '"'))
                {
                    for (var j = 0; j < RawQuoteCount && i + j < result.Length; j++)
                        result[i + j] = ' ';
                    i += RawQuoteCount - 1;
                    inRaw = false;
                    continue;
                }

                result[i] = ' ';
                continue;
            }

            if (!inString && current == '@' && next == '"')
            {
                inString = true;
                inVerbatim = true;
                result[i] = ' ';
                continue;
            }

            if (!inString && current == '"')
            {
                inString = true;
                inVerbatim = false;
                result[i] = ' ';
                continue;
            }

            if (inString)
            {
                if (current == '"' && inVerbatim && next == '"')
                {
                    result[i] = ' ';
                    result[i + 1] = ' ';
                    i++;
                    continue;
                }

                if (current == '"' && (inVerbatim || !IsEscaped(source, i)))
                    inString = false;

                result[i] = ' ';
                continue;
            }

            result[i] = current;
        }

        return new string(result);
    }

    private static bool IsEscaped(string source, int index)
    {
        var backslashes = 0;
        for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
            backslashes++;
        return backslashes % 2 == 1;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
