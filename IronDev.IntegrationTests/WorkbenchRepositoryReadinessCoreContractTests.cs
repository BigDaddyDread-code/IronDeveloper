using IronDev.Core.Workbench;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("WorkbenchReadiness")]
public sealed class WorkbenchRepositoryReadinessCoreContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Ready_RequiresExactlyTheNineNormativeTechnicalGates()
    {
        var result = RepositoryReadinessEvaluator.Evaluate(ReadyContext());

        Assert.AreEqual(ProjectExecutionReadinessStates.Ready, result.ExecutionReadiness);
        Assert.IsTrue(result.IsReady);
        Assert.AreEqual(9, result.Gates.Count);
        CollectionAssert.AreEqual(
            Enum.GetValues<RepositoryReadinessGateName>(),
            result.Gates.Select(static gate => gate.Gate).ToArray());
        Assert.IsTrue(result.Gates.All(static gate => gate.Passed));
        Assert.IsFalse(result.Gates.Any(static gate =>
            gate.Gate.ToString().Contains("Authoriz", StringComparison.OrdinalIgnoreCase)));
    }

    [DataTestMethod]
    [DataRow("ProjectId")]
    [DataRow("RepositoryBindingId")]
    [DataRow("RepositoryBindingRevision")]
    [DataRow("BaselineCommit")]
    [DataRow("RepositoryStateObservationId")]
    [DataRow("WorktreeFingerprint")]
    [DataRow("ProjectExecutionProfileId")]
    [DataRow("ProjectExecutionProfileRevision")]
    [DataRow("ProfileDefinitionId")]
    [DataRow("ProfileDescriptorRevision")]
    [DataRow("ProfileDescriptorSha256")]
    [DataRow("RestoreCommandSha256")]
    [DataRow("BuildCommandSha256")]
    [DataRow("TestCommandSha256")]
    [DataRow("SdkToolchainManifestId")]
    [DataRow("ContainerImageDigest")]
    [DataRow("SandboxPolicyVersion")]
    [DataRow("SandboxPolicySha256")]
    [DataRow("OfflineFeedManifestSha256")]
    [DataRow("TemplateBundleSha256")]
    [DataRow("CodeIndexSnapshotId")]
    [DataRow("CodeIndexSnapshotRevision")]
    public void AnyUniversalAuthorityBindingMismatch_InvalidatesCurrentness(string field)
    {
        var context = ReadyContext();
        Assert.AreEqual(
            ProjectExecutionReadinessStates.Ready,
            RepositoryReadinessEvaluator.Evaluate(context).ExecutionReadiness);

        if (field is "CodeIndexSnapshotId" or "CodeIndexSnapshotRevision")
        {
            context = context with
            {
                CodeIndex = context.CodeIndex! with
                {
                    Binding = Mutate(context.CodeIndex.Binding, field)
                }
            };
        }
        else
        {
            context = context with
            {
                BuildValidation = context.BuildValidation! with
                {
                    Binding = Mutate(context.BuildValidation.Binding, field)
                }
            };
        }

        var result = RepositoryReadinessEvaluator.Evaluate(context);

        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired, result.ExecutionReadiness);
        Assert.IsFalse(result.IsReady);
        Assert.IsTrue(result.Gates.Any(static gate => !gate.Passed));
    }

    [TestMethod]
    public void NewTimestampAlone_CannotMakeMismatchedEvidenceCurrent()
    {
        var context = ReadyContext();
        var stale = context.BuildValidation! with
        {
            Binding = context.BuildValidation.Binding with
            {
                BaselineCommit = Git('f')
            },
            ValidatedAtUtc = Now.AddYears(20)
        };

        var result = RepositoryReadinessEvaluator.Evaluate(context with { BuildValidation = stale });

        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired, result.ExecutionReadiness);
        Assert.IsFalse(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.RestorePassed).Passed);
        Assert.IsFalse(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.BuildPassed).Passed);
    }

    [TestMethod]
    public void ProviderAvailability_IsReturnedButCannotMutateDurableReadiness()
    {
        var context = ReadyContext();
        var available = RepositoryReadinessEvaluator.Evaluate(context with
        {
            Availability = Availability(ExecutionAvailabilityStates.Available, "ProviderAvailable")
        });
        var unavailable = RepositoryReadinessEvaluator.Evaluate(context with
        {
            Availability = Availability(ExecutionAvailabilityStates.Unavailable, "ProviderOffline")
        });

        Assert.AreEqual(ProjectExecutionReadinessStates.Ready, available.ExecutionReadiness);
        Assert.AreEqual(ProjectExecutionReadinessStates.Ready, unavailable.ExecutionReadiness);
        CollectionAssert.AreEqual(available.Gates.ToArray(), unavailable.Gates.ToArray());
        Assert.IsTrue(available.Availability!.IsAvailable);
        Assert.IsFalse(unavailable.Availability!.IsAvailable);
    }

    [TestMethod]
    public void NotConfigured_RequiresQualifiedRepositoryAndPinnedProfileAuthority()
    {
        var context = ReadyContext() with
        {
            RepositoryBindingState = RepositoryBindingStates.SetupConfirmed,
            CurrentAuthority = null,
            RepositoryObservation = null,
            BuildValidation = null,
            TestValidation = null,
            CodeIndex = null,
            SandboxQualification = null
        };

        var result = RepositoryReadinessEvaluator.Evaluate(context);

        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured, result.ExecutionReadiness);
        Assert.IsFalse(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.RepositoryBindingQualified).Passed);
    }

    [TestMethod]
    public void QualifiedConfigurationWithMissingEvidence_IsValidationRequired()
    {
        var context = ReadyContext() with
        {
            RepositoryObservation = null,
            BuildValidation = null,
            TestValidation = null,
            CodeIndex = null,
            SandboxQualification = null,
            BuilderConfigurationEvidence = null
        };

        var result = RepositoryReadinessEvaluator.Evaluate(context);

        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired, result.ExecutionReadiness);
        Assert.IsTrue(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.RepositoryBindingQualified).Passed);
        Assert.IsTrue(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.ExecutionProfilePinned).Passed);
    }

    [TestMethod]
    public void UnresolvedSandboxPolicy_DoesNotReclassifyQualifiedRepositoryAsNotConfigured()
    {
        var context = ReadyContext();
        var unresolvedAuthority = context.CurrentAuthority! with
        {
            ContainerImageDigest = null,
            SandboxPolicyVersion = null,
            SandboxPolicySha256 = null,
            OfflineFeedManifestSha256 = null
        };

        var result = RepositoryReadinessEvaluator.Evaluate(context with
        {
            CurrentAuthority = unresolvedAuthority,
            RepositoryObservation = null,
            BuildValidation = null,
            TestValidation = null,
            CodeIndex = null,
            SandboxQualification = null
        });

        Assert.AreEqual(ProjectExecutionReadinessStates.ValidationRequired, result.ExecutionReadiness);
        Assert.IsTrue(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.RepositoryBindingQualified).Passed);
        Assert.IsTrue(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.ExecutionProfilePinned).Passed);
        Assert.IsFalse(result.Gates.Single(static gate =>
            gate.Gate == RepositoryReadinessGateName.SandboxQualified).Passed);
    }

    [TestMethod]
    public void CodeIndexCanonicalHash_PreservesSemanticSourceOrder()
    {
        var snapshot = ReadyContext().CodeIndex!;
        var reversed = snapshot with
        {
            Sources =
            [
                snapshot.Sources[1] with { Ordinal = 1 },
                snapshot.Sources[0] with { Ordinal = 2 }
            ]
        };
        reversed = reversed with
        {
            IndexedContentSha256 = CodeIndexSnapshotCodec.ComputeIndexedContentHash(reversed.Sources)
        };

        Assert.AreNotEqual(
            CodeIndexSnapshotCodec.ComputeHash(snapshot),
            CodeIndexSnapshotCodec.ComputeHash(reversed));
        StringAssert.Contains(
            CodeIndexSnapshotCodec.SerializeCanonical(snapshot),
            "src/App.cs");
    }

    [TestMethod]
    public void CodeIndexCanonicalHash_RejectsMoreThanTheBoundedSourceCount()
    {
        var sources = Enumerable.Range(1, CodeIndexSnapshotCodec.MaximumSources + 1)
            .Select(static ordinal => new CodeIndexSourceFingerprint(
                ordinal,
                $"src/{ordinal}.cs",
                new string('a', 64)))
            .ToArray();

        var exception = Assert.ThrowsExactly<RepositoryReadinessValidationException>(() =>
            CodeIndexSnapshotCodec.ComputeIndexedContentHash(sources));

        StringAssert.Contains(exception.Message, "4096");
    }

    [TestMethod]
    public void CodeIndexCanonicalHash_RejectsRelativePathsLongerThanTheSqlBound()
    {
        IReadOnlyList<CodeIndexSourceFingerprint> sources =
        [
            new(1, new string('a', CodeIndexSnapshotCodec.MaximumRelativePathLength + 1), Hash('a'))
        ];

        Assert.ThrowsExactly<RepositoryReadinessValidationException>(() =>
            CodeIndexSnapshotCodec.ComputeIndexedContentHash(sources));
    }

    [TestMethod]
    public void CanonicalBindingHash_NormalizesHashFormattingButChangesForAuthorityDrift()
    {
        var binding = ReadyContext().CodeIndex!.Binding;
        var formattingOnly = binding with
        {
            ProfileDescriptorSha256 = binding.ProfileDescriptorSha256.ToUpperInvariant(),
            ContainerImageDigest = binding.ContainerImageDigest.ToUpperInvariant()
        };
        var drifted = binding with { RepositoryBindingRevision = binding.RepositoryBindingRevision + 1 };

        Assert.AreEqual(
            RepositoryReadinessEvidenceBindingCodec.ComputeHash(binding),
            RepositoryReadinessEvidenceBindingCodec.ComputeHash(formattingOnly));
        Assert.AreNotEqual(
            RepositoryReadinessEvidenceBindingCodec.ComputeHash(binding),
            RepositoryReadinessEvidenceBindingCodec.ComputeHash(drifted));
    }

    [TestMethod]
    public void ReadinessCoreContract_HasNoBuilderAuthorizationInput()
    {
        var readinessTypes = new[]
        {
            typeof(RepositoryReadinessEvaluationContext),
            typeof(RepositoryReadinessEvaluationResult),
            typeof(RepositoryReadinessGateResult),
            typeof(RefreshRepositoryReadinessCommand)
        };

        Assert.IsFalse(readinessTypes
            .SelectMany(static type => type.GetProperties())
            .Any(static property => property.Name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase)));
    }

    private static RepositoryReadinessEvaluationContext ReadyContext()
    {
        var observation = new RepositoryStateObservation
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            RepositoryBindingId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            RepositoryBindingRevision = 4,
            BaselineCommit = Git('a'),
            HeadCommit = Git('a'),
            GitTreeId = Git('c'),
            WorktreeState = RepositoryWorktreeStates.Clean,
            WorktreeFingerprint = Hash('b'),
            ObservedAtUtc = Now,
            EvidenceHash = Hash('0')
        };
        observation = observation with
        {
            EvidenceHash = RepositoryStateObservationCodec.ComputeEvidenceHash(observation)
        };

        var indexId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var current = new RepositoryReadinessEvidenceBinding
        {
            ProjectId = 17,
            RepositoryBindingId = observation.RepositoryBindingId,
            RepositoryBindingRevision = observation.RepositoryBindingRevision,
            BaselineCommit = observation.BaselineCommit,
            RepositoryStateObservationId = observation.Id,
            WorktreeFingerprint = observation.WorktreeFingerprint,
            ProjectExecutionProfileId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            ProjectExecutionProfileRevision = 3,
            ProfileDefinitionId = RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1,
            ProfileDescriptorRevision = 1,
            ProfileDescriptorSha256 = Hash('c'),
            RestoreCommandSha256 = Hash('d'),
            BuildCommandSha256 = Hash('e'),
            TestCommandSha256 = Hash('f'),
            SdkToolchainManifestId = "dotnet-sdk-10-offline-v1",
            ContainerImageDigest = "sha256:" + Hash('1'),
            SandboxPolicyVersion = "irondev-workbench-sandbox-v0.1-policy-2",
            SandboxPolicySha256 = Hash('2'),
            OfflineFeedManifestSha256 = Hash('3'),
            TemplateBundleSha256 = Hash('4'),
            CodeIndexSnapshotId = indexId,
            CodeIndexSnapshotRevision = 2
        };
        var authority = RepositoryReadinessAuthorityCodec.ToAuthority(current);
        var preIndex = current.WithoutCodeIndex();
        var build = new BuildValidationRecord
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000005"),
            Revision = 1,
            Binding = preIndex,
            RestoreResult = Passed(preIndex.RestoreCommandSha256, '5'),
            BuildResult = Passed(preIndex.BuildCommandSha256, '6'),
            ValidatedAtUtc = Now.AddMinutes(1),
            EvidenceManifestSha256 = Hash('7')
        };
        var test = new TestValidationRecord
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000006"),
            Revision = 1,
            Binding = preIndex,
            TestResult = Passed(preIndex.TestCommandSha256, '8'),
            ValidatedAtUtc = Now.AddMinutes(2),
            EvidenceManifestSha256 = Hash('9')
        };
        IReadOnlyList<CodeIndexSourceFingerprint> sources =
        [
            new(1, "src/App.cs", Hash('a')),
            new(2, "tests/AppTests.cs", Hash('b'))
        ];
        var index = new CodeIndexSnapshot
        {
            Id = indexId,
            Revision = 2,
            Binding = current,
            State = RepositoryCodeIndexStates.Current,
            IndexFormatVersion = "irondev-code-index-v1",
            Sources = sources,
            IndexedContentSha256 = CodeIndexSnapshotCodec.ComputeIndexedContentHash(sources),
            IndexedAtUtc = Now.AddMinutes(3),
            EvidenceManifestSha256 = Hash('c')
        };
        var sandbox = new RepositorySandboxQualificationEvidence
        {
            QualificationAttemptId = Guid.Parse("70000000-0000-0000-0000-000000000007"),
            Revision = 1,
            Binding = preIndex,
            State = RepositorySandboxQualificationEvidenceStates.Passed,
            ValidatedAtUtc = Now.AddMinutes(4),
            EvidenceManifestSha256 = Hash('d')
        };
        var configuration = new BuilderStableConfigurationBinding
        {
            ConfigurationId = Guid.Parse("80000000-0000-0000-0000-000000000008"),
            Revision = 5,
            ProviderId = "openai",
            ModelId = "builder-model-v1",
            ConfigurationSha256 = Hash('e')
        };
        var configurationEvidence = new BuilderStableConfigurationEvidence
        {
            Binding = configuration,
            IsConfigured = true,
            ValidatedAtUtc = Now.AddMinutes(5),
            EvidenceManifestSha256 = Hash('f')
        };

        return new RepositoryReadinessEvaluationContext
        {
            ProjectId = current.ProjectId,
            RepositoryBindingState = RepositoryBindingStates.Qualified,
            CurrentAuthority = authority,
            RepositoryObservation = observation,
            BuildValidation = build,
            TestValidation = test,
            CodeIndex = index,
            SandboxQualification = sandbox,
            CurrentBuilderConfiguration = configuration,
            BuilderConfigurationEvidence = configurationEvidence,
            Availability = Availability(ExecutionAvailabilityStates.Available, "ProviderAvailable")
        };
    }

    private static RepositoryValidationCommandResult Passed(string commandSha256, char outputHash) => new()
    {
        CommandSha256 = commandSha256,
        Outcome = RepositoryValidationOutcome.Passed,
        ExitCode = 0,
        TimedOut = false,
        DurationMilliseconds = 100,
        StandardOutputSha256 = Hash(outputHash),
        StandardErrorSha256 = Hash('0')
    };

    private static ExecutionAvailabilityCheck Availability(string state, string reasonCode) => new()
    {
        State = state,
        ReasonCode = reasonCode,
        SafeMessage = reasonCode,
        CheckedAtUtc = Now.AddMinutes(10)
    };

    private static RepositoryReadinessEvidenceBinding Mutate(
        RepositoryReadinessEvidenceBinding binding,
        string field) => field switch
        {
            "ProjectId" => binding with { ProjectId = binding.ProjectId + 1 },
            "RepositoryBindingId" => binding with { RepositoryBindingId = Guid.Parse("91000000-0000-0000-0000-000000000001") },
            "RepositoryBindingRevision" => binding with { RepositoryBindingRevision = binding.RepositoryBindingRevision + 1 },
            "BaselineCommit" => binding with { BaselineCommit = Git('0') },
            "RepositoryStateObservationId" => binding with { RepositoryStateObservationId = Guid.Parse("91000000-0000-0000-0000-000000000002") },
            "WorktreeFingerprint" => binding with { WorktreeFingerprint = Hash('0') },
            "ProjectExecutionProfileId" => binding with { ProjectExecutionProfileId = Guid.Parse("91000000-0000-0000-0000-000000000003") },
            "ProjectExecutionProfileRevision" => binding with { ProjectExecutionProfileRevision = binding.ProjectExecutionProfileRevision + 1 },
            "ProfileDefinitionId" => binding with { ProfileDefinitionId = "different-profile-v1" },
            "ProfileDescriptorRevision" => binding with { ProfileDescriptorRevision = binding.ProfileDescriptorRevision + 1 },
            "ProfileDescriptorSha256" => binding with { ProfileDescriptorSha256 = Hash('0') },
            "RestoreCommandSha256" => binding with { RestoreCommandSha256 = Hash('0') },
            "BuildCommandSha256" => binding with { BuildCommandSha256 = Hash('0') },
            "TestCommandSha256" => binding with { TestCommandSha256 = Hash('0') },
            "SdkToolchainManifestId" => binding with { SdkToolchainManifestId = "different-toolchain-v1" },
            "ContainerImageDigest" => binding with { ContainerImageDigest = "sha256:" + Hash('0') },
            "SandboxPolicyVersion" => binding with { SandboxPolicyVersion = "different-policy-v1" },
            "SandboxPolicySha256" => binding with { SandboxPolicySha256 = Hash('0') },
            "OfflineFeedManifestSha256" => binding with { OfflineFeedManifestSha256 = Hash('0') },
            "TemplateBundleSha256" => binding with { TemplateBundleSha256 = Hash('0') },
            "CodeIndexSnapshotId" => binding with { CodeIndexSnapshotId = Guid.Parse("91000000-0000-0000-0000-000000000004") },
            "CodeIndexSnapshotRevision" => binding with { CodeIndexSnapshotRevision = binding.CodeIndexSnapshotRevision + 1 },
            _ => throw new AssertFailedException($"Unknown authority field '{field}'.")
        };

    private static string Hash(char value) => new(value, 64);

    private static string Git(char value) => new(value, 40);
}
