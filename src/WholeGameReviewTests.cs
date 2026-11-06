using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GoDesk
{
    internal static class WholeGameReviewTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("整盘分析测试：" + message);
        }

        public static string Run()
        {
            var thresholds = new ReviewThresholds();
            var best = new MoveCandidate { Move = "B8", ScoreLead = -3, Winrate = .32, Visits = 100, Order = 0 };
            var actual = new MoveCandidate { Move = "A9", ScoreLead = 2, Winrate = .6, Visits = 40, Order = 1 };
            var analysis = new AnalysisResult { BestMove = best.Move, Candidates = new List<MoveCandidate> { best, actual },
                BlackScoreLead = -2.7, BlackWinrate = .34, Visits = 256 };
            var white = new ReviewRow(1, new Move(0, 0, Stone.White), 9, 256, false, analysis, actual);
            Check(white.HasEvaluation && !white.IsFirstChoice && white.PointLoss == 5, "白棋目损方向错误");
            Check(Math.Abs(white.WinrateLoss - 28) < .000001 && white.Grade(thresholds) == ReviewGrade.Bad, "白棋胜率降幅或等级错误");
            var black = new ReviewRow(2, new Move(0, 0, Stone.Black), 9, 256, false, analysis, actual);
            Check(black.PointLoss == 0 && black.WinrateLoss == 0 && black.Grade(thresholds) == ReviewGrade.Good, "负目损应截为零");
            var first = new ReviewRow(3, new Move(1, 1, Stone.White), 9, 256, false, analysis, best);
            Check(first.IsFirstChoice && first.Grade(thresholds) == ReviewGrade.Best && first.PointLoss == 0, "精确首选识别错误");
            actual.ScoreLead = 3;
            var blunder = new ReviewRow(4, new Move(0, 0, Stone.White), 9, 256, false, analysis, actual);
            Check(blunder.PointLoss == 6 && blunder.Grade(thresholds) == ReviewGrade.Blunder, "6 目边界应计为恶手");
            Check(blunder.Grade(new ReviewThresholds(1, 2, 4, 7)) == ReviewGrade.Bad, "阈值变更应重新分类");
            actual.ScoreLead = -1.5;
            var good = new ReviewRow(5, new Move(0, 0, Stone.White), 9, 256, false, analysis, actual);
            Check(good.PointLoss == 1.5 && good.Grade(thresholds) == ReviewGrade.Good, "1.5 目边界应计为好手");
            bool rejected = false;
            try { new ReviewThresholds(1.5, 3, 3, 6); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "重叠阈值应拒绝");

            var summary = new ReviewSummary();
            summary.Add(first, thresholds);
            summary.Add(white, thresholds);
            summary.Add(blunder, thresholds);
            summary.Add(new ReviewRow(6, new Move(2, 2, Stone.Black), 9, 256, false, error: "没有评估"), thresholds);
            summary.Add(new ReviewRow(7, new Move(-1, -1, Stone.White), 9, 256, true), thresholds);
            summary.Add(new ReviewRow(8, new Move(3, 3, Stone.Black), 9, 256, false), thresholds);
            Check(summary.EligibleCount == 5 && summary.EvaluatedCount == 3 && summary.PendingCount == 1, "有效样本分母错误");
            Check(summary.UnavailableCount == 1 && summary.TerminalPassCount == 1, "缺测和终局停着必须分开");
            Check(summary.FirstCount == 1 && summary.GoodCount == 1 && summary.BlunderCount == 1 &&
                Math.Abs(summary.AveragePointLoss - 11.0 / 3) < .000001, "汇总指标错误");
            Check(Double.IsNaN(new ReviewSummary().AveragePointLoss), "零样本不应显示 0 目");

            GoGame game = GoGame.FromSgf("(;SZ[9]KM[6.5]AB[ee]PL[W];W[aa];B[];W[cc];B[];W[])");
            game.Goto(1);
            using (var review = new WholeGameReview(game, EngineSettings.CreateCpu(), 32))
            {
                game.Goto(0);
                game.Pass();
                ReviewRow[] rows = review.Snapshot();
                Check(review.TotalMoves == 5 && review.CompletedCount == 2 && !rows[1].IsTerminalPass &&
                    rows[3].IsTerminalPass && rows[4].IsTerminalPass, "冻结主线和终局停着识别错误");
                GoGame copy = review.GetPosition(2);
                Check(copy.Position == 2 && copy.Moves.Count == 5 && copy.InitialPlayer == Stone.White &&
                    copy.Board[4, 4] == Stone.Black && copy.Board[0, 0] == Stone.White, "初始摆子、先行方或历史前缀未冻结");
                copy.Board[0, 0] = Stone.Empty;
                Check(review.GetPosition(2).Board[0, 0] == Stone.White, "查看副本不能修改冻结报告");
                ReviewSummary whiteSlice = review.Summarize(Stone.White, 2, 5, thresholds);
                Check(whiteSlice.EligibleCount == 1 && whiteSlice.PendingCount == 1 && whiteSlice.TerminalPassCount == 1,
                    "按棋色、手数筛选错误");
            }
            return "整盘复盘：白黑视角、分级边界、统计分母、终局停着、冻结主线和区间筛选通过。";
        }

        internal static async Task<string> RunEngineChecksAsync(EngineSettings settings)
        {
            GoGame game = GoGame.FromSgf("(;SZ[9]KM[6.5]AB[ee]PL[W];W[aa];B[];W[cc];B[];W[])");
            game.Goto(0);
            using (var engine = new KataGoEngine(settings))
            {
                AnalysisResult forced = await engine.AnalyzeReviewAsync(game, 32, game.Moves[0]).ConfigureAwait(false);
                Check(forced.BestMove == "A9" && forced.Candidates.Count == 1 && forced.Candidates[0].Visits > 0,
                    "真实引擎必须只补算实战着点");
                Check(forced.Candidates[0].Pv.Length > 1, "实战补算限制只能施加在根节点");
                game.Goto(1);
                AnalysisResult pass = await engine.AnalyzeReviewAsync(game, 32, game.Moves[1]).ConfigureAwait(false);
                Check(pass.BestMove == "pass" && pass.Candidates[0].Visits > 0, "非终局停着应可单独补算");
            }
            using (var review = new WholeGameReview(game, settings, 32))
            {
                bool paused = false;
                review.Changed += delegate
                {
                    if (!paused && review.CompletedCount == 3 && review.IsRunning)
                    {
                        paused = true;
                        review.Pause();
                    }
                };
                await review.RunAsync().ConfigureAwait(false);
                ReviewRow original = review.Snapshot()[0];
                Check(paused && !review.IsRunning && review.CompletedCount == 3 && original.HasEvaluation,
                    "暂停应保留已完成着法");
                await review.RunAsync().ConfigureAwait(false);
                Check(review.CompletedCount == 5 && Object.ReferenceEquals(original, review.Snapshot()[0]) &&
                    review.Summarize(Stone.Empty, 1, 5, new ReviewThresholds()).EvaluatedCount == 3,
                    "续算不应重算已完成着法，终局两次停着不能计入样本");
                ReviewRow deep = await review.RecheckAsync(1, 64).ConfigureAwait(false);
                ReviewRow cached = await review.RecheckAsync(1, 64).ConfigureAwait(false);
                Check(deep.HasEvaluation && deep.Visits == 64 && Object.ReferenceEquals(deep, cached) &&
                    Object.ReferenceEquals(original, review.Snapshot()[0]), "深度复算缓存不应污染原始预算报告");
            }
            return "真实 KataGo：实战点/停着根节点补算、完整手顺、整盘暂停续算和复算缓存通过。";
        }
    }
}
