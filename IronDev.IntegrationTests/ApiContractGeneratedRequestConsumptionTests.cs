using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ApiContractGeneratedRequestConsumptionTests
{
    [TestMethod]
    public void ChatWrites_UseExplicitRequestContractsInsteadOfPersistenceModels()
    {
        var controller = Read("IronDev.Api", "Controllers", "ChatController.cs");

        StringAssert.Contains(controller, "SaveSession(int projectId, SaveProjectChatSessionRequest request");
        StringAssert.Contains(controller, "SaveMessage(int projectId, long sessionId, SaveProjectChatMessageRequest request");
        Assert.IsFalse(controller.Contains("SaveSession(int projectId, ProjectChatSession session", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("SaveMessage(int projectId, long sessionId, ChatMessage message", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OpenApi_NamesEveryActivePreviouslyHiddenRequestBody()
    {
        using var document = JsonDocument.Parse(Read("IronDev.TauriShell", "openapi", "irondev-api.openapi.json"));
        var paths = document.RootElement.GetProperty("paths");

        AssertRequestSchema(paths, "/api/projects/{projectId}/chat/sessions", "SaveProjectChatSessionRequest");
        AssertRequestSchema(paths, "/api/projects/{projectId}/chat/sessions/{sessionId}/messages", "SaveProjectChatMessageRequest");
        AssertRequestSchema(paths, "/api/v1/projects/{projectId}/accepted-approvals", "CreateAcceptedApprovalRequest");
        AssertRequestSchema(paths, "/api/v1/projects/{projectId}/policy-satisfactions", "PolicySatisfactionCreateRequest");
    }

    [TestMethod]
    public void ClientTransportRequests_AreGeneratedAliasesNotStandaloneInterfaces()
    {
        var types = Read("IronDev.TauriShell", "src", "api", "types.ts");
        var aliases = new Dictionary<string, string>
        {
            ["ConfirmBaWorkingDraftRequest"] = "ConfirmBaWorkingDraftRequest",
            ["SaveProjectChatSessionRequest"] = "SaveProjectChatSessionRequest",
            ["SaveProjectChatMessageRequest"] = "SaveProjectChatMessageRequest",
            ["StartTicketBuildRunRequest"] = "StartTicketBuildRunRequest",
            ["SaveDiscussionRequest"] = "SaveDiscussionRequest",
            ["CreateTicketFromDocumentRequest"] = "CreateTicketFromDocumentRequest",
            ["RunTicketReviewRequest"] = "RunTicketReviewRequest",
            ["StartDisposableCodeRunRequest"] = "StartDisposableCodeRunRequest",
            ["LoginRequest"] = "LoginRequest",
            ["SetProjectChannelMembershipRequest"] = "SetProjectChannelMembershipRequest",
            ["CreateProjectChannelRequest"] = "CreateProjectChannelRequest",
            ["CreateTenantUserRequest"] = "CreateTenantUserRequest",
            ["ControlledActionRequestCreateRequest"] = "ControlledActionRequestCreateRequest",
            ["CreateAcceptedApprovalUiRequest"] = "CreateAcceptedApprovalRequest"
        };

        foreach (var (clientName, schemaName) in aliases)
        {
            Assert.IsFalse(types.Contains($"export interface {clientName}", StringComparison.Ordinal),
                $"{clientName} must not drift back to a handwritten transport interface.");
            StringAssert.Contains(types, $"components['schemas']['{schemaName}']");
        }
    }

    private static void AssertRequestSchema(JsonElement paths, string path, string schemaName)
    {
        var schemaReference = paths
            .GetProperty(path)
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        Assert.AreEqual($"#/components/schemas/{schemaName}", schemaReference);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(parts)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
