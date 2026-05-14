using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public interface IDeepCodeLookupService
{
    Task<DeepCodeEvidence?> GetDeepCodeEvidenceAsync(
        int projectId,
        string filePath,
        string symbolName,
        string query,
        CancellationToken ct = default);
}

public sealed class DeepCodeLookupService : IDeepCodeLookupService
{
    private readonly ICodeIndexService _codeIndexService;
    private const int MaxChars = 4000;

    public DeepCodeLookupService(ICodeIndexService codeIndexService)
    {
        _codeIndexService = codeIndexService;
    }

    public async Task<DeepCodeEvidence?> GetDeepCodeEvidenceAsync(
        int projectId,
        string filePath,
        string symbolName,
        string query,
        CancellationToken ct = default)
    {
        var file = await _codeIndexService.GetByPathAsync(projectId, filePath, ct);
        if (file == null || string.IsNullOrWhiteSpace(file.Content)) return null;

        var content = file.Content;

        if (content.Length <= MaxChars)
        {
            return new DeepCodeEvidence
            {
                FilePath = filePath,
                SymbolName = symbolName,
                EvidenceType = DeepEvidenceType.FullSmallFile,
                CodeText = content,
                Confidence = 0.9,
                Reason = "File is small enough to include fully."
            };
        }

        // Try to find the symbol and its body
        var lowerQuery = query.ToLowerInvariant();
        
        // 1. Method body extraction for a specific method
        if (!string.IsNullOrEmpty(symbolName) && !symbolName.EndsWith("Controller") && !symbolName.Equals("ProjectTicket", StringComparison.OrdinalIgnoreCase))
        {
            var methodBody = ExtractMethodBody(content, symbolName);
            if (!string.IsNullOrEmpty(methodBody))
            {
                // Truncate if necessary
                if (methodBody.Length > MaxChars) methodBody = methodBody.Substring(0, MaxChars) + "\n...[TRUNCATED]";
                return new DeepCodeEvidence
                {
                    FilePath = filePath,
                    SymbolName = symbolName,
                    EvidenceType = DeepEvidenceType.MethodBody,
                    CodeText = methodBody,
                    Confidence = 0.9,
                    Reason = $"Found full method body for {symbolName}."
                };
            }
        }

        // 2. Class property extraction (e.g., ProjectTicket -> IsDeleted)
        if (symbolName.Equals("ProjectTicket", StringComparison.OrdinalIgnoreCase) || lowerQuery.Contains("isdeleted"))
        {
            var isDeletedProp = ExtractProperty(content, "IsDeleted");
            if (!string.IsNullOrEmpty(isDeletedProp))
            {
                return new DeepCodeEvidence
                {
                    FilePath = filePath,
                    SymbolName = "IsDeleted",
                    EvidenceType = DeepEvidenceType.PropertyDefinition,
                    CodeText = isDeletedProp,
                    Confidence = 0.85,
                    Reason = "Found requested property definition."
                };
            }
        }

        // 3. Controller/Service Auth methods extraction
        if (symbolName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) || lowerQuery.Contains("auth") || lowerQuery.Contains("login") || lowerQuery.Contains("token"))
        {
            var authMethods = ExtractAuthMethods(content);
            if (!string.IsNullOrEmpty(authMethods))
            {
                if (authMethods.Length > MaxChars) authMethods = authMethods.Substring(0, MaxChars) + "\n...[TRUNCATED]";
                return new DeepCodeEvidence
                {
                    FilePath = filePath,
                    SymbolName = symbolName,
                    EvidenceType = DeepEvidenceType.SymbolBody,
                    CodeText = authMethods,
                    Confidence = 0.85,
                    Reason = "Extracted auth-related methods."
                };
            }
        }

        // 4. Default: Extract a window around the first occurrence of the symbol or query
        var searchTarget = !string.IsNullOrEmpty(symbolName) ? symbolName : query;
        var idx = content.IndexOf(searchTarget, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var start = Math.Max(0, idx - 1000);
            var length = Math.Min(content.Length - start, MaxChars);
            var window = content.Substring(start, length);
            return new DeepCodeEvidence
            {
                FilePath = filePath,
                SymbolName = symbolName,
                EvidenceType = DeepEvidenceType.FileWindow,
                CodeText = window,
                Confidence = 0.6,
                Reason = "Extracted text window around symbol."
            };
        }

        return null;
    }

    private static string ExtractMethodBody(string content, string methodName)
    {
        // Simple regex to find method signature and extract its body using brace matching
        var idx = content.IndexOf(methodName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        // Find the next '{'
        var braceIdx = content.IndexOf('{', idx);
        if (braceIdx < 0)
        {
            // Maybe it's an expression-bodied method =>
            var arrowIdx = content.IndexOf("=>", idx);
            if (arrowIdx > 0 && arrowIdx < idx + 200)
            {
                var semiIdx = content.IndexOf(';', arrowIdx);
                if (semiIdx > 0)
                {
                    // Back up to the start of the line for signature
                    var startIdx = content.LastIndexOf('\n', idx);
                    startIdx = startIdx < 0 ? 0 : startIdx + 1;
                    return content.Substring(startIdx, semiIdx - startIdx + 1).Trim();
                }
            }
            return string.Empty;
        }

        // We have a '{'. Find matching '}'
        int depth = 0;
        int endIdx = -1;
        for (int i = braceIdx; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    endIdx = i;
                    break;
                }
            }
        }

        if (endIdx > 0)
        {
            // Include signature
            var startIdx = content.LastIndexOf('\n', idx);
            startIdx = startIdx < 0 ? 0 : startIdx + 1;
            return content.Substring(startIdx, endIdx - startIdx + 1).Trim();
        }

        return string.Empty;
    }

    private static string ExtractProperty(string content, string propName)
    {
        var idx = content.IndexOf(propName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        var startIdx = content.LastIndexOf('\n', idx);
        startIdx = startIdx < 0 ? 0 : startIdx + 1;

        var semiIdx = content.IndexOf(';', idx);
        var braceIdx = content.IndexOf('{', idx);

        if (semiIdx > 0 && (braceIdx < 0 || semiIdx < braceIdx))
        {
            return content.Substring(startIdx, semiIdx - startIdx + 1).Trim();
        }
        else if (braceIdx > 0)
        {
            var endBrace = content.IndexOf('}', braceIdx);
            if (endBrace > 0)
            {
                return content.Substring(startIdx, endBrace - startIdx + 1).Trim();
            }
        }

        return string.Empty;
    }

    private static string ExtractAuthMethods(string content)
    {
        var methods = new List<string>();
        var keywords = new[] { "login", "auth", "token", "jwt", "bearer" };
        
        foreach (var kw in keywords)
        {
            var idx = content.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var body = ExtractMethodBody(content, kw);
                if (!string.IsNullOrEmpty(body) && !methods.Contains(body))
                {
                    methods.Add(body);
                }
                idx = content.IndexOf(kw, idx + kw.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (methods.Count > 0)
            return string.Join("\n\n", methods);

        return string.Empty;
    }
}
