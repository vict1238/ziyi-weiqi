using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GoDesk
{
    public enum Stone { Empty = 0, Black = 1, White = 2 }

    public sealed class Move
    {
        public int X;
        public int Y;
        public Stone Color;
        public bool IsPass { get { return X == -1 && Y == -1; } }

        public Move(int x, int y, Stone color) { X = x; Y = y; Color = color; }

        public string ToGtp(int size)
        {
            if (IsPass) return "pass";
            if (X < 0 || X >= size || Y < 0 || Y >= size || size > 25)
                throw new ArgumentException("棋子坐标超出棋盘。");
            int letter = X >= 8 ? X + 1 : X;
            return ((char)('A' + letter)).ToString() + (size - Y).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// An independent Go model. Coordinates are [x,y], with y=0 at the top.
    /// Chinese rules use positional superko; Japanese rules use simple ko.
    /// Passing is always legal until two consecutive passes.
    /// Moves retains the complete line while Position selects its active prefix.
    /// </summary>
    public sealed class GoGame
    {
        private sealed class State
        {
            public Stone[,] Board;
            public Stone ToPlay;
            public int BlackCaptures;
            public int WhiteCaptures;
            public int Passes;
            public string Key;
        }

        private readonly List<State> states = new List<State>();
        private readonly List<Move> moves = new List<Move>();
        private readonly List<Move> initialStones = new List<Move>();
        private readonly Dictionary<string, List<string>> metadata = new Dictionary<string, List<string>>();
        private int size;
        private int position;
        private Stone initialPlayer;
        private double komi;
        private string rules;

        public int Size { get { return size; } }
        public string ImportNotice { get; private set; }
        public double Komi
        {
            get { return komi; }
            set
            {
                if (!IsValidKomi(value))
                    throw new ArgumentException("贴目必须在 -150 到 150 之间，且为整数或半整数（如 6、6.5）。");
                komi = value;
            }
        }
        public string Rules
        {
            get { return rules; }
            set { rules = NormalizeRules(value); }
        }
        public Stone ToPlay { get { return Current.ToPlay; } }
        public int Position { get { return position; } }
        public List<Move> Moves { get { return moves; } }
        public List<Move> InitialStones { get { return initialStones; } }
        public Stone InitialPlayer { get { return initialPlayer; } }
        public int BlackCaptures { get { return Current.BlackCaptures; } }
        public int WhiteCaptures { get { return Current.WhiteCaptures; } }
        public bool IsOver { get { return Current.Passes >= 2; } }
        public Stone[,] Board { get { return Current.Board; } }
        private State Current { get { return states[position]; } }

        public GoGame(int size = 19, double komi = 7.5) { NewGame(size, komi, 0); }

        public static Stone Opponent(Stone color)
        {
            if (color == Stone.Black) return Stone.White;
            if (color == Stone.White) return Stone.Black;
            throw new ArgumentException("棋子颜色必须为黑棋或白棋。");
        }

        public void NewGame(int boardSize, double newKomi, int handicap)
        {
            NewGame(boardSize, newKomi, handicap, Stone.Black);
        }

        // The recipient receives setup stones; the opponent makes the first
        // actual move. A zero-stone game always starts with Black.
        public void NewGame(int boardSize, double newKomi, int handicap, Stone handicapRecipient)
        {
            if (boardSize < 2 || boardSize > 25)
                throw new ArgumentException("棋盘边长必须在 2 到 25 之间。");
            if (handicap < 0 || handicap > 9 || (handicap > 0 && boardSize < 7))
                throw new ArgumentException("让子数必须在 0 到 9 之间，让子棋盘至少为 7 路。");
            if (handicapRecipient != Stone.Black && handicapRecipient != Stone.White)
                throw new ArgumentException("获得让子的一方必须为黑棋或白棋。", "handicapRecipient");
            // Validate before resetting a game, so a failed request preserves it.
            if (!IsValidKomi(newKomi))
                throw new ArgumentException("贴目必须在 -150 到 150 之间，且为整数或半整数（如 6、6.5）。");
            size = boardSize;
            Komi = newKomi;
            Rules = "chinese-ogs";
            moves.Clear();
            initialStones.Clear();
            metadata.Clear();
            ImportNotice = null;
            initialPlayer = handicap > 0 ? Opponent(handicapRecipient) : Stone.Black;
            if (handicap == 1)
            {
                AddInitial(size / 2, size / 2, handicapRecipient);
            }
            else if (handicap >= 2)
            {
                int low = size < 13 ? 2 : 3;
                int high = size - 1 - low;
                int middle = size / 2;
                AddInitial(high, low, handicapRecipient);
                AddInitial(low, high, handicapRecipient);
                if (handicap >= 3) AddInitial(high, high, handicapRecipient);
                if (handicap >= 4) AddInitial(low, low, handicapRecipient);
                if (handicap == 5 || handicap == 7 || handicap == 9)
                    AddInitial(middle, middle, handicapRecipient);
                if (handicap >= 6)
                {
                    AddInitial(low, middle, handicapRecipient);
                    AddInitial(high, middle, handicapRecipient);
                }
                if (handicap >= 8)
                {
                    AddInitial(middle, low, handicapRecipient);
                    AddInitial(middle, high, handicapRecipient);
                }
            }
            ResetStates();
        }

        private void AddInitial(int x, int y, Stone color)
        {
            initialStones.Add(new Move(x, y, color));
        }

        private void ResetStates()
        {
            Stone[,] board = new Stone[size, size];
            foreach (Move m in initialStones) board[m.X, m.Y] = m.Color;
            states.Clear();
            states.Add(new State { Board = board, ToPlay = initialPlayer, Key = BoardKey(board) });
            position = 0;
        }

        public bool TryPlay(int x, int y, out string error)
        {
            return TryPlayMove(new Move(x, y, ToPlay), out error);
        }

        public bool TryPlayMove(Move move, out string error)
        {
            error = null;
            if (move == null) { error = "落子不能为空。"; return false; }
            if (IsOver) { error = "双方已连续停一手，对局结束。可以悔棋后继续。"; return false; }
            if (move.Color != ToPlay)
            {
                error = ToPlay == Stone.Black ? "现在轮到黑棋落子。" : "现在轮到白棋落子。";
                return false;
            }
            State old = Current;
            State next = new State
            {
                Board = (Stone[,])old.Board.Clone(),
                ToPlay = Opponent(ToPlay),
                BlackCaptures = old.BlackCaptures,
                WhiteCaptures = old.WhiteCaptures,
                Passes = move.IsPass ? old.Passes + 1 : 0
            };
            if (!move.IsPass)
            {
                int x = move.X, y = move.Y;
                if (x < 0 || y < 0 || x >= size || y >= size)
                { error = "落子位置超出棋盘。"; return false; }
                if (next.Board[x, y] != Stone.Empty)
                { error = "这里已经有棋子了。"; return false; }
                next.Board[x, y] = move.Color;
                int captured = 0;
                foreach (int point in Neighbours(x, y))
                {
                    int nx = point % size, ny = point / size;
                    if (next.Board[nx, ny] != Opponent(move.Color)) continue;
                    bool hasLiberty;
                    List<int> group = Group(next.Board, nx, ny, out hasLiberty);
                    if (hasLiberty) continue;
                    foreach (int stone in group) next.Board[stone % size, stone / size] = Stone.Empty;
                    captured += group.Count;
                }
                bool ownLiberty;
                Group(next.Board, x, y, out ownLiberty);
                if (!ownLiberty) { error = "此处为禁入点，落子后己方棋子没有气。"; return false; }
                if (move.Color == Stone.Black) next.BlackCaptures += captured;
                else next.WhiteCaptures += captured;
                next.Key = BoardKey(next.Board);
                if (rules == "japanese")
                {
                    if (position > 0 && states[position - 1].Key == next.Key)
                    { error = "此手为立即回提，请先在别处落子（日本规则：单劫禁着）。"; return false; }
                }
                else
                {
                    for (int i = 0; i <= position; i++)
                    {
                        if (states[i].Key == next.Key)
                        { error = "此手会使棋盘局面重复，请先在别处落子（中国规则：全局同形禁着）。"; return false; }
                    }
                }
            }
            else next.Key = old.Key;

            if (position < moves.Count) moves.RemoveRange(position, moves.Count - position);
            if (states.Count > position + 1) states.RemoveRange(position + 1, states.Count - position - 1);
            moves.Add(new Move(move.X, move.Y, move.Color));
            states.Add(next);
            position++;
            metadata.Remove("RE");
            return true;
        }

        private IEnumerable<int> Neighbours(int x, int y)
        {
            if (x > 0) yield return y * size + x - 1;
            if (x + 1 < size) yield return y * size + x + 1;
            if (y > 0) yield return (y - 1) * size + x;
            if (y + 1 < size) yield return (y + 1) * size + x;
        }

        private List<int> Group(Stone[,] board, int x, int y, out bool hasLiberty)
        {
            Stone color = board[x, y];
            bool[] seen = new bool[size * size];
            Stack<int> pending = new Stack<int>();
            List<int> group = new List<int>();
            pending.Push(y * size + x);
            seen[y * size + x] = true;
            hasLiberty = false;
            while (pending.Count > 0)
            {
                int point = pending.Pop();
                group.Add(point);
                foreach (int adjacent in Neighbours(point % size, point / size))
                {
                    Stone next = board[adjacent % size, adjacent / size];
                    if (next == Stone.Empty) hasLiberty = true;
                    else if (next == color && !seen[adjacent])
                    {
                        seen[adjacent] = true;
                        pending.Push(adjacent);
                    }
                }
            }
            return group;
        }

        private string BoardKey(Stone[,] board)
        {
            char[] key = new char[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) key[y * size + x] = (char)('0' + (int)board[x, y]);
            return new String(key);
        }

        public void Pass()
        {
            string error;
            if (!TryPlayMove(new Move(-1, -1, ToPlay), out error)) throw new InvalidOperationException(error);
        }

        public void Undo() { if (position > 0) Goto(position - 1); }

        public void Goto(int newPosition)
        {
            if (newPosition < 0 || newPosition > moves.Count)
                throw new ArgumentOutOfRangeException("newPosition", "手数超出棋谱范围。");
            position = newPosition;
        }

        public static Move ParseGtp(string coord, Stone color, int size)
        {
            if (color != Stone.Black && color != Stone.White)
                throw new ArgumentException("棋子颜色必须为黑棋或白棋。");
            if (size < 2 || size > 25) throw new ArgumentException("棋盘边长必须在 2 到 25 之间。");
            if (coord == null) throw new FormatException("AI 未返回落点。");
            string text = coord.Trim().ToUpperInvariant();
            if (text == "PASS") return new Move(-1, -1, color);
            if (text == "RESIGN") throw new FormatException("AI 已认输。");
            if (text.Length < 2 || text[0] < 'A' || text[0] > 'Z' || text[0] == 'I')
                throw new FormatException("无法识别围棋坐标：" + coord);
            int row;
            if (!Int32.TryParse(text.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out row))
                throw new FormatException("无法识别围棋坐标：" + coord);
            int x = text[0] - 'A';
            if (text[0] > 'I') x--;
            int y = size - row;
            if (x < 0 || y < 0 || x >= size || y >= size)
                throw new FormatException("围棋坐标超出棋盘：" + coord);
            return new Move(x, y, color);
        }

        public string ToSgf()
        {
            StringBuilder b = new StringBuilder();
            b.Append("(;GM[1]FF[4]CA[UTF-8]AP[梓懿围棋:1.6]RU[")
                .Append(rules == "japanese" ? "Japanese" : "Chinese").Append("]SZ[").Append(size)
                .Append("]KM[").Append(komi.ToString("0.################", CultureInfo.InvariantCulture)).Append(']');
            int blacks = 0, whites = 0;
            foreach (Move m in initialStones) { if (m.Color == Stone.Black) blacks++; else if (m.Color == Stone.White) whites++; }
            if (blacks >= 2 && whites == 0) b.Append("HA[").Append(blacks).Append(']');
            AppendSetup(b, "AB", Stone.Black);
            AppendSetup(b, "AW", Stone.White);
            b.Append("PL[").Append(initialPlayer == Stone.Black ? 'B' : 'W').Append(']');
            foreach (KeyValuePair<string, List<string>> property in metadata)
            {
                b.Append(property.Key);
                foreach (string value in property.Value) b.Append('[').Append(EscapeSgf(value)).Append(']');
            }
            // Export the retained game, including later moves during history review.
            foreach (Move move in moves)
            {
                b.Append(';').Append(move.Color == Stone.Black ? 'B' : 'W').Append('[');
                if (!move.IsPass) b.Append(SgfPoint(move.X, move.Y));
                b.Append(']');
            }
            b.Append(')');
            return b.ToString();
        }

        private void AppendSetup(StringBuilder b, string property, Stone color)
        {
            bool started = false;
            foreach (Move m in initialStones)
            {
                if (m.Color != color) continue;
                if (!started) { b.Append(property); started = true; }
                b.Append('[').Append(SgfPoint(m.X, m.Y)).Append(']');
            }
        }

        private static string SgfPoint(int x, int y) { return new String(new char[] { (char)('a' + x), (char)('a' + y) }); }
        private static string EscapeSgf(string value) { return value.Replace("\\", "\\\\").Replace("]", "\\]"); }

        public static GoGame FromSgf(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new FormatException("SGF 棋谱为空。");
            SgfParser parser = new SgfParser(text);
            List<SgfNode> nodes = parser.Parse();
            SgfNode root = nodes[0];
            string gm = First(root, "GM");
            if (gm != null && gm.Trim() != "1") throw new FormatException("此 SGF 不是围棋棋谱（GM 必须为 1）。");
            int size = 19;
            string sizeText = First(root, "SZ");
            if (sizeText != null && (!Int32.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 2 || size > 25))
                throw new FormatException("只支持边长为 2 到 25 的正方形棋盘。");
            double komi = 7.5;
            string komiText = First(root, "KM");
            if (komiText != null && !Double.TryParse(komiText, NumberStyles.Float, CultureInfo.InvariantCulture, out komi))
                throw new FormatException("SGF 中的贴目 KM 不是有效数字。");
            double exportedKomi = komi;
            // Match KataGo's Fox SGF compatibility mapping before checking native
            // analysis bounds. Fox exports can repeat AP after another application.
            if (IsFoxExport(root)) komi = NormalizeFoxKomi(komi);
            if (!IsValidKomi(komi))
                throw new FormatException("SGF 中的贴目 KM 必须在 -150 到 150 之间，且为整数或半整数（如 6、6.5）。");
            GoGame game = new GoGame(size, komi);
            if (komi != exportedKomi)
                game.ImportNotice = "已识别野狐棋谱：贴目从 KM[" + komiText + "] 转为 " +
                    komi.ToString("0.#", CultureInfo.InvariantCulture) + " 目。";
            string rulesText = First(root, "RU");
            if (rulesText != null)
            {
                try { game.Rules = rulesText; }
                catch (ArgumentException ex) { throw new FormatException(ex.Message, ex); }
            }
            Stone[,] setup = new Stone[size, size];
            ReadSetup(root, "AB", Stone.Black, setup, size);
            ReadSetup(root, "AW", Stone.White, setup, size);
            List<string> empty;
            if (root.Properties.TryGetValue("AE", out empty))
                foreach (string value in empty)
                    foreach (int point in SetupPoints(value, size)) setup[point % size, point / size] = Stone.Empty;
            int blackCount = 0, whiteCount = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (setup[x, y] == Stone.Empty) continue;
                    game.AddInitial(x, y, setup[x, y]);
                    if (setup[x, y] == Stone.Black) blackCount++; else whiteCount++;
                }
            int handicap;
            bool hasHandicap = Int32.TryParse(First(root, "HA"), NumberStyles.Integer, CultureInfo.InvariantCulture, out handicap) && handicap >= 2;
            game.initialPlayer = (hasHandicap || (blackCount >= 2 && whiteCount == 0)) ? Stone.White : Stone.Black;
            string player = First(root, "PL");
            if (player != null)
            {
                if (player == "B") game.initialPlayer = Stone.Black;
                else if (player == "W") game.initialPlayer = Stone.White;
                else throw new FormatException("SGF 中 PL 必须为 B 或 W。");
            }
            else
            {
                // Problem diagrams can start with White without an explicit PL.
                foreach (SgfNode node in nodes)
                {
                    if (node.Properties.ContainsKey("B")) { game.initialPlayer = Stone.Black; break; }
                    if (node.Properties.ContainsKey("W")) { game.initialPlayer = Stone.White; break; }
                }
            }
            game.ResetStates();
            for (int i = 0; i < nodes.Count; i++)
            {
                SgfNode node = nodes[i];
                if (i > 0 && (node.Properties.ContainsKey("AB") || node.Properties.ContainsKey("AW") || node.Properties.ContainsKey("AE") || node.Properties.ContainsKey("PL")))
                    throw new FormatException("暂不支持棋谱中途摆子或切换行棋方（第 " + i + " 个节点）。请使用仅在起始局面摆子的 SGF。");
                if (i > 0 && (node.Properties.ContainsKey("SZ") || node.Properties.ContainsKey("KM") || node.Properties.ContainsKey("RU")))
                    throw new FormatException("暂不支持棋谱中途改变棋盘大小、贴目或规则。");
                bool hasBlack = node.Properties.ContainsKey("B"), hasWhite = node.Properties.ContainsKey("W");
                if (!hasBlack && !hasWhite) continue;
                if (hasBlack && hasWhite) throw new FormatException("SGF 的同一节点不能同时包含黑棋和白棋落子。");
                List<string> values = node.Properties[hasBlack ? "B" : "W"];
                if (values.Count != 1) throw new FormatException("SGF 的一个落子节点只能有一个坐标。");
                Stone color = hasBlack ? Stone.Black : Stone.White;
                Move move = SgfMove(values[0], color, size);
                string error;
                if (!game.TryPlayMove(move, out error))
                    throw new FormatException("SGF 第 " + (game.Moves.Count + 1) + " 手无效：" + error);
            }
            string[] preserve = new string[] { "PB", "PW", "BR", "WR", "BT", "WT", "DT", "EV", "RO", "PC", "GN", "GC", "SO", "US", "AN", "CP", "RE", "C" };
            foreach (string property in preserve)
            {
                List<string> values;
                if (root.Properties.TryGetValue(property, out values)) game.metadata[property] = new List<string>(values);
            }
            return game;
        }

        private static string First(SgfNode node, string property)
        {
            List<string> values;
            return node.Properties.TryGetValue(property, out values) && values.Count > 0 ? values[0] : null;
        }

        private static bool IsFoxExport(SgfNode root)
        {
            List<string> applications;
            if (!root.Properties.TryGetValue("AP", out applications)) return false;
            foreach (string application in applications)
                if (String.Equals(application.Split(':')[0].Trim(), "foxwq", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static double NormalizeFoxKomi(double value)
        {
            // Upstream cpp/dataio/sgf.cpp, KataGo v1.14.1, getKomiOrDefault.
            if (value == 550) return 5.5;
            if (value == 325 || value == 650) return 6.5;
            if (value == 375 || value == 750) return 7.5;
            if (value == 350 || value == 700) return 7;
            return value;
        }

        private static string NormalizeRules(string value)
        {
            string normalized = value == null ? "" : value.Trim().ToLowerInvariant();
            if (normalized == "chinese" || normalized == "chinese-ogs" || normalized == "chinese ogs" ||
                normalized == "chinese-kgs" || normalized == "chinese kgs") return "chinese-ogs";
            if (normalized == "japanese") return "japanese";
            throw new ArgumentException("暂不支持此围棋规则：" + (String.IsNullOrWhiteSpace(value) ? "（空值）" : value) +
                "。当前支持 Chinese（中国规则）和 Japanese（日本规则）。");
        }

        private static bool IsValidKomi(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value) && Math.Abs(value) <= 150 &&
                value * 2 == Math.Round(value * 2);
        }

        private static void ReadSetup(SgfNode node, string property, Stone color, Stone[,] board, int size)
        {
            List<string> values;
            if (!node.Properties.TryGetValue(property, out values)) return;
            foreach (string value in values)
                foreach (int point in SetupPoints(value, size))
                {
                    int x = point % size, y = point / size;
                    if (board[x, y] != Stone.Empty && board[x, y] != color)
                        throw new FormatException("SGF 起始局面的黑白棋子坐标冲突。");
                    board[x, y] = color;
                }
        }

        private static IEnumerable<int> SetupPoints(string value, int size)
        {
            string[] ends = value.Split(':');
            if (ends.Length > 2) throw new FormatException("SGF 摆子范围无效：" + value);
            Move first = SgfMove(ends[0], Stone.Black, size);
            Move last = ends.Length == 2 ? SgfMove(ends[1], Stone.Black, size) : first;
            if (first.IsPass || last.IsPass || last.X < first.X || last.Y < first.Y)
                throw new FormatException("SGF 摆子坐标或范围无效：" + value);
            for (int y = first.Y; y <= last.Y; y++)
                for (int x = first.X; x <= last.X; x++) yield return y * size + x;
        }

        private static Move SgfMove(string value, Stone color, int size)
        {
            if (value.Length == 0 || (value == "tt" && size <= 19)) return new Move(-1, -1, color);
            if (value.Length != 2) throw new FormatException("SGF 落子坐标无效：" + value);
            int x = value[0] - 'a', y = value[1] - 'a';
            if (x < 0 || x >= size || y < 0 || y >= size)
                throw new FormatException("SGF 落子坐标超出棋盘：" + value);
            return new Move(x, y, color);
        }

        private sealed class SgfNode
        {
            public readonly Dictionary<string, List<string>> Properties = new Dictionary<string, List<string>>();
        }

        private sealed class SgfParser
        {
            private readonly string source;
            private int index;
            private int count;
            public SgfParser(string source) { this.source = source; }

            public List<SgfNode> Parse()
            {
                SkipSpace();
                List<SgfNode> mainLine = ParseTree(0);
                SkipSpace();
                if (index < source.Length) throw Error("一个文件中只支持一盘棋，请拆分 SGF 棋谱集合。");
                return mainLine;
            }

            private List<SgfNode> ParseTree(int depth)
            {
                if (depth > 256) throw Error("棋谱变化分支嵌套过深。");
                Expect('(');
                SkipSpace();
                List<SgfNode> sequence = new List<SgfNode>();
                while (index < source.Length && source[index] == ';')
                {
                    sequence.Add(ParseNode());
                    if (++count > 100000) throw Error("棋谱节点过多。");
                    SkipSpace();
                }
                if (sequence.Count == 0) throw Error("棋谱缺少节点。");
                bool first = true;
                while (index < source.Length && source[index] == '(')
                {
                    List<SgfNode> variation = ParseTree(depth + 1);
                    if (first) { sequence.AddRange(variation); first = false; }
                    SkipSpace();
                }
                Expect(')');
                return sequence;
            }

            private SgfNode ParseNode()
            {
                Expect(';');
                SgfNode node = new SgfNode();
                SkipSpace();
                while (index < source.Length && Char.IsLetter(source[index]))
                {
                    int start = index;
                    while (index < source.Length && Char.IsLetter(source[index])) index++;
                    string property = source.Substring(start, index - start).ToUpperInvariant();
                    SkipSpace();
                    if (index >= source.Length || source[index] != '[') throw Error("属性 " + property + " 缺少方括号。");
                    List<string> values;
                    if (!node.Properties.TryGetValue(property, out values))
                    {
                        values = new List<string>();
                        node.Properties.Add(property, values);
                    }
                    while (index < source.Length && source[index] == '[')
                    {
                        values.Add(ParseValue());
                        SkipSpace();
                    }
                }
                return node;
            }

            private string ParseValue()
            {
                Expect('[');
                StringBuilder b = new StringBuilder();
                while (index < source.Length)
                {
                    char c = source[index++];
                    if (c == ']') return b.ToString();
                    if (c == '\\')
                    {
                        if (index >= source.Length) throw Error("属性值中的转义不完整。");
                        c = source[index++];
                        if (c == '\r') { if (index < source.Length && source[index] == '\n') index++; continue; }
                        if (c == '\n') { if (index < source.Length && source[index] == '\r') index++; continue; }
                        b.Append(c);
                    }
                    else if (c == '\r')
                    {
                        if (index < source.Length && source[index] == '\n') index++;
                        b.Append('\n');
                    }
                    else b.Append(c);
                }
                throw Error("属性值缺少结束方括号。");
            }

            private void SkipSpace()
            {
                while (index < source.Length && (Char.IsWhiteSpace(source[index]) || source[index] == '\uFEFF')) index++;
            }

            private void Expect(char expected)
            {
                if (index >= source.Length || source[index] != expected) throw Error("应为“" + expected + "”。");
                index++;
            }

            private FormatException Error(string message)
            {
                return new FormatException("SGF 格式错误（第 " + (index + 1) + " 个字符）：" + message);
            }
        }
    }
}
