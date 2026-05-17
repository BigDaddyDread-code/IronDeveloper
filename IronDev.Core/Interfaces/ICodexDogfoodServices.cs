using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface ILanguageSemanticIndexer
{
    string LanguageId { get; }
    string Confidence { get; }

    bool CanHandle(string filePath);

    Task<IReadOnlyList<SemanticSymbolInfo>> IndexAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);
}

public interface ICodexSnapshotBuilder
{
    Task<CodexProjectSnapshot> BuildSnapshotAsync(
        CodexSnapshotBuildRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICodexContextQualityScorer
{
    CodexContextQualityResult Score(CodexProjectSnapshot snapshot);
}

public interface ICodexTicketGroundingValidator
{
    IReadOnlyList<CodebaseTicketDraft> ValidateAndScore(
        IReadOnlyList<CodebaseTicketDraft> tickets,
        CodexProjectSnapshot snapshot);
}

public interface IProjectSemanticIndexService
{
    Task<SemanticIndex> IndexProjectAsync(
        string solutionOrProjectPath,
        CancellationToken cancellationToken = default);
}
