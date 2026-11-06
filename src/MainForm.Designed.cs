using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GoDesk
{
    public partial class MainForm
    {
        private readonly AnalysisTimeline analysisTimeline = new AnalysisTimeline();
        private readonly Dictionary<string, WarmButton> sectionButtons = new Dictionary<string, WarmButton>();
        private readonly List<WarmCard> rightCards = new List<WarmCard>();
        private Panel workspace, settingsWorkspace, pageContainer;
        private Label workspaceTitle, settingsProfile, settingsRules, settingsDepth;
        private FlowLayoutPanel rightStack;
        private WinrateChart winrateChart;
        private CandidateTable candidateTable;
        private NumericUpDown jumpMove;
        private WarmCard reviewControls;
        private Button showVariationButton;
        private bool variationExpanded;
        private bool reviewMode, refreshingDesign;
        private string currentSection = "play";

        private void BuildDesignedUi()
        {
            MenuStrip menu = new MenuStrip { BackColor = UiTheme.Surface, ForeColor = ink, Padding = new Padding(16, 2, 0, 2) };
            ToolStripMenuItem file = new ToolStripMenuItem("棋谱(&F)");
            file.DropDownItems.Add("新对局", null, delegate { NewGame(); });
            file.DropDownItems.Add("打开 SGF…", null, delegate { OpenSgf(); });
            file.DropDownItems.Add("保存 SGF…", null, delegate { SaveSgf(); });
            file.DropDownItems.Add("截图 / 照片摆棋…", null, delegate { ImportBoardImage(); });
            file.DropDownItems.Add(new ToolStripSeparator()); file.DropDownItems.Add("退出", null, delegate { Close(); });
            ToolStripMenuItem help = new ToolStripMenuItem("帮助(&H)");
            help.DropDownItems.Add("使用说明", null, delegate { ShowHelp(); });
            help.DropDownItems.Add("引擎 / 模型设置…", null, delegate { ShowEngineSettings(this); });
            help.DropDownItems.Add("第三方许可", null, delegate { Program.ShowLicenses(this); });
            help.DropDownItems.Add("关于梓懿围棋", null, delegate { ShowAbout(); });
            menu.Items.Add(file); menu.Items.Add(help); MainMenuStrip = menu; menu.Visible = false;

            Panel brand = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = UiTheme.Surface, Padding = new Padding(18, 0, 18, 0) };
            PictureBox logo = new PictureBox { Image = UiTheme.ResourceImage("brand.png"), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(18, 7), Size = new Size(28, 28) };
            Label title = new Label { Text = "梓懿围棋", Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold), ForeColor = ink, Location = new Point(54, 5), Size = new Size(160, 30) };
            Label subtitle = new Label { Text = "本地对弈  ·  联机研讨  ·  整盘复盘", ForeColor = muted, Location = new Point(220, 13), Size = new Size(400, 22) };
            Label local = new Label { Text = "KataGo  /  本机计算", ForeColor = UiTheme.Accent, Dock = DockStyle.Right, Width = 210, TextAlign = ContentAlignment.MiddleRight };
            brand.Controls.Add(logo); brand.Controls.Add(title); brand.Controls.Add(subtitle); brand.Controls.Add(local);

            Panel shell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 12, 8), BackColor = UiTheme.Background };
            FlowLayoutPanel nav = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 112, Padding = new Padding(2, 26, 8, 0), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            AddSection(nav, "play", "对局", "home", "NavPlay", delegate { ShowWorkspace("play"); });
            AddSection(nav, "review", "复盘", "history", "NavReview", delegate { ShowWorkspace("review"); });
            AddSection(nav, "online", "联机", "global", "NavOnline", delegate { OpenOnlineRoom(); });
            AddSection(nav, "image", "截图识别", "camera", "ImportBoardImage", delegate { ImportBoardImage(); });
            AddSection(nav, "settings", "设置", "settings", "NavSettings", delegate { ShowWorkspace("settings"); });
            Label navNote = new Label { Text = "梓懿围棋\r\nv2.0.0", Width = 94, Height = 55, ForeColor = muted, Margin = new Padding(6, 28, 0, 0), Font = new Font(Font.FontFamily, 8f) }; nav.Controls.Add(navNote);
            pageContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(9, 0, 0, 0) };
            workspace = new Panel { Dock = DockStyle.Fill, Name = "PlayWorkspace" };
            workspaceTitle = new Label { Text = "本地对弈与复盘", Dock = DockStyle.Top, Height = 38, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), ForeColor = UiTheme.Accent, Padding = new Padding(3, 8, 0, 0) };
            TableLayoutPanel columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0), Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 322));
            Panel boardColumn = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 0) };
            WarmCard boardCard = new WarmCard { Dock = DockStyle.Fill, Padding = new Padding(10), Name = "BoardCard" };
            Panel boardHeader = new Panel { Dock = DockStyle.Top, Height = 32 };
            turnLabel = new Label { Dock = DockStyle.Left, Width = 180, Font = new Font(Font, FontStyle.Bold), ForeColor = ink, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            gameInfoLabel = new Label { Name = "GameInfo", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = muted, Font = new Font(Font.FontFamily, 8f), AutoEllipsis = true };
            boardHeader.Controls.Add(gameInfoLabel); boardHeader.Controls.Add(turnLabel);
            board = new BoardControl { Dock = DockStyle.Fill, Name = "GoBoard", Game = game, BackColor = UiTheme.Surface };
            board.PointClicked += OnBoardPoint; board.RegionSelected += OnStudyRegionSelected;
            boardCard.Controls.Add(board); boardCard.Controls.Add(boardHeader);
            WarmCard controls = new WarmCard { Dock = DockStyle.Bottom, Height = 112, Padding = new Padding(10, 7, 10, 7), Name = "ReviewControls" };
            reviewControls = controls;
            FlowLayoutPanel historyRow = new FlowLayoutPanel { Name = "HistoryActions", Dock = DockStyle.Top, Height = 35, WrapContents = false, Margin = Padding.Empty };
            firstButton = IconButton("first", "首手", delegate { Navigate(0); });
            prevButton = IconButton("previous", "上一手", delegate { Navigate(game.Position - 1); });
            nextButton = IconButton("next", "下一手", delegate { Navigate(game.Position + 1); });
            lastButton = IconButton("last", "末手", delegate { Navigate(game.Moves.Count); });
            moveLabel = new Label { Width = 126, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ink };
            jumpMove = new NumericUpDown { Name = "JumpToMove", Width = 65, Minimum = 0, Maximum = 0, Height = 29, Margin = new Padding(12, 3, 0, 0) };
            jumpMove.ValueChanged += delegate { if (!updating) Navigate((int)jumpMove.Value); };
            historyRow.Controls.Add(firstButton); historyRow.Controls.Add(prevButton); historyRow.Controls.Add(moveLabel); historyRow.Controls.Add(nextButton); historyRow.Controls.Add(lastButton); historyRow.Controls.Add(jumpMove);
            FlowLayoutPanel studyRow = new FlowLayoutPanel { Name = "StudyActions", Dock = DockStyle.Top, Height = 34, WrapContents = false, Margin = Padding.Empty };
            selectStudyButton = MakeButton("框选局部", 112, delegate { SelectStudyRegion(); }); selectStudyButton.Name = "SelectStudyRegion"; SetIcon(selectStudyButton, "target");
            clearStudyButton = MakeButton("退出局部", 100, delegate { ClearStudyRegion(); }); clearStudyButton.Name = "ClearStudyRegion";
            globalComparisonButton = MakeButton("全局对比", 108, async delegate { await CompareGloballyAsync(); }); globalComparisonButton.Name = "GlobalComparison";
            moveNumbersBox = new WarmToggle { Name = "ShowMoveNumbers", Text = "显示手数", Width = 130, Height = 29, Margin = new Padding(10, 2, 0, 0) };
            moveNumbersBox.CheckedChanged += delegate { board.ShowMoveNumbers = moveNumbersBox.Checked; };
            studyRow.Controls.Add(selectStudyButton); studyRow.Controls.Add(clearStudyButton); studyRow.Controls.Add(globalComparisonButton); studyRow.Controls.Add(moveNumbersBox);
            studyRegionStatus = new Label { Name = "StudyRegionStatus", Dock = DockStyle.Fill, Height = 28, ForeColor = muted, Font = new Font(Font.FontFamily, 8f), AutoEllipsis = true };
            captureLabel = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = muted, Font = new Font(Font.FontFamily, 8f), TextAlign = ContentAlignment.MiddleLeft };
            controls.Controls.Add(studyRegionStatus); controls.Controls.Add(captureLabel); controls.Controls.Add(studyRow); controls.Controls.Add(historyRow);
            boardColumn.Controls.Add(boardCard); boardColumn.Controls.Add(controls);
            Panel sideHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(1), Name = "AnalysisSidebar" };
            rightStack = new FlowLayoutPanel { Location = Point.Empty, Width = 297, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            sideHost.Controls.Add(rightStack);
            sideHost.SizeChanged += delegate { ResizeRightCards(sideHost); };

            FlowLayoutPanel gameCard = SideCard("对局设置", "settings");
            humanBox = Combo(new string[] { "执黑", "执白", "双方 · 自由复盘" }); humanBox.Name = "HumanColor";
            humanBox.SelectedIndexChanged += delegate { if (!updating) { CancelAnalysis(); UpdateUi(); MaybeAutoMove(); } };
            gameCard.Controls.Add(DesignedField("执子", humanBox));
            sizeBox = Combo(new string[] { "19 路", "13 路", "9 路" }); sizeBox.Name = "BoardSize";
            Button ruleSettings = MakeButton("规则 / 设置", 105, delegate { GameSettings(); }); ruleSettings.Name = "RuleSettings";
            gameCard.Controls.Add(DesignedPair(sizeBox, ruleSettings));
            Button newGame = MakeButton("新对局", 129, delegate { NewGame(); }); newGame.Name = "NewGame"; SetIcon(newGame, "plus");
            Button load = MakeButton("载入棋谱", 129, delegate { OpenSgf(); }); load.Name = "OpenSgf"; SetIcon(load, "folder");
            gameCard.Controls.Add(DesignedPair(newGame, load));
            Button fullReview = MakeButton("整盘分析", 264, delegate { OpenWholeGameReview(); });
            fullReview.Name = "WholeGameReview"; SetIcon(fullReview, "chart"); gameCard.Controls.Add(fullReview);

            FlowLayoutPanel engineCard = SideCard("KataGo 引擎", "cpu");
            engineLabel = new Label { Height = 22, Text = "引擎待命", ForeColor = muted, AutoEllipsis = true, Margin = Padding.Empty, Font = new Font(Font.FontFamily, 8.3f) };
            profileLabel = new Label { Name = "EngineProfile", Height = 24, ForeColor = muted, AutoEllipsis = true, Margin = Padding.Empty, Font = new Font(Font.FontFamily, 8f) };
            Button engineSettings = MakeButton("设置", 62, delegate { ShowEngineSettings(this); }); engineSettings.Name = "EngineSettings";
            engineCard.Controls.Add(engineLabel); engineCard.Controls.Add(DesignedPair(profileLabel, engineSettings, .76f));
            difficultyBox = Combo(SearchDepth.Choices()); difficultyBox.Name = "SearchLevel"; difficultyBox.SelectedIndex = selectedDepthIndex; difficultyBox.DropDownWidth = 330;
            difficultyBox.SelectedIndexChanged += delegate { if (!updating && difficultyBox.SelectedIndex >= 0 && difficultyBox.SelectedIndex != SearchDepth.CustomIndex) selectedDepthIndex = difficultyBox.SelectedIndex; };
            difficultyBox.SelectionChangeCommitted += OnDepthSelected;
            engineCard.Controls.Add(DesignedField("计算", difficultyBox));

            FlowLayoutPanel aiCard = SideCard("AI 分析", "chart");
            automaticBox = new WarmToggle { Name = "AutomaticReply", Text = "AI 自动应手", Checked = true, Height = 23, Margin = new Padding(0, 0, 0, 3) };
            automaticBox.CheckedChanged += delegate { if (!updating) { if (automaticBox.Checked) MaybeAutoMove(); else if (thinking) CancelAnalysis(); } };
            aiCard.Controls.Add(automaticBox);
            aiButton = MakeButton("AI 落子", 129, async delegate { await AnalyzeAsync(true, false); }); aiButton.Name = "AiPlay"; ((WarmButton)aiButton).Primary = true; SetIcon(aiButton, "play");
            selfPlayButton = MakeButton("AI 自弈", 129, async delegate { await ToggleSelfPlayAsync(); }); selfPlayButton.Name = "SelfPlay";
            aiCard.Controls.Add(DesignedPair(aiButton, selfPlayButton));
            hintButton = MakeButton("推荐着点", 129, async delegate { if (analysis != null && board.ShowHints && !(studyRegion != null && analysis.Region == null)) { board.ShowHints = false; hintButton.Text = "推荐着点"; } else await AnalyzeAsync(false, false); }); hintButton.Name = "BestHints";
            scoreButton = MakeButton("形势 / 领地", 129, async delegate { if (analysis != null && board.ShowOwnership) { board.ShowOwnership = false; scoreButton.Text = "形势 / 领地"; } else await AnalyzeAsync(false, true); }); scoreButton.Name = "Score";
            aiCard.Controls.Add(DesignedPair(hintButton, scoreButton));
            cancelButton = MakeButton("结束思考", 264, delegate { StopThinking(); }); cancelButton.Name = "StopThinking"; cancelButton.Visible = false; SetIcon(cancelButton, "stop"); aiCard.Controls.Add(cancelButton);

            FlowLayoutPanel evaluation = SideCard("形势与推荐点", "target");
            evaluationLabel = new Label { Height = 22, ForeColor = ink, Text = "形势等待分析", Margin = Padding.Empty };
            evaluationBar = new Panel { Height = 5, BackColor = UiTheme.Border, Margin = new Padding(0, 2, 0, 5) }; evaluationBar.Paint += PaintEvaluation;
            detailLabel = new Label { Height = 22, ForeColor = muted, AutoEllipsis = true, Margin = Padding.Empty, Font = new Font(Font.FontFamily, 8f) };
            winrateChart = new WinrateChart { Name = "WinrateHistory", Height = 64, Margin = new Padding(0, 2, 0, 3), Font = Font };
            winrateChart.MoveSelected += delegate(int position) { ShowWorkspace("review"); Navigate(position); };
            candidateTable = new CandidateTable { Name = "CandidateTable", Height = 72, Font = Font, Margin = Padding.Empty };
            variationLabel = new Label { Name = "AnalysisVariation", Height = 42, ForeColor = muted, Font = new Font(Font.FontFamily, 8f), Visible = false, Margin = new Padding(0, 5, 0, 0) };
            evaluation.Controls.Add(evaluationLabel); evaluation.Controls.Add(evaluationBar); evaluation.Controls.Add(detailLabel); evaluation.Controls.Add(new Label { Name = "TimelineScope", Text = "全盘胜率历史 · 未分析处留空", Height = 16, Font = new Font(Font.FontFamily, 7.5f), ForeColor = muted, Margin = Padding.Empty }); evaluation.Controls.Add(winrateChart); evaluation.Controls.Add(candidateTable);
            showVariationButton = MakeButton("查看后续走法", 264, delegate { variationExpanded = !variationExpanded; UpdateUi(); }); showVariationButton.Name = "ShowVariation"; evaluation.Controls.Add(showVariationButton); evaluation.Controls.Add(variationLabel);

            FlowLayoutPanel moves = new FlowLayoutPanel { Name = "GameActions", Dock = DockStyle.Bottom, Height = 35, WrapContents = false, Margin = Padding.Empty };
            passButton = MakeButton("停一手", 129, delegate { PassMove(); }); passButton.Name = "Pass";
            undoButton = MakeButton("悔棋", 129, delegate { UndoMove(); }); undoButton.Name = "Undo"; SetIcon(undoButton, "undo");
            Button save = MakeButton("保存棋谱", 129, delegate { SaveSgf(); }); save.Name = "SaveSgf"; SetIcon(save, "save");
            Button image = MakeButton("截图摆棋", 129, delegate { ImportBoardImage(); }); image.Name = "ImportImageShortcut"; SetIcon(image, "camera");
            passButton.Width = 96; undoButton.Width = 92; save.Width = 114; image.Width = 114;
            foreach (Button button in new Button[] { passButton, undoButton, save, image }) { button.Margin = new Padding(0, 1, 7, 0); moves.Controls.Add(button); } controls.Controls.Add(moves);
            foreach (FlowLayoutPanel row in new FlowLayoutPanel[] { historyRow, studyRow, moves })
                row.SizeChanged += delegate(object sender, EventArgs e) { CenterToolbarRow((FlowLayoutPanel)sender); };
            columns.Controls.Add(boardColumn, 0, 0); columns.Controls.Add(sideHost, 1, 0);
            workspace.Controls.Add(columns); workspace.Controls.Add(workspaceTitle);
            BuildSettingsWorkspace(); pageContainer.Controls.Add(workspace); pageContainer.Controls.Add(settingsWorkspace);
            shell.Controls.Add(pageContainer); shell.Controls.Add(nav);
            StatusStrip status = new StatusStrip { BackColor = UiTheme.Surface, ForeColor = muted, SizingGrip = true, Padding = new Padding(17, 0, 12, 0) };
            statusLabel = new ToolStripStatusLabel("在棋盘上点击落子，或导入截图开始研究。") { Spring = true, TextAlign = ContentAlignment.MiddleLeft }; status.Items.Add(statusLabel);
            Controls.Add(shell); Controls.Add(brand); Controls.Add(menu); Controls.Add(status);
            tooltips.SetToolTip(moveNumbersBox, "显示每颗现存棋子的原始手数；预摆棋子不编号。");
            tooltips.SetToolTip(selectStudyButton, "按住左键拖动，选择双方限定应手的研究范围。");
            tooltips.SetToolTip(globalComparisonButton, "保持局部框，仅做全盘分析，不自动落子。");
            ResizeRightCards(sideHost); SetNavigationSelection("play");
            Shown += delegate { UpdateUi(); };
        }

        private void AddSection(FlowLayoutPanel panel, string key, string text, string icon, string name, EventHandler click)
        { WarmButton button = new WarmButton { Name = name, Text = text, IconName = icon, Width = 100, Height = 44, Margin = new Padding(0, 0, 0, 9), TextAlign = ContentAlignment.MiddleLeft }; button.Click += click; sectionButtons[key] = button; panel.Controls.Add(button); }
        private Button IconButton(string icon, string accessible, EventHandler click)
        { Button button = MakeButton("", 35, click); button.Height = 29; button.Margin = new Padding(0, 0, 4, 0); button.AccessibleName = accessible; SetIcon(button, icon); tooltips.SetToolTip(button, accessible); return button; }
        private static void SetIcon(Button button, string icon) { WarmButton warm = button as WarmButton; if (warm != null) warm.IconName = icon; }
        private static void CenterToolbarRow(FlowLayoutPanel row)
        {
            if (UiTheme.Scaling) return;
            int width = 0;
            foreach (Control child in row.Controls) if (child.Visible) width += child.Width + child.Margin.Horizontal;
            int left = Math.Max(0, (row.ClientSize.Width - width) / 2);
            if (row.Padding.Left != left) row.Padding = new Padding(left, 0, 0, 0);
        }
        private FlowLayoutPanel SideCard(string title, string icon)
        {
            WarmCard card = new WarmCard { Title = title, IconName = icon, Width = 296, Height = 140, Margin = new Padding(0, 0, 0, 10) };
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            content.SizeChanged += delegate { if (UiTheme.Scaling) return; foreach (Control child in content.Controls) child.Width = Math.Max(1, content.ClientSize.Width - child.Margin.Horizontal); };
            card.Controls.Add(content); rightCards.Add(card); rightStack.Controls.Add(card); return content;
        }
        private Control DesignedField(string label, Control input)
        {
            TableLayoutPanel row = new TableLayoutPanel { Height = 26, Width = 268, ColumnCount = 2, Margin = new Padding(0, 0, 0, 3), Padding = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted, Margin = Padding.Empty }, 0, 0);
            input.Dock = DockStyle.Fill; input.Margin = new Padding(0, 1, 0, 0); row.Controls.Add(input, 1, 0); return row;
        }
        private Control DesignedPair(Control first, Control second, float proportion = .5f)
        {
            TableLayoutPanel row = new TableLayoutPanel { Height = Math.Max(28, Math.Max(first.Height, second.Height)), Width = 268, ColumnCount = 2, Margin = new Padding(0, 0, 0, 3), Padding = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, proportion * 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, (1 - proportion) * 100));
            first.Dock = second.Dock = DockStyle.Fill; first.Margin = new Padding(0, 0, 4, 0); second.Margin = new Padding(4, 0, 0, 0);
            row.Controls.Add(first, 0, 0); row.Controls.Add(second, 1, 0); return row;
        }
        private void ResizeRightCards(Panel host)
        {
            if (rightStack == null || UiTheme.Scaling) return;
            int width = Math.Max(180, host.ClientSize.Width - host.Padding.Horizontal - 2);
            rightStack.MinimumSize = new Size(width, 0); rightStack.MaximumSize = new Size(width, 0); rightStack.Width = width;
            foreach (WarmCard card in rightCards) card.Width = width;
        }
        private static DialogResult ShowStyledDialog(Form dialog, IWin32Window owner) { UiTheme.StyleDialog(dialog); return dialog.ShowDialog(owner); }
        private static DialogResult ShowStyledDialog(CommonDialog dialog, IWin32Window owner) { return dialog.ShowDialog(owner); }

        private void BuildSettingsWorkspace()
        {
            settingsWorkspace = new Panel { Dock = DockStyle.Fill, Name = "SettingsWorkspace", Visible = false, AutoScroll = true, Padding = new Padding(3) };
            Label title = new Label { Text = "设置", Dock = DockStyle.Top, Height = 44, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), ForeColor = UiTheme.Accent };
            FlowLayoutPanel cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 4, 16, 0) };
            settingsProfile = SettingsCard(cards, "引擎与模型", "cpu", "选择 CUDA、OpenCL 或 CPU，以及带日期的模型。", "引擎 / 模型设置…", "SettingsEngine", delegate { ShowEngineSettings(this); });
            settingsRules = SettingsCard(cards, "对局与声音", "settings", "棋盘规则、贴目、双向让子和落子音量。", "规则与声音设置…", "SettingsRules", delegate { GameSettings(); });
            settingsDepth = SettingsCard(cards, "计算深度", "chart", "选择预设、自定义搜索次数或持续思考。", "计算深度设置…", "SettingsDepth", delegate { ShowDepthSettings(this); });
            Label about = SettingsCard(cards, "关于梓懿围棋", "help", Tribute + "\r\nv2.0.0 · 本地对弈、AI 分析与截图研究", "关于梓懿围棋", "SettingsAbout", delegate { ShowAbout(); });
            WarmCard aboutCard = (WarmCard)about.Parent; aboutCard.Height = 170;
            FlowLayoutPanel aboutActions = (FlowLayoutPanel)aboutCard.Controls[1];
            Button usage = MakeButton("使用说明", 210, delegate { ShowHelp(); }); usage.Name = "SettingsHelp";
            Button licenses = MakeButton("第三方许可", 210, delegate { Program.ShowLicenses(this); }); licenses.Name = "SettingsLicenses";
            aboutActions.Controls.Add(usage); aboutActions.Controls.Add(licenses);
            cards.SizeChanged += delegate { foreach (Control c in cards.Controls) c.Width = Math.Max(360, cards.ClientSize.Width - 30); };
            settingsWorkspace.Controls.Add(cards); settingsWorkspace.Controls.Add(title);
        }
        private Label SettingsCard(FlowLayoutPanel parent, string title, string icon, string text, string action, string name, EventHandler click)
        {
            WarmCard card = new WarmCard { Title = title, IconName = icon, Width = 700, Height = 125, Margin = new Padding(0, 0, 0, 14) };
            Label label = new Label { Text = text, Dock = DockStyle.Fill, ForeColor = muted, AutoEllipsis = true };
            Button button = MakeButton(action, 190, click); button.Name = name; button.Height = 32;
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            button.Width = 210; actions.Controls.Add(button);
            card.Controls.Add(label); card.Controls.Add(actions); parent.Controls.Add(card); return label;
        }
        private void SetNavigationSelection(string key) { foreach (var pair in sectionButtons) pair.Value.Selected = pair.Key == key; }
        private void ShowWorkspace(string section)
        {
            if (section == "settings") { CancelAnalysis(); workspace.Visible = false; settingsWorkspace.Visible = true; settingsWorkspace.BringToFront(); }
            else
            {
                if (section == "review") CancelAnalysis();
                workspace.Visible = true; settingsWorkspace.Visible = false; workspace.BringToFront();
                reviewMode = section == "review"; workspaceTitle.Text = reviewMode ? "复盘与研究" : "本地对弈与复盘";
            }
            currentSection = section; SetNavigationSelection(section); UpdateUi();
            if (section == "play") MaybeAutoMove();
        }
        private void RefreshDesignedUi()
        {
            if (winrateChart == null || refreshingDesign || UiTheme.Scaling) return;
            refreshingDesign = true;
            try
            {
                bool current = analysis != null && analysisPosition == game.Position;
                candidateTable.ShowResult(current ? analysis : null, game.Size);
                bool hasVariation = current && analysis.Candidates != null && analysis.Candidates.Count > 0 && analysis.Candidates[0].Pv != null && analysis.Candidates[0].Pv.Length > 0;
                showVariationButton.Visible = hasVariation; showVariationButton.Text = variationExpanded ? "收起后续走法" : "查看后续走法";
                variationLabel.Visible = hasVariation && variationExpanded;
                winrateChart.SetPoints(analysisTimeline.Snapshot(game), game.Position);
                if (!current && analysis != null) { evaluationLabel.Text = "上一手分析 · 黑方视角"; detailLabel.Text = "点击推荐着点，分析当前局面。"; variationLabel.Visible = false; }
                studyRegionStatus.Visible = studyRegion != null || board.SelectingRegion;
                if (reviewControls != null)
                {
                    int footerHeight = reviewControls.Padding.Vertical;
                    foreach (Control child in reviewControls.Controls) if (child != studyRegionStatus) footerHeight += child.Height;
                    if (studyRegionStatus.Visible) footerHeight += TextRenderer.MeasureText("局部研究", studyRegionStatus.Font).Height * 2;
                    reviewControls.Height = footerHeight;
                    foreach (Control child in reviewControls.Controls)
                    { FlowLayoutPanel row = child as FlowLayoutPanel; if (row != null) CenterToolbarRow(row); }
                }
                automaticBox.Enabled = !reviewMode; automaticBox.Text = reviewMode ? "复盘中暂停自动应手" : "AI 自动应手";
                updating = true; jumpMove.Maximum = game.Moves.Count; jumpMove.Value = Math.Min(game.Position, game.Moves.Count); updating = false;
                if (settingsProfile != null) settingsProfile.Text = engineSettings.Description;
                if (settingsRules != null) settingsRules.Text = (rules == "japanese" ? "日本规则" : "中国规则") + " · 贴目 " + komi.ToString("0.#") + " · " + (handicap == 0 ? "分先" : (handicapColor == Stone.Black ? "白让黑 " : "黑让白 ") + handicap + " 子") + "\r\n在这里设置规则、让子和音量。";
                if (settingsDepth != null) settingsDepth.Text = Visits == 0 ? "持续思考，直到手动结束。" : Visits.ToString("N0") + " 次搜索 · 可选择其他预设或自定义次数。";
                if (Visible && workspace.Visible) foreach (WarmCard card in rightCards)
                {
                    if (card.Controls.Count == 0) continue; int height = card.Padding.Vertical;
                    foreach (Control child in card.Controls[0].Controls) child.Width = Math.Max(1, card.Controls[0].ClientSize.Width - child.Margin.Horizontal);
                    foreach (Control child in card.Controls[0].Controls) if (child.Visible) height += child.Height + child.Margin.Vertical;
                    card.Height = Math.Max(card.Padding.Vertical + 24, height + 2);
                }
            }
            finally { refreshingDesign = false; }
        }
        private void ShowAbout()
        {
            MessageBox.Show(this, "梓懿围棋 2.0.0\r\n\r\n" + Tribute + "\r\n\r\n本地对弈 · AI 分析 · 截图识别 · 复盘研究\r\n当前引擎 / 模型：" + engineSettings.Description + "\r\n\r\n搜索次数表示计算量，不是向后推演的手数或人类段位。\r\n截图导入的是初始摆子局面，不能恢复原始手数与劫争历史。\r\n\r\nKataGo：github.com/lightvector/KataGo", "关于梓懿围棋", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
