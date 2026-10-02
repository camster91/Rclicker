using QRCoder;

namespace RClicker.Qr;

/// <summary>
/// Generates the QR code as a plain module matrix (true = dark). The desktop UI paints it
/// with whole-pixel modules so it stays sharp at any size.
/// </summary>
public static class QrCodeMatrix
{
    /// <summary>Quiet zone (white border) in modules. The QR spec asks for 4.</summary>
    public const int QuietZoneModules = 4;

    public static bool[,] Create(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        using var generator = new QRCodeGenerator();

        // Medium error correction: robust on projectors/monitors while keeping modules large.
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var rows = data.ModuleMatrix; // Includes a 4-module quiet zone.
        int size = rows.Count;
        var matrix = new bool[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                matrix[y, x] = rows[y][x];
            }
        }

        return matrix;
    }
}
