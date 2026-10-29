namespace ShuRuk.Runtime.Contracts;

public interface IEncryptionService
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}
