using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace GoDesk
{
    public sealed class BoardPointEventArgs : EventArgs
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public BoardPointEventArgs(int x, int y) { X = x; Y = y; }
    }

    public sealed class StudyRegionEventArgs : EventArgs
    {
        public StudyRegion Region { get; private set; }
        public StudyRegionEventArgs(StudyRegion region) { Region = region; }
    }

    public class BoardControl : Control
    {
        private GoGame game;
        private AnalysisResult analysis;
        private bool showHints, showOwnership, showMoveNumbers, selectingRegion, draggingRegion, suppressRegionClick;
        private StudyRegion studyRegion;
        private Point regionStart, regionEnd;
        private readonly MoveNumbersCache moveNumbers = new MoveNumbersCache();
        private int hoverX = -1, hoverY = -1;
        private RectangleF wood, grid;
        private float step;
        public event EventHandler<BoardPointEventArgs> PointClicked;
        public event EventHandler<StudyRegionEventArgs> RegionSelected;
        public GoGame Game
        {
            get { return game; }
            set
            {
                SelectingRegion = false; game = value; hoverX = hoverY = -1; step = 0; moveNumbers.Clear();
                if (studyRegion != null && (game == null || !RegionFits(studyRegion))) studyRegion = null;
                Invalidate();
            }
        }
        public AnalysisResult Analysis { get { return analysis; } set { analysis = value; Invalidate(); } }
        public bool ShowHints { get { return showHints; } set { showHints = value; Invalidate(); } }
        public bool ShowOwnership { get { return showOwnership; } set { showOwnership = value; Invalidate(); } }
        public bool ShowMoveNumbers { get { return showMoveNumbers; } set { showMoveNumbers = value; Invalidate(); } }
        public bool SelectingRegion
        {
            get { return selectingRegion; }
            set
            {
                if (!value) CancelRegionDrag();
                selectingRegion = value; hoverX = hoverY = -1;
                Cursor = value ? Cursors.Cross : Cursors.Hand;
                Invalidate();
            }
        }
        public StudyRegion StudyRegion
        {
            get { return studyRegion; }
            set
            {
                if (value != null && game != null) value.Validate(game.Size);
                studyRegion = value; Invalidate();
            }
        }
        public bool AllowInput { get; set; }

        public BoardControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(242, 240, 234);
            AllowInput = true;
            TabStop = true;
            AccessibleName = "围棋棋盘";
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (game == null || Width < 90 || Height < 90) return;
            float scale = g.DpiX / 96f;
            float side = Math.Min(Width - 24 * scale, Height - 24 * scale);
            if (side <= 0) return;
            wood = new RectangleF((Width - side) / 2, (Height - side) / 2, side, side);
            float inset = Math.Max(24 * scale, side * .061f);
            grid = RectangleF.FromLTRB(wood.Left + inset, wood.Top + inset, wood.Right - inset, wood.Bottom - inset);
            step = grid.Width / (game.Size - 1);
            using (Brush shadow = new SolidBrush(Color.FromArgb(24, 44, 35, 20)))
                g.FillRectangle(shadow, wood.X + 3 * scale, wood.Y + 5 * scale, wood.Width, wood.Height);
            using (Brush woodBrush = new LinearGradientBrush(wood, Color.FromArgb(226, 184, 117), Color.FromArgb(211, 164, 94), 25f))
                g.FillRectangle(woodBrush, wood);
            using (Pen grain = new Pen(Color.FromArgb(13, 126, 82, 27), .7f * scale))
            {
                for (int n = 1; n < 90; n++)
                {
                    float x = wood.Left + n * wood.Width / 90;
                    float bend = (float)Math.Sin(n * 4.2) * 3 * scale;
                    g.DrawBezier(grain, x, wood.Top, x + bend, wood.Top + side / 3, x - bend, wood.Top + side * 2 / 3, x + bend / 2, wood.Bottom);
                }
            }
            using (Pen outline = new Pen(Color.FromArgb(174, 131, 70), scale)) g.DrawRectangle(outline, wood.X, wood.Y, wood.Width, wood.Height);
            DrawStudyRegion(g, scale);
            using (Pen line = new Pen(Color.FromArgb(105, 76, 40), Math.Max(.7f, scale * .8f)))
            {
                for (int n = 0; n < game.Size; n++)
                {
                    float px = grid.Left + n * step, py = grid.Top + n * step;
                    g.DrawLine(line, px, grid.Top, px, grid.Bottom);
                    g.DrawLine(line, grid.Left, py, grid.Right, py);
                }
            }
            DrawCoordinates(g, scale);
            int[] stars = game.Size == 19 ? new int[] { 3, 9, 15 } : game.Size == 13 ? new int[] { 3, 6, 9 } : new int[] { 2, 4, 6 };
            using (Brush star = new SolidBrush(Color.FromArgb(83, 58, 28)))
                foreach (int x in stars) foreach (int y in stars)
                {
                    if (game.Size != 19 && x != y && x + y != game.Size - 1) continue;
                    float radius = Math.Max(2, step * .072f);
                    g.FillEllipse(star, grid.Left + x * step - radius, grid.Top + y * step - radius, radius * 2, radius * 2);
                }
            DrawOwnership(g);
            for (int x = 0; x < game.Size; x++) for (int y = 0; y < game.Size; y++)
                if (game.Board[x, y] != Stone.Empty) DrawStone(g, x, y, game.Board[x, y], 255);
            if (AllowInput && !selectingRegion && hoverX >= 0 && game.Board[hoverX, hoverY] == Stone.Empty)
                DrawStone(g, hoverX, hoverY, game.ToPlay, 100);
            if (game.Position > 0)
            {
                Move last = game.Moves[game.Position - 1];
                if (!last.IsPass && last.X >= 0 && last.X < game.Size && last.Y >= 0 && last.Y < game.Size)
                {
                    float r = step * (showMoveNumbers ? .43f : .15f);
                    using (Pen mark = new Pen(Color.FromArgb(224, 139, 52), Math.Max(1.7f, step * .066f)))
                        g.DrawEllipse(mark, grid.Left + last.X * step - r, grid.Top + last.Y * step - r, r * 2, r * 2);
                }
            }
            if (showMoveNumbers) DrawMoveNumbers(g);
            DrawHints(g);
        }

        private bool RegionFits(StudyRegion region)
        {
            return game != null && region.Left >= 0 && region.Top >= 0 && region.Right < game.Size && region.Bottom < game.Size;
        }

        private void DrawStudyRegion(Graphics g, float scale)
        {
            StudyRegion region = draggingRegion ? new StudyRegion(regionStart.X, regionStart.Y, regionEnd.X, regionEnd.Y) : studyRegion;
            if (region == null || !RegionFits(region)) return;
            float padding = step * .48f;
            RectangleF bounds = RectangleF.FromLTRB(
                Math.Max(wood.Left + scale, grid.Left + region.Left * step - padding),
                Math.Max(wood.Top + scale, grid.Top + region.Top * step - padding),
                Math.Min(wood.Right - scale, grid.Left + region.Right * step + padding),
                Math.Min(wood.Bottom - scale, grid.Top + region.Bottom * step + padding));
            Color color = draggingRegion ? Color.FromArgb(211, 128, 39) : Color.FromArgb(34, 125, 108);
            using (Brush wash = new SolidBrush(Color.FromArgb(draggingRegion ? 27 : 18, color))) g.FillRectangle(wash, bounds);
            using (Pen edge = new Pen(Color.FromArgb(220, color), Math.Max(1.5f * scale, step * .045f)))
            {
                edge.DashStyle = DashStyle.Dash;
                g.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        private void DrawMoveNumbers(Graphics g)
        {
            int[,] numbers = moveNumbers.Get(game);
            using (Brush onBlack = new SolidBrush(Color.FromArgb(252, 252, 248)))
            using (Brush onWhite = new SolidBrush(Color.FromArgb(27, 31, 34)))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic)
                { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                float available = step * .76f;
                var fonts = new Dictionary<int, Font>();
                try
                {
                    for (int x = 0; x < game.Size; x++) for (int y = 0; y < game.Size; y++)
                    {
                        if (numbers[x, y] <= 0 || game.Board[x, y] == Stone.Empty) continue;
                        string label = numbers[x, y].ToString(CultureInfo.InvariantCulture);
                        Font font;
                        if (!fonts.TryGetValue(label.Length, out font))
                        {
                            float fontSize = Math.Max(4f, step * .43f);
                            using (Font measured = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                            {
                                SizeF textSize = g.MeasureString(new string('8', label.Length), measured, PointF.Empty, format);
                                float fit = Math.Min(1f, Math.Min(available / Math.Max(1f, textSize.Width), available / Math.Max(1f, textSize.Height)));
                                font = new Font("Segoe UI", Math.Max(3f, fontSize * fit), FontStyle.Bold, GraphicsUnit.Pixel);
                                fonts.Add(label.Length, font);
                            }
                        }
                        RectangleF area = new RectangleF(grid.Left + x * step - available / 2, grid.Top + y * step - available / 2, available, available);
                        g.DrawString(label, font, game.Board[x, y] == Stone.Black ? onBlack : onWhite, area, format);
                    }
                }
                finally { foreach (Font font in fonts.Values) font.Dispose(); }
            }
        }

        private void DrawCoordinates(Graphics g, float scale)
        {
            float size = Math.Max(8, Math.Min(11 * scale, step * .37f));
            using (Font f = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(Color.FromArgb(109, 77, 37)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < game.Size; i++)
                {
                    string col = BoardCoordinates.ColumnLabel(i, game.Size);
                    string row = BoardCoordinates.RowLabel(i, game.Size);
                    float x = grid.Left + i * step, y = grid.Top + i * step;
                    g.DrawString(col, f, b, new PointF(x, wood.Top + (grid.Top - wood.Top) * .44f), sf);
                    g.DrawString(col, f, b, new PointF(x, wood.Bottom - (wood.Bottom - grid.Bottom) * .41f), sf);
                    g.DrawString(row, f, b, new PointF(wood.Left + (grid.Left - wood.Left) * .43f, y), sf);
                    g.DrawString(row, f, b, new PointF(wood.Right - (wood.Right - grid.Right) * .4f, y), sf);
                }
            }
        }

        private void DrawStone(Graphics g, int x, int y, Stone color, int opacity)
        {
            float diameter = step * .91f;
            RectangleF r = new RectangleF(grid.Left + x * step - diameter / 2, grid.Top + y * step - diameter / 2, diameter, diameter);
            using (Brush b = new SolidBrush(Color.FromArgb(opacity == 255 ? 65 : 14, 66, 47, 22)))
                g.FillEllipse(b, r.X + diameter * .06f, r.Y + diameter * .10f, r.Width, r.Height);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(path))
                {
                    b.CenterPoint = new PointF(r.X + diameter * .31f, r.Y + diameter * .26f);
                    b.CenterColor = color == Stone.Black ? Color.FromArgb(opacity, 92, 94, 96) : Color.FromArgb(opacity, 255, 255, 255);
                    b.SurroundColors = new Color[] { color == Stone.Black ? Color.FromArgb(opacity, 15, 17, 20) : Color.FromArgb(opacity, 207, 208, 203) };
                    g.FillEllipse(b, r);
                }
            }
            using (Pen edge = new Pen(Color.FromArgb(opacity, color == Stone.Black ? Color.FromArgb(24, 25, 27) : Color.FromArgb(170, 171, 166)), .7f))
                g.DrawEllipse(edge, r);
        }

        private void DrawOwnership(Graphics g)
        {
            if (!showOwnership || analysis == null || analysis.Ownership == null || analysis.Ownership.Length < game.Size * game.Size) return;
            for (int y = 0; y < game.Size; y++) for (int x = 0; x < game.Size; x++)
            {
                if (game.Board[x, y] != Stone.Empty) continue;
                double value = analysis.Ownership[y * game.Size + x];
                if (Math.Abs(value) < .12) continue;
                int alpha = (int)(Math.Min(1, Math.Abs(value)) * 175);
                using (Brush b = new SolidBrush(value > 0 ? Color.FromArgb(alpha, 28, 32, 36) : Color.FromArgb(alpha, 255, 255, 248)))
                {
                    float d = step * .32f;
                    g.FillRectangle(b, grid.Left + x * step - d / 2, grid.Top + y * step - d / 2, d, d);
                }
            }
        }

        private void DrawHints(Graphics g)
        {
            if (!showHints || analysis == null || analysis.Candidates == null) return;
            int count = Math.Min(5, analysis.Candidates.Count);
            for (int i = count - 1; i >= 0; i--)
            {
                Move move;
                try { move = GoGame.ParseGtp(analysis.Candidates[i].Move, game.ToPlay, game.Size); }
                catch { continue; }
                if (move.IsPass || move.X < 0 || move.Y < 0 || move.X >= game.Size || move.Y >= game.Size) continue;
                float d = step * .76f;
                RectangleF r = new RectangleF(grid.Left + move.X * step - d / 2, grid.Top + move.Y * step - d / 2, d, d);
                using (Brush b = new SolidBrush(i == 0 ? Color.FromArgb(235, 49, 123, 109) : Color.FromArgb(218, 68, 130, 161))) g.FillEllipse(b, r);
                using (Pen p = new Pen(Color.FromArgb(245, 251, 250), Math.Max(1, step * .035f))) g.DrawEllipse(p, r);
                using (Font f = new Font("Segoe UI", Math.Max(8, step * .38f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(Color.White))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(((char)('A' + i)).ToString(), f, b, r, sf);
            }
        }

        private bool GetPoint(Point p, out int x, out int y)
        {
            x = y = -1;
            if (game == null || step <= 0) return false;
            x = (int)Math.Round((p.X - grid.Left) / step);
            y = (int)Math.Round((p.Y - grid.Top) / step);
            return x >= 0 && y >= 0 && x < game.Size && y < game.Size && Math.Abs(p.X - grid.Left - x * step) < step * .47f && Math.Abs(p.Y - grid.Top - y * step) < step * .47f;
        }

        private bool GetRegionPoint(Point p, out int x, out int y)
        {
            x = y = -1;
            if (game == null || step <= 0) return false;
            float margin = step * .47f;
            if (p.X < grid.Left - margin || p.Y < grid.Top - margin || p.X > grid.Right + margin || p.Y > grid.Bottom + margin) return false;
            x = Math.Max(0, Math.Min(game.Size - 1, (int)Math.Round((p.X - grid.Left) / step)));
            y = Math.Max(0, Math.Min(game.Size - 1, (int)Math.Round((p.Y - grid.Top) / step)));
            return true;
        }

        private void CancelRegionDrag()
        {
            bool wasDragging = draggingRegion;
            draggingRegion = false;
            if (wasDragging)
            {
                suppressRegionClick = true;
                Capture = false;
                Invalidate();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            CancelRegionDrag(); step = 0;
            base.OnSizeChanged(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            // A new gesture clears suppression left by an interrupted selection.
            // The click paired with this selection is suppressed again below.
            suppressRegionClick = false;
            if (!selectingRegion) return;
            suppressRegionClick = true; Focus();
            int x, y;
            if (!GetRegionPoint(e.Location, out x, out y)) return;
            regionStart = regionEnd = new Point(x, y);
            draggingRegion = true; hoverX = hoverY = -1; Capture = true; Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !selectingRegion) return;
            suppressRegionClick = true;
            int x, y;
            if (!draggingRegion || !GetRegionPoint(e.Location, out x, out y))
            {
                CancelRegionDrag(); return;
            }
            var selected = new StudyRegion(regionStart.X, regionStart.Y, x, y);
            selected.Validate(game.Size);
            SelectingRegion = false;
            studyRegion = selected;
            suppressRegionClick = true;
            Invalidate();
            if (RegionSelected != null) RegionSelected(this, new StudyRegionEventArgs(selected));
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (draggingRegion && !Capture) CancelRegionDrag();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int x, y;
            if (selectingRegion)
            {
                if (draggingRegion && GetRegionPoint(e.Location, out x, out y) && (regionEnd.X != x || regionEnd.Y != y))
                { regionEnd = new Point(x, y); Invalidate(); }
                return;
            }
            if (!GetPoint(e.Location, out x, out y)) x = y = -1;
            if (x == hoverX && y == hoverY) return;
            hoverX = x; hoverY = y; Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverX = hoverY = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && (selectingRegion || suppressRegionClick))
            { suppressRegionClick = false; return; }
            int x, y;
            if (e.Button == MouseButtons.Left && AllowInput && GetPoint(e.Location, out x, out y) && PointClicked != null)
                PointClicked(this, new BoardPointEventArgs(x, y));
        }
    }
}
