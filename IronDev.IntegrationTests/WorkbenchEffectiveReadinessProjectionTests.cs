using Dapper;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchEffectiveReadinessProjectionSqlTests : IntegrationTestBase
{
    [TestMethod]
    public async Task EffectiveProjection_IsValidationRequiredWithoutEvidence_ReadyForExactEvidence_AndInvalidatedByNewerEvidence()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        ProjectionStates states;
        try
        {
            states = await connection.QuerySingleAsync<ProjectionStates>(
                """
                INSERT dbo.Tenants(Name, Slug)
                VALUES (N'Effective readiness projection tenant', N'effective-readiness-projection');
                DECLARE @TenantId INT=CONVERT(INT, SCOPE_IDENTITY());
                INSERT dbo.Users(Email, DisplayName, IsActive)
                VALUES (N'effective-readiness-projection@irondev.local',
                        N'Effective readiness projection actor', 1);
                DECLARE @ActorUserId INT=CONVERT(INT, SCOPE_IDENTITY());

                DECLARE @Now DATETIME2(7)=SYSUTCDATETIME();
                DECLARE @BindingId UNIQUEIDENTIFIER=NEWID();
                DECLARE @ProfileId UNIQUEIDENTIFIER=NEWID();
                DECLARE @SandboxAttemptId UNIQUEIDENTIFIER=NEWID();
                DECLARE @SandboxManifestId UNIQUEIDENTIFIER=NEWID();
                DECLARE @TechnicalAttemptId UNIQUEIDENTIFIER=NEWID();
                DECLARE @ObservationId UNIQUEIDENTIFIER=NEWID();
                DECLARE @BuildId UNIQUEIDENTIFIER=NEWID();
                DECLARE @TestId UNIQUEIDENTIFIER=NEWID();
                DECLARE @IndexId UNIQUEIDENTIFIER=NEWID();
                DECLARE @ModelId UNIQUEIDENTIFIER=NEWID();
                DECLARE @ConfigurationId UNIQUEIDENTIFIER=NEWID();
                DECLARE @Baseline CHAR(40)=REPLICATE('b', 40);
                DECLARE @GitTree CHAR(40)=REPLICATE('c', 40);
                DECLARE @DescriptorHash CHAR(64)=REPLICATE('a', 64);
                DECLARE @TemplateHash CHAR(64)=REPLICATE('b', 64);
                DECLARE @ToolchainHash CHAR(64)=REPLICATE('c', 64);
                DECLARE @ImageHash CHAR(64)=REPLICATE('d', 64);
                DECLARE @PolicyHash CHAR(64)=REPLICATE('e', 64);
                DECLARE @FeedHash CHAR(64)=REPLICATE('f', 64);
                DECLARE @SandboxHash CHAR(64)=REPLICATE('1', 64);
                DECLARE @Fingerprint CHAR(64)=REPLICATE('2', 64);
                DECLARE @ObservationHash CHAR(64)=REPLICATE('3', 64);
                DECLARE @BuildHash CHAR(64)=REPLICATE('4', 64);
                DECLARE @TestHash CHAR(64)=REPLICATE('5', 64);
                DECLARE @IndexHash CHAR(64)=REPLICATE('6', 64);
                DECLARE @ModelHash CHAR(64)=REPLICATE('7', 64);
                DECLARE @TechnicalHash CHAR(64)=REPLICATE('8', 64);
                DECLARE @RestoreCommand NVARCHAR(1000)=N'dotnet restore';
                DECLARE @BuildCommand NVARCHAR(1000)=N'dotnet build';
                DECLARE @TestCommand NVARCHAR(1000)=N'dotnet test';
                DECLARE @RestoreHash CHAR(64)=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        @RestoreCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2));
                DECLARE @BuildCommandHash CHAR(64)=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        @BuildCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2));
                DECLARE @TestCommandHash CHAR(64)=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        @TestCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2));

                INSERT dbo.Projects (TenantId, Name) VALUES (@TenantId, N'Effective readiness projection');
                DECLARE @ProjectId INT=CONVERT(INT, SCOPE_IDENTITY());
                INSERT dbo.RepositoryBindings
                    (Id, TenantId, ProjectId, CurrentRevision, RepositoryKind, CanonicalPath,
                     BindingState, DefaultBranch, BaselineCommit, CreatedByActorUserId,
                     ConfirmedAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@BindingId, @TenantId, @ProjectId, 1, N'Existing', N'C:\projection-test',
                     N'Qualified', N'main', @Baseline, @ActorUserId,
                     @Now, @Now, @Now);
                INSERT dbo.ProjectExecutionProfiles
                    (Id, TenantId, ProjectId, RepositoryBindingId, CurrentRevision,
                     ProfileDefinitionId, ProfileDescriptorRevision, DescriptorSha256,
                     TemplateBundleSha256, PlanningBundleSha256, TargetFramework, Language,
                     ApplicationKind, TestFramework, SdkVersion, RuntimeVersion, SolutionPath,
                     AppProjectPath, TestProjectPath, RestoreCommand, BuildCommand, TestCommand,
                     ToolchainManifestId, ExecutionImageReference, PlanningReadiness,
                     CertificationState, CreatedByActorUserId, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@ProfileId, @TenantId, @ProjectId, @BindingId, 1,
                     N'test-profile', 1, @DescriptorHash, @TemplateHash, @TemplateHash,
                     N'net10.0', N'C#', N'Console', N'MSTest', N'10.0.0', N'10.0.0',
                     N'test.slnx', N'src/test.csproj', N'tests/test.csproj',
                     @RestoreCommand, @BuildCommand, @TestCommand, N'test-toolchain',
                     N'test@sha256:' + @ImageHash, N'PreviewPlanningOnly',
                     N'NotCertificationReady', @ActorUserId, @Now, @Now);

                DECLARE @ConfiguredWithoutEvidence NVARCHAR(50)=
                    (SELECT ExecutionReadiness FROM dbo.vw_WorkbenchEffectiveProjectReadiness
                     WHERE TenantId=@TenantId AND ProjectId=@ProjectId);
                DECLARE @ConfiguredWithoutEvidenceReason NVARCHAR(100)=
                    (SELECT ReasonCode FROM dbo.vw_WorkbenchEffectiveProjectReadiness
                     WHERE TenantId=@TenantId AND ProjectId=@ProjectId);

                -- The synthetic tuple isolates view semantics from mutation-authority setup.
                -- Every fence change is transaction-scoped, rolled back in finally, and
                -- independently checked below before the test returns.
                ALTER TABLE dbo.SandboxQualificationAttempts NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.SandboxEvidenceManifests NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.TechnicalValidationAttempts NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.RepositoryStateObservations NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.BuildValidationRecords NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.TestValidationRecords NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.CodeIndexSnapshots NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.BuilderModelConfigurationRecords NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.ProjectTechnicalReadinessEvidence NOCHECK CONSTRAINT ALL;
                DISABLE TRIGGER ALL ON dbo.SandboxQualificationAttempts;
                DISABLE TRIGGER ALL ON dbo.SandboxEvidenceManifests;
                DISABLE TRIGGER ALL ON dbo.TechnicalValidationAttempts;
                DISABLE TRIGGER ALL ON dbo.RepositoryStateObservations;
                DISABLE TRIGGER ALL ON dbo.BuildValidationRecords;
                DISABLE TRIGGER ALL ON dbo.TestValidationRecords;
                DISABLE TRIGGER ALL ON dbo.CodeIndexSnapshots;
                DISABLE TRIGGER ALL ON dbo.BuilderModelConfigurationRecords;
                DISABLE TRIGGER ALL ON dbo.ProjectTechnicalReadinessEvidence;

                INSERT dbo.SandboxQualificationAttempts
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     RepositoryProvisioningReceiptId, ClientOperationRecordId, ClientOperationId,
                     ActorUserId, ClientOperationKind, ClientOperationResourceScopeId,
                     WorkbenchSessionId, LeaseEpoch, AttemptNumber, ExpectedBindingRevision,
                     ExpectedExecutionProfileRevision, BaselineCommit, SourceManifestSha256,
                     SourceGitTreeId, ProfileDefinitionId, ProfileDescriptorRevision,
                     DescriptorSha256, TemplateBundleSha256, ToolchainManifestId,
                     ContainerImageDigest, OfflineFeedManifestSha256, SandboxPolicyVersion,
                     SandboxPolicySha256, TrustedSupervisorVersion, TrustedSupervisorSha256,
                     State, EvidenceManifestSha256, CleanupConfirmed, StartedAtUtc, CompletedAtUtc)
                VALUES
                    (@SandboxAttemptId, @TenantId, @ProjectId, @BindingId, @ProfileId,
                     NEWID(), -1, NEWID(), @ActorUserId, N'QualifyProductionSandbox',
                     N'projection-test', -1, 1, 1, 1, 1, @Baseline, @TemplateHash, @GitTree,
                     N'test-profile', 1, @DescriptorHash, @TemplateHash, N'test-toolchain',
                     N'test@sha256:' + @ImageHash, @FeedHash, N'test-policy', @PolicyHash,
                     N'test-supervisor', @PolicyHash, N'Passed', @SandboxHash, 1, @Now, @Now);
                INSERT dbo.SandboxEvidenceManifests
                    (Id, TenantId, ProjectId, SandboxQualificationAttemptId,
                     RepositoryBindingId, RepositoryBindingRevision, ProjectExecutionProfileId,
                     ProjectExecutionProfileRevision, ActorUserId, SchemaVersion, Passed,
                     ManifestJson, ManifestSha256, CreatedAtUtc)
                VALUES
                    (@SandboxManifestId, @TenantId, @ProjectId, @SandboxAttemptId,
                     @BindingId, 1, @ProfileId, 1, @ActorUserId, 1, 1,
                     N'{}', @SandboxHash, @Now);
                INSERT dbo.TechnicalValidationAttempts
                    (Id, TenantId, ProjectId, ClientOperationRecordId, ClientOperationId,
                     ActorUserId, ClientOperationKind, ClientOperationResourceScopeId,
                     WorkbenchSessionId, LeaseEpoch, AttemptNumber, RepositoryBindingId,
                     RepositoryBindingRevision, BaselineCommit, ProjectExecutionProfileId,
                     ProjectExecutionProfileRevision, ProfileDefinitionId,
                     ProfileDescriptorRevision, ProfileDescriptorSha256, RestoreCommandSha256,
                     BuildCommandSha256, TestCommandSha256, ToolchainManifestId,
                     ToolchainManifestSha256, ContainerImageDigest, ContainerImageDigestSha256,
                     SandboxPolicyVersion, SandboxPolicySha256, OfflineFeedManifestSha256,
                     TemplateBundleSha256, SandboxQualificationAttemptId,
                     SandboxEvidenceManifestId, SandboxEvidenceManifestSha256, State,
                     EvidenceSha256, StartedAtUtc, CompletedAtUtc)
                VALUES
                    (@TechnicalAttemptId, @TenantId, @ProjectId, -2, NEWID(), @ActorUserId,
                     N'ValidateRepositoryTechnicalReadiness', N'projection-test', -1, 1, 1,
                     @BindingId, 1, @Baseline, @ProfileId, 1, N'test-profile', 1,
                     @DescriptorHash, @RestoreHash, @BuildCommandHash, @TestCommandHash,
                     N'test-toolchain', @ToolchainHash, N'test@sha256:' + @ImageHash,
                     @ImageHash, N'test-policy', @PolicyHash, @FeedHash, @TemplateHash,
                     @SandboxAttemptId, @SandboxManifestId, @SandboxHash, N'Passed',
                     @TechnicalHash, @Now, @Now);
                INSERT dbo.RepositoryStateObservations
                    (Id, TenantId, ProjectId, TechnicalValidationAttemptId, RepositoryBindingId,
                     RepositoryBindingRevision, BaselineCommit, ProjectExecutionProfileId,
                     ProjectExecutionProfileRevision, ProfileDefinitionId,
                     ProfileDescriptorRevision, ProfileDescriptorSha256, RestoreCommandSha256,
                     BuildCommandSha256, TestCommandSha256, ToolchainManifestSha256,
                     ContainerImageDigestSha256, SandboxPolicySha256,
                     OfflineFeedManifestSha256, TemplateBundleSha256,
                     SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                     SandboxEvidenceManifestSha256, RepositoryFingerprintSha256, HeadCommit,
                     GitTreeId, DirtyState, ObservedAtUtc, EvidenceSha256)
                VALUES
                    (@ObservationId, @TenantId, @ProjectId, @TechnicalAttemptId, @BindingId,
                     1, @Baseline, @ProfileId, 1, N'test-profile', 1, @DescriptorHash,
                     @RestoreHash, @BuildCommandHash, @TestCommandHash, @ToolchainHash,
                     @ImageHash, @PolicyHash, @FeedHash, @TemplateHash, @SandboxAttemptId,
                     @SandboxManifestId, @SandboxHash, @Fingerprint, @Baseline, @GitTree,
                     N'Clean', @Now, @ObservationHash);
                INSERT dbo.BuildValidationRecords
                    (Id, TenantId, ProjectId, TechnicalValidationAttemptId, RepositoryBindingId,
                     RepositoryBindingRevision, BaselineCommit, RepositoryStateObservationId,
                     RepositoryFingerprintSha256, ProjectExecutionProfileId,
                     ProjectExecutionProfileRevision, ProfileDefinitionId,
                     ProfileDescriptorRevision, ProfileDescriptorSha256, RestoreCommandSha256,
                     BuildCommandSha256, TestCommandSha256, ToolchainManifestSha256,
                     ContainerImageDigestSha256, SandboxPolicySha256,
                     OfflineFeedManifestSha256, TemplateBundleSha256,
                     SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                     SandboxEvidenceManifestSha256, RestoreOutcome, RestoreExitCode,
                     RestoreStartedAtUtc, RestoreCompletedAtUtc, RestoreEvidenceSha256,
                     BuildOutcome, BuildExitCode, BuildStartedAtUtc, BuildCompletedAtUtc,
                     BuildEvidenceSha256, CreatedAtUtc, EvidenceSha256)
                VALUES
                    (@BuildId, @TenantId, @ProjectId, @TechnicalAttemptId, @BindingId, 1,
                     @Baseline, @ObservationId, @Fingerprint, @ProfileId, 1, N'test-profile',
                     1, @DescriptorHash, @RestoreHash, @BuildCommandHash, @TestCommandHash,
                     @ToolchainHash, @ImageHash, @PolicyHash, @FeedHash, @TemplateHash,
                     @SandboxAttemptId, @SandboxManifestId, @SandboxHash, N'Passed', 0,
                     @Now, @Now, @BuildHash, N'Passed', 0, @Now, @Now, @BuildHash,
                     @Now, @BuildHash);
                INSERT dbo.TestValidationRecords
                    (Id, TenantId, ProjectId, TechnicalValidationAttemptId, RepositoryBindingId,
                     RepositoryBindingRevision, BaselineCommit, RepositoryStateObservationId,
                     RepositoryFingerprintSha256, ProjectExecutionProfileId,
                     ProjectExecutionProfileRevision, ProfileDefinitionId,
                     ProfileDescriptorRevision, ProfileDescriptorSha256, RestoreCommandSha256,
                     BuildCommandSha256, TestCommandSha256, ToolchainManifestSha256,
                     ContainerImageDigestSha256, SandboxPolicySha256,
                     OfflineFeedManifestSha256, TemplateBundleSha256,
                     SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                     SandboxEvidenceManifestSha256, TestOutcome, TestExitCode, TotalTests,
                     PassedTests, FailedTests, SkippedTests, StartedAtUtc, CompletedAtUtc,
                     EvidenceSha256)
                VALUES
                    (@TestId, @TenantId, @ProjectId, @TechnicalAttemptId, @BindingId, 1,
                     @Baseline, @ObservationId, @Fingerprint, @ProfileId, 1, N'test-profile',
                     1, @DescriptorHash, @RestoreHash, @BuildCommandHash, @TestCommandHash,
                     @ToolchainHash, @ImageHash, @PolicyHash, @FeedHash, @TemplateHash,
                     @SandboxAttemptId, @SandboxManifestId, @SandboxHash, N'Passed', 0,
                     1, 1, 0, 0, @Now, @Now, @TestHash);
                INSERT dbo.CodeIndexSnapshots
                    (Id, TenantId, ProjectId, TechnicalValidationAttemptId, RepositoryBindingId,
                     RepositoryBindingRevision, BaselineCommit, RepositoryStateObservationId,
                     RepositoryFingerprintSha256, ProjectExecutionProfileId,
                     ProjectExecutionProfileRevision, ProfileDefinitionId,
                     ProfileDescriptorRevision, ProfileDescriptorSha256, RestoreCommandSha256,
                     BuildCommandSha256, TestCommandSha256, ToolchainManifestSha256,
                     ContainerImageDigestSha256, SandboxPolicySha256,
                     OfflineFeedManifestSha256, TemplateBundleSha256,
                     SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                     SandboxEvidenceManifestSha256, IndexState, IndexSchemaVersion,
                     IndexerVersion, IndexedFileCount, IndexedChunkCount, SourcesJson,
                     SourcesSha256, IndexContentSha256, StartedAtUtc, CompletedAtUtc,
                     EvidenceSha256)
                VALUES
                    (@IndexId, @TenantId, @ProjectId, @TechnicalAttemptId, @BindingId, 1,
                     @Baseline, @ObservationId, @Fingerprint, @ProfileId, 1, N'test-profile',
                     1, @DescriptorHash, @RestoreHash, @BuildCommandHash, @TestCommandHash,
                     @ToolchainHash, @ImageHash, @PolicyHash, @FeedHash, @TemplateHash,
                     @SandboxAttemptId, @SandboxManifestId, @SandboxHash, N'Ready', 1,
                     N'test-indexer', 0, 0, N'[]', @IndexHash, @IndexHash, @Now, @Now,
                     @IndexHash);
                INSERT dbo.BuilderModelConfigurationRecords
                    (Id, TenantId, ProjectId, TechnicalValidationAttemptId, RepositoryBindingId,
                     RepositoryBindingRevision, BaselineCommit, RepositoryStateObservationId,
                     RepositoryFingerprintSha256, ProjectExecutionProfileId,
                     ProjectExecutionProfileRevision, ProfileDefinitionId,
                     ProfileDescriptorRevision, ProfileDescriptorSha256, RestoreCommandSha256,
                     BuildCommandSha256, TestCommandSha256, ToolchainManifestSha256,
                     ContainerImageDigestSha256, SandboxPolicySha256,
                     OfflineFeedManifestSha256, TemplateBundleSha256,
                     SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                     SandboxEvidenceManifestSha256, ConfigurationState, ConfigurationId,
                     ProviderId, ModelId, ConfigurationRevision, ConfigurationSha256,
                     PolicyVersion, PolicySha256, RecordedAtUtc, EvidenceSha256)
                VALUES
                    (@ModelId, @TenantId, @ProjectId, @TechnicalAttemptId, @BindingId, 1,
                     @Baseline, @ObservationId, @Fingerprint, @ProfileId, 1, N'test-profile',
                     1, @DescriptorHash, @RestoreHash, @BuildCommandHash, @TestCommandHash,
                     @ToolchainHash, @ImageHash, @PolicyHash, @FeedHash, @TemplateHash,
                     @SandboxAttemptId, @SandboxManifestId, @SandboxHash, N'Configured',
                     @ConfigurationId, N'test-provider', N'test-model', 1, @ModelHash,
                     N'test-model-policy', @PolicyHash, @Now, @ModelHash);

                INSERT dbo.ProjectReadinessAssessments
                    (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode, Summary,
                     AssessedByActorUserId, AssessedAtUtc)
                VALUES
                    (@TenantId, @ProjectId, 1, N'Ready', N'RepositoryTechnicalReadinessCurrent',
                     N'Exact technical-readiness evidence is current.', @ActorUserId, @Now);
                DECLARE @AssessmentId BIGINT=CONVERT(BIGINT, SCOPE_IDENTITY());
                INSERT dbo.ProjectTechnicalReadinessEvidence
                    (Id, TenantId, ProjectId, ProjectReadinessAssessmentId,
                     ProjectReadinessRevision, TechnicalValidationAttemptId, RepositoryBindingId,
                     RepositoryBindingRevision, BaselineCommit, RepositoryStateObservationId,
                     RepositoryFingerprintSha256, RepositoryObservationEvidenceSha256,
                     ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                     ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                     RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                     ToolchainManifestSha256, ContainerImageDigestSha256, SandboxPolicySha256,
                     OfflineFeedManifestSha256, TemplateBundleSha256,
                     SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                     SandboxEvidenceManifestSha256, BuildValidationRecordId,
                     BuildValidationEvidenceSha256, TestValidationRecordId,
                     TestValidationEvidenceSha256, CodeIndexSnapshotId, CodeIndexEvidenceSha256,
                     BuilderModelConfigurationRecordId, BuilderModelConfigurationEvidenceSha256,
                     RepositoryBindingQualified, RepositoryCleanAtBaseline,
                     ExecutionProfilePinned, RestorePassed, BuildPassed, TestCommandPassed,
                     CodeIndexCurrent, SandboxQualified, BuilderModelConfigured, GateResultsJson,
                     GateResultsSha256, ExecutionReadiness, ReasonCode, AssessedAtUtc,
                     EvidenceSha256, CreatedAtUtc)
                VALUES
                    (NEWID(), @TenantId, @ProjectId, @AssessmentId, 1, @TechnicalAttemptId,
                     @BindingId, 1, @Baseline, @ObservationId, @Fingerprint, @ObservationHash,
                     @ProfileId, 1, N'test-profile', 1, @DescriptorHash, @RestoreHash,
                     @BuildCommandHash, @TestCommandHash, @ToolchainHash, @ImageHash,
                     @PolicyHash, @FeedHash, @TemplateHash, @SandboxAttemptId,
                     @SandboxManifestId, @SandboxHash, @BuildId, @BuildHash, @TestId,
                     @TestHash, @IndexId, @IndexHash, @ModelId, @ModelHash,
                     1, 1, 1, 1, 1, 1, 1, 1, 1, N'[]', @DescriptorHash, N'Ready',
                     N'RepositoryTechnicalReadinessCurrent', @Now, @TechnicalHash, @Now);

                DECLARE @ExactEvidence NVARCHAR(50)=
                    (SELECT ExecutionReadiness FROM dbo.vw_WorkbenchEffectiveProjectReadiness
                     WHERE TenantId=@TenantId AND ProjectId=@ProjectId);
                DECLARE @ExactEvidenceReason NVARCHAR(100)=
                    (SELECT ReasonCode FROM dbo.vw_WorkbenchEffectiveProjectReadiness
                     WHERE TenantId=@TenantId AND ProjectId=@ProjectId);

                INSERT dbo.SandboxQualificationAttempts
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     RepositoryProvisioningReceiptId, ClientOperationRecordId, ClientOperationId,
                     ActorUserId, ClientOperationKind, ClientOperationResourceScopeId,
                     WorkbenchSessionId, LeaseEpoch, AttemptNumber, ExpectedBindingRevision,
                     ExpectedExecutionProfileRevision, BaselineCommit, SourceManifestSha256,
                     SourceGitTreeId, ProfileDefinitionId, ProfileDescriptorRevision,
                     DescriptorSha256, TemplateBundleSha256, ToolchainManifestId,
                     ContainerImageDigest, OfflineFeedManifestSha256, SandboxPolicyVersion,
                     SandboxPolicySha256, TrustedSupervisorVersion, TrustedSupervisorSha256,
                     State, FailureCode, FailureSummary, CleanupConfirmed, StartedAtUtc, CompletedAtUtc)
                VALUES
                    (NEWID(), @TenantId, @ProjectId, @BindingId, @ProfileId, NEWID(), -3,
                     NEWID(), @ActorUserId, N'QualifyProductionSandbox', N'projection-test',
                     -1, 1, 2, 1, 1, @Baseline, @TemplateHash, @GitTree, N'test-profile', 1,
                     @DescriptorHash, @TemplateHash, N'test-toolchain',
                     N'test@sha256:' + @ImageHash, @FeedHash, N'test-policy', @PolicyHash,
                     N'test-supervisor', @PolicyHash, N'Failed', N'NewerEvidenceFailed',
                     N'The newer qualification invalidates the older ready tuple.', 1, @Now, @Now);
                DECLARE @NewerEvidence NVARCHAR(50)=
                    (SELECT ExecutionReadiness FROM dbo.vw_WorkbenchEffectiveProjectReadiness
                     WHERE TenantId=@TenantId AND ProjectId=@ProjectId);
                DECLARE @NewerEvidenceReason NVARCHAR(100)=
                    (SELECT ReasonCode FROM dbo.vw_WorkbenchEffectiveProjectReadiness
                     WHERE TenantId=@TenantId AND ProjectId=@ProjectId);

                SELECT @ConfiguredWithoutEvidence AS ConfiguredWithoutEvidence,
                       @ConfiguredWithoutEvidenceReason AS ConfiguredWithoutEvidenceReason,
                       @ExactEvidence AS ExactEvidence,
                       @ExactEvidenceReason AS ExactEvidenceReason,
                       @NewerEvidence AS NewerEvidence,
                       @NewerEvidenceReason AS NewerEvidenceReason;
                """,
                transaction: transaction);
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        Assert.AreEqual("ValidationRequired", states.ConfiguredWithoutEvidence);
        Assert.AreEqual("RepositoryObservationRequired", states.ConfiguredWithoutEvidenceReason);
        Assert.AreEqual("Ready", states.ExactEvidence);
        Assert.AreEqual("RepositoryTechnicalReadinessCurrent", states.ExactEvidenceReason);
        Assert.AreEqual("ValidationRequired", states.NewerEvidence);
        Assert.AreEqual("SandboxQualificationRequired", states.NewerEvidenceReason);

        var disabledObjects = await connection.ExecuteScalarAsync<int>(
            """
            SELECT
                (SELECT COUNT(*) FROM sys.foreign_keys
                 WHERE parent_object_id IN
                    (OBJECT_ID(N'dbo.SandboxQualificationAttempts'),
                     OBJECT_ID(N'dbo.SandboxEvidenceManifests'),
                     OBJECT_ID(N'dbo.TechnicalValidationAttempts'),
                     OBJECT_ID(N'dbo.RepositoryStateObservations'),
                     OBJECT_ID(N'dbo.BuildValidationRecords'),
                     OBJECT_ID(N'dbo.TestValidationRecords'),
                     OBJECT_ID(N'dbo.CodeIndexSnapshots'),
                     OBJECT_ID(N'dbo.BuilderModelConfigurationRecords'),
                     OBJECT_ID(N'dbo.ProjectTechnicalReadinessEvidence'))
                   AND is_disabled=1)
              + (SELECT COUNT(*) FROM sys.check_constraints
                 WHERE parent_object_id IN
                    (OBJECT_ID(N'dbo.SandboxQualificationAttempts'),
                     OBJECT_ID(N'dbo.SandboxEvidenceManifests'),
                     OBJECT_ID(N'dbo.TechnicalValidationAttempts'),
                     OBJECT_ID(N'dbo.RepositoryStateObservations'),
                     OBJECT_ID(N'dbo.BuildValidationRecords'),
                     OBJECT_ID(N'dbo.TestValidationRecords'),
                     OBJECT_ID(N'dbo.CodeIndexSnapshots'),
                     OBJECT_ID(N'dbo.BuilderModelConfigurationRecords'),
                     OBJECT_ID(N'dbo.ProjectTechnicalReadinessEvidence'))
                   AND is_disabled=1)
              + (SELECT COUNT(*) FROM sys.triggers
                 WHERE parent_id IN
                    (OBJECT_ID(N'dbo.SandboxQualificationAttempts'),
                     OBJECT_ID(N'dbo.SandboxEvidenceManifests'),
                     OBJECT_ID(N'dbo.TechnicalValidationAttempts'),
                     OBJECT_ID(N'dbo.RepositoryStateObservations'),
                     OBJECT_ID(N'dbo.BuildValidationRecords'),
                     OBJECT_ID(N'dbo.TestValidationRecords'),
                     OBJECT_ID(N'dbo.CodeIndexSnapshots'),
                     OBJECT_ID(N'dbo.BuilderModelConfigurationRecords'),
                     OBJECT_ID(N'dbo.ProjectTechnicalReadinessEvidence'))
                   AND is_disabled=1);
            """);
        Assert.AreEqual(0, disabledObjects, "The rolled-back fixture must leave every authority fence enabled.");
    }

    private sealed class ProjectionStates
    {
        public string ConfiguredWithoutEvidence { get; init; } = string.Empty;
        public string ConfiguredWithoutEvidenceReason { get; init; } = string.Empty;
        public string ExactEvidence { get; init; } = string.Empty;
        public string ExactEvidenceReason { get; init; } = string.Empty;
        public string NewerEvidence { get; init; } = string.Empty;
        public string NewerEvidenceReason { get; init; } = string.Empty;
    }
}
