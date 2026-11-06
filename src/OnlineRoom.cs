using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GoDesk
{
    internal enum OnlineRoomMode { Match, Study }

    internal sealed class OnlineRoomState
    {
        public OnlineRoomMode Mode { get; set; }
        public Stone HostColor { get; set; }
        public string HostName { get; set; }
        public string GuestName { get; set; }
        public bool Connected { get; set; }
        public bool Started { get; set; }
        public bool GuestReady { get; set; }
        public long Revision { get; set; }
        public long PositionRevision { get; set; }
        public string Sgf { get; set; }
        public int Position { get; set; }
        public string Result { get; set; }
        public OnlineProposal Proposal { get; set; }
        public OnlineSharedAnalysis HostAnalysis { get; set; }
        public OnlineSharedAnalysis GuestAnalysis { get; set; }
        public bool Editing { get; set; }
        public string Setup { get; set; }
        public Stone SetupPlayer { get; set; }
        public bool OriginalAvailable { get; set; }
        public string OriginalMatchSgf { get; set; }

        internal OnlineRoomState Copy() { return (OnlineRoomState)MemberwiseClone(); }

        public GoGame GetGame()
        {
            GoGame game = GoGame.FromSgf(Sgf);
            if (Editing) return OnlineAuthority.FromSetup(game, Setup, SetupPlayer);
            game.Goto(Position);
            return game;
        }

        public string ExportSgf()
        {
            if (!Editing) return Sgf;
            GoGame draft = GetGame();
            OnlineAuthority.ValidateSetup(draft);
            return draft.ToSgf();
        }
    }

    internal sealed class OnlineProposal
    {
        public string Kind { get; set; }
        public string Text { get; set; }
        public int Seat { get; set; }
    }

    internal sealed class OnlineSharedAnalysis
    {
        public string Model { get; set; }
        public long Visits { get; set; }
        public double BlackWinrate { get; set; }
        public double BlackScoreLead { get; set; }
        public string[] Candidates { get; set; }
        public string[] GtpMoves { get; set; }
        public string Region { get; set; }
    }

    internal sealed class OnlineCommand
    {
        public string Kind { get; set; }
        public long Revision { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public Stone Color { get; set; }
        public int Position { get; set; }
        public string Text { get; set; }
        public bool Accept { get; set; }
        public OnlineSharedAnalysis Analysis { get; set; }
        public long PositionRevision { get; set; }
    }

    // Called serially under the host session lock. Connection status and
    // proposals have their own revision without invalidating AI for this board.
    internal sealed class OnlineAuthority
    {
        internal const int MaxSgfChars = 65000;
        internal const int MaxMoves = 2000;
        private GoGame game;
        private string originalSgf;
        private GoGame pendingImport;
        private readonly Stack<Tuple<string, Stone>> edits = new Stack<Tuple<string, Stone>>();
        private readonly OnlineRoomState state;

        internal OnlineAuthority(OnlineRoomMode mode, Stone hostColor, GoGame initial, string name)
        {
            if (mode != OnlineRoomMode.Match && mode != OnlineRoomMode.Study) throw new ArgumentException("房间模式无效。");
            if (!IsPlayer(hostColor)) throw new ArgumentException("请选择房主执色。");
            if (initial == null) throw new ArgumentNullException("initial");
            if (mode == OnlineRoomMode.Match)
            {
                if (initial.Moves.Count != 0) throw new ArgumentException("切磋房必须从初始局面开始。");
                game = FromSetup(initial, BoardText(initial), initial.ToPlay);
            }
            else
            {
                game = ParseStudy(initial.ToSgf());
                game.Goto(initial.Position);
                originalSgf = game.ToSgf();
            }
            ValidateSize(game);
            ValidateSetup(game);
            state = new OnlineRoomState
            {
                Mode = mode, HostColor = hostColor, HostName = PlayerName(name, "房主"),
                OriginalAvailable = mode == OnlineRoomMode.Study, Revision = 1, PositionRevision = 1
            };
            WriteGame();
        }

        internal OnlineRoomState State { get { return state.Copy(); } }

        internal void Connect(string name)
        {
            state.GuestName = PlayerName(name, "棋友");
            state.Connected = true;
            if (state.Mode == OnlineRoomMode.Study) state.Started = true;
            state.Revision++;
        }

        internal void Disconnect()
        {
            state.Connected = false;
            state.Revision++;
        }

        internal static string PlayerName(string name, string fallback)
        {
            name = (name ?? "").Trim();
            if (name.Length > 24) throw new ArgumentException("昵称最多 24 个字符。");
            foreach (char c in name) if (Char.IsControl(c)) throw new ArgumentException("昵称包含无效字符。");
            return name.Length == 0 ? fallback : name;
        }

        internal void Apply(int seat, long revision, OnlineCommand command)
        {
            Require(seat == 0 || seat == 1, "房间席位无效。");
            Require(state.Connected, "等待棋友连接或重连。");
            Require(command != null && command.Kind != null, "房间操作为空。");
            // Independent analyses can arrive concurrently. They use a board
            // revision, while board edits always require the whole room revision.
            Require(command.Kind == "share" || revision == state.Revision, "房间已更新，请在最新局面上重试。");
            if (command.Kind == "respond" || command.Kind == "cancel-proposal")
                ResolveProposal(seat, command);
            else
            {
                Require(state.Proposal == null, "请先处理当前请求。");
                if (state.Mode == OnlineRoomMode.Match) MatchCommand(seat, command);
                else StudyCommand(seat, command);
            }
            state.Revision++;
        }

        private void MatchCommand(int seat, OnlineCommand command)
        {
            switch (command.Kind)
            {
                case "ready":
                    Require(seat == 1 && !state.Started, "只有未开局的客人可以准备。");
                    state.GuestReady = true;
                    return;
                case "start":
                    Require(seat == 0 && !state.Started && state.GuestReady, "请等待对方确认规则并准备。");
                    state.Started = true;
                    return;
                case "study-copy":
                    Require(!String.IsNullOrEmpty(state.Result), "请先确认切磋结果。");
                    SetProposal(seat, command.Kind, "");
                    return;
            }
            Require(state.Started && String.IsNullOrEmpty(state.Result), "当前没有可操作的切磋棋局。");
            switch (command.Kind)
            {
                case "move":
                case "pass":
                    Stone color = seat == 0 ? state.HostColor : GoGame.Opponent(state.HostColor);
                    Require(game.ToPlay == color, "当前轮到对方。");
                    Play(command);
                    break;
                case "undo":
                    Require(game.Position > 0, "当前没有可撤回的落子。");
                    SetProposal(seat, command.Kind, "");
                    break;
                case "result":
                    Require(game.IsOver, "连续两次停着后才能确认结果。");
                    SetProposal(seat, command.Kind, CheckResult(command.Text));
                    break;
                case "resign":
                    Stone loser = seat == 0 ? state.HostColor : GoGame.Opponent(state.HostColor);
                    state.Result = loser == Stone.Black ? "W+R" : "B+R";
                    WriteGame();
                    break;
                default:
                    throw new InvalidOperationException("切磋中只允许轮流落子及对局操作。");
            }
        }

        private void StudyCommand(int seat, OnlineCommand command)
        {
            switch (command.Kind)
            {
                case "move":
                case "pass":
                    Require(!state.Editing, "请先完成摆棋。");
                    Play(command);
                    break;
                case "navigate":
                    Require(!state.Editing, "请先完成或取消摆棋。");
                    Require(command.Position >= 0 && command.Position <= game.Moves.Count, "手数超出棋谱。");
                    if (command.Position != game.Position) { game.Goto(command.Position); BoardChanged(); }
                    break;
                case "edit":
                    Require(command.X >= 0 && command.X < game.Size && command.Y >= 0 && command.Y < game.Size, "摆子位置超出棋盘。");
                    Require(command.Color == Stone.Empty || IsPlayer(command.Color), "摆子颜色无效。");
                    BeginEdit();
                    int i = command.Y * game.Size + command.X;
                    if (state.Setup[i] == (char)('0' + (int)command.Color)) break;
                    RememberEdit();
                    char[] draft = state.Setup.ToCharArray();
                    draft[i] = (char)('0' + (int)command.Color);
                    state.Setup = new String(draft);
                    BoardChanged();
                    break;
                case "setup-player":
                    Require(IsPlayer(command.Color), "请选择黑先或白先。");
                    BeginEdit();
                    if (state.SetupPlayer == command.Color) break;
                    RememberEdit();
                    state.SetupPlayer = command.Color;
                    BoardChanged();
                    break;
                case "finish-setup":
                    Require(state.Editing, "当前没有摆棋草稿。");
                    GoGame setup = FromSetup(game, state.Setup, state.SetupPlayer);
                    ValidateSetup(setup);
                    ReplaceGame(setup);
                    break;
                case "cancel-setup":
                    Require(state.Editing, "当前没有摆棋草稿。");
                    ReplaceGame(game);
                    break;
                case "undo":
                    if (state.Editing)
                    {
                        Require(edits.Count > 0, "当前没有可撤销的摆棋。");
                        Tuple<string, Stone> previous = edits.Pop();
                        state.Setup = previous.Item1;
                        state.SetupPlayer = previous.Item2;
                    }
                    else
                    {
                        Require(game.Position > 0, "已在棋谱起点。");
                        game.Undo();
                    }
                    BoardChanged();
                    break;
                case "original":
                    Require(originalSgf != null, "当前没有原谱。");
                    Require(!state.Editing, "请先完成或取消摆棋。");
                    ReplaceGame(GoGame.FromSgf(originalSgf));
                    break;
                case "clear":
                    SetProposal(seat, command.Kind, "");
                    break;
                case "import":
                    pendingImport = ParseStudy(command.Text);
                    SetProposal(seat, command.Kind, "");
                    break;
                case "share":
                    Require(!state.Editing && command.PositionRevision == state.PositionRevision, "分析对应的局面已更新。");
                    OnlineSharedAnalysis analysis = ValidateAnalysis(command.Analysis);
                    if (seat == 0) state.HostAnalysis = analysis; else state.GuestAnalysis = analysis;
                    break;
                default:
                    throw new InvalidOperationException("研讨房不支持这个操作。");
            }
        }

        private void BeginEdit()
        {
            if (state.Editing) return;
            state.Editing = true;
            state.Setup = BoardText(game);
            state.SetupPlayer = game.ToPlay;
            BoardChanged();
        }

        private void RememberEdit()
        {
            Require(edits.Count < MaxMoves, "本次摆棋操作过多，请先完成摆棋。");
            edits.Push(Tuple.Create(state.Setup, state.SetupPlayer));
        }

        private void Play(OnlineCommand command)
        {
            Require(game.Position < MaxMoves, "已达到联机棋谱手数上限。");
            if (command.Kind == "move")
                Require(command.X >= 0 && command.X < game.Size && command.Y >= 0 && command.Y < game.Size, "落点超出棋盘。");
            string error;
            Move move = command.Kind == "pass" ? new Move(-1, -1, game.ToPlay) : new Move(command.X, command.Y, game.ToPlay);
            Require(game.TryPlayMove(move, out error), error);
            BoardChanged();
        }

        private void ReplaceGame(GoGame replacement)
        {
            game = replacement;
            state.Editing = false;
            state.Setup = null;
            edits.Clear();
            BoardChanged();
        }

        private void WriteGame()
        {
            string sgf = game.ToSgf();
            if (!String.IsNullOrEmpty(state.Result)) sgf = sgf.Insert(2, "RE[" + state.Result + "]");
            state.Sgf = sgf;
            state.Position = game.Position;
            if (!state.Editing) state.SetupPlayer = game.ToPlay;
        }

        private void BoardChanged()
        {
            state.PositionRevision++;
            state.HostAnalysis = state.GuestAnalysis = null;
            WriteGame();
        }

        private void SetProposal(int seat, string kind, string text)
        {
            state.Proposal = new OnlineProposal { Kind = kind, Seat = seat, Text = text };
        }

        private void ResolveProposal(int seat, OnlineCommand command)
        {
            OnlineProposal proposal = state.Proposal;
            Require(proposal != null, "当前没有待处理的请求。");
            if (command.Kind == "cancel-proposal")
                Require(proposal.Seat == seat, "只有发起者可以撤回请求。");
            else
                Require(proposal.Seat != seat, "请求必须由对方确认。");
            if (command.Kind == "respond" && command.Accept)
            {
                switch (proposal.Kind)
                {
                    case "undo":
                        ReplaceGame(GoGame.FromSgf(TruncateLastMove(game)));
                        break;
                    case "result":
                        state.Result = proposal.Text;
                        WriteGame();
                        break;
                    case "clear":
                        ReplaceGame(FromSetup(game, new String('0', game.Size * game.Size), Stone.Black));
                        break;
                    case "import":
                        originalSgf = pendingImport.ToSgf();
                        pendingImport.Goto(0);
                        ReplaceGame(pendingImport);
                        break;
                    case "study-copy":
                        state.OriginalMatchSgf = state.Sgf;
                        originalSgf = state.Sgf;
                        state.Mode = OnlineRoomMode.Study;
                        state.Result = null;
                        state.OriginalAvailable = true;
                        ReplaceGame(GoGame.FromSgf(originalSgf));
                        break;
                }
            }
            state.Proposal = null;
            pendingImport = null;
        }

        private static string TruncateLastMove(GoGame game)
        {
            string sgf = game.ToSgf();
            int length = game.Moves[game.Moves.Count - 1].IsPass ? 4 : 6;
            return sgf.Remove(sgf.Length - 1 - length, length);
        }

        private static string CheckResult(string text)
        {
            text = (text ?? "").Trim().ToUpperInvariant();
            if (text == "0" || text == "和棋") return "0";
            double points = 0;
            Require(text.Length >= 3 && text.Length <= 8 &&
                (text.StartsWith("B+", StringComparison.Ordinal) || text.StartsWith("W+", StringComparison.Ordinal)) &&
                Double.TryParse(text.Substring(2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out points) &&
                points > 0 && points <= 1500 && Math.Abs(points * 2 - Math.Round(points * 2)) < 0.00001,
                "结果请填写 B+2.5、W+2.5 或 0（和棋）。");
            return text.Substring(0, 2) + points.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static GoGame ParseStudy(string sgf)
        {
            Require(!String.IsNullOrWhiteSpace(sgf) && sgf.Length <= MaxSgfChars, "棋谱为空或超过联机大小限制。");
            CountSgfNodes(sgf);
            GoGame parsed = GoGame.FromSgf(sgf);
            ValidateSize(parsed);
            Require(parsed.Moves.Count <= MaxMoves, "棋谱超过联机手数限制。");
            int end = parsed.Position;
            parsed.Goto(0);
            ValidateSetup(parsed);
            parsed.Goto(end);
            return parsed;
        }

        private static void CountSgfNodes(string text)
        {
            bool value = false;
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (value)
                {
                    if (current == '\\') i++;
                    else if (current == ']') value = false;
                }
                else if (current == '[') value = true;
                else if (current == ';')
                    Require(++count <= MaxMoves + 512, "棋谱超过联机节点数限制。");
            }
        }

        private static void ValidateSize(GoGame game)
        {
            Require(game.Size == 9 || game.Size == 13 || game.Size == 19, "联机支持 9、13 和 19 路棋盘。");
        }

        private static bool IsPlayer(Stone color) { return color == Stone.Black || color == Stone.White; }

        private static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }

        private static string BoardText(GoGame game)
        {
            char[] data = new char[game.Size * game.Size];
            for (int y = 0; y < game.Size; y++)
                for (int x = 0; x < game.Size; x++)
                    data[y * game.Size + x] = (char)('0' + (int)game.Board[x, y]);
            return new String(data);
        }

        internal static GoGame FromSetup(GoGame template, string setup, Stone player)
        {
            Require(setup != null && setup.Length == template.Size * template.Size && IsPlayer(player), "摆棋草稿无效。");
            var text = new StringBuilder("(;GM[1]FF[4]SZ[");
            text.Append(template.Size).Append("]KM[").Append(template.Komi.ToString(CultureInfo.InvariantCulture));
            text.Append("]RU[").Append(template.Rules == "japanese" ? "Japanese" : "Chinese");
            text.Append("]PL[").Append(player == Stone.Black ? 'B' : 'W').Append(']');
            AppendSetup(text, setup, template.Size, '1', "AB");
            AppendSetup(text, setup, template.Size, '2', "AW");
            return GoGame.FromSgf(text.Append(')').ToString());
        }

        private static void AppendSetup(StringBuilder text, string setup, int size, char stone, string property)
        {
            bool started = false;
            for (int i = 0; i < setup.Length; i++)
            {
                if (setup[i] != stone) continue;
                if (!started) { text.Append(property); started = true; }
                text.Append('[').Append((char)('a' + i % size)).Append((char)('a' + i / size)).Append(']');
            }
        }

        internal static void ValidateSetup(GoGame game)
        {
            int size = game.Size;
            Stone[,] board = game.Board;
            bool[] seen = new bool[size * size];
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < seen.Length; i++)
            {
                Stone color = board[i % size, i / size];
                if (seen[i] || color == Stone.Empty) continue;
                seen[i] = true;
                bool liberty = false;
                var queue = new Queue<int>();
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int point = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        int x = point % size + dx[d], y = point / size + dy[d];
                        if (x < 0 || x >= size || y < 0 || y >= size) continue;
                        if (board[x, y] == Stone.Empty) liberty = true;
                        else if (board[x, y] == color && !seen[y * size + x])
                        {
                            seen[y * size + x] = true;
                            queue.Enqueue(y * size + x);
                        }
                    }
                }
                Require(liberty, "摆棋中有无气棋块，请擦除或调整后继续。");
            }
        }

        private static OnlineSharedAnalysis ValidateAnalysis(OnlineSharedAnalysis value)
        {
            Require(value != null && !String.IsNullOrWhiteSpace(value.Model) && value.Model.Length <= 256 &&
                value.Visits > 0 && value.BlackWinrate >= 0 && value.BlackWinrate <= 1 &&
                !Double.IsNaN(value.BlackScoreLead) && !Double.IsInfinity(value.BlackScoreLead) &&
                (value.Region == null || value.Region.Length <= 128), "共享的分析摘要无效。");
            string[] candidates = value.Candidates ?? new string[0];
            string[] coordinates = value.GtpMoves ?? new string[0];
            Require(candidates.Length <= 12 && coordinates.Length <= 12, "共享的候选手过多。");
            foreach (string line in candidates) Require(line != null && line.Length <= 512, "共享的候选摘要过长。");
            foreach (string coord in coordinates) Require(coord != null && coord.Length <= 4, "共享的候选坐标无效。");
            return new OnlineSharedAnalysis
            {
                Model = value.Model, Visits = value.Visits, Region = value.Region,
                BlackWinrate = value.BlackWinrate, BlackScoreLead = value.BlackScoreLead,
                Candidates = (string[])candidates.Clone(), GtpMoves = (string[])coordinates.Clone()
            };
        }
    }
}
