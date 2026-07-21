namespace ShuRuk.Contracts.Interfaces;

public interface IEncryptionService
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}
