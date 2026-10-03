namespace AI.Application.Skills;

using AI.Contracts.Chats;
using AI.Contracts.Navigation;

public interface IAppGuideLanguageContext
{
    string Create(AppGuideStartRequest request, ChatDetails? visibleChat);
}
