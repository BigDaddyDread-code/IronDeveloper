using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IChatPromptTemplateProvider
{
    string GetTemplate(ChatPromptTemplate template);
}
