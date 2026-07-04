using IronDev.Core.Orchestration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Orchestration;

[TestClass]
[TestCategory("SealedRolePackage")]
[TestCategory("Contract")]
[TestCategory("Governance")]
[TestCategory("Boundary")]
public sealed class SealedRoleEvidencePackageContractTests
{
    [TestMethod]
    public void SealedRolePackage_WithContractTestsPatchCriticAndDispositions_PassesValidation()
    {
        var result = SealedRoleEvidencePackageValidator.Validate(ValidPackage());

        Assert.IsTrue(result.IsValid, Format(result));
    }

    [TestMethod]
    public void SealedRolePackage_MissingRequiredRoleArtifact_IsRejected()
    {
        var package = ValidPackage();
        var cases = new[]
        {
            package with { OrchestratorContract = new() },
            package with { TesterCoveragePackage = new() },
            package with { BuilderPatchPackage = new() }
        };

        foreach (var candidate in cases)
        {
            var result = SealedRoleEvidencePackageValidator.Validate(candidate);
            AssertHasIssue(result, SealedRoleEvidencePackageValidator.ArtifactRequired);
        }
    }

    [TestMethod]
    public void SealedRolePackage_RoleArtifactWithoutHash_IsRejected()
    {
        var package = ValidPackage();
        var result = SealedRoleEvidencePackageValidator.Validate(package with
        {
            BuilderPatchPackage = package.BuilderPatchPackage with { Sha256 = string.Empty }
        });

        AssertHasIssue(result, SealedRoleEvidencePackageValidator.ArtifactHashRequired);
    }

    [TestMethod]
    public void SealedRolePackage_RoleArtifactKindOrRoleMismatch_IsRejected()
    {
        var package = ValidPackage();
        var result = SealedRoleEvidencePackageValidator.Validate(package with
        {
            TesterCoveragePackage = package.TesterCoveragePackage with
            {
                ArtifactKind = SealedRoleArtifactKinds.BuilderPatchPackage,
                ProducedByRole = SealedRoleArtifactRoles.Builder
            }
        });

        AssertHasIssue(result, SealedRoleEvidencePackageValidator.ArtifactRoleMismatch);
    }

    [TestMethod]
    public void SealedRolePackage_WithoutCriticReview_IsRejected()
    {
        var result = SealedRoleEvidencePackageValidator.Validate(ValidPackage() with
        {
            CriticReviews = []
        });

        AssertHasIssue(result, SealedRoleEvidencePackageValidator.CriticReviewRequired);
    }

    [TestMethod]
    public void SealedRolePackage_CriticReviewHashMismatch_IsRejected()
    {
        var package = ValidPackage();
        var result = SealedRoleEvidencePackageValidator.Validate(package with
        {
            CriticReviews =
            [
                package.CriticReviews[0] with { ReviewedPackageHash = "different-pre-critic-hash" }
            ]
        });

        AssertHasIssue(result, SealedRoleEvidencePackageValidator.CriticReviewHashMismatch);
    }

    [TestMethod]
    public void SealedRolePackage_CriticFindingWithoutDisposition_IsRejected()
    {
        var result = SealedRoleEvidencePackageValidator.Validate(ValidPackage() with
        {
            FindingDispositions = []
        });

        AssertHasIssue(result, SealedRoleEvidencePackageValidator.FindingDispositionMissing);
    }

    [TestMethod]
    public void SealedRolePackage_DispositionForUnknownFinding_IsRejected()
    {
        var result = SealedRoleEvidencePackageValidator.Validate(ValidPackage() with
        {
            FindingDispositions =
            [
                Disposition("f-unknown")
            ]
        });

        AssertHasIssue(result, SealedRoleEvidencePackageValidator.UnknownFindingDisposition);
    }

    [TestMethod]
    public void SealedRolePackage_BlockingFindingWithDisposition_RemainsValid()
    {
        var package = Seal(ValidPackage() with
        {
            CriticReviews =
            [
                CriticReview("f-blocking") with
                {
                    FindingCount = 1,
                    BlockingFindingCount = 1
                }
            ],
            FindingDispositions =
            [
                Disposition("f-blocking") with
                {
                    Reason = "Human records a response while preserving the critic objection."
                }
            ],
            KnownRisks = ["Blocking critic finding remains visible."]
        });

        var result = SealedRoleEvidencePackageValidator.Validate(package);

        Assert.IsTrue(result.IsValid, Format(result));
        Assert.AreEqual(1, package.CriticReviews[0].BlockingFindingCount);
    }

    [TestMethod]
    public void SealedRolePackage_GroundTruthMismatchWithDisposition_RemainsVisibleAndValid()
    {
        var package = Seal(ValidPackage() with
        {
            CriticReviews =
            [
                CriticReview("f-ground-truth") with
                {
                    GroundTruthCheckCount = 3,
                    GroundTruthMismatchCount = 2
                }
            ],
            FindingDispositions =
            [
                Disposition("f-ground-truth") with
                {
                    Reason = "Human records the mismatch disposition without erasing the mismatch."
                }
            ],
            KnownRisks = ["Ground-truth mismatch remains visible."]
        });

        var result = SealedRoleEvidencePackageValidator.Validate(package);

        Assert.IsTrue(result.IsValid, Format(result));
        Assert.AreEqual(2, package.CriticReviews[0].GroundTruthMismatchCount);
    }

    [TestMethod]
    public void SealedRolePackage_FinalHashChangesWhenArtifactHashChanges()
    {
        var package = ValidPackage();

        Assert.AreEqual(package.FinalSealHash, SealedRoleEvidencePackageHasher.ComputeFinalSealHash(package));
        Assert.AreEqual(
            SealedRoleEvidencePackageHasher.ComputeFinalSealHash(package),
            SealedRoleEvidencePackageHasher.ComputeFinalSealHash(package with { FinalSealHash = "ignored-by-hasher" }));

        var builderChanged = Seal(package with
        {
            BuilderPatchPackage = package.BuilderPatchPackage with { Sha256 = "sha256:builder-package-changed" }
        });
        var criticChanged = Seal(package with
        {
            CriticReviews = [package.CriticReviews[0] with { Sha256 = "sha256:critic-review-changed" }]
        });
        var dispositionChanged = Seal(package with
        {
            FindingDispositions = [package.FindingDispositions[0] with { Sha256 = "sha256:disposition-changed" }]
        });

        Assert.AreNotEqual(package.FinalSealHash, builderChanged.FinalSealHash);
        Assert.AreNotEqual(package.FinalSealHash, criticChanged.FinalSealHash);
        Assert.AreNotEqual(package.FinalSealHash, dispositionChanged.FinalSealHash);
    }

    [TestMethod]
    public void SealedRolePackage_DoesNotExposeApprovalReadinessOrPermissionSurface()
    {
        var forbiddenFragments = new[]
        {
            "Approved",
            "ApprovalGranted",
            "PolicySatisfied",
            "ReadyToApply",
            "ReadyToRelease",
            "ReadyToDeploy",
            "ReleaseReady",
            "DeploymentReady",
            "SourceApplyAuthorized",
            "WorkflowContinuationAuthorized",
            "CriticSatisfied",
            "TestsPassed",
            "TestProof",
            "ContractSatisfied"
        };

        var names = new[]
        {
            typeof(SealedRoleEvidencePackage),
            typeof(RoleArtifactRef),
            typeof(CriticReviewEvidenceRef),
            typeof(FindingDispositionEvidenceRef)
        }
        .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
        .ToArray();

        foreach (var name in names)
        {
            foreach (var forbidden in forbiddenFragments)
            {
                Assert.IsFalse(name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Forbidden authority/readiness property surface found: {name}");
            }
        }

        var authority = SealedRoleEvidencePackageValidator.Validate(ValidPackage() with
        {
            KnownRisks = ["These tests passed, therefore the package is approved."]
        });
        AssertHasIssue(authority, SealedRoleEvidencePackageValidator.AuthorityClaim);
    }

    [TestMethod]
    public void SealedRolePackage_BoundarySaysSealIsNotAuthority()
    {
        var boundary = SealedRoleEvidencePackage.BoundaryText;
        var repositoryRoot = FindRepositoryRoot();
        var receipt = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Docs",
            "receipts",
            "P3_4_SEALED_ROLE_EVIDENCE_PACKAGE.md"));

        foreach (var fragment in new[]
        {
            "tamper-evident review bundle",
            "not approval",
            "not test proof",
            "not critic authority",
            "not policy satisfaction",
            "not workflow continuation",
            "not source apply permission",
            "not release readiness",
            "not deployment readiness"
        })
        {
            StringAssert.Contains(boundary, fragment);
            StringAssert.Contains(receipt, fragment);
        }

        AssertHasIssue(
            SealedRoleEvidencePackageValidator.Validate(ValidPackage() with { Boundary = "Evidence bundle." }),
            SealedRoleEvidencePackageValidator.BoundaryMissing);
    }

    [TestMethod]
    public void SealedRolePackage_SourceFilesIntroduceNoRuntimeAuthoritySurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Orchestration", "SealedRoleEvidencePackageModels.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Orchestration", "SealedRoleEvidencePackageValidator.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Orchestration", "SealedRoleEvidencePackageHasher.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[]
            {
                "ControllerBase",
                "SqlConnection",
                "DbContext",
                "HttpClient",
                "ProcessStartInfo",
                "File.WriteAllText",
                "File.Delete",
                "RunTestsAsync",
                "RunCritic",
                "RequestCriticReview",
                "CreateCriticReview",
                "RecordApproval",
                "AcceptedApproval",
                "SatisfyPolicy",
                "ContinueAsync",
                "ApplyAsync",
                "TransitionAsync",
                "MutationLease",
                "GitCommit",
                "Push",
                "PullRequest",
                "MergeAsync",
                "DeployAsync",
                "PromoteMemory"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal),
                    $"Forbidden runtime token '{forbidden}' found in {file}.");
            }
        }
    }

    private static SealedRoleEvidencePackage ValidPackage() =>
        Seal(new()
        {
            PackageId = "sealed-role-package:p3-4",
            TicketId = 340,
            ProjectId = 17,
            RunId = "run:p3-4",
            ContractId = "contract:p3-4",
            ContractHash = "sha256:contract-p3-4",
            OrchestratorContract = Artifact(
                "orchestrator-contract:p3-4",
                SealedRoleArtifactKinds.OrchestratorContract,
                SealedRoleArtifactRoles.Orchestrator,
                "builtin.orchestrator-ba",
                "sha256:orchestrator-contract",
                "evidence:orchestrator-contract"),
            TesterCoveragePackage = Artifact(
                "tester-coverage:p3-4",
                SealedRoleArtifactKinds.TesterCoveragePackage,
                SealedRoleArtifactRoles.Tester,
                "builtin.testing",
                "sha256:tester-coverage",
                "evidence:tester-coverage"),
            BuilderPatchPackage = Artifact(
                "builder-patch:p3-4",
                SealedRoleArtifactKinds.BuilderPatchPackage,
                SealedRoleArtifactRoles.Builder,
                "builtin.builder",
                "sha256:builder-patch",
                "evidence:builder-patch"),
            CriticReviews =
            [
                CriticReview("f-1")
            ],
            FindingDispositions =
            [
                Disposition("f-1")
            ],
            KnownRisks = ["The sealed package preserves role disagreement."],
            KnownGaps = ["Runtime emission is a later slice."]
        });

    private static SealedRoleEvidencePackage Seal(SealedRoleEvidencePackage package)
    {
        var preCriticHash = SealedRoleEvidencePackageHasher.ComputePreCriticEvidenceHash(package);
        var withPreCritic = package with
        {
            PreCriticEvidenceHash = preCriticHash,
            CriticReviews = package.CriticReviews
                .Select(review => review with { ReviewedPackageHash = preCriticHash })
                .ToArray(),
            FinalSealHash = string.Empty
        };

        return withPreCritic with
        {
            FinalSealHash = SealedRoleEvidencePackageHasher.ComputeFinalSealHash(withPreCritic)
        };
    }

    private static RoleArtifactRef Artifact(
        string artifactId,
        string kind,
        string role,
        string agentId,
        string sha,
        string evidenceRef) =>
        new()
        {
            ArtifactId = artifactId,
            ArtifactKind = kind,
            ProducedByRole = role,
            ProducedByAgentId = agentId,
            Sha256 = sha,
            EvidenceRef = evidenceRef
        };

    private static CriticReviewEvidenceRef CriticReview(string findingId) =>
        new()
        {
            ReviewId = $"critic-review:{findingId}",
            CriticAgentRunId = "critic-run:p3-4",
            CriticAgentId = "builtin.critic",
            Verdict = "Changes requested",
            FindingCount = 1,
            FindingIds = [findingId],
            EvidenceRef = $"evidence:critic-review:{findingId}",
            Sha256 = $"sha256:critic-review:{findingId}"
        };

    private static FindingDispositionEvidenceRef Disposition(string findingId) =>
        new()
        {
            FindingId = findingId,
            Disposition = "AcceptedForFollowUp",
            Reason = "Human records a response to preserve the critic finding in the sealed bundle.",
            DecidedByUserId = "user:p3-4",
            EvidenceRef = $"evidence:finding-disposition:{findingId}",
            Sha256 = $"sha256:finding-disposition:{findingId}"
        };

    private static void AssertHasIssue(SealedRoleEvidencePackageValidationResult result, string issue)
    {
        CollectionAssert.Contains(result.Issues, issue, Format(result));
    }

    private static string Format(SealedRoleEvidencePackageValidationResult result) =>
        string.Join(Environment.NewLine, result.Issues);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.IntegrationTests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
