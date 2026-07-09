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
            // Drawn last as a faint watermark over everything, since the bars leave
            // almost no empty background for a mark placed behind them to show through.
            DrawClaudeMark(g, size / 2f, size / 2f, size / 2f);
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

            DrawClaudeMark(g, size / 2f, size / 2f, size / 2f);

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

    // Claude's brand mark is a rounded 6-ray sparkle/asterisk. Drawn faint and behind
    // the bars, it just hints at "this is Claude" without competing with the bars for
    // attention - this is an homage sketch, not a reproduction of the official asset.
    private static readonly Color ClaudeBrandColor = Color.FromArgb(217, 119, 87);

    private static void DrawClaudeMark(Graphics g, float centerX, float centerY, float radius)
    {
        const int rayCount = 6;
        const int alpha = 55;

        var rayLength = radius * 0.95f;
        var rayWidth = rayLength * 0.30f;

        using var brush = new SolidBrush(Color.FromArgb(alpha, ClaudeBrandColor));
        var state = g.Save();
        g.TranslateTransform(centerX, centerY);
        for (var i = 0; i < rayCount; i++)
        {
            g.RotateTransform(i == 0 ? 0f : 360f / rayCount);
            using var path = new GraphicsPath();
            path.AddPolygon(
            [
                new PointF(0, -rayWidth / 2f),
                new PointF(rayLength * 0.7f, -rayWidth / 5f),
                new PointF(rayLength, 0),
                new PointF(rayLength * 0.7f, rayWidth / 5f),
                new PointF(0, rayWidth / 2f),
            ]);
            g.FillPath(brush, path);
        }
        g.Restore(state);
    }

    private static void DrawBar(Graphics g, int percent, Rectangle rect)
    {
        var clamped = Math.Clamp(percent, 0, 100);

        using (var trackPath = RoundedRect(rect, 4))
        using (var trackBrush = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
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
