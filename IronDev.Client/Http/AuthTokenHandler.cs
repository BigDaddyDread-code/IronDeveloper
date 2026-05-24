using System.Net.Http.Headers;
using IronDev.Client.Auth;

namespace IronDev.Client.Http;

public sealed class AuthTokenHandler : DelegatingHandler
{
    private readonly IIronDevSession _session;

    public AuthTokenHandler(IIronDevSession session)
    {
        _session = session;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_session.AccessToken is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
