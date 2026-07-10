namespace IronDev.Core.KnowledgeCompiler;

public static class ProjectDocumentContextSource
{
    public const string EntityType = "ProjectDocumentVersion";
    private const string Prefix = EntityType + ":";

    public static string ForVersion(long versionId) => $"{Prefix}{versionId}";

    public static bool TryGetVersionId(string? source, out long versionId)
    {
        versionId = 0;
        return !string.IsNullOrWhiteSpace(source)
            && source.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(source[Prefix.Length..], out versionId)
            && versionId > 0;
    }
}
