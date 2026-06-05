using System.Net.Http.Headers;
using IronDev.Client.Chat;

namespace IronDev.Client;

/// <summary>
/// Creates standalone <see cref="IChatApiClient"/> instances for use by
/// CLI tools and test runners that don't use the full DI stack.
/// </summary>
public static class ChatApiClientFactory
{
    public static IChatApiClient Create(
        string apiBaseUrl,
        string? token = null,
        HttpMessageHandler? handler = null)
    {
        var http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);

        var baseUrl = apiBaseUrl.Trim().TrimEnd('/') + "/api/";
        http.BaseAddress = new Uri(baseUrl);
        http.Timeout     = TimeSpan.FromMinutes(5);

        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

        return new ChatApiClient(http);
    }
}
