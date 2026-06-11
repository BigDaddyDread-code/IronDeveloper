using Dapper;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests;

internal static class AgentMemorySchemaTestSupport
{
    public static async Task ApplyCoreAgentMemoryMigrationsAsync(string connectionString, string repositoryRoot)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var migration in new[]
        {
            "migrate_agent_local_memory.sql",
            "migrate_agent_memory_influence.sql",
            "migrate_agent_memory_handoff.sql",
            "migrate_agent_memory_improvement_proposals.sql",
            "migrate_agent_memory_stored_procedures.sql"
        })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Database", migration)).ConfigureAwait(false);
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }
    }

    public static async Task DropAgentMemorySchemaInDependencyOrderAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID('agent.usp_MemoryExecutionAudit_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryExecutionAudit_Create;
            IF OBJECT_ID('agent.usp_MemoryIndexEvent_Add', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryIndexEvent_Add;
            IF OBJECT_ID('agent.usp_MemoryIndexQueue_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryIndexQueue_Create;
            IF OBJECT_ID('agent.usp_MemoryImprovementProposal_AddEvent', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryImprovementProposal_AddEvent;
            IF OBJECT_ID('agent.usp_MemoryImprovementProposal_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryImprovementProposal_Create;
            IF OBJECT_ID('agent.usp_AgentMemoryHandoff_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentMemoryHandoff_Create;
            IF OBJECT_ID('agent.usp_AgentMemoryInfluence_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentMemoryInfluence_Create;
            IF OBJECT_ID('agent.usp_AgentLocalMemory_AddEvent', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentLocalMemory_AddEvent;
            IF OBJECT_ID('agent.usp_AgentLocalMemory_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentLocalMemory_Create;

            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryExecutionAudit', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryExecutionAudit;

            IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_ValidateProjection', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexQueue_ValidateProjection;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryIndexEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryIndexEvent;
            IF OBJECT_ID('agent.AgentMemoryIndexQueue', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryIndexQueue;

            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_ValidateSources', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposal_ValidateSources;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryImprovementProposalEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryImprovementProposalEvent;
            IF OBJECT_ID('agent.AgentMemoryImprovementProposal', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryImprovementProposal;

            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryHandoffSlice;

            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_ValidateScope', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_ValidateScope;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryInfluenceRecord', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryInfluenceRecord;

            IF OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V') IS NOT NULL
                DROP VIEW agent.vwAgentLocalMemoryCurrentState;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryItem_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryItem_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentLocalMemoryEvidenceRef', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvidenceRef;
            IF OBJECT_ID('agent.AgentLocalMemoryEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvent;
            IF OBJECT_ID('agent.AgentLocalMemoryItem', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryItem;

            IF SCHEMA_ID('agent') IS NOT NULL
                DROP SCHEMA agent;
            """).ConfigureAwait(false);
    }
}
