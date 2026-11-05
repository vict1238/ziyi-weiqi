using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GoDesk
{
    public static class BoardImageRecognizerTests
    {
        private static int assertions;
        public static string Run()
        {
            assertions = 0;
            CheckGeometry();
            CheckBoards();
            CheckAmbiguity();
            return "图像识别检查通过：" + assertions + " 项断言（四角透视、退化校验、黑白/空、星位、标记、局部网格与不同明暗）。";
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("图像识别测试失败：" + message);
        }

        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, message);
        }

        private static void Close(PointF actual, PointF expected, string message)
        {
            Check(Math.Abs(actual.X - expected.X) < .001 && Math.Abs(actual.Y - expected.Y) < .001, message);
        }

        private static void CheckGeometry()
        {
            BoardImageGeometry quad = new BoardImageGeometry { Columns = 13, Rows = 13,
                TopLeft = new PointF(120, 170), TopRight = new PointF(610, 65),
                BottomRight = new PointF(750, 530), BottomLeft = new PointF(150, 690) };
            quad.Validate(850, 760);
            Close(quad.Project(0, 0), quad.TopLeft, "透视左上角");
            Close(quad.Project(12, 0), quad.TopRight, "透视右上角");
            Close(quad.Project(12, 12), quad.BottomRight, "透视右下角");
            Close(quad.Project(0, 12), quad.BottomLeft, "透视左下角");
            BoardImageGeometry copy = quad.Clone(); copy.TopLeft = new PointF(0, 0); copy.Columns = 9;
            Check(quad.TopLeft.X == 120 && quad.Columns == 13, "几何副本不改原图定位");
            copy = quad.Clone(); copy.Columns = 1; Reject(() => copy.Validate(850, 760), "拒绝一列");
            copy = quad.Clone(); copy.Rows = 20; Reject(() => copy.Validate(850, 760), "拒绝超十九路");
            copy = quad.Clone(); copy.TopLeft = new PointF(-1, 0); Reject(() => copy.Validate(850, 760), "拒绝图片外定位");
            copy = quad.Clone(); copy.TopLeft = new PointF(Single.NaN, 0); Reject(() => copy.Validate(850, 760), "拒绝非数字");
            copy = quad.Clone(); copy.TopRight = quad.BottomLeft; copy.BottomLeft = quad.TopRight;
            Reject(() => copy.Validate(850, 760), "拒绝反向/交叉四角");
            copy = quad.Clone(); copy.BottomRight = quad.TopRight; Reject(() => copy.Validate(850, 760), "拒绝重合四角");
            copy = quad.Clone(); copy.TopRight = new PointF(121, 170); Reject(() => copy.Validate(850, 760), "拒绝低分辨率网格");
            Reject(() => BoardImageRecognizer.Recognize(null, quad), "拒绝空图片");
            using (Bitmap tiny = new Bitmap(10, 10)) Reject(() => BoardImageRecognizer.Detect(tiny, 19), "拒绝极小图片");
        }

        private static void CheckBoards()
        {
            foreach (bool perspective in new bool[] { false, true })
                foreach (Color wood in new Color[] { Color.FromArgb(219, 174, 94), Color.FromArgb(113, 85, 49), Color.FromArgb(194, 193, 186) })
                {
                    BoardImageGeometry geometry = new BoardImageGeometry { Columns = 9, Rows = 9,
                        TopLeft = perspective ? new PointF(120, 170) : new PointF(75, 75),
                        TopRight = perspective ? new PointF(610, 65) : new PointF(675, 75),
                        BottomRight = perspective ? new PointF(750, 530) : new PointF(675, 675),
                        BottomLeft = perspective ? new PointF(150, 690) : new PointF(75, 675) };
                    Stone[,] expected = Pattern(9, 9);
                    using (Bitmap image = DrawBoard(850, 760, geometry, expected, wood, true))
                    {
                        BoardImageResult actual = BoardImageRecognizer.Recognize(image, geometry);
                        Compare(actual, expected, "9路" + (perspective ? "斜拍" : "正视") + wood);
                        Check(!Object.ReferenceEquals(actual.Geometry, geometry), "识别保存独立定位快照");
                        Check(image.GetPixel(0, 0).A == 255, "不释放或修改调用方图片");
                    }
                }

            BoardImageGeometry partial = new BoardImageGeometry { Columns = 7, Rows = 11,
                TopLeft = new PointF(70, 55), TopRight = new PointF(430, 55),
                BottomRight = new PointF(430, 655), BottomLeft = new PointF(70, 655) };
            Stone[,] local = Pattern(7, 11);
            using (Bitmap image = DrawBoard(510, 730, partial, local, Color.FromArgb(224, 180, 99), true))
                Compare(BoardImageRecognizer.Recognize(image, partial), local, "非正方局部7列11行");

            foreach (int size in new int[] { 9, 13, 19 })
            {
                BoardImageGeometry geometry = new BoardImageGeometry { Columns = size, Rows = size,
                    TopLeft = new PointF(65, 65), TopRight = new PointF(785, 65),
                    BottomRight = new PointF(785, 785), BottomLeft = new PointF(65, 785) };
                Stone[,] expected = Pattern(size, size);
                using (Bitmap image = DrawBoard(850, 850, geometry, expected, Color.FromArgb(222, 177, 94), false))
                {
                    BoardImageGeometry auto = BoardImageRecognizer.Detect(image, size);
                    Check(auto.Columns == size && auto.Rows == size, "自动识别" + size + "路行列");
                    Compare(BoardImageRecognizer.Recognize(image, auto), expected, "自动定位" + size + "路");
                }
            }
        }

        private static void Compare(BoardImageResult actual, Stone[,] expected, string message)
        {
            Check(actual.Stones.GetLength(0) == expected.GetLength(0) && actual.Stones.GetLength(1) == expected.GetLength(1), message + "矩阵大小");
            for (int y = 0; y < expected.GetLength(1); y++) for (int x = 0; x < expected.GetLength(0); x++)
                Check(actual.Stones[x, y] == expected[x, y], message + "点" + x + "," + y + "应为" + expected[x, y] + "实际" + actual.Stones[x, y]);
        }

        private static void CheckAmbiguity()
        {
            BoardImageGeometry geometry = new BoardImageGeometry { Columns = 9, Rows = 9,
                TopLeft = new PointF(75, 75), TopRight = new PointF(675, 75),
                BottomRight = new PointF(675, 675), BottomLeft = new PointF(75, 675) };
            Stone[,] stones = Pattern(9, 9);
            using (Bitmap image = DrawBoard(750, 750, geometry, stones, Color.FromArgb(219, 174, 94), false))
            {
                // A reflection/obstruction divides one stone into incompatible black
                // and white regions. It must be offered for correction, not hidden.
                using (Graphics graphics = Graphics.FromImage(image))
                using (Brush white = new SolidBrush(Color.FromArgb(240, 241, 238)))
                    graphics.FillRectangle(white, 225, 275, 35, 50);
                BoardImageResult result = BoardImageRecognizer.Recognize(image, geometry);
                Check(result.Uncertain[2, 3], "混合遮挡标记待检查");
                Check(result.Stones[0, 0] == Stone.Black && result.Stones[8, 8] == Stone.Black, "疑点不污染其他交叉点");
            }
        }

        private static Stone[,] Pattern(int columns, int rows)
        {
            Stone[,] board = new Stone[columns, rows];
            board[0, 0] = Stone.Black; board[columns - 1, 0] = Stone.White;
            board[0, rows - 1] = Stone.White; board[columns - 1, rows - 1] = Stone.Black;
            board[2, 3] = Stone.Black; board[3, 3] = Stone.White; board[4, 3] = Stone.Black;
            board[2, 4] = Stone.White; board[3, 4] = Stone.Black; board[4, 4] = Stone.White;
            return board;
        }

        internal static Bitmap DrawBoard(int width, int height, BoardImageGeometry geometry, Stone[,] stones, Color wood, bool markers)
        {
            Bitmap image = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(232, 236, 240));
                PointF[] edge = { geometry.Project(-.7, -.7), geometry.Project(geometry.Columns - .3, -.7),
                    geometry.Project(geometry.Columns - .3, geometry.Rows - .3), geometry.Project(-.7, geometry.Rows - .3) };
                using (Brush fill = new SolidBrush(wood)) g.FillPolygon(fill, edge);
                using (Pen line = new Pen(Color.FromArgb(50, 43, 31), 1))
                {
                    for (int x = 0; x < geometry.Columns; x++) g.DrawLine(line, geometry.Project(x, 0), geometry.Project(x, geometry.Rows - 1));
                    for (int y = 0; y < geometry.Rows; y++) g.DrawLine(line, geometry.Project(0, y), geometry.Project(geometry.Columns - 1, y));
                }
                using (Brush black = new SolidBrush(Color.FromArgb(22, 23, 24)))
                    foreach (Point p in new Point[] { new Point(2, 2), new Point(geometry.Columns / 2, geometry.Rows / 2) })
                        g.FillPolygon(black, Circle(geometry, p.X, p.Y, .075));
                for (int y = 0; y < geometry.Rows; y++) for (int x = 0; x < geometry.Columns; x++)
                {
                    Stone stone = stones[x, y]; if (stone == Stone.Empty) continue;
                    using (Brush fill = new SolidBrush(stone == Stone.Black ? Color.FromArgb(22, 23, 24) : Color.FromArgb(240, 241, 238)))
                        g.FillPolygon(fill, Circle(geometry, x, y, .46));
                    if (markers && x == 2 && y == 3)
                    {
                        PointF center = geometry.Project(x, y);
                        using (Font font = new Font("Segoe UI", 11, FontStyle.Bold))
                        using (Brush text = new SolidBrush(Color.White))
                        using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("123", font, text, center, format);
                    }
                    if (markers && x == 3 && y == 3)
                        using (Pen ring = new Pen(Color.FromArgb(40, 40, 40), 2)) g.DrawPolygon(ring, Circle(geometry, x, y, .15));
                }
            }
            return image;
        }

        private static PointF[] Circle(BoardImageGeometry geometry, int x, int y, double radius)
        {
            PointF[] circle = new PointF[48];
            for (int i = 0; i < circle.Length; i++)
                circle[i] = geometry.Project(x + radius * Math.Cos(i * 2 * Math.PI / circle.Length), y + radius * Math.Sin(i * 2 * Math.PI / circle.Length));
            return circle;
        }
    }
}
