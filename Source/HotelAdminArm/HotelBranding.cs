using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HotelAdminArm;

internal static class HotelBranding
{
    public const string HotelName = "Volga Grand Hotel";
    public const string SystemTitle = "АРМ администратора гостиницы";
    public const string Tagline = "Номерной фонд, бронирование, заселение, услуги и оплата в одном окне";
    public const int DesignVersion = 2;

    public static Color Background => Color.FromArgb(246, 247, 242);
    public static Color Surface => Color.White;
    public static Color SurfaceAlt => Color.FromArgb(238, 242, 235);
    public static Color Ink => Color.FromArgb(35, 43, 39);
    public static Color MutedInk => Color.FromArgb(96, 105, 99);
    public static Color Primary => Color.FromArgb(21, 93, 79);
    public static Color PrimaryDark => Color.FromArgb(14, 58, 53);
    public static Color Accent => Color.FromArgb(181, 117, 63);
    public static Color Danger => Color.FromArgb(160, 59, 52);
    public static Color Border => Color.FromArgb(210, 216, 207);
    public static Color GridHeader => Color.FromArgb(31, 80, 70);
    public static Color GridSelection => Color.FromArgb(209, 229, 222);

    public static void ApplyControlTheme(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    textBox.BackColor = Surface;
                    textBox.ForeColor = Ink;
                    break;
                case ComboBox comboBox:
                    comboBox.BackColor = Surface;
                    comboBox.ForeColor = Ink;
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = Surface;
                    numeric.ForeColor = Ink;
                    break;
                case DateTimePicker picker:
                    picker.CalendarTitleBackColor = Primary;
                    picker.CalendarForeColor = Ink;
                    break;
                case CheckBox checkBox:
                    checkBox.ForeColor = Ink;
                    break;
                case Label label:
                    label.ForeColor = Ink;
                    break;
            }

            if (control.HasChildren)
            {
                ApplyControlTheme(control);
            }
        }
    }

    public static Bitmap CreateLogoBitmap(int size = 72)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new LinearGradientBrush(
            new Rectangle(0, 0, size, size),
            Primary,
            Accent,
            45F);
        using var white = new SolidBrush(Color.White);
        using var pen = new Pen(Color.FromArgb(245, 246, 240), Math.Max(2, size / 30));
        using var titleFont = new Font("Segoe UI", size * 0.28F, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", size * 0.13F, FontStyle.Regular, GraphicsUnit.Pixel);

        graphics.FillEllipse(background, 1, 1, size - 2, size - 2);
        graphics.DrawEllipse(pen, 4, 4, size - 8, size - 8);
        DrawCenteredText(graphics, "VG", titleFont, white, new RectangleF(0, size * 0.22F, size, size * 0.34F));
        DrawCenteredText(graphics, "HOTEL", smallFont, white, new RectangleF(0, size * 0.58F, size, size * 0.18F));

        return bitmap;
    }

    public static Icon CreateAppIcon()
    {
        using var bitmap = CreateLogoBitmap(64);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static byte[] CreateRoomImage(string roomNumber, int roomTypeId)
    {
        using var bitmap = new Bitmap(760, 470);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var palette = roomTypeId switch
        {
            1 => (
                Wall: Color.FromArgb(234, 242, 238),
                Floor: Color.FromArgb(205, 185, 158),
                Bed: Color.FromArgb(41, 97, 88),
                Throw: Color.FromArgb(188, 125, 64),
                Accent: Color.FromArgb(228, 212, 179),
                Caption: "Люкс: гостиная зона, мягкий свет, расширенный сервис"),
            2 => (
                Wall: Color.FromArgb(238, 241, 232),
                Floor: Color.FromArgb(199, 178, 145),
                Bed: Color.FromArgb(92, 127, 117),
                Throw: Color.FromArgb(177, 99, 70),
                Accent: Color.FromArgb(222, 230, 217),
                Caption: "Полулюкс: просторный номер повышенной комфортности"),
            3 => (
                Wall: Color.FromArgb(232, 238, 234),
                Floor: Color.FromArgb(193, 174, 145),
                Bed: Color.FromArgb(77, 103, 96),
                Throw: Color.FromArgb(194, 133, 74),
                Accent: Color.FromArgb(238, 232, 213),
                Caption: "Одноместный: спокойный интерьер для деловой поездки"),
            _ => (
                Wall: Color.FromArgb(238, 236, 229),
                Floor: Color.FromArgb(198, 177, 148),
                Bed: Color.FromArgb(61, 96, 89),
                Throw: Color.FromArgb(159, 92, 74),
                Accent: Color.FromArgb(219, 228, 224),
                Caption: "Двухместный: удобное размещение для двух гостей")
        };

        using var wall = new SolidBrush(palette.Wall);
        using var floor = new SolidBrush(palette.Floor);
        using var bed = new SolidBrush(palette.Bed);
        using var bedDark = new SolidBrush(ControlPaint.Dark(palette.Bed));
        using var throwBrush = new SolidBrush(palette.Throw);
        using var accent = new SolidBrush(palette.Accent);
        using var white = new SolidBrush(Color.FromArgb(250, 250, 246));
        using var shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0));
        using var ink = new SolidBrush(Ink);
        using var muted = new SolidBrush(MutedInk);
        using var goldPen = new Pen(Accent, 4);
        using var windowPen = new Pen(Color.FromArgb(170, 188, 183), 3);
        using var titleFont = new Font("Segoe UI", 34, FontStyle.Bold);
        using var subtitleFont = new Font("Segoe UI", 17, FontStyle.Regular);
        using var smallFont = new Font("Segoe UI", 13, FontStyle.Regular);

        graphics.FillRectangle(wall, 0, 0, bitmap.Width, 330);
        graphics.FillRectangle(floor, 0, 330, bitmap.Width, 140);

        using (var light = new LinearGradientBrush(new Rectangle(0, 0, bitmap.Width, bitmap.Height), Color.FromArgb(80, Color.White), Color.Transparent, 90F))
        {
            graphics.FillRectangle(light, 0, 0, bitmap.Width, bitmap.Height);
        }

        DrawRoundedRectangle(graphics, accent, new Rectangle(470, 52, 210, 155), 18);
        DrawRoundedRectangle(graphics, new SolidBrush(Color.FromArgb(232, 245, 241)), new Rectangle(486, 68, 178, 123), 12);
        graphics.DrawLine(windowPen, 575, 70, 575, 190);
        graphics.DrawLine(windowPen, 488, 130, 663, 130);

        DrawRoundedRectangle(graphics, shadow, new Rectangle(88, 300, 470, 32), 18);
        DrawRoundedRectangle(graphics, bedDark, new Rectangle(98, 204, 430, 128), 16);
        DrawRoundedRectangle(graphics, bed, new Rectangle(128, 178, 372, 120), 18);
        DrawRoundedRectangle(graphics, white, new Rectangle(150, 197, 108, 52), 14);
        DrawRoundedRectangle(graphics, white, new Rectangle(275, 197, 108, 52), 14);
        DrawRoundedRectangle(graphics, throwBrush, new Rectangle(132, 266, 365, 30), 12);

        DrawRoundedRectangle(graphics, shadow, new Rectangle(566, 303, 86, 20), 12);
        DrawRoundedRectangle(graphics, new SolidBrush(Color.FromArgb(116, 87, 65)), new Rectangle(574, 226, 68, 86), 8);
        DrawRoundedRectangle(graphics, new SolidBrush(Color.FromArgb(246, 214, 146)), new Rectangle(585, 177, 45, 48), 18);
        graphics.DrawLine(new Pen(Color.FromArgb(91, 72, 58), 4), 607, 226, 607, 252);

        graphics.DrawLine(goldPen, 70, 76, 70, 164);
        graphics.DrawString("Номер " + roomNumber, titleFont, ink, 88, 54);
        graphics.DrawString(HotelName, subtitleFont, muted, 92, 105);
        graphics.DrawString(palette.Caption, smallFont, muted, 92, 145);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void DrawCenteredText(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    public static void DrawRoundedRectangle(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = RoundedRectangle(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle rectangle, int radius)
    {
        using var path = RoundedRectangle(rectangle, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

internal sealed class GradientHeaderPanel : Panel
{
    public GradientHeaderPanel()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(
            ClientRectangle,
            HotelBranding.PrimaryDark,
            HotelBranding.Primary,
            0F);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        using var accentBrush = new SolidBrush(Color.FromArgb(70, HotelBranding.Accent));
        e.Graphics.FillEllipse(accentBrush, Width - 220, -95, 320, 220);
        e.Graphics.FillEllipse(accentBrush, Width - 420, 38, 210, 110);

        base.OnPaint(e);
    }
}
