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
    Task<ImplementationPlanResult> GeneratePlanAsync(int projectId, ProjectTicket ticket, string context);
    Task<CodeDraftResult> GenerateCodeDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, string context);
    Task<TestDraftResult> GenerateTestDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, CodeDraftResult codeDraft, string context);
}

public sealed class WorkbenchGeneratorService : IWorkbenchGeneratorService
{
    private readonly ILLMService _llmService;

    public WorkbenchGeneratorService(ILLMService llmService)
    {
        _llmService = llmService;
    }

    public async Task<ImplementationPlanResult> GeneratePlanAsync(int projectId, ProjectTicket ticket, string context)
    {
        var prompt = $@"
Based on the following saved ticket and codebase context, output a structured Implementation Plan.
Return ONLY valid JSON matching this schema:
{{
  ""summary"": ""string"",
  ""targetFilePath"": ""string"",
  ""relevantFiles"": [""string""],
  ""implementationSteps"": [""string""],
  ""risks"": [""string""]
}}

TICKET TITLE: {ticket.Title}
TICKET PROBLEM: {ticket.Problem}
TICKET ACCEPTANCE CRITERIA: {ticket.AcceptanceCriteria}
TICKET DETAILS: {ticket.Content}

CONTEXT:
{context}
";
        var response = await _llmService.GetResponseAsync(prompt);
        return ExtractJson<ImplementationPlanResult>(response) ?? new ImplementationPlanResult { Summary = "Failed to parse plan." };
    }

    public async Task<CodeDraftResult> GenerateCodeDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, string context)
    {
        var prompt = $@"
You are a senior engineer drafting code for the target file: {plan.TargetFilePath}.
Using the following Implementation Plan and Context, output ONLY valid JSON matching this schema:
{{
  ""filePath"": ""{plan.TargetFilePath}"",
  ""language"": ""C#"",
  ""code"": ""// Raw code goes here\npublic class XYZ {{}}""
}}

Ensure the string 'code' contains the exact literal syntax needed to compile, unescaped to fit standard conventions but valid inside the JSON structure.
If you prefer, just generate the code fenced inside ```csharp and I will strip it. But try to use the JSON schema.

PLAN SUMMARY: {plan.Summary}
TICKET DETAILS: {ticket.Content}

CONTEXT:
{context}
";
        var response = await _llmService.GetResponseAsync(prompt);
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

    public async Task<TestDraftResult> GenerateTestDraftAsync(ProjectTicket ticket, ImplementationPlanResult plan, CodeDraftResult codeDraft, string context)
    {
        var prompt = $@"
You are a senior engineer testing newly drafted code.
Write an MSTest or identical testing framework suite for the Drafted Code.
Return ONLY valid JSON matching this schema:
{{
  ""filePath"": ""FilePathToTests.cs"",
  ""testFramework"": ""MSTest"",
  ""code"": ""// Test class goes here""
}}

DRAFTED CODE CLASS:
{codeDraft.Code}

PLAN SUMMARY: {plan.Summary}
";
        var response = await _llmService.GetResponseAsync(prompt);
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
