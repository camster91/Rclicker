using RClicker.Relay;
using RClicker.Sessions;

namespace RClicker.Tests.Relay;

public class RelayCryptoTests
{
    // Key = bytes 0..31. Expected values computed independently with Python (hmac, hashlib,
    // cryptography.AESGCM); the relay's JS tests check the browser side against the same values.
    private const string VectorKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";
    private const string VectorRoom = "bncOdSXPsr83gBpAiVfdXA";
    private const string VectorPhoneToken = "tF7RBRIVqAILOAVAhmjK6Wb-tazsqrv3GOizNQKYTdI";
    private const string VectorPhoneAuthHash = "ShJRZDxA8UMVn7WtNFVrxvMlfFXszqkKxXsz78HldQ4";
    private const string VectorEncKeyHex = "258fd6ccbc209bb7d00443fd4ba308238de0cad7794d7f3da64bd5d47dd3924d";
    private const string VectorIv = "AAECAwQFBgcICQoL";
    private const string VectorCiphertext = "rolBs-nc5q9ZPh-mYywM-5RegNXM-2vbBW7a5IxQsc6bIh8gL6dZxVZ1bktYSWGH3tvZeiLwcYYnttyPLp2w";

    [Fact]
    public void Derive_MatchesIndependentImplementation()
    {
        var keys = RelayKeys.Derive(VectorKey);

        Assert.Equal(VectorRoom, keys.RoomId);
        Assert.Equal(VectorPhoneToken, keys.PhoneToken);
        Assert.Equal(VectorPhoneAuthHash, keys.PhoneAuthHash);
        Assert.Equal(VectorEncKeyHex, Convert.ToHexStringLower(keys.EncryptionKey));
    }

    [Fact]
    public void Decrypt_ReadsCiphertextFromIndependentImplementation()
    {
        using var cipher = new RelayCipher(RelayKeys.Derive(VectorKey));

        Assert.True(cipher.TryDecrypt(VectorIv, VectorCiphertext, RelayDirection.PhoneToHost, out var plain));
        Assert.Equal("""{"type":"presentation.next","id":1,"nonce":"n"}""", plain);
    }

    [Fact]
    public void Derive_DifferentSessionsShareNothing()
    {
        var a = RelayKeys.Derive(SessionToken.Generate());
        var b = RelayKeys.Derive(SessionToken.Generate());

        Assert.NotEqual(a.RoomId, b.RoomId);
        Assert.NotEqual(a.PhoneToken, b.PhoneToken);
        Assert.NotEqual(a.EncryptionKey, b.EncryptionKey);
        Assert.Equal(22, a.RoomId.Length);
        Assert.Equal(43, a.PhoneToken.Length);
    }

    [Fact]
    public void Derive_RejectsMalformedKeys()
    {
        Assert.Throws<ArgumentException>(() => RelayKeys.Derive("short"));
    }

    [Fact]
    public void Encrypt_RoundTrips_WithFreshIvEachTime()
    {
        using var cipher = new RelayCipher(RelayKeys.Derive(SessionToken.Generate()));

        var first = cipher.Encrypt("hello", RelayDirection.HostToPhone);
        var second = cipher.Encrypt("hello", RelayDirection.HostToPhone);

        Assert.NotEqual(first.Iv, second.Iv);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.True(cipher.TryDecrypt(first.Iv, first.Ciphertext, RelayDirection.HostToPhone, out var plain));
        Assert.Equal("hello", plain);
    }

    [Fact]
    public void Decrypt_FailsForWrongDirection_OtherSession_OrTampering()
    {
        var keys = RelayKeys.Derive(SessionToken.Generate());
        using var cipher = new RelayCipher(keys);
        using var other = new RelayCipher(RelayKeys.Derive(SessionToken.Generate()));
        var (iv, ct) = cipher.Encrypt("""{"type":"presentation.next"}""", RelayDirection.PhoneToHost);

        Assert.False(cipher.TryDecrypt(iv, ct, RelayDirection.HostToPhone, out _)); // reflected back
        Assert.False(other.TryDecrypt(iv, ct, RelayDirection.PhoneToHost, out _)); // another room
        var tampered = (ct[0] == 'A' ? 'B' : 'A') + ct[1..];
        Assert.False(cipher.TryDecrypt(iv, tampered, RelayDirection.PhoneToHost, out _));
        Assert.False(cipher.TryDecrypt("bad!", ct, RelayDirection.PhoneToHost, out _));
        Assert.False(cipher.TryDecrypt(iv, "AAAA", RelayDirection.PhoneToHost, out _));
        Assert.False(cipher.TryDecrypt(null, ct, RelayDirection.PhoneToHost, out _));
    }
}
