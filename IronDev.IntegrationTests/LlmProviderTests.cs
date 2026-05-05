using IronDev.Core;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace IronDev.IntegrationTests;

[TestClass]
public class LlmProviderTests
{
    [TestMethod]
    public void OpenAi_ResolvesCorrectImplementation()
    {
        var options = new LlmOptions { Provider = "OpenAI", ApiKey = "fake-key" };
        var service = CreateService(options);
        Assert.IsInstanceOfType(service, typeof(OpenAiLlmService));
    }

    [TestMethod]
    public void LocalOpenAi_ResolvesCorrectImplementation()
    {
        var options = new LlmOptions { Provider = "LocalOpenAI", BaseUrl = "http://localhost:11434" };
        var service = CreateService(options);
        Assert.IsInstanceOfType(service, typeof(LocalOpenAiCompatibleLlmService));
    }

    [TestMethod]
    public void Ollama_ResolvesCorrectImplementation()
    {
        var options = new LlmOptions { Provider = "Ollama", BaseUrl = "http://localhost:11434" };
        var service = CreateService(options);
        Assert.IsInstanceOfType(service, typeof(OllamaLlmService));
    }

    [TestMethod]
    public void LocalOpenAi_MissingBaseUrl_Throws()
    {
        var options = new LlmOptions { Provider = "LocalOpenAI", BaseUrl = "" };
        try
        {
            _ = new LocalOpenAiCompatibleLlmService(options);
            Assert.Fail("Should have thrown ArgumentException");
        }
        catch (ArgumentException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void UnknownProvider_Throws()
    {
        var options = new LlmOptions { Provider = "Unknown" };
        try
        {
            _ = CreateService(options);
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void DraftTicketService_CanUseILLMService()
    {
        // This test verifies that DraftTicketService can be instantiated with an ILLMService
        // and doesn't know about the provider.
        var mockLlm = new StubLlmService();
        var draftService = new DraftTicketService(mockLlm);
        Assert.IsNotNull(draftService);
    }

    private ILLMService CreateService(LlmOptions options)
    {
        var provider = options.Provider?.ToLowerInvariant() ?? "openai";
        return provider switch
        {
            "openai"      => new OpenAiLlmService(options),
            "localopenai" => new LocalOpenAiCompatibleLlmService(options),
            "ollama"      => new OllamaLlmService(options),
            "custom"      => new LocalOpenAiCompatibleLlmService(options),
            _ => throw new InvalidOperationException($"Unsupported AI provider: {options.Provider}")
        };
    }

    private class StubLlmService : ILLMService
    {
        public Task<string> GetResponseAsync(string prompt, System.Threading.CancellationToken ct = default) => Task.FromResult("{}");
    }
}
