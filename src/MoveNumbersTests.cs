using System;

namespace GoDesk
{
    public static class MoveNumbersTests
    {
        private static int checks;

        public static string Run()
        {
            checks = 0;
            TestPassesAndCaptures();
            TestHandicapAndSetup();
            TestKoAndNavigation();
            TestCacheAndBranches();
            TestFourDigitHistory();
            bool rejected = false;
            try { MoveNumbers.Build(null); } catch (ArgumentNullException) { rejected = true; }
            Check(rejected, "null game is rejected");
            return "棋子落子手数测试通过：" + checks + " 项断言。";
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception("棋子落子手数测试失败：" + message);
        }

        private static void Play(GoGame game, int x, int y)
        {
            string error;
            if (!game.TryPlay(x, y, out error)) throw new Exception("Move-number test move failed: " + error);
        }

        private static void TestPassesAndCaptures()
        {
            GoGame passed = GoGame.FromSgf("(;SZ[9];B[aa];W[];B[cc];W[dd];B[];W[ee])");
            int[,] numbers = MoveNumbers.Build(passed);
            Check(numbers[0, 0] == 1 && numbers[2, 2] == 3 && numbers[3, 3] == 4 && numbers[4, 4] == 6,
                "passes consume move numbers without labelling an intersection");
            Check(numbers[1, 1] == 0, "empty intersections are unnumbered");
            GoGame captured = GoGame.FromSgf("(;SZ[9];B[aa];W[ba];B[cc];W[ab])");
            numbers = MoveNumbers.Build(captured);
            Check(captured.WhiteCaptures == 1 && numbers[0, 0] == 0, "captured move label disappears");
            Check(numbers[1, 0] == 2 && numbers[2, 2] == 3 && numbers[0, 1] == 4, "surviving stones keep their original numbers");
            captured.Goto(1);
            numbers = MoveNumbers.Build(captured);
            Check(numbers[0, 0] == 1 && numbers[1, 0] == 0, "review restores a captured stone and excludes later moves");
        }

        private static void TestHandicapAndSetup()
        {
            foreach (int size in new int[] { 9, 13, 19 })
                foreach (Stone recipient in new Stone[] { Stone.Black, Stone.White })
                    foreach (int handicap in new int[] { 0, 1, 2, 9 })
                    {
                        var game = new GoGame(size);
                        game.NewGame(size, 0.5, handicap, recipient);
                        int[,] numbers = MoveNumbers.Build(game);
                        Check(numbers.GetLength(0) == size && numbers.GetLength(1) == size, "number matrix matches the board size");
                        foreach (Move setup in game.InitialStones)
                            Check(numbers[setup.X, setup.Y] == 0, "handicap setup is not a numbered move");
                        Play(game, 0, 0);
                        numbers = MoveNumbers.Build(game);
                        Check(numbers[0, 0] == 1 && game.Moves[0].Color == game.InitialPlayer, "first actual move starts at one for either recipient");
                        foreach (Move setup in game.InitialStones)
                            Check(numbers[setup.X, setup.Y] == 0, "setup remains unnumbered after actual play");
                    }
            GoGame mixed = GoGame.FromSgf("(;SZ[9]AB[aa]AW[ii]PL[W];W[dd])");
            int[,] mixedNumbers = MoveNumbers.Build(mixed);
            Check(mixedNumbers[0, 0] == 0 && mixedNumbers[8, 8] == 0 && mixedNumbers[3, 3] == 1,
                "mixed SGF setup and explicit White first player are preserved");
        }

        private static void TestKoAndNavigation()
        {
            GoGame game = GoGame.FromSgf("(;SZ[9]AB[ba][ab][bc]AW[bb][ca][db][cc]PL[B])");
            Play(game, 2, 1); Play(game, 8, 8); Play(game, 7, 8); Play(game, 1, 1);
            Play(game, 8, 7); Play(game, 7, 7); Play(game, 2, 1);
            string before = game.ToSgf();
            Stone[,] originalBoard = game.Board;
            Move originalFirstMove = game.Moves[0];
            int[,] numbers = MoveNumbers.Build(game);
            Check(numbers[2, 1] == 7 && numbers[1, 1] == 0, "same-color ko recapture receives the new move number");
            Check(game.Board[8, 8] == Stone.Empty && numbers[8, 8] == 0 && game.BlackCaptures == 3,
                "the move-2 corner threat captured by move 5 loses its number alongside the two ko captures");
            Check(numbers[7, 8] == 3 && numbers[8, 7] == 5 && numbers[7, 7] == 6,
                "surviving ko threats retain their individual move numbers");
            Check(game.Position == 7 && game.ToSgf() == before && Object.ReferenceEquals(originalBoard, game.Board) &&
                Object.ReferenceEquals(originalFirstMove, game.Moves[0]), "building numbers never changes game state or history objects");
            game.Goto(4);
            numbers = MoveNumbers.Build(game);
            Check(numbers[1, 1] == 4 && numbers[2, 1] == 0 && numbers[8, 7] == 0, "earlier ko position excludes future recaptures and threats");
            Check(game.Position == 4 && game.Moves.Count == 7 && game.ToSgf() == before, "history selection and retained future survive numbering");
            game.Goto(0);
            numbers = MoveNumbers.Build(game);
            Check(numbers[1, 1] == 0 && numbers[2, 1] == 0, "initial ko setup carries no move numbers");
        }

        private static void TestCacheAndBranches()
        {
            var cache = new MoveNumbersCache();
            GoGame game = GoGame.FromSgf("(;SZ[9];B[aa];W[bb])");
            int[,] first = cache.Get(game);
            Check(Object.ReferenceEquals(first, cache.Get(game)), "unchanged paints reuse the calculated numbers");
            game.Goto(1); Play(game, 4, 4);
            int[,] branch = cache.Get(game);
            Check(game.Position == 2 && game.Moves.Count == 2 && branch[4, 4] == 2 && branch[1, 1] == 0,
                "same-length branch invalidates prior labels");
            Check(!Object.ReferenceEquals(first, branch) && first[1, 1] == 2, "cache replacement does not modify an earlier number matrix");
            game.Goto(1);
            Check(cache.Get(game)[4, 4] == 0, "navigation invalidates labels beyond the selected prefix");
            game.Goto(2);
            Check(cache.Get(game)[4, 4] == 2, "forward navigation restores the selected branch labels");
            game.NewGame(13, 7.5, 0);
            Check(cache.Get(game).GetLength(0) == 13 && cache.Get(game)[0, 0] == 0, "same model instance can reset to a different board");
            GoGame replacement = GoGame.FromSgf("(;SZ[13];B[cc])");
            Check(cache.Get(replacement)[2, 2] == 1, "replacing the model invalidates the cache");
            cache.Clear();
            Check(cache.Get(replacement)[2, 2] == 1, "explicit cache reset rebuilds valid numbers");
        }

        private static void TestFourDigitHistory()
        {
            string setup = ";SZ[19]RU[Japanese]AB[ba][ab][bc][hb][ia][jb][ic][bg][ah][bi]" +
                "AW[bb][ca][db][cc][ha][gb][hc][bh][cg][dh][ci]PL[B]";
            GoGame game = GoGame.FromSgf("(" + setup + ")");
            int[,] cycle = new int[,] { { 2, 1 }, { 8, 1 }, { 2, 7 }, { 1, 1 }, { 7, 1 }, { 1, 7 } };
            for (int i = 0; i < 1002; i++) Play(game, cycle[i % 6, 0], cycle[i % 6, 1]);
            int[,] numbers = MoveNumbers.Build(game);
            Check(numbers[1, 1] == 1000 && numbers[7, 1] == 1001 && numbers[1, 7] == 1002,
                "long legal ko histories keep exact four-digit recapture numbers");
            Check(numbers[2, 1] == 0 && numbers[8, 1] == 0 && numbers[2, 7] == 0,
                "stones captured late in a long history have no stale labels");
        }
    }
}
