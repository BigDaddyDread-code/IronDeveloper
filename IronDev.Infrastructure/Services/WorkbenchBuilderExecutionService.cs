using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchBuilderExecutionService(
    IDbConnectionFactory connections,
    IBuilderRepositoryBranchObserver branches,
    IWorkbenchBuilderModelGateway models,
    IWorkbenchBuilderSandboxRunner sandbox,
    TimeProvider timeProvider) : IWorkbenchBuilderExecutionService
{
    public async Task<BuilderExecutionResult> ExecuteAsync(
        ExecuteBuilderAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var expectedInputHash = NormalizeHash(command.ExpectedProviderInputSha256);
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            command.ProjectId,
            command.BuilderAgentRunId,
            expectedProviderInputSha256 = expectedInputHash
        }));

        string repositoryPath;
        using (var read = connections.CreateConnection())
        {
            read.Open();
            repositoryPath = await read.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT binding.CanonicalPath
                FROM dbo.RepositoryBindings binding
                INNER JOIN dbo.ProjectMembers member
                    ON member.TenantId=binding.TenantId AND member.ProjectId=binding.ProjectId
                   AND member.UserId=@ActorUserId AND member.Status=N'Active'
                   AND member.ProjectRole IN (N'Owner', N'Contributor')
                WHERE binding.TenantId=@TenantId AND binding.ProjectId=@ProjectId;
                """, command, cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw new WorkbenchProjectNotAccessibleException();
        }
        var observed = await branches.ObserveAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        var input = await ClaimAsync(
            command, expectedInputHash, payloadHash, observed, cancellationToken).ConfigureAwait(false);
        if (input.Replay is not null)
            return input.Replay with { IsReplay = true };

        IReadOnlyList<BuilderProposedFile>? files = null;
        SandboxExecutionResult? sandboxResult = null;
        string? repairEvidence = null;
        string failureCode = BuilderExecutionFailureCodes.ProviderFailed;
        string failureEvidence = "Builder provider invocation did not complete.";
        var completedAttempt = 0;
        for (var attempt = 1; attempt <= BuilderExecutionContract.MaximumAttempts; attempt++)
        {
            var started = timeProvider.GetUtcNow();
            BuilderProviderResponse? response = null;
            try
            {
                response = await models.InvokeAsync(
                    input.Prepared!, attempt, repairEvidence, cancellationToken).ConfigureAwait(false);
                var expectedSafeRequestId = $"builder-{input.Prepared!.BuilderAgentRunId:N}-{attempt}";
                if (!string.Equals(response.SafeRequestId, expectedSafeRequestId, StringComparison.Ordinal) ||
                    response.DurationMilliseconds < 0 || response.ProviderRequestId?.Length > 200 ||
                    response.ProviderRequestId?.Any(char.IsControl) == true ||
                    response.Usage.InputTokens < 0 || response.Usage.OutputTokens < 0)
                    throw new BuilderOutputValidationException(
                        BuilderExecutionFailureCodes.OutputInvalid,
                        "Builder provider returned invalid invocation metadata.");
                files = BuilderOutputValidator.Validate(response.Output, input.Prepared!.WorkPackageCore);
                sandboxResult = await sandbox.ValidateAsync(
                    new BuilderSandboxValidationRequest(
                        Guid.NewGuid(), input.Prepared.WorkPackageCore, files),
                    cancellationToken).ConfigureAwait(false);
                if (sandboxResult.Status != SandboxExecutionStatus.Succeeded || !sandboxResult.CleanedUp)
                    throw new BuilderOutputValidationException(
                        BuilderExecutionFailureCodes.SandboxFailed,
                        $"Qualified sandbox result: {sandboxResult.ReasonCode}.");
                await RecordAttemptAsync(
                    command, attempt, response, outputValid: true, null, null,
                    started, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                completedAttempt = attempt;
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failureCode = exception switch
                {
                    BuilderOutputValidationException invalid => invalid.FailureCode,
                    BuilderExecutionConflictException conflict => conflict.ReasonCode,
                    SandboxContractValidationException => BuilderExecutionFailureCodes.SandboxFailed,
                    _ => BuilderExecutionFailureCodes.ProviderFailed
                };
                failureEvidence = SafeEvidence(exception.Message);
                repairEvidence = JsonSerializer.Serialize(new
                {
                    priorAttempt = attempt,
                    failureCode,
                    failureEvidence,
                    instruction = "Return a corrected proposal within the unchanged package and file authority."
                });
                await RecordAttemptAsync(
                    command, attempt, response, outputValid: false, failureCode, failureEvidence,
                    started, timeProvider.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);
                files = null;
                sandboxResult = null;
            }
        }

        if (files is null || sandboxResult is null)
            return await FinalizeFailureAsync(
                command, BuilderExecutionContract.MaximumAttempts, failureCode, failureEvidence, cancellationToken).ConfigureAwait(false);

        var finalObservation = await branches.ObserveAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(finalObservation.BranchName, input.Prepared!.WorkPackageCore.BranchName, StringComparison.Ordinal) ||
            !string.Equals(finalObservation.HeadCommit, input.Prepared.WorkPackageCore.BaselineCommit, StringComparison.Ordinal))
            return await FinalizeFailureAsync(
                command, completedAttempt, BuilderExecutionFailureCodes.RepositoryBaselineChanged,
                "The repository baseline changed while the Builder proposal was executing.",
                cancellationToken).ConfigureAwait(false);
        var patch = await BuildPatchAsync(repositoryPath, files, cancellationToken).ConfigureAwait(false);
        return await FinalizeSuccessAsync(
            command, completedAttempt, files, patch, sandboxResult, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ExecutionClaim> ClaimAsync(
        ExecuteBuilderAgentRunCommand command,
        string expectedInputHash,
        string payloadHash,
        BuilderRepositoryBranchObservation observed,
        CancellationToken cancellationToken)
    {
        using var connection = connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
                """
                SELECT run.*, core.CanonicalJson, core.CoreHash,
                       binding.CurrentRevision AS CurrentBindingRevision,
                       binding.BaselineCommit AS CurrentBaselineCommit,
                       readiness.ExecutionReadiness,
                       readiness.TechnicalReadinessEvidenceId AS CurrentTechnicalEvidenceId,
                       model.ConfigurationId AS CurrentBuilderConfigurationId,
                       model.ConfigurationSha256 AS CurrentBuilderConfigurationSha256,
                       evidence.SandboxPolicySha256 AS CurrentSandboxPolicySha256,
                       evidence.ContainerImageDigestSha256 AS CurrentImageDigest
                FROM dbo.BuilderAgentRuns run WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.BuilderWorkPackageCores core
                    ON core.TenantId=run.TenantId AND core.ProjectId=run.ProjectId
                   AND core.Id=run.BuilderWorkPackageCoreId
                INNER JOIN dbo.RepositoryBindings binding WITH (UPDLOCK, HOLDLOCK)
                    ON binding.TenantId=run.TenantId AND binding.ProjectId=run.ProjectId
                LEFT JOIN dbo.vw_WorkbenchEffectiveProjectReadiness readiness
                    ON readiness.TenantId=run.TenantId AND readiness.ProjectId=run.ProjectId
                LEFT JOIN dbo.ProjectTechnicalReadinessEvidence evidence
                    ON evidence.TenantId=readiness.TenantId AND evidence.ProjectId=readiness.ProjectId
                   AND evidence.Id=readiness.TechnicalReadinessEvidenceId
                LEFT JOIN dbo.BuilderModelConfigurationRecords model
                    ON model.TenantId=evidence.TenantId AND model.ProjectId=evidence.ProjectId
                   AND model.Id=evidence.BuilderModelConfigurationRecordId
                WHERE run.TenantId=@TenantId AND run.ProjectId=@ProjectId
                  AND run.Id=@BuilderAgentRunId AND run.ActorUserId=@ActorUserId;
                """, command, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw new BuilderExecutionConflictException(
                    BuilderExecutionFailureCodes.PreparedInputChanged,
                    "The prepared Builder AgentRun does not exist in this exact scope.");
            if (row.ExecutionClientOperationId is not null)
            {
                if (row.ExecutionClientOperationId != command.ClientOperationId ||
                    !string.Equals(row.ExecutionPayloadSha256, payloadHash, StringComparison.Ordinal))
                    throw new BuilderExecutionConflictException(
                        BuilderExecutionFailureCodes.AlreadyInvoked,
                        "This Builder AgentRun was already claimed by another execution operation.");
                if (row.Status is BuilderAgentRunTerminalStates.Succeeded or BuilderAgentRunTerminalStates.Failed)
                {
                    var replay = await ReadResultAsync(connection, transaction, row, cancellationToken)
                        .ConfigureAwait(false);
                    transaction.Commit();
                    return new ExecutionClaim(null, replay);
                }
                throw new BuilderExecutionConflictException(
                    BuilderExecutionFailureCodes.AlreadyInvoked,
                    "This Builder AgentRun invocation is already in progress.");
            }
            if (row.Status != BuilderAgentRunStates.Prepared ||
                !string.Equals(row.ProviderInputSha256, expectedInputHash, StringComparison.Ordinal) ||
                !string.Equals(Hash(row.ProviderInputJson), row.ProviderInputSha256, StringComparison.Ordinal) ||
                !string.Equals(Hash(row.SystemPrompt), row.PromptSha256, StringComparison.Ordinal) ||
                !string.Equals(Hash(row.RoleContextJson), row.RoleContextSha256, StringComparison.Ordinal) ||
                !string.Equals(Hash(row.ToolManifestJson), row.ToolManifestSha256, StringComparison.Ordinal) ||
                !string.Equals(Hash(row.EffectiveProfileJson), row.EffectiveProfileSha256, StringComparison.Ordinal) ||
                !string.Equals(Hash(row.CanonicalJson), row.CoreHash, StringComparison.Ordinal))
                throw new BuilderExecutionConflictException(
                    BuilderExecutionFailureCodes.PreparedInputChanged,
                    "The prepared Builder input or one of its hashes is no longer exact.");
            var core = BuilderWorkPackageCoreCodec.DeserializeCanonical(row.CanonicalJson);
            if (row.CurrentBindingRevision != core.RepositoryBindingRevision ||
                !string.Equals(row.CurrentBaselineCommit, core.BaselineCommit, StringComparison.Ordinal) ||
                !string.Equals(observed.BranchName, core.BranchName, StringComparison.Ordinal) ||
                !string.Equals(observed.HeadCommit, core.BaselineCommit, StringComparison.Ordinal))
                throw Conflict(BuilderExecutionFailureCodes.RepositoryBaselineChanged);
            if (row.ExecutionReadiness != ProjectExecutionReadinessStates.Ready ||
                row.CurrentTechnicalEvidenceId != core.ReadinessAssessment.TechnicalEvidenceId)
                throw Conflict(BuilderPromptPreparationReasonCodes.ReadinessChanged);
            if (row.CurrentBuilderConfigurationId != core.EffectiveProfile.BuilderConfigurationId ||
                row.CurrentBuilderConfigurationSha256 != core.EffectiveProfile.BuilderConfigurationSha256)
                throw Conflict(BuilderExecutionFailureCodes.BuilderProfileChanged);
            if (row.CurrentSandboxPolicySha256 != core.Sandbox.PolicySha256 ||
                row.CurrentImageDigest != core.Sandbox.QualifiedImageDigest)
                throw Conflict(BuilderExecutionFailureCodes.SandboxPolicyChanged);
            var lease = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.WorkbenchWriteLeases SET HeartbeatAtUtc=SYSUTCDATETIME(),
                    ExpiresAtUtc=DATEADD(MINUTE,30,SYSUTCDATETIME())
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch
                  AND HolderActorUserId=@ActorUserId AND RevokedAtUtc IS NULL
                  AND ExpiresAtUtc>SYSUTCDATETIME();
                """, command, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (lease != 1)
                throw new WorkbenchLeaseFenceException();
            var now = timeProvider.GetUtcNow();
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.BuilderAgentRuns
                SET Status=N'Invoking', ExecutionClaimId=@ClaimId,
                    ExecutionClientOperationId=@ClientOperationId,
                    ExecutionPayloadSha256=@PayloadHash,
                    ProviderInvokedAtUtc=@Now, InvocationStartedAtUtc=@Now
                WHERE Id=@BuilderAgentRunId AND Status=N'Prepared'
                  AND ExecutionClaimId IS NULL AND ProviderInvokedAtUtc IS NULL;
                """, new
                {
                    command.BuilderAgentRunId,
                    command.ClientOperationId,
                    PayloadHash = payloadHash,
                    ClaimId = Guid.NewGuid(),
                    Now = now.UtcDateTime
                }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed != 1)
                throw Conflict(BuilderExecutionFailureCodes.AlreadyInvoked);
            transaction.Commit();
            return new ExecutionClaim(new BuilderPreparedExecutionInput(
                row.Id, row.TenantId, row.ProjectId, row.EffectiveProfileJson,
                row.EffectiveProfileSha256, row.SystemPrompt, row.RoleContextJson,
                row.ToolManifestJson, row.ProviderInputSha256, core), null);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task RecordAttemptAsync(
        ExecuteBuilderAgentRunCommand command,
        int attempt,
        BuilderProviderResponse? response,
        bool outputValid,
        string? failureCode,
        string? failureEvidence,
        DateTimeOffset started,
        DateTimeOffset completed,
        CancellationToken cancellationToken)
    {
        using var connection = connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var raw = response?.Output;
        var advanced = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.BuilderAgentRuns SET AttemptCount=@AttemptNumber
            WHERE Id=@BuilderAgentRunId AND Status=N'Invoking' AND AttemptCount=@PriorAttempt;
            """, new
            {
                command.BuilderAgentRunId, AttemptNumber = attempt, PriorAttempt = attempt - 1
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (advanced != 1)
            throw new BuilderExecutionIntegrityException("Builder attempt sequence is no longer exact.");
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.BuilderAgentRunAttempts
                (Id,TenantId,ProjectId,BuilderAgentRunId,AttemptNumber,SafeRequestId,
                 ProviderRequestId,RawOutput,RawOutputSha256,OutputValid,FailureCode,
                 FailureEvidence,StartedAtUtc,CompletedAtUtc)
            VALUES
                (NEWID(),@TenantId,@ProjectId,@BuilderAgentRunId,@AttemptNumber,@SafeRequestId,
                 @ProviderRequestId,@RawOutput,@RawOutputSha256,@OutputValid,@FailureCode,
                 @FailureEvidence,@StartedAtUtc,@CompletedAtUtc);
            """, new
            {
                command.TenantId, command.ProjectId, command.BuilderAgentRunId,
                AttemptNumber = attempt,
                SafeRequestId = response?.SafeRequestId ?? $"builder-{command.BuilderAgentRunId:N}-{attempt}",
                response?.ProviderRequestId, RawOutput = raw,
                RawOutputSha256 = raw is null ? null : Hash(raw),
                OutputValid = outputValid, FailureCode = failureCode,
                FailureEvidence = failureEvidence,
                StartedAtUtc = started.UtcDateTime, CompletedAtUtc = completed.UtcDateTime
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        transaction.Commit();
    }

    private async Task<BuilderExecutionResult> FinalizeSuccessAsync(
        ExecuteBuilderAgentRunCommand command,
        int attemptCount,
        IReadOnlyList<BuilderProposedFile> files,
        string patch,
        SandboxExecutionResult evidence,
        CancellationToken cancellationToken)
    {
        var completed = timeProvider.GetUtcNow();
        var manifest = JsonSerializer.Serialize(files.Select((file, index) => new
        {
            ordinal = index + 1, file.RelativePath, file.ContentSha256,
            utf8ByteLength = Encoding.UTF8.GetByteCount(file.Content)
        }));
        var tools = ToolEvidence(command.BuilderAgentRunId, files, evidence);
        using var connection = connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        for (var index = 0; index < files.Count; index++)
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.BuilderAgentRunProposedFiles
                    (TenantId,ProjectId,BuilderAgentRunId,Ordinal,RelativePath,Content,ContentSha256,Utf8ByteLength)
                VALUES (@TenantId,@ProjectId,@BuilderAgentRunId,@Ordinal,@RelativePath,@Content,@ContentSha256,@Length);
                """, new
                {
                    command.TenantId, command.ProjectId, command.BuilderAgentRunId,
                    Ordinal = index + 1, files[index].RelativePath, files[index].Content,
                    files[index].ContentSha256, Length = Encoding.UTF8.GetByteCount(files[index].Content)
                }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await InsertToolsAsync(connection, transaction, command, tools, cancellationToken).ConfigureAwait(false);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.BuilderAgentRuns SET Status=N'Succeeded', CompletedAtUtc=@CompletedAtUtc,
                RawPatch=@Patch, RawPatchSha256=@PatchHash,
                ChangedFileManifestJson=@Manifest, ChangedFileManifestSha256=@ManifestHash,
                SandboxEvidenceManifestSha256=@EvidenceHash
            WHERE Id=@BuilderAgentRunId AND Status=N'Invoking';
            """, new
            {
                command.BuilderAgentRunId, CompletedAtUtc = completed.UtcDateTime,
                Patch = patch, PatchHash = Hash(patch), Manifest = manifest,
                ManifestHash = Hash(manifest), EvidenceHash = evidence.EvidenceManifestSha256
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (changed != 1)
            throw new BuilderExecutionIntegrityException("Builder success evidence did not finalize atomically.");
        transaction.Commit();
        return Result(command.BuilderAgentRunId, BuilderAgentRunTerminalStates.Succeeded,
            attemptCount, files, patch, tools, evidence.EvidenceManifestSha256, null, null, completed);
    }

    private async Task<BuilderExecutionResult> FinalizeFailureAsync(
        ExecuteBuilderAgentRunCommand command,
        int attemptCount,
        string failureCode,
        string failureEvidence,
        CancellationToken cancellationToken)
    {
        var completed = timeProvider.GetUtcNow();
        using var connection = connections.CreateConnection();
        connection.Open();
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.BuilderAgentRuns SET Status=N'Failed', CompletedAtUtc=@CompletedAtUtc,
                FailureCode=@FailureCode, FailureEvidence=@FailureEvidence
            WHERE Id=@BuilderAgentRunId AND Status=N'Invoking';
            """, new
            {
                command.BuilderAgentRunId, CompletedAtUtc = completed.UtcDateTime,
                FailureCode = failureCode, FailureEvidence = SafeEvidence(failureEvidence)
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (changed != 1)
            throw new BuilderExecutionIntegrityException("Builder failure evidence did not finalize atomically.");
        return Result(command.BuilderAgentRunId, BuilderAgentRunTerminalStates.Failed,
            attemptCount, [], string.Empty, [], null, failureCode, SafeEvidence(failureEvidence), completed);
    }

    private static async Task<BuilderExecutionResult> ReadResultAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RunRow row,
        CancellationToken cancellationToken)
    {
        var files = (await connection.QueryAsync<BuilderProposedFile>(new CommandDefinition(
            """
            SELECT RelativePath,Content,ContentSha256 FROM dbo.BuilderAgentRunProposedFiles
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND BuilderAgentRunId=@Id ORDER BY Ordinal;
            """, row, transaction, cancellationToken: cancellationToken))).ToArray();
        var tools = (await connection.QueryAsync<BuilderToolCallEvidence>(new CommandDefinition(
            """
            SELECT Ordinal,ToolName,ToolVersion,InputSha256,OutputSha256,Status
            FROM dbo.BuilderAgentRunToolCalls WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND BuilderAgentRunId=@Id ORDER BY Ordinal;
            """, row, transaction, cancellationToken: cancellationToken))).ToArray();
        return Result(row.Id, row.Status, row.AttemptCount, files, row.RawPatch ?? string.Empty, tools,
            row.SandboxEvidenceManifestSha256, row.FailureCode, row.FailureEvidence,
            new DateTimeOffset(DateTime.SpecifyKind(row.CompletedAtUtc!.Value, DateTimeKind.Utc)));
    }

    private static async Task InsertToolsAsync(
        IDbConnection connection, IDbTransaction transaction,
        ExecuteBuilderAgentRunCommand command,
        IReadOnlyList<BuilderToolCallEvidence> tools,
        CancellationToken cancellationToken)
    {
        foreach (var tool in tools)
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.BuilderAgentRunToolCalls
                    (TenantId,ProjectId,BuilderAgentRunId,Ordinal,ToolName,ToolVersion,InputSha256,OutputSha256,Status)
                VALUES (@TenantId,@ProjectId,@BuilderAgentRunId,@Ordinal,@ToolName,@ToolVersion,@InputSha256,@OutputSha256,@Status);
                """, new { command.TenantId, command.ProjectId, command.BuilderAgentRunId,
                    tool.Ordinal, tool.ToolName, tool.ToolVersion, tool.InputSha256, tool.OutputSha256, tool.Status },
                transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static IReadOnlyList<BuilderToolCallEvidence> ToolEvidence(
        Guid runId, IReadOnlyList<BuilderProposedFile> files, SandboxExecutionResult sandbox) =>
    [
        new(1, "builder.sandbox.files.read", "v1", Hash(runId.ToString("D")),
            Hash(string.Join("\n", files.Select(file => file.RelativePath))), "Completed"),
        new(2, "builder.sandbox.files.propose", "v1",
            Hash(string.Join("\n", files.Select(file => file.RelativePath))),
            Hash(JsonSerializer.Serialize(files)), "Completed"),
        new(3, "builder.sandbox.process.run", "v1", Hash(JsonSerializer.Serialize(files)),
            sandbox.EvidenceManifestSha256, "Completed")
    ];

    private static BuilderExecutionResult Result(
        Guid runId, string status, int attemptCount, IReadOnlyList<BuilderProposedFile> files, string patch,
        IReadOnlyList<BuilderToolCallEvidence> tools, string? evidenceHash,
        string? failureCode, string? failureEvidence, DateTimeOffset completed) => new()
    {
        BuilderAgentRunId = runId, Status = status,
        AttemptCount = attemptCount,
        ProposedFiles = files,
        ChangedFiles = files.Select(file => new BuilderChangedFile(
            file.RelativePath, file.ContentSha256, Encoding.UTF8.GetByteCount(file.Content))).ToArray(),
        RawPatch = patch, RawPatchSha256 = Hash(patch), ToolCalls = tools,
        SandboxEvidenceManifestSha256 = evidenceHash,
        FailureCode = failureCode, FailureEvidence = failureEvidence,
        CompletedAtUtc = completed
    };

    private static async Task<string> BuildPatchAsync(
        string repositoryPath,
        IReadOnlyList<BuilderProposedFile> files,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var root = Path.GetFullPath(repositoryPath).TrimEnd('\\', '/');
        foreach (var file in files)
        {
            var path = Path.GetFullPath(Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new BuilderExecutionIntegrityException("Patch source escaped the repository read boundary.");
            var before = File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : string.Empty;
            var beforeLines = Lines(before);
            var afterLines = Lines(file.Content);
            builder.AppendLine($"diff --git a/{file.RelativePath} b/{file.RelativePath}");
            builder.AppendLine($"--- a/{file.RelativePath}");
            builder.AppendLine($"+++ b/{file.RelativePath}");
            builder.AppendLine($"@@ -{(beforeLines.Length == 0 ? 0 : 1)},{beforeLines.Length} +{(afterLines.Length == 0 ? 0 : 1)},{afterLines.Length} @@");
            foreach (var line in beforeLines)
                builder.Append('-').AppendLine(line);
            foreach (var line in afterLines)
                builder.Append('+').AppendLine(line);
        }
        return builder.ToString();
    }

    private static string[] Lines(string value)
    {
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        return normalized.Length == 0 ? [] : normalized.TrimEnd('\n').Split('\n');
    }

    private static void Validate(ExecuteBuilderAgentRunCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.BuilderAgentRunId == Guid.Empty)
            throw new BuilderExecutionValidationException("Exact actor, project, fence, operation, and Builder run are required.");
    }

    private static string NormalizeHash(string value)
    {
        var hash = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (hash.StartsWith("sha256:", StringComparison.Ordinal)) hash = hash[7..];
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new BuilderExecutionValidationException("ExpectedProviderInputSha256 must be SHA-256.");
        return hash;
    }

    private static string SafeEvidence(string value) =>
        string.IsNullOrWhiteSpace(value) ? "No safe failure detail was available."
        : value.Replace('\r', ' ').Replace('\n', ' ').Trim()[..Math.Min(2000, value.Replace('\r', ' ').Replace('\n', ' ').Trim().Length)];

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
    private static BuilderExecutionConflictException Conflict(string code) =>
        new(code, "The exact prepared Builder authority is no longer current.");

    private sealed record ExecutionClaim(BuilderPreparedExecutionInput? Prepared, BuilderExecutionResult? Replay);
    private sealed record RunRow
    {
        public Guid Id { get; init; }
        public int TenantId { get; init; }
        public int ProjectId { get; init; }
        public string Status { get; init; } = "";
        public Guid? ExecutionClientOperationId { get; init; }
        public string? ExecutionPayloadSha256 { get; init; }
        public string ProviderInputJson { get; init; } = "";
        public string ProviderInputSha256 { get; init; } = "";
        public string SystemPrompt { get; init; } = "";
        public string PromptSha256 { get; init; } = "";
        public string RoleContextJson { get; init; } = "";
        public string RoleContextSha256 { get; init; } = "";
        public string ToolManifestJson { get; init; } = "";
        public string ToolManifestSha256 { get; init; } = "";
        public string EffectiveProfileJson { get; init; } = "";
        public string EffectiveProfileSha256 { get; init; } = "";
        public string CanonicalJson { get; init; } = "";
        public string CoreHash { get; init; } = "";
        public long CurrentBindingRevision { get; init; }
        public string? CurrentBaselineCommit { get; init; }
        public string? ExecutionReadiness { get; init; }
        public Guid? CurrentTechnicalEvidenceId { get; init; }
        public Guid? CurrentBuilderConfigurationId { get; init; }
        public string? CurrentBuilderConfigurationSha256 { get; init; }
        public string? CurrentSandboxPolicySha256 { get; init; }
        public string? CurrentImageDigest { get; init; }
        public int AttemptCount { get; init; }
        public string? RawPatch { get; init; }
        public string? SandboxEvidenceManifestSha256 { get; init; }
        public string? FailureCode { get; init; }
        public string? FailureEvidence { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
    }
}
