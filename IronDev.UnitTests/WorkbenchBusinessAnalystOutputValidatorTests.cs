using IronDev.Core.Workbench;

namespace IronDev.UnitTests;

[TestClass]
public sealed class WorkbenchBusinessAnalystOutputValidatorTests
{
    private static readonly WorkbenchBusinessAnalystContext Context = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        1,
        7,
        "An idea",
        70,
        1,
        700,
        7000,
        3,
        "{}",
        [new WorkbenchAgentContextMessage(7000, "user", "Shape this idea", DateTime.UnixEpoch)],
        WorkbenchBusinessAnalystContract.AgentVersion,
        WorkbenchBusinessAnalystContract.PromptVersion,
        WorkbenchBusinessAnalystContract.ToolPolicyVersion,
        WorkbenchBusinessAnalystContract.OutputSchemaVersion,
        new string('a', 64));

    [TestMethod]
    public void Validate_AcceptsThePinnedSchemaAndServerContext()
    {
        WorkbenchBusinessAnalystOutputValidator.Validate(
            ValidOutput(),
            Context);
    }

    [TestMethod]
    public void Validate_RejectsUnknownSchemaVersion()
    {
        var output = ValidOutput() with { OutputSchemaVersion = 2 };

        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(output, Context));
    }

    [TestMethod]
    public void Validate_RejectsContextHashOrUnderstandingRevisionMismatch()
    {
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(
                ValidOutput() with { ContextHash = new string('b', 64) },
                Context));
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(
                ValidOutput() with { BasedOnUnderstandingRevision = 4 },
                Context));
    }

    [TestMethod]
    public void Validate_RejectsNonTerminalOutcomeAndBlankAssistantMessage()
    {
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(
                ValidOutput() with { Outcome = WorkbenchAgentRunStates.Running },
                Context));
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(
                ValidOutput() with { AssistantMessage = " " },
                Context));
    }

    [TestMethod]
    public void DeserializeAndValidate_RejectsUnknownOrMissingSchemaFields()
    {
        var unknown = $$"""
            {
              "outputSchemaVersion": 1,
              "contextHash": "{{Context.ContextHash}}",
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer",
              "unexpectedAuthority": true
            }
            """;
        var missing = $$"""
            {
              "contextHash": "{{Context.ContextHash}}",
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer"
            }
            """;
        var missingContextHash = """
            {
              "outputSchemaVersion": 1,
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer"
            }
            """;
        var nullContextHash = """
            {
              "outputSchemaVersion": 1,
              "contextHash": null,
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer"
            }
            """;

        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.DeserializeAndValidate(unknown, Context));
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.DeserializeAndValidate(missing, Context));
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.DeserializeAndValidate(missingContextHash, Context));
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.DeserializeAndValidate(nullContextHash, Context));
    }

    private static WorkbenchBusinessAnalystOutput ValidOutput() => new(
        WorkbenchBusinessAnalystContract.OutputSchemaVersion,
        Context.ContextHash,
        Context.UnderstandingRevision,
        WorkbenchAgentRunStates.Completed,
        "Let us clarify the users and desired outcome.");
}
