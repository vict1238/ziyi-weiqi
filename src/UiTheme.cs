using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace GoDesk
{
    /// <summary>The shared warm desktop palette and embedded presentation assets.</summary>
    public static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(248, 245, 240);
        public static readonly Color Surface = Color.FromArgb(255, 253, 250);
        public static readonly Color Text = Color.FromArgb(55, 46, 36);
        public static readonly Color Muted = Color.FromArgb(132, 121, 108);
        public static readonly Color Accent = Color.FromArgb(153, 104, 53);
        public static readonly Color Border = Color.FromArgb(232, 224, 212);
        public static readonly Color SoftAccent = Color.FromArgb(248, 237, 219);
        public static readonly Color Success = Color.FromArgb(43, 123, 76);
        private static readonly Dictionary<string, Image> images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly object imageLock = new object();
        private static readonly ConditionalWeakTable<Form, object> scaledForms = new ConditionalWeakTable<Form, object>();
        internal static bool Scaling { get; private set; }

        /// <summary>Shared immutable image; consumers must not dispose it.</summary>
        public static Image ResourceImage(string name)
        {
            if (String.IsNullOrEmpty(name)) return null;
            lock (imageLock)
            {
                Image image;
                if (images.TryGetValue(name, out image)) return image;
                try
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui." + name))
                    {
                        if (stream != null) using (Image loaded = Image.FromStream(stream)) image = new Bitmap(loaded);
                        else image = null;
                    }
                }
                catch (ArgumentException) { image = null; }
                catch (IOException) { image = null; }
                images[name] = image;
                return image;
            }
        }

        public static Image Icon(string name) { return ResourceImage("icons." + name + ".png"); }

        // Constructors build all controls in 96-DPI design units. Assigning Font
        // before children exist can otherwise reset WinForms' baseline too early.
        internal static void FinishDpiLayout(Form form, Size designSize)
        {
            object marker; if (scaledForms.TryGetValue(form, out marker)) return;
            float factor; using (Graphics graphics = form.CreateGraphics()) factor = graphics.DpiX / 96f;
            form.AutoScaleMode = AutoScaleMode.None;
            form.Size = designSize;
            form.MinimumSize = new Size(1000, 650);
            Scaling = true;
            try { form.Scale(new SizeF(factor, factor)); }
            finally { Scaling = false; }
            scaledForms.Add(form, new object());
            form.ResumeLayout(true);
            Rectangle area = Screen.FromControl(form).WorkingArea;
            int width = Math.Max(600, area.Width - 24), height = Math.Max(450, area.Height - 24);
            form.MinimumSize = new Size(Math.Min((int)(1000 * factor), width), Math.Min((int)(650 * factor), height));
            form.Size = new Size(Math.Min(form.Width, width), Math.Min(form.Height, height));
        }

        public static void PaintStone(Graphics graphics, RectangleF bounds, Stone color, int opacity = 255)
        {
            if (bounds.Width < 1 || bounds.Height < 1 || color == Stone.Empty) return;
            opacity = Math.Max(0, Math.Min(255, opacity));
            float diameter = bounds.Width;
            using (Brush shadow = new SolidBrush(Color.FromArgb(opacity == 255 ? 46 : 12, 74, 49, 21)))
                graphics.FillEllipse(shadow, bounds.X + diameter * .045f, bounds.Y + diameter * .085f, bounds.Width, bounds.Height);
            using (GraphicsPath outline = new GraphicsPath())
            {
                outline.AddEllipse(bounds);
                using (PathGradientBrush fill = new PathGradientBrush(outline))
                {
                    fill.CenterPoint = new PointF(bounds.X + diameter * .30f, bounds.Y + diameter * .25f);
                    fill.CenterColor = color == Stone.Black ? Color.FromArgb(opacity, 93, 92, 88) : Color.FromArgb(opacity, 255, 255, 253);
                    fill.SurroundColors = new Color[] { color == Stone.Black ? Color.FromArgb(opacity, 19, 20, 19) : Color.FromArgb(opacity, 224, 223, 216) };
                    graphics.FillEllipse(fill, bounds);
                }
            }
            using (Pen edge = new Pen(color == Stone.Black ? Color.FromArgb(opacity, 35, 34, 30) : Color.FromArgb(opacity, 185, 181, 170), Math.Max(.55f, diameter * .019f)))
                graphics.DrawEllipse(edge, bounds);
        }

        internal static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(0, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            if (d < 1) { path.AddRectangle(rect); return path; }
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }

        internal static void DrawIcon(Graphics graphics, string name, Rectangle bounds, bool light, bool enabled)
        {
            Image image = Icon(name);
            if (image == null || bounds.Width < 1 || bounds.Height < 1) return;
            using (ImageAttributes attributes = new ImageAttributes())
            {
                if (light)
                    attributes.SetColorMatrix(new ColorMatrix(new float[][] {
                        new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                        new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, enabled ? 1 : .45f, 0 },
                        new float[] { 1, 1, 1, 0, 1 } }));
                else if (!enabled)
                {
                    ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = .4f;
                    attributes.SetColorMatrix(matrix);
                }
                graphics.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        /// <summary>Apply presentation only, preserving names, validation and event behavior.</summary>
        public static void StyleDialog(Form dialog)
        {
            if (dialog == null) return;
            dialog.BackColor = Background; dialog.ForeColor = Text;
            StyleChildren(dialog);
            object marker;
            if (!scaledForms.TryGetValue(dialog, out marker))
            {
                dialog.AutoScaleMode = AutoScaleMode.None;
                float factor; using (Graphics graphics = dialog.CreateGraphics()) factor = graphics.DpiX / 96f;
                dialog.Scale(new SizeF(factor, factor));
                scaledForms.Add(dialog, new object());
            }
        }
        private static void StyleChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is WarmButton || c is WarmToggle || c is WarmCard) { c.Invalidate(); }
                else if (c is Button)
                {
                    Button b = (Button)c; b.FlatStyle = FlatStyle.Flat; b.BackColor = Surface; b.ForeColor = Text;
                    b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = SoftAccent;
                    b.FlatAppearance.MouseDownBackColor = Color.FromArgb(238, 220, 194);
                }
                else if (c is TextBoxBase)
                {
                    TextBoxBase t = (TextBoxBase)c; t.BackColor = Color.White; t.ForeColor = Text;
                    t.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is ComboBox)
                { ComboBox b = (ComboBox)c; b.BackColor = Color.White; b.ForeColor = Text; b.FlatStyle = FlatStyle.Flat; }
                else if (c is NumericUpDown)
                { NumericUpDown n = (NumericUpDown)c; n.BackColor = Color.White; n.ForeColor = Text; n.BorderStyle = BorderStyle.FixedSingle; }
                else if (c is Label || c is CheckBox || c is RadioButton)
                {
                    Color color = c.ForeColor;
                    if (Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) < 45)
                        c.ForeColor = (color.R + color.G + color.B) / 3 > 95 ? Muted : Text;
                    c.BackColor = Color.Transparent;
                }
                else if (c is Panel && !(c is SplitterPanel))
                    c.BackColor = parent is WarmCard ? Surface : Background;
                if (!(c is NumericUpDown) && !(c is ComboBox) && !(c is TextBoxBase)) StyleChildren(c);
            }
        }
    }

    public class WarmButton : Button
    {
        private bool hover, pressed, primary, selected;
        private string iconName;
        public bool Primary { get { return primary; } set { primary = value; Invalidate(); } }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public string IconName { get { return iconName; } set { iconName = value; Invalidate(); } }
        public WarmButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = UiTheme.Surface; ForeColor = UiTheme.Text;
            Cursor = Cursors.Hand; Height = 34; UseVisualStyleBackColor = false;
            Padding = new Padding(9, 0, 9, 0);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? UiTheme.Background : Parent.BackColor);
            float scale = g.DpiX / 96f;
            bool filled = primary || selected;
            Color fill = filled ? UiTheme.Accent : UiTheme.Surface;
            if (!Enabled) fill = Color.FromArgb(244, 240, 234);
            else if (pressed) fill = filled ? Color.FromArgb(128, 83, 40) : Color.FromArgb(239, 224, 203);
            else if (hover) fill = filled ? Color.FromArgb(172, 122, 66) : UiTheme.SoftAccent;
            RectangleF area = new RectangleF(.5f, .5f, Math.Max(1, Width - 1.5f), Math.Max(1, Height - 1.5f));
            using (GraphicsPath path = UiTheme.Rounded(area, 7 * scale))
            {
                using (Brush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (Pen pen = new Pen(filled && Enabled ? Color.FromArgb(145, 96, 46) : UiTheme.Border, scale)) g.DrawPath(pen, path);
            }
            Color textColor = !Enabled ? Color.FromArgb(178, 168, 155) : filled ? Color.White : UiTheme.Text;
            Image icon = UiTheme.Icon(iconName);
            int iconSize = (int)Math.Round(17 * scale), gap = String.IsNullOrEmpty(Text) ? 0 : (int)Math.Round(7 * scale);
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            // Measuring an empty proposed rectangle with EndEllipsis returns the
            // ellipsis width, shifting centered labels right and clipping them.
            Size textSize = TextRenderer.MeasureText(g, Text, Font, new Size(10000, 1000), flags & ~TextFormatFlags.EndEllipsis);
            int combinedWidth = Math.Min(Width - Padding.Horizontal, textSize.Width + (icon == null ? 0 : iconSize + gap));
            bool alignLeft = TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.TopLeft || TextAlign == ContentAlignment.BottomLeft;
            int start = alignLeft ? Padding.Left : Math.Max(Padding.Left, (Width - combinedWidth) / 2);
            if (icon != null)
            {
                UiTheme.DrawIcon(g, iconName, new Rectangle(start, (Height - iconSize) / 2, iconSize, iconSize), filled && Enabled, Enabled);
                start += iconSize + gap;
            }
            Rectangle textBounds = new Rectangle(start, 1, Math.Max(0, Width - start - Padding.Right), Height - 2);
            TextRenderer.DrawText(g, Text, Font, textBounds, textColor, flags);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -(int)(4 * scale), -(int)(4 * scale)), textColor, fill);
        }
    }

    public class WarmToggle : CheckBox
    {
        private bool hover;
        public WarmToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false; Height = 28; BackColor = Color.Transparent; ForeColor = UiTheme.Text; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = g.DpiX / 96f;
            int w = (int)(31 * scale), h = (int)(17 * scale), x = Math.Max(0, Width - w - 2), y = (Height - h) / 2;
            Color fill = !Enabled ? Color.FromArgb(224, 217, 208) : Checked ? UiTheme.Accent : hover ? Color.FromArgb(195, 180, 161) : Color.FromArgb(209, 198, 184);
            using (GraphicsPath path = UiTheme.Rounded(new RectangleF(x, y, w, h), h / 2f))
            using (Brush brush = new SolidBrush(fill)) g.FillPath(brush, path);
            float knob = h - 4 * scale;
            float knobX = Checked ? x + w - knob - 2 * scale : x + 2 * scale;
            using (Brush shadow = new SolidBrush(Color.FromArgb(25, 71, 48, 20))) g.FillEllipse(shadow, knobX, y + 2 * scale + 1, knob, knob);
            using (Brush white = new SolidBrush(Color.White)) g.FillEllipse(white, knobX, y + 2 * scale, knob, knob);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Math.Max(0, x - (int)(7 * scale)), Height), Enabled ? ForeColor : UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -1, -1));
        }
    }

    public class WarmCard : Panel
    {
        private string title, iconName;
        public string Title { get { return title; } set { title = value; Invalidate(); } }
        public string IconName { get { return iconName; } set { iconName = value; Invalidate(); } }
        public WarmCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Padding = new Padding(12, 28, 12, 10); BackColor = UiTheme.Surface; ForeColor = UiTheme.Text;
            Margin = new Padding(0, 0, 0, 9);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        { e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = g.DpiX / 96f;
            RectangleF bounds = new RectangleF(.5f, .5f, Math.Max(1, Width - 2), Math.Max(1, Height - 3));
            using (GraphicsPath shadowPath = UiTheme.Rounded(new RectangleF(bounds.X + scale, bounds.Y + 2 * scale, bounds.Width, bounds.Height), 10 * scale))
            using (Brush shadow = new SolidBrush(Color.FromArgb(12, 82, 57, 30))) g.FillPath(shadow, shadowPath);
            using (GraphicsPath path = UiTheme.Rounded(bounds, 10 * scale))
            {
                using (Brush surface = new SolidBrush(UiTheme.Surface)) g.FillPath(surface, path);
                using (Pen border = new Pen(UiTheme.Border, scale)) g.DrawPath(border, path);
            }
            if (!String.IsNullOrEmpty(title))
            {
                int x = (int)(12 * scale), iconSize = (int)(17 * scale), top = (int)(10 * scale);
                if (UiTheme.Icon(iconName) != null)
                { UiTheme.DrawIcon(g, iconName, new Rectangle(x, top, iconSize, iconSize), false, true); x += iconSize + (int)(7 * scale); }
                using (Font font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, title, font, new Rectangle(x, top - 1, Math.Max(1, Width - x - (int)(12 * scale)), (int)(22 * scale)), UiTheme.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            base.OnPaint(e);
        }
    }
}
