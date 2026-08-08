using System.Security.Cryptography;
using System.Text;

namespace PacToolkits.Api.Auth;

/// <summary>API Key 的 SHA-256 hex 与恒定时间比较</summary>
public static class ApiKeyHasher
{
    public static string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>配置校验：是否为 64 位 hex（大小写不限）</summary>
    public static bool IsSha256Hex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var s = value.Trim();
        if (s.Length != 64)
        {
            return false;
        }

        try
        {
            _ = Convert.FromHexString(s);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool FixedTimeEqualsHex(string presentedHash, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(presentedHash) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(presentedHash.Trim().ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(storedHash.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
