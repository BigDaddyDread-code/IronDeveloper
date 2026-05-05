using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Data.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Services;

public interface IWorkbenchGeneratorService
{
    Task<ImplementationPlanResult> GeneratePlanAsync(int projectId, ProjectTicket ticket, string context, System.Threading.CancellationToken ct = default);
    Task<CodeDraftResult> GenerateCodeDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, string context, System.Threading.CancellationToken ct = default);
    Task<TestDraftResult> GenerateTestDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, CodeDraftResult codeDraft, string context, System.Threading.CancellationToken ct = default);
}

public sealed class WorkbenchGeneratorService : IWorkbenchGeneratorService
{
    private readonly ILLMService _llmService;

    public WorkbenchGeneratorService(ILLMService llmService)
    {
        _llmService = llmService;
    }

    public async Task<ImplementationPlanResult> GeneratePlanAsync(int projectId, ProjectTicket ticket, string context, System.Threading.CancellationToken ct = default)
    {
        var prompt = $@"
...
";
        var response = await _llmService.GetResponseAsync(prompt, ct);
        return ExtractJson<ImplementationPlanResult>(response) ?? new ImplementationPlanResult { Summary = "Failed to parse plan." };
    }

    public async Task<CodeDraftResult> GenerateCodeDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, string context, System.Threading.CancellationToken ct = default)
    {
        var prompt = $@"
...
";
        var response = await _llmService.GetResponseAsync(prompt, ct);
        var res = ExtractJson<CodeDraftResult>(response);
        if (res == null)
        {
            // fallback parser
            return new CodeDraftResult
            {
                FilePath = plan.TargetFilePath,
                Code = StripCodeFences(response)
            };
        }
        return res;
    }

    public async Task<TestDraftResult> GenerateTestDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, CodeDraftResult codeDraft, string context, System.Threading.CancellationToken ct = default)
    {
        var prompt = $@"
...
";
        var response = await _llmService.GetResponseAsync(prompt, ct);
        var res = ExtractJson<TestDraftResult>(response);
        if (res == null)
        {
            // fallback parser
            return new TestDraftResult
            {
                FilePath = "UnitTests.cs",
                Code = StripCodeFences(response)
            };
        }
        return res;
    }
    
    private static T? ExtractJson<T>(string input) where T : class
    {
        var cleaned = input.Trim();
        if (cleaned.StartsWith("```json"))
            cleaned = cleaned.Substring(7);
        else if (cleaned.StartsWith("```"))
            cleaned = cleaned.Substring(3);
            
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<T>(cleaned, options);
        }
        catch
        {
            return null;
        }
    }

    private static string StripCodeFences(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0) cleaned = cleaned.Substring(firstNewline + 1);
            if (cleaned.EndsWith("```")) cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
        }
        return cleaned;
    }
}
