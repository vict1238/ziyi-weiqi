using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    internal sealed class WholeGameReviewForm : Form
    {
        private readonly EngineSettings profile;
        private readonly BoardControl board;
        private readonly Label status, positionLabel, comparison, model;
        private readonly ComboBox depth, phase, side, problemMetric;
        private readonly TextBox visitsInput;
        private readonly NumericUpDown startMove, endMove, moveNumber;
        private readonly Button open, start, pause, deeper, after, thresholdsButton;
        private readonly ProgressBar progress;
        private readonly TabControl tabs;
        private readonly DataGridView summary, matches, segments, problems, distribution;
        private readonly WinrateChart winrate;
        private readonly ReviewScoreChart score;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly Dictionary<int, ReviewRow> deepRows = new Dictionary<int, ReviewRow>();
        private WholeGameReview report;
        private GoGame game;
        private ReviewRow[] rows = new ReviewRow[0];
        private ReviewThresholds thresholds = new ReviewThresholds();
        private double goodLimit = 1.5, averageLimit = 3, poorLimit = 4.5, blunderLimit = 6;
        private bool busy, updating, showAfter;
        private int dirty;

        internal WholeGameReviewForm(GoGame source, EngineSettings settings)
        {
            profile = settings.Clone();
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Name = "WholeGameReviewForm";
            Text = "梓懿围棋 · 整盘分析";
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            KeyPreview = true;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(10, 6, 6, 0), WrapContents = false };
            open = AddButton(top, "导入 SGF", 100, delegate { OpenRecord(); });
            open.Name = "ReviewOpenSgf";
            depth = Choices("快速 · 256", "标准 · 1024", "精细 · 4096", "自定义搜索次数");
            depth.Width = 154;
            depth.Name = "ReviewBudget";
            top.Controls.Add(depth);
            visitsInput = new TextBox { Width = 82, Text = "1024", ReadOnly = true, AccessibleName = "每手搜索次数" };
            top.Controls.Add(visitsInput);
            depth.SelectedIndexChanged += delegate
            {
                visitsInput.ReadOnly = depth.SelectedIndex != 3;
                if (depth.SelectedIndex != 3) visitsInput.Text = new[] { "256", "1024", "4096" }[depth.SelectedIndex];
            };
            depth.SelectedIndex = profile.IsGpu ? 1 : 0;
            start = AddButton(top, "开始分析", 105, async delegate { await StartAnalysis(); }, true);
            start.Name = "ReviewStart";
            pause = AddButton(top, "暂停", 65, delegate { if (report != null) report.Pause(); status.Text = "正在暂停，已完成的结果会保留。"; });
            pause.Name = "ReviewPause";
            thresholdsButton = AddButton(top, "评分阈值", 89, delegate { EditThresholds(); });

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(13, 2, 13, 4) };
            model = TextLine("", 19);
            status = TextLine("", 21);
            progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 6, Maximum = 1, AccessibleName = "整盘分析进度" };
            footer.Controls.Add(progress);
            footer.Controls.Add(status);
            footer.Controls.Add(model);

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10, 0, 10, 2) };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
            var left = new WarmCard { Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 0, 8, 0) };
            board = new BoardControl { Dock = DockStyle.Fill, AllowInput = false, ShowMoveNumbers = true, BackColor = UiTheme.Surface };
            positionLabel = TextLine("", 23);
            var nav = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 37, WrapContents = false, Padding = new Padding(1, 2, 0, 0) };
            AddButton(nav, "上一手", 64, delegate { SelectMove((int)moveNumber.Value - 1); });
            moveNumber = new NumericUpDown { Width = 65, Minimum = 1, Maximum = 1, AccessibleName = "查看第几手", Name = "ReviewMove" };
            moveNumber.ValueChanged += delegate { if (!updating) { showAfter = false; RefreshBoard(); } };
            nav.Controls.Add(moveNumber);
            AddButton(nav, "下一手", 64, delegate { SelectMove((int)moveNumber.Value + 1); });
            after = AddButton(nav, "看实战后", 82, delegate { showAfter = !showAfter; RefreshBoard(); });
            deeper = AddButton(nav, "复算本手", 86, async delegate { await Recheck(); });
            comparison = new Label { Dock = DockStyle.Bottom, Height = 100, ForeColor = UiTheme.Text, Padding = new Padding(5), AutoEllipsis = true };
            left.Controls.Add(board);
            left.Controls.Add(nav);
            left.Controls.Add(comparison);
            left.Controls.Add(positionLabel);

            var right = new Panel { Dock = DockStyle.Fill };
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 37, WrapContents = false, Padding = new Padding(1, 2, 0, 0) };
            phase = Choices("全盘", "布局", "中盘", "官子", "自选手数");
            phase.Width = 100;
            phase.AccessibleName = "手数区间";
            phase.SelectedIndex = 0;
            side = Choices("双方", "黑方", "白方");
            side.Width = 72;
            side.AccessibleName = "筛选执色";
            side.SelectedIndex = 0;
            startMove = new NumericUpDown { Width = 63, Minimum = 1, Maximum = 1, AccessibleName = "区间起始手数" };
            endMove = new NumericUpDown { Width = 63, Minimum = 1, Maximum = 1, AccessibleName = "区间结束手数" };
            filters.Controls.Add(phase);
            filters.Controls.Add(startMove);
            filters.Controls.Add(new Label { Text = "至", Width = 23, Height = 26, TextAlign = ContentAlignment.MiddleCenter });
            filters.Controls.Add(endMove);
            filters.Controls.Add(side);
            phase.SelectedIndexChanged += delegate { if (!updating) { UpdateRange(); RefreshTabs(); } };
            side.SelectedIndexChanged += delegate { if (!updating) RefreshTabs(); };
            startMove.ValueChanged += delegate { if (!updating) RefreshTabs(); };
            endMove.ValueChanged += delegate { if (!updating) RefreshTabs(); };

            tabs = new TabControl { Dock = DockStyle.Fill, Name = "ReviewTabs" };
            summary = Grid("棋方", "有效手数", "一选率", "好手率", "恶手率", "平均目损", "终局停着");
            AddTab("测评", summary, "好手率包含一选。比例分母为已有效评估的实战手。");
            matches = Grid("手数", "棋方", "实战", "AI 首选", "吻合", "目损");
            segments = Grid("棋方", "连续好手区段", "一选", "好手");
            segments.Dock = DockStyle.Top;
            segments.Height = 108;
            var matchPanel = new Panel { Dock = DockStyle.Fill };
            matchPanel.Controls.Add(matches);
            matchPanel.Controls.Add(segments);
            AddTab("吻合度", matchPanel, "区段列出同一棋方连续至少 5 手好手，点击跳转。");
            winrate = new WinrateChart { Dock = DockStyle.Fill };
            winrate.MoveSelected += delegate(int index) { SelectMove(index + 1); };
            score = new ReviewScoreChart { Dock = DockStyle.Fill };
            score.MoveSelected += delegate(int index) { SelectMove(index + 1); };
            var graphs = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            graphs.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            graphs.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            graphs.Controls.Add(winrate, 0, 0);
            graphs.Controls.Add(score, 0, 1);
            AddTab("胜率与目差", graphs, "黑方视角 · 每个点为该手落子前的局面。");
            problems = Grid("手数", "棋方", "实战", "AI 首选", "目损", "胜率下降");
            problemMetric = Choices("按目损筛选", "胜率下降至少 5 个百分点");
            problemMetric.Dock = DockStyle.Top;
            problemMetric.SelectedIndex = 0;
            problemMetric.SelectedIndexChanged += delegate { RefreshTabs(); };
            var problemPanel = new Panel { Dock = DockStyle.Fill };
            problemPanel.Controls.Add(problems);
            problemPanel.Controls.Add(problemMetric);
            AddTab("问题手", problemPanel, "点击问题手，棋盘蓝圈标出实战落点。");
            distribution = Grid("发挥分档", "黑方", "白方");
            AddTab("发挥分布", distribution, "分档以首选与目损为依据。阈值可在上方调整。");
            tabs.SelectedIndexChanged += delegate { RefreshTabs(); };
            right.Controls.Add(tabs);
            right.Controls.Add(filters);
            columns.Controls.Add(left, 0, 0);
            columns.Controls.Add(right, 1, 0);
            Controls.Add(columns);
            Controls.Add(footer);
            Controls.Add(top);
            refreshTimer = new System.Windows.Forms.Timer { Interval = 240 };
            refreshTimer.Tick += delegate { if (Interlocked.Exchange(ref dirty, 0) != 0) RefreshReport(); };
            refreshTimer.Start();
            SetGame(GoGame.FromSgf(source.ToSgf()));
            UiTheme.FinishDpiLayout(this, new Size(1250, 830));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (refreshTimer != null) refreshTimer.Dispose();
                if (report != null) { report.Dispose(); report = null; }
            }
            base.Dispose(disposing);
        }

        private static Button AddButton(FlowLayoutPanel panel, string text, int width, EventHandler click, bool primary = false)
        {
            var button = new WarmButton { Text = text, Width = width, Height = 28, Primary = primary, Margin = new Padding(2, 0, 4, 0) };
            button.Click += click;
            panel.Controls.Add(button);
            return button;
        }

        private static ComboBox Choices(params string[] values)
        {
            var input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            input.Items.AddRange(values);
            return input;
        }

        private static Label TextLine(string text, int height)
        {
            return new Label { Text = text, Height = height, Dock = DockStyle.Top, ForeColor = UiTheme.Muted, AutoEllipsis = true };
        }

        private DataGridView Grid(params string[] columns)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = UiTheme.Surface, BorderStyle = BorderStyle.None,
                ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.SoftAccent;
            grid.DefaultCellStyle.SelectionBackColor = UiTheme.SoftAccent;
            grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.Background;
            foreach (string name in columns)
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, SortMode = DataGridViewColumnSortMode.NotSortable, MinimumWidth = 34 });
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is int) SelectMove((int)grid.Rows[e.RowIndex].Tag);
            };
            return grid;
        }

        private void AddTab(string name, Control content, string description)
        {
            var page = new TabPage(name) { BackColor = UiTheme.Surface, Padding = new Padding(7) };
            page.Controls.Add(content);
            page.Controls.Add(TextLine(description, 23));
            tabs.TabPages.Add(page);
        }

        private void SetGame(GoGame newGame)
        {
            if (newGame.Size != 9 && newGame.Size != 13 && newGame.Size != 19)
                throw new ArgumentException("整盘复盘支持 9、13、19 路棋谱。");
            if (report != null) { report.Dispose(); report = null; }
            game = newGame;
            deepRows.Clear();
            rows = new ReviewRow[game.Moves.Count];
            board.Game = game;
            updating = true;
            moveNumber.Maximum = startMove.Maximum = endMove.Maximum = Math.Max(1, game.Moves.Count);
            moveNumber.Value = 1;
            phase.SelectedIndex = 0;
            updating = false;
            UpdateRange();
            showAfter = false;
            status.Text = game.Moves.Count > 0 ? "已载入 " + game.Moves.Count + " 手，选择搜索次数后开始分析。" : "请导入一份带有落子记录的 SGF 棋谱。";
            RefreshReport();
        }

        private void OpenRecord()
        {
            using (var dialog = new OpenFileDialog { Filter = "围棋棋谱 (*.sgf)|*.sgf|所有文件 (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { SetGame(GoGame.FromSgf(File.ReadAllText(dialog.FileName, Encoding.UTF8))); }
                catch (Exception error) { status.Text = "无法载入棋谱：" + error.Message; }
            }
        }

        private bool ParseVisits(string text, out long visits)
        {
            visits = 0;
            BigInteger parsed;
            string error;
            if (SearchDepth.TryParse(text, out parsed, out error) && SearchDepth.TryGetNativeValue(parsed, out visits, out error)) return true;
            status.Text = error;
            return false;
        }

        private async Task StartAnalysis()
        {
            long visits;
            if (busy || game.Moves.Count == 0 || !ParseVisits(visitsInput.Text, out visits)) return;
            try
            {
                if (report == null || report.Visits != visits)
                {
                    if (report != null && report.CompletedCount > 0 &&
                        MessageBox.Show(this, "更改搜索次数会开始一份新的报告，继续吗？", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                    if (report != null) { report.Dispose(); report = null; }
                    report = new WholeGameReview(game, profile, visits);
                    report.Changed += delegate { Interlocked.Exchange(ref dirty, 1); };
                    deepRows.Clear();
                }
                busy = true;
                status.Text = "正在分析棋谱…";
                RefreshReport();
                await report.RunAsync();
                if (!IsDisposed) status.Text = report.CompletedCount == report.TotalMoves ? "本轮分析完成。点报告里的手数可回看。" : "已暂停，可以继续分析。";
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "已暂停，可以继续分析。"; }
            catch (Exception error) { if (!IsDisposed) status.Text = "分析中断：" + error.Message; }
            finally { busy = false; if (!IsDisposed) RefreshReport(); }
        }

        private async Task Recheck()
        {
            if (busy || report == null || game.Moves.Count == 0) return;
            int selected = (int)moveNumber.Value;
            using (var dialog = new Form { Text = "复算第 " + selected + " 手", ClientSize = new Size(360, 140), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font })
            {
                var box = new TextBox { Text = Math.Min(SearchDepth.NativeMaximumVisits, Math.Max(4096, report.Visits > SearchDepth.NativeMaximumVisits / 4 ? SearchDepth.NativeMaximumVisits : report.Visits * 4)).ToString(CultureInfo.InvariantCulture), Bounds = new Rectangle(15, 43, 325, 25), AccessibleName = "复算搜索次数" };
                var ok = new WarmButton { Text = "开始复算", Primary = true, Bounds = new Rectangle(234, 87, 106, 31), DialogResult = DialogResult.OK };
                dialog.Controls.Add(new Label { Text = "输入这一步的搜索次数", Bounds = new Rectangle(15, 14, 320, 25) });
                dialog.Controls.Add(box); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                UiTheme.StyleDialog(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                long visits;
                if (!ParseVisits(box.Text, out visits)) return;
                try
                {
                    busy = true;
                    status.Text = "正在复算第 " + selected + " 手…";
                    RefreshReport();
                    ReviewRow row = await report.RecheckAsync(selected, visits);
                    if (!IsDisposed)
                    {
                        if (row.HasEvaluation) deepRows[selected] = row;
                        status.Text = row.HasEvaluation ? "复算完成。详细结果显示在棋盘下方。" : "这次复算没有得到有效结果，已保留原分析。";
                    }
                }
                catch (OperationCanceledException) { if (!IsDisposed) status.Text = "已暂停复算。"; }
                catch (Exception error) { if (!IsDisposed) status.Text = "复算中断：" + error.Message; }
                finally { busy = false; if (!IsDisposed) RefreshReport(); }
            }
        }

        private void EditThresholds()
        {
            using (var dialog = new Form { Text = "评分阈值 · 目损", ClientSize = new Size(390, 233), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font })
            {
                string[] titles = { "好手上限", "一般上限", "欠佳上限", "恶手起点" };
                double[] values = { goodLimit, averageLimit, poorLimit, blunderLimit };
                var fields = new NumericUpDown[4];
                for (int i = 0; i < fields.Length; i++)
                {
                    dialog.Controls.Add(new Label { Text = titles[i], Bounds = new Rectangle(17, 15 + i * 36, 151, 25) });
                    fields[i] = new NumericUpDown { DecimalPlaces = 1, Minimum = 0, Maximum = 100, Increment = .5m, Value = (decimal)values[i], Bounds = new Rectangle(183, 12 + i * 36, 175, 26), AccessibleName = titles[i] };
                    dialog.Controls.Add(fields[i]);
                }
                var explanation = new Label { Text = "一选始终为最佳；欠佳与恶手之间为不好。", Bounds = new Rectangle(17, 160, 350, 25), ForeColor = UiTheme.Muted };
                var ok = new WarmButton { Text = "应用", Primary = true, Bounds = new Rectangle(253, 190, 105, 30) };
                ok.Click += delegate
                {
                    for (int i = 1; i < fields.Length; i++)
                        if (fields[i].Value <= fields[i - 1].Value) { explanation.Text = "阈值须依次增大。"; return; }
                    thresholds = new ReviewThresholds((double)fields[0].Value, (double)fields[1].Value, (double)fields[2].Value, (double)fields[3].Value);
                    goodLimit = (double)fields[0].Value; averageLimit = (double)fields[1].Value;
                    poorLimit = (double)fields[2].Value; blunderLimit = (double)fields[3].Value;
                    dialog.DialogResult = DialogResult.OK;
                };
                dialog.Controls.Add(explanation); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                UiTheme.StyleDialog(dialog);
                if (dialog.ShowDialog(this) == DialogResult.OK) RefreshReport();
            }
        }

        private void UpdateRange()
        {
            int total = Math.Max(1, game.Moves.Count);
            int third = Math.Max(1, total / 3), twoThirds = Math.Min(total, Math.Max(third, total * 2 / 3));
            updating = true;
            if (phase.SelectedIndex == 0) { startMove.Value = 1; endMove.Value = total; }
            if (phase.SelectedIndex == 1) { startMove.Value = 1; endMove.Value = third; }
            if (phase.SelectedIndex == 2) { startMove.Value = Math.Min(total, third + 1); endMove.Value = Math.Max(startMove.Value, twoThirds); }
            if (phase.SelectedIndex == 3) { startMove.Value = Math.Min(total, twoThirds + 1); endMove.Value = total; }
            startMove.Enabled = endMove.Enabled = phase.SelectedIndex == 4;
            updating = false;
        }

        private void SelectMove(int number)
        {
            if (game == null || game.Moves.Count == 0) return;
            showAfter = false;
            updating = true;
            moveNumber.Value = Math.Max(1, Math.Min(number, game.Moves.Count));
            updating = false;
            RefreshBoard();
        }

        private void RefreshReport()
        {
            if (report != null) rows = report.Snapshot();
            int done = report == null ? 0 : report.CompletedCount;
            progress.Maximum = Math.Max(1, game.Moves.Count);
            progress.Value = Math.Min(done, progress.Maximum);
            model.Text = profile.Description + " · 已处理 " + done + "/" + game.Moves.Count +
                (report == null ? "" : " · 每手 " + report.Visits.ToString("N0") + " 次搜索");
            problemMetric.Items[0] = "目损超过 " + goodLimit.ToString("0.0") + " 目";
            start.Text = done == 0 ? "开始分析" : done < game.Moves.Count ? "继续分析" : "按预算分析";
            open.Enabled = start.Enabled = thresholdsButton.Enabled = !busy;
            start.Enabled = !busy && game.Moves.Count > 0;
            depth.Enabled = visitsInput.Enabled = !busy;
            pause.Enabled = busy;
            RefreshTabs();
            RefreshBoard();
        }

        private void RefreshBoard()
        {
            if (game == null) return;
            int selected = game.Moves.Count == 0 ? 0 : (int)moveNumber.Value;
            after.Enabled = selected > 0;
            deeper.Enabled = !busy && report != null && selected > 0;
            after.Text = showAfter ? "看落子前" : "看实战后";
            if (selected == 0)
            {
                positionLabel.Text = "等待棋谱";
                comparison.Text = "导入 SGF 后可查看实战手和 AI 推荐。";
                board.ReviewMove = null;
                board.Analysis = null;
                return;
            }
            game.Goto(showAfter ? selected : selected - 1);
            Move played = game.Moves[selected - 1];
            positionLabel.Text = "第 " + selected + " 手 · " + ColorName(played.Color) + " " + played.ToDisplay(game.Size) + (showAfter ? " · 实战落子后" : " · 蓝圈为实战落点");
            ReviewRow row;
            bool deep = deepRows.TryGetValue(selected, out row);
            if (!deep) row = selected <= rows.Length ? rows[selected - 1] : null;
            board.ReviewMove = showAfter ? null : played;
            board.Analysis = !showAfter && row != null ? row.Recommendation : null;
            board.ShowHints = !showAfter && board.Analysis != null;
            if (row == null || row.IsPending) { comparison.Text = "这一步等待分析。"; UpdateCharts(); return; }
            if (row.IsTerminalPass) { comparison.Text = "终局停着，单独记录。"; UpdateCharts(); return; }
            if (!row.HasEvaluation)
            {
                comparison.Text = "这一步暂无有效比较。" + (String.IsNullOrEmpty(row.Error) ? "" : " " + row.Error);
                UpdateCharts(); return;
            }
            MoveCandidate best = row.Recommendation.Candidates[0];
            comparison.Text = (deep ? "单手复算 · " : "") + row.Visits.ToString("N0") + " 次 · " +
                GradeName(row.Grade(thresholds)) + " · 目损 " + row.PointLoss.ToString("0.0") +
                " · 胜率下降 " + row.WinrateLoss.ToString("0.0") + " 个百分点\r\nAI 首选 " +
                BoardCoordinates.FromGtp(best.Move, game.Size) + " · " + Pv(best) + "\r\n实战 " + played.ToDisplay(game.Size) + " · " + Pv(row.ActualCandidate);
            UpdateCharts();
        }

        private string Pv(MoveCandidate candidate)
        {
            if (candidate == null || candidate.Pv == null || candidate.Pv.Length == 0) return "暂无后续";
            return String.Join(" → ", candidate.Pv.Take(7).Select(p => BoardCoordinates.FromGtp(p, game.Size)));
        }

        private void RefreshTabs()
        {
            if (game == null) return;
            int first = Math.Min((int)startMove.Value, (int)endMove.Value), last = Math.Max((int)startMove.Value, (int)endMove.Value);
            switch (tabs.SelectedIndex)
            {
                case 0:
                    summary.Rows.Clear();
                    foreach (Stone color in FilterColors())
                    {
                        ReviewSummary data = report == null ? null : report.Summarize(color, first, last, thresholds);
                        if (data == null) summary.Rows.Add(ColorName(color), "等待分析", "—", "—", "—", "—", "—");
                        else summary.Rows.Add(ColorName(color), data.EvaluatedCount + "/" + data.EligibleCount, Rate(data.FirstCount, data.EvaluatedCount), Rate(data.GoodCount, data.EvaluatedCount), Rate(data.BlunderCount, data.EvaluatedCount), data.EvaluatedCount == 0 ? "—" : data.AveragePointLoss.ToString("0.00"), data.TerminalPassCount);
                    }
                    break;
                case 1:
                    RefreshMatches(first, last);
                    break;
                case 2:
                    UpdateCharts();
                    break;
                case 3:
                    RefreshProblems(first, last);
                    break;
                case 4:
                    distribution.Rows.Clear();
                    distribution.Columns[1].Visible = side.SelectedIndex != 2;
                    distribution.Columns[2].Visible = side.SelectedIndex != 1;
                    ReviewSummary black = report == null ? null : report.Summarize(Stone.Black, first, last, thresholds);
                    ReviewSummary white = report == null ? null : report.Summarize(Stone.White, first, last, thresholds);
                    for (int i = 0; i < 6; i++)
                    {
                        ReviewGrade grade = (ReviewGrade)i;
                        distribution.Rows.Add(GradeName(grade), CountRate(black, grade), CountRate(white, grade));
                    }
                    break;
            }
        }

        private void RefreshMatches(int first, int last)
        {
            int scroll = matches.FirstDisplayedScrollingRowIndex;
            matches.Rows.Clear();
            for (int n = first; n <= last && n <= rows.Length; n++)
            {
                Move move = game.Moves[n - 1];
                if (!ColorIncluded(move.Color)) continue;
                ReviewRow row = rows[n - 1];
                string state = row == null || row.IsPending ? "待分析" : row.IsTerminalPass ? "终局停着" : !row.HasEvaluation ? "未评估" : row.IsFirstChoice ? "一选" : GradeName(row.Grade(thresholds));
                AddMoveRow(matches, n, ColorName(move.Color), move.ToDisplay(game.Size), BestPoint(row), state, row != null && row.HasEvaluation ? row.PointLoss.ToString("0.0") : "—");
            }
            RestoreScroll(matches, scroll);
            segments.Rows.Clear();
            foreach (Stone color in FilterColors())
            {
                int begin = 0, end = 0, count = 0, hit = 0;
                for (int n = first; n <= last + 1; n++)
                {
                    bool inside = n <= last && n <= rows.Length;
                    if (inside && game.Moves[n - 1].Color != color) continue;
                    ReviewRow row = inside ? rows[n - 1] : null;
                    if (row != null && row.HasEvaluation && (row.IsFirstChoice || row.PointLoss <= goodLimit))
                    {
                        if (begin == 0) begin = n;
                        end = n; count++;
                        if (row.IsFirstChoice) hit++;
                    }
                    else
                    {
                        if (count >= 5)
                        {
                            int index = segments.Rows.Add(ColorName(color), begin + "–" + end + " 手", hit + "/" + count, count + "/" + count);
                            segments.Rows[index].Tag = begin;
                        }
                        begin = end = count = hit = 0;
                    }
                }
            }
        }

        private void RefreshProblems(int first, int last)
        {
            int scroll = problems.FirstDisplayedScrollingRowIndex;
            problems.Rows.Clear();
            var selected = rows.Where(row => row != null && row.HasEvaluation && !row.IsFirstChoice &&
                row.MoveNumber >= first && row.MoveNumber <= last && ColorIncluded(row.ActualMove.Color));
            selected = problemMetric.SelectedIndex == 0
                ? selected.Where(row => row.PointLoss > goodLimit).OrderByDescending(row => row.PointLoss)
                : selected.Where(row => row.WinrateLoss >= 5).OrderByDescending(row => row.WinrateLoss);
            foreach (ReviewRow row in selected)
                AddMoveRow(problems, row.MoveNumber, ColorName(row.ActualMove.Color), row.ActualMove.ToDisplay(game.Size), BestPoint(row), row.PointLoss.ToString("0.0"), row.WinrateLoss.ToString("0.0"));
            RestoreScroll(problems, scroll);
        }

        private void UpdateCharts()
        {
            if (tabs.SelectedIndex != 2) return;
            var winPoints = new double?[rows.Length];
            var scorePoints = new double?[rows.Length];
            int first = Math.Min((int)startMove.Value, (int)endMove.Value);
            int last = Math.Max((int)startMove.Value, (int)endMove.Value);
            for (int i = first - 1; i < last && i < rows.Length; i++)
            {
                AnalysisResult result = rows[i] == null ? null : rows[i].Recommendation;
                if (result == null) continue;
                winPoints[i] = result.BlackWinrate;
                scorePoints[i] = result.BlackScoreLead;
            }
            int current = Math.Max(0, (int)moveNumber.Value - 1);
            winrate.SetPoints(winPoints, current);
            score.SetPoints(scorePoints, current);
        }

        private IEnumerable<Stone> FilterColors()
        {
            if (side.SelectedIndex != 2) yield return Stone.Black;
            if (side.SelectedIndex != 1) yield return Stone.White;
        }

        private bool ColorIncluded(Stone color) { return side.SelectedIndex == 0 || (side.SelectedIndex == 1 ? color == Stone.Black : color == Stone.White); }
        private string BestPoint(ReviewRow row) { return row == null || row.Recommendation == null ? "—" : BoardCoordinates.FromGtp(row.Recommendation.BestMove, game.Size); }
        private static string Rate(int count, int total) { return total == 0 ? "—" : (100.0 * count / total).ToString("0.0") + "%"; }
        private static string CountRate(ReviewSummary summary, ReviewGrade grade)
        { return summary == null || summary.EvaluatedCount == 0 ? "—" : summary.Count(grade) + " 手 · " + Rate(summary.Count(grade), summary.EvaluatedCount); }
        private static string ColorName(Stone color) { return color == Stone.Black ? "黑方" : "白方"; }
        private static string GradeName(ReviewGrade grade)
        {
            switch (grade)
            {
                case ReviewGrade.Best: return "最佳";
                case ReviewGrade.Good: return "好手";
                case ReviewGrade.Average: return "一般";
                case ReviewGrade.Inaccurate: return "欠佳";
                case ReviewGrade.Bad: return "不好";
                case ReviewGrade.Blunder: return "恶手";
                default: return "未评估";
            }
        }
        private static void AddMoveRow(DataGridView grid, int move, params object[] cells)
        {
            var data = new object[cells.Length + 1];
            data[0] = move;
            Array.Copy(cells, 0, data, 1, cells.Length);
            grid.Rows[grid.Rows.Add(data)].Tag = move;
        }
        private static void RestoreScroll(DataGridView grid, int scroll)
        { if (scroll >= 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll; }

        internal async Task<string> RunSmokeAsync(string folder)
        {
            depth.SelectedIndex = 0;
            await StartAnalysis();
            if (report == null || report.CompletedCount != 6 || rows.Count(row => row.HasEvaluation) != 4 || rows.Count(row => row.IsTerminalPass) != 2)
                throw new InvalidOperationException("界面整盘结果、已评估样本或终局停着错误。");
            SelectMove(3);
            if (game.Position != 2 || board.ReviewMove == null || board.Analysis == null || !comparison.Text.Contains("实战"))
                throw new InvalidOperationException("问题手没有定位到落子前或缺少实战比较。");
            after.PerformClick();
            if (game.Position != 3 || board.ReviewMove != null || board.Analysis != null)
                throw new InvalidOperationException("实战落子后视图保留了落子前标记。");
            after.PerformClick();
            tabs.SelectedIndex = 1;
            if (matches.Rows.Count != 6) throw new InvalidOperationException("吻合度列表缺手。");
            phase.SelectedIndex = 4;
            startMove.Value = 3;
            endMove.Value = 4;
            side.SelectedIndex = 2;
            if (matches.Rows.Count != 1 || (int)matches.Rows[0].Tag != 3)
                throw new InvalidOperationException("手数和棋方组合筛选错误。");
            side.SelectedIndex = 0;
            phase.SelectedIndex = 0;
            tabs.SelectedIndex = 4;
            if (distribution.Rows.Count != 6) throw new InvalidOperationException("发挥分档缺失。");
            Size = MinimumSize;
            PerformLayout();
            foreach (Control control in new Control[] { open, start, pause, thresholdsButton, moveNumber, deeper, tabs })
                if (!ClientRectangle.Contains(RectangleToClient(control.RectangleToScreen(control.ClientRectangle))))
                    throw new InvalidOperationException("最小窗口操作超出视图：" + control.Text);
            tabs.SelectedIndex = 2;
            await Task.Delay(500);
            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(Path.Combine(folder, "whole-game-review.png"));
            }
            return "整盘复盘窗口通过：实际 CPU 6 手分析、4 手有效、2 手终局停着、落子前后定位、手数与棋方筛选、六档分布和最小窗口。";
        }
    }

    internal sealed class ReviewScoreChart : Control
    {
        private double?[] points = new double?[0];
        private int selected;
        public event Action<int> MoveSelected;
        private int PlotLeft { get { return Math.Max(29, Font.Height * 2 + 5); } }

        public ReviewScoreChart()
        {
            DoubleBuffered = true;
            BackColor = UiTheme.Surface;
            Cursor = Cursors.Hand;
            AccessibleName = "黑方领先目数趋势，点击查看对应手数";
        }

        public void SetPoints(double?[] values, int current)
        {
            points = values;
            selected = current;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new RectangleF(PlotLeft, Font.Height + 6, Math.Max(1, Width - PlotLeft - 9), Math.Max(1, Height - Font.Height * 2 - 12));
            double limit = 5;
            foreach (double? point in points)
                if (point.HasValue && !Double.IsNaN(point.Value) && !Double.IsInfinity(point.Value)) limit = Math.Max(limit, Math.Ceiling(Math.Abs(point.Value) / 5) * 5);
            using (var grid = new Pen(UiTheme.Border))
            using (var line = new Pen(UiTheme.Success, 2))
            using (var dot = new SolidBrush(UiTheme.Success))
            using (var marker = new Pen(UiTheme.Muted) { DashStyle = DashStyle.Dot })
            {
                TextRenderer.DrawText(g, "黑方领先（目）", Font, new Point(3, 0), UiTheme.Text);
                for (int i = 0; i < 3; i++)
                {
                    float y = area.Top + area.Height * i / 2;
                    g.DrawLine(grid, area.Left, y, area.Right, y);
                    TextRenderer.DrawText(g, (limit * (1 - i)).ToString("0"), Font, new Point(2, (int)y - Font.Height / 2), UiTheme.Muted);
                }
                float denominator = Math.Max(1, points.Length - 1);
                PointF previous = PointF.Empty;
                bool joined = false;
                for (int i = 0; i < points.Length; i++)
                {
                    double? value = points[i];
                    if (!value.HasValue || Double.IsNaN(value.Value) || Double.IsInfinity(value.Value)) { joined = false; continue; }
                    var point = new PointF(area.Left + area.Width * i / denominator, area.Top + area.Height * (float)((limit - value.Value) / (2 * limit)));
                    if (joined) g.DrawLine(line, previous, point);
                    g.FillEllipse(dot, point.X - 2, point.Y - 2, 4, 4);
                    previous = point;
                    joined = true;
                }
                float cursor = area.Left + area.Width * selected / denominator;
                if (points.Length > 0) g.DrawLine(marker, cursor, area.Top, cursor, area.Bottom);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || points.Length == 0 || MoveSelected == null) return;
            double ratio = Math.Max(0, Math.Min(1, (e.X - PlotLeft) / (double)Math.Max(1, Width - PlotLeft - 9)));
            MoveSelected((int)Math.Round(ratio * (points.Length - 1)));
        }
    }
}
