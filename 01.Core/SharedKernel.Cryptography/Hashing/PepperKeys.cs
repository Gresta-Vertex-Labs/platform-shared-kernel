using System.Diagnostics.CodeAnalysis;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Cryptography.Hashing;

/// <summary>Parses and validates pepper ids and secrets.</summary>
internal static class PepperKeys
{
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 32)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryDecode(string? base64, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        byte[] buffer = new byte[base64.Length * 3 / 4];
        if (!Convert.TryFromBase64String(base64, buffer, out int written)
            || written < OneWayHashingOptions.MinimumPepperBytes)
        {
            return false;
        }

        bytes = buffer[..written];
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
        return true;
    }
}
