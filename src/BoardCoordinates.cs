using System;
using System.Globalization;

namespace GoDesk
{
    /// <summary>Screen coordinates use consecutive columns and rows counted from the top.</summary>
    public static class BoardCoordinates
    {
        public static string ColumnLabel(int x, int size)
        {
            ValidateIndex(x, size);
            return ((char)('A' + x)).ToString();
        }

        public static string RowLabel(int y, int size)
        {
            ValidateIndex(y, size);
            return (y + 1).ToString(CultureInfo.InvariantCulture);
        }

        public static string ToDisplay(this Move move, int size)
        {
            if (move == null) throw new ArgumentNullException("move");
            ValidateSize(size);
            if (move.IsPass) return "停一手";
            return ColumnLabel(move.X, size) + RowLabel(move.Y, size);
        }

        /// <summary>Decode standard engine GTP before formatting a screen coordinate.</summary>
        public static string FromGtp(string coordinate, int size)
        {
            return GoGame.ParseGtp(coordinate, Stone.Black, size).ToDisplay(size);
        }

        private static void ValidateIndex(int index, int size)
        {
            ValidateSize(size);
            if (index < 0 || index >= size) throw new ArgumentOutOfRangeException("index", "坐标超出棋盘。");
        }

        private static void ValidateSize(int size)
        {
            if (size < 2 || size > 25) throw new ArgumentOutOfRangeException("size", "棋盘边长必须在 2 到 25 之间。");
        }
    }
}
