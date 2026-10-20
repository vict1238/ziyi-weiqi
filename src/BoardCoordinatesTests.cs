using System;
using System.Globalization;
using System.Threading;

namespace GoDesk
{
    public static class BoardCoordinatesTests
    {
        private static int checks;

        public static string Run()
        {
            checks = 0;
            const string columns = "ABCDEFGHIJKLMNOPQRS";
            foreach (int size in new int[] { 9, 13, 19 })
            {
                for (int index = 0; index < size; index++)
                {
                    Check(BoardCoordinates.ColumnLabel(index, size) == columns.Substring(index, 1), "列标签连续包含 I");
                    Check(BoardCoordinates.RowLabel(index, size) == (index + 1).ToString(CultureInfo.InvariantCulture), "行标签从顶部开始递增");
                }
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    var move = new Move(x, y, Stone.Black);
                    string expected = columns.Substring(x, 1) + (y + 1).ToString(CultureInfo.InvariantCulture);
                    Check(move.ToDisplay(size) == expected, "实际落子显示与棋盘标签一致");
                    Check(BoardCoordinates.FromGtp(move.ToGtp(size), size) == expected, "引擎着点在每个交叉点均转换为显示坐标");
                }
                Check(new Move(-1, -1, Stone.White).ToDisplay(size) == "停一手", "历史停着显示");
                Check(BoardCoordinates.FromGtp(" pass ", size) == "停一手", "引擎停着显示");
            }

            Check(BoardCoordinates.FromGtp("A19", 19) == "A1", "左上角 A1");
            Check(BoardCoordinates.FromGtp("J19", 19) == "I1", "第九列显示 I");
            Check(BoardCoordinates.FromGtp("T1", 19) == "S19", "右下角 S19");
            Check(BoardCoordinates.FromGtp("D4", 19) == "D16", "引擎底部计数转换为顶部计数");
            Check(BoardCoordinates.FromGtp("N1", 13) == "M13", "十三路右下角");
            Check(BoardCoordinates.FromGtp("J1", 9) == "I9", "九路右下角");

            var sgf = GoGame.FromSgf("(;SZ[19];B[ia];W[ss])");
            Check(sgf.Moves[0].ToDisplay(19) == "I1" && sgf.Moves[1].ToDisplay(19) == "S19", "SGF 历史使用同一显示坐标");
            Check(sgf.Moves[0].ToGtp(19) == "J19" && sgf.Moves[1].ToGtp(19) == "T1", "显示转换不改变引擎协议");
            Check(sgf.Board[8, 0] == Stone.Black && sgf.Board[18, 18] == Stone.White, "显示转换不改变实际棋子位置");
            Check(sgf.ToSgf().Contains(";B[ia];W[ss]"), "SGF 导出保持原始棋盘坐标");
            bool invalidGtp = false;
            try { BoardCoordinates.FromGtp("I1", 19); } catch (FormatException) { invalidGtp = true; }
            Check(invalidGtp, "屏幕 I 列不混入标准 GTP 协议");

            CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
                Check(BoardCoordinates.RowLabel(18, 19) == "19" && new Move(8, 18, Stone.White).ToDisplay(19) == "I19", "坐标数字不随系统区域变化");
            }
            finally { Thread.CurrentThread.CurrentCulture = originalCulture; }

            bool invalidPoint = false;
            try { new Move(19, 0, Stone.Black).ToDisplay(19); } catch (ArgumentOutOfRangeException) { invalidPoint = true; }
            Check(invalidPoint, "越界位置不产生可见坐标");
            return "棋盘显示坐标测试通过：" + checks + " 项断言。";
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception("棋盘显示坐标测试失败：" + message);
        }
    }
}
