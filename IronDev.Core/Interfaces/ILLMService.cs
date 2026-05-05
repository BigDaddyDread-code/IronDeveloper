using System.Threading;
using System.Threading.Tasks;

namespace IronDev.Core;

public interface ILLMService
{
    Task<string> GetResponseAsync(string prompt, CancellationToken ct = default);
}