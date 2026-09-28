namespace AI.Server.Hosting;

public interface IBrowserAccessService
{
    string Grant();
    bool Allows(string? token);
    bool Revoke(string? token);

    /// <summary>A short-lived, single-use code that a browser opened by the Host exchanges for a grant.</summary>
    string CreatePairingCode();

    /// <summary>The grant for a code from <see cref="CreatePairingCode"/>, or null when it is unknown, used or expired.</summary>
    string? RedeemPairingCode(string? code);
}
