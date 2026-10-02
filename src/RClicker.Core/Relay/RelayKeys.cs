using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using RClicker.Sessions;

namespace RClicker.Relay;

/// <summary>
/// Everything derived from the session key in the QR code (HMAC-SHA-256 with fixed labels).
/// The phone page derives the same values with WebCrypto. The relay only ever learns
/// <see cref="RoomId"/> and <see cref="PhoneAuthHash"/>, never the key itself.
/// </summary>
public sealed class RelayKeys
{
    public const string RoomLabel = "rclicker/v1/room";
    public const string PhoneLabel = "rclicker/v1/phone";
    public const string EncryptionLabel = "rclicker/v1/enc";

    private RelayKeys(string roomId, string phoneToken, string phoneAuthHash, byte[] encryptionKey)
    {
        RoomId = roomId;
        PhoneToken = phoneToken;
        PhoneAuthHash = phoneAuthHash;
        EncryptionKey = encryptionKey;
    }

    /// <summary>Which relay room to use (22 chars). Not secret.</summary>
    public string RoomId { get; }

    /// <summary>What the phone shows the relay to join the room. Only the phone sends it.</summary>
    public string PhoneToken { get; }

    /// <summary>SHA-256 of <see cref="PhoneToken"/>; the PC registers this with the relay.</summary>
    public string PhoneAuthHash { get; }

    /// <summary>AES-256-GCM key shared by the PC and the phone only.</summary>
    public byte[] EncryptionKey { get; }

    public static RelayKeys Derive(string sessionToken)
    {
        if (!SessionToken.IsWellFormed(sessionToken))
        {
            throw new ArgumentException("Not a session key.", nameof(sessionToken));
        }

        var key = Base64Url.DecodeFromChars(sessionToken);
        var room = Mac(key, RoomLabel);
        var phone = Mac(key, PhoneLabel);
        var enc = Mac(key, EncryptionLabel);

        var phoneToken = Base64Url.EncodeToString(phone);
        var phoneAuthHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(phoneToken)));
        return new RelayKeys(Base64Url.EncodeToString(room.AsSpan(0, 16)), phoneToken, phoneAuthHash, enc);
    }

    private static byte[] Mac(byte[] key, string label) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(label));
}
