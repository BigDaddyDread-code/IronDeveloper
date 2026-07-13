/* CLN-21: exact data-preservation assertions after current migrations. */

IF DB_NAME() NOT LIKE N'%Test%'
    THROW 51000, N'Refusing CLN-21 preservation verification outside a test database.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = 2101 AND Name = N'CLN-21 Upgrade Tenant' AND Slug = N'cln-21-upgrade' AND IsActive = 1)
    THROW 51000, N'CLN-21 tenant row was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.Projects WHERE Id = 2101 AND TenantId = 2101 AND Name = N'CLN-21 Upgrade Project' AND Description = N'Preservation fixture' AND IndexedFileCount = 12)
    THROW 51000, N'CLN-21 project row was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.ProjectContextDocuments WHERE Id = 210101 AND TenantId = 2101 AND ProjectId = 2101 AND Content = N'context-payload-2101' AND AuthorityLevel = N'Accepted' AND Status = N'Active')
    THROW 51000, N'CLN-21 context document was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.ProjectTickets WHERE Id = 210103 AND TenantId = 2101 AND ProjectId = 2101 AND Content = N'ticket-payload-2101' AND SourceDocumentVersionId = 210105 AND BlockedByTicketIds = N'[210104]' AND Revision = 7)
    THROW 51000, N'CLN-21 ticket compatibility data was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.ArtifactSourceReferences WHERE ArtifactSourceReferenceId = 210102 AND TenantId = 2101 AND ProjectId = 2101 AND ArtifactId = 210103 AND SourceId = 210101 AND Summary = N'preserve-source-reference' AND RelevanceScore = 0.8750 AND IsRequired = 1)
    THROW 51000, N'CLN-21 artifact source reference was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.Runs WHERE Id = 210104 AND RunId = N'cln21-upgrade-run' AND ProjectId = 2101 AND TicketId = 210103 AND State = N'Completed' AND IsDisposable = 1 AND Summary = N'preserve-run-summary')
    THROW 51000, N'CLN-21 run row was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.RunEvents WHERE Id = 210105 AND EventId = '21000000-0000-0000-0000-000000000001' AND RunId = N'cln21-upgrade-run' AND EventType = N'UpgradeFixtureRecorded' AND Message = N'preserve-run-event' AND PayloadJson = N'{"version":1,"value":"preserve"}')
    THROW 51000, N'CLN-21 run event was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SemanticArtefacts WHERE Id = '21000000-0000-0000-0000-000000000002' AND TenantId = 2101 AND ProjectId = 2101 AND ContentHash = N'hash-artefact-2101' AND Title = N'Preserved semantic artefact')
    THROW 51000, N'CLN-21 semantic artefact was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SemanticChunks WHERE Id = '21000000-0000-0000-0000-000000000003' AND ArtefactId = '21000000-0000-0000-0000-000000000002' AND ChunkIndex = 3 AND ChunkText = N'preserve-semantic-chunk' AND ContentHash = N'hash-chunk-2101')
    THROW 51000, N'CLN-21 semantic chunk was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.EmbeddingJobs WHERE Id = '21000000-0000-0000-0000-000000000004' AND ProjectId = 2101 AND Status = N'Completed' AND Attempts = 2)
    THROW 51000, N'CLN-21 embedding job was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SemanticSearchTraces WHERE Id = '21000000-0000-0000-0000-000000000005' AND ProjectId = 2101 AND QueryText = N'preserve query' AND Consumer = N'CLN21')
    THROW 51000, N'CLN-21 semantic search trace was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SemanticSearchTraceResults WHERE Id = '21000000-0000-0000-0000-000000000006' AND SearchTraceId = '21000000-0000-0000-0000-000000000005' AND MatchReason = N'preserve-match-reason' AND FinalScore = 0.93)
    THROW 51000, N'CLN-21 semantic search result was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SemanticEmbeddings WHERE Id = '21000000-0000-0000-0000-000000000007' AND ArtefactId = '21000000-0000-0000-0000-000000000002' AND ContentHash = N'hash-embedding-2101' AND VectorDimensions = 4 AND VectorData = 0x01020304 AND Provider = N'Fixture')
    THROW 51000, N'CLN-21 semantic embedding metadata was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SemanticIndexRuns WHERE Id = 210106 AND ProjectId = 2101 AND Status = N'Completed' AND TotalDocuments = 12 AND ProcessedDocuments = 12)
    THROW 51000, N'CLN-21 semantic index run was not preserved.', 1;

IF (SELECT COUNT(*) FROM dbo.ProjectContextDocuments WHERE ProjectId = 2101) <> 1 OR
   (SELECT COUNT(*) FROM dbo.ArtifactSourceReferences WHERE ProjectId = 2101) <> 1 OR
   (SELECT COUNT(*) FROM dbo.Runs WHERE ProjectId = 2101) <> 1 OR
   (SELECT COUNT(*) FROM dbo.SemanticArtefacts WHERE ProjectId = 2101) <> 1 OR
   (SELECT COUNT(*) FROM dbo.SemanticChunks WHERE ProjectId = 2101) <> 1
    THROW 51000, N'CLN-21 scoped row counts changed during upgrade.', 1;

PRINT 'PASS CLN-21 upgrade data-preservation assertions.';
