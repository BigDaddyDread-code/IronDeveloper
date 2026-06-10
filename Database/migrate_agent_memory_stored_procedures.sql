-- =====================================================================
-- migrate_agent_memory_stored_procedures.sql
--
-- Approved stored-procedure write surface for governed agent memory.
-- Runtime services must call these procedures instead of direct table writes.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_AgentLocalMemory_Create
    @MemoryItemId NVARCHAR(80), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80), @AgentId NVARCHAR(120),
    @MemoryType INT, @AuthorityLevel INT, @Title NVARCHAR(300), @Summary NVARCHAR(MAX), @Confidence DECIMAL(5,4), @CreatedAtUtc DATETIME2(7),
    @CreatedByAgentId NVARCHAR(120), @EvidenceRefsJson NVARCHAR(MAX), @MemoryJson NVARCHAR(MAX) = NULL, @ExpiresAtUtc DATETIME2(7) = NULL,
    @WorkflowId NVARCHAR(120) = NULL, @TicketId NVARCHAR(120) = NULL, @CorrelationId NVARCHAR(120) = NULL, @ThoughtLedgerEntryId NVARCHAR(120) = NULL,
    @SupersedesMemoryItemId NVARCHAR(80) = NULL, @KnownLimitations NVARCHAR(MAX) = NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@MemoryItemId, N''''))), N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@TenantId, N''''))), N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@ProjectId, N''''))), N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@CampaignId, N''''))), N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@RunId, N''''))), N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@AgentId, N''''))), N'''') IS NULL THROW 52001, ''Agent local memory create requires complete scope and memory ID.'', 1;
    IF @CreatedByAgentId IS NULL OR @CreatedByAgentId <> @AgentId THROW 52002, ''Agent local memory create actor must match scoped agent.'', 1;
    IF @AuthorityLevel NOT IN (1, 2) THROW 52003, ''Agent local memory create supports only local memory authority.'', 1;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@Title, N''''))), N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@Summary, N''''))), N'''') IS NULL THROW 52004, ''Agent local memory create requires title and summary.'', 1;
    IF @EvidenceRefsJson IS NULL OR ISJSON(@EvidenceRefsJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson)) THROW 52005, ''Agent local memory create requires evidence refs JSON.'', 1;
    IF EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson) WITH (EvidenceId NVARCHAR(120) ''$.evidenceId'', EvidenceType INT ''$.evidenceType'', SourceId NVARCHAR(160) ''$.sourceId'') e WHERE NULLIF(LTRIM(RTRIM(ISNULL(e.EvidenceId, N''''))), N'''') IS NULL OR e.EvidenceType NOT BETWEEN 1 AND 12 OR NULLIF(LTRIM(RTRIM(ISNULL(e.SourceId, N''''))), N'''') IS NULL) THROW 52006, ''Agent local memory evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
    IF @MemoryJson IS NOT NULL AND ISJSON(@MemoryJson) <> 1 THROW 52007, ''Agent local memory JSON must be valid JSON.'', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT INTO agent.AgentLocalMemoryItem (MemoryItemId,TenantId,ProjectId,CampaignId,RunId,AgentId,MemoryType,AuthorityLevel,Title,Summary,Confidence,CreatedAtUtc,ExpiresAtUtc,SupersedesMemoryItemId,KnownLimitations,ContentJson,ContentHashSha256)
        VALUES (@MemoryItemId,@TenantId,@ProjectId,@CampaignId,@RunId,@AgentId,@MemoryType,@AuthorityLevel,@Title,@Summary,@Confidence,@CreatedAtUtc,@ExpiresAtUtc,@SupersedesMemoryItemId,@KnownLimitations,@MemoryJson,NULL);
        INSERT INTO agent.AgentLocalMemoryEvidenceRef (MemoryItemId,EvidenceId,EvidenceType,SourceId,SourceUri,Summary,CapturedAtUtc)
        SELECT @MemoryItemId, e.EvidenceId, e.EvidenceType, e.SourceId, e.SourceUri, e.Summary, e.CapturedAtUtc FROM OPENJSON(@EvidenceRefsJson) WITH (EvidenceId NVARCHAR(120) ''$.evidenceId'', EvidenceType INT ''$.evidenceType'', SourceId NVARCHAR(160) ''$.sourceId'', SourceUri NVARCHAR(1024) ''$.sourceUri'', Summary NVARCHAR(MAX) ''$.summary'', CapturedAtUtc DATETIME2(7) ''$.capturedAt'') e;
        INSERT INTO agent.AgentLocalMemoryEvent (MemoryEventId,MemoryItemId,EventType,EventReason,CreatedAtUtc,CreatedByAgentId,CreatedByUserId,CorrelationId,DecisionId,ThoughtLedgerEntryId,EventJson)
        VALUES (CONCAT(N''memevt-created-'', REPLACE(CONVERT(NVARCHAR(36), NEWID()), N''-'', N'''')), @MemoryItemId, 1, N''Memory item created.'', @CreatedAtUtc, @CreatedByAgentId, NULL, @CorrelationId, NULL, @ThoughtLedgerEntryId, NULL);
        COMMIT TRANSACTION;
    END TRY BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; THROW; END CATCH
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_AgentLocalMemory_AddEvent
    @MemoryEventId NVARCHAR(80), @MemoryItemId NVARCHAR(80), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80), @AgentId NVARCHAR(120),
    @EventType INT, @EventReason NVARCHAR(MAX), @CreatedAtUtc DATETIME2(7), @CreatedByAgentId NVARCHAR(120) = NULL, @CreatedByUserId NVARCHAR(120) = NULL, @DecisionId NVARCHAR(120) = NULL, @ThoughtLedgerEntryId NVARCHAR(120) = NULL, @CorrelationId NVARCHAR(120) = NULL, @EventJson NVARCHAR(MAX) = NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @EventType = 1 OR @EventType NOT IN (2,3,4,5,6,7) THROW 52011, ''Agent local memory add event does not allow Created or unknown event types.'', 1;
    IF @CreatedByAgentId IS NULL AND @CreatedByUserId IS NULL THROW 52012, ''Agent local memory event requires an actor.'', 1;
    IF @EventJson IS NOT NULL AND ISJSON(@EventJson) <> 1 THROW 52013, ''Agent local memory event JSON must be valid JSON.'', 1;
    DECLARE @CurrentEventType INT;
    SELECT TOP (1) @CurrentEventType = ISNULL(v.CurrentEventType, 1) FROM agent.AgentLocalMemoryItem i LEFT JOIN agent.vwAgentLocalMemoryCurrentState v ON v.MemoryItemId = i.MemoryItemId WHERE i.MemoryItemId=@MemoryItemId AND i.TenantId=@TenantId AND i.ProjectId=@ProjectId AND i.CampaignId=@CampaignId AND i.RunId=@RunId AND i.AgentId=@AgentId;
    IF @CurrentEventType IS NULL THROW 52014, ''Agent local memory event target does not exist in scope.'', 1;
    IF @CurrentEventType IN (2,4,6,7) THROW 52015, ''Agent local memory terminal state cannot receive new events.'', 1;
    IF @CurrentEventType = 5 AND @EventType NOT IN (3,4) THROW 52016, ''Agent local memory proposed state can only expire or invalidate.'', 1;
    IF @CurrentEventType = 3 AND @EventType <> 4 THROW 52017, ''Agent local memory expired state can only invalidate.'', 1;
    INSERT INTO agent.AgentLocalMemoryEvent (MemoryEventId,MemoryItemId,EventType,EventReason,CreatedAtUtc,CreatedByAgentId,CreatedByUserId,CorrelationId,DecisionId,ThoughtLedgerEntryId,EventJson) VALUES (@MemoryEventId,@MemoryItemId,@EventType,@EventReason,@CreatedAtUtc,@CreatedByAgentId,@CreatedByUserId,@CorrelationId,@DecisionId,@ThoughtLedgerEntryId,@EventJson);
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_AgentMemoryInfluence_Create
    @InfluenceId NVARCHAR(80), @MemoryItemId NVARCHAR(80), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80), @AgentId NVARCHAR(120), @DecisionId NVARCHAR(120), @InfluenceType INT, @InfluenceSummary NVARCHAR(MAX), @EvidenceRefsJson NVARCHAR(MAX), @Confidence DECIMAL(5,4), @CreatedAtUtc DATETIME2(7), @ThoughtLedgerEntryId NVARCHAR(120)=NULL, @CorrelationId NVARCHAR(120)=NULL, @InfluenceJson NVARCHAR(MAX)=NULL, @AffectedArtifactType NVARCHAR(80)=NULL, @AffectedArtifactId NVARCHAR(160)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@InfluenceId,N''''))),N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@MemoryItemId,N''''))),N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@DecisionId,N''''))),N'''') IS NULL THROW 52020, ''Memory influence create requires IDs and decision ID.'', 1;
    IF @InfluenceType NOT BETWEEN 1 AND 7 OR @Confidence < 0 OR @Confidence > 1 THROW 52022, ''Memory influence type or confidence is invalid.'', 1;
    IF @EvidenceRefsJson IS NULL OR ISJSON(@EvidenceRefsJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson)) THROW 52023, ''Memory influence requires evidence refs JSON.'', 1;
    IF EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson) WITH (EvidenceId NVARCHAR(120) ''$.evidenceId'', EvidenceType INT ''$.evidenceType'', SourceId NVARCHAR(160) ''$.sourceId'') e WHERE NULLIF(LTRIM(RTRIM(ISNULL(e.EvidenceId, N''''))), N'''') IS NULL OR e.EvidenceType NOT BETWEEN 1 AND 12 OR NULLIF(LTRIM(RTRIM(ISNULL(e.SourceId, N''''))), N'''') IS NULL) THROW 52024, ''Memory influence evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
    IF @InfluenceJson IS NOT NULL AND ISJSON(@InfluenceJson) <> 1 THROW 52025, ''Memory influence JSON must be valid JSON.'', 1;
    INSERT INTO agent.AgentMemoryInfluenceRecord (InfluenceId,TenantId,ProjectId,CampaignId,RunId,AgentId,MemoryItemId,DecisionId,InfluenceType,InfluenceSummary,Confidence,MemoryAuthorityLevelAtInfluence,MemoryLifecycleStatusAtInfluence,AffectedArtifactType,AffectedArtifactId,EvidenceRefsJson,CreatedAtUtc,ThoughtLedgerEntryId,CorrelationId,InfluenceJson,ContentHashSha256)
    SELECT @InfluenceId,@TenantId,@ProjectId,@CampaignId,@RunId,@AgentId,@MemoryItemId,@DecisionId,@InfluenceType,@InfluenceSummary,@Confidence,s.AuthorityLevel,CASE WHEN s.ExpiresAtUtc IS NOT NULL AND s.ExpiresAtUtc <= SYSUTCDATETIME() THEN 3 ELSE ISNULL(s.CurrentEventType,1) END,@AffectedArtifactType,@AffectedArtifactId,@EvidenceRefsJson,@CreatedAtUtc,@ThoughtLedgerEntryId,@CorrelationId,@InfluenceJson,NULL
    FROM agent.vwAgentLocalMemoryCurrentState s WHERE s.MemoryItemId=@MemoryItemId AND s.TenantId=@TenantId AND s.ProjectId=@ProjectId AND s.CampaignId=@CampaignId AND s.RunId=@RunId AND s.AgentId=@AgentId AND ISNULL(s.CurrentEventType,1) NOT IN (2,3,4,6,7) AND (s.ExpiresAtUtc IS NULL OR s.ExpiresAtUtc > SYSUTCDATETIME());
    IF @@ROWCOUNT <> 1 THROW 52026, ''Memory influence must reference active memory in the bound scope.'', 1;
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_AgentMemoryHandoff_Create
    @HandoffMemorySliceId NVARCHAR(80), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80), @SourceAgentId NVARCHAR(120), @TargetAgentId NVARCHAR(120), @MemoryItemIdsJson NVARCHAR(MAX), @MemorySnapshotsJson NVARCHAR(MAX), @Summary NVARCHAR(MAX), @AllowedUse INT, @EvidenceRefsJson NVARCHAR(MAX), @Confidence DECIMAL(5,4), @CreatedAtUtc DATETIME2(7), @InfluenceIdsJson NVARCHAR(MAX)=NULL, @DecisionId NVARCHAR(120)=NULL, @ThoughtLedgerEntryId NVARCHAR(120)=NULL, @CorrelationId NVARCHAR(120)=NULL, @HandoffJson NVARCHAR(MAX)=NULL, @ExpiresAtUtc DATETIME2(7)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @SourceAgentId = @TargetAgentId THROW 52031, ''Handoff source and target agents must be different.'', 1;
    IF @AllowedUse NOT BETWEEN 1 AND 4 OR @Confidence < 0 OR @Confidence > 1 THROW 52032, ''Handoff allowed use or confidence is invalid.'', 1;
    IF @MemoryItemIdsJson IS NULL OR ISJSON(@MemoryItemIdsJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@MemoryItemIdsJson)) THROW 52033, ''Handoff requires memory item IDs JSON.'', 1;
    IF @MemorySnapshotsJson IS NULL OR ISJSON(@MemorySnapshotsJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@MemorySnapshotsJson)) THROW 52034, ''Handoff requires memory snapshots JSON.'', 1;
    IF @EvidenceRefsJson IS NULL OR ISJSON(@EvidenceRefsJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson)) THROW 52035, ''Handoff requires evidence refs JSON.'', 1;
    IF EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson) WITH (EvidenceId NVARCHAR(120) ''$.evidenceId'', EvidenceType INT ''$.evidenceType'', SourceId NVARCHAR(160) ''$.sourceId'') e WHERE NULLIF(LTRIM(RTRIM(ISNULL(e.EvidenceId, N''''))), N'''') IS NULL OR e.EvidenceType NOT BETWEEN 1 AND 12 OR NULLIF(LTRIM(RTRIM(ISNULL(e.SourceId, N''''))), N'''') IS NULL) THROW 52038, ''Handoff evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
    IF @InfluenceIdsJson IS NOT NULL AND ISJSON(@InfluenceIdsJson) <> 1 THROW 52036, ''Handoff influence IDs JSON must be valid JSON.'', 1;
    IF @HandoffJson IS NOT NULL AND ISJSON(@HandoffJson) <> 1 THROW 52037, ''Handoff JSON must be valid JSON.'', 1;
    INSERT INTO agent.AgentMemoryHandoffSlice (HandoffMemorySliceId,TenantId,ProjectId,CampaignId,RunId,SourceAgentId,TargetAgentId,MemoryItemIdsJson,MemorySnapshotsJson,Summary,AllowedUse,EvidenceRefsJson,Confidence,InfluenceIdsJson,DecisionId,ThoughtLedgerEntryId,CorrelationId,CreatedAtUtc,ExpiresAtUtc,HandoffJson,ContentHashSha256)
    VALUES (@HandoffMemorySliceId,@TenantId,@ProjectId,@CampaignId,@RunId,@SourceAgentId,@TargetAgentId,@MemoryItemIdsJson,@MemorySnapshotsJson,@Summary,@AllowedUse,@EvidenceRefsJson,@Confidence,@InfluenceIdsJson,@DecisionId,@ThoughtLedgerEntryId,@CorrelationId,@CreatedAtUtc,@ExpiresAtUtc,@HandoffJson,NULL);
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_MemoryImprovementProposal_Create
    @ProposalId NVARCHAR(80), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80), @AgentId NVARCHAR(120), @ProposalType INT, @Title NVARCHAR(300), @Summary NVARCHAR(MAX), @SourcesJson NVARCHAR(MAX), @EvidenceRefsJson NVARCHAR(MAX), @Confidence DECIMAL(5,4), @ProposedByAgentId NVARCHAR(120)=NULL, @ProposedByUserId NVARCHAR(120)=NULL, @CreatedAtUtc DATETIME2(7), @ThoughtLedgerEntryId NVARCHAR(120)=NULL, @CorrelationId NVARCHAR(120)=NULL, @ProposalJson NVARCHAR(MAX)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @ProposalType NOT BETWEEN 1 AND 8 OR @Confidence < 0 OR @Confidence > 1 THROW 52041, ''Memory improvement proposal type or confidence is invalid.'', 1;
    IF @ProposedByAgentId IS NULL AND @ProposedByUserId IS NULL THROW 52043, ''Memory improvement proposal requires a proposer.'', 1;
    IF @ProposedByAgentId IS NOT NULL AND @ProposedByAgentId <> @AgentId THROW 52044, ''Agent-created memory improvement proposal must be created by scoped agent.'', 1;
    IF @SourcesJson IS NULL OR ISJSON(@SourcesJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@SourcesJson)) THROW 52045, ''Memory improvement proposal requires source JSON.'', 1;
    IF @EvidenceRefsJson IS NULL OR ISJSON(@EvidenceRefsJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson)) THROW 52046, ''Memory improvement proposal requires evidence JSON.'', 1;
    IF EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson) WITH (EvidenceId NVARCHAR(120) ''$.evidenceId'', EvidenceType INT ''$.evidenceType'', SourceId NVARCHAR(160) ''$.sourceId'') e WHERE NULLIF(LTRIM(RTRIM(ISNULL(e.EvidenceId, N''''))), N'''') IS NULL OR e.EvidenceType NOT BETWEEN 1 AND 12 OR NULLIF(LTRIM(RTRIM(ISNULL(e.SourceId, N''''))), N'''') IS NULL) THROW 52048, ''Memory improvement proposal evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
    IF @ProposalJson IS NOT NULL AND (ISJSON(@ProposalJson) <> 1 OR @ProposalJson LIKE ''%RawPrompt%'' OR @ProposalJson LIKE ''%RawCompletion%'' OR @ProposalJson LIKE ''%ChainOfThought%'' OR @ProposalJson LIKE ''%Scratchpad%'' OR @ProposalJson LIKE ''%PrivateReasoning%'') THROW 52047, ''Memory improvement proposal JSON is invalid or contains private reasoning markers.'', 1;
    BEGIN TRY BEGIN TRANSACTION;
    INSERT INTO agent.AgentMemoryImprovementProposal (ProposalId,TenantId,ProjectId,CampaignId,RunId,AgentId,ProposalType,Title,Summary,SourcesJson,EvidenceRefsJson,Confidence,ProposedByAgentId,ProposedByUserId,CreatedAtUtc,ThoughtLedgerEntryId,CorrelationId,ProposalJson,ContentHashSha256)
    VALUES (@ProposalId,@TenantId,@ProjectId,@CampaignId,@RunId,@AgentId,@ProposalType,@Title,@Summary,@SourcesJson,@EvidenceRefsJson,@Confidence,@ProposedByAgentId,@ProposedByUserId,@CreatedAtUtc,@ThoughtLedgerEntryId,@CorrelationId,@ProposalJson,NULL);
    INSERT INTO agent.AgentMemoryImprovementProposalEvent (ProposalEventId,ProposalId,EventType,Reason,CreatedAtUtc,CreatedByUserId,CreatedByAgentId,ThoughtLedgerEntryId,CorrelationId,EventJson)
    VALUES (CONCAT(N''proposal-submitted-'', REPLACE(CONVERT(NVARCHAR(36), NEWID()), N''-'', N'''')),@ProposalId,1,N''Memory improvement proposal submitted.'',@CreatedAtUtc,@ProposedByUserId,@ProposedByAgentId,@ThoughtLedgerEntryId,@CorrelationId,NULL);
    COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; THROW; END CATCH
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_MemoryImprovementProposal_AddEvent
    @ProposalEventId NVARCHAR(80), @ProposalId NVARCHAR(80), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80), @AgentId NVARCHAR(120), @EventType INT, @Reason NVARCHAR(MAX)=NULL, @CreatedAtUtc DATETIME2(7), @CreatedByUserId NVARCHAR(120)=NULL, @CreatedByAgentId NVARCHAR(120)=NULL, @ThoughtLedgerEntryId NVARCHAR(120)=NULL, @CorrelationId NVARCHAR(120)=NULL, @EventJson NVARCHAR(MAX)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @EventType = 1 OR @EventType NOT BETWEEN 2 AND 5 THROW 52051, ''Memory improvement proposal add event does not allow Submitted or unknown event types.'', 1;
    IF @CreatedByUserId IS NULL AND @CreatedByAgentId IS NULL THROW 52052, ''Memory improvement proposal event requires an actor.'', 1;
    IF @EventJson IS NOT NULL AND (ISJSON(@EventJson) <> 1 OR @EventJson LIKE ''%RawPrompt%'' OR @EventJson LIKE ''%RawCompletion%'' OR @EventJson LIKE ''%ChainOfThought%'' OR @EventJson LIKE ''%Scratchpad%'' OR @EventJson LIKE ''%PrivateReasoning%'') THROW 52053, ''Memory improvement proposal event JSON is invalid or contains private reasoning markers.'', 1;
    DECLARE @CurrentStatus INT;
    SELECT TOP (1) @CurrentStatus=e.EventType FROM agent.AgentMemoryImprovementProposal p INNER JOIN agent.AgentMemoryImprovementProposalEvent e ON e.ProposalId=p.ProposalId WHERE p.ProposalId=@ProposalId AND p.TenantId=@TenantId AND p.ProjectId=@ProjectId AND p.CampaignId=@CampaignId AND p.RunId=@RunId AND p.AgentId=@AgentId ORDER BY e.CreatedAtUtc DESC, e.ProposalEventId DESC;
    IF @CurrentStatus IS NULL THROW 52054, ''Memory improvement proposal does not exist in scope.'', 1;
    IF @CurrentStatus <> 1 THROW 52055, ''Terminal memory improvement proposal cannot receive new events.'', 1;
    INSERT INTO agent.AgentMemoryImprovementProposalEvent (ProposalEventId,ProposalId,EventType,Reason,CreatedAtUtc,CreatedByUserId,CreatedByAgentId,ThoughtLedgerEntryId,CorrelationId,EventJson) VALUES (@ProposalEventId,@ProposalId,@EventType,@Reason,@CreatedAtUtc,@CreatedByUserId,@CreatedByAgentId,@ThoughtLedgerEntryId,@CorrelationId,@EventJson);
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_MemoryIndexQueue_Create
    @IndexRecordId NVARCHAR(100), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80)=NULL, @AgentId NVARCHAR(120)=NULL, @ArtifactType INT, @ArtifactId NVARCHAR(120), @AuthorityLevel INT, @Title NVARCHAR(300), @Summary NVARCHAR(MAX), @EvidenceRefsJson NVARCHAR(MAX), @MetadataJson NVARCHAR(MAX)=NULL, @SourceHashSha256 NVARCHAR(64)=NULL, @DecisionId NVARCHAR(120)=NULL, @ThoughtLedgerEntryId NVARCHAR(120)=NULL, @CorrelationId NVARCHAR(120)=NULL, @CreatedAtUtc DATETIME2(7)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @ArtifactType NOT BETWEEN 1 AND 6 OR @AuthorityLevel NOT BETWEEN 1 AND 5 THROW 52061, ''Memory index queue artifact type or authority is invalid.'', 1;
    IF @EvidenceRefsJson IS NULL OR ISJSON(@EvidenceRefsJson) <> 1 OR NOT EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson)) THROW 52063, ''Memory index queue requires evidence refs JSON.'', 1;
    IF EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson) WITH (EvidenceId NVARCHAR(120) ''$.evidenceId'', EvidenceType INT ''$.evidenceType'', SourceId NVARCHAR(160) ''$.sourceId'') e WHERE NULLIF(LTRIM(RTRIM(ISNULL(e.EvidenceId, N''''))), N'''') IS NULL OR e.EvidenceType NOT BETWEEN 1 AND 12 OR NULLIF(LTRIM(RTRIM(ISNULL(e.SourceId, N''''))), N'''') IS NULL) THROW 52066, ''Memory index queue evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
    IF @MetadataJson IS NOT NULL AND ISJSON(@MetadataJson) <> 1 THROW 52064, ''Memory index queue metadata JSON must be valid JSON.'', 1;
    IF @SourceHashSha256 IS NOT NULL AND (LEN(@SourceHashSha256) <> 64 OR @SourceHashSha256 LIKE ''%[^0-9A-Fa-f]%'') THROW 52065, ''Memory index queue source hash must be 64 hex characters.'', 1;
    BEGIN TRY BEGIN TRANSACTION;
    INSERT INTO agent.AgentMemoryIndexQueue (IndexRecordId,TenantId,ProjectId,CampaignId,RunId,AgentId,ArtifactType,ArtifactId,AuthorityLevel,Title,Summary,EvidenceRefsJson,MetadataJson,SourceHashSha256,DecisionId,ThoughtLedgerEntryId,CorrelationId,CreatedAtUtc)
    VALUES (@IndexRecordId,@TenantId,@ProjectId,@CampaignId,@RunId,@AgentId,@ArtifactType,@ArtifactId,@AuthorityLevel,@Title,@Summary,@EvidenceRefsJson,@MetadataJson,@SourceHashSha256,@DecisionId,@ThoughtLedgerEntryId,@CorrelationId,@CreatedAtUtc);
    INSERT INTO agent.AgentMemoryIndexEvent (IndexEventId,IndexRecordId,EventType,WeaviateObjectId,Error,CreatedAtUtc) VALUES (CONCAT(N''index-event-'', REPLACE(CONVERT(NVARCHAR(36), NEWID()), N''-'', N'''')),@IndexRecordId,1,NULL,NULL,@CreatedAtUtc);
    COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; THROW; END CATCH
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_MemoryIndexEvent_Add
    @IndexEventId NVARCHAR(100), @IndexRecordId NVARCHAR(100), @EventType INT, @WeaviateObjectId NVARCHAR(160)=NULL, @Error NVARCHAR(MAX)=NULL, @CreatedAtUtc DATETIME2(7)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @EventType = 1 OR @EventType NOT BETWEEN 2 AND 5 THROW 52071, ''Memory index add event does not allow Queued or unknown event types.'', 1;
    IF NOT EXISTS (SELECT 1 FROM agent.AgentMemoryIndexQueue WHERE IndexRecordId=@IndexRecordId) THROW 52072, ''Memory index queue record does not exist.'', 1;
    INSERT INTO agent.AgentMemoryIndexEvent (IndexEventId,IndexRecordId,EventType,WeaviateObjectId,Error,CreatedAtUtc) VALUES (@IndexEventId,@IndexRecordId,@EventType,@WeaviateObjectId,@Error,@CreatedAtUtc);
END');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_MemoryExecutionAudit_Create
    @AuditId NVARCHAR(120), @TenantId NVARCHAR(80), @ProjectId NVARCHAR(80), @CampaignId NVARCHAR(80), @RunId NVARCHAR(80), @AgentId NVARCHAR(120), @ExecutionId NVARCHAR(160), @ContextId NVARCHAR(160), @RequestId NVARCHAR(160), @ReviewId NVARCHAR(160), @SkillId NVARCHAR(160), @DecisionId NVARCHAR(120), @ActionType INT, @Outcome INT, @ExecutionStatus NVARCHAR(80), @GateDecision INT, @GovernanceDecision INT=NULL, @GovernanceCheckId NVARCHAR(120)=NULL, @Executed BIT, @SourceMutated BIT, @WorkspaceMutated BIT, @ExternalSystemCalled BIT, @TicketCreated BIT, @MemoryWritten BIT, @ApprovalGranted BIT, @ShellCommandRun BIT, @ToolName NVARCHAR(160)=NULL, @AffectedArtifactType NVARCHAR(80)=NULL, @AffectedArtifactId NVARCHAR(160)=NULL, @ThoughtLedgerEntryId NVARCHAR(120)=NULL, @CorrelationId NVARCHAR(120)=NULL, @Summary NVARCHAR(MAX), @MemoryItemIdsJson NVARCHAR(MAX), @InfluenceIdsJson NVARCHAR(MAX), @HandoffMemorySliceIdsJson NVARCHAR(MAX), @EvidencePathsJson NVARCHAR(MAX), @BlockersJson NVARCHAR(MAX), @WarningsJson NVARCHAR(MAX), @IssueCodesJson NVARCHAR(MAX), @CreatedAtUtc DATETIME2(7)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@SkillId,N''''))),N'''') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@DecisionId,N''''))),N'''') IS NULL THROW 52080, ''Memory execution audit requires skill ID and decision ID.'', 1;
    IF @ActionType NOT BETWEEN 1 AND 9 OR @Outcome NOT BETWEEN 1 AND 7 OR @GateDecision NOT BETWEEN 1 AND 4 THROW 52081, ''Memory execution audit enum values are invalid.'', 1;
    IF @MemoryItemIdsJson IS NULL OR ISJSON(@MemoryItemIdsJson) <> 1 OR @InfluenceIdsJson IS NULL OR ISJSON(@InfluenceIdsJson) <> 1 OR @HandoffMemorySliceIdsJson IS NULL OR ISJSON(@HandoffMemorySliceIdsJson) <> 1 OR @EvidencePathsJson IS NULL OR ISJSON(@EvidencePathsJson) <> 1 OR @BlockersJson IS NULL OR ISJSON(@BlockersJson) <> 1 OR @WarningsJson IS NULL OR ISJSON(@WarningsJson) <> 1 OR @IssueCodesJson IS NULL OR ISJSON(@IssueCodesJson) <> 1 THROW 52082, ''Memory execution audit requires valid JSON arrays.'', 1;
    IF NOT EXISTS (SELECT 1 FROM OPENJSON(@MemoryItemIdsJson)) AND NOT EXISTS (SELECT 1 FROM OPENJSON(@InfluenceIdsJson)) AND NOT EXISTS (SELECT 1 FROM OPENJSON(@HandoffMemorySliceIdsJson)) THROW 52083, ''Memory execution audit requires at least one memory, influence, or handoff reference.'', 1;
    INSERT INTO agent.AgentMemoryExecutionAudit (AuditId,TenantId,ProjectId,CampaignId,RunId,AgentId,ExecutionId,ContextId,RequestId,ReviewId,SkillId,DecisionId,ActionType,Outcome,ExecutionStatus,GateDecision,GovernanceDecision,GovernanceCheckId,Executed,SourceMutated,WorkspaceMutated,ExternalSystemCalled,TicketCreated,MemoryWritten,ApprovalGranted,ShellCommandRun,ToolName,AffectedArtifactType,AffectedArtifactId,ThoughtLedgerEntryId,CorrelationId,Summary,MemoryItemIdsJson,InfluenceIdsJson,HandoffMemorySliceIdsJson,EvidencePathsJson,BlockersJson,WarningsJson,IssueCodesJson,CreatedAtUtc)
    VALUES (@AuditId,@TenantId,@ProjectId,@CampaignId,@RunId,@AgentId,@ExecutionId,@ContextId,@RequestId,@ReviewId,@SkillId,@DecisionId,@ActionType,@Outcome,@ExecutionStatus,@GateDecision,@GovernanceDecision,@GovernanceCheckId,@Executed,@SourceMutated,@WorkspaceMutated,@ExternalSystemCalled,@TicketCreated,@MemoryWritten,@ApprovalGranted,@ShellCommandRun,@ToolName,@AffectedArtifactType,@AffectedArtifactId,@ThoughtLedgerEntryId,@CorrelationId,@Summary,@MemoryItemIdsJson,@InfluenceIdsJson,@HandoffMemorySliceIdsJson,@EvidencePathsJson,@BlockersJson,@WarningsJson,@IssueCodesJson,@CreatedAtUtc);
END');
