namespace AI.Client.Web.Notifications;

internal interface IUnreadCountPublisher
{
    Task PublishAsync(int count);
}
