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

    [TestMethod]
    public void ProposalTitle_UsesPermanentTicketLimitWithoutTruncation()
    {
        var context = Context();
        var exactTitle = new string('x', TicketProposalConstraints.MaximumTitleCharacters);
        var valid = Output(context,
        [
            Proposal("first", 1, [], context.SourceUserMessageId) with { Title = exactTitle }
        ]);

        WorkbenchBusinessAnalystOutputValidator.Validate(valid, context);
        var document = TicketProposalSetDocumentCodec.Materialize(
            valid.TicketProposalSet!, 3, 4, 1, 2, context.AgentRunId,
            existingSetId: null, revision: 1, DateTime.UtcNow, DateTime.UtcNow, valid.Outcome);
        var roundTrip = TicketProposalSetDocumentCodec.Deserialize(
            TicketProposalSetDocumentCodec.Serialize(document));

        Assert.AreEqual(exactTitle, roundTrip.Proposals.Single().Title);

        var tooLong = valid with
        {
            TicketProposalSet = valid.TicketProposalSet! with
            {
                Proposals =
                [
                    valid.TicketProposalSet.Proposals.Single() with
                    {
                        Title = new string('x', TicketProposalConstraints.MaximumTitleCharacters + 1)
                    }
                ]
            }
        };
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(tooLong, context));

        var invalidDocument = document with
        {
            Proposals =
            [
                document.Proposals.Single() with
                {
                    Title = new string(
                        'x',
                        TicketProposalConstraints.HistoricalMaximumTitleCharacters + 1)
                }
            ]
        };
        Assert.ThrowsException<InvalidOperationException>(() =>
            TicketProposalSetDocumentCodec.Serialize(invalidDocument));
    }

    [TestMethod]
    public void HistoricalReadyProposalTitle_RemainsReadableButCannotBecomeCommitted()
    {
        var context = Context();
        var output = Output(context,
        [
            Proposal("first", 1, [], context.SourceUserMessageId)
        ]);
        var now = DateTime.UtcNow;
        var document = TicketProposalSetDocumentCodec.Materialize(
            output.TicketProposalSet!, 3, 4, 1, 2, context.AgentRunId,
            existingSetId: null, revision: 1, now, now, output.Outcome);
        var historicalTitle = new string(
            'h',
            TicketProposalConstraints.MaximumTitleCharacters + 1);
        var historicalReady = document with
        {
            Proposals =
            [
                document.Proposals.Single() with { Title = historicalTitle }
            ]
        };

        var roundTrip = TicketProposalSetDocumentCodec.Deserialize(
            TicketProposalSetDocumentCodec.Serialize(historicalReady));

        Assert.AreEqual(historicalTitle, roundTrip.Proposals.Single().Title);
        Assert.ThrowsException<InvalidOperationException>(() =>
            TicketProposalSetDocumentCodec.Serialize(
                historicalReady with { Status = TicketProposalSetStatuses.Committed }));
    }

    [TestMethod]
    public void CommittedDocument_RequiresResolvedIssuesAndRoundTripsCommittedStatus()
    {
        var context = Context();
        var output = Output(context,
        [
            Proposal("first", 1, [], context.SourceUserMessageId)
        ]);
        var now = DateTime.UtcNow;
        var document = TicketProposalSetDocumentCodec.Materialize(
            output.TicketProposalSet!, 3, 4, 1, 2, context.AgentRunId,
            existingSetId: null, revision: 1, now, now, output.Outcome);
        var resolvedIssue = new TicketProposalIssueDocument(
            Guid.NewGuid(),
            TicketProposalIssueKinds.Question,
            "Which users are affected?",
            TicketProposalIssueStatuses.Resolved,
            "Project owners.",
            [context.SourceUserMessageId]);
        var committed = document with
        {
            Revision = 2,
            Status = TicketProposalSetStatuses.Committed,
            OpenQuestions = [resolvedIssue],
            UpdatedAtUtc = now.AddSeconds(1)
        };

        var roundTrip = TicketProposalSetDocumentCodec.Deserialize(
            TicketProposalSetDocumentCodec.Serialize(committed));

        Assert.AreEqual(TicketProposalSetStatuses.Committed, roundTrip.Status);
        Assert.AreEqual("Committed", TicketProposalRevisionChangeKinds.Committed);

        var openIssue = resolvedIssue with
        {
            Status = TicketProposalIssueStatuses.Open,
            Resolution = null
        };
        Assert.ThrowsException<InvalidOperationException>(() =>
            TicketProposalSetDocumentCodec.Serialize(committed with { OpenQuestions = [openIssue] }));
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
