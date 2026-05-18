using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IDiscussionSeedService
{
    Task<DiscussionSeedResult> GenerateDiscussionDocumentsAsync(
        DiscussionSeedRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDiscussionResolverService
{
    Task<DiscussionResolutionResult> ResolveDiscussionAsync(
        DiscussionResolverRequest request,
        CancellationToken cancellationToken = default);
}

public interface IKnowledgeArtefactApplyService
{
    Task<ArtefactApplyResult> ApplyAsync(
        ArtefactApplyRequest request,
        CancellationToken cancellationToken = default);
}
