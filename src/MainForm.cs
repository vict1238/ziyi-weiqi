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
        private readonly StoneSound stoneSound;
        private EngineSettings engineSettings;
        private readonly bool persistEnginePreferences;
        private Action<string> engineStatusHandler;
        private int engineSession;
        private AnalysisResult analysis;
        private BoardControl board;
        private ComboBox humanBox, sizeBox, difficultyBox;
        private CheckBox automaticBox;
        private Button aiButton, hintButton, scoreButton, passButton, undoButton, cancelButton, selfPlayButton;
        private Button firstButton, prevButton, nextButton, lastButton;
        private Label turnLabel, moveLabel, captureLabel, engineLabel, profileLabel, evaluationLabel, detailLabel, gameInfoLabel;
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
        public EngineSettings CurrentEngineSettings { get { return engineSettings.Clone(); } }

        public MainForm() : this(null, true) { }

        internal MainForm(EngineSettings startupSettings, bool savePreferences)
        {
            persistEnginePreferences = savePreferences;
            stoneSound = new StoneSound(savePreferences ? AudioSettings.LoadVolume() : 0);
            string settingsError = null;
            try { engineSettings = startupSettings == null ? EngineSettings.Load() : startupSettings.Clone(); }
            catch (Exception ex)
            {
                engineSettings = EngineSettings.GpuAvailable ? EngineSettings.CreateGpu() : EngineSettings.CreateCpu();
                settingsError = "引擎设置读取失败，请打开引擎 / 模型设置后确认：" + ex.Message;
            }
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
            if (settingsError == null) InstallEngine(engineSettings);
            else { UpdateEngineDescription(); engineLabel.Text = "引擎设置需要确认"; SetStatus(settingsError); }
            UpdateUi();
            FormClosing += delegate { closing = true; generation++; engineSession++; if (engine != null) { engine.Cancel(); engine.Dispose(); } stoneSound.Dispose(); };
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
            help.DropDownItems.Add("引擎 / 模型设置…", null, delegate { ShowEngineSettings(this); });
            help.DropDownItems.Add("第三方许可", null, delegate { Program.ShowLicenses(this); });
            help.DropDownItems.Add("关于梓懿围棋", null, delegate { MessageBox.Show(this, "梓懿围棋 1.2\r\n\r\n" + Tribute + "\r\n\r\n原生 Windows 围棋对弈与复盘工具。\r\n当前引擎 / 模型：" + engineSettings.Description + "\r\n内置 CPU 模型为 g170 b10（2020）；GPU 和自定义使用设置中的模型文件。\r\n搜索次数表示计算量，例如 256 次访问；不是向后推演 256 手，也不对应人类段位。\r\n支持 SGF 棋谱。\r\n\r\nKataGo：github.com/lightvector/KataGo", "关于梓懿围棋", MessageBoxButtons.OK, MessageBoxIcon.Information); });
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
            Panel profileRow = new Panel { Width = 264, Height = 36, Margin = new Padding(0, 0, 0, 3) };
            profileLabel = new Label { Name = "EngineProfile", Location = new Point(0, 0), Width = 195, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted, AutoEllipsis = true, Font = new Font("Microsoft YaHei UI", 8) };
            Button engineSettingsButton = MakeButton("设置…", 63, delegate { ShowEngineSettings(this); }); engineSettingsButton.Name = "EngineSettings"; engineSettingsButton.Location = new Point(201, 0); engineSettingsButton.Height = 30;
            profileRow.Controls.Add(profileLabel); profileRow.Controls.Add(engineSettingsButton); side.Controls.Add(profileRow);
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

        private void InstallEngine(EngineSettings profile)
        {
            KataGoEngine replacement = new KataGoEngine(profile);
            CancelAnalysis();
            int session = ++engineSession;
            if (engine != null)
            {
                if (engineStatusHandler != null) engine.StatusChanged -= engineStatusHandler;
                engine.Dispose();
            }
            engine = replacement;
            engineSettings = profile.Clone();
            engineStatusHandler = delegate(string text) { OnEngineStatus(session, text); };
            engine.StatusChanged += engineStatusHandler;
            UpdateEngineDescription();
            OnEngineStatus(session, engine.Status);
        }

        private void ApplyEngineSettings(EngineSettings profile)
        {
            profile.Validate();
            if (persistEnginePreferences) profile.Save();
            InstallEngine(profile);
            ClearAnalysis(); UpdateUi();
            SetStatus("引擎 / 模型已设置：" + engineSettings.Description + "。下次分析时加载，当前棋局已保留。");
        }

        private void UpdateEngineDescription()
        {
            profileLabel.Text = engineSettings.Description;
            tooltips.SetToolTip(profileLabel, engineSettings.Description + "\r\n程序：" + (String.IsNullOrWhiteSpace(engineSettings.EnginePath) ? "内置 CPU KataGo" : engineSettings.EnginePath) + "\r\n模型：" + (String.IsNullOrWhiteSpace(engineSettings.ModelPath) ? "内置 g170 b10（2020）" : engineSettings.ModelPath));
        }

        private bool ShowEngineSettings(IWin32Window owner)
        {
            using (Form dialog = new Form { Name = "EngineModelSettings", Text = "引擎 / 模型设置", ClientSize = new Size(660, 390), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font, BackColor = BackColor })
            {
                Label backendLabel = new Label { Text = "计算后端", Location = new Point(22, 23), Size = new Size(100, 27) };
                ComboBox backend = Combo(new string[] { "CPU · 内置便携引擎", "GPU · OpenCL 套件", "自定义 · 本地 KataGo" }); backend.Name = "EngineBackend"; backend.Location = new Point(128, 19); backend.Width = 510;
                Label executableLabel = new Label { Text = "KataGo 程序（.exe）", Location = new Point(22, 66), Size = new Size(310, 25) };
                TextBox executable = new TextBox { Name = "EnginePath", Location = new Point(22, 93), Width = 537 };
                Label modelLabel = new Label { Text = "神经网络模型（.bin.gz / .bin）", Location = new Point(22, 138), Size = new Size(420, 25) };
                TextBox model = new TextBox { Name = "ModelPath", Location = new Point(22, 165), Width = 537 };
                Button browseEngine = MakeButton("浏览…", 72, delegate
                {
                    using (OpenFileDialog file = new OpenFileDialog { Title = "选择本地 KataGo 程序", Filter = "KataGo 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*" })
                        if (file.ShowDialog(dialog) == DialogResult.OK) executable.Text = file.FileName;
                }); browseEngine.Name = "BrowseEngine"; browseEngine.Location = new Point(566, 90); browseEngine.Height = 30;
                Button browseModel = MakeButton("浏览…", 72, delegate
                {
                    using (OpenFileDialog file = new OpenFileDialog { Title = "选择 KataGo 神经网络模型", Filter = "KataGo 模型 (*.bin.gz;*.bin)|*.bin.gz;*.bin|所有文件 (*.*)|*.*" })
                        if (file.ShowDialog(dialog) == DialogResult.OK) model.Text = file.FileName;
                }); browseModel.Name = "BrowseModel"; browseModel.Location = new Point(566, 162); browseModel.Height = 30;
                Label explanation = new Label { Name = "EngineSettingsExplanation", Location = new Point(22, 213), Size = new Size(616, 58), ForeColor = muted };
                Label error = new Label { Name = "EngineSettingsError", Location = new Point(22, 276), Size = new Size(616, 57), ForeColor = Color.FromArgb(170, 68, 48) };
                EngineSettings[] drafts = new EngineSettings[] { EngineSettings.CreateCpu(), EngineSettings.CreateGpu(), new EngineSettings { Backend = "custom", EnginePath = "", ModelPath = "" } };
                int previous = engineSettings.Backend == "gpu" ? 1 : engineSettings.Backend == "custom" ? 2 : 0;
                drafts[previous] = engineSettings.Clone();
                backend.SelectedIndex = previous;
                Action refresh = delegate
                {
                    EngineSettings draft = drafts[backend.SelectedIndex];
                    executable.Text = draft.EnginePath ?? ""; model.Text = draft.ModelPath ?? "";
                    explanation.Text = backend.SelectedIndex == 0 ? "CPU 留空时使用内置 KataGo 与 g170 b10 模型（2020）；可另选本地模型。\r\n设置确认后应用，棋局保持不变。" : backend.SelectedIndex == 1 ? "GPU 使用 OpenCL，引擎与模型均从所选文件加载。套件默认使用现代 b18 模型。\r\n请确保显卡驱动可用；加载失败会显示原因，可在这里修正路径。" : "选择本地 KataGo 程序及对应模型文件。\r\n引擎需支持 KataGo analysis JSON 协议；设置确认后应用。";
                    error.Text = "";
                };
                refresh();
                backend.SelectedIndexChanged += delegate
                {
                    drafts[previous].EnginePath = executable.Text; drafts[previous].ModelPath = model.Text;
                    previous = backend.SelectedIndex; refresh();
                };
                Button ok = MakeButton("确定", 88, delegate
                {
                    EngineSettings selected = drafts[backend.SelectedIndex].Clone();
                    selected.EnginePath = executable.Text.Trim(); selected.ModelPath = model.Text.Trim();
                    try { ApplyEngineSettings(selected); dialog.DialogResult = DialogResult.OK; }
                    catch (Exception ex) { error.Text = ex.Message; }
                }); ok.Name = "EngineSettingsOK"; ok.Location = new Point(550, 342);
                Button cancel = MakeButton("取消", 88, delegate { dialog.DialogResult = DialogResult.Cancel; }); cancel.Name = "EngineSettingsCancel"; cancel.Location = new Point(452, 342);
                dialog.Controls.AddRange(new Control[] { backendLabel, backend, executableLabel, executable, browseEngine, modelLabel, model, browseModel, explanation, error, ok, cancel });
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

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
            stoneSound.Play();
            ClearAnalysis(); UpdateUi();
            SetStatus("已落子 " + game.Moves[game.Position - 1].ToDisplay(game.Size) + "。");
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
            CancelAnalysis(); game.Goto(position); ClearAnalysis(); UpdateUi();
            string reviewedMove = game.Position > 0 ? " · " + game.Moves[game.Position - 1].ToDisplay(game.Size) : "";
            SetStatus("复盘第 " + game.Position + " 手" + reviewedMove + "。可继续落子建立新变化。");
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
            ClearAnalysis(); board.Game = game; UpdateUi(); SetStatus((String.IsNullOrEmpty(game.ImportNotice) ? "" : game.ImportNotice + " ") + "棋谱已载入。用方向键复盘，或点击棋盘继续。");
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
            if (engine == null) { SetStatus("请打开引擎 / 模型设置，确认可用的引擎与模型后重试。"); return; }
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
                    if (!selected.IsPass) stoneSound.Play();
                    board.ShowHints = false; board.ShowOwnership = false;
                    SetStatus("KataGo：" + selected.ToDisplay(game.Size) + "。" + (game.IsOver ? "双方连续停着，对局结束。" : selfPlaying ? "AI 正在自弈。" : "轮到你了。"));
                }
                else SetStatus(ownership ? "已显示领地估计。黑 / 白方块表示归属，结果为 AI 估计。" : "已显示推荐着点 A–E，A：" + BoardCoordinates.FromGtp(result.BestMove, game.Size) + "。");
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
            if (!stopping) SetStatus("已搜索 " + result.Visits.ToString("N0", CultureInfo.InvariantCulture) + " 次，当前最佳 " + BoardCoordinates.FromGtp(result.BestMove, game.Size) + "。可结束思考采用当前结果。");
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
        private void OnEngineStatus(int session, string text)
        {
            if (closing || IsDisposed || session != engineSession) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(() => OnEngineStatus(session, text))); } catch (InvalidOperationException) { } return; }
            if (session != engineSession || engine == null) return;
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
            using (Form dialog = new Form { Name = "GameSettings", Text = "设置", Width = 360, Height = 430, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font, BackColor = BackColor })
            {
                Label k = new Label { Text = "贴目", Location = new Point(23, 24), Size = new Size(86, 26) };
                NumericUpDown komiInput = new NumericUpDown { Location = new Point(117, 20), Width = 166, Minimum = -150, Maximum = 150, DecimalPlaces = 1, Increment = .5m, Value = (decimal)komi };
                Label h = new Label { Text = "让子（黑）", Location = new Point(23, 68), Size = new Size(94, 26) };
                NumericUpDown handicapInput = new NumericUpDown { Location = new Point(117, 64), Width = 166, Minimum = 0, Maximum = 9, Value = handicap };
                Label ruleLabel = new Label { Text = "规则", Location = new Point(23, 111), Size = new Size(86, 26) };
                ComboBox ruleInput = Combo(new string[] { "中国规则 · 数子", "日本规则 · 数目" }); ruleInput.Location = new Point(117, 107); ruleInput.Width = 166; ruleInput.SelectedIndex = rules == "japanese" ? 1 : 0;
                Label depthLabel = new Label { Text = "计算深度", Location = new Point(23, 154), Size = new Size(86, 26) };
                Button depthButton = MakeButton("设置计算深度…", 166, delegate { ShowDepthSettings(dialog); }); depthButton.Location = new Point(117, 148); depthButton.Height = 30;
                Label volumeLabel = new Label { Text = "落子音量", Location = new Point(23, 195), Size = new Size(86, 27) };
                TrackBar volumeInput = new TrackBar { Name = "StoneVolume", Location = new Point(108, 186), Size = new Size(181, 45), Minimum = 0, Maximum = 100, TickFrequency = 25, SmallChange = 5, LargeChange = 10, Value = stoneSound.Volume };
                Label volumePercent = new Label { Name = "StoneVolumePercent", Text = volumeInput.Value + "%", Location = new Point(289, 195), Size = new Size(47, 27) };
                volumeInput.ValueChanged += delegate { volumePercent.Text = volumeInput.Value + "%"; };
                Button soundPreview = MakeButton("试听落子声音", 166, delegate { stoneSound.Preview(volumeInput.Value); }); soundPreview.Name = "StoneSoundPreview"; soundPreview.Location = new Point(117, 235); soundPreview.Height = 30;
                Label info = new Label { Text = "对局设置应用于下一局。\r\n音量确认后立即生效；0% 为静音。", Location = new Point(23, 281), Size = new Size(304, 42), ForeColor = muted };
                Button ok = MakeButton("确定", 85, delegate {
                    if (handicapInput.Value == 1) { MessageBox.Show(dialog, "让子请选择 0 或 2–9 子。"); return; }
                    if ((komiInput.Value * 2) % 1 != 0) { MessageBox.Show(dialog, "贴目请输入整数或半目，例如 6.5 或 7.5。"); return; }
                    try { if (persistEnginePreferences) AudioSettings.SaveVolume(volumeInput.Value); }
                    catch (Exception ex) { MessageBox.Show(dialog, "无法保存音量：" + ex.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    stoneSound.Volume = volumeInput.Value;
                    komi = (double)komiInput.Value; handicap = (int)handicapInput.Value; rules = ruleInput.SelectedIndex == 1 ? "japanese" : "chinese-ogs"; dialog.DialogResult = DialogResult.OK;
                }); ok.Name = "GameSettingsOk"; ok.Location = new Point(215, 340);
                Button cancel = MakeButton("取消", 85, delegate { dialog.DialogResult = DialogResult.Cancel; }); cancel.Name = "GameSettingsCancel"; cancel.Location = new Point(119, 340);
                dialog.Controls.AddRange(new Control[] { k, komiInput, h, handicapInput, ruleLabel, ruleInput, depthLabel, depthButton, volumeLabel, volumeInput, volumePercent, soundPreview, info, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }
        private void ShowHelp()
        {
            MessageBox.Show(this, "在棋盘交叉点点击落子，KataGo 可自动应手。\r\n选择执白后，AI 会先行；选择双方可自由摆棋与复盘。\r\n坐标从左上角 A1 起，列连续包含 I；19 路右下角为 S19。\r\n\r\n推荐着点：显示 A–E 候选。\r\n形势 / 领地：显示胜率、目差和归属估计。\r\n计算深度可选自定义，填写每步搜索访问次数。\r\n持续思考模式由你手动结束，并采用当前最佳结果。\r\n当前引擎 / 模型：" + engineSettings.Description + "。\r\n可从右侧“设置…”或帮助菜单选择 CPU、GPU 及本地模型。\r\nCPU 内置 g170 b10（2020）；搜索次数表示计算量，不是推演手数或人类段位。\r\n\r\n← / →：上一手 / 下一手；Home / End：首手 / 末手。\r\nCtrl+N：新对局；Ctrl+O：打开 SGF；Ctrl+S：保存 SGF。\r\n\r\n首次运行请等待本地 KataGo 加载。", "梓懿围棋使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
