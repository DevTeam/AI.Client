namespace AI.Web.Notifications;

internal interface IUnreadCountPublisher
{
    Task PublishAsync(int count);
}
