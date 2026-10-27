using System;
using System.Collections.Generic;

namespace GoDesk
{
    /// <summary>An inclusive rectangle in board coordinates, independent of UI pixels.</summary>
    public sealed class StudyRegion
    {
        public int Left { get; private set; }
        public int Top { get; private set; }
        public int Right { get; private set; }
        public int Bottom { get; private set; }

        public StudyRegion(int x1, int y1, int x2, int y2)
        {
            Left = Math.Min(x1, x2); Top = Math.Min(y1, y2);
            Right = Math.Max(x1, x2); Bottom = Math.Max(y1, y2);
        }

        public bool Contains(int x, int y)
        {
            return x >= Left && x <= Right && y >= Top && y <= Bottom;
        }

        public void Validate(int size)
        {
            if (size < 2 || size > 25) throw new ArgumentOutOfRangeException("size", "棋盘边长必须在 2 到 25 之间。");
            if (Left < 0 || Top < 0 || Right >= size || Bottom >= size)
                throw new ArgumentOutOfRangeException("region", "研究范围超出当前棋盘，请重新框选。");
        }

        public string Describe(int size)
        {
            Validate(size);
            return new Move(Left, Top, Stone.Black).ToDisplay(size) + "–" +
                new Move(Right, Bottom, Stone.Black).ToDisplay(size);
        }
    }

    public static class LocalStudy
    {
        internal const int RestrictionDepth = 1000000000;

        public static bool HasLegalMove(GoGame game, StudyRegion region)
        {
            if (game == null) throw new ArgumentNullException("game");
            if (region == null) throw new ArgumentNullException("region");
            region.Validate(game.Size);
            if (game.IsOver) return false;
            int position = game.Position;
            // SGF preserves setup, rules, real move history and ko/superko. Return
            // the clone to the active prefix before checking any point, since a
            // reviewed game can retain future stones or two ending passes.
            GoGame copy = GoGame.FromSgf(game.ToSgf());
            copy.Goto(position);
            for (int y = region.Top; y <= region.Bottom; y++)
                for (int x = region.Left; x <= region.Right; x++)
                {
                    if (copy.Board[x, y] != Stone.Empty) continue;
                    string error;
                    if (!copy.TryPlay(x, y, out error)) continue;
                    copy.Undo();
                    return true;
                }
            return false;
        }

        internal static Dictionary<string, object>[] CreateAvoidMoves(int size, StudyRegion region)
        {
            if (region == null) throw new ArgumentNullException("region");
            region.Validate(size);
            var locations = new List<string>();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (!region.Contains(x, y)) locations.Add(new Move(x, y, Stone.Black).ToGtp(size));
            locations.Add("pass");
            // Both bundled KataGo versions accept multiple avoidMoves entries;
            // CPU 1.14.1 only accepts one allowMoves entry. Banning all outside
            // points and pass is the same bilateral region constraint without
            // weakening either player's restriction. Occupancy is deliberately
            // irrelevant: captured points can become legal later in the tree.
            string[] moves = locations.ToArray();
            return new Dictionary<string, object>[] {
                new Dictionary<string, object> { { "player", "B" }, { "moves", moves }, { "untilDepth", RestrictionDepth } },
                new Dictionary<string, object> { { "player", "W" }, { "moves", (string[])moves.Clone() }, { "untilDepth", RestrictionDepth } }
            };
        }
    }
}
