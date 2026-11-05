using System;

namespace GoDesk
{
    internal static class RedesignTests
    {
        private static int assertions;
        private static void Check(bool valid, string message) { assertions++; if (!valid) throw new Exception("界面数据回归：" + message); }
        private static AnalysisResult Result(double value, StudyRegion region = null) { return new AnalysisResult { BlackWinrate = value, Visits = 256, Region = region }; }
        public static string Run()
        {
            assertions = 0; var history = new AnalysisTimeline(); var game = new GoGame(9); string error;
            Check(history.Snapshot(game).Length == 1 && !history.Snapshot(game)[0].HasValue, "新盘不应有装饰数据");
            history.Record(game, Result(.63)); Check(history.Snapshot(game)[0] == .63, "当前根局面记录错误");
            game.TryPlay(2, 2, out error); history.Record(game, Result(.48));
            Check(history.Snapshot(game)[0] == .63 && history.Snapshot(game)[1] == .48, "落子前后历史错位");
            game.TryPlay(6, 6, out error); game.TryPlay(3, 3, out error); history.Record(game, Result(.7));
            Check(!history.Snapshot(game)[2].HasValue && history.Snapshot(game)[3] == .7, "未分析手数被插值填充");
            game.Goto(1); Check(history.Snapshot(game)[3] == .7, "复盘丢失当前分支未来分析");
            game.TryPlay(5, 5, out error); var branch = history.Snapshot(game);
            Check(branch.Length == 3 && !branch[2].HasValue && branch[1] == .48, "新分支混入旧分支数据");
            history.Record(game, Result(.22, new StudyRegion(0, 0, 3, 3)));
            Check(!history.Snapshot(game)[2].HasValue, "局部值混入全局历史");
            history.Record(game, Result(Double.NaN)); history.Record(game, Result(1.1)); history.Record(game, new AnalysisResult { BlackWinrate = .5, Visits = 0 });
            Check(!history.Snapshot(game)[2].HasValue, "无效或未计算结果进入历史");
            game.Komi = 0.5; Check(!history.Snapshot(game)[0].HasValue && !history.Snapshot(game)[1].HasValue, "贴目改变仍显示旧值");
            history.Record(game, Result(.31)); game.Rules = "japanese";
            Check(!history.Snapshot(game)[2].HasValue, "规则改变仍显示旧值");
            var other = GoGame.FromSgf("(;SZ[9]KM[7.5]AW[cc]PL[W])");
            Check(!history.Snapshot(other)[0].HasValue, "跨棋局混入数据"); history.Record(other, Result(.12));
            history.Clear(); Check(!history.Snapshot(other)[0].HasValue, "引擎上下文清空无效");
            var pass = new GoGame(9); history.Record(pass, Result(.8)); pass.Pass(); history.Record(pass, Result(.4));
            Check(history.Snapshot(pass)[1] == .4, "停一手历史错误"); pass.Goto(0); pass.TryPlay(1, 1, out error);
            Check(!history.Snapshot(pass)[1].HasValue && history.Snapshot(pass)[0] == .8, "停着与实着指纹冲突");
            Check(UiTheme.Icon("camera") != null && UiTheme.Icon("home") != null && UiTheme.ResourceImage("board-wood.png") != null, "嵌入资源缺失");
            return "界面数据检查通过：" + assertions + " 项（真实历史、缺测空段、前缀分支、停着、规则/贴目、模型上下文清空、局部隔离与嵌入资源）。";
        }
    }
}
