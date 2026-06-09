namespace IronDev.Core.AgentMemory;

public enum EvidenceType
{
    ToolOutput = 1,
    BuildResult = 2,
    TestResult = 3,
    CriticFinding = 4,
    DoubtFinding = 5,
    ConscienceDecision = 6,
    HumanNote = 7,
    TraceEvent = 8,
    HandoffPayload = 9,
    RunReport = 10,
    CodeReference = 11,
    DocumentReference = 12
}
