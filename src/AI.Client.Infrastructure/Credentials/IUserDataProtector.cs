namespace AI.Client.Infrastructure.Credentials;

public interface IUserDataProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] protectedData);
}
