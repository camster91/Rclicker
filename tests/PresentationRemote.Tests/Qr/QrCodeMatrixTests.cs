using System.Net;
using PresentationRemote.Networking;
using PresentationRemote.Qr;
using PresentationRemote.Sessions;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace PresentationRemote.Tests.Qr;

public class QrCodeMatrixTests
{
    [Fact]
    public void QrCode_DecodesToTheCompleteControllerUrl()
    {
        var url = ControllerUrl.Build(IPAddress.Parse("192.168.100.200"), 65000, SessionToken.Generate()).ToString();

        var matrix = QrCodeMatrix.Create(url);

        Assert.Equal(url, Decode(matrix));
    }

    [Fact]
    public void QrCode_HasQuietZoneAndStaysSmallEnoughToScanFromAcrossARoom()
    {
        var url = ControllerUrl.Build(IPAddress.Parse("192.168.100.200"), 65000, SessionToken.Generate()).ToString();

        var matrix = QrCodeMatrix.Create(url);
        int size = matrix.GetLength(0);

        // Version 5 = 37 modules + 2 * 4 quiet zone. Bigger versions mean smaller modules on screen.
        Assert.True(size <= 37 + (2 * QrCodeMatrix.QuietZoneModules), $"QR is {size} modules");
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
        var ip = IPAddress.Parse("10.0.0.5");
        var before = Decode(QrCodeMatrix.Create(ControllerUrl.Build(ip, 8765, sessions.Current.Token).ToString()));

        sessions.Regenerate();
        var after = Decode(QrCodeMatrix.Create(ControllerUrl.Build(ip, 8765, sessions.Current.Token).ToString()));

        Assert.NotEqual(before, after);
        Assert.Contains(sessions.Current.Token, after, StringComparison.Ordinal);
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
