using System;

namespace GoDesk
{
    /// <summary>Deterministic model checks; runnable from a small console entry point.</summary>
    public static class GoGameTests
    {
        private static int checks;

        public static string Run()
        {
            checks = 0;
            TestBoardsAndCoordinates();
            TestCaptureAndSuicide();
            TestKo();
            TestRulesAndKomi();
            TestHistoryAndPasses();
            TestHandicap();
            TestSgf();
            return "围棋规则与 SGF 测试通过：" + checks + " 项断言。";
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception("围棋模型测试失败：" + message);
        }

        private static void Play(GoGame game, int x, int y)
        {
            string error;
            if (!game.TryPlay(x, y, out error)) throw new Exception("测试落子失败：" + x + "," + y + "；" + error);
        }

        private static void RejectSgf(string sgf, string message)
        {
            bool rejected = false;
            try { GoGame.FromSgf(sgf); }
            catch (FormatException) { rejected = true; }
            Check(rejected, message);
        }

        private static void TestBoardsAndCoordinates()
        {
            foreach (int size in new int[] { 9, 13, 19 })
            {
                GoGame game = new GoGame(size);
                Check(game.Size == size && game.ToPlay == Stone.Black && game.Position == 0, "棋盘初始化");
                Check(game.Board.GetLength(0) == size && game.Board.GetLength(1) == size, "棋盘尺寸");
                Play(game, 0, 0);
                Check(game.Board[0, 0] == Stone.Black && game.ToPlay == Stone.White, "坐标与轮次");
                string error;
                Check(!game.TryPlay(0, 0, out error) && game.Position == 1, "禁止覆盖已有棋子");
                Check(!game.TryPlay(-1, 0, out error) && !game.TryPlay(size, size, out error), "拒绝越界坐标");
                Move corner = GoGame.ParseGtp(new Move(size - 1, size - 1, Stone.Black).ToGtp(size), Stone.White, size);
                Check(corner.X == size - 1 && corner.Y == size - 1 && corner.Color == Stone.White, "GTP 往返");
            }
            Check(new Move(8, 0, Stone.Black).ToGtp(19) == "J19", "GTP 跳过 I 列");
            Check(GoGame.ParseGtp(" pass ", Stone.Black, 19).IsPass, "GTP 停一手");
            bool invalid = false;
            try { GoGame.ParseGtp("I3", Stone.Black, 19); } catch (FormatException) { invalid = true; }
            Check(invalid, "拒绝无效 GTP I 列");
        }

        private static void TestCaptureAndSuicide()
        {
            GoGame game = GoGame.FromSgf("(;SZ[9]AB[ba][ab][cb]AW[bb]PL[B])");
            Play(game, 1, 2);
            Check(game.Board[1, 1] == Stone.Empty && game.BlackCaptures == 1, "单子提取");
            game.Undo();
            Check(game.Board[1, 1] == Stone.White && game.BlackCaptures == 0, "悔棋恢复提子与棋盘");
            GoGame multi = GoGame.FromSgf("(;SZ[9]AB[ba][ca][ab][db][bc]AW[bb][cb]PL[B])");
            Play(multi, 2, 2);
            Check(multi.BlackCaptures == 2 && multi.Board[1, 1] == Stone.Empty && multi.Board[2, 1] == Stone.Empty, "整块提取");
            GoGame suicide = GoGame.FromSgf("(;SZ[9]AW[ba][ab][cb][bc]PL[B])");
            string error;
            Check(!suicide.TryPlay(1, 1, out error) && suicide.Position == 0 && suicide.Board[1, 1] == Stone.Empty, "禁入点无副作用");
            GoGame take = GoGame.FromSgf("(;SZ[9]AB[ca][bb]AW[ba]PL[B])");
            Play(take, 0, 0);
            Check(take.BlackCaptures == 1 && take.Board[0, 0] == Stone.Black, "无气点提子后合法");
        }

        private static void TestKo()
        {
            // Black at cb captures White bb; immediate White bb would restore setup.
            GoGame ko = GoGame.FromSgf("(;SZ[9]AB[ba][ab][bc]AW[bb][ca][db][cc]PL[B])");
            Play(ko, 2, 1);
            string error;
            Check(!ko.TryPlay(1, 1, out error) && error.Contains("重复"), "禁止立即回提");
            Check(ko.Board[2, 1] == Stone.Black && ko.BlackCaptures == 1 && ko.Position == 1, "非法劫争无副作用");
            Play(ko, 8, 8);
            Play(ko, 7, 8);
            Play(ko, 1, 1);
            Check(ko.WhiteCaptures == 1 && ko.Board[2, 1] == Stone.Empty, "劫材交换后可回提");
            GoGame passKo = GoGame.FromSgf("(;SZ[9]AB[ba][ab][bc]AW[bb][ca][db][cc]PL[B])");
            Play(passKo, 2, 1);
            passKo.Pass();
            passKo.Pass();
            Check(passKo.IsOver && passKo.Position == 3, "重复局面的停一手仍合法");
        }

        private static void TestHistoryAndPasses()
        {
            GoGame game = new GoGame(9);
            Play(game, 0, 0);
            Play(game, 1, 0);
            Play(game, 2, 0);
            game.Goto(1);
            Check(game.Position == 1 && game.Moves.Count == 3 && game.ToPlay == Stone.White, "导航保留完整历史");
            Check(game.Board[1, 0] == Stone.Empty && game.Board[2, 0] == Stone.Empty, "导航恢复棋盘");
            GoGame retained = GoGame.FromSgf(game.ToSgf());
            Check(retained.Moves.Count == 3, "审阅历史时导出保留完整棋谱");
            Play(game, 4, 4);
            Check(game.Moves.Count == 2 && game.Position == 2 && game.Board[2, 0] == Stone.Empty, "新分支截断旧后续");
            game.Pass();
            game.Pass();
            Check(game.IsOver && game.ToPlay == Stone.Black, "双方停一手结束");
            string error;
            Check(!game.TryPlay(5, 5, out error), "结束后拒绝继续落子");
            game.Undo();
            Check(!game.IsOver && game.ToPlay == Stone.White, "悔棋恢复结束前状态");
            Play(game, 5, 5);
            Check(!game.IsOver && game.Moves.Count == 4, "停一手后正常落子重置结束计数");
        }

        private static void TestRulesAndKomi()
        {
            GoGame game = new GoGame(19);
            Check(game.Rules == "chinese-ogs", "新对局默认使用无自杀的中国全局同形规则");
            foreach (string alias in new string[] { "Chinese", "Chinese-ogs", "Chinese KGS" })
            {
                GoGame chinese = GoGame.FromSgf("(;SZ[9]RU[" + alias + "]KM[7.5];B[dd])");
                Check(chinese.Rules == "chinese-ogs" && chinese.ToSgf().Contains("RU[Chinese]"), "中国规则别名归一化：" + alias);
                Check(GoGame.FromSgf(chinese.ToSgf()).Rules == "chinese-ogs", "中国规则导出往返");
            }
            GoGame japanese = GoGame.FromSgf("(;SZ[9]RU[Japanese]KM[6.5];B[dd];W[ff])");
            Check(japanese.Rules == "japanese" && japanese.ToSgf().Contains("RU[Japanese]"), "日本规则保留导出");
            Check(GoGame.FromSgf(japanese.ToSgf()).Rules == "japanese", "日本规则导出往返");
            japanese.NewGame(9, 7.5, 0);
            Check(japanese.Rules == "chinese-ogs", "重开对局恢复默认规则");
            RejectSgf("(;SZ[9]RU[AGA])", "拒绝不支持的明确规则");
            RejectSgf("(;SZ[9]RU[house rules])", "拒绝未知的非正式规则");
            RejectSgf("(;SZ[9]RU[])", "拒绝空规则名称");
            RejectSgf("(;SZ[9];B[aa];RU[Japanese])", "拒绝中途改变规则");

            string koSetup = "(;SZ[9]RU[Japanese]AB[ba][ab][bc]AW[bb][ca][db][cc]PL[B])";
            GoGame simple = GoGame.FromSgf(koSetup);
            Play(simple, 2, 1);
            string error;
            Check(!simple.TryPlay(1, 1, out error) && error.Contains("立即回提"), "日本规则也禁止单劫立即回提");
            simple.Pass();
            Check(simple.Position == 2 && simple.ToPlay == Stone.Black, "日本规则允许停一手");

            // Three independent kos restore the entire board after six plies.
            // Simple ko allows the cycle; positional superko forbids its last move.
            string tripleSetup = ";SZ[19]AB[ba][ab][bc][hb][ia][jb][ic][bg][ah][bi]" +
                "AW[bb][ca][db][cc][ha][gb][hc][bh][cg][dh][ci]PL[B]";
            GoGame cycle = GoGame.FromSgf("(" + tripleSetup + "RU[Japanese])");
            int[,] sequence = new int[,] { { 2, 1 }, { 8, 1 }, { 2, 7 }, { 1, 1 }, { 7, 1 }, { 1, 7 } };
            for (int i = 0; i < 6; i++) Play(cycle, sequence[i, 0], sequence[i, 1]);
            Check(cycle.Position == 6 && cycle.BlackCaptures == 3 && cycle.WhiteCaptures == 3,
                "日本单劫规则允许六手三劫循环");
            GoGame cycleRoundTrip = GoGame.FromSgf(cycle.ToSgf());
            Check(cycleRoundTrip.Position == 6 && cycleRoundTrip.Rules == "japanese", "日本规则重复局面棋谱可往返");
            GoGame superko = GoGame.FromSgf("(" + tripleSetup + "RU[Chinese])");
            for (int i = 0; i < 5; i++) Play(superko, sequence[i, 0], sequence[i, 1]);
            Check(!superko.TryPlay(sequence[5, 0], sequence[5, 1], out error) && error.Contains("全局同形"),
                "中国全局同形规则禁止三劫循环复原");
            Check(superko.Position == 5 && superko.WhiteCaptures == 2, "被禁止的循环不改变棋局");

            foreach (double allowed in new double[] { -400, -100, 0, 7.5, 100, 400 })
            {
                GoGame bounded = new GoGame(9, allowed);
                Check(GoGame.FromSgf(bounded.ToSgf()).Komi == allowed, "合法贴目边界往返：" + allowed);
            }
            foreach (double invalid in new double[] { -400.5, 400.5, 6.25, 6.2, -0.25, Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity })
            {
                bool rejected = false;
                try { game.Komi = invalid; } catch (ArgumentException) { rejected = true; }
                Check(rejected && game.Komi == 7.5, "拒绝引擎不支持的贴目且保留原值");
                bool constructorRejected = false;
                try { new GoGame(9, invalid); } catch (ArgumentException) { constructorRejected = true; }
                Check(constructorRejected, "构造函数拒绝非半目或无效贴目");
                bool resetRejected = false;
                try { game.NewGame(9, invalid, 0); } catch (ArgumentException) { resetRejected = true; }
                Check(resetRejected && game.Size == 19 && game.Komi == 7.5, "重开对局拒绝无效贴目且保留原棋局");
            }
            RejectSgf("(;SZ[9]KM[400.5])", "SGF 拒绝过大贴目");
            RejectSgf("(;SZ[9]KM[-400.5])", "SGF 拒绝过小贴目");
            RejectSgf("(;SZ[9]KM[NaN])", "SGF 拒绝 NaN 贴目");
            RejectSgf("(;SZ[9]KM[6.25])", "SGF 拒绝非半整数贴目");
            RejectSgf("(;SZ[9]KM[6.2])", "SGF 拒绝十分目贴目");
            bool newGameRejected = false;
            try { game.NewGame(9, 401, 0); } catch (ArgumentException) { newGameRejected = true; }
            Check(newGameRejected && game.Size == 19 && game.Komi == 7.5, "非法贴目新局不破坏原棋局");
        }

        private static void TestHandicap()
        {
            foreach (int size in new int[] { 9, 13, 19 })
                for (int handicap = 2; handicap <= 9; handicap++)
                {
                    GoGame game = new GoGame();
                    game.NewGame(size, 0.5, handicap);
                    int count = 0;
                    for (int x = 0; x < size; x++)
                        for (int y = 0; y < size; y++) if (game.Board[x, y] == Stone.Black) count++;
                    Check(count == handicap && game.InitialStones.Count == handicap && game.ToPlay == Stone.White, "标准让子 " + size + " 路 " + handicap + " 子");
                    GoGame roundTrip = GoGame.FromSgf(game.ToSgf());
                    Check(roundTrip.InitialPlayer == Stone.White && roundTrip.InitialStones.Count == handicap, "让子 SGF 往返");
                }
        }

        private static void TestSgf()
        {
            GoGame game = GoGame.FromSgf("(;GM[1]FF[4]SZ[13]KM[6.5]C[escaped \\] bracket and \\\\ slash]PL[B];B[dd];W[jj](;B[cd];W[])(;B[ee]))");
            Check(game.Size == 13 && Math.Abs(game.Komi - 6.5) < 0.0001 && game.Moves.Count == 4, "SGF 主线与贴目");
            Check(game.Board[2, 3] == Stone.Black && game.Board[4, 4] == Stone.Empty && game.Moves[3].IsPass, "仅导入第一变化");
            GoGame roundTrip = GoGame.FromSgf(game.ToSgf());
            Check(roundTrip.Moves.Count == 4 && roundTrip.Board[9, 9] == Stone.White && roundTrip.ToSgf().Contains("escaped \\]"), "SGF 转义及落子往返");
            GoGame setup = GoGame.FromSgf("(;SZ[9]AB[aa:bb]AW[cc]AE[ab]PL[W];W[dd])");
            Check(setup.InitialStones.Count == 4 && setup.InitialPlayer == Stone.White && setup.Board[0, 1] == Stone.Empty, "SGF 摆子范围与清除");
            Check(setup.Board[3, 3] == Stone.White && setup.ToPlay == Stone.Black, "SGF PL 初始行棋方");
            Check(GoGame.FromSgf("(;SZ[9];W[dd])").InitialPlayer == Stone.White, "题目棋谱白先推断");
            Check(GoGame.FromSgf("(;SZ[19];B[tt];W[])").IsOver, "兼容旧版 tt 停一手");
            RejectSgf("(;SZ[9];B[aa];AB[bb])", "拒绝中途摆子");
            RejectSgf("(;SZ[9];B[aa];PL[B];B[bb])", "拒绝中途改变轮次");
            RejectSgf("(;SZ[9];B[aa];W[aa])", "拒绝非法棋谱落子");
            RejectSgf("(;SZ[9]AB[aa]AW[aa])", "拒绝冲突摆子");
            RejectSgf("(;SZ[19:19])", "拒绝不支持的棋盘大小格式");
            RejectSgf("(;SZ[9]C[unclosed)", "拒绝未闭合 SGF");
            RejectSgf("(;SZ[9])(;SZ[13])", "拒绝多棋谱集合避免误存");
        }
    }
}
