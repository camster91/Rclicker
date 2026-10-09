using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using RClicker.UI;

namespace RClicker.Tests.UI;

public class PdfTargetUiTests
{
    [Fact]
    public void PdfModeIsSelectableAndKeepsForegroundProtection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(null, null, NullLoggerFactory.Instance);
                form.Show();
                Application.DoEvents();
                var controls = Descendants(form).ToArray();
                var target = Assert.Single(controls.OfType<ComboBox>());
                var restriction = Assert.Single(controls.OfType<CheckBox>());
                Assert.Equal(0, target.SelectedIndex);
                restriction.Checked = false;
                target.SelectedIndex = 1;
                Assert.True(restriction.Checked);
                Assert.False(restriction.Enabled);
                Assert.Contains(controls.OfType<Label>(), l => l.Text.Contains("Black is unavailable.", StringComparison.Ordinal));
                foreach (var size in new[] { new Size(460, 700), new Size(400, 600) })
                {
                    form.ClientSize = size;
                    Application.DoEvents();
                    var bounds = form.RectangleToClient(target.RectangleToScreen(target.ClientRectangle));
                    Assert.True(bounds.Left >= 0 && bounds.Right <= form.ClientSize.Width, "App selector is clipped.");
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    var folder = Path.Combine(AppContext.BaseDirectory, "TestResults");
                    Directory.CreateDirectory(folder);
                    bitmap.Save(Path.Combine(folder, $"pdf-mode-{size.Width}.png"));
                }
                target.SelectedIndex = 0;
                Assert.True(restriction.Enabled);
                Assert.True(restriction.Checked);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI fixture did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
