using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace GoDesk
{
    internal sealed class AnalysisTimeline
    {
        private GoGame owner;
        private readonly Dictionary<ulong, double> values = new Dictionary<ulong, double>();
        public void Clear() { owner = null; values.Clear(); }
        public void Record(GoGame game, AnalysisResult result)
        {
            if (game == null || result == null || result.Region != null || result.Visits <= 0 ||
                Double.IsNaN(result.BlackWinrate) || result.BlackWinrate < 0 || result.BlackWinrate > 1) return;
            EnsureOwner(game);
            ulong key = RootKey(game);
            for (int i = 0; i < game.Position; i++) key = AddMove(key, game.Moves[i]);
            values[key] = result.BlackWinrate;
        }
        public double?[] Snapshot(GoGame game)
        {
            EnsureOwner(game);
            double?[] result = new double?[game.Moves.Count + 1];
            ulong key = RootKey(game); double value;
            if (values.TryGetValue(key, out value)) result[0] = value;
            for (int i = 0; i < game.Moves.Count; i++)
            {
                key = AddMove(key, game.Moves[i]);
                if (values.TryGetValue(key, out value)) result[i + 1] = value;
            }
            return result;
        }
        private void EnsureOwner(GoGame game) { if (!Object.ReferenceEquals(owner, game)) { values.Clear(); owner = game; } }
        private static ulong Add(ulong key, int value) { unchecked { for (int i = 0; i < 4; i++) { key ^= (byte)(value >> (i * 8)); key *= 1099511628211UL; } return key; } }
        private static ulong RootKey(GoGame game)
        {
            ulong key = Add(14695981039346656037UL, game.Size);
            string context = game.Rules + ":" + game.Komi.ToString("R", CultureInfo.InvariantCulture);
            foreach (char c in context) key = Add(key, c);
            key = Add(key, (int)game.InitialPlayer);
            // Scan in board order so equivalent SGF property ordering has no effect.
            Stone[,] initial = new Stone[game.Size, game.Size];
            foreach (Move stone in game.InitialStones) initial[stone.X, stone.Y] = stone.Color;
            for (int y = 0; y < game.Size; y++) for (int x = 0; x < game.Size; x++) key = Add(key, (int)initial[x, y]);
            return key;
        }
        private static ulong AddMove(ulong key, Move move) { return Add(Add(Add(key, (int)move.Color), move.X), move.Y); }
    }

    internal sealed class WinrateChart : Control
    {
        private double?[] points = new double?[0];
        public int CurrentPosition { get; set; }
        public event Action<int> MoveSelected;
        public void SetPoints(double?[] value, int current) { points = value ?? new double?[0]; CurrentPosition = current; Invalidate(); }
        internal double?[] Points { get { return (double?[])points.Clone(); } }
        public WinrateChart() { DoubleBuffered = true; BackColor = UiTheme.Surface; Cursor = Cursors.Hand; AccessibleName = "黑方胜率走势，点击查看对应手数"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF area = new RectangleF(PlotLeft, 7, Math.Max(1, Width - PlotLeft - 9), Math.Max(1, Height - Font.Height - 12));
            using (Font font = new Font(Font.FontFamily, 7.5f))
            using (Pen grid = new Pen(UiTheme.Border))
            using (Brush muted = new SolidBrush(UiTheme.Muted))
            {
                for (int n = 0; n <= 2; n++)
                { float y = area.Top + n * area.Height / 2; g.DrawLine(grid, area.Left, y, area.Right, y); g.DrawString((100 - n * 50).ToString() + "%", font, muted, 0, y - 6); }
                bool hasData = false;
                for (int i = 0; i < points.Length; i++) if (points[i].HasValue) { hasData = true; break; }
                if (!hasData) { TextRenderer.DrawText(g, "分析后显示真实胜率走势", font, Rectangle.Round(area), UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
                float denominator = Math.Max(1, points.Length - 1);
                using (Pen line = new Pen(UiTheme.Accent, 1.8f))
                using (Brush dot = new SolidBrush(UiTheme.Accent))
                using (Brush areaBrush = new SolidBrush(Color.FromArgb(25, UiTheme.Accent)))
                {
                    PointF previous = PointF.Empty; bool connected = false;
                    for (int i = 0; i < points.Length; i++)
                    {
                        if (!points[i].HasValue) { connected = false; continue; }
                        PointF p = new PointF(area.Left + area.Width * i / denominator, area.Bottom - (float)points[i].Value * area.Height);
                        if (connected)
                        {
                            g.FillPolygon(areaBrush, new PointF[] { previous, p, new PointF(p.X, area.Bottom), new PointF(previous.X, area.Bottom) });
                            g.DrawLine(line, previous, p);
                        }
                        g.FillEllipse(dot, p.X - 2, p.Y - 2, 4, 4); previous = p; connected = true;
                    }
                }
                using (Pen cursor = new Pen(Color.FromArgb(135, UiTheme.Text), 1))
                { cursor.DashStyle = DashStyle.Dot; float x = area.Left + area.Width * Math.Min(CurrentPosition, Math.Max(0, points.Length - 1)) / denominator; g.DrawLine(cursor, x, area.Top, x, area.Bottom); }
                g.DrawString("0 手", font, muted, area.Left, area.Bottom + 3);
                string last = Math.Max(0, points.Length - 1) + " 手"; SizeF measure = g.MeasureString(last, font); g.DrawString(last, font, muted, area.Right - measure.Width, area.Bottom + 3);
            }
        }
        private float PlotLeft { get { return Math.Max(29, Font.Height * 2 + 5); } }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left || points.Length == 0) return;
            int position = (int)Math.Round(Math.Max(0, Math.Min(1, (e.X - PlotLeft) / Math.Max(1, Width - PlotLeft - 9))) * (points.Length - 1));
            if (MoveSelected != null) MoveSelected(position);
        }
    }

    internal sealed class CandidateTable : Control
    {
        private AnalysisResult analysis;
        private int size = 19;
        public CandidateTable() { DoubleBuffered = true; BackColor = UiTheme.Surface; AccessibleName = "推荐着点列表，胜率与目差均为黑方视角"; }
        public void ShowResult(AnalysisResult result, int boardSize) { analysis = result; size = boardSize; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics;
            using (Font small = new Font(Font.FontFamily, 8f))
            using (Font bold = new Font(Font.FontFamily, 8.5f, FontStyle.Bold))
            {
                Rectangle header = new Rectangle(0, 0, Width, 20);
                TextRenderer.DrawText(g, "前三推荐着点          黑方胜率     目差", small, header, UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                if (analysis == null || analysis.Candidates == null || analysis.Candidates.Count == 0)
                { TextRenderer.DrawText(g, "点击“推荐着点”查看 AI 建议", small, new Rectangle(0, 23, Width, 30), UiTheme.Muted, TextFormatFlags.Left); return; }
                int count = Math.Min(3, analysis.Candidates.Count), rowHeight = Math.Max(19, Math.Min(24, (Height - 20) / count));
                for (int i = 0; i < count; i++)
                {
                    MoveCandidate c = analysis.Candidates[i]; Rectangle row = new Rectangle(0, 20 + i * rowHeight, Width, rowHeight);
                    if (i == 0) using (Brush brush = new SolidBrush(Color.FromArgb(24, UiTheme.Accent))) g.FillRectangle(brush, row);
                    TextRenderer.DrawText(g, ((char)('A' + i)) + "   " + BoardCoordinates.FromGtp(c.Move, size), i == 0 ? bold : small, new Rectangle(7, row.Y, Width / 2 - 9, row.Height), UiTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, (c.Winrate * 100).ToString("0.0") + "%", small, new Rectangle(Width / 2, row.Y, Width / 4, row.Height), UiTheme.Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, c.ScoreLead.ToString("+0.0;-0.0;0.0"), small, new Rectangle(Width * 3 / 4, row.Y, Width / 4 - 4, row.Height), UiTheme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
