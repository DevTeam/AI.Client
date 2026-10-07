namespace AI.Web.Updates;

public interface IPublishedDownloadLinks
{
    Task<PublishedDownloadCatalog> LoadAsync(CancellationToken token = default);
}
