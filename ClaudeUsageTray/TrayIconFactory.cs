using System.Drawing.Drawing2D;

namespace ClaudeUsageTray;

/// <summary>
/// Renders the small tray icon on the fly (two stacked progress bars - session on top,
/// weekly below) so we don't need to ship .ico assets or update them on every value
/// change. At real tray size (commonly 16x16px) two legible 2-digit numbers don't fit,
/// so the bars themselves - fill length plus colour - carry the at-a-glance signal;
/// exact percentages are in the tooltip and context menu instead.
/// </summary>
public static class TrayIconFactory
{
    public static Icon CreateUsageIcon(int sessionPercent, int weeklyPercent)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            DrawBar(g, sessionPercent, new Rectangle(1, 2, size - 2, 13));
            DrawBar(g, weeklyPercent, new Rectangle(1, 17, size - 2, 13));
        }

        return ToIcon(bitmap);
    }

    public static Icon CreateUnavailableIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pen = new Pen(Color.FromArgb(180, 200, 200, 200), 4f);
            var rect = new RectangleF(3, 3, size - 6, size - 6);
            g.DrawEllipse(pen, rect);

            using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.FromArgb(220, 200, 200, 200));
            var textSize = g.MeasureString("?", font);
            g.DrawString("?", font, textBrush,
                (size - textSize.Width) / 2f,
                (size - textSize.Height) / 2f);
        }

        return ToIcon(bitmap);
    }

    private static void DrawBar(Graphics g, int percent, Rectangle rect)
    {
        var clamped = Math.Clamp(percent, 0, 100);

        using (var trackPath = RoundedRect(rect, 4))
        using (var trackBrush = new SolidBrush(Color.FromArgb(127, Color.White)))
            g.FillPath(trackBrush, trackPath);

        var fillWidth = Math.Max((int)Math.Round(rect.Width * clamped / 100.0), clamped > 0 ? 6 : 0);
        if (fillWidth > 0)
        {
            var fillRect = new Rectangle(rect.X, rect.Y, Math.Min(fillWidth, rect.Width), rect.Height);
            using var fillPath = RoundedRect(fillRect, 4);
            using var fillBrush = new SolidBrush(ColorForPercent(clamped));
            g.FillPath(fillBrush, fillPath);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ColorForPercent(int percent) => percent switch
    {
        > 80 => Color.FromArgb(235, 70, 70),
        > 50 => Color.FromArgb(240, 170, 60),
        _ => Color.FromArgb(90, 190, 110),
    };

    private static Icon ToIcon(Bitmap bitmap)
    {
        var hIcon = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);
}
