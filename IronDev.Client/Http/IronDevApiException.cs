using System.Net;

namespace IronDev.Client.Http;

public sealed class IronDevApiException : Exception
{
    public IronDevApiException(
        HttpStatusCode statusCode,
        string message,
        string? responseBody = null,
        string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }
    public string? ErrorCode { get; }
}
