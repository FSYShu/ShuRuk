using System.Security.Cryptography;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.Infrastructure.Services;

public sealed class EncryptionService : IEncryptionService
{
    public byte[] Protect(byte[] data)
    {
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] data)
    {
        return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
    }
}
