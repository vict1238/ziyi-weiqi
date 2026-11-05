using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GoDesk
{
    /// <summary>The four OUTER GRID intersections, clockwise, in source image pixels.</summary>
    public sealed class BoardImageGeometry
    {
        public PointF TopLeft, TopRight, BottomRight, BottomLeft;
        public int Columns = 19, Rows = 19;

        public BoardImageGeometry Clone()
        {
            return new BoardImageGeometry { TopLeft = TopLeft, TopRight = TopRight,
                BottomRight = BottomRight, BottomLeft = BottomLeft, Columns = Columns, Rows = Rows };
        }

        public void Validate(int width, int height)
        {
            if (Columns < 2 || Columns > 19 || Rows < 2 || Rows > 19)
                throw new ArgumentException("可见网格的列数和行数必须在 2 到 19 之间。");
            PointF[] q = { TopLeft, TopRight, BottomRight, BottomLeft };
            double area = 0;
            for (int i = 0; i < 4; i++)
            {
                PointF p = q[i], next = q[(i + 1) % 4], after = q[(i + 2) % 4];
                if (Single.IsNaN(p.X) || Single.IsNaN(p.Y) || Single.IsInfinity(p.X) || Single.IsInfinity(p.Y) ||
                    p.X < 0 || p.Y < 0 || p.X > width - 1 || p.Y > height - 1)
                    throw new ArgumentException("请将四个校准点放在图片内的格线交叉点上。");
                double cross = (next.X - p.X) * (after.Y - next.Y) - (next.Y - p.Y) * (after.X - next.X);
                if (cross <= .01) throw new ArgumentException("校准四角必须按左上、右上、右下、左下排列，不能交叉或重合。");
                double dx = next.X - p.X, dy = next.Y - p.Y;
                double cells = i % 2 == 0 ? Columns - 1 : Rows - 1;
                if (Math.Sqrt(dx * dx + dy * dy) / cells < 3)
                    throw new ArgumentException("网格太小，请选择更清晰的图片或减少可见行列数。");
                area += p.X * next.Y - next.X * p.Y;
            }
            if (area < 100) throw new ArgumentException("校准范围太小。");
        }

        public PointF Project(double column, double row)
        {
            double u = column / (Columns - 1), v = row / (Rows - 1);
            double dx1 = TopRight.X - BottomRight.X, dx2 = BottomLeft.X - BottomRight.X;
            double dy1 = TopRight.Y - BottomRight.Y, dy2 = BottomLeft.Y - BottomRight.Y;
            double dx3 = TopLeft.X - TopRight.X + BottomRight.X - BottomLeft.X;
            double dy3 = TopLeft.Y - TopRight.Y + BottomRight.Y - BottomLeft.Y;
            double g = 0, h = 0, determinant = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(dx3) + Math.Abs(dy3) > .000001)
            {
                if (Math.Abs(determinant) < .000001) throw new ArgumentException("校准点不能确定有效透视网格。");
                g = (dx3 * dy2 - dx2 * dy3) / determinant;
                h = (dx1 * dy3 - dx3 * dy1) / determinant;
            }
            double denominator = 1 + g * u + h * v;
            if (Math.Abs(denominator) < .000001) throw new ArgumentException("透视角度过大，请重新校准四角。");
            return new PointF((float)(((TopRight.X - TopLeft.X + g * TopRight.X) * u +
                (BottomLeft.X - TopLeft.X + h * BottomLeft.X) * v + TopLeft.X) / denominator),
                (float)(((TopRight.Y - TopLeft.Y + g * TopRight.Y) * u +
                (BottomLeft.Y - TopLeft.Y + h * BottomLeft.Y) * v + TopLeft.Y) / denominator));
        }
    }

    public sealed class BoardImageResult
    {
        public Stone[,] Stones;
        public bool[,] Uncertain;
        public BoardImageGeometry Geometry;
    }

    /// <summary>Offline grid/stone recognition. A recognition is always an editable draft.</summary>
    public static class BoardImageRecognizer
    {
        private sealed class Pixels
        {
            internal int Width, Height;
            internal double Scale;
            private byte[] rgb;
            internal Pixels(Bitmap source, int maximum)
            {
                Scale = Math.Min(1.0, maximum / (double)Math.Max(source.Width, source.Height));
                Width = Math.Max(1, (int)Math.Round(source.Width * Scale));
                Height = Math.Max(1, (int)Math.Round(source.Height * Scale));
                using (Bitmap copy = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(copy))
                    {
                        graphics.Clear(Color.White);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        graphics.DrawImage(source, new Rectangle(0, 0, Width, Height));
                    }
                    BitmapData data = copy.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        rgb = new byte[Width * Height * 4];
                        for (int y = 0; y < Height; y++)
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), rgb, y * Width * 4, Width * 4);
                    }
                    finally { copy.UnlockBits(data); }
                }
            }
            internal Color Get(int x, int y)
            {
                x = Math.Max(0, Math.Min(Width - 1, x)); y = Math.Max(0, Math.Min(Height - 1, y));
                int i = (y * Width + x) * 4;
                return Color.FromArgb(rgb[i + 2], rgb[i + 1], rgb[i]);
            }
            internal bool TryGet(PointF original, out Color color)
            {
                int x = (int)Math.Round(original.X * Scale), y = (int)Math.Round(original.Y * Scale);
                if (x < 0 || y < 0 || x >= Width || y >= Height) { color = Color.Empty; return false; }
                color = Get(x, y); return true;
            }
        }

        public static BoardImageGeometry Detect(Bitmap image, int expectedSize)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (image.Width < 20 || image.Height < 20) throw new ArgumentException("图片太小，无法辨认棋盘。");
            if (expectedSize < 2 || expectedSize > 19) throw new ArgumentOutOfRangeException("expectedSize");
            Pixels pixels = new Pixels(image, 1200);
            BoardImageGeometry woodQuad;
            Rectangle wood = FindBoard(pixels, out woodQuad);
            int rectWidth = wood.Width, rectHeight = wood.Height;
            double[] horizontal = new double[rectHeight], vertical = new double[rectWidth];
            for (int y = 0; y < rectHeight; y++) for (int x = 0; x < rectWidth; x++)
            {
                PointF point = woodQuad.Project(x / (double)(rectWidth - 1), y / (double)(rectHeight - 1));
                Color color = pixels.Get((int)Math.Round(point.X), (int)Math.Round(point.Y));
                double darkness = 255 - Luma(color);
                horizontal[y] += darkness / rectWidth;
                vertical[x] += darkness / rectHeight;
            }
            int minLength = Math.Min(wood.Width, wood.Height);
            Comb xfit = FitComb(vertical, Math.Max(5, minLength / 24.0), minLength / 4.0);
            Comb yfit = FitComb(horizontal, Math.Max(5, minLength / 24.0), minLength / 4.0);
            bool found = xfit.Count >= 5 && xfit.Count <= 19 && yfit.Count >= 5 && yfit.Count <= 19;
            double left, right, top, bottom;
            int columns, rows;
            if (found)
            {
                left = xfit.First; right = xfit.Last;
                top = yfit.First; bottom = yfit.Last;
                columns = xfit.Count; rows = yfit.Count;
            }
            else
            {
                // Photos may have converging lines. The editable four-corner overlay
                // is intentionally retained rather than pretending this is exact.
                left = rectWidth * .065; right = rectWidth * .935;
                top = rectHeight * .065; bottom = rectHeight * .935;
                columns = rows = expectedSize;
            }
            double scale = pixels.Scale;
            PointF a = woodQuad.Project(left / (rectWidth - 1), top / (rectHeight - 1));
            PointF b = woodQuad.Project(right / (rectWidth - 1), top / (rectHeight - 1));
            PointF c = woodQuad.Project(right / (rectWidth - 1), bottom / (rectHeight - 1));
            PointF d = woodQuad.Project(left / (rectWidth - 1), bottom / (rectHeight - 1));
            BoardImageGeometry geometry = new BoardImageGeometry { Columns = columns, Rows = rows,
                TopLeft = new PointF((float)(a.X / scale), (float)(a.Y / scale)),
                TopRight = new PointF((float)(b.X / scale), (float)(b.Y / scale)),
                BottomRight = new PointF((float)(c.X / scale), (float)(c.Y / scale)),
                BottomLeft = new PointF((float)(d.X / scale), (float)(d.Y / scale)) };
            geometry.Validate(image.Width, image.Height);
            return geometry;
        }

        private static Rectangle FindBoard(Pixels pixels, out BoardImageGeometry quad)
        {
            int w = pixels.Width, h = pixels.Height;
            bool[] warm = new bool[w * h], expanded = new bool[w * h], visited = new bool[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                Color p = pixels.Get(x, y);
                warm[y * w + x] = p.R > 75 && p.R >= p.G * .94 && p.G - p.B > 29 && p.R - p.B > 49;
            }
            // Bridge thin grid lines and wood grain; the stone-sized holes remain.
            int radius = Math.Max(1, Math.Min(w, h) / 200);
            int[] integral = new int[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                int row = 0;
                for (int x = 0; x < w; x++)
                {
                    if (warm[y * w + x]) row++;
                    integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + row;
                }
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int l = Math.Max(0, x - radius), r = Math.Min(w, x + radius + 1);
                int t = Math.Max(0, y - radius), b = Math.Min(h, y + radius + 1);
                expanded[y * w + x] = integral[b * (w + 1) + r] - integral[t * (w + 1) + r] -
                    integral[b * (w + 1) + l] + integral[t * (w + 1) + l] > 0;
            }
            int[] queue = new int[w * h];
            int bestCount = 0;
            Rectangle best = new Rectangle((int)(w * .08), (int)(h * .08), (int)(w * .84), (int)(h * .84));
            PointF[] bestCorners = null;
            for (int seed = 0; seed < expanded.Length; seed++)
            {
                if (!expanded[seed] || visited[seed]) continue;
                int head = 0, tail = 0, minX = w, minY = h, maxX = 0, maxY = 0;
                PointF[] corners = { new PointF(w, h), new PointF(0, h), new PointF(0, 0), new PointF(w, 0) };
                queue[tail++] = seed; visited[seed] = true;
                while (head < tail)
                {
                    int current = queue[head++], x = current % w, y = current / w;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    if (x + y < corners[0].X + corners[0].Y) corners[0] = new PointF(x, y);
                    if (x - y > corners[1].X - corners[1].Y) corners[1] = new PointF(x, y);
                    if (x + y > corners[2].X + corners[2].Y) corners[2] = new PointF(x, y);
                    if (x - y < corners[3].X - corners[3].Y) corners[3] = new PointF(x, y);
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int next = direction == 0 ? (x > 0 ? current - 1 : -1) :
                            direction == 1 ? (x + 1 < w ? current + 1 : -1) :
                            direction == 2 ? (y > 0 ? current - w : -1) : (y + 1 < h ? current + w : -1);
                        if (next >= 0 && !visited[next] && expanded[next]) { visited[next] = true; queue[tail++] = next; }
                    }
                }
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (tail > bestCount && bw > w * .18 && bh > h * .14 && bw < bh * 3 && bh < bw * 3)
                {
                    bestCount = tail; best = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1); bestCorners = corners;
                }
            }
            quad = new BoardImageGeometry { Columns = 2, Rows = 2,
                TopLeft = bestCorners == null ? new PointF(best.Left, best.Top) : bestCorners[0],
                TopRight = bestCorners == null ? new PointF(best.Right - 1, best.Top) : bestCorners[1],
                BottomRight = bestCorners == null ? new PointF(best.Right - 1, best.Bottom - 1) : bestCorners[2],
                BottomLeft = bestCorners == null ? new PointF(best.Left, best.Bottom - 1) : bestCorners[3] };
            try { quad.Validate(w, h); }
            catch (ArgumentException)
            {
                quad.TopLeft = new PointF(best.Left, best.Top); quad.TopRight = new PointF(best.Right - 1, best.Top);
                quad.BottomRight = new PointF(best.Right - 1, best.Bottom - 1); quad.BottomLeft = new PointF(best.Left, best.Bottom - 1);
            }
            return best;
        }

        private sealed class Comb
        {
            internal double First, Last, Spacing, Score;
            internal int Count;
        }
        private static Comb FitComb(double[] projection, double minimum, double maximum)
        {
            int length = projection.Length, radius = Math.Max(3, length / 70);
            double[] score = new double[length], cumulative = new double[length + 1];
            for (int i = 0; i < length; i++) cumulative[i + 1] = cumulative[i] + projection[i];
            for (int i = 0; i < length; i++)
            {
                int l = Math.Max(0, i - radius), r = Math.Min(length, i + radius + 1);
                score[i] = Math.Max(0, projection[i] - (cumulative[r] - cumulative[l]) / (r - l));
            }
            Comb best = new Comb();
            for (double spacing = minimum; spacing <= maximum; spacing += .35)
                for (double phase = 0; phase < spacing; phase += 1)
                {
                    int slots = 0, hits = 0, first = -1, last = -1;
                    double total = 0;
                    for (double point = phase; point < length; point += spacing)
                    {
                        int center = (int)Math.Round(point), index = center; double peak = 0;
                        int tolerance = Math.Max(1, Math.Min(4, (int)Math.Round(spacing * .06)));
                        for (int k = Math.Max(0, center - tolerance); k <= Math.Min(length - 1, center + tolerance); k++)
                            if (score[k] > peak) { peak = score[k]; index = k; }
                        slots++;
                        if (index < length * .025 || index > length * .975) { slots--; continue; }
                        if (peak >= 2.1) { hits++; if (first < 0) first = index; last = index; }
                        total += Math.Min(40, peak);
                    }
                    if (hits < 5 || slots == 0) continue;
                    // All grid lines must count, including lighter/thinner lines.
                    // A square-root preference otherwise selects every second dark
                    // line on some rendered boards and physical photographs.
                    double value = total / slots * Math.Pow(hits / (double)slots, 1.5) * Math.Pow(hits, .9);
                    if (value > best.Score)
                    {
                        int count = (int)Math.Round((last - first) / spacing) + 1;
                        best = new Comb { First = first, Last = last, Spacing = spacing, Count = count, Score = value };
                    }
                }
            return best;
        }

        public static BoardImageResult Recognize(Bitmap image, BoardImageGeometry geometry)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (geometry == null) throw new ArgumentNullException("geometry");
            geometry = geometry.Clone(); geometry.Validate(image.Width, image.Height);
            Pixels pixels = new Pixels(image, 2400);
            BoardImageResult result = new BoardImageResult { Geometry = geometry,
                Stones = new Stone[geometry.Columns, geometry.Rows], Uncertain = new bool[geometry.Columns, geometry.Rows] };
            for (int y = 0; y < geometry.Rows; y++) for (int x = 0; x < geometry.Columns; x++)
            {
                List<double> backgroundL = new List<double>(), backgroundS = new List<double>(), backgroundMin = new List<double>();
                for (int sy = -1; sy <= 1; sy += 2) for (int sx = -1; sx <= 1; sx += 2)
                    for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++)
                    {
                        Color color;
                        if (!pixels.TryGet(geometry.Project(x + sx * (.40 + i * .04), y + sy * (.40 + j * .04)), out color)) continue;
                        backgroundL.Add(Luma(color)); backgroundS.Add(Saturation(color)); backgroundMin.Add(Math.Min(color.R, Math.Min(color.G, color.B)));
                    }
                if (backgroundL.Count < 4) { result.Uncertain[x, y] = true; continue; }
                double bg = Median(backgroundL), bgS = Median(backgroundS), bgMin = Median(backgroundMin);
                List<double> centerL = new List<double>(), centerS = new List<double>(), centerMin = new List<double>();
                int dark = 0, bright = 0, total = 0, missing = 0;
                for (int j = -7; j <= 7; j++) for (int i = -7; i <= 7; i++)
                {
                    double u = i * .05, v = j * .05;
                    if (u * u + v * v > .35 * .35) continue;
                    Color color;
                    if (!pixels.TryGet(geometry.Project(x + u, y + v), out color)) { missing++; continue; }
                    double l = Luma(color), s = Saturation(color), minimum = Math.Min(color.R, Math.Min(color.G, color.B));
                    centerL.Add(l); centerS.Add(s); centerMin.Add(minimum); total++;
                    if (l < bg - 28 && l < bg * .79) dark++;
                    if ((l > bg + 19) || (minimum > bgMin + 23 && s < bgS * .7 && l > bg - 16)) bright++;
                }
                if (total < 12) { result.Uncertain[x, y] = true; continue; }
                double median = Median(centerL), sat = Median(centerS), min = Median(centerMin);
                double black = dark / (double)total, white = bright / (double)total;
                bool isBlack = black >= .56 && median < bg - 25;
                bool isWhite = white >= .56 && ((median > bg + 17) || (min > bgMin + 21 && sat < bgS * .75));
                result.Stones[x, y] = isBlack ? Stone.Black : isWhite ? Stone.White : Stone.Empty;
                double contrast = median - bg;
                bool questionable = isBlack ? black < .74 || contrast > -45 : isWhite ? white < .74 :
                    black > .42 || (white > .42 && contrast > 8) || contrast < -24 || contrast > 21;
                // Directional occlusion / tilted stones are visible as mixed-color
                // patches; mark them instead of presenting low-angle guesses as certain.
                double low = Quantile(centerL, .15), high = Quantile(centerL, .85);
                if ((isBlack || isWhite) && high - low > 105 && Math.Max(black, white) < .8) questionable = true;
                result.Uncertain[x, y] = questionable || missing > 0;
            }
            return result;
        }

        private static double Luma(Color c) { return .299 * c.R + .587 * c.G + .114 * c.B; }
        private static double Saturation(Color c)
        {
            int high = Math.Max(c.R, Math.Max(c.G, c.B)), low = Math.Min(c.R, Math.Min(c.G, c.B));
            return high == 0 ? 0 : (high - low) / (double)high;
        }
        private static double Median(List<double> values) { return Quantile(values, .5); }
        private static double Quantile(List<double> values, double q)
        {
            values.Sort(); return values[Math.Max(0, Math.Min(values.Count - 1, (int)Math.Round((values.Count - 1) * q)))];
        }
    }
}
