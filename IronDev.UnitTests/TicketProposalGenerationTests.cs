using IronDev.Core.Workbench;

namespace IronDev.UnitTests;

[TestClass]
public sealed class TicketProposalGenerationTests
{
    [TestMethod]
    public void CompletedV3_AcceptsOneToFiveOrderedProposalsWithTrustedProvenance()
    {
        var context = Context();
        var output = Output(context,
        [
            Proposal("first", 1, [], context.SourceUserMessageId),
            Proposal("second", 2, ["first"], context.SourceUserMessageId)
        ]);

        WorkbenchBusinessAnalystOutputValidator.Validate(output, context);
        var roundTrip = WorkbenchBusinessAnalystOutputValidator.DeserializeAndValidate(
            WorkbenchBusinessAnalystOutputValidator.Serialize(output), context);

        Assert.AreEqual(2, roundTrip.TicketProposalSet?.Proposals.Count);
    }

    [TestMethod]
    public void NeedsInput_RequiresZeroProposalsAndAtLeastOneQuestionOrConflict()
    {
        var context = Context();
        var valid = Output(context, [], WorkbenchAgentRunStates.NeedsInput) with
        {
            TicketProposalSet = new TicketProposalSetOutput(
                null,
                [],
                [new TicketProposalIssueOutput(
                    TicketProposalIssueKinds.Question,
                    "Which users need this outcome?",
                    [context.SourceUserMessageId])],
                [],
                [context.SourceUserMessageId])
        };
        WorkbenchBusinessAnalystOutputValidator.Validate(valid, context);

        var invalid = valid with
        {
            TicketProposalSet = valid.TicketProposalSet! with { OpenQuestions = [] }
        };
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(invalid, context));
    }

    [TestMethod]
    public void CompletedV3_RejectsUnknownOrForwardDependenciesAndUntrustedSources()
    {
        var context = Context();
        var forwardDependency = Output(context,
        [
            Proposal("first", 1, ["second"], context.SourceUserMessageId),
            Proposal("second", 2, [], context.SourceUserMessageId)
        ]);
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(forwardDependency, context));

        var untrusted = Output(context, [Proposal("first", 1, [], 999_999)]);
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(untrusted, context));
    }

    [TestMethod]
    public void CompletedV3_AcceptsServerValidatedUnderstandingProvenanceOutsideTheMessageWindow()
    {
        const long olderSourceMessageId = 9;
        var context = Context() with
        {
            UnderstandingJson = ProjectUnderstandingDocumentCodec.Serialize(
                new ProjectUnderstandingDocument(
                    ProjectUnderstandingContract.SchemaVersion,
                    [new ProjectUnderstandingFact(
                        "ProductSummary",
                        "Coordinate volunteer schedules.",
                        ProjectUnderstandingFactStates.Confirmed,
                        UserLocked: false,
                        ProjectUnderstandingAuthorKinds.Actor,
                        AuthorActorUserId: 1,
                        AuthorAgentRunId: null,
                        [olderSourceMessageId],
                        "Confirmed in an earlier project message.",
                        Revision: 1)],
                    [],
                    []))
        };
        var output = Output(context, [Proposal("first", 1, [], olderSourceMessageId)]) with
        {
            TicketProposalSet = Output(context, []).TicketProposalSet! with
            {
                Proposals = [Proposal("first", 1, [], olderSourceMessageId)],
                SourceMessageIds = [olderSourceMessageId]
            }
        };

        WorkbenchBusinessAnalystOutputValidator.Validate(output, context);
    }

    [TestMethod]
    public void MaterializedDocument_AllocatesServerIdsAndRoundTripsCanonicalHash()
    {
        var context = Context();
        var output = Output(context,
        [
            Proposal("first", 1, [], context.SourceUserMessageId),
            Proposal("second", 2, ["first"], context.SourceUserMessageId)
        ]);
        var now = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var document = TicketProposalSetDocumentCodec.Materialize(
            output.TicketProposalSet!, 3, 4, 1, 2, context.AgentRunId,
            existingSetId: null, revision: 1, now, now, output.Outcome);

        var json = TicketProposalSetDocumentCodec.Serialize(document);
        var read = TicketProposalSetDocumentCodec.Deserialize(json);

        Assert.AreEqual(2, read.Proposals.Count);
        Assert.AreEqual(
            read.Proposals[0].TicketProposalId,
            read.Proposals[1].DependencyProposalIds.Single());
        Assert.AreEqual(64, TicketProposalSetDocumentCodec.ComputeHash(json).Length);
    }

    private static WorkbenchBusinessAnalystContext Context()
    {
        var hash = new string('a', 64);
        return new WorkbenchBusinessAnalystContext(
            Guid.NewGuid(), 1, 3, "Volunteer planner", 4, 1, 5, 10, 2, "{}",
            [new WorkbenchAgentContextMessage(10, "user", "/ticket", DateTime.UtcNow)],
            WorkbenchBusinessAnalystContract.AgentVersion,
            WorkbenchBusinessAnalystContract.PromptVersion3,
            WorkbenchBusinessAnalystContract.ToolPolicyVersion,
            WorkbenchBusinessAnalystContract.ContextSchemaVersion3,
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion3,
            WorkbenchBusinessAnalystContract.OutputSchemaVersion3,
            hash,
            WorkbenchAgentInvocationKinds.TicketProposalGeneration,
            null,
            Guid.NewGuid(),
            null);
    }

    private static WorkbenchBusinessAnalystOutput Output(
        WorkbenchBusinessAnalystContext context,
        IReadOnlyList<TicketProposalOutput> proposals,
        string outcome = WorkbenchAgentRunStates.Completed) => new(
        WorkbenchBusinessAnalystContract.OutputSchemaVersion3,
        context.ContextHash,
        context.UnderstandingRevision,
        outcome,
        "Review these bounded ticket proposals.",
        UnderstandingPatch: null,
        RenameProposal: null,
        TicketProposalSet: new TicketProposalSetOutput(
            "Split by independent user-visible outcomes.",
            proposals,
            [],
            [],
            [context.SourceUserMessageId]));

    private static TicketProposalOutput Proposal(
        string key,
        int order,
        IReadOnlyList<string> dependencies,
        long sourceMessageId) => new(
        key,
        $"Proposal {order}",
        "A bounded user problem.",
        "A bounded product change.",
        ["An observable acceptance result is met."],
        dependencies,
        order,
        [sourceMessageId]);
}
