using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBDDeploymentReadinessDecisionPackageTests
{
    private static readonly string CandidateCommitSha = new('e', 40);
    private static readonly string ArtifactSha256 = new('a', 64);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockBD_Package_CreatesDeploymentReadinessDecisionPackage()
    {
        var artifacts = Build();

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor, artifacts.Package.PackageVerdict);
        Assert.IsTrue(artifacts.Package.CanProceedToControlledDeploymentExecutor);
        Assert.AreEqual("deployment_readiness_sep_bd", artifacts.Package.SourceDeploymentReadinessSeparationPackageId);
        Assert.AreEqual("owner/repo", artifacts.Package.Repository);
        Assert.AreEqual(CandidateCommitSha, artifacts.Package.CandidateCommitSha);
        Assert.AreEqual("1.2.3", artifacts.Package.CandidateVersion);
        Assert.AreEqual("v1.2.3", artifacts.Package.CandidateTagName);
        Assert.AreEqual("Stable", artifacts.Package.ReleaseChannel);
        Assert.AreEqual("production", artifacts.Package.DeploymentTarget);
        Assert.AreEqual("prod-west", artifacts.Package.DeploymentEnvironment);
        Assert.AreEqual("artifact.zip", artifacts.Package.DeploymentArtifactName);
        Assert.AreEqual(ArtifactSha256, artifacts.Package.DeploymentArtifactSha256);
        Assert.AreEqual(DeploymentReadinessDecision.ApprovedForControlledDeploymentExecutor, artifacts.Package.Decision);
        Assert.IsTrue(artifacts.Receipt.BoundaryStatements.Any(statement => statement.Contains("Deployment readiness decision package is not deployment execution", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBD_Package_RequiresBCSeparationPackage()
    {
        var artifacts = Build(CreateInput() with { DeploymentReadinessSeparationPackage = null });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        Assert.IsFalse(artifacts.Package.CanProceedToControlledDeploymentExecutor);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentReadinessSeparationPackage);
    }

    [TestMethod]
    public void BlockBD_Package_BlocksIncompleteOrBlockedBCPackage()
    {
        var cases = new (string Name, DeploymentReadinessSeparationPackage Package, DeploymentReadinessDecisionPackageBlockReason Reason)[]
        {
            ("incomplete", CreateSeparationPackage() with { PackageVerdict = DeploymentReadinessSeparationVerdict.PackageIncomplete, CanProceedToDeploymentReadinessDecision = false }, DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationPackageNotReady),
            ("blocked", CreateSeparationPackage() with { PackageVerdict = DeploymentReadinessSeparationVerdict.PackageBlocked, CanProceedToDeploymentReadinessDecision = false }, DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationPackageBlocked),
            ("rejected", CreateSeparationPackage() with { PackageVerdict = DeploymentReadinessSeparationVerdict.PackageRejected, CanProceedToDeploymentReadinessDecision = false }, DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationPackageRejected)
        };

        foreach (var item in cases)
        {
            var artifacts = Build(CreateInput(item.Package));

            Assert.AreNotEqual(DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, item.Reason);
        }
    }

    [TestMethod]
    public void BlockBD_Package_BlocksBCBoundaryWithForbiddenAuthority()
    {
        var cases = new (string Name, DeploymentReadinessSeparationBoundary Boundary)[]
        {
            ("decide", DeploymentReadinessSeparationBoundary.Evidence with { CanDecideDeploymentReadiness = true }),
            ("deploy", DeploymentReadinessSeparationBoundary.Evidence with { CanDeploy = true }),
            ("publish", DeploymentReadinessSeparationBoundary.Evidence with { CanPublishPackages = true }),
            ("continue", DeploymentReadinessSeparationBoundary.Evidence with { CanContinueWorkflow = true }),
            ("environment", DeploymentReadinessSeparationBoundary.Evidence with { CanMutateEnvironment = true }),
            ("rollback", DeploymentReadinessSeparationBoundary.Evidence with { CanExecuteRollback = true })
        };

        foreach (var item in cases)
        {
            var package = CreateSeparationPackage() with { Boundary = item.Boundary };
            var artifacts = Build(CreateInput(package));

            Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageBlocked, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationBoundaryAuthorityViolation);
        }
    }

    [TestMethod]
    public void BlockBD_Package_RequiresExplicitDeploymentReadinessDecision()
    {
        var artifacts = Build(CreateInput() with { DeploymentReadinessDecision = null });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentReadinessDecision);
    }

    [TestMethod]
    public void BlockBD_Package_BlocksRejectedDecision()
    {
        var artifacts = Build(CreateInput() with { DeploymentReadinessDecision = CreateDecision() with { Decision = DeploymentReadinessDecision.Rejected } });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageRejected, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionRejected);
    }

    [TestMethod]
    public void BlockBD_Package_BlocksNeedsMoreEvidenceDecision()
    {
        var artifacts = Build(CreateInput() with { DeploymentReadinessDecision = CreateDecision() with { Decision = DeploymentReadinessDecision.NeedsMoreEvidence } });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionNeedsMoreEvidence);
    }

    [TestMethod]
    public void BlockBD_Package_BlocksDecisionMadeBeforeBCSeparationEvidence()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentReadinessDecision = CreateDecision() with { DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T12:00:00Z") }
        });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionStale);
    }

    [TestMethod]
    public void BlockBD_Package_BlocksSelfApprovalFromBCPackageCreator()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentReadinessDecision = CreateDecision() with { DecisionMadeBy = "release-captain" }
        });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.DecisionMakerNotAllowed);
    }

    [TestMethod]
    public void BlockBD_Package_BlocksIdentityMismatch()
    {
        var cases = new (string Name, DeploymentReadinessDecisionPackageInput Input, DeploymentReadinessDecisionPackageBlockReason Reason)[]
        {
            ("repo", CreateInput() with { Repository = "other/repo", DeploymentReadinessDecision = CreateDecision() with { ExpectedRepository = "other/repo" } }, DeploymentReadinessDecisionPackageBlockReason.RepositoryMismatch),
            ("commit", CreateInput() with { CandidateCommitSha = new string('f', 40), DeploymentReadinessDecision = CreateDecision() with { ExpectedCandidateCommitSha = new string('f', 40) } }, DeploymentReadinessDecisionPackageBlockReason.CandidateCommitMismatch),
            ("version", CreateInput() with { CandidateVersion = "1.2.4", DeploymentReadinessDecision = CreateDecision() with { ExpectedVersion = "1.2.4" } }, DeploymentReadinessDecisionPackageBlockReason.CandidateVersionMismatch),
            ("tag", CreateInput() with { CandidateTagName = "v1.2.4", DeploymentReadinessDecision = CreateDecision() with { ExpectedTagName = "v1.2.4" } }, DeploymentReadinessDecisionPackageBlockReason.CandidateTagMismatch),
            ("channel", CreateInput() with { ReleaseChannel = "Preview", DeploymentReadinessDecision = CreateDecision() with { ExpectedReleaseChannel = "Preview" } }, DeploymentReadinessDecisionPackageBlockReason.ReleaseChannelMismatch),
            ("target", CreateInput() with { DeploymentTarget = "staging", DeploymentReadinessDecision = CreateDecision() with { ExpectedDeploymentTarget = "staging" } }, DeploymentReadinessDecisionPackageBlockReason.DeploymentTargetMismatch)
        };

        foreach (var item in cases)
        {
            var artifacts = Build(item.Input);

            Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageBlocked, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, item.Reason);
        }
    }

    [TestMethod]
    public void BlockBD_Package_RequiresDeploymentEnvironment()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentEnvironment = " ",
            DeploymentReadinessDecision = CreateDecision() with { ExpectedDeploymentEnvironment = " " }
        });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentEnvironment);
    }

    [TestMethod]
    public void BlockBD_Package_RequiresDeploymentArtifactName()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentArtifactName = " ",
            DeploymentReadinessDecision = CreateDecision() with { ExpectedDeploymentArtifactName = " " }
        });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentArtifactName);
    }

    [TestMethod]
    public void BlockBD_Package_RequiresDeploymentArtifactChecksum()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentArtifactSha256 = " ",
            DeploymentReadinessDecision = CreateDecision() with { ExpectedDeploymentArtifactSha256 = " " }
        });

        Assert.AreEqual(DeploymentReadinessDecisionPackageVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentArtifactChecksum);
    }

    [TestMethod]
    public void BlockBD_Boundary_RemainsEvidenceOnly()
    {
        var boundary = DeploymentReadinessDecisionPackageBoundary.Evidence;

        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPublishPackages);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanMutateEnvironment);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanExecuteRollback);
        Assert.IsFalse(boundary.CanDispatchPipeline);
    }

    [TestMethod]
    public void BlockBD_DoesNotDeployPublishPromoteContinueMutateOrRollback()
    {
        var package = Build().Package;

        Assert.IsTrue(package.CanProceedToControlledDeploymentExecutor);
        Assert.IsFalse(package.Boundary.CanDeploy);
        Assert.IsFalse(package.Boundary.CanPublishPackages);
        Assert.IsFalse(package.Boundary.CanPromoteMemory);
        Assert.IsFalse(package.Boundary.CanContinueWorkflow);
        Assert.IsFalse(package.Boundary.CanMutateEnvironment);
        Assert.IsFalse(package.Boundary.CanMutateSource);
        Assert.IsFalse(package.Boundary.CanExecuteRollback);
        Assert.IsFalse(DeploymentReadinessDecisionPackageBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(DeploymentReadinessDecisionPackageBypassEvaluator.CanPublishPackages(package));
        Assert.IsFalse(DeploymentReadinessDecisionPackageBypassEvaluator.CanPromoteMemory(package));
        Assert.IsFalse(DeploymentReadinessDecisionPackageBypassEvaluator.CanContinueWorkflow(package));
        Assert.IsFalse(DeploymentReadinessDecisionPackageBypassEvaluator.CanMutateEnvironment(package));
        Assert.IsFalse(DeploymentReadinessDecisionPackageBypassEvaluator.CanMutateSource(package));
        Assert.IsFalse(DeploymentReadinessDecisionPackageBypassEvaluator.CanExecuteRollback(package));
    }

    [TestMethod]
    public async Task BlockBD_Cli_ReturnsZeroOnlyForReadyDecisionPackage()
    {
        var separationPath = WriteSeparationPackage(CreateSeparationPackage());
        var readyOut = Path.Combine(Path.GetTempPath(), $"bd-ready-{Guid.NewGuid():N}");

        var ready = await RunCliAsync(PackageArgs(separationPath, readyOut)).ConfigureAwait(false);

        Assert.AreEqual(0, ready.ExitCode, ready.Error);
        Assert.IsTrue(File.Exists(Path.Combine(readyOut, "deployment-readiness-decision-package.json")));
        var events = File.ReadAllText(Path.Combine(readyOut, FileBackedGovernanceEventStore.ArtifactName));
        StringAssert.Contains(events, "DeploymentReadinessDecisionPackageCreated");

        var blockedPackage = CreateSeparationPackage() with { PackageVerdict = DeploymentReadinessSeparationVerdict.PackageBlocked, CanProceedToDeploymentReadinessDecision = false };
        var blockedPath = WriteSeparationPackage(blockedPackage);
        var blockedOut = Path.Combine(Path.GetTempPath(), $"bd-blocked-{Guid.NewGuid():N}");

        var blocked = await RunCliAsync(PackageArgs(blockedPath, blockedOut)).ConfigureAwait(false);

        Assert.AreEqual(1, blocked.ExitCode, blocked.Output + blocked.Error);

        var invalid = await RunCliAsync("deployment-readiness-decision", "package", "--deployment-readiness-separation-package", separationPath).ConfigureAwait(false);
        Assert.AreEqual(2, invalid.ExitCode, invalid.Output + invalid.Error);
    }

    [TestMethod]
    public async Task BlockBD_Cli_RejectsDeployExecutePublishPromoteContinueCommitPushRollbackVerbs()
    {
        foreach (var forbidden in new[]
        {
            "deploy",
            "execute",
            "publish",
            "publish-package",
            "promote",
            "promote-memory",
            "continue",
            "continue-workflow",
            "dispatch",
            "trigger-pipeline",
            "commit",
            "push",
            "merge",
            "source-apply",
            "rollback",
            "rollback-execute"
        })
        {
            var result = await RunCliAsync("deployment-readiness-decision", forbidden, "--package", "deployment-readiness-decision-package.json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBD_StaticBoundary_NoDeploymentExecutorSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliDeploymentReadinessDecision.cs"));
        var model = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "DeploymentReadinessDecisionPackage.cs"));
        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BD_DEPLOYMENT_READINESS_DECISION_PACKAGE.md"));

        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("ProcessStartInfo", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh api", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("az webapp", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(model.Contains("DeploymentExecutionRequest", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(model.Contains("IDeploymentExecutionGateway", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(model, "CanProceedToControlledDeploymentExecutor");
        StringAssert.Contains(model, "CanDeploy(object? evidence) => false");
        StringAssert.Contains(receipt, "BC separation package is not deployment readiness decision.");
        StringAssert.Contains(receipt, "Deployment readiness decision package is not deployment execution.");
        StringAssert.Contains(receipt, "BD does not deploy.");
        StringAssert.Contains(receipt, "BD does not mutate environments.");
        StringAssert.Contains(receipt, "BD does not execute rollback.");
    }

    private static DeploymentReadinessDecisionPackageArtifacts Build(DeploymentReadinessDecisionPackageInput? input = null) =>
        DeploymentReadinessDecisionPackageBuilder.Build(input ?? CreateInput());

    private static DeploymentReadinessDecisionPackageInput CreateInput(DeploymentReadinessSeparationPackage? separationPackage = null) => new()
    {
        DeploymentReadinessSeparationPackage = separationPackage ?? CreateSeparationPackage(),
        DeploymentReadinessDecision = CreateDecision(separationPackage),
        Repository = "owner/repo",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        DeploymentTarget = "production",
        DeploymentEnvironment = "prod-west",
        DeploymentArtifactName = "artifact.zip",
        DeploymentArtifactSha256 = ArtifactSha256,
        CreatedBy = "deployment-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T12:20:00Z")
    };

    private static DeploymentReadinessSeparationPackage CreateSeparationPackage() => new()
    {
        DeploymentReadinessSeparationPackageId = "deployment_readiness_sep_bd",
        SourceReleaseExecutionReceiptId = "release_exec_bd",
        Repository = "owner/repo",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        DeploymentTarget = "production",
        DeploymentReadinessScope = "release-v1.2.3",
        PackageVerdict = DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision,
        CanProceedToDeploymentReadinessDecision = true,
        BlockReasons = [],
        PackageIssues = [],
        CreatedBy = "release-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T12:10:00Z"),
        Boundary = DeploymentReadinessSeparationBoundary.Evidence
    };

    private static DeploymentReadinessDecisionEvidence CreateDecision(DeploymentReadinessSeparationPackage? separationPackage = null)
    {
        var source = separationPackage ?? CreateSeparationPackage();
        return new DeploymentReadinessDecisionEvidence
        {
            DeploymentReadinessDecisionId = "deployment_readiness_decision_bd",
            Decision = DeploymentReadinessDecision.ApprovedForControlledDeploymentExecutor,
            DecisionMadeBy = "deployment-captain",
            DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T12:15:00Z"),
            DecisionRationale = "BC separation evidence is eligible and the deployment target/artifact identity is explicitly bound.",
            ExpectedDeploymentReadinessSeparationPackageId = source.DeploymentReadinessSeparationPackageId,
            ExpectedRepository = "owner/repo",
            ExpectedCandidateCommitSha = CandidateCommitSha,
            ExpectedVersion = "1.2.3",
            ExpectedTagName = "v1.2.3",
            ExpectedReleaseChannel = "Stable",
            ExpectedDeploymentTarget = "production",
            ExpectedDeploymentEnvironment = "prod-west",
            ExpectedDeploymentArtifactName = "artifact.zip",
            ExpectedDeploymentArtifactSha256 = ArtifactSha256
        };
    }

    private static void AssertContainsReason(
        DeploymentReadinessDecisionPackage package,
        DeploymentReadinessDecisionPackageBlockReason reason) =>
        Assert.IsTrue(package.BlockReasons.Contains(reason), $"Expected {reason}; actual: {string.Join(", ", package.BlockReasons)}");

    private static string WriteSeparationPackage(DeploymentReadinessSeparationPackage package)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bd-separation-package-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(package with { CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) }, JsonOptions));
        return path;
    }

    private static string[] PackageArgs(string separationPackagePath, string outDir) =>
    [
        "deployment-readiness-decision",
        "package",
        "--deployment-readiness-separation-package",
        separationPackagePath,
        "--repo",
        "owner/repo",
        "--candidate-commit",
        CandidateCommitSha,
        "--version",
        "1.2.3",
        "--tag",
        "v1.2.3",
        "--channel",
        "Stable",
        "--deployment-target",
        "production",
        "--deployment-environment",
        "prod-west",
        "--artifact-name",
        "artifact.zip",
        "--artifact-sha256",
        ArtifactSha256,
        "--decision",
        "approved-for-controlled-deployment-executor",
        "--decision-by",
        "deployment-captain",
        "--decision-rationale",
        "BC separation evidence is eligible and the deployment target/artifact identity is explicitly bound.",
        "--created-by",
        "deployment-captain",
        "--out",
        outDir,
        "--json"
    ];

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
