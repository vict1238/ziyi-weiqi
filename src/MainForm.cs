using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    public class MainForm : Form
    {
        internal const string Tribute = "致敬六大至尊围棋王“梓”和“懿”";
        private readonly Color ink = Color.FromArgb(39, 48, 51), muted = Color.FromArgb(111, 119, 119), green = Color.FromArgb(41, 113, 99);
        private readonly ToolTip tooltips = new ToolTip();
        private GoGame game;
        private KataGoEngine engine;
        private AnalysisResult analysis;
        private BoardControl board;
        private ComboBox humanBox, sizeBox, difficultyBox;
        private CheckBox automaticBox;
        private Button aiButton, hintButton, scoreButton, passButton, undoButton, cancelButton, selfPlayButton;
        private Button firstButton, prevButton, nextButton, lastButton;
        private Label turnLabel, moveLabel, captureLabel, engineLabel, evaluationLabel, detailLabel, gameInfoLabel;
        private ToolStripStatusLabel statusLabel;
        private Panel evaluationBar;
        private bool thinking, closing, updating;
        private int generation, handicap;
        private int selfPlaySession;
        private bool selfPlaying;
        private bool stopping, analysisWillPlay, continuousDepth;
        private long customVisits = 1000;
        private int selectedDepthIndex = 1;
        private double komi = 7.5;
        private string rules = "chinese-ogs";
        public GoGame CurrentGame { get { return game; } }

        public MainForm()
        {
            Text = "梓懿围棋 · KataGo";
            try { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            MinimumSize = new Size(1000, 740);
            Size = new Size(1180, 850);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(242, 240, 234);
            ForeColor = ink;
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;
            game = new GoGame(19, komi);
            BuildUi();
            engine = new KataGoEngine();
            engine.StatusChanged += OnEngineStatus;
            UpdateUi();
            FormClosing += delegate { closing = true; generation++; engine.Cancel(); engine.Dispose(); };
            KeyDown += OnShortcut;
        }

        private void BuildUi()
        {
            MenuStrip menu = new MenuStrip { BackColor = Color.FromArgb(250, 249, 246), ForeColor = ink, Padding = new Padding(14, 3, 0, 3) };
            ToolStripMenuItem file = new ToolStripMenuItem("棋谱(&F)");
            file.DropDownItems.Add("新对局", null, delegate { NewGame(); });
            file.DropDownItems.Add("打开 SGF…", null, delegate { OpenSgf(); });
            file.DropDownItems.Add("保存 SGF…", null, delegate { SaveSgf(); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("退出", null, delegate { Close(); });
            ToolStripMenuItem help = new ToolStripMenuItem("帮助(&H)");
            help.DropDownItems.Add("使用说明", null, delegate { ShowHelp(); });
            help.DropDownItems.Add("第三方许可", null, delegate { Program.ShowLicenses(this); });
            help.DropDownItems.Add("关于梓懿围棋", null, delegate { MessageBox.Show(this, "梓懿围棋 1.1\r\n\r\n" + Tribute + "\r\n\r\n原生 Windows 围棋对弈与复盘工具。\r\nAI 使用本机 KataGo 引擎，支持 SGF 棋谱。\r\n\r\nKataGo：github.com/lightvector/KataGo", "关于梓懿围棋", MessageBoxButtons.OK, MessageBoxIcon.Information); });
            menu.Items.Add(file); menu.Items.Add(help); MainMenuStrip = menu;

            Panel header = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(26, 9, 24, 8), BackColor = Color.FromArgb(250, 249, 246) };
            Label title = new Label { Text = "梓懿围棋", AutoSize = true, Location = new Point(24, 12), Font = new Font("Microsoft YaHei UI", 20, FontStyle.Bold), ForeColor = ink };
            Label subtitle = new Label { Text = "KataGo  /  本地对弈与复盘", AutoSize = true, Location = new Point(197, 28), ForeColor = muted, Font = new Font("Microsoft YaHei UI", 10) };
            Label local = new Label { Dock = DockStyle.Right, Width = 178, Text = "●  本地引擎 · 独立运行", TextAlign = ContentAlignment.MiddleRight, ForeColor = green };
            header.Controls.Add(title); header.Controls.Add(subtitle); header.Controls.Add(local);

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(18, 10, 20, 9) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 306));
            Panel left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
            Panel boardHeader = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(13, 0, 12, 0) };
            turnLabel = new Label { Dock = DockStyle.Left, Width = 210, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            gameInfoLabel = new Label { Dock = DockStyle.Right, Width = 220, TextAlign = ContentAlignment.MiddleRight, ForeColor = muted };
            boardHeader.Controls.Add(turnLabel); boardHeader.Controls.Add(gameInfoLabel);
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(12, 5, 12, 0) };
            FlowLayoutPanel navigation = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 37, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            firstButton = NavButton("|◀", "首手", delegate { Navigate(0); });
            prevButton = NavButton("◀", "上一手", delegate { Navigate(game.Position - 1); });
            nextButton = NavButton("▶", "下一手", delegate { Navigate(game.Position + 1); });
            lastButton = NavButton("▶|", "末手", delegate { Navigate(game.Moves.Count); });
            moveLabel = new Label { Width = 150, Height = 32, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ink };
            navigation.Controls.Add(firstButton); navigation.Controls.Add(prevButton); navigation.Controls.Add(moveLabel); navigation.Controls.Add(nextButton); navigation.Controls.Add(lastButton);
            captureLabel = new Label { Dock = DockStyle.Bottom, Height = 25, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted };
            footer.Controls.Add(navigation); footer.Controls.Add(captureLabel);
            board = new BoardControl { Dock = DockStyle.Fill, Name = "GoBoard", Game = game };
            board.PointClicked += OnBoardPoint;
            left.Controls.Add(board); left.Controls.Add(boardHeader); left.Controls.Add(footer);

            Panel sideHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 249, 246), Padding = new Padding(15, 14, 15, 10), AutoScroll = true };
            FlowLayoutPanel side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = 269, Location = new Point(15, 14) };
            side.Controls.Add(SectionTitle("对局设置"));
            humanBox = Combo(new string[] { "执黑 · Black", "执白 · White", "双方 · 自由复盘" }); humanBox.Name = "HumanColor";
            humanBox.SelectedIndexChanged += delegate { if (!updating) { CancelAnalysis(); UpdateUi(); MaybeAutoMove(); } };
            side.Controls.Add(Field("你的执子", humanBox));
            sizeBox = Combo(new string[] { "19 路", "13 路", "9 路" }); sizeBox.Name = "BoardSize";
            Panel boardRow = new Panel { Width = 264, Height = 38, Margin = new Padding(0, 0, 0, 3) };
            Label sizeLabel = new Label { Text = "棋盘", Width = 63, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted };
            sizeBox.Location = new Point(65, 1); sizeBox.Width = 89;
            Button settings = MakeButton("规则 / 设置", 104, delegate { GameSettings(); }); settings.Location = new Point(160, 0); settings.Height = 30;
            boardRow.Controls.Add(sizeLabel); boardRow.Controls.Add(sizeBox); boardRow.Controls.Add(settings); side.Controls.Add(boardRow);
            Button newGameButton = MakeButton("新对局", 264, delegate { NewGame(); }); newGameButton.Name = "NewGame"; side.Controls.Add(newGameButton);
            side.Controls.Add(Divider());
            side.Controls.Add(SectionTitle("KataGo"));
            engineLabel = new Label { Width = 264, Height = 43, Text = "○  引擎待命\r\n首次分析时加载模型", ForeColor = muted, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 5) };
            side.Controls.Add(engineLabel);
            difficultyBox = Combo(new string[] { "快速 · 16 次搜索", "标准 · 64 次搜索", "深入 · 256 次搜索", "自定义…" }); difficultyBox.SelectedIndex = 1; difficultyBox.Name = "SearchLevel";
            difficultyBox.DropDownWidth = 340;
            difficultyBox.SelectedIndexChanged += delegate { if (!updating && difficultyBox.SelectedIndex != 3) selectedDepthIndex = difficultyBox.SelectedIndex; };
            difficultyBox.SelectionChangeCommitted += OnDepthSelected;
            tooltips.SetToolTip(difficultyBox, "选择自定义，可输入搜索次数或持续思考，手动停止。");
            side.Controls.Add(Field("计算深度", difficultyBox));
            automaticBox = new CheckBox { Text = "AI 自动应手", Checked = true, Width = 264, Height = 27, Margin = new Padding(0, 0, 0, 4), ForeColor = ink, Name = "AutomaticReply" };
            automaticBox.CheckedChanged += delegate { if (!updating) { if (automaticBox.Checked) MaybeAutoMove(); else if (thinking) CancelAnalysis(); } };
            side.Controls.Add(automaticBox);
            Panel aiRow = new Panel { Width = 264, Height = 39, Margin = new Padding(0) };
            aiButton = MakeButton("AI 落子", 169, async delegate { await AnalyzeAsync(true, false); }); aiButton.Name = "AiPlay"; aiButton.BackColor = green; aiButton.ForeColor = Color.White; aiButton.FlatAppearance.BorderColor = green;
            selfPlayButton = MakeButton("AI 自弈", 89, async delegate { await ToggleSelfPlayAsync(); }); selfPlayButton.Name = "SelfPlay"; selfPlayButton.Location = new Point(175, 0);
            aiRow.Controls.Add(aiButton); aiRow.Controls.Add(selfPlayButton); side.Controls.Add(aiRow);
            Panel analysisRow = new Panel { Width = 264, Height = 39, Margin = new Padding(0) };
            hintButton = MakeButton("推荐着点", 129, async delegate { if (analysis != null && board.ShowHints) { board.ShowHints = false; hintButton.Text = "推荐着点"; } else await AnalyzeAsync(false, false); }); hintButton.Name = "BestHints"; hintButton.Location = new Point(0, 0);
            scoreButton = MakeButton("形势 / 领地", 129, async delegate { if (analysis != null && board.ShowOwnership) { board.ShowOwnership = false; scoreButton.Text = "形势 / 领地"; } else await AnalyzeAsync(false, true); }); scoreButton.Name = "Score"; scoreButton.Location = new Point(135, 0);
            analysisRow.Controls.Add(hintButton); analysisRow.Controls.Add(scoreButton); side.Controls.Add(analysisRow);
            cancelButton = MakeButton("结束思考", 264, delegate { StopThinking(); }); cancelButton.Name = "StopThinking"; cancelButton.Visible = false; side.Controls.Add(cancelButton);
            evaluationLabel = new Label { Width = 264, Height = 27, Text = "形势等待分析", ForeColor = ink, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 0, 0) };
            evaluationBar = new Panel { Width = 264, Height = 8, Margin = new Padding(0, 0, 0, 5), BackColor = Color.FromArgb(226, 226, 221) };
            evaluationBar.Paint += PaintEvaluation;
            detailLabel = new Label { Width = 264, Height = 41, Text = "点击推荐着点，查看候选与胜率。", ForeColor = muted, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 5) };
            side.Controls.Add(evaluationLabel); side.Controls.Add(evaluationBar); side.Controls.Add(detailLabel);
            side.Controls.Add(Divider());
            Panel playRow = new Panel { Width = 264, Height = 39, Margin = new Padding(0) };
            passButton = MakeButton("停一手", 129, delegate { PassMove(); }); passButton.Name = "Pass";
            undoButton = MakeButton("悔棋", 129, delegate { UndoMove(); }); undoButton.Name = "Undo"; undoButton.Location = new Point(135, 0);
            playRow.Controls.Add(passButton); playRow.Controls.Add(undoButton); side.Controls.Add(playRow);
            Panel sgfRow = new Panel { Width = 264, Height = 39, Margin = new Padding(0) };
            Button open = MakeButton("打开棋谱", 129, delegate { OpenSgf(); }); open.Name = "OpenSgf";
            Button save = MakeButton("保存棋谱", 129, delegate { SaveSgf(); }); save.Name = "SaveSgf"; save.Location = new Point(135, 0);
            sgfRow.Controls.Add(open); sgfRow.Controls.Add(save); side.Controls.Add(sgfRow);
            Label note = new Label { Width = 264, Height = 38, Text = "橙环标记上一手  ·  A–E 为推荐着点\r\n方向键复盘  ·  Ctrl+O / Ctrl+S 棋谱", ForeColor = muted, Font = new Font("Microsoft YaHei UI", 8), Margin = new Padding(0, 6, 0, 0) }; side.Controls.Add(note);
            sideHost.Controls.Add(side);
            body.Controls.Add(left, 0, 0); body.Controls.Add(sideHost, 1, 0);

            StatusStrip status = new StatusStrip { BackColor = Color.FromArgb(250, 249, 246), SizingGrip = true, Padding = new Padding(19, 0, 15, 0) };
            statusLabel = new ToolStripStatusLabel("在棋盘上点击落子。首次 AI 思考可能需要数秒加载。") { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted };
            status.Items.Add(statusLabel);
            Controls.Add(body); Controls.Add(header); Controls.Add(menu); Controls.Add(status);
        }

        private Label SectionTitle(string text) { return new Label { Text = text, Width = 264, Height = 29, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold), Margin = new Padding(0), ForeColor = ink }; }
        private Panel Divider() { return new Panel { Width = 264, Height = 1, BackColor = Color.FromArgb(225, 225, 218), Margin = new Padding(0, 8, 0, 11) }; }
        private ComboBox Combo(string[] items)
        {
            ComboBox b = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = 197, Height = 29, BackColor = Color.White, ForeColor = ink };
            b.Items.AddRange(items); b.SelectedIndex = 0; return b;
        }
        private Panel Field(string title, Control control)
        {
            Panel p = new Panel { Width = 264, Height = 36, Margin = new Padding(0, 0, 0, 2) };
            p.Controls.Add(new Label { Text = title, Width = 63, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted });
            control.Location = new Point(65, 1); p.Controls.Add(control); return p;
        }
        private Button MakeButton(string text, int width, EventHandler action)
        {
            Button b = new Button { Text = text, Width = width, Height = 33, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = ink, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 6), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = Color.FromArgb(211, 215, 210); b.FlatAppearance.MouseOverBackColor = Color.FromArgb(233, 240, 236); b.Click += action; return b;
        }
        private Button NavButton(string text, string tooltip, EventHandler action)
        {
            Button b = MakeButton(text, 47, action); b.Height = 30; b.Margin = new Padding(0, 0, 5, 0); b.AccessibleName = tooltip;
            tooltips.SetToolTip(b, tooltip); return b;
        }

        private Stone HumanColor { get { return humanBox.SelectedIndex == 1 ? Stone.White : Stone.Black; } }
        private bool HumanTurn { get { return humanBox.SelectedIndex == 2 || game.ToPlay == HumanColor; } }
        private long Visits { get { return selectedDepthIndex == 3 ? (continuousDepth ? 0 : customVisits) : selectedDepthIndex == 0 ? 16 : selectedDepthIndex == 2 ? 256 : 64; } }

        private void OnDepthSelected(object sender, EventArgs e)
        {
            if (updating) return;
            if (difficultyBox.SelectedIndex != 3) { selectedDepthIndex = difficultyBox.SelectedIndex; return; }
            if (!ShowDepthSettings(this))
            {
                updating = true; difficultyBox.SelectedIndex = selectedDepthIndex; updating = false;
            }
        }

        private bool ShowDepthSettings(IWin32Window owner)
        {
            using (Form dialog = new Form { Name = "SearchDepthSettings", Text = "计算深度设置", ClientSize = new Size(442, 312), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font, BackColor = BackColor })
            {
                Label modeLabel = new Label { Text = "计算深度", Location = new Point(22, 21), Size = new Size(90, 27) };
                ComboBox mode = Combo(new string[] { "快速 · 16 次搜索", "标准 · 64 次搜索", "深入 · 256 次搜索", "自定义" }); mode.Name = "DepthMode"; mode.Location = new Point(118, 18); mode.Width = 300; mode.SelectedIndex = difficultyBox.SelectedIndex;
                Label countLabel = new Label { Text = "每步搜索次数", Location = new Point(22, 67), Size = new Size(104, 25) };
                TextBox count = new TextBox { Name = "CustomVisits", Text = customVisits.ToString(CultureInfo.InvariantCulture), Location = new Point(22, 96), Width = 396, MaxLength = 0 };
                CheckBox continuous = new CheckBox { Name = "ContinuousDepth", Text = "持续思考，直到手动停止（不限次数）", Checked = continuousDepth, Location = new Point(22, 136), Size = new Size(396, 28) };
                Label note = new Label { Text = "搜索过程会显示进度；结束思考时采用当前最佳结果。", Location = new Point(22, 176), Size = new Size(396, 37), ForeColor = muted };
                Label errorLabel = new Label { Name = "DepthError", Location = new Point(22, 212), Size = new Size(396, 49), ForeColor = Color.FromArgb(170, 68, 48) };
                Action refresh = delegate { bool custom = mode.SelectedIndex == 3; countLabel.Enabled = custom; continuous.Enabled = custom; count.Enabled = custom && !continuous.Checked; errorLabel.Text = ""; };
                mode.SelectedIndexChanged += delegate { refresh(); }; continuous.CheckedChanged += delegate { refresh(); }; refresh();
                Button ok = MakeButton("确定", 88, delegate
                {
                    long requested = customVisits;
                    if (mode.SelectedIndex == 3 && !continuous.Checked)
                    {
                        BigInteger parsed; string error;
                        if (!SearchDepth.TryParse(count.Text, out parsed, out error) || !SearchDepth.TryGetNativeValue(parsed, out requested, out error)) { errorLabel.Text = error; return; }
                    }
                    customVisits = requested; continuousDepth = continuous.Checked;
                    updating = true;
                    difficultyBox.Items[3] = continuousDepth ? "自定义 · 持续思考" : "自定义 · " + customVisits.ToString("N0", CultureInfo.InvariantCulture) + " 次";
                    difficultyBox.SelectedIndex = mode.SelectedIndex; selectedDepthIndex = mode.SelectedIndex;
                    tooltips.SetToolTip(difficultyBox, difficultyBox.Items[mode.SelectedIndex] + "；选择自定义可修改搜索次数或持续思考。");
                    updating = false;
                    dialog.DialogResult = DialogResult.OK;
                }); ok.Name = "DepthOK"; ok.Location = new Point(330, 269);
                Button cancel = MakeButton("取消", 88, delegate { dialog.DialogResult = DialogResult.Cancel; }); cancel.Name = "DepthCancel"; cancel.Location = new Point(232, 269);
                dialog.Controls.AddRange(new Control[] { modeLabel, mode, countLabel, count, continuous, note, errorLabel, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                bool applied = dialog.ShowDialog(owner) == DialogResult.OK;
                if (applied) SetStatus("计算深度已设置为：" + difficultyBox.SelectedItem + "，下次分析生效。");
                return applied;
            }
        }
        private void OnBoardPoint(object sender, BoardPointEventArgs e)
        {
            if (thinking || selfPlaying || game.IsOver) return;
            if (!HumanTurn) { SetStatus("当前轮到 AI。点击 AI 落子，或选择双方自由复盘。"); return; }
            string error;
            if (!game.TryPlay(e.X, e.Y, out error)) { SetStatus(error); return; }
            ClearAnalysis(); UpdateUi();
            SetStatus("已落子 " + game.Moves[game.Position - 1].ToGtp(game.Size) + "。");
            MaybeAutoMove();
        }
        private void PassMove()
        {
            if (thinking || selfPlaying || game.IsOver || !HumanTurn) return;
            CancelAnalysis(); game.Pass(); ClearAnalysis(); UpdateUi();
            SetStatus(game.IsOver ? "双方连续停着。点击形势 / 领地查看 KataGo 估计。" : "已停一手。");
            MaybeAutoMove();
        }
        private void UndoMove()
        {
            if (game.Position == 0) return;
            CancelAnalysis(); game.Undo();
            if (automaticBox.Checked && humanBox.SelectedIndex != 2 && game.Position > 0 && !HumanTurn) game.Undo();
            ClearAnalysis(); UpdateUi(); SetStatus("已悔棋。点击棋盘继续，或用方向键复盘。");
        }
        private void Navigate(int position)
        {
            if (position < 0 || position > game.Moves.Count || position == game.Position) return;
            CancelAnalysis(); game.Goto(position); ClearAnalysis(); UpdateUi(); SetStatus("复盘第 " + game.Position + " 手。可继续落子建立新变化。");
        }
        private void NewGame()
        {
            CancelAnalysis(); int size = sizeBox.SelectedIndex == 1 ? 13 : sizeBox.SelectedIndex == 2 ? 9 : 19;
            game.NewGame(size, komi, handicap); game.Rules = rules; ClearAnalysis(); board.Game = game; UpdateUi();
            SetStatus("新对局已开始。" + (handicap > 0 ? "让 " + handicap + " 子，白方先行。" : "黑方先行。")); MaybeAutoMove();
        }
        public void LoadGame(GoGame value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Size != 9 && value.Size != 13 && value.Size != 19) throw new ArgumentException("桌面版支持 9 路、13 路和 19 路棋谱。");
            CancelAnalysis(); game = value; komi = game.Komi; rules = game.Rules; handicap = 0;
            updating = true; sizeBox.SelectedIndex = game.Size == 13 ? 1 : game.Size == 9 ? 2 : 0; humanBox.SelectedIndex = 2; updating = false;
            ClearAnalysis(); board.Game = game; UpdateUi(); SetStatus("棋谱已载入。用方向键复盘，或点击棋盘继续。");
        }
        private async void MaybeAutoMove()
        {
            if (closing || thinking || selfPlaying || !automaticBox.Checked || humanBox.SelectedIndex == 2 || game.IsOver || HumanTurn) return;
            await AnalyzeAsync(true, false);
        }
        private async Task ToggleSelfPlayAsync()
        {
            if (selfPlaying) { CancelAnalysis(); SetStatus("AI 自弈已停止。"); return; }
            if (thinking || game.IsOver) return;
            CancelAnalysis();
            int session = ++selfPlaySession;
            selfPlaying = true;
            UpdateUi();
            try
            {
                while (!closing && selfPlaying && session == selfPlaySession && !game.IsOver)
                {
                    int before = game.Position;
                    await AnalyzeAsync(true, false);
                    if (game.Position == before || !selfPlaying || session != selfPlaySession) break;
                    await Task.Delay(220);
                }
            }
            finally
            {
                if (session == selfPlaySession)
                {
                    selfPlaying = false;
                    if (!closing) UpdateUi();
                }
            }
        }
        private async Task AnalyzeAsync(bool play, bool ownership)
        {
            if (closing || thinking || (play && game.IsOver)) return;
            int stamp = ++generation;
            thinking = true; stopping = false; analysisWillPlay = play; UpdateUi(); SetStatus(ownership ? "KataGo 正在分析形势与领地…" : play ? "KataGo 正在思考下一手…" : "KataGo 正在计算推荐着点…");
            try
            {
                AnalysisResult result = await engine.AnalyzeAsync(game, Visits, ownership, partial => ShowAnalysisProgress(stamp, partial, play, ownership));
                if (closing || stamp != generation) return;
                analysis = result; board.Analysis = result; board.ShowHints = !play && !ownership; board.ShowOwnership = ownership;
                if (play)
                {
                    string error;
                    Move selected = GoGame.ParseGtp(result.BestMove, game.ToPlay, game.Size);
                    if (!game.TryPlayMove(selected, out error)) throw new InvalidOperationException("引擎返回的着点无法落子：" + error);
                    board.ShowHints = false; board.ShowOwnership = false;
                    SetStatus("KataGo：" + (selected.IsPass ? "停一手" : selected.ToGtp(game.Size)) + "。" + (game.IsOver ? "双方连续停着，对局结束。" : selfPlaying ? "AI 正在自弈。" : "轮到你了。"));
                }
                else SetStatus(ownership ? "已显示领地估计。黑 / 白方块表示归属，结果为 AI 估计。" : "已显示推荐着点 A–E，A 为首选。");
            }
            catch (OperationCanceledException) { if (stamp == generation) SetStatus("已停止思考。"); }
            catch (Exception ex)
            {
                if (!closing && stamp == generation)
                {
                    engineLabel.Text = "●  引擎需要检查"; engineLabel.ForeColor = Color.FromArgb(170, 91, 44);
                    SetStatus("AI 暂不可用：" + ex.Message);
                    MessageBox.Show(this, ex.Message + "\r\n\r\n修复后可直接点击 AI 落子重试；你也可以选择双方自由复盘。", "KataGo 无法完成分析", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally { if (!closing && stamp == generation) { thinking = false; stopping = false; UpdateUi(); } }
        }
        private void ShowAnalysisProgress(int stamp, AnalysisResult result, bool play, bool ownership)
        {
            if (closing || IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ShowAnalysisProgress(stamp, result, play, ownership))); } catch (InvalidOperationException) { }
                return;
            }
            if (stamp != generation || !thinking) return;
            analysis = result; board.Analysis = result; board.ShowHints = !play && !ownership; board.ShowOwnership = ownership;
            UpdateUi();
            if (!stopping) SetStatus("已搜索 " + result.Visits.ToString("N0", CultureInfo.InvariantCulture) + " 次，当前最佳 " + result.BestMove + "。可结束思考采用当前结果。");
        }
        private void StopThinking()
        {
            if (selfPlaying) { CancelAnalysis(); SetStatus("AI 自弈已停止。"); return; }
            if (!thinking || stopping) return;
            stopping = true; UpdateUi(); SetStatus("正在结束思考并采用当前最佳结果…"); engine.Stop();
        }
        private void CancelAnalysis()
        {
            generation++; selfPlaySession++; selfPlaying = false; if (engine != null) engine.Cancel(); thinking = false; stopping = false;
            if (board != null) UpdateUi();
        }
        private void ClearAnalysis()
        {
            analysis = null; board.Analysis = null; board.ShowHints = false; board.ShowOwnership = false;
            hintButton.Text = "推荐着点"; scoreButton.Text = "形势 / 领地";
        }
        private void OnEngineStatus(string text)
        {
            if (closing || IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(OnEngineStatus), text); } catch (InvalidOperationException) { } return; }
            engineLabel.Text = (engine.IsReady ? "●  " : "○  ") + text;
            engineLabel.ForeColor = engine.IsReady ? green : muted;
        }
        private void SetStatus(string text) { statusLabel.Text = text; }
        private void UpdateUi()
        {
            if (board == null) return;
            turnLabel.Text = game.IsOver ? "对局结束 · 连续停着" : (game.ToPlay == Stone.Black ? "●  黑方行棋" : "○  白方行棋") + (thinking ? "   /   思考中…" : "");
            gameInfoLabel.Text = game.Size + " 路  ·  " + (game.Rules == "japanese" ? "日本规则" : "中国规则") + "  ·  贴目 " + game.Komi.ToString("0.#", CultureInfo.InvariantCulture);
            moveLabel.Text = "第 " + game.Position + " / " + game.Moves.Count + " 手";
            captureLabel.Text = "提子    黑 " + game.BlackCaptures + "    白 " + game.WhiteCaptures;
            if (game.Position < game.Moves.Count) captureLabel.Text += "    ·    当前处于复盘";
            board.AllowInput = !thinking && !selfPlaying && !game.IsOver && HumanTurn;
            board.Cursor = thinking ? Cursors.WaitCursor : board.AllowInput ? Cursors.Hand : Cursors.Default;
            board.Invalidate();
            aiButton.Enabled = !thinking && !selfPlaying && !game.IsOver; hintButton.Enabled = scoreButton.Enabled = !thinking && !selfPlaying;
            selfPlayButton.Enabled = selfPlaying || (!thinking && !game.IsOver); selfPlayButton.Text = selfPlaying ? "停止自弈" : "AI 自弈";
            passButton.Enabled = !thinking && !selfPlaying && !game.IsOver && HumanTurn; undoButton.Enabled = game.Position > 0;
            cancelButton.Visible = thinking || selfPlaying;
            cancelButton.Enabled = !stopping;
            cancelButton.Text = selfPlaying ? "停止自弈" : analysisWillPlay ? "结束思考并落子" : "结束分析";
            firstButton.Enabled = prevButton.Enabled = game.Position > 0;
            nextButton.Enabled = lastButton.Enabled = game.Position < game.Moves.Count;
            hintButton.Text = board.ShowHints ? "隐藏推荐" : "推荐着点"; scoreButton.Text = board.ShowOwnership ? "隐藏领地" : "形势 / 领地";
            if (analysis == null) { evaluationLabel.Text = "形势等待分析"; detailLabel.Text = "点击推荐着点，查看候选与胜率。"; }
            else
            {
                evaluationLabel.Text = "黑方胜率  " + (analysis.BlackWinrate * 100).ToString("0.0") + "%";
                double lead = analysis.BlackScoreLead;
                detailLabel.Text = (lead >= 0 ? "黑方领先 " : "白方领先 ") + Math.Abs(lead).ToString("0.0") + " 目  ·  " + analysis.Visits + " 次搜索\r\nAI 形势估计，非最终裁判结果。";
            }
            evaluationBar.Invalidate();
        }
        private void PaintEvaluation(object sender, PaintEventArgs e)
        {
            if (analysis == null) return;
            float p = (float)Math.Max(0, Math.Min(1, analysis.BlackWinrate));
            e.Graphics.Clear(Color.FromArgb(226, 227, 222));
            using (Brush b = new SolidBrush(Color.FromArgb(50, 57, 60))) e.Graphics.FillRectangle(b, 0, 0, evaluationBar.Width * p, evaluationBar.Height);
        }
        private void OpenSgf()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "打开围棋棋谱", Filter = "SGF 棋谱 (*.sgf)|*.sgf|所有文件 (*.*)|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try { LoadGame(GoGame.FromSgf(File.ReadAllText(dialog.FileName, Encoding.UTF8))); }
                    catch (Exception ex) { MessageBox.Show(this, "无法打开棋谱：" + ex.Message, "棋谱错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void SaveSgf()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "保存围棋棋谱", Filter = "SGF 棋谱 (*.sgf)|*.sgf", DefaultExt = "sgf", FileName = "梓懿围棋-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sgf" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try { File.WriteAllText(dialog.FileName, game.ToSgf(), new UTF8Encoding(false)); SetStatus("棋谱已保存：" + dialog.FileName); }
                    catch (Exception ex) { MessageBox.Show(this, "无法保存棋谱：" + ex.Message, "保存错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void GameSettings()
        {
            using (Form dialog = new Form { Text = "设置", Width = 344, Height = 330, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font, BackColor = BackColor })
            {
                Label k = new Label { Text = "贴目", Location = new Point(23, 24), Size = new Size(86, 26) };
                NumericUpDown komiInput = new NumericUpDown { Location = new Point(117, 20), Width = 166, Minimum = -400, Maximum = 400, DecimalPlaces = 1, Increment = .5m, Value = (decimal)komi };
                Label h = new Label { Text = "让子（黑）", Location = new Point(23, 68), Size = new Size(94, 26) };
                NumericUpDown handicapInput = new NumericUpDown { Location = new Point(117, 64), Width = 166, Minimum = 0, Maximum = 9, Value = handicap };
                Label ruleLabel = new Label { Text = "规则", Location = new Point(23, 111), Size = new Size(86, 26) };
                ComboBox ruleInput = Combo(new string[] { "中国规则 · 数子", "日本规则 · 数目" }); ruleInput.Location = new Point(117, 107); ruleInput.Width = 166; ruleInput.SelectedIndex = rules == "japanese" ? 1 : 0;
                Label depthLabel = new Label { Text = "计算深度", Location = new Point(23, 154), Size = new Size(86, 26) };
                Button depthButton = MakeButton("设置计算深度…", 166, delegate { ShowDepthSettings(dialog); }); depthButton.Location = new Point(117, 148); depthButton.Height = 30;
                Label info = new Label { Text = "对局设置应用于下一局。\r\n计算深度设置应用于下一次分析。", Location = new Point(23, 193), Size = new Size(288, 42), ForeColor = muted };
                Button ok = MakeButton("确定", 85, delegate {
                    if (handicapInput.Value == 1) { MessageBox.Show(dialog, "让子请选择 0 或 2–9 子。"); return; }
                    if ((komiInput.Value * 2) % 1 != 0) { MessageBox.Show(dialog, "贴目请输入整数或半目，例如 6.5 或 7.5。"); return; }
                    komi = (double)komiInput.Value; handicap = (int)handicapInput.Value; rules = ruleInput.SelectedIndex == 1 ? "japanese" : "chinese-ogs"; dialog.DialogResult = DialogResult.OK;
                }); ok.Location = new Point(198, 242);
                dialog.Controls.AddRange(new Control[] { k, komiInput, h, handicapInput, ruleLabel, ruleInput, depthLabel, depthButton, info, ok }); dialog.AcceptButton = ok; dialog.ShowDialog(this);
            }
        }
        private void ShowHelp()
        {
            MessageBox.Show(this, "在棋盘交叉点点击落子，KataGo 可自动应手。\r\n选择执白后，AI 会先行；选择双方可自由摆棋与复盘。\r\n\r\n推荐着点：显示 A–E 候选。\r\n形势 / 领地：显示胜率、目差和归属估计。\r\n计算深度可选自定义，填写每步搜索次数。\r\n持续思考模式由你手动结束，并采用当前最佳结果。\r\n\r\n← / →：上一手 / 下一手；Home / End：首手 / 末手。\r\nCtrl+N：新对局；Ctrl+O：打开 SGF；Ctrl+S：保存 SGF。\r\n\r\n首次运行请等待本地 KataGo 加载。", "梓懿围棋使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void OnShortcut(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O) { OpenSgf(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { SaveSgf(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.N) { NewGame(); e.Handled = true; }
            else if (!(ActiveControl is ComboBox))
            {
                if (e.KeyCode == Keys.Left) { Navigate(game.Position - 1); e.Handled = true; }
                if (e.KeyCode == Keys.Right) { Navigate(game.Position + 1); e.Handled = true; }
                if (e.KeyCode == Keys.Home) { Navigate(0); e.Handled = true; }
                if (e.KeyCode == Keys.End) { Navigate(game.Moves.Count); e.Handled = true; }
            }
        }
        public void CaptureScreenshot(string path)
        {
            using (Bitmap bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(path, ImageFormat.Png); }
        }
    }
}
