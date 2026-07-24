using IronDev.Core.Workbench;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("WorkbenchBuilderPromptPreparation")]
public sealed class WorkbenchBuilderPromptContractTests
{
    [TestMethod]
    public void Materialization_IsDeterministicExactAndContainsNoLooseQuery()
    {
        var package = ValidPackage();
        var runId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        var preparedAt = DateTimeOffset.Parse("2026-07-24T00:05:00Z");

        var first = BuilderPromptContract.Materialize(package, runId, preparedAt);
        var second = BuilderPromptContract.Materialize(package, runId, preparedAt);

        Assert.AreEqual(first, second);
        StringAssert.Contains(first.RoleContextJson, "\"singleUseAuthorizationId\"");
        StringAssert.Contains(first.RoleContextJson, "\"acceptanceCriteria\"");
        StringAssert.Contains(first.RoleContextJson, "\"permittedFiles\"");
        StringAssert.Contains(first.ToolManifestJson, "\"mayWriteActiveRepository\":false");
        StringAssert.Contains(first.ProviderInputJson, "\"roleContextSha256\"");
        StringAssert.Contains(first.ProviderInputJson, first.RoleContextSha256);
        StringAssert.Contains(first.ProviderInputJson, first.ToolManifestSha256);
        Assert.IsFalse(first.ProviderInputJson.Contains(
            "using the current repository", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(first.ProviderInputJson.Contains(
            "\"query\"", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AnyExactPackageChange_ChangesContextAndProviderInputHashes()
    {
        var package = ValidPackage();
        var changedCore = package.Core with
        {
            Tickets =
            [
                package.Core.Tickets[0] with
                {
                    AcceptanceCriteria = "A different exact criterion."
                }
            ]
        };
        var changed = package with
        {
            Core = changedCore,
            CoreSha256 = BuilderWorkPackageCoreCodec.ComputeHash(changedCore)
        };
        var runId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        var preparedAt = DateTimeOffset.Parse("2026-07-24T00:05:00Z");

        var before = BuilderPromptContract.Materialize(package, runId, preparedAt);
        var after = BuilderPromptContract.Materialize(changed, runId, preparedAt);

        Assert.AreNotEqual(before.RoleContextSha256, after.RoleContextSha256);
        Assert.AreNotEqual(before.ProviderInputSha256, after.ProviderInputSha256);
        Assert.AreEqual(before.PromptSha256, after.PromptSha256);
        Assert.AreEqual(before.ToolManifestSha256, after.ToolManifestSha256);
    }

    [TestMethod]
    public void Materialization_RejectsExpiredOrMismatchedAuthorizationPackage()
    {
        var package = ValidPackage();
        var runId = Guid.NewGuid();
        Assert.ThrowsExactly<BuilderPromptPreparationValidationException>(() =>
            BuilderPromptContract.Materialize(
                package,
                runId,
                package.ExpiresAtUtc));
        Assert.ThrowsExactly<BuilderPromptPreparationIntegrityException>(() =>
            BuilderPromptContract.Materialize(
                package with { CoreSha256 = new string('0', 64) },
                runId,
                DateTimeOffset.Parse("2026-07-24T00:05:00Z")));
    }

    private static BuilderWorkPackage ValidPackage()
    {
        var core = new BuilderWorkPackageCore
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            CanonicalizationVersion = 1,
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
        return new BuilderWorkPackage
        {
            Core = core,
            CoreSha256 = BuilderWorkPackageCoreCodec.ComputeHash(core),
            SingleUseAuthorizationId = Guid.Parse("66666666-6666-4666-8666-666666666666"),
            AuthorizedAtUtc = DateTimeOffset.Parse("2026-07-24T00:00:00Z"),
            ExpiresAtUtc = DateTimeOffset.Parse("2026-07-24T00:15:00Z"),
            SingleUse = true
        };
    }

    private static string Hash(char value) => new(value, 64);
}
