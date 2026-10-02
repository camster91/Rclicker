using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace RClicker.Relay;

public enum RelayDirection
{
    PhoneToHost,
    HostToPhone,
}

/// <summary>
/// AES-256-GCM end-to-end encryption between the PC and the phone. The additional data binds
/// each message to its room and direction, so the relay cannot reflect or move messages.
/// Wire format matches WebCrypto: ciphertext followed by the 16-byte tag, base64url.
/// </summary>
public sealed class RelayCipher : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly AesGcm _aes;
    private readonly byte[] _phoneToHostAad;
    private readonly byte[] _hostToPhoneAad;

    public RelayCipher(RelayKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _aes = new AesGcm(keys.EncryptionKey, TagSize);
        _phoneToHostAad = Encoding.UTF8.GetBytes("rclicker/v1/p2h/" + keys.RoomId);
        _hostToPhoneAad = Encoding.UTF8.GetBytes("rclicker/v1/h2p/" + keys.RoomId);
    }

    public (string Iv, string Ciphertext) Encrypt(string plaintext, RelayDirection direction)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[plain.Length + TagSize];
        _aes.Encrypt(nonce, plain, output.AsSpan(0, plain.Length), output.AsSpan(plain.Length), Aad(direction));
        return (Base64Url.EncodeToString(nonce), Base64Url.EncodeToString(output));
    }

    /// <summary>False if the message was tampered with, is for another room/direction, or is not ours.</summary>
    public bool TryDecrypt(string? iv, string? ciphertext, RelayDirection direction, out string plaintext)
    {
        plaintext = string.Empty;
        if (iv is null || ciphertext is null)
        {
            return false;
        }

        byte[] nonce;
        byte[] data;
        try
        {
            nonce = Base64Url.DecodeFromChars(iv);
            data = Base64Url.DecodeFromChars(ciphertext);
        }
        catch (FormatException)
        {
            return false;
        }

        if (nonce.Length != NonceSize || data.Length < TagSize)
        {
            return false;
        }

        var plain = new byte[data.Length - TagSize];
        try
        {
            _aes.Decrypt(nonce, data.AsSpan(0, plain.Length), data.AsSpan(plain.Length), plain, Aad(direction));
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }

        try
        {
            plaintext = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(plain);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    public void Dispose() => _aes.Dispose();

    private byte[] Aad(RelayDirection direction) =>
        direction == RelayDirection.PhoneToHost ? _phoneToHostAad : _hostToPhoneAad;
}
