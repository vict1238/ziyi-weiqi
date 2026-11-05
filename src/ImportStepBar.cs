using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GoDesk
{
    internal sealed class ImportStepBar : Control
    {
        internal int Stage;
        internal bool IsBusy;
        internal ImportStepBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Background;
        }
        internal static GraphicsPath Round(RectangleF rectangle, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 100 || Height < 30) return;
            Graphics graphics = e.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bounds = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Round(bounds, 11))
            using (Brush background = new SolidBrush(UiTheme.Surface))
            using (Pen edge = new Pen(UiTheme.Border, 1)) { graphics.FillPath(background, path); graphics.DrawPath(edge, path); }
            string[] labels = { "上传图片", "自动识别", "手动校正", "导入棋盘" };
            float step = Width / 4f;
            using (Font labelFont = new Font(Font.FontFamily, 9f, FontStyle.Regular))
            using (Font numberFont = new Font(Font.FontFamily, 9f, FontStyle.Bold))
            {
                for (int i = 0; i < 4; i++)
                {
                    bool current = i == Stage, done = i < Stage;
                    float groupWidth = Math.Min(step - 22, 165), groupX = step * i + (step - groupWidth) / 2;
                    RectangleF group = new RectangleF(groupX, (Height - 33) / 2f, groupWidth, 33);
                    if (current)
                        using (GraphicsPath active = Round(group, 17)) using (Brush fill = new SolidBrush(UiTheme.Accent)) graphics.FillPath(fill, active);
                    Rectangle circle = new Rectangle((int)group.Left + 9, (int)group.Top + 5, 23, 23);
                    Color foreground = current ? Color.White : done ? UiTheme.Accent : UiTheme.Muted;
                    using (Pen ring = new Pen(current ? Color.FromArgb(220, 255, 255, 255) : UiTheme.Border, 1)) graphics.DrawEllipse(ring, circle);
                    TextRenderer.DrawText(graphics, (i + 1).ToString(), numberFont, circle, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    string text = i == 1 && current && IsBusy ? "正在识别" : labels[i];
                    TextRenderer.DrawText(graphics, text, labelFont, new Rectangle(circle.Right + 6, (int)group.Top, (int)group.Right - circle.Right - 12, 33), foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    if (i < 3) using (Pen separator = new Pen(UiTheme.Border, 1)) graphics.DrawLine(separator, (i + 1) * step, 18, (i + 1) * step, Height - 18);
                }
            }
        }
    }
}
