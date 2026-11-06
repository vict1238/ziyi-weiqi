using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace GoDesk
{
    internal static class OnlineRoomTests
    {
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("联机测试失败：" + description);
        }

        private static async Task WaitFor(Func<bool> condition, string description)
        {
            Stopwatch time = Stopwatch.StartNew();
            while (!condition())
            {
                Check(time.Elapsed < TimeSpan.FromSeconds(12), description);
                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        private static OnlineCommand Action(string kind)
        {
            return new OnlineCommand { Kind = kind };
        }

        private static async Task Command(OnlineSession sender, OnlineSession receiver, OnlineCommand command)
        {
            long revision = sender.State.Revision;
            command.Revision = revision;
            sender.Send(command);
            await WaitFor(delegate { return receiver.State.Revision > revision && sender.State.Revision == receiver.State.Revision; },
                command.Kind + " 未同步").ConfigureAwait(false);
            Check(sender.LastError == null, command.Kind + " 返回错误");
        }

        private static async Task RejectInvitation(string invitation)
        {
            try
            {
                using (OnlineSession guest = await OnlineSession.JoinAsync(invitation, "无效邀请").ConfigureAwait(false))
                    throw new Exception("无效邀请意外加入。");
            }
            catch (InvalidOperationException) { }
        }

        private static async Task MatchAndStudy(IPAddress address)
        {
            using (OnlineSession host = OnlineSession.Host(address, 0, OnlineRoomMode.Match, Stone.Black, new GoGame(9, 6.5), "甲"))
            {
                var invalid = OnlineInvite.Parse(host.Invitation);
                invalid.Pin = new String('0', 64);
                await RejectInvitation(invalid.Format()).ConfigureAwait(false);
                invalid = OnlineInvite.Parse(host.Invitation);
                invalid.Secret = new String('0', 48);
                await RejectInvitation(invalid.Format()).ConfigureAwait(false);
                using (OnlineSession guest = await OnlineSession.JoinAsync(host.Invitation, "乙").ConfigureAwait(false))
                {
                    Check(host.State.Connected && guest.State.Connected && !host.State.Started, "邀请、双方席位和待开局状态");
                    Check(guest.Seat == 1 && host.State.GuestName == "乙" && guest.State.GetGame().Komi == 6.5, "房间基本信息");
                    await RejectInvitation(host.Invitation).ConfigureAwait(false);
                    await Command(guest, host, Action("ready")).ConfigureAwait(false);
                    await Command(host, guest, Action("start")).ConfigureAwait(false);
                    Check(host.State.Started && guest.State.Started, "双方确认开局");
                    guest.Send(Action("study-copy"));
                    await WaitFor(delegate { return guest.LastError != null; }, "切磋未结束不能转研讨").ConfigureAwait(false);
                    guest.Send(Action("share"));
                    await WaitFor(delegate { return guest.LastError != null; }, "切磋中不能共享 AI").ConfigureAwait(false);
                    guest.Send(new OnlineCommand { Kind = "move", X = 1, Y = 1 });
                    await WaitFor(delegate { return guest.LastError != null; }, "白方抢走黑方轮次应被拒绝").ConfigureAwait(false);
                    await Command(host, guest, new OnlineCommand { Kind = "move", X = 2, Y = 2 }).ConfigureAwait(false);
                    Check(guest.State.GetGame().Board[2, 2] == Stone.Black, "黑棋实际同步");
                    long stale = guest.State.Revision - 1;
                    guest.Send(new OnlineCommand { Kind = "move", X = 3, Y = 3, Revision = stale });
                    await WaitFor(delegate { return guest.LastError != null; }, "旧棋局版本应被拒绝").ConfigureAwait(false);
                    Check(host.State.Position == 1, "旧版本没有改变棋局");
                    guest.Send(new OnlineCommand { Kind = "move", X = 2, Y = 2 });
                    await WaitFor(delegate { return guest.LastError != null; }, "重占同一点应被拒绝").ConfigureAwait(false);
                    await Command(guest, host, new OnlineCommand { Kind = "move", X = 3, Y = 3 }).ConfigureAwait(false);

                    var socket = (OnlinePeer)typeof(OnlineGuest).GetField("peer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(guest);
                    long beforeDrop = host.State.Revision;
                    socket.Dispose();
                    await WaitFor(delegate
                    {
                        return host.State.Revision >= beforeDrop + 2 && host.State.Connected && guest.State.Connected &&
                            guest.State.Revision == host.State.Revision;
                    }, "断线后同席位自动恢复").ConfigureAwait(false);
                    Check(guest.State.Sgf == host.State.Sgf && guest.State.Position == 2 && guest.State.GuestName == "乙", "断线后完整手顺和席位");
                    await Command(host, guest, Action("undo")).ConfigureAwait(false);
                    await Command(guest, host, new OnlineCommand { Kind = "respond", Accept = true }).ConfigureAwait(false);
                    Check(guest.State.GetGame().Moves.Count == 1 && guest.State.GetGame().Board[3, 3] == Stone.Empty, "双方同意后的撤销截断棋谱");
                    await Command(guest, host, Action("pass")).ConfigureAwait(false);
                    await Command(host, guest, Action("pass")).ConfigureAwait(false);
                    Check(guest.State.GetGame().IsOver, "连续停着结束落子");
                    await Command(host, guest, new OnlineCommand { Kind = "result", Text = "W+2.5" }).ConfigureAwait(false);
                    await Command(guest, host, new OnlineCommand { Kind = "respond", Accept = true }).ConfigureAwait(false);
                    Check(guest.State.Result == "W+2.5" && guest.State.ExportSgf().Contains("RE[W+2.5]"), "双方确认结果可导出");
                    await Command(guest, host, Action("study-copy")).ConfigureAwait(false);
                    await Command(host, guest, new OnlineCommand { Kind = "respond", Accept = true }).ConfigureAwait(false);
                    Check(guest.State.Mode == OnlineRoomMode.Study && guest.State.OriginalMatchSgf.Contains("RE[W+2.5]"), "研讨副本保留原局");
                    await Command(guest, host, new OnlineCommand { Kind = "navigate", Position = 1 }).ConfigureAwait(false);
                    await Command(host, guest, new OnlineCommand { Kind = "move", X = 5, Y = 5 }).ConfigureAwait(false);
                    Check(guest.State.GetGame().Moves.Count == 2 && guest.State.GetGame().Board[5, 5] == Stone.White, "原谱分支可试下");
                    await Command(guest, host, new OnlineCommand { Kind = "edit", Color = Stone.Black, X = 0, Y = 0 }).ConfigureAwait(false);
                    await Command(host, guest, new OnlineCommand { Kind = "edit", Color = Stone.White, X = 0, Y = 1 }).ConfigureAwait(false);
                    await Command(guest, host, new OnlineCommand { Kind = "edit", Color = Stone.Empty, X = 0, Y = 0 }).ConfigureAwait(false);
                    await Command(guest, host, Action("undo")).ConfigureAwait(false);
                    Check(host.State.Editing && host.State.GetGame().Board[0, 0] == Stone.Black && host.State.GetGame().Board[0, 1] == Stone.White, "双方可摆子、擦除和撤销");
                    await Command(host, guest, new OnlineCommand { Kind = "setup-player", Color = Stone.White }).ConfigureAwait(false);
                    await Command(guest, host, Action("finish-setup")).ConfigureAwait(false);
                    Check(guest.State.GetGame().ToPlay == Stone.White && guest.State.Position == 0, "摆子形成独立初始局面");
                    var summary = new OnlineSharedAnalysis
                    {
                        Model = "test-model", Visits = 128, BlackWinrate = .51, BlackScoreLead = 1.2,
                        Candidates = new[] { "D4" }, GtpMoves = new[] { "D4" }
                    };
                    long analyzed = guest.State.PositionRevision;
                    await Command(guest, host, new OnlineCommand { Kind = "share", PositionRevision = analyzed, Analysis = summary }).ConfigureAwait(false);
                    await Command(host, guest, new OnlineCommand { Kind = "share", PositionRevision = analyzed, Analysis = summary }).ConfigureAwait(false);
                    Check(guest.State.HostAnalysis != null && host.State.GuestAnalysis != null, "双方分别共享分析");
                    await Command(host, guest, new OnlineCommand { Kind = "move", X = 4, Y = 4 }).ConfigureAwait(false);
                    Check(guest.State.HostAnalysis == null && host.State.GuestAnalysis == null, "分析不会覆盖新局面");
                    guest.Send(new OnlineCommand { Kind = "share", PositionRevision = analyzed, Analysis = summary });
                    await WaitFor(delegate { return guest.LastError != null; }, "过期分析应被拒绝").ConfigureAwait(false);
                    await Command(guest, host, Action("original")).ConfigureAwait(false);
                    Check(guest.State.Position == 3 && guest.State.GetGame().IsOver, "可恢复原局完整手顺");
                    await Command(guest, host, Action("clear")).ConfigureAwait(false);
                    await Command(host, guest, new OnlineCommand { Kind = "respond", Accept = false }).ConfigureAwait(false);
                    Check(host.State.Position == 3, "拒绝清盘保留现状");
                    await Command(guest, host, new OnlineCommand { Kind = "import", Text = "(;GM[1]SZ[13]KM[6.5]AB[dd]PL[W];W[ff])" }).ConfigureAwait(false);
                    await Command(host, guest, new OnlineCommand { Kind = "respond", Accept = true }).ConfigureAwait(false);
                    Check(guest.State.GetGame().Size == 13 && guest.State.GetGame().Moves.Count == 1 && guest.State.Position == 0, "双方确认的棋谱替换");
                }
            }
        }

        public static async Task<string> RunAsync(IPAddress address)
        {
            var resignation = new OnlineAuthority(OnlineRoomMode.Match, Stone.White, new GoGame(9), "甲");
            resignation.Connect("乙");
            resignation.Apply(1, resignation.State.Revision, Action("ready"));
            resignation.Apply(0, resignation.State.Revision, Action("start"));
            resignation.Apply(0, resignation.State.Revision, Action("resign"));
            Check(resignation.State.Result == "B+R" && resignation.State.ExportSgf().Contains("RE[B+R]"), "认输写入正确赢家");
            await MatchAndStudy(address ?? IPAddress.Loopback).ConfigureAwait(false);
            return "联机 PASS：真实 TCP/TLS 双端；邀请指纹、邀请码、席位、开局、越权与过期命令、断线续弈、悔棋、终局、双端摆棋、试下、恢复原谱、共享分析及 SGF 替换。";
        }
    }
}
