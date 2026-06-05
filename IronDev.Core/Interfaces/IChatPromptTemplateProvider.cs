using IronDev.Core.Chat;

namespace IronDev.Core.Interfaces;

public interface IChatPromptTemplateProvider
{
    string GetTemplate(ChatPromptTemplate template);
}
