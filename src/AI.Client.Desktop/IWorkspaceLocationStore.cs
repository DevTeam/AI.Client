namespace AI.Client.Desktop;

internal interface IWorkspaceLocationStore
{
    Uri? Restore(Uri address);

    void Save(Guid? projectId, Guid? chatId, Guid? branchId);
}
