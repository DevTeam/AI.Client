namespace AI.Server.Hosting;

public interface IBrowserAccessService
{
    string Grant();
    bool Allows(string? token);
    bool Revoke(string? token);
}
