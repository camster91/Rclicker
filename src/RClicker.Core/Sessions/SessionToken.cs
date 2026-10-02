using System.Buffers.Text;
using System.Security.Cryptography;

namespace RClicker.Sessions;

/// <summary>
/// Creates and handles the secret that is embedded in the QR code URL.
/// </summary>
public static class SessionToken
{
    /// <summary>256 bits of randomness (the requirement is at least 128).</summary>
    public const int ByteLength = 32;

    /// <summary>Length of the base64url text form of <see cref="ByteLength"/> bytes, without padding.</summary>
    public const int TextLength = 43;

    /// <summary>Generates a new token from the OS cryptographic random number generator.</summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[ByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>True when <paramref name="token"/> has the exact shape of a generated token.</summary>
    public static bool IsWellFormed(string? token)
    {
        if (token is null || token.Length != TextLength)
        {
            return false;
        }

        foreach (char c in token)
        {
            bool ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Constant-time comparison so response timing does not leak how much of a guess was right.</summary>
    public static bool FixedTimeEquals(string expected, string? candidate)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (candidate is null)
        {
            return false;
        }

        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var candidateBytes = System.Text.Encoding.UTF8.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
    }

    /// <summary>Safe form for logs and UI diagnostics. Never log the full token.</summary>
    public static string Redact(string? token) => token switch
    {
        null or "" => "(none)",
        { Length: <= 8 } => "***",
        _ => string.Concat(token.AsSpan(0, 4), "…"),
    };
}
