-- =====================================================================
-- migrate_agent_memory_permissions.sql
--
-- Restricted runtime permission boundary for governed agent memory.
-- Runtime role can execute approved write procedures and read approved
-- memory tables/views, but cannot mutate governed memory tables directly.
-- Safe to rerun.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE name = 'IronDevMemoryRuntimeRole'
      AND type = 'R'
)
BEGIN
    CREATE ROLE IronDevMemoryRuntimeRole;
END

GRANT EXECUTE ON OBJECT::agent.usp_AgentLocalMemory_Create TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_AgentLocalMemory_AddEvent TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_AgentMemoryInfluence_Create TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_AgentMemoryHandoff_Create TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_MemoryImprovementProposal_Create TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_MemoryImprovementProposal_AddEvent TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_MemoryIndexQueue_Create TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_MemoryIndexEvent_Add TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::agent.usp_MemoryExecutionAudit_Create TO IronDevMemoryRuntimeRole;

GRANT SELECT ON OBJECT::agent.AgentLocalMemoryItem TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentLocalMemoryEvidenceRef TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentLocalMemoryEvent TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.vwAgentLocalMemoryCurrentState TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentMemoryInfluenceRecord TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentMemoryHandoffSlice TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentMemoryImprovementProposal TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentMemoryImprovementProposalEvent TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentMemoryIndexQueue TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentMemoryIndexEvent TO IronDevMemoryRuntimeRole;
GRANT SELECT ON OBJECT::agent.AgentMemoryExecutionAudit TO IronDevMemoryRuntimeRole;

DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentLocalMemoryItem TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentLocalMemoryEvidenceRef TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentLocalMemoryEvent TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentMemoryInfluenceRecord TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentMemoryHandoffSlice TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentMemoryImprovementProposal TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentMemoryImprovementProposalEvent TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentMemoryIndexQueue TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentMemoryIndexEvent TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::agent.AgentMemoryExecutionAudit TO IronDevMemoryRuntimeRole;

DENY ALTER ON SCHEMA::agent TO IronDevMemoryRuntimeRole;
-- Do not grant CONTROL on the agent schema. An explicit DENY CONTROL at schema
-- scope also denies EXECUTE on procedures in the schema, which would block the
-- approved runtime procedure boundary above.
