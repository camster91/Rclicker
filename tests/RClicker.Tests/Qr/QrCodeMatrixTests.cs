using RClicker.Qr;
using RClicker.Relay;
using RClicker.Sessions;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace RClicker.Tests.Qr;

public class QrCodeMatrixTests
{
    [Fact]
    public void QrCode_DecodesToTheCompleteControllerUrl()
    {
        var url = PhoneUrl(SessionToken.Generate());

        var matrix = QrCodeMatrix.Create(url);

        Assert.Equal(url, Decode(matrix));
    }

    [Fact]
    public void QrCode_HasQuietZoneAndStaysSmallEnoughToScanFromAcrossARoom()
    {
        var url = PhoneUrl(SessionToken.Generate());

        var matrix = QrCodeMatrix.Create(url);
        int size = matrix.GetLength(0);

        // Version 6 = 41 modules + 2 * 4 quiet zone (the https relay link is ~100 characters).
        // Bigger versions mean smaller modules on screen.
        Assert.True(size <= 41 + (2 * QrCodeMatrix.QuietZoneModules), $"QR is {size} modules");
        for (int i = 0; i < size; i++)
        {
            for (int q = 0; q < QrCodeMatrix.QuietZoneModules; q++)
            {
                Assert.False(matrix[q, i]);
                Assert.False(matrix[i, q]);
                Assert.False(matrix[size - 1 - q, i]);
                Assert.False(matrix[i, size - 1 - q]);
            }
        }
    }

    [Fact]
    public void NewSession_ProducesADifferentQrCode()
    {
        var sessions = new SessionManager();
        var before = Decode(QrCodeMatrix.Create(PhoneUrl(sessions.Current.Token)));

        sessions.Regenerate();
        var after = Decode(QrCodeMatrix.Create(PhoneUrl(sessions.Current.Token)));

        Assert.NotEqual(before, after);
        Assert.Contains(sessions.Current.Token, after, StringComparison.Ordinal);
    }

    // Real relay addresses: the old workers.dev one is the longest, so it decides the size limit.
    private static string PhoneUrl(string token, string relayAddress = "https://rclicker.cameron-rotman.workers.dev")
    {
        Assert.True(RelayUrls.TryParse(relayAddress, out var relay));
        return RelayUrls.PhoneUrl(relay, token).ToString();
    }

    [Fact]
    public void QrCode_ForDefaultAddress_IsSmallAndDecodes()
    {
        var url = PhoneUrl(SessionToken.Generate(), "https://clicker.rotmanav.ca");
        var matrix = QrCodeMatrix.Create(url);

        Assert.Equal(url, Decode(matrix));
        Assert.True(matrix.GetLength(0) <= 37 + (2 * QrCodeMatrix.QuietZoneModules), $"QR is {matrix.GetLength(0)} modules"); // version 5
    }

    private static string Decode(bool[,] matrix, int scale = 6)
    {
        int modules = matrix.GetLength(0);
        int size = modules * scale;
        var pixels = new byte[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                pixels[(y * size) + x] = matrix[y / scale, x / scale] ? (byte)0 : (byte)255;
            }
        }

        var source = new RGBLuminanceSource(pixels, size, size, RGBLuminanceSource.BitmapFormat.Gray8);
        var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)));
        Assert.NotNull(result);
        return result.Text;
    }
}
