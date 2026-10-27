using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace GoDesk
{
    public static class LocalStudyTests
    {
        private static int assertions;

        public static string Run()
        {
            assertions = 0;
            CheckRegions();
            CheckLegalMoves();
            CheckKoHistory();
            CheckWireRestrictions();
            CheckResultValidation();
            CheckPreflightDoesNotStartEngine();
            return "局部研究检查通过：" + assertions + " 项断言（范围、复盘前缀、禁入/劫、双边协议、候选/PV 校验）。";
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("局部研究测试失败：" + message);
        }

        private static void Reject<T>(Action operation, string message) where T : Exception
        {
            bool rejected = false;
            try { operation(); } catch (T) { rejected = true; }
            Check(rejected, message);
        }

        private static void Play(GoGame game, int x, int y)
        {
            string error;
            if (!game.TryPlay(x, y, out error)) throw new Exception("测试准备落子失败：" + error);
        }

        private static void CheckRegions()
        {
            var region = new StudyRegion(12, 8, 8, 3);
            Check(region.Left == 8 && region.Top == 3 && region.Right == 12 && region.Bottom == 8, "反向拖框归一化");
            Check(region.Contains(8, 3) && region.Contains(12, 8) && region.Contains(10, 5), "包含四边与内部点");
            Check(!region.Contains(7, 3) && !region.Contains(13, 8) && !region.Contains(8, 2) && !region.Contains(12, 9), "不包含四边外的点");
            Check(region.Describe(19) == "I4–M9", "范围使用连续含I、顶部为1的显示坐标");
            Check(new StudyRegion(8, 0, 8, 0).Describe(19) == "I1–I1", "单交叉点研究范围");
            foreach (int size in new int[] { 9, 13, 19, 25 })
            {
                var full = new StudyRegion(size - 1, size - 1, 0, 0);
                full.Validate(size);
                Check(full.Contains(0, 0) && full.Contains(size - 1, size - 1), "全棋盘范围包含边角");
            }
            Reject<ArgumentOutOfRangeException>(() => region.Validate(9), "换小棋盘拒绝旧范围");
            Reject<ArgumentOutOfRangeException>(() => new StudyRegion(-1, 0, 2, 2).Validate(19), "拒绝负坐标");
            Reject<ArgumentOutOfRangeException>(() => new StudyRegion(0, 0, 19, 2).Validate(19), "拒绝超右边界");
            Reject<ArgumentOutOfRangeException>(() => new StudyRegion(0, 0, 2, 19).Validate(19), "拒绝超下边界");
            Reject<ArgumentOutOfRangeException>(() => new StudyRegion(Int32.MinValue, 0, Int32.MaxValue, 0).Validate(19), "极端坐标不会溢出后成为有效范围");
            Reject<ArgumentOutOfRangeException>(() => region.Validate(1), "拒绝过小棋盘");
            Reject<ArgumentOutOfRangeException>(() => region.Describe(26), "描述范围也校验棋盘边界");
        }

        private static void CheckUnchanged(GoGame game, string before, int position, Stone[,] board, string message)
        {
            bool same = game.ToSgf() == before && game.Position == position && game.Board.GetLength(0) == board.GetLength(0);
            for (int x = 0; same && x < game.Size; x++)
                for (int y = 0; same && y < game.Size; y++)
                    if (game.Board[x, y] != board[x, y]) same = false;
            Check(same, message);
        }

        private static void CheckLegalMoves()
        {
            var single = new StudyRegion(0, 0, 0, 0);
            var game = new GoGame(9);
            string before = game.ToSgf();
            Check(LocalStudy.HasLegalMove(game, single), "单个空点可以研究");
            Check(game.Position == 0 && game.ToPlay == Stone.Black && game.ToSgf() == before, "探测合法着不向原棋局落子");
            Play(game, 0, 0);
            Check(!LocalStudy.HasLegalMove(game, single), "全被占据的局部没有合法落点");
            Check(LocalStudy.HasLegalMove(game, new StudyRegion(0, 0, 1, 0)), "范围中部分被占据仍能找到合法空点");
            Reject<ArgumentNullException>(() => LocalStudy.HasLegalMove(null, single), "拒绝空棋局");
            Reject<ArgumentNullException>(() => LocalStudy.HasLegalMove(game, null), "拒绝空范围");
            Reject<ArgumentOutOfRangeException>(() => LocalStudy.HasLegalMove(game, new StudyRegion(8, 8, 9, 9)), "预检查拒绝越界范围");

            var suicide = GoGame.FromSgf("(;SZ[9]KM[6.5]RU[Japanese]AW[ba][ab][cb][bc]PL[B])");
            var center = new StudyRegion(1, 1, 1, 1);
            before = suicide.ToSgf();
            Stone[,] board = (Stone[,])suicide.Board.Clone();
            Check(!LocalStudy.HasLegalMove(suicide, center), "空交叉点为禁入时不能当成可走");
            CheckUnchanged(suicide, before, 0, board, "禁入检查保留初始摆子、贴目、日本规则与棋盘");
            var capture = GoGame.FromSgf("(;SZ[9]AB[ca][bb]AW[ba]PL[B])");
            Check(LocalStudy.HasLegalMove(capture, single), "无气但提子后合法的落点被正确允许");
            Check(capture.BlackCaptures == 0 && capture.Position == 0 && capture.Board[1, 0] == Stone.White,
                "提子探测没有改变原棋盘和提子数");

            var future = GoGame.FromSgf("(;SZ[9]KM[-0.5]RU[Japanese]PB[黑方]PW[白方];B[aa];W[bb];B[];W[])");
            future.Goto(1);
            before = future.ToSgf(); board = (Stone[,])future.Board.Clone();
            Check(LocalStudy.HasLegalMove(future, center), "复盘只检查当前前缀，忽略未来占点与结束停着");
            CheckUnchanged(future, before, 1, board, "复盘预检不改变完整后续、当前位置与元数据");
            Check(future.Moves.Count == 4 && future.ToPlay == Stone.White && future.Rules == "japanese" && future.Komi == -0.5,
                "复盘合法性检查保留轮次和规则贴目");
            future.Goto(4);
            Check(!LocalStudy.HasLegalMove(future, new StudyRegion(2, 2, 3, 3)), "已经连续停着结束时没有可继续研究的着法");

            var whiteSetup = new GoGame(9);
            whiteSetup.NewGame(9, 0.5, 2, Stone.White);
            Check(LocalStudy.HasLegalMove(whiteSetup, single) && whiteSetup.InitialPlayer == Stone.Black && whiteSetup.InitialStones.Count == 2,
                "白方受让局面按实际黑方轮次检查且不丢摆子");
        }

        private static void CheckKoHistory()
        {
            const string setup = "(;SZ[9]AB[ba][ab][bc]AW[bb][ca][db][cc]PL[B])";
            foreach (string rules in new string[] { "Chinese", "Japanese" })
            {
                var ko = GoGame.FromSgf(setup);
                ko.Rules = rules;
                Play(ko, 2, 1);
                string before = ko.ToSgf();
                Stone[,] board = (Stone[,])ko.Board.Clone();
                Check(!LocalStudy.HasLegalMove(ko, new StudyRegion(1, 1, 1, 1)), "局部预检遵守" + rules + "立即回提禁令");
                CheckUnchanged(ko, before, 1, board, "劫禁点检查不改变已提子局面和历史");
                Play(ko, 8, 8); Play(ko, 7, 8);
                Check(LocalStudy.HasLegalMove(ko, new StudyRegion(1, 1, 1, 1)), "交换劫材后局部回提可行");
            }
            const string triple = ";SZ[19]AB[ba][ab][bc][hb][ia][jb][ic][bg][ah][bi]AW[bb][ca][db][cc][ha][gb][hc][bh][cg][dh][ci]PL[B]";
            int[,] sequence = { { 2, 1 }, { 8, 1 }, { 2, 7 }, { 1, 1 }, { 7, 1 } };
            foreach (string rules in new string[] { "Chinese", "Japanese" })
            {
                GoGame game = GoGame.FromSgf("(" + triple + "RU[" + rules + "])");
                for (int i = 0; i < 5; i++) Play(game, sequence[i, 0], sequence[i, 1]);
                string before = game.ToSgf();
                Check(LocalStudy.HasLegalMove(game, new StudyRegion(1, 7, 1, 7)) == (rules == "Japanese"),
                    "克隆保留全历史，区分日本单劫与中国全局同形");
                Check(game.ToSgf() == before && game.Position == 5, "全局同形合法性探测没有产生第六手");
            }
        }

        private static void CheckWireRestrictions()
        {
            var entries = LocalStudy.CreateAvoidMoves(19, new StudyRegion(9, 1, 8, 0));
            Check(entries.Length == 2 && (string)entries[0]["player"] == "B" && (string)entries[1]["player"] == "W", "黑白双方各有且仅有一项禁止着法，兼容CPU1.14.1");
            foreach (Dictionary<string, object> entry in entries)
            {
                string[] moves = (string[])entry["moves"];
                var prohibited = new HashSet<string>(moves);
                Check(moves.Length == 358 && prohibited.Count == moves.Length && prohibited.Contains("A19") && prohibited.Contains("T1"),
                    "精确禁止361个点中的357个框外点和pass，使用含T而跳I的引擎GTP坐标");
                Check(!prohibited.Contains("J19") && !prohibited.Contains("K19") && !prohibited.Contains("J18") && !prohibited.Contains("K18"),
                    "屏幕I1–J2对应GTP J19/K19/J18/K18，四个框内点均未禁止");
                Check((int)entry["untilDepth"] == 1000000000 && entry.Count == 3, "限制贯穿允许的深度，使用确切三字段协议");
                Check(prohibited.Contains("pass"), "强制局部研究显式禁止pass");
                bool exactComplement = true;
                for (int y = 0; y < 19; y++)
                    for (int x = 0; x < 19; x++)
                        if (prohibited.Contains(new Move(x, y, Stone.Black).ToGtp(19)) == (x >= 8 && x <= 9 && y <= 1)) exactComplement = false;
                Check(exactComplement, "双方禁止集合恰好为全盘减研究矩形，边界没有遗漏或多禁");
            }
            Check(!Object.ReferenceEquals(entries[0]["moves"], entries[1]["moves"]), "双方列表没有共享可变数组");
            string[] fullBoard = (string[])LocalStudy.CreateAvoidMoves(9, new StudyRegion(0, 0, 8, 8))[0]["moves"];
            Check(fullBoard.Length == 1 && fullBoard[0] == "pass", "全棋盘范围只禁止pass，所有交叉点保留原生合法性检查");
            Reject<ArgumentOutOfRangeException>(() => LocalStudy.CreateAvoidMoves(9, new StudyRegion(8, 0, 9, 0)), "生成协议前拒绝越界范围");
        }

        private static Dictionary<string, object> Analysis(string moves)
        {
            return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(
                "{\"rootInfo\":{\"winrate\":0.5,\"scoreLead\":1.5,\"visits\":32},\"moveInfos\":[" + moves + "]}");
        }

        private static void CheckResultValidation()
        {
            var region = new StudyRegion(8, 0, 9, 1);
            var valid = Analysis("{\"move\":\"K19\",\"order\":1,\"pv\":[\"K19\",\"J18\"]},{\"move\":\"J19\",\"order\":0,\"pv\":[\"J19\",\"K18\"]}");
            AnalysisResult result = KataGoEngine.ParseResult(valid, 19, region);
            Check(result.BestMove == "J19" && result.Candidates.Count == 2, "局部结果保留按引擎order排列的全部候选");
            Check(result.Candidates[0].Pv.Length == 2 && result.Candidates[0].Pv[1] == "K18", "候选PV被解析并保留");
            Check(Object.ReferenceEquals(result.Region, region), "局部结果携带该请求的范围身份");
            Check(KataGoEngine.ParseResult(valid, 19).Region == null, "默认全局结果没有局部标记");
            Reject<InvalidDataException>(() => KataGoEngine.ParseResult(Analysis("{\"move\":\"A1\",\"order\":0}"), 19, region), "拒绝范围外最佳着法");
            Reject<InvalidDataException>(() => KataGoEngine.ParseResult(Analysis("{\"move\":\"J19\",\"order\":0},{\"move\":\"A1\",\"order\":1}"), 19, region), "非最佳候选也不能混入区外着法");
            Reject<InvalidDataException>(() => KataGoEngine.ParseResult(Analysis("{\"move\":\"J19\",\"order\":0,\"pv\":[\"J19\",\"A1\"]}"), 19, region), "最佳着法在区内也拒绝后续PV脱先");
            Reject<InvalidDataException>(() => KataGoEngine.ParseResult(Analysis("{\"move\":\"pass\",\"order\":0}"), 19, region), "局部结果不得以pass回退");
            Reject<InvalidDataException>(() => KataGoEngine.ParseResult(Analysis("{\"move\":\"J19\",\"order\":0,\"pv\":[\"J19\",\"pass\"]}"), 19, region), "局部PV也不得含pass");
            Reject<InvalidDataException>(() => KataGoEngine.ParseResult(Analysis("{\"move\":\"I19\",\"order\":0}"), 19, region), "引擎坐标不能误用显示I列");
            AnalysisResult global = KataGoEngine.ParseResult(Analysis("{\"move\":\"pass\",\"order\":0,\"pv\":[\"pass\",\"A1\"]}"), 19);
            Check(global.BestMove == "pass" && global.Candidates[0].Pv.Length == 2, "全局分析仍支持pass和全盘PV");
        }

        private static void CheckPreflightDoesNotStartEngine()
        {
            var game = new GoGame(9);
            Play(game, 0, 0);
            var missingEngine = new EngineSettings { Backend = "custom", EnginePath = "missing-local-study-test-engine.exe", ModelPath = "missing-local-study-test-network.bin.gz", ModelId = "" };
            using (var engine = new KataGoEngine(missingEngine))
            {
                var task = engine.AnalyzeAsync(game, 16, false, null, new StudyRegion(0, 0, 0, 0));
                Check(task.IsCompleted && task.IsFaulted, "区内无合法着时在第一个await和引擎启动前返回错误");
                bool explained = false;
                try { task.GetAwaiter().GetResult(); }
                catch (InvalidOperationException ex) { explained = ex.Message.Contains("没有") && ex.Message.Contains("全局"); }
                Check(explained && !engine.IsReady && engine.Status.Contains("尚未启动"), "无合法着提示可操作且没有启动引擎或全局回退");
                Check(game.Position == 1 && game.Moves.Count == 1 && game.ToPlay == Stone.White, "失败的局部请求不改原棋局");
            }
        }
    }
}
