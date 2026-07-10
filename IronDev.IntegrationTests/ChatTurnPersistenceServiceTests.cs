using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatTurnPersistenceServiceTests : IntegrationTestBase
{
    [TestMethod]
    public void ChatTurnPersistenceService_DoesNotRunRuntimeDdl()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ChatTurnPersistenceService.cs"));

        Assert.IsFalse(source.Contains("EnsureTablesAsync", StringComparison.Ordinal), "Runtime DDL must not live in ChatTurnPersistenceService.");
        Assert.IsFalse(source.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase), "ChatTurnPersistenceService must not create audit tables at runtime.");
        Assert.IsFalse(source.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase), "ChatTurnPersistenceService must not alter audit tables at runtime.");
        Assert.IsFalse(source.Contains("OBJECT_ID", StringComparison.OrdinalIgnoreCase), "ChatTurnPersistenceService must not probe schema at runtime.");
    }

    [TestMethod]
    public async Task SaveMessageAsync_AssistantEnvelopePersistsGovernanceClarificationAndTraceRows()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Governance persistence test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson(),
            ContextSummary = "Context summary for persisted trace.",
            LinkedFilePaths = "src/App.cs",
            LinkedSymbols = "App"
        });

        var snapshot = await turnPersistence.GetByMessageIdAsync(messageId);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(ChatGovernanceMode.Formalization, snapshot.Mode);
        Assert.AreEqual(0.97, snapshot.ModeConfidence);
        Assert.AreEqual(ChatClarificationKind.GovernanceIntent, snapshot.Clarification.Kind);
        Assert.AreEqual("Do you want to turn this into a ticket?", snapshot.Clarification.Questions.Single());
        Assert.IsTrue(snapshot.Gate.CanCreateTicket);
        Assert.AreEqual("route-123", snapshot.RouteTraceId);
        Assert.AreEqual("dogfood-456", snapshot.DogfoodTraceId);
        Assert.AreEqual("ProjectChatContextPipeline", snapshot.RouteSource);
        Assert.IsNotNull(snapshot.RouteChallenge);
        Assert.AreEqual(ChatGovernanceMode.Confirmation, snapshot.RouteChallenge.SuggestedMode);
        Assert.AreEqual(ContextRequestKind.CreateTicket, snapshot.RouteChallenge.SuggestedRequestKind);
        Assert.IsNotNull(snapshot.BaDraft);
        Assert.AreEqual("Parcels can be marked Lost", snapshot.BaDraft.CandidateTitle);
        Assert.AreEqual("101", snapshot.BaDraft.SourceMessageIds.Single());
        Assert.AreEqual("Context summary for persisted trace.", snapshot.ContextSummary);
        Assert.AreEqual("src/App.cs", snapshot.LinkedFilePaths);
        Assert.AreEqual("App", snapshot.LinkedSymbols);
    }

    [TestMethod]
    public async Task SaveTurnAsync_WritesMessageGovernanceClarificationAndTraceAtomically()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Atomic audit write test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Atomic ticket handoff is ready.",
            Tags = BuildEnvelopeJson(),
            ContextSummary = "Atomic context summary.",
            LinkedFilePaths = "src/Atomic.cs",
            LinkedSymbols = "Atomic"
        });

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatMessages", "Id = @MessageId", new { MessageId = messageId }));
        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatTurnGovernance", "ChatMessageId = @MessageId", new { MessageId = messageId }));
        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatTurnClarifications", "ChatMessageId = @MessageId", new { MessageId = messageId }));
        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatTurnTraces", "ChatMessageId = @MessageId", new { MessageId = messageId }));
    }

    [TestMethod]
    public async Task SaveTurnAsync_AuditFailureDoesNotLeaveSuccessfulMessageWithoutAuditRows()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Audit rollback test"
        });
        var messageText = $"Audit rollback proof {Guid.NewGuid():N}";

        try
        {
            await chat.SaveMessageAsync(new ChatMessage
            {
                ProjectId = projectId,
                ChatSessionId = sessionId,
                Role = "assistant",
                Message = messageText,
                Tags = BuildNullModeReasonEnvelopeJson(),
                ContextSummary = "This should roll back with the message.",
                LinkedFilePaths = "src/Rollback.cs",
                LinkedSymbols = "Rollback"
            });
            Assert.Fail("Expected normalized audit row failure to abort the chat message save.");
        }
        catch (SqlException)
        {
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(
            0,
            await CountRowsAsync(
                connection,
                "dbo.ChatMessages",
                "ChatSessionId = @SessionId AND Message = @Message",
                new { SessionId = sessionId, Message = messageText }),
            "The message insert must roll back when a normalized audit row fails.");
        Assert.AreEqual(0, await CountRowsAsync(connection, "dbo.ChatTurnGovernance", "ChatSessionId = @SessionId", new { SessionId = sessionId }));
        Assert.AreEqual(0, await CountRowsAsync(connection, "dbo.ChatTurnClarifications", "ChatSessionId = @SessionId", new { SessionId = sessionId }));
        Assert.AreEqual(0, await CountRowsAsync(connection, "dbo.ChatTurnTraces", "ChatSessionId = @SessionId", new { SessionId = sessionId }));
    }

    [TestMethod]
    public async Task AuditLookup_PrefersNormalizedRowsOverTags()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Normalized audit lookup test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson()
        });

        var snapshot = await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId);

        Assert.IsNotNull(snapshot);
        Assert.IsFalse(snapshot.IsFallbackEvidence);
        Assert.AreEqual(ChatGovernanceMode.Formalization, snapshot.Mode);
    }

    [TestMethod]
    public async Task AuditLookup_LabelsTagFallbackAsFallback()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Tags fallback lookup test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson(),
            ContextSummary = "Fallback context summary.",
            LinkedFilePaths = "src/Fallback.cs",
            LinkedSymbols = "Fallback"
        });

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            DELETE FROM dbo.ChatTurnTraces WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnClarifications WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnGovernance WHERE ChatMessageId = @MessageId;
            """,
            new { MessageId = messageId });

        var snapshot = await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId);

        Assert.IsNotNull(snapshot);
        Assert.IsTrue(snapshot.IsFallbackEvidence);
        Assert.AreEqual(ChatGovernanceMode.Formalization, snapshot.Mode);
        Assert.AreEqual(ChatClarificationKind.GovernanceIntent, snapshot.Clarification.Kind);
        Assert.AreEqual("ProjectChatContextPipeline", snapshot.RouteSource);
        Assert.IsNotNull(snapshot.RouteChallenge);
        Assert.IsNotNull(snapshot.BaDraft);
        Assert.AreEqual("Parcels can be marked Lost", snapshot.BaDraft.CandidateTitle);
        Assert.AreEqual("Fallback context summary.", snapshot.ContextSummary);
        Assert.AreEqual("src/Fallback.cs", snapshot.LinkedFilePaths);
        Assert.AreEqual("Fallback", snapshot.LinkedSymbols);
    }

    [TestMethod]
    public async Task AuditLookup_ByMessageIdDoesNotUseTagsFallback()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "ID-only fallback boundary test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson()
        });

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            DELETE FROM dbo.ChatTurnTraces WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnClarifications WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnGovernance WHERE ChatMessageId = @MessageId;
            """,
            new { MessageId = messageId });

        Assert.IsNull(await turnPersistence.GetByMessageIdAsync(messageId));

        var scopedSnapshot = await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId);
        Assert.IsNotNull(scopedSnapshot);
        Assert.IsTrue(scopedSnapshot.IsFallbackEvidence);
    }

    [TestMethod]
    public async Task AuditLookup_TagFallbackRequiresScope()
    {
        var projectId = await SeedProjectAsync();
        var otherProjectId = await SeedProjectAsync(name: "Other fallback project");
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Scoped fallback lookup test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson()
        });

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            DELETE FROM dbo.ChatTurnTraces WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnClarifications WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnGovernance WHERE ChatMessageId = @MessageId;
            """,
            new { MessageId = messageId });

        Assert.IsNotNull(await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(otherProjectId, sessionId, messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(projectId, sessionId + 1, messageId));
    }

    [TestMethod]
    public async Task SaveMessageAsync_LegacyTagsDoNotPersistNormalizedTurnRows()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Legacy tags test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Legacy response.",
            Tags = "projectQuestion"
        });

        var snapshot = await turnPersistence.GetByMessageIdAsync(messageId);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public async Task SaveMessageAsync_UserEnvelopeDoesNotPersistNormalizedTurnRows()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "User tags test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "user",
            Message = "Turn this into a ticket.",
            Tags = BuildEnvelopeJson()
        });

        Assert.IsNull(await turnPersistence.GetByMessageIdAsync(messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId));
    }

    [TestMethod]
    public async Task GetByMessageAsync_RequiresProjectAndSessionScope()
    {
        var projectId = await SeedProjectAsync();
        var otherProjectId = await SeedProjectAsync(name: "Other Project");
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Scoped audit read"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson()
        });

        Assert.IsNotNull(await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(otherProjectId, sessionId, messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(projectId, sessionId + 1, messageId));
    }

    [TestMethod]
    public async Task GetByMessageAsync_RequiresTenantScope()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Tenant scoped audit read"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson()
        });

        Assert.IsNotNull(await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId));

        TenantContext.TenantId = 2;
        try
        {
            Assert.IsNull(await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId));
        }
        finally
        {
            TenantContext.TenantId = 1;
        }
    }

    [TestMethod]
    public async Task GetByMessageAsync_DoesNotInferFallbackEvidenceFromClarificationReason()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Fallback audit read"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Need lane confirmation.",
            Tags = BuildFallbackEnvelopeJson()
        });

        var snapshot = await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(ChatGovernanceMode.Confirmation, snapshot.Mode);
        StringAssert.Contains(snapshot.Clarification.Reason, "Fallback clarification evidence");
        Assert.IsFalse(snapshot.IsFallbackEvidence);
    }

    [TestMethod]
    public async Task SaveMessageAsync_NormalizesRequiredClarificationWithNoneKind()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Invalid clarification invariant test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Need a slice recommendation.",
            Tags = BuildInvalidClarificationEnvelopeJson()
        });

        var snapshot = await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId);

        Assert.IsNotNull(snapshot);
        Assert.IsTrue(snapshot.Clarification.Required);
        Assert.AreNotEqual(ChatClarificationKind.None, snapshot.Clarification.Kind);
        Assert.AreEqual(ChatClarificationKind.GeneralScope, snapshot.Clarification.Kind);
        Assert.AreEqual("What slice should we start with?", snapshot.Clarification.Questions.Single());
    }

    [TestMethod]
    public async Task DeleteSessionAsync_DeletesPersistedTurnRowsBeforeMessages()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Delete turn state test"
        });
        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson()
        });

        Assert.IsNotNull(await turnPersistence.GetByMessageIdAsync(messageId));

        await chat.DeleteSessionAsync(sessionId);

        Assert.IsNull(await turnPersistence.GetByMessageIdAsync(messageId));
    }

    private static string BuildEnvelopeJson() =>
        """
        {
          "v": 1,
          "mode": "Formalization",
          "modeConfidence": 0.97,
          "modeReason": "The user explicitly asked to turn the discussion into project work.",
          "clarification": {
            "required": true,
            "kind": "GovernanceIntent",
            "questions": ["Do you want to turn this into a ticket?"],
            "reason": "The user is committing the discussion into durable work."
          },
          "gate": {
            "mode": "Formalization",
            "canSaveDiscussion": true,
            "canCreateTicket": true,
            "canViewSources": true,
            "canCopyMarkdown": true,
            "reason": "The user explicitly asked to turn the discussion into project work.",
            "confidence": 0.97,
            "governanceActions": ["Save this response as a Discussion.", "Create a Ticket from the saved Discussion."]
          },
          "routeTraceId": "route-123",
          "dogfoodTraceId": "dogfood-456",
          "routeSource": "ProjectChatContextPipeline",
          "routeChallenge": {
            "suggestedMode": "Confirmation",
            "suggestedRequestKind": "CreateTicket",
            "confidence": 0.51,
            "reason": "Classifier advisory differed, but the pipeline route remained final."
          },
          "baDraft": {
            "candidateTitle": "Parcels can be marked Lost",
            "problem": "Parcels need a controlled way to be marked Lost.",
            "proposedChange": "Add a Lost parcel state transition.",
            "businessRules": ["Only dispatched parcels can be marked Lost."],
            "acceptanceCriteria": ["InTransit parcels can be marked Lost."],
            "assumptions": [],
            "openQuestions": ["Should marking a parcel Lost require a reason/comment?"],
            "sourceMessageIds": ["101"],
            "confidence": 0.82,
            "readyForConfirmation": true,
            "potentialConflicts": [],
            "suggestedArtifact": "Ticket",
            "boundary": "A BA draft is shaped evidence, not a ticket."
          }
        }
        """;

    private static string BuildFallbackEnvelopeJson() =>
        """
        {
          "v": 1,
          "mode": "Confirmation",
          "modeConfidence": 0.55,
          "modeReason": "Ambiguous commitment language.",
          "clarification": {
            "required": true,
            "kind": "GovernanceIntent",
            "questions": ["Do you want exploration or formalization?"],
            "reason": "Fallback clarification evidence: confirmation mode requires an explicit lane question."
          },
          "gate": {
            "mode": "Confirmation",
            "canSaveDiscussion": false,
            "canCreateTicket": false,
            "canViewSources": false,
            "canCopyMarkdown": false,
            "reason": "Ambiguous commitment language.",
            "confidence": 0.55,
            "governanceActions": []
          },
          "routeTraceId": "route-fallback",
          "dogfoodTraceId": "dogfood-fallback"
        }
        """;

    private static string BuildInvalidClarificationEnvelopeJson() =>
        """
        {
          "v": 1,
          "mode": "Exploration",
          "modeConfidence": 0.85,
          "modeReason": "The user is exploring a product slice.",
          "clarification": {
            "required": true,
            "kind": "None",
            "questions": ["What slice should we start with?"],
            "reason": "Invalid contradictory envelope from replay."
          },
          "gate": {
            "mode": "Exploration",
            "canSaveDiscussion": false,
            "canCreateTicket": false,
            "canViewSources": false,
            "canCopyMarkdown": false,
            "reason": "The user is exploring a product slice.",
            "confidence": 0.85,
            "governanceActions": []
          },
          "routeTraceId": null,
          "dogfoodTraceId": "dogfood-invalid-clarification"
        }
        """;

    private static string BuildNullModeReasonEnvelopeJson() =>
        """
        {
          "v": 1,
          "mode": "Formalization",
          "modeConfidence": 0.97,
          "modeReason": null,
          "clarification": {
            "required": true,
            "kind": "GovernanceIntent",
            "questions": ["Do you want to turn this into a ticket?"],
            "reason": "The user is committing the discussion into durable work."
          },
          "gate": {
            "mode": "Formalization",
            "canSaveDiscussion": true,
            "canCreateTicket": true,
            "canViewSources": true,
            "canCopyMarkdown": true,
            "reason": "The user explicitly asked to turn the discussion into project work.",
            "confidence": 0.97,
            "governanceActions": ["Save this response as a Discussion.", "Create a Ticket from the saved Discussion."]
          },
          "routeTraceId": "route-rollback",
          "dogfoodTraceId": "dogfood-rollback"
        }
        """;

    private static async Task<int> CountRowsAsync(
        SqlConnection connection,
        string table,
        string where,
        object parameters)
    {
        var sql = $"SELECT COUNT(1) FROM {table} WHERE {where};";
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private static string FindRepoRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("IRONDEV_REPO_ROOT"),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var current = new DirectoryInfo(candidate!);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
