using System.Text.Json;
using IronDev.Core.Workbench;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchBuilderExecutionContractTests
{
    [TestMethod]
    public void ExactOutput_AcceptsOnlyPermittedNonTestFiles()
    {
        var core = Core(["src/App.cs"]);
        var output = JsonSerializer.Serialize(new
        {
            schemaVersion = BuilderRoleContract.OutputSchemaVersion,
            proposedFiles = new[] { new { relativePath = "src/App.cs", content = "namespace App;" } }
        });

        var files = BuilderOutputValidator.Validate(output, core);

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual("src/App.cs", files[0].RelativePath);
        Assert.AreEqual(64, files[0].ContentSha256.Length);
    }

    [TestMethod]
    public void ExactOutput_RejectsAuthorityExpansion()
    {
        var error = Assert.ThrowsExactly<BuilderOutputValidationException>(() =>
            BuilderOutputValidator.Validate(JsonSerializer.Serialize(new
            {
                schemaVersion = BuilderRoleContract.OutputSchemaVersion,
                proposedFiles = new[] { new { relativePath = "src/Other.cs", content = "x" } }
            }), Core(["src/App.cs"])));

        Assert.AreEqual(BuilderExecutionFailureCodes.ScopeViolation, error.FailureCode);
    }

    [TestMethod]
    public void ExactOutput_RejectsTestRewriteEvenWhenTicketPermitsIt()
    {
        var error = Assert.ThrowsExactly<BuilderOutputValidationException>(() =>
            BuilderOutputValidator.Validate(JsonSerializer.Serialize(new
            {
                schemaVersion = BuilderRoleContract.OutputSchemaVersion,
                proposedFiles = new[] { new { relativePath = "tests/AppTests.cs", content = "green" } }
            }), Core(["tests/AppTests.cs"])));

        Assert.AreEqual(BuilderExecutionFailureCodes.TestRewriteRejected, error.FailureCode);
    }

    [TestMethod]
    public void ExactOutput_RejectsUnknownPropertiesAndTraversal()
    {
        Assert.ThrowsExactly<BuilderOutputValidationException>(() =>
            BuilderOutputValidator.Validate(
                """{"schemaVersion":"irondev-builder-output-v1","proposedFiles":[],"authority":"more"}""",
                Core(["src/App.cs"])));
        Assert.ThrowsExactly<BuilderOutputValidationException>(() =>
            BuilderOutputValidator.Validate(JsonSerializer.Serialize(new
            {
                schemaVersion = BuilderRoleContract.OutputSchemaVersion,
                proposedFiles = new[] { new { relativePath = "../App.cs", content = "x" } }
            }), Core(["src/App.cs"])));
    }

    private static BuilderWorkPackageCore Core(IReadOnlyList<string> permittedFiles) =>
        new()
        {
            Id = Guid.NewGuid(),
            CanonicalizationVersion = 1,
            TenantId = 1,
            ProjectId = 2,
            Tickets =
            [
                new BuilderWorkPackageTicketReference
                {
                    Ordinal = 1, WorkItemId = 1, WorkItemVersion = 1,
                    WorkItemContractId = 1, WorkItemContractRevision = 1,
                    WorkItemContractSha256 = new('a', 64),
                    TicketId = 1, TicketRevision = 1,
                    AcceptanceCriteria = "Works.", PermittedFiles = permittedFiles
                }
            ],
            GoverningArtifacts = [new(1, "ProjectUnderstanding", 1, 1)],
            RepositoryBindingId = Guid.NewGuid(),
            RepositoryBindingRevision = 1,
            BranchName = "main",
            BaselineCommit = new('b', 40),
            ReadinessAssessment = new BuilderReadinessAssessmentSnapshot
            {
                Id = 1, Revision = 1, TechnicalEvidenceId = Guid.NewGuid(),
                EvidenceSha256 = new('c', 64), AssessedAtUtc = DateTimeOffset.UtcNow
            },
            RepositoryObservation = new BuilderRepositoryObservationSnapshot
            {
                Id = Guid.NewGuid(), EvidenceSha256 = new('d', 64),
                HeadCommit = new('b', 40), GitTreeId = new('e', 40),
                WorktreeState = "Clean", WorktreeFingerprint = new('f', 64),
                ObservedAtUtc = DateTimeOffset.UtcNow
            },
            CodeIndex = new BuilderCodeIndexSnapshot
            {
                Id = Guid.NewGuid(), Revision = 1, EvidenceSha256 = new('1', 64),
                SchemaVersion = 1, IndexerVersion = "index-v1",
                IndexedContentSha256 = new('2', 64), Sources = [],
                IndexedAtUtc = DateTimeOffset.UtcNow
            },
            RestoreCommandSha256 = new('3', 64),
            BuildCommandSha256 = new('4', 64),
            TestCommandSha256 = new('5', 64),
            BuilderAgentVersion = BuilderRoleContract.BuilderAgentVersion,
            PromptVersion = BuilderRoleContract.PromptVersion,
            ToolPolicyVersion = BuilderRoleContract.ToolPolicyVersion,
            ContextSchemaVersion = BuilderRoleContract.ContextSchemaVersion,
            OutputSchemaVersion = BuilderRoleContract.OutputSchemaVersion,
            EffectiveProfile = new BuilderEffectiveProfileSnapshot
            {
                ProjectExecutionProfileId = Guid.NewGuid(),
                ProjectExecutionProfileRevision = 1,
                ProfileDefinitionId = "profile-v1", ProfileDescriptorRevision = 1,
                ProfileDescriptorSha256 = new('6', 64),
                BuilderConfigurationId = Guid.NewGuid(), BuilderConfigurationRevision = 1,
                ProviderId = "provider", ModelId = "model",
                BuilderConfigurationSha256 = new('7', 64)
            },
            Sandbox = new BuilderSandboxAuthoritySnapshot
            {
                QualificationAttemptId = Guid.NewGuid(), EvidenceManifestId = Guid.NewGuid(),
                EvidenceManifestSha256 = new('8', 64), PolicyVersion = "policy-v1",
                PolicySha256 = new('9', 64), QualifiedImageDigest = new('a', 64),
                ToolchainManifestId = "tools-v1", ToolchainManifestSha256 = new('b', 64),
                OfflineFeedManifestSha256 = new('c', 64), TemplateBundleSha256 = new('d', 64)
            },
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}
