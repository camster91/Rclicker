using System.Drawing.Drawing2D;

namespace RClicker.UI;

/// <summary>
/// Paints a QR module matrix with whole-pixel modules, so it is always crisp (no blurry
/// scaling) and keeps its white quiet zone.
/// </summary>
internal sealed class QrCodeView : Control
{
    private bool[,]? _modules;

    public QrCodeView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.White;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "QR code to open the phone remote";
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool[,]? Modules
    {
        get => _modules;
        set
        {
            _modules = value;
            Invalidate();
        }
    }

    /// <summary>Text drawn instead of the code when there is nothing to show (e.g. no network).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Placeholder { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);

        var modules = _modules;
        if (modules is null)
        {
            TextRenderer.DrawText(g, Placeholder ?? string.Empty, Font, ClientRectangle, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        int count = modules.GetLength(0);
        int available = Math.Min(ClientSize.Width, ClientSize.Height);
        int moduleSize = Math.Max(1, available / count);
        int size = moduleSize * count;
        int left = (ClientSize.Width - size) / 2;
        int top = (ClientSize.Height - size) / 2;

        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.FillRectangle(Brushes.White, left, top, size, size);
        for (int y = 0; y < count; y++)
        {
            for (int x = 0; x < count; x++)
            {
                if (modules[y, x])
                {
                    g.FillRectangle(Brushes.Black, left + (x * moduleSize), top + (y * moduleSize), moduleSize, moduleSize);
                }
            }
        }
    }
}
