namespace IronDev.Core;

public interface ILLMService
{
    Task<string> GetResponseAsync(string prompt);
}