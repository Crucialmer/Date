using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Date.Services;

public static class TrayIconGenerator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 在内存中动态绘制 macOS 风格的微型日历图标（含当日号数）
    /// </summary>
    public static Icon GenerateCalendarIcon(int dayNumber, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 外边距与圆角
            int p = Math.Max(1, (int)(size * 0.08));
            int w = size - 2 * p;
            int h = size - 2 * p;
            int r = Math.Max(2, (int)(size * 0.22));

            using var path = new GraphicsPath();
            path.AddArc(p, p, r * 2, r * 2, 180, 90);
            path.AddArc(p + w - r * 2, p, r * 2, r * 2, 270, 90);
            path.AddArc(p + w - r * 2, p + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(p, p + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();

            // 1. 卡片主体背景 - 纯净微暖白
            using (var bodyBrush = new SolidBrush(Color.FromArgb(250, 250, 252)))
            {
                g.FillPath(bodyBrush, path);
            }

            // 2. 顶部红标栏 (macOS Calendar Red)
            int headerH = Math.Max(3, (int)(h * 0.32));
            var headerRect = new Rectangle(p, p, w, headerH);
            g.SetClip(path);
            using (var headerBrush = new SolidBrush(Color.FromArgb(255, 59, 48)))
            {
                g.FillRectangle(headerBrush, headerRect);
            }
            g.ResetClip();

            // 3. 卡片外轮廓细边框
            using (var borderPen = new Pen(Color.FromArgb(160, 40, 40, 40), 1))
            {
                g.DrawPath(borderPen, path);
            }

            // 4. 当日号数文字 (例如 22)
            string text = dayNumber.ToString();
            float fontSize = (float)Math.Max(6.0, size * (text.Length > 2 ? 0.32 : 0.38));
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(28, 28, 30));
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            var textRect = new RectangleF(p, p + headerH - (size * 0.04f), w, h - headerH + (size * 0.04f));
            g.DrawString(text, font, textBrush, textRect, sf);
        }

        // 转换为 GDI Icon 并安全销毁底层句柄避免句柄泄漏
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
