using System.Security.Cryptography;
using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.Runtime.Abstractions.Services;

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
