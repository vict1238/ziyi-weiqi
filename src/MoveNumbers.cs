using System;

namespace GoDesk
{
    /// <summary>Move numbers belonging to the stones alive at the selected history position.</summary>
    public static class MoveNumbers
    {
        public static int[,] Build(GoGame game)
        {
            if (game == null) throw new ArgumentNullException("game");
            int size = game.Size;
            var numbers = new int[size, size];
            if (game.Position == 0) return numbers;

            // Replay a separate model. Looking only at the final color on a point
            // cannot distinguish an original stone from a later ko recapture.
            GoGame replay = GoGame.FromSgf(game.ToSgf());
            for (int position = 1; position <= game.Position; position++)
            {
                replay.Goto(position);
                for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
                    if (replay.Board[x, y] == Stone.Empty) numbers[x, y] = 0;
                Move move = replay.Moves[position - 1];
                if (!move.IsPass) numbers[move.X, move.Y] = position;
            }
            return numbers;
        }
    }

    // Repainting progress, hints, hover or a dragged region should not replay an
    // entire record. Compare history contents as well as the board snapshot so a
    // same-length branch never reuses the previous line's labels.
    internal sealed class MoveNumbersCache
    {
        private GoGame source;
        private Stone[,] board;
        private int position = -1, size, retainedMoves;
        private Move[] prefix;
        private int[,] numbers;

        public int[,] Get(GoGame game)
        {
            if (game == null) throw new ArgumentNullException("game");
            if (Object.ReferenceEquals(source, game) && Object.ReferenceEquals(board, game.Board) &&
                position == game.Position && size == game.Size && retainedMoves == game.Moves.Count && MatchesPrefix(game))
                return numbers;
            int[,] replacement = MoveNumbers.Build(game);
            source = game; board = game.Board; position = game.Position; size = game.Size; retainedMoves = game.Moves.Count;
            prefix = new Move[position];
            for (int i = 0; i < position; i++)
            {
                Move move = game.Moves[i];
                prefix[i] = new Move(move.X, move.Y, move.Color);
            }
            numbers = replacement;
            return numbers;
        }

        private bool MatchesPrefix(GoGame game)
        {
            if (prefix == null || prefix.Length != game.Position) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                Move expected = prefix[i], actual = game.Moves[i];
                if (actual == null || expected.X != actual.X || expected.Y != actual.Y || expected.Color != actual.Color) return false;
            }
            return true;
        }

        public void Clear()
        {
            source = null; board = null; prefix = null; numbers = null; position = -1;
        }
    }
}
