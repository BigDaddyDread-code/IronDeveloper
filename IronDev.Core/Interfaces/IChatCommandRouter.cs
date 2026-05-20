using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IChatCommandRouter
{
    Task<ChatRouteResult> RouteAsync(ChatTurnInput input, CancellationToken cancellationToken = default);
}
