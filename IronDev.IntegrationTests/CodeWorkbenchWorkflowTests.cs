using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data;
using IronDev.Data.Models;
using IronDev.Services;
using IronDev.Infrastructure.Services;
using IronDev.AI;
using IronDev.Core;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels;

namespace IronDev.IntegrationTests;

public class MockWorkbenchLlmService : ILLMService
{
    public string NextResponse { get; set; } = string.Empty;

    public Task<string> GetResponseAsync(string prompt)
    {
        return Task.FromResult(NextResponse);
    }
}

[TestClass]
public class CodeWorkbenchWorkflowTests : IntegrationTestBase
{
    private CodeWorkbenchViewModel CreateViewModel(ILLMService llmService, out IServiceScope scope)
    {
        scope = ServiceProvider.CreateScope();
        
        // Use real services except LLM
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        
        var generatorService = new WorkbenchGeneratorService(llmService);
        
        return new CodeWorkbenchViewModel(ticketService, generatorService, indexService, promptBuilder);
    }

    [TestMethod]
    public async Task LoadTicketCommand_ShouldFetchTicketFromDb()
    {
        var mockLlm = new MockWorkbenchLlmService();
        var vm = CreateViewModel(mockLlm, out var scope);
        using (scope)
        {
            var projectService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
            var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
            var projectId = await SeedProjectAsync();
            var ticketId = await ticketService.SaveTicketAsync(new ProjectTicket { ProjectId = projectId, Title = "Test Load Ticket", Status = "Draft", Content = "Test content" });

            vm.TicketId = ticketId;
            await vm.LoadTicketCommand.ExecuteAsync(null);

            Assert.IsNotNull(vm.LoadedTicket);
            Assert.AreEqual("Test Load Ticket", vm.LoadedTicket.Title);
            Assert.IsTrue(vm.LoadTriggered);
        }
    }

    [TestMethod]
    public async Task OpenWorkbench_WithoutSelectedTicket_ShouldReturnFalseInCanExecute()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var vm = new OutputPanelViewModel(ticketService);
        
        vm.ClearTicket();
        Assert.IsFalse(vm.CanOpenWorkbench, "Workbench should be disabled when no ticket is loaded");
        Assert.IsFalse(vm.OpenWorkbenchCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task OpenWorkbench_WithSelectedTicket_ShouldReturnTrueInCanExecute()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var vm = new OutputPanelViewModel(ticketService);
        
        vm.Receive(new TicketSelectedMessage(new TicketItem { Id = 123, Title = "Saved Ticket" }));
        
        Assert.IsTrue(vm.CanOpenWorkbench, "Workbench should be enabled when a saved ticket is selected");
        Assert.IsTrue(vm.OpenWorkbenchCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task GeneratePlanCommand_ShouldParseJsonResult()
    {
        var mockLlm = new MockWorkbenchLlmService();
        var vm = CreateViewModel(mockLlm, out var scope);
        using (scope)
        {
            vm.LoadedTicket = new ProjectTicket { Title = "Plan test" };
            
            mockLlm.NextResponse = @"```json
{
  ""summary"": ""Great plan"",
  ""targetFilePath"": ""/src/test.cs""
}
```";
            await vm.GeneratePlanCommand.ExecuteAsync(null);

            Assert.IsNotNull(vm.ImplementationPlan);
            Assert.AreEqual("Great plan", vm.ImplementationPlan.Summary);
            Assert.AreEqual("/src/test.cs", vm.SelectedTargetFilePath);
        }
    }

    [TestMethod]
    public async Task GenerateCodeDraftCommand_ShouldStripFences()
    {
        var mockLlm = new MockWorkbenchLlmService();
        var vm = CreateViewModel(mockLlm, out var scope);
        using (scope)
        {
            vm.LoadedTicket = new ProjectTicket { Title = "Code test" };
            vm.ImplementationPlan = new ImplementationPlanResult { TargetFilePath = "test.cs" };
            vm.SelectedTargetFilePath = "test.cs";
            
            // Simulating a fallback stripped response if JSON fails
            mockLlm.NextResponse = @"```csharp
public class GeneratedClass {}
```";
            await vm.GenerateCodeDraftCommand.ExecuteAsync(null);

            Assert.AreEqual("public class GeneratedClass {}", vm.GeneratedCode);
        }
    }

    [TestMethod]
    public async Task GenerateTestDraftCommand_ShouldStripFences()
    {
        var mockLlm = new MockWorkbenchLlmService();
        var vm = CreateViewModel(mockLlm, out var scope);
        using (scope)
        {
            vm.LoadedTicket = new ProjectTicket { Title = "Test test" };
            vm.ImplementationPlan = new ImplementationPlanResult { TargetFilePath = "test.cs" };
            vm.SelectedTargetFilePath = "test.cs";
            vm.GeneratedCode = "public class GeneratedClass {}";
            
            mockLlm.NextResponse = @"```
[TestClass] public class GeneratedClassTests {}
```";
            await vm.GenerateTestDraftCommand.ExecuteAsync(null);

            Assert.AreEqual("[TestClass] public class GeneratedClassTests {}", vm.GeneratedTestCode);
        }
    }
}
