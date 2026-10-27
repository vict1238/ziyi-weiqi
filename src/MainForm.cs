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
        private StudyRegion studyRegion;
        private Button selectStudyButton, clearStudyButton, globalComparisonButton;
        private CheckBox moveNumbersBox;
        private Label studyRegionStatus, variationLabel;
        private string studyComparisonSummary;
        private int analysisPosition = -1;
        private ComboBox humanBox, sizeBox, difficultyBox;
        private CheckBox automaticBox;
        private Button aiButton, hintButton, scoreButton, passButton, undoButton, cancelButton, selfPlayButton;
        private Button firstButton, prevButton, nextButton, lastButton;
        private Label turnLabel, moveLabel, captureLabel, engineLabel, profileLabel, evaluationLabel, detailLabel, gameInfoLabel;
        private ToolStripStatusLabel statusLabel;
        private Panel evaluationBar;
        private bool thinking, closing, updating;
        private int generation, handicap;
        private Stone handicapColor = Stone.Black;
        private int selfPlaySession;
        private bool selfPlaying;
        private bool stopping, analysisWillPlay, continuousDepth;
        private long customVisits = 4096;
        private int selectedDepthIndex;
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
                engineSettings = EngineSettings.CreateDefault();
                settingsError = "引擎设置读取失败，请打开引擎 / 模型设置后确认：" + ex.Message;
            }
            selectedDepthIndex = SearchDepth.DefaultIndex(engineSettings.IsGpu);
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
            if (settingsError == null && !String.IsNullOrEmpty(engineSettings.MigrationNotice)) SetStatus(engineSettings.MigrationNotice);
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
            help.DropDownItems.Add("关于梓懿围棋", null, delegate { MessageBox.Show(this, "梓懿围棋 1.6\r\n\r\n" + Tribute + "\r\n\r\n原生 Windows 围棋对弈与复盘工具。\r\n当前引擎 / 模型：" + engineSettings.Description + "\r\nNVIDIA 显卡可使用 CUDA 加速，设置中可选择 CUDA、OpenCL 或 CPU。\r\nGPU 套件默认使用 tf3-b11c768 最强模型（2026-09-25），保留旧模型与本地模型选择。\r\n搜索次数表示计算量，例如 4096 次访问；不是向后推演 4096 手，也不对应人类段位。\r\n支持局部研究、棋子手数、双向 0–9 子让子与 SGF 棋谱。\r\n\r\nKataGo：github.com/lightvector/KataGo", "关于梓懿围棋", MessageBoxButtons.OK, MessageBoxIcon.Information); });
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
            gameInfoLabel = new Label { Name = "GameInfo", Dock = DockStyle.Right, Width = 350, TextAlign = ContentAlignment.MiddleRight, ForeColor = muted };
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
            board.RegionSelected += OnStudyRegionSelected;
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
            Panel studyRow = new Panel { Width = 264, Height = 36, Margin = new Padding(0) };
            selectStudyButton = MakeButton("框选局部", 129, delegate { SelectStudyRegion(); }); selectStudyButton.Name = "SelectStudyRegion";
            clearStudyButton = MakeButton("退出局部", 129, delegate { ClearStudyRegion(); }); clearStudyButton.Name = "ClearStudyRegion"; clearStudyButton.Location = new Point(135, 0);
            studyRow.Controls.Add(selectStudyButton); studyRow.Controls.Add(clearStudyButton); side.Controls.Add(studyRow);
            Panel studyOptions = new Panel { Width = 264, Height = 36, Margin = new Padding(0) };
            globalComparisonButton = MakeButton("全局对比", 129, async delegate { await CompareGloballyAsync(); }); globalComparisonButton.Name = "GlobalComparison";
            moveNumbersBox = new CheckBox { Name = "ShowMoveNumbers", Text = "显示手数", Location = new Point(141, 4), Width = 123, Height = 27 };
            moveNumbersBox.CheckedChanged += delegate { board.ShowMoveNumbers = moveNumbersBox.Checked; };
            studyOptions.Controls.Add(globalComparisonButton); studyOptions.Controls.Add(moveNumbersBox); side.Controls.Add(studyOptions);
            studyRegionStatus = new Label { Name = "StudyRegionStatus", Width = 264, Height = 36, ForeColor = muted, Margin = new Padding(0, 0, 0, 3), Font = new Font("Microsoft YaHei UI", 8) };
            side.Controls.Add(studyRegionStatus);
            tooltips.SetToolTip(selectStudyButton, "在棋盘上按住左键拖动框选。双方推演和实际落子限制在框内，保留全盘棋子作为背景。");
            tooltips.SetToolTip(globalComparisonButton, "仅做一次全盘分析，不自动落子，也不清除研究框；下一次局部分析仍使用原区域。");
            tooltips.SetToolTip(moveNumbersBox, "在现存棋子上显示原始落子手数；停着占手数，开局预摆棋子不编号。");
            side.Controls.Add(Divider());
            side.Controls.Add(SectionTitle("KataGo"));
            engineLabel = new Label { Width = 264, Height = 43, Text = "○  引擎待命\r\n首次分析时加载模型", ForeColor = muted, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 5) };
            side.Controls.Add(engineLabel);
            Panel profileRow = new Panel { Width = 264, Height = 36, Margin = new Padding(0, 0, 0, 3) };
            profileLabel = new Label { Name = "EngineProfile", Location = new Point(0, 0), Width = 195, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted, AutoEllipsis = true, Font = new Font("Microsoft YaHei UI", 8) };
            Button engineSettingsButton = MakeButton("设置…", 63, delegate { ShowEngineSettings(this); }); engineSettingsButton.Name = "EngineSettings"; engineSettingsButton.Location = new Point(201, 0); engineSettingsButton.Height = 30;
            profileRow.Controls.Add(profileLabel); profileRow.Controls.Add(engineSettingsButton); side.Controls.Add(profileRow);
            difficultyBox = Combo(SearchDepth.Choices()); difficultyBox.SelectedIndex = selectedDepthIndex; difficultyBox.Name = "SearchLevel";
            difficultyBox.DropDownWidth = 340;
            difficultyBox.SelectedIndexChanged += delegate { if (!updating && difficultyBox.SelectedIndex >= 0 && difficultyBox.SelectedIndex != SearchDepth.CustomIndex) selectedDepthIndex = difficultyBox.SelectedIndex; };
            difficultyBox.SelectionChangeCommitted += OnDepthSelected;
            tooltips.SetToolTip(difficultyBox, "GPU 默认 2048 次；更高搜索量需要更多等待时间。选择自定义可输入其他次数，或持续思考直到手动停止。");
            side.Controls.Add(Field("计算深度", difficultyBox));
            automaticBox = new CheckBox { Text = "AI 自动应手", Checked = true, Width = 264, Height = 27, Margin = new Padding(0, 0, 0, 4), ForeColor = ink, Name = "AutomaticReply" };
            automaticBox.CheckedChanged += delegate { if (!updating) { if (automaticBox.Checked) MaybeAutoMove(); else if (thinking) CancelAnalysis(); } };
            side.Controls.Add(automaticBox);
            Panel aiRow = new Panel { Width = 264, Height = 39, Margin = new Padding(0) };
            aiButton = MakeButton("AI 落子", 169, async delegate { await AnalyzeAsync(true, false); }); aiButton.Name = "AiPlay"; aiButton.BackColor = green; aiButton.ForeColor = Color.White; aiButton.FlatAppearance.BorderColor = green;
            selfPlayButton = MakeButton("AI 自弈", 89, async delegate { await ToggleSelfPlayAsync(); }); selfPlayButton.Name = "SelfPlay"; selfPlayButton.Location = new Point(175, 0);
            aiRow.Controls.Add(aiButton); aiRow.Controls.Add(selfPlayButton); side.Controls.Add(aiRow);
            Panel analysisRow = new Panel { Width = 264, Height = 39, Margin = new Padding(0) };
            hintButton = MakeButton("推荐着点", 129, async delegate { if (analysis != null && board.ShowHints && !(studyRegion != null && analysis.Region == null)) { board.ShowHints = false; hintButton.Text = "推荐着点"; } else await AnalyzeAsync(false, false); }); hintButton.Name = "BestHints"; hintButton.Location = new Point(0, 0);
            scoreButton = MakeButton("形势 / 领地", 129, async delegate { if (analysis != null && board.ShowOwnership) { board.ShowOwnership = false; scoreButton.Text = "形势 / 领地"; } else await AnalyzeAsync(false, true); }); scoreButton.Name = "Score"; scoreButton.Location = new Point(135, 0);
            analysisRow.Controls.Add(hintButton); analysisRow.Controls.Add(scoreButton); side.Controls.Add(analysisRow);
            cancelButton = MakeButton("结束思考", 264, delegate { StopThinking(); }); cancelButton.Name = "StopThinking"; cancelButton.Visible = false; side.Controls.Add(cancelButton);
            evaluationLabel = new Label { Width = 264, Height = 27, Text = "形势等待分析", ForeColor = ink, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 0, 0) };
            evaluationBar = new Panel { Width = 264, Height = 8, Margin = new Padding(0, 0, 0, 5), BackColor = Color.FromArgb(226, 226, 221) };
            evaluationBar.Paint += PaintEvaluation;
            detailLabel = new Label { Width = 264, Height = 41, Text = "点击推荐着点，查看候选与胜率。", ForeColor = muted, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 5) };
            side.Controls.Add(evaluationLabel); side.Controls.Add(evaluationBar); side.Controls.Add(detailLabel);
            variationLabel = new Label { Name = "AnalysisVariation", Width = 264, Height = 56, ForeColor = muted, Visible = false, Margin = new Padding(0, 0, 0, 4), Font = new Font("Microsoft YaHei UI", 8) };
            side.Controls.Add(variationLabel);
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
            b.Items.AddRange(items); if (b.Items.Count > 0) b.SelectedIndex = 0; return b;
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
        private long Visits { get { return selectedDepthIndex == SearchDepth.CustomIndex ? (continuousDepth ? 0 : customVisits) : SearchDepth.PresetVisits(selectedDepthIndex); } }

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
            using (Form dialog = new Form { Name = "EngineModelSettings", Text = "引擎 / 模型设置", ClientSize = new Size(710, 502), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font, BackColor = BackColor })
            {
                Label backendLabel = new Label { Text = "计算后端", Location = new Point(22, 23), Size = new Size(100, 27) };
                ComboBox backend = Combo(new string[] { "CPU · 内置便携引擎", EngineSettings.CudaBackendDescription + " · NVIDIA 显卡", EngineSettings.GpuBackendDescription + " · 通用显卡", "自定义 · 本地 KataGo" }); backend.Name = "EngineBackend"; backend.Location = new Point(128, 19); backend.Width = 560;
                Label executableLabel = new Label { Text = "KataGo 程序（.exe）", Location = new Point(22, 66), Size = new Size(310, 25) };
                TextBox executable = new TextBox { Name = "EnginePath", Location = new Point(22, 93), Width = 587 };
                Label modelLabel = new Label { Text = "神经网络模型 · 日期按官方上传 / 收录记录标注", Location = new Point(22, 138), Size = new Size(666, 25) };
                ComboBox modelChoice = Combo(new string[0]); modelChoice.Name = "ModelSelection"; modelChoice.Location = new Point(22, 166); modelChoice.Width = 666; modelChoice.DropDownWidth = 720;
                Label modelDetails = new Label { Name = "ModelDetails", Location = new Point(22, 204), Size = new Size(666, 48), ForeColor = muted };
                Label modelPathLabel = new Label { Text = "模型文件（自定义模型可浏览本地 .bin.gz / .bin）", Location = new Point(22, 262), Size = new Size(666, 25) };
                TextBox model = new TextBox { Name = "ModelPath", Location = new Point(22, 292), Width = 587 };
                Button browseEngine = MakeButton("浏览…", 72, delegate
                {
                    using (OpenFileDialog file = new OpenFileDialog { Title = "选择本地 KataGo 程序", Filter = "KataGo 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*" })
                        if (file.ShowDialog(dialog) == DialogResult.OK) executable.Text = file.FileName;
                }); browseEngine.Name = "BrowseEngine"; browseEngine.Location = new Point(616, 90); browseEngine.Height = 30;
                Button browseModel = MakeButton("浏览…", 72, delegate
                {
                    using (OpenFileDialog file = new OpenFileDialog { Title = "选择 KataGo 神经网络模型", Filter = "KataGo 模型 (*.bin.gz;*.bin)|*.bin.gz;*.bin|所有文件 (*.*)|*.*" })
                        if (file.ShowDialog(dialog) == DialogResult.OK) { modelChoice.SelectedIndex = modelChoice.Items.Count - 1; model.Text = file.FileName; }
                }); browseModel.Name = "BrowseModel"; browseModel.Location = new Point(616, 289); browseModel.Height = 30;
                Label explanation = new Label { Name = "EngineSettingsExplanation", Location = new Point(22, 335), Size = new Size(666, 60), ForeColor = muted };
                Label error = new Label { Name = "EngineSettingsError", Location = new Point(22, 397), Size = new Size(666, 50), ForeColor = Color.FromArgb(170, 68, 48) };
                EngineSettings[] drafts = new EngineSettings[] { EngineSettings.CreateCpu(), EngineSettings.CreateCuda(), EngineSettings.CreateGpu(), new EngineSettings { Backend = "custom", EnginePath = "", ModelPath = "", ModelId = "" } };
                int previous = engineSettings.NormalizedBackend == "cuda" ? 1 : engineSettings.NormalizedBackend == "gpu" ? 2 : engineSettings.NormalizedBackend == "custom" ? 3 : 0;
                drafts[previous] = engineSettings.Clone();
                backend.SelectedIndex = previous;
                bool refreshing = false;
                Action refreshModelDetails = delegate
                {
                    ModelCatalogEntry entry = modelChoice.SelectedItem as ModelCatalogEntry;
                    model.ReadOnly = entry != null;
                    modelDetails.Text = entry == null ? "自定义模型：请选择与你使用的 KataGo 引擎兼容的本地模型文件。" :
                        entry.ModelName + "\r\n" + entry.Label + (entry.Id == ModelCatalog.StrongestId ? "；截至 2026-10-20 官方评分可信的最强模型。" : "；保留旧版选择。");
                    tooltips.SetToolTip(modelDetails, modelDetails.Text);
                };
                Action refresh = delegate
                {
                    refreshing = true;
                    EngineSettings draft = drafts[backend.SelectedIndex];
                    executable.Text = draft.EnginePath ?? ""; model.Text = draft.ModelPath ?? "";
                    modelChoice.Items.Clear();
                    ModelCatalogEntry[] entries = backend.SelectedIndex == 0 ? new ModelCatalogEntry[] { ModelCatalog.Find(ModelCatalog.LegacyCpuId) } : ModelCatalog.GpuModels;
                    int selected = -1;
                    foreach (ModelCatalogEntry entry in entries)
                    {
                        modelChoice.Items.Add(entry);
                        if (entry.Id == draft.ModelId) selected = modelChoice.Items.Count - 1;
                    }
                    modelChoice.Items.Add("自定义模型文件…");
                    modelChoice.SelectedIndex = selected >= 0 ? selected : modelChoice.Items.Count - 1;
                    refreshModelDetails();
                    explanation.Text = backend.SelectedIndex == 0 ? "内置 b10 轻量模型用于 CPU 便携运行；GPU 模型请在上方切换计算后端后选择。\r\n确认后应用；当前棋局保留。" : backend.SelectedIndex == 1 ? "CUDA 适用于 NVIDIA 显卡；完整包已附所需运行库，无需安装 CUDA 开发工具。\r\n默认使用最强模型，也可选择 b18。首次加载需稍候；确认后保留棋局。" : backend.SelectedIndex == 2 ? "OpenCL 适用于支持的 NVIDIA、AMD 和 Intel 显卡。\r\n与 CUDA 共用模型；首次调优可能需要数分钟。确认后保留棋局。" : "选择本地 KataGo 程序，可搭配套件模型或自定义模型。\r\n引擎需支持所选模型及 KataGo analysis JSON 协议。";
                    error.Text = "";
                    refreshing = false;
                };
                refresh();
                modelChoice.SelectedIndexChanged += delegate
                {
                    if (refreshing) return;
                    EngineSettings draft = drafts[backend.SelectedIndex];
                    ModelCatalogEntry entry = modelChoice.SelectedItem as ModelCatalogEntry;
                    draft.ModelId = entry == null ? "" : entry.Id;
                    draft.ModelPath = entry == null ? "" : entry.Embedded ? "" : entry.RelativePath;
                    model.Text = draft.ModelPath;
                    refreshModelDetails(); error.Text = "";
                };
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
                }); ok.Name = "EngineSettingsOK"; ok.Location = new Point(600, 455);
                Button cancel = MakeButton("取消", 88, delegate { dialog.DialogResult = DialogResult.Cancel; }); cancel.Name = "EngineSettingsCancel"; cancel.Location = new Point(502, 455);
                dialog.Controls.AddRange(new Control[] { backendLabel, backend, executableLabel, executable, browseEngine, modelLabel, modelChoice, modelDetails, modelPathLabel, model, browseModel, explanation, error, ok, cancel });
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private void OnDepthSelected(object sender, EventArgs e)
        {
            if (updating) return;
            if (difficultyBox.SelectedIndex != SearchDepth.CustomIndex) { selectedDepthIndex = difficultyBox.SelectedIndex; return; }
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
                ComboBox mode = Combo(SearchDepth.Choices()); mode.Name = "DepthMode"; mode.Location = new Point(118, 18); mode.Width = 300; mode.SelectedIndex = difficultyBox.SelectedIndex;
                Label countLabel = new Label { Text = "每步搜索次数", Location = new Point(22, 67), Size = new Size(104, 25) };
                TextBox count = new TextBox { Name = "CustomVisits", Text = customVisits.ToString(CultureInfo.InvariantCulture), Location = new Point(22, 96), Width = 396, MaxLength = 0 };
                CheckBox continuous = new CheckBox { Name = "ContinuousDepth", Text = "持续思考，直到手动停止（不限次数）", Checked = continuousDepth, Location = new Point(22, 136), Size = new Size(396, 28) };
                Label note = new Label { Text = "搜索过程会显示进度；结束思考时采用当前最佳结果。", Location = new Point(22, 176), Size = new Size(396, 37), ForeColor = muted };
                Label errorLabel = new Label { Name = "DepthError", Location = new Point(22, 212), Size = new Size(396, 49), ForeColor = Color.FromArgb(170, 68, 48) };
                Action refresh = delegate { bool custom = mode.SelectedIndex == SearchDepth.CustomIndex; countLabel.Enabled = custom; continuous.Enabled = custom; count.Enabled = custom && !continuous.Checked; errorLabel.Text = ""; };
                mode.SelectedIndexChanged += delegate { refresh(); }; continuous.CheckedChanged += delegate { refresh(); }; refresh();
                Button ok = MakeButton("确定", 88, delegate
                {
                    long requested = customVisits;
                    if (mode.SelectedIndex == SearchDepth.CustomIndex && !continuous.Checked)
                    {
                        BigInteger parsed; string error;
                        if (!SearchDepth.TryParse(count.Text, out parsed, out error) || !SearchDepth.TryGetNativeValue(parsed, out requested, out error)) { errorLabel.Text = error; return; }
                    }
                    customVisits = requested; continuousDepth = continuous.Checked;
                    updating = true;
                    difficultyBox.Items[SearchDepth.CustomIndex] = continuousDepth ? "自定义 · 持续思考" : "自定义 · " + customVisits.ToString("N0", CultureInfo.InvariantCulture) + " 次";
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
            if (thinking || selfPlaying || game.IsOver || board.SelectingRegion) return;
            if (!HumanTurn) { SetStatus("当前轮到 AI。点击 AI 落子，或选择双方自由复盘。"); return; }
            if (studyRegion != null && !studyRegion.Contains(e.X, e.Y)) { SetStatus("落点在局部研究框外。请在框内落子，或扩大范围 / 退出局部。"); return; }
            string error;
            if (!game.TryPlay(e.X, e.Y, out error)) { SetStatus(error); return; }
            stoneSound.Play();
            ClearAnalysis(); UpdateUi();
            SetStatus("已落子 " + game.Moves[game.Position - 1].ToDisplay(game.Size) + "。");
            MaybeAutoMove();
        }
        private void PassMove()
        {
            if (thinking || selfPlaying || game.IsOver || !HumanTurn || board.SelectingRegion) return;
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
            game.NewGame(size, komi, handicap, handicapColor); game.Rules = rules; ResetStudyRegion(); ClearAnalysis(); board.Game = game; UpdateUi();
            SetStatus("新对局已开始。" + (handicap > 0
                ? (handicapColor == Stone.Black ? "白让黑 " : "黑让白 ") + handicap + " 子，" +
                    (handicapColor == Stone.Black ? "黑棋先摆，白方先行。" : "白棋先摆，黑方先行。")
                : "分先，黑方先行。")); MaybeAutoMove();
        }
        public void LoadGame(GoGame value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Size != 9 && value.Size != 13 && value.Size != 19) throw new ArgumentException("桌面版支持 9 路、13 路和 19 路棋谱。");
            CancelAnalysis(); ResetStudyRegion(); game = value; komi = game.Komi; rules = game.Rules; handicap = 0; handicapColor = Stone.Black;
            updating = true; sizeBox.SelectedIndex = game.Size == 13 ? 1 : game.Size == 9 ? 2 : 0; humanBox.SelectedIndex = 2; updating = false;
            ClearAnalysis(); board.Game = game; UpdateUi(); SetStatus((String.IsNullOrEmpty(game.ImportNotice) ? "" : game.ImportNotice + " ") + "棋谱已载入。用方向键复盘，或点击棋盘继续。");
        }
        private async void MaybeAutoMove()
        {
            if (closing || thinking || selfPlaying || board.SelectingRegion || !automaticBox.Checked || humanBox.SelectedIndex == 2 || game.IsOver || HumanTurn) return;
            await AnalyzeAsync(true, false);
        }
        private async Task ToggleSelfPlayAsync()
        {
            if (selfPlaying) { CancelAnalysis(); SetStatus("AI 自弈已停止。"); return; }
            if (thinking || game.IsOver || board.SelectingRegion) return;
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
        private void SelectStudyRegion()
        {
            CancelAnalysis(); ClearAnalysis();
            board.SelectingRegion = !board.SelectingRegion;
            UpdateUi();
            SetStatus(board.SelectingRegion ? "按住左键拖动框选研究区域；Esc 可取消，框选不会落子。" : "已取消框选，保留原研究区域。");
        }
        private void OnStudyRegionSelected(object sender, StudyRegionEventArgs e)
        {
            CancelAnalysis(); studyRegion = e.Region; studyRegion.Validate(game.Size);
            board.SelectingRegion = false; board.StudyRegion = studyRegion;
            ClearAnalysis(); UpdateUi();
            SetStatus("已框选 " + studyRegion.Describe(game.Size) + "。双方在框内研究，可点击 AI 落子或推荐着点。");
        }
        private void ResetStudyRegion()
        {
            studyRegion = null; board.SelectingRegion = false; board.StudyRegion = null;
        }
        private void ClearStudyRegion()
        {
            CancelAnalysis(); ResetStudyRegion(); ClearAnalysis(); UpdateUi(); SetStatus("已退出局部研究，恢复全盘分析与落子。");
        }
        private async Task CompareGloballyAsync()
        {
            if (studyRegion == null) { SetStatus("请先框选局部，再比较全盘推荐。"); return; }
            AnalysisResult localResult = analysis != null && analysis.Region != null && analysisPosition == game.Position ? analysis : null;
            CancelAnalysis(); board.SelectingRegion = false; ClearAnalysis();
            int comparisonGeneration = generation + 1;
            await RunAnalysisAsync(false, false, true);
            if (!closing && generation == comparisonGeneration && localResult != null && analysis != null && analysis.Region == null && studyRegion != null)
            {
                studyComparisonSummary = "局部 A：" + BoardCoordinates.FromGtp(localResult.BestMove, game.Size) + " · 全局 A：" + BoardCoordinates.FromGtp(analysis.BestMove, game.Size);
                UpdateUi();
            }
        }
        private Task AnalyzeAsync(bool play, bool ownership)
        {
            return RunAnalysisAsync(play, ownership, false);
        }
        private async Task RunAnalysisAsync(bool play, bool ownership, bool globalComparison)
        {
            if (closing || thinking || board.SelectingRegion || (play && game.IsOver)) return;
            if (engine == null) { SetStatus("请打开引擎 / 模型设置，确认可用的引擎与模型后重试。"); return; }
            StudyRegion requestedRegion = globalComparison ? null : studyRegion;
            if (requestedRegion != null && !LocalStudy.HasLegalMove(game, requestedRegion))
            {
                ClearAnalysis(); UpdateUi(); SetStatus("研究区域内暂无合法落点，请扩大范围或退出局部研究；没有自动脱先或停着。"); return;
            }
            int stamp = ++generation;
            thinking = true; stopping = false; analysisWillPlay = play; UpdateUi(); SetStatus((globalComparison ? "全局对比：" : requestedRegion != null ? "局部研究：" : "") + (ownership ? "KataGo 正在分析形势与领地…" : play ? "KataGo 正在思考下一手…" : "KataGo 正在计算推荐着点…"));
            try
            {
                AnalysisResult result = await engine.AnalyzeAsync(game, Visits, ownership, partial => ShowAnalysisProgress(stamp, partial, play, ownership), requestedRegion);
                if (closing || stamp != generation) return;
                analysis = result; analysisPosition = game.Position; studyComparisonSummary = null;
                board.Analysis = result; board.ShowHints = !play && !ownership; board.ShowOwnership = ownership;
                if (play)
                {
                    string error;
                    Move selected = GoGame.ParseGtp(result.BestMove, game.ToPlay, game.Size);
                    if (requestedRegion != null && (selected.IsPass || !requestedRegion.Contains(selected.X, selected.Y))) throw new InvalidOperationException("AI 返回了研究范围外的着法，已拒绝落子。");
                    if (!game.TryPlayMove(selected, out error)) throw new InvalidOperationException("引擎返回的着点无法落子：" + error);
                    analysisPosition = -1; studyComparisonSummary = null;
                    if (!selected.IsPass) stoneSound.Play();
                    board.ShowHints = false; board.ShowOwnership = false;
                    SetStatus("KataGo：" + selected.ToDisplay(game.Size) + "。" + (game.IsOver ? "双方连续停着，对局结束。" : selfPlaying ? "AI 正在自弈。" : "轮到你了。"));
                }
                else SetStatus((globalComparison ? "全局对比（仅分析，研究框保留）：" : requestedRegion != null ? "局部条件下：" : "") + (ownership ? "已显示全盘领地估计。黑 / 白方块表示归属，结果为 AI 估计。" : "已显示推荐着点 A–E，A：" + BoardCoordinates.FromGtp(result.BestMove, game.Size) + "。"));
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
            analysis = result; analysisPosition = game.Position; studyComparisonSummary = null;
            board.Analysis = result; board.ShowHints = !play && !ownership; board.ShowOwnership = ownership;
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
            studyComparisonSummary = null; analysisPosition = -1;
            if (variationLabel != null) { variationLabel.Text = ""; variationLabel.Visible = false; }
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
            if (studyRegionStatus != null)
            {
                studyRegionStatus.Text = board.SelectingRegion ? "请在棋盘上拖动框选；Esc 取消。" : studyRegion == null ? "全盘模式 · 可框选局部研究" : "局部 " + studyRegion.Describe(game.Size) + " · 双方限定应手\r\n" + (studyComparisonSummary ?? "保留全盘背景，评估为局部条件下估计。");
                selectStudyButton.Text = board.SelectingRegion ? "取消框选" : studyRegion == null ? "框选局部" : "重新框选";
                clearStudyButton.Enabled = studyRegion != null || board.SelectingRegion;
                globalComparisonButton.Enabled = studyRegion != null && !thinking && !selfPlaying && !board.SelectingRegion;
            }
            turnLabel.Text = game.IsOver ? "对局结束 · 连续停着" : (game.ToPlay == Stone.Black ? "●  黑方行棋" : "○  白方行棋") + (thinking ? "   /   思考中…" : "");
            gameInfoLabel.Text = game.Size + " 路  ·  " + (game.Rules == "japanese" ? "日本规则" : "中国规则") + "  ·  贴目 " + game.Komi.ToString("0.#", CultureInfo.InvariantCulture);
            if (game.InitialStones.Count > 0)
            {
                Stone setupColor = game.InitialStones[0].Color;
                foreach (Move stone in game.InitialStones) if (stone.Color != setupColor) { setupColor = Stone.Empty; break; }
                gameInfoLabel.Text += "  ·  " + (setupColor == Stone.Black ? "黑预摆 " : setupColor == Stone.White ? "白预摆 " : "初始摆子 ") + game.InitialStones.Count + " 子";
            }
            moveLabel.Text = "第 " + game.Position + " / " + game.Moves.Count + " 手";
            captureLabel.Text = "提子    黑 " + game.BlackCaptures + "    白 " + game.WhiteCaptures;
            if (game.Position < game.Moves.Count) captureLabel.Text += "    ·    当前处于复盘";
            board.AllowInput = !thinking && !selfPlaying && !game.IsOver && HumanTurn && !board.SelectingRegion;
            board.Cursor = board.SelectingRegion ? Cursors.Cross : thinking ? Cursors.WaitCursor : board.AllowInput ? Cursors.Hand : Cursors.Default;
            board.Invalidate();
            aiButton.Enabled = !thinking && !selfPlaying && !game.IsOver && !board.SelectingRegion; hintButton.Enabled = scoreButton.Enabled = !thinking && !selfPlaying && !board.SelectingRegion;
            selfPlayButton.Enabled = selfPlaying || (!thinking && !game.IsOver && !board.SelectingRegion); selfPlayButton.Text = selfPlaying ? "停止自弈" : "AI 自弈";
            passButton.Enabled = !thinking && !selfPlaying && !game.IsOver && HumanTurn && !board.SelectingRegion; undoButton.Enabled = game.Position > 0;
            cancelButton.Visible = thinking || selfPlaying;
            cancelButton.Enabled = !stopping;
            cancelButton.Text = selfPlaying ? "停止自弈" : analysisWillPlay ? "结束思考并落子" : "结束分析";
            firstButton.Enabled = prevButton.Enabled = game.Position > 0;
            nextButton.Enabled = lastButton.Enabled = game.Position < game.Moves.Count;
            hintButton.Text = board.ShowHints ? (studyRegion != null && analysis != null && analysis.Region == null ? "返回局部推荐" : "隐藏推荐") : "推荐着点"; scoreButton.Text = board.ShowOwnership ? "隐藏领地" : "形势 / 领地";
            if (analysis == null) { evaluationLabel.Text = "形势等待分析"; detailLabel.Text = "点击推荐着点，查看候选与胜率。"; }
            else
            {
                evaluationLabel.Text = (analysis.Region != null ? "局部条件 · " : studyRegion != null ? "全局对比 · " : "") + "黑方胜率  " + (analysis.BlackWinrate * 100).ToString("0.0") + "%";
                double lead = analysis.BlackScoreLead;
                detailLabel.Text = (lead >= 0 ? "黑方领先 " : "白方领先 ") + Math.Abs(lead).ToString("0.0") + " 目  ·  " + analysis.Visits + " 次搜索\r\nAI 形势估计，非最终裁判结果。";
            }
            if (variationLabel != null)
            {
                variationLabel.Visible = analysis != null && analysis.Candidates != null && analysis.Candidates.Count > 0 && analysis.Candidates[0].Pv != null && analysis.Candidates[0].Pv.Length > 0;
                if (variationLabel.Visible)
                {
                    StringBuilder variation = new StringBuilder("A 的变化：");
                    string[] pv = analysis.Candidates[0].Pv;
                    for (int i = 0; i < Math.Min(10, pv.Length); i++)
                    {
                        if (i > 0) variation.Append(" → ");
                        variation.Append(BoardCoordinates.FromGtp(pv[i], game.Size));
                    }
                    variationLabel.Text = variation.ToString();
                }
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
            using (Form dialog = new Form { Name = "GameSettings", Text = "规则 / 设置", Width = 450, Height = 540, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font, BackColor = BackColor })
            {
                Label k = new Label { Text = "贴目", Location = new Point(23, 24), Size = new Size(86, 26) };
                NumericUpDown komiInput = new NumericUpDown { Name = "KomiInput", Location = new Point(117, 20), Width = 166, Minimum = -150, Maximum = 150, DecimalPlaces = 1, Increment = .5m, Value = (decimal)komi };
                Label h = new Label { Text = "让子方向", Location = new Point(23, 68), Size = new Size(94, 26) };
                ComboBox handicapDirection = Combo(new string[] { "白让黑（黑棋先摆）", "黑让白（白棋先摆）" }); handicapDirection.Name = "HandicapDirection"; handicapDirection.Location = new Point(117, 64); handicapDirection.Width = 286; handicapDirection.SelectedIndex = handicapColor == Stone.White ? 1 : 0;
                Label countLabel = new Label { Text = "让子数量", Location = new Point(23, 111), Size = new Size(94, 26) };
                NumericUpDown handicapInput = new NumericUpDown { Name = "HandicapCount", Location = new Point(117, 107), Width = 166, Minimum = 0, Maximum = 9, Value = handicap };
                Label countHint = new Label { Text = "子（0 为分先）", Location = new Point(290, 111), Size = new Size(122, 26), ForeColor = muted };
                Label handicapPreview = new Label { Name = "HandicapPreview", Location = new Point(23, 150), Size = new Size(389, 54), ForeColor = muted };
                Action updateHandicapPreview = delegate {
                    handicapPreview.Text = handicapInput.Value == 0 ? "分先，不预摆棋子，黑方先行。" :
                        (handicapDirection.SelectedIndex == 0 ? "黑棋先摆 " : "白棋先摆 ") + handicapInput.Value + " 子，" +
                        (handicapDirection.SelectedIndex == 0 ? "白方先行。" : "黑方先行。") + (handicapInput.Value == 1 ? "1 子放天元。" : "按标准星位摆放。 ");
                    handicapPreview.Text += "\r\n贴目仍归白方；让子与规则应用于下一局。";
                };
                handicapDirection.SelectedIndexChanged += delegate { updateHandicapPreview(); }; handicapInput.ValueChanged += delegate { updateHandicapPreview(); }; updateHandicapPreview();
                Label ruleLabel = new Label { Text = "规则", Location = new Point(23, 215), Size = new Size(86, 26) };
                ComboBox ruleInput = Combo(new string[] { "中国规则 · 数子", "日本规则 · 数目" }); ruleInput.Name = "GameRule"; ruleInput.Location = new Point(117, 211); ruleInput.Width = 286; ruleInput.SelectedIndex = rules == "japanese" ? 1 : 0;
                Label depthLabel = new Label { Text = "计算深度", Location = new Point(23, 255), Size = new Size(86, 26) };
                Button depthButton = MakeButton("设置计算深度…", 166, delegate { ShowDepthSettings(dialog); }); depthButton.Location = new Point(117, 249); depthButton.Height = 30;
                Label volumeLabel = new Label { Text = "落子音量", Location = new Point(23, 297), Size = new Size(86, 27) };
                TrackBar volumeInput = new TrackBar { Name = "StoneVolume", Location = new Point(108, 288), Size = new Size(181, 45), Minimum = 0, Maximum = 100, TickFrequency = 25, SmallChange = 5, LargeChange = 10, Value = stoneSound.Volume };
                Label volumePercent = new Label { Name = "StoneVolumePercent", Text = volumeInput.Value + "%", Location = new Point(289, 297), Size = new Size(47, 27) };
                volumeInput.ValueChanged += delegate { volumePercent.Text = volumeInput.Value + "%"; };
                Button soundPreview = MakeButton("试听落子声音", 166, delegate { stoneSound.Preview(volumeInput.Value); }); soundPreview.Name = "StoneSoundPreview"; soundPreview.Location = new Point(117, 337); soundPreview.Height = 30;
                Label info = new Label { Text = "点确定后，再点“新对局”应用让子与规则。\r\n音量确认后立即生效；0% 为静音。", Location = new Point(23, 381), Size = new Size(389, 42), ForeColor = muted };
                Button ok = MakeButton("确定", 85, delegate {
                    if ((komiInput.Value * 2) % 1 != 0) { MessageBox.Show(dialog, "贴目请输入整数或半目，例如 6.5 或 7.5。"); return; }
                    try { if (persistEnginePreferences) AudioSettings.SaveVolume(volumeInput.Value); }
                    catch (Exception ex) { MessageBox.Show(dialog, "无法保存音量：" + ex.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    stoneSound.Volume = volumeInput.Value;
                    komi = (double)komiInput.Value; handicap = (int)handicapInput.Value; handicapColor = handicapDirection.SelectedIndex == 1 ? Stone.White : Stone.Black; rules = ruleInput.SelectedIndex == 1 ? "japanese" : "chinese-ogs"; dialog.DialogResult = DialogResult.OK;
                }); ok.Name = "GameSettingsOk"; ok.Location = new Point(318, 445);
                Button cancel = MakeButton("取消", 85, delegate { dialog.DialogResult = DialogResult.Cancel; }); cancel.Name = "GameSettingsCancel"; cancel.Location = new Point(222, 445);
                dialog.Controls.AddRange(new Control[] { k, komiInput, h, handicapDirection, countLabel, handicapInput, countHint, handicapPreview, ruleLabel, ruleInput, depthLabel, depthButton, volumeLabel, volumeInput, volumePercent, soundPreview, info, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }
        private void ShowHelp()
        {
            MessageBox.Show(this, "在棋盘交叉点点击落子，KataGo 可自动应手。\r\n分先时选择执白，AI 会先行；让子棋按实际先行方应手。选择双方可自由摆棋与复盘。\r\n坐标从左上角 A1 起，列连续包含 I；19 路右下角为 S19。\r\n规则 / 设置中可选择黑让白或白让黑 0–9 子，确认后点新对局生效。\r\n1 子放天元，获得让子的一方先摆，对方先行。\r\n\r\n框选局部：拖动选择研究区域，双方搜索与落子限定在框内。\r\n全局对比：仅分析全盘，保留研究框；退出局部恢复全盘对弈。\r\n显示手数：棋子上显示原始落子序号，开局预摆棋子不编号。\r\n局部推荐是限定条件下的估计，征子和劫争可能需要扩大范围。\r\n\r\n推荐着点：显示 A–E 候选及 A 的后续变化。\r\n形势 / 领地：显示胜率、目差和归属估计。\r\n计算深度预设 256、1024、2048、4096、8192、16384 次，或自定义。\r\n持续思考模式由你手动结束，并采用当前最佳结果。\r\n当前引擎 / 模型：" + engineSettings.Description + "。\r\n可从右侧“设置…”或帮助菜单选择 CUDA、OpenCL、CPU、带日期的模型及自定义文件。\r\nCUDA 适用于 NVIDIA 显卡，完整包无需另装 CUDA 开发工具。\r\nGPU 默认最强模型与 2048 次搜索；CPU 便携版默认 256 次。\r\n搜索次数表示计算量，不是推演手数或人类段位。\r\n\r\n← / →：上一手 / 下一手；Home / End：首手 / 末手。\r\nCtrl+N：新对局；Ctrl+O：打开 SGF；Ctrl+S：保存 SGF。\r\n\r\n首次运行请等待本地 KataGo 加载。", "梓懿围棋使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void OnShortcut(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && board.SelectingRegion) { board.SelectingRegion = false; UpdateUi(); SetStatus("已取消框选，保留原研究区域。"); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.O) { OpenSgf(); e.Handled = true; }
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
