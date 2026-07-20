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
        WorkbenchBusinessAnalystContract.ContextSchemaVersion,
        WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion,
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
        var output = ValidOutput() with { OutputSchemaVersion = 99 };

        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(output, Context));

        var unsupportedContext = Context with { OutputSchemaVersion = 99 };
        var matchingFutureOutput = ValidOutput() with { OutputSchemaVersion = 99 };
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(matchingFutureOutput, unsupportedContext));
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
              "outputSchemaVersion": 2,
              "contextHash": "{{Context.ContextHash}}",
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer",
              "understandingPatch": null,
              "renameProposal": null,
              "unexpectedAuthority": true
            }
            """;
        var missing = $$"""
            {
              "contextHash": "{{Context.ContextHash}}",
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer",
              "understandingPatch": null,
              "renameProposal": null
            }
            """;
        var missingContextHash = """
            {
              "outputSchemaVersion": 2,
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer",
              "understandingPatch": null,
              "renameProposal": null
            }
            """;
        var nullContextHash = """
            {
              "outputSchemaVersion": 2,
              "contextHash": null,
              "basedOnUnderstandingRevision": 3,
              "outcome": "Completed",
              "assistantMessage": "answer",
              "understandingPatch": null,
              "renameProposal": null
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

    [TestMethod]
    public void Validate_V2RejectsUnknownFactAndNonFrozenSource()
    {
        var unknownFact = ValidOutput() with
        {
            UnderstandingPatch = new ProjectUnderstandingPatch(
                [new ProjectUnderstandingFactChange(
                    "BuildPassed", "yes", ProjectUnderstandingFactStates.Confirmed, [7000], "User said so.")])
        };
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(unknownFact, Context));

        var foreignSource = ValidOutput() with
        {
            UnderstandingPatch = new ProjectUnderstandingPatch(
                [new ProjectUnderstandingFactChange(
                    "Goals", "Ship safely", ProjectUnderstandingFactStates.Confirmed, [9999], "User said so.")])
        };
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(foreignSource, Context));
    }

    [TestMethod]
    public void Serialize_PreservesTheExactVersion1Shape()
    {
        var legacyContext = Context with
        {
            PromptVersion = WorkbenchBusinessAnalystContract.PromptVersion1,
            OutputSchemaVersion = WorkbenchBusinessAnalystContract.OutputSchemaVersion1
        };
        var output = new WorkbenchBusinessAnalystOutput(
            WorkbenchBusinessAnalystContract.OutputSchemaVersion1,
            legacyContext.ContextHash,
            legacyContext.UnderstandingRevision,
            WorkbenchAgentRunStates.Completed,
            "Legacy output.");

        var json = WorkbenchBusinessAnalystOutputValidator.Serialize(output);
        Assert.IsFalse(json.Contains("understandingPatch", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("renameProposal", StringComparison.Ordinal));
        WorkbenchBusinessAnalystOutputValidator.Validate(output, legacyContext);
    }
}
