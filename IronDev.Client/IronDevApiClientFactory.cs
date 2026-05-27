using System.Net.Http.Headers;

namespace IronDev.Client;

public static class IronDevApiClientFactory
{
    public static IIronDevApiClient Create(
        string apiBaseUrl,
        string? token = null,
        HttpMessageHandler? handler = null)
    {
        var http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);

        http.BaseAddress = new Uri(NormalizeBaseUrl(apiBaseUrl) + "/");
        http.Timeout = TimeSpan.FromMinutes(5);

        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return new IronDevApiClient(http);
    }

    private static string NormalizeBaseUrl(string value) =>
        value.Trim().TrimEnd('/');
}
