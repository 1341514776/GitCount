using System.Security.Cryptography;
using System.Text;

namespace GitCount.Services;

public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GitCount.GitLab.Token.v1");

    public static string Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            var unprotectedBytes = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotectedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
