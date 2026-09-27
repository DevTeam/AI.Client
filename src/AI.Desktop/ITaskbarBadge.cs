namespace AI.Desktop;

internal interface ITaskbarBadge
{
    void Attach(IntPtr windowHandle);

    void SetCount(int count);

    void Detach();
}
