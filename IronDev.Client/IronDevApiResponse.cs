namespace IronDev.Client;

public sealed record IronDevApiResponse<T>(
    bool IsSuccess,
    int StatusCode,
    string Status,
    T? Data,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<IronDevApiError> Errors,
    string? RawBody);

public sealed record IronDevApiError(string Code, string Message);
