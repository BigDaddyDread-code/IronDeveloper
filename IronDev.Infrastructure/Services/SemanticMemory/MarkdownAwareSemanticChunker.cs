using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using IronDev.Core.KnowledgeCompiler;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class MarkdownAwareSemanticChunker : ISemanticChunker
{
    private const int MaxChunkCharacters = 2400;
    private const int MinChunkCharacters = 300;

    public IReadOnlyList<SemanticChunkDraft> Chunk(SemanticArtefactDraft artefact)
    {
        if (string.IsNullOrWhiteSpace(artefact.SearchableText))
            return [];

        var sections = SplitSections(artefact.SearchableText);
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var section in sections)
        {
            if (current.Length > 0 && current.Length + section.Length > MaxChunkCharacters)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            if (section.Length > MaxChunkCharacters)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }

                foreach (var piece in SplitLongText(section, MaxChunkCharacters))
                    chunks.Add(piece);
                continue;
            }

            if (current.Length > 0)
                current.AppendLine().AppendLine();
            current.Append(section);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        if (chunks.Count > 1)
        {
            for (var i = chunks.Count - 1; i > 0; i--)
            {
                if (chunks[i].Length >= MinChunkCharacters)
                    continue;

                chunks[i - 1] = $"{chunks[i - 1]}\n\n{chunks[i]}";
                chunks.RemoveAt(i);
            }
        }

        return chunks
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
            .Select((chunk, index) => new SemanticChunkDraft
            {
                Id = DeterministicGuid($"{artefact.Id}:{index}:{Hash(chunk)}"),
                ArtefactId = artefact.Id,
                ProjectId = artefact.ProjectId,
                ChunkIndex = index,
                ChunkText = chunk,
                TokenEstimate = Math.Max(1, chunk.Length / 4),
                ContentHash = Hash(chunk)
            })
            .ToList();
    }

    private static IReadOnlyList<string> SplitSections(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sections = new List<string>();
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith('#') && current.Length > 0)
            {
                sections.Add(current.ToString().Trim());
                current.Clear();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
            sections.Add(current.ToString().Trim());

        return sections.Count == 0 ? [markdown.Trim()] : sections;
    }

    private static IEnumerable<string> SplitLongText(string text, int maxLength)
    {
        var remaining = text.Trim();
        while (remaining.Length > maxLength)
        {
            var splitAt = remaining.LastIndexOf('\n', maxLength);
            if (splitAt < MinChunkCharacters)
                splitAt = remaining.LastIndexOf(' ', maxLength);
            if (splitAt < MinChunkCharacters)
                splitAt = maxLength;

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
            yield return remaining;
    }

    internal static string Hash(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
    }

    private static Guid DeterministicGuid(string value)
    {
        using var md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
