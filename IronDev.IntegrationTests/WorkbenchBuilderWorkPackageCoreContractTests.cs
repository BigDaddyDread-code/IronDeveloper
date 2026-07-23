using IronDev.Core.Workbench;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class WorkbenchBuilderWorkPackageCoreContractTests
{
    [TestMethod]
    public void CanonicalCore_IsDeterministic_AndRoundTripsExactly()
    {
        var core = ValidCore();
        var json = BuilderWorkPackageCoreCodec.SerializeCanonical(core);

        Assert.AreEqual(json, BuilderWorkPackageCoreCodec.SerializeCanonical(core));
        Assert.AreEqual(
            BuilderWorkPackageCoreCodec.ComputeHash(core),
            BuilderWorkPackageCoreCodec.ComputeHash(
                BuilderWorkPackageCoreCodec.DeserializeCanonical(json)));
    }

    [TestMethod]
    public void CanonicalCore_FreezesEveryPr07AInput()
    {
        var core = ValidCore();
        var json = BuilderWorkPackageCoreCodec.SerializeCanonical(core);

        foreach (var required in new[]
                 {
                     "\"tenantId\"", "\"projectId\"", "\"workItemId\"",
                     "\"workItemContractRevision\"", "\"workItemContractSha256\"",
                     "\"acceptanceCriteria\"", "\"permittedFiles\"",
                     "\"repositoryBindingId\"", "\"baselineCommit\"",
                     "\"readinessAssessment\"", "\"repositoryObservation\"", "\"codeIndex\"",
                     "\"builderAgentVersion\"", "\"promptVersion\"", "\"toolPolicyVersion\"",
                     "\"contextSchemaVersion\"", "\"outputSchemaVersion\"",
                     "\"effectiveProfile\"", "\"sandbox\""
                 })
            StringAssert.Contains(json, required);
    }

    [TestMethod]
    public void CanonicalCore_AnyFrozenAuthorityChange_ChangesHash()
    {
        var core = ValidCore();
        var changed = core with
        {
            PromptVersion = "irondev-builder-prompt-v2"
        };

        Assert.ThrowsExactly<BuilderWorkPackageCoreValidationException>(
            () => BuilderWorkPackageCoreCodec.ComputeHash(changed));

        var changedCriteria = core with
        {
            Tickets =
            [
                core.Tickets[0] with
                {
                    AcceptanceCriteria = "A different exact criterion."
                }
            ]
        };
        Assert.AreNotEqual(
            BuilderWorkPackageCoreCodec.ComputeHash(core),
            BuilderWorkPackageCoreCodec.ComputeHash(changedCriteria));
    }

    [TestMethod]
    public void CanonicalCore_RejectsMissingAcceptanceCriteriaOrScope()
    {
        var core = ValidCore();
        AssertInvalid(core with
        {
            Tickets = [core.Tickets[0] with { AcceptanceCriteria = "" }]
        });
        AssertInvalid(core with
        {
            Tickets = [core.Tickets[0] with { PermittedFiles = [] }]
        });
    }

    [TestMethod]
    public void CanonicalCore_RejectsUnsafeOrDuplicatePaths()
    {
        var core = ValidCore();
        AssertInvalid(core with
        {
            Tickets = [core.Tickets[0] with { PermittedFiles = ["src/App.cs", "../escape.cs"] }]
        });
        AssertInvalid(core with
        {
            Tickets = [core.Tickets[0] with { PermittedFiles = ["src/App.cs", "src/App.cs"] }]
        });
    }

    [TestMethod]
    public void CanonicalCore_RejectsNonContiguousTicketAndIndexOrdinals()
    {
        var core = ValidCore();
        AssertInvalid(core with
        {
            Tickets = [core.Tickets[0] with { Ordinal = 2 }]
        });
        AssertInvalid(core with
        {
            CodeIndex = core.CodeIndex with
            {
                Sources = [core.CodeIndex.Sources[0] with { Ordinal = 2 }]
            }
        });
    }

    [TestMethod]
    public void CanonicalCore_RejectsNonCanonicalJsonAndUnknownMembers()
    {
        var json = BuilderWorkPackageCoreCodec.SerializeCanonical(ValidCore());
        Assert.ThrowsExactly<BuilderWorkPackageCoreValidationException>(
            () => BuilderWorkPackageCoreCodec.DeserializeCanonical(" " + json));
        Assert.ThrowsExactly<BuilderWorkPackageCoreValidationException>(
            () => BuilderWorkPackageCoreCodec.DeserializeCanonical(
                json[..^1] + ",\"query\":\"retrieve whatever you need\"}"));
    }

    [TestMethod]
    public void AuthorizationEnvelope_DoesNotAlterCoreHash()
    {
        var core = ValidCore();
        var before = BuilderWorkPackageCoreCodec.ComputeHash(core);
        var package = new BuilderWorkPackage
        {
            Core = core,
            CoreSha256 = before,
            SingleUseAuthorizationId = Guid.NewGuid(),
            AuthorizedAtUtc = DateTimeOffset.Parse("2026-07-24T00:00:00Z"),
            ExpiresAtUtc = DateTimeOffset.Parse("2026-07-24T00:15:00Z"),
            SingleUse = true
        };

        Assert.AreEqual(before, BuilderWorkPackageCoreCodec.ComputeHash(package.Core));
        Assert.IsTrue(package.SingleUse);
    }

    [TestMethod]
    public void Core_HasNoAuthorizationOrProviderInvocationAuthority()
    {
        var names = typeof(BuilderWorkPackageCore).GetProperties()
            .Select(static property => property.Name)
            .ToArray();
        Assert.IsFalse(names.Any(static name =>
            name.Contains("Authorization", StringComparison.Ordinal) ||
            name.Contains("Lease", StringComparison.Ordinal) ||
            name.Contains("Actor", StringComparison.Ordinal) ||
            name.Contains("Query", StringComparison.Ordinal)));
    }

    private static BuilderWorkPackageCore ValidCore() => new()
    {
        Id = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
        CanonicalizationVersion = BuilderWorkPackageCoreContract.CanonicalizationVersion1,
        TenantId = 1,
        ProjectId = 42,
        Tickets =
        [
            new BuilderWorkPackageTicketReference
            {
                Ordinal = 1,
                WorkItemId = 1001,
                WorkItemVersion = 3,
                WorkItemContractId = 2001,
                WorkItemContractRevision = 2,
                WorkItemContractSha256 = Hash('1'),
                TicketId = 42,
                TicketRevision = 7,
                AcceptanceCriteria = "The exact behaviour is independently observable.",
                PermittedFiles = ["src/App.cs", "tests/AppTests.cs"]
            }
        ],
        GoverningArtifacts =
        [
            new BuilderWorkPackageArtifactReference(
                1,
                BuilderWorkPackageGoverningArtifactKinds.ProjectUnderstanding,
                91,
                4)
        ],
        RepositoryBindingId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
        RepositoryBindingRevision = 5,
        BranchName = "main",
        BaselineCommit = new string('a', 40),
        ReadinessAssessment = new BuilderReadinessAssessmentSnapshot
        {
            Id = 301,
            Revision = 8,
            TechnicalEvidenceId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            EvidenceSha256 = Hash('2'),
            AssessedAtUtc = DateTimeOffset.Parse("2026-07-23T23:55:00Z")
        },
        RepositoryObservation = new BuilderRepositoryObservationSnapshot
        {
            Id = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
            EvidenceSha256 = Hash('3'),
            HeadCommit = new string('a', 40),
            GitTreeId = new string('b', 40),
            WorktreeState = RepositoryWorktreeStates.Clean,
            WorktreeFingerprint = Hash('4'),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-23T23:50:00Z")
        },
        CodeIndex = new BuilderCodeIndexSnapshot
        {
            Id = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"),
            Revision = 1,
            EvidenceSha256 = Hash('5'),
            SchemaVersion = 1,
            IndexerVersion = "workbench-git-tree-index-v1",
            IndexedContentSha256 = Hash('6'),
            Sources = [new BuilderCodeIndexSourceSnapshot(1, "src/App.cs", Hash('7'))],
            IndexedAtUtc = DateTimeOffset.Parse("2026-07-23T23:54:00Z")
        },
        RestoreCommandSha256 = Hash('8'),
        BuildCommandSha256 = Hash('9'),
        TestCommandSha256 = Hash('a'),
        BuilderAgentVersion = BuilderRoleContract.BuilderAgentVersion,
        PromptVersion = BuilderRoleContract.PromptVersion,
        ToolPolicyVersion = BuilderRoleContract.ToolPolicyVersion,
        ContextSchemaVersion = BuilderRoleContract.ContextSchemaVersion,
        OutputSchemaVersion = BuilderRoleContract.OutputSchemaVersion,
        EffectiveProfile = new BuilderEffectiveProfileSnapshot
        {
            ProjectExecutionProfileId = Guid.Parse("11111111-1111-4111-8111-111111111111"),
            ProjectExecutionProfileRevision = 3,
            ProfileDefinitionId = "dotnet-v1",
            ProfileDescriptorRevision = 2,
            ProfileDescriptorSha256 = Hash('b'),
            BuilderConfigurationId = Guid.Parse("22222222-2222-4222-8222-222222222222"),
            BuilderConfigurationRevision = 4,
            ProviderId = "openai",
            ModelId = "builder-model",
            BuilderConfigurationSha256 = Hash('c')
        },
        Sandbox = new BuilderSandboxAuthoritySnapshot
        {
            QualificationAttemptId = Guid.Parse("33333333-3333-4333-8333-333333333333"),
            EvidenceManifestId = Guid.Parse("44444444-4444-4444-8444-444444444444"),
            EvidenceManifestSha256 = Hash('d'),
            PolicyVersion = "sandbox-v1",
            PolicySha256 = Hash('e'),
            QualifiedImageDigest = Hash('f'),
            ToolchainManifestId = "dotnet-sdk-v1",
            ToolchainManifestSha256 = Hash('0'),
            OfflineFeedManifestSha256 = Hash('1'),
            TemplateBundleSha256 = Hash('2')
        },
        CreatedAtUtc = DateTimeOffset.Parse("2026-07-24T00:00:00Z")
    };

    private static string Hash(char value) => new(value, 64);

    private static void AssertInvalid(BuilderWorkPackageCore core) =>
        Assert.ThrowsExactly<BuilderWorkPackageCoreValidationException>(
            () => BuilderWorkPackageCoreCodec.SerializeCanonical(core));
}
