using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    /// <summary>A private, editable draft. The caller's game is never edited here.</summary>
    public sealed class ImageImportForm : Form
    {
        private readonly double komi;
        private readonly string rules;
        private Bitmap source;
        private BoardImageGeometry geometry;
        private readonly ImageGridControl imageView;
        private readonly ImportDraftBoard boardView;
        private readonly ComboBox sizeBox, anchorBox, playerBox;
        private readonly NumericUpDown columnsBox, rowsBox, startColumnBox, startRowBox;
        private readonly Button recognizeButton, confirmButton, undoButton;
        private readonly Label status, mappingLabel;
        private readonly List<Control> busyControls = new List<Control>();
        private readonly Stack<DraftChange> undo = new Stack<DraftChange>();
        private Stone[,] stones = new Stone[19, 19];
        private bool[,] uncertain = new bool[19, 19];
        private bool[,] manual = new bool[19, 19];
        private BoardImageGeometry lastRecognizedGeometry;
        private StudyRegion lastRecognizedRegion;
        private int placementAnchor;
        private bool updating, busy, recognized, disposed;
        private int revision;
        private Stone tool = Stone.Black;
        private StudyRegion visibleRegion;

        private sealed class DraftChange
        {
            public int X, Y;
            public Stone Before;
            public bool WasUncertain;
            public bool WasManual;
            public Stone[,] Stones;
            public bool[,] Doubts, Manual;
            public StudyRegion Region;
            public int Anchor;
        }

        public GoGame ImportedGame { get; private set; }
        public StudyRegion ImportedRegion { get; private set; }

        public ImageImportForm(double newKomi, string newRules)
        {
            komi = newKomi; rules = newRules;
            Name = "ImageImport"; Text = "截图 / 照片摆棋";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            MinimumSize = new Size(1000, 700); Size = new Size(1180, 820);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(242, 240, 234);
            KeyPreview = true;

            FlowLayoutPanel files = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 6, 6, 0), WrapContents = false, AutoScroll = true };
            files.Controls.Add(ActionButton("选择图片…", "ImportChooseImage", async delegate { await ChooseImageAsync(); }));
            files.Controls.Add(ActionButton("粘贴图片", "ImportPasteImage", async delegate { await PasteImageAsync(); }));
            files.Controls.Add(ActionButton("屏幕框选…", "ImportCaptureScreen", async delegate { await CaptureScreenAsync(); }));
            files.Controls.Add(ActionButton("旋转 90°", "ImportRotateImage", delegate { RotateImage(); }));
            files.Controls.Add(ActionButton("自动定位", "ImportDetectGrid", async delegate { await DetectGridAsync(); }));
            recognizeButton = ActionButton("识别棋子", "ImportRecognize", async delegate { await RecognizeImageAsync(); });
            recognizeButton.BackColor = Color.FromArgb(41, 113, 99); recognizeButton.ForeColor = Color.White;
            files.Controls.Add(recognizeButton);

            FlowLayoutPanel settings = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(13, 2, 6, 0), AutoScroll = true };
            sizeBox = Choice("ImportBoardSize", new string[] { "19 路", "13 路", "9 路" }, 77);
            columnsBox = Number("ImportColumns", 2, 19, 19);
            rowsBox = Number("ImportRows", 2, 19, 19);
            anchorBox = Choice("ImportAnchor", new string[] { "左上角", "右上角", "左下角", "右下角", "居中", "自定义位置" }, 108);
            startColumnBox = Number("ImportStartColumn", 1, 19, 1);
            startRowBox = Number("ImportStartRow", 1, 19, 1);
            playerBox = Choice("ImportNextPlayer", new string[] { "黑先", "白先" }, 73);
            settings.Controls.Add(Field("完整棋盘", sizeBox));
            settings.Controls.Add(Field("图中列数", columnsBox));
            settings.Controls.Add(Field("图中行数", rowsBox));
            settings.Controls.Add(Field("放在棋盘", anchorBox));
            settings.Controls.Add(Field("起始列", startColumnBox));
            settings.Controls.Add(Field("起始行", startRowBox));
            settings.Controls.Add(Field("下一手", playerBox));
            mappingLabel = new Label { Name = "ImportMapping", Dock = DockStyle.Top, Height = 35, Padding = new Padding(14, 1, 10, 1), ForeColor = Color.FromArgb(76, 88, 84) };

            SplitContainer split = new SplitContainer { Size = new Size(1140, 550), Dock = DockStyle.Fill, SplitterWidth = 7, Panel1MinSize = 330, Panel2MinSize = 330, SplitterDistance = 565 };
            Panel original = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 3, 0) };
            Label imageTitle = new Label { Dock = DockStyle.Top, Height = 48, Text = "原图：四角对齐最外侧可见交叉点\r\n拖动圆点校准；滚轮缩放，右键拖动，双击恢复全图。", ForeColor = Color.FromArgb(76, 88, 84) };
            imageView = new ImageGridControl { Name = "ImportImagePreview", Dock = DockStyle.Fill };
            imageView.GeometryChanged += delegate
            {
                if (source == null) return;
                geometry = imageView.Geometry.Clone(); MarkStale("格线已调整，请重新识别棋子。");
            };
            original.Controls.Add(imageView); original.Controls.Add(imageTitle); split.Panel1.Controls.Add(original);
            Panel preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(3, 0, 10, 0) };
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false, AutoScroll = true };
            RadioButton blackTool = new RadioButton { Name = "ImportBlackTool", Text = "放黑子", AutoSize = true, Checked = true, Padding = new Padding(0, 7, 0, 0) };
            RadioButton whiteTool = new RadioButton { Name = "ImportWhiteTool", Text = "放白子", AutoSize = true, Padding = new Padding(0, 7, 0, 0) };
            RadioButton eraseTool = new RadioButton { Name = "ImportEraseTool", Text = "擦除", AutoSize = true, Padding = new Padding(0, 7, 0, 0) };
            blackTool.CheckedChanged += delegate { if (blackTool.Checked) tool = Stone.Black; };
            whiteTool.CheckedChanged += delegate { if (whiteTool.Checked) tool = Stone.White; };
            eraseTool.CheckedChanged += delegate { if (eraseTool.Checked) tool = Stone.Empty; };
            undoButton = ActionButton("撤销修正", "ImportUndo", delegate { UndoEdit(); }); undoButton.Width = 94;
            tools.Controls.Add(blackTool); tools.Controls.Add(whiteTool); tools.Controls.Add(eraseTool); tools.Controls.Add(undoButton);
            boardView = new ImportDraftBoard { Name = "ImportBoardPreview", Dock = DockStyle.Fill };
            boardView.PointClicked += delegate(object sender, BoardPointEventArgs e) { EditPoint(e.X, e.Y, tool); };
            Label draftHint = new Label { Dock = DockStyle.Bottom, Height = 38, Text = "橙圈：请核对。灰色区域：原图未提供，可手动补子。\r\n连续摆子不会轮流换色或自动提子；右键可擦除。", ForeColor = Color.FromArgb(76, 88, 84), Font = new Font(Font.FontFamily, 8f) };
            boardView.EraseClicked += delegate(object sender, BoardPointEventArgs e) { EditPoint(e.X, e.Y, Stone.Empty); };
            preview.Controls.Add(boardView); preview.Controls.Add(draftHint); preview.Controls.Add(tools); split.Panel2.Controls.Add(preview);

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 87, Padding = new Padding(12, 3, 12, 7) };
            FlowLayoutPanel decisions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button cancel = ActionButton("取消", "ImportCancel", delegate { DialogResult = DialogResult.Cancel; Close(); }); cancel.Width = 85;
            confirmButton = ActionButton("确认导入，开始研究", "ImportConfirm", delegate { ConfirmImport(); }); confirmButton.Width = 180;
            confirmButton.BackColor = Color.FromArgb(41, 113, 99); confirmButton.ForeColor = Color.White;
            decisions.Controls.Add(cancel); decisions.Controls.Add(confirmButton);
            status = new Label { Name = "ImportStatus", Dock = DockStyle.Fill, Text = "选择图片或粘贴截图。实体棋盘照片也可使用；先检查格线，再识别棋子。", ForeColor = Color.FromArgb(76, 88, 84) };
            footer.Controls.Add(status); footer.Controls.Add(decisions);
            Controls.Add(split); Controls.Add(mappingLabel); Controls.Add(settings); Controls.Add(files); Controls.Add(footer);
            CancelButton = cancel;

            foreach (Control c in files.Controls) busyControls.Add(c);
            foreach (Control c in new Control[] { sizeBox, columnsBox, rowsBox, anchorBox, startColumnBox, startRowBox, playerBox }) busyControls.Add(c);
            sizeBox.SelectedIndexChanged += delegate { if (!updating) ChangeBoardSize(); };
            columnsBox.ValueChanged += delegate { if (!updating) ChangeVisibleSize(); };
            rowsBox.ValueChanged += delegate { if (!updating) ChangeVisibleSize(); };
            anchorBox.SelectedIndexChanged += delegate { if (!updating) { UpdatePlacement(true); } };
            startColumnBox.ValueChanged += delegate { if (!updating) { SelectCustomAnchor(); UpdatePlacement(true); } };
            startRowBox.ValueChanged += delegate { if (!updating) { SelectCustomAnchor(); UpdatePlacement(true); } };
            KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.V) { e.Handled = true; e.SuppressKeyPress = true; await PasteImageAsync(); }
                if (e.Control && e.KeyCode == Keys.Z) { UndoEdit(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            UpdatePlacement(false); UpdateEnabled();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true; revision++;
                if (imageView != null) imageView.Source = null;
                if (source != null) { source.Dispose(); source = null; }
            }
            base.Dispose(disposing);
        }

        private int BoardSize { get { return sizeBox.SelectedIndex == 1 ? 13 : sizeBox.SelectedIndex == 2 ? 9 : 19; } }
        private Button ActionButton(string text, string name, EventHandler action)
        {
            Button button = new Button { Text = text, Name = name, Width = 105, Height = 31, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 1, 5, 1), BackColor = Color.White };
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 205); button.Click += action; return button;
        }
        private static ComboBox Choice(string name, string[] choices, int width)
        {
            ComboBox box = new ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            box.Items.AddRange(choices); box.SelectedIndex = 0; return box;
        }
        private static NumericUpDown Number(string name, int min, int max, int value)
        {
            return new NumericUpDown { Name = name, Minimum = min, Maximum = max, Value = value, Width = 53 };
        }
        private static FlowLayoutPanel Field(string label, Control input)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel { AutoSize = true, Height = 29, WrapContents = false, Margin = new Padding(0, 3, 10, 2) };
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0), Margin = new Padding(0, 0, 3, 0) }); panel.Controls.Add(input); return panel;
        }

        private async Task ChooseImageAsync()
        {
            if (busy) return;
            if (!ConfirmDiscardEdits("选择另一张图片会清除当前手动修正。")) return;
            using (OpenFileDialog file = new OpenFileDialog { Title = "选择棋盘截图或照片", Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件 (*.*)|*.*" })
            {
                if (file.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (FileStream stream = File.OpenRead(file.FileName))
                    using (Image image = Image.FromStream(stream, true, true))
                    {
                        ValidateImageSize(image.Width, image.Height);
                        using (Bitmap bitmap = new Bitmap(image))
                        {
                            ApplyExifOrientation(bitmap, image);
                            LoadImage(bitmap);
                        }
                    }
                    await DetectGridAsync();
                }
                catch (Exception ex) { ShowError("无法打开这张图片：" + ex.Message); }
            }
        }
        private static void ApplyExifOrientation(Bitmap bitmap, Image original)
        {
            try
            {
                const int orientation = 0x112;
                if (Array.IndexOf(original.PropertyIdList, orientation) < 0) return;
                int value = BitConverter.ToUInt16(original.GetPropertyItem(orientation).Value, 0);
                RotateFlipType[] transforms = new RotateFlipType[] { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
                if (value >= 1 && value <= 8) bitmap.RotateFlip(transforms[value]);
            }
            catch (ArgumentException) { }
        }
        private static void ValidateImageSize(int width, int height)
        {
            if (width < 24 || height < 24) throw new ArgumentException("图片太小，请使用更清晰的棋盘图片。");
            if (width > 20000 || height > 20000 || (long)width * height > 50000000) throw new ArgumentException("图片过大，请先缩小到 5000 万像素以内。");
        }
        private async Task PasteImageAsync()
        {
            if (busy) return;
            try
            {
                if (!Clipboard.ContainsImage()) { ShowError("剪贴板中没有图片，请先复制截图。"); return; }
                if (!ConfirmDiscardEdits("粘贴另一张图片会清除当前手动修正。")) return;
                using (Image image = Clipboard.GetImage())
                {
                    if (image == null) { ShowError("无法读取剪贴板图片，请重新复制。"); return; }
                    using (Bitmap bitmap = new Bitmap(image)) LoadImage(bitmap);
                }
                await DetectGridAsync();
            }
            catch (Exception ex) { ShowError("无法粘贴图片：" + ex.Message); }
        }

        internal void LoadImage(Bitmap image)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (busy) throw new InvalidOperationException("请等待识别完成。");
            ValidateImageSize(image.Width, image.Height);
            Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(copy)) { g.Clear(Color.White); g.DrawImage(image, 0, 0, image.Width, image.Height); }
            imageView.Source = null;
            if (source != null) source.Dispose(); source = copy;
            imageView.Source = source;
            float margin = Math.Min(source.Width, source.Height) * .09f;
            geometry = new BoardImageGeometry { Columns = (int)columnsBox.Value, Rows = (int)rowsBox.Value, TopLeft = new PointF(margin, margin), TopRight = new PointF(source.Width - 1 - margin, margin), BottomRight = new PointF(source.Width - 1 - margin, source.Height - 1 - margin), BottomLeft = new PointF(margin, source.Height - 1 - margin) };
            imageView.Geometry = geometry.Clone(); imageView.Fit();
            stones = new Stone[BoardSize, BoardSize]; uncertain = new bool[BoardSize, BoardSize]; manual = new bool[BoardSize, BoardSize]; undo.Clear(); lastRecognizedGeometry = null; lastRecognizedRegion = null;
            MarkStale("图片已载入。点击“自动定位”，检查四角与可见行列数后点击“识别棋子”。");
        }
        private void RotateImage()
        {
            if (source == null || busy) return;
            if (!ConfirmDiscardEdits("旋转后需要重新识别，会清除当前手动修正。")) return;
            using (Bitmap rotated = new Bitmap(source)) { rotated.RotateFlip(RotateFlipType.Rotate90FlipNone); LoadImage(rotated); }
        }

        private async Task DetectGridAsync()
        {
            if (source == null || busy) return;
            int token = revision; int expected = BoardSize;
            using (Bitmap snapshot = new Bitmap(source))
            {
                SetBusy(true, "正在寻找棋盘格线…");
                try
                {
                    BoardImageGeometry detected = await Task.Run(delegate { return BoardImageRecognizer.Detect(snapshot, expected); });
                    if (disposed || token != revision) return;
                    detected.Validate(source.Width, source.Height);
                    updating = true;
                    columnsBox.Value = Math.Max(2, Math.Min(BoardSize, detected.Columns)); rowsBox.Value = Math.Max(2, Math.Min(BoardSize, detected.Rows));
                    updating = false;
                    detected.Columns = (int)columnsBox.Value; detected.Rows = (int)rowsBox.Value;
                    geometry = detected; imageView.Geometry = geometry.Clone();
                    UpdatePlacement(false); MarkStale("已找到候选格线。请检查可见行列数，并拖动四角对齐交叉点，再识别棋子。");
                }
                catch (Exception ex) { if (!disposed) ShowError("自动定位未完成，请手动调整四角和可见行列数：" + ex.Message); }
                finally { if (!disposed) SetBusy(false, null); }
            }
        }

        private async Task RecognizeImageAsync()
        {
            if (source == null || busy || geometry == null) return;
            if (!ConfirmDiscardEdits("重新识别会清除当前手动修正，包括原图外手动补上的棋子。")) { RestoreRecognizedCalibration(); return; }
            try { geometry.Validate(source.Width, source.Height); }
            catch (Exception ex) { ShowError("请先调整四角：" + ex.Message); return; }
            int token = revision; BoardImageGeometry snapshotGeometry = geometry.Clone();
            using (Bitmap snapshot = new Bitmap(source))
            {
                SetBusy(true, "正在识别黑子、白子与空点…");
                try
                {
                    BoardImageResult result = await Task.Run(delegate { return BoardImageRecognizer.Recognize(snapshot, snapshotGeometry); });
                    if (disposed || token != revision) return;
                    int x0 = (int)startColumnBox.Value - 1, y0 = (int)startRowBox.Value - 1;
                    stones = new Stone[BoardSize, BoardSize]; uncertain = new bool[BoardSize, BoardSize]; manual = new bool[BoardSize, BoardSize]; undo.Clear();
                    for (int y = 0; y < snapshotGeometry.Rows; y++) for (int x = 0; x < snapshotGeometry.Columns; x++)
                    {
                        stones[x0 + x, y0 + y] = result.Stones[x, y]; uncertain[x0 + x, y0 + y] = result.Uncertain[x, y];
                    }
                    recognized = true; lastRecognizedGeometry = snapshotGeometry.Clone(); lastRecognizedRegion = visibleRegion; UpdateDraft(); UpdateSummary();
                }
                catch (Exception ex) { if (!disposed) ShowError("识别未完成，可重新校准格线：" + ex.Message); }
                finally { if (!disposed) SetBusy(false, null); }
            }
        }

        private void ChangeBoardSize()
        {
            if (!ConfirmDiscardEdits("改变完整棋盘大小会清除当前手动修正。"))
            {
                updating = true; sizeBox.SelectedIndex = stones.GetLength(0) == 13 ? 1 : stones.GetLength(0) == 9 ? 2 : 0; updating = false; return;
            }
            updating = true;
            columnsBox.Maximum = rowsBox.Maximum = BoardSize;
            columnsBox.Value = Math.Min(columnsBox.Value, BoardSize); rowsBox.Value = Math.Min(rowsBox.Value, BoardSize);
            updating = false;
            stones = new Stone[BoardSize, BoardSize]; uncertain = new bool[BoardSize, BoardSize]; manual = new bool[BoardSize, BoardSize]; undo.Clear(); lastRecognizedGeometry = null; lastRecognizedRegion = null;
            ChangeVisibleSize();
        }
        private void ChangeVisibleSize()
        {
            if (geometry != null) { geometry.Columns = (int)columnsBox.Value; geometry.Rows = (int)rowsBox.Value; imageView.Geometry = geometry.Clone(); }
            UpdatePlacement(false); MarkStale("可见行列数已改变，请核对格线并重新识别。");
        }
        private void SelectCustomAnchor()
        {
            updating = true; anchorBox.SelectedIndex = 5; updating = false;
        }
        private void UpdatePlacement(bool stale)
        {
            StudyRegion previous = visibleRegion;
            int maxX = BoardSize - (int)columnsBox.Value + 1, maxY = BoardSize - (int)rowsBox.Value + 1;
            updating = true;
            startColumnBox.Maximum = Math.Max(1, maxX); startRowBox.Maximum = Math.Max(1, maxY);
            int anchor = anchorBox.SelectedIndex;
            if (anchor == 0) { startColumnBox.Value = 1; startRowBox.Value = 1; }
            else if (anchor == 1) { startColumnBox.Value = maxX; startRowBox.Value = 1; }
            else if (anchor == 2) { startColumnBox.Value = 1; startRowBox.Value = maxY; }
            else if (anchor == 3) { startColumnBox.Value = maxX; startRowBox.Value = maxY; }
            else if (anchor == 4) { startColumnBox.Value = (maxX + 1) / 2; startRowBox.Value = (maxY + 1) / 2; }
            updating = false;
            int x = (int)startColumnBox.Value - 1, y = (int)startRowBox.Value - 1;
            StudyRegion next = new StudyRegion(x, y, x + (int)columnsBox.Value - 1, y + (int)rowsBox.Value - 1);
            bool moved = recognized && previous != null && previous.Right - previous.Left == next.Right - next.Left && previous.Bottom - previous.Top == next.Bottom - next.Top &&
                (previous.Left != next.Left || previous.Top != next.Top);
            if (moved && !MoveRecognizedDraft(previous, next))
            {
                updating = true; anchorBox.SelectedIndex = placementAnchor; startColumnBox.Value = previous.Left + 1; startRowBox.Value = previous.Top + 1; updating = false;
                return;
            }
            visibleRegion = next; placementAnchor = anchorBox.SelectedIndex;
            mappingLabel.Text = "图片中的 " + columnsBox.Value + " 列 × " + rowsBox.Value + " 行 → 完整棋盘 " + visibleRegion.Describe(BoardSize) + "。坐标按本软件：左上 A1，字母包含 I。";
            UpdateDraft();
            if (moved) { lastRecognizedRegion = next; UpdateSummary(); status.Text = "已移动识别区域和区域内修正；原图外手动补子保留原位置。可撤销本次移动。"; }
            else if (stale && !recognized) MarkStale("摆放位置已改变，请检查格线后识别。");
        }

        private bool MoveRecognizedDraft(StudyRegion previous, StudyRegion next)
        {
            int dx = next.Left - previous.Left, dy = next.Top - previous.Top;
            for (int y = previous.Top; y <= previous.Bottom; y++) for (int x = previous.Left; x <= previous.Right; x++)
            {
                int nx = x + dx, ny = y + dy;
                if (!previous.Contains(nx, ny) && manual[nx, ny] && stones[nx, ny] != stones[x, y])
                {
                    ShowError("不能移动：目标位置 " + new Move(nx, ny, Stone.Black).ToDisplay(BoardSize) + " 与原图外手动修正冲突。原位置和所有修正已保留。"); return false;
                }
            }
            var saved = new DraftChange { Stones = (Stone[,])stones.Clone(), Doubts = (bool[,])uncertain.Clone(), Manual = (bool[,])manual.Clone(), Region = previous, Anchor = placementAnchor };
            for (int y = previous.Top; y <= previous.Bottom; y++) for (int x = previous.Left; x <= previous.Right; x++) { stones[x, y] = Stone.Empty; uncertain[x, y] = false; manual[x, y] = false; }
            for (int y = previous.Top; y <= previous.Bottom; y++) for (int x = previous.Left; x <= previous.Right; x++)
            {
                int nx = x + dx, ny = y + dy; bool stationary = !previous.Contains(nx, ny) && manual[nx, ny];
                stones[nx, ny] = saved.Stones[x, y]; uncertain[nx, ny] = stationary ? false : saved.Doubts[x, y]; manual[nx, ny] = stationary || saved.Manual[x, y];
            }
            undo.Push(saved); return true;
        }

        private bool HasManualCorrections()
        {
            for (int y = 0; y < manual.GetLength(1); y++) for (int x = 0; x < manual.GetLength(0); x++) if (manual[x, y]) return true;
            return false;
        }
        internal Func<string, bool> ConfirmDiscardCorrectionsForTest;
        private bool ConfirmDiscardEdits(string message)
        {
            if (!HasManualCorrections()) return true;
            if (ConfirmDiscardCorrectionsForTest != null) return ConfirmDiscardCorrectionsForTest(message);
            return MessageBox.Show(this, message + "\r\n是否继续？选择“否”保留当前修正。", "保留手动修正", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        private void RestoreRecognizedCalibration()
        {
            if (lastRecognizedGeometry == null || lastRecognizedRegion == null) return;
            updating = true; columnsBox.Value = lastRecognizedGeometry.Columns; rowsBox.Value = lastRecognizedGeometry.Rows; anchorBox.SelectedIndex = 5;
            startColumnBox.Maximum = BoardSize - lastRecognizedGeometry.Columns + 1; startRowBox.Maximum = BoardSize - lastRecognizedGeometry.Rows + 1;
            startColumnBox.Value = lastRecognizedRegion.Left + 1; startRowBox.Value = lastRecognizedRegion.Top + 1; updating = false;
            geometry = lastRecognizedGeometry.Clone(); imageView.Geometry = geometry.Clone(); visibleRegion = lastRecognizedRegion; placementAnchor = 5; recognized = true;
            UpdatePlacement(false); UpdateDraft(); UpdateEnabled(); status.Text = "已取消重新识别，恢复原校准并保留所有手动修正。";
        }
        private void MarkStale(string message)
        {
            revision++; recognized = false; status.ForeColor = Color.FromArgb(76, 88, 84); status.Text = message; UpdateDraft(); UpdateEnabled();
        }
        private void SetBusy(bool value, string message)
        {
            busy = value; if (message != null) { status.ForeColor = Color.FromArgb(76, 88, 84); status.Text = message; } UpdateEnabled();
        }
        private void UpdateEnabled()
        {
            foreach (Control control in busyControls) control.Enabled = !busy;
            imageView.Enabled = !busy; boardView.Enabled = !busy && recognized;
            recognizeButton.Enabled = !busy && source != null;
            confirmButton.Enabled = !busy && recognized;
            undoButton.Enabled = !busy && recognized && undo.Count > 0;
        }
        private void UpdateDraft()
        {
            boardView.SetDraft(stones, uncertain, visibleRegion);
        }
        private void UpdateSummary()
        {
            int black = 0, white = 0, doubts = 0;
            for (int y = 0; y < BoardSize; y++) for (int x = 0; x < BoardSize; x++)
            { if (stones[x, y] == Stone.Black) black++; if (stones[x, y] == Stone.White) white++; if (uncertain[x, y]) doubts++; }
            status.Text = "黑子 " + black + " 颗，白子 " + white + " 颗；" + doubts + " 个位置请核对。与原图对照，点击右侧棋盘修正后确认。";
            UpdateEnabled();
        }
        private void EditPoint(int x, int y, Stone color)
        {
            if (!recognized || busy) return;
            if (x < 0 || y < 0 || x >= BoardSize || y >= BoardSize) throw new ArgumentOutOfRangeException("x");
            if (color != Stone.Empty && color != Stone.Black && color != Stone.White) throw new ArgumentException("棋子颜色无效。");
            if (stones[x, y] == color && !uncertain[x, y]) return;
            undo.Push(new DraftChange { X = x, Y = y, Before = stones[x, y], WasUncertain = uncertain[x, y], WasManual = manual[x, y] });
            stones[x, y] = color; uncertain[x, y] = false; manual[x, y] = true; UpdateDraft(); UpdateSummary();
        }
        private void UndoEdit()
        {
            if (busy || !recognized || undo.Count == 0) return;
            DraftChange change = undo.Pop();
            if (change.Stones != null)
            {
                stones = change.Stones; uncertain = change.Doubts; manual = change.Manual; visibleRegion = change.Region; lastRecognizedRegion = change.Region;
                updating = true; anchorBox.SelectedIndex = change.Anchor; startColumnBox.Value = change.Region.Left + 1; startRowBox.Value = change.Region.Top + 1; updating = false; placementAnchor = change.Anchor; UpdatePlacement(false);
            }
            else { stones[change.X, change.Y] = change.Before; uncertain[change.X, change.Y] = change.WasUncertain; manual[change.X, change.Y] = change.WasManual; }
            UpdateDraft(); UpdateSummary();
        }

        internal GoGame CreateImportedGame()
        {
            if (!recognized || busy) throw new InvalidOperationException("请先完成识别并核对棋子。");
            ValidateLivingGroups();
            StringBuilder sgf = new StringBuilder("(;GM[1]FF[4]CA[UTF-8]SZ[");
            sgf.Append(BoardSize).Append("]KM[").Append(komi.ToString(CultureInfo.InvariantCulture)).Append("]RU[").Append(rules == "japanese" ? "Japanese" : "Chinese").Append("]PL[").Append(playerBox.SelectedIndex == 1 ? "W" : "B").Append(']');
            foreach (Stone color in new Stone[] { Stone.Black, Stone.White })
            {
                bool started = false;
                for (int y = 0; y < BoardSize; y++) for (int x = 0; x < BoardSize; x++) if (stones[x, y] == color)
                { if (!started) { sgf.Append(color == Stone.Black ? "AB" : "AW"); started = true; } sgf.Append('[').Append((char)('a' + x)).Append((char)('a' + y)).Append(']'); }
            }
            bool partial = visibleRegion.Left != 0 || visibleRegion.Top != 0 || visibleRegion.Right != BoardSize - 1 || visibleRegion.Bottom != BoardSize - 1;
            sgf.Append("C[截图／照片摆棋；棋子为初始摆子，原始手数、先前提子和劫争历史未知。");
            if (partial) sgf.Append("局部题图：原图仅提供 ").Append(visibleRegion.Describe(BoardSize)).Append("；其余区域未提供，按空盘补齐，可手动补子。全盘估计仅适用于当前搭建局面。");
            sgf.Append("])");
            return GoGame.FromSgf(sgf.ToString());
        }

        private void ValidateLivingGroups()
        {
            bool[,] visited = new bool[BoardSize, BoardSize];
            int[] dx = new int[] { -1, 1, 0, 0 }, dy = new int[] { 0, 0, -1, 1 };
            for (int y = 0; y < BoardSize; y++) for (int x = 0; x < BoardSize; x++)
            {
                if (stones[x, y] == Stone.Empty || visited[x, y]) continue;
                Stone color = stones[x, y]; bool liberty = false;
                Queue<Point> group = new Queue<Point>(); group.Enqueue(new Point(x, y)); visited[x, y] = true;
                while (group.Count > 0)
                {
                    Point point = group.Dequeue();
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int nx = point.X + dx[direction], ny = point.Y + dy[direction];
                        if (nx < 0 || ny < 0 || nx >= BoardSize || ny >= BoardSize) continue;
                        if (stones[nx, ny] == Stone.Empty) liberty = true;
                        else if (stones[nx, ny] == color && !visited[nx, ny]) { visited[nx, ny] = true; group.Enqueue(new Point(nx, ny)); }
                    }
                }
                if (!liberty) throw new InvalidOperationException(new Move(x, y, color).ToDisplay(BoardSize) + " 附近有一块没有气的" + (color == Stone.Black ? "黑棋" : "白棋") + "，请对照原图修正后再导入。");
            }
        }
        private void ConfirmImport()
        {
            try
            {
                ImportedGame = CreateImportedGame();
                bool partial = visibleRegion.Left != 0 || visibleRegion.Top != 0 || visibleRegion.Right != BoardSize - 1 || visibleRegion.Bottom != BoardSize - 1;
                ImportedRegion = partial ? new StudyRegion(visibleRegion.Left, visibleRegion.Top, visibleRegion.Right, visibleRegion.Bottom) : null;
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { ShowError("还不能导入：" + ex.Message); }
        }
        private void ShowError(string text) { status.Text = text; status.ForeColor = Color.FromArgb(163, 66, 37); }

        internal Task RecognizeForTestAsync() { return RecognizeImageAsync(); }
        internal Task DetectForTestAsync() { return DetectGridAsync(); }
        internal void SetGeometryForTest(BoardImageGeometry value)
        {
            if (source == null) throw new InvalidOperationException("没有图片。");
            value.Validate(source.Width, source.Height);
            if (value.Columns > BoardSize || value.Rows > BoardSize) throw new ArgumentException("可见行列超出棋盘。");
            updating = true; columnsBox.Value = value.Columns; rowsBox.Value = value.Rows; updating = false;
            geometry = value.Clone(); imageView.Geometry = geometry.Clone(); UpdatePlacement(false); MarkStale("校准已更新，请识别。");
        }
        internal void EditPointForTest(int column, int row, Stone color) { EditPoint(column, row, color); }
        internal void UndoForTest() { UndoEdit(); }
        internal void ConfirmForTest() { ConfirmImport(); }
        internal Stone GetDraftForTest(int column, int row) { return stones[column, row]; }
        internal bool IsRecognizedForTest { get { return recognized; } }
        internal Func<Rectangle, Bitmap> CaptureScreenshotForTest;
        internal Action<Form> CaptureOverlayShownForTest;
        internal Task CaptureForTestAsync() { return CaptureScreenAsync(); }

        private async Task CaptureScreenAsync()
        {
            if (busy) return;
            if (!ConfirmDiscardEdits("截取另一张图片会清除当前手动修正。")) return;
            bool captured = false;
            Form owner = Owner;
            double oldOpacity = Opacity, ownerOpacity = owner == null ? 1 : owner.Opacity;
            // A modal form must stay Visible: Hide() ends ShowDialog immediately.
            // Transparency removes both windows from the screenshot while keeping
            // the caller's modal lifetime and owned-window relationship intact.
            Opacity = 0; if (owner != null && !owner.IsDisposed) owner.Opacity = 0;
            try
            {
                await Task.Delay(220);
                if (disposed || IsDisposed) return;
                Rectangle screen = SystemInformation.VirtualScreen;
                using (Bitmap bitmap = CaptureScreenshotForTest == null ? CaptureDesktop(screen) : CaptureScreenshotForTest(screen))
                {
                    if (bitmap == null || bitmap.Width != screen.Width || bitmap.Height != screen.Height) throw new InvalidOperationException("无法取得完整屏幕图片。");
                    using (ImportScreenCapture overlay = new ImportScreenCapture(bitmap, screen))
                    {
                        if (CaptureOverlayShownForTest != null) overlay.Shown += delegate { CaptureOverlayShownForTest(overlay); };
                        if (overlay.ShowDialog() == DialogResult.OK)
                            using (Bitmap crop = bitmap.Clone(overlay.Selection, PixelFormat.Format24bppRgb)) { LoadImage(crop); captured = true; }
                    }
                }
            }
            catch (Exception ex) { if (!disposed) ShowError("截屏未完成：" + ex.Message); }
            finally
            {
                if (owner != null && !owner.IsDisposed) owner.Opacity = ownerOpacity;
                if (!disposed && !IsDisposed) { Opacity = oldOpacity; Activate(); }
            }
            if (!disposed && captured) await DetectGridAsync();
        }

        private static Bitmap CaptureDesktop(Rectangle screen)
        {
            Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bitmap)) g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
    }

    internal sealed class ImageGridControl : Control
    {
        private Bitmap source;
        private BoardImageGeometry geometry;
        private float zoom = 1, fitScale = 1;
        private PointF offset;
        private int dragging = -1;
        private bool panning;
        private Point lastMouse;
        public event EventHandler GeometryChanged;
        public Bitmap Source { get { return source; } set { source = value; dragging = -1; Capture = false; Invalidate(); } }
        public BoardImageGeometry Geometry { get { return geometry; } set { geometry = value; dragging = -1; Capture = false; Invalidate(); } }
        public ImageGridControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(39, 45, 44); TabStop = true;
        }
        public void Fit() { zoom = 1; offset = PointF.Empty; Invalidate(); }
        private RectangleF ImageBounds()
        {
            if (source == null) return RectangleF.Empty;
            fitScale = Math.Min((Width - 20f) / source.Width, (Height - 20f) / source.Height);
            float width = source.Width * fitScale * zoom, height = source.Height * fitScale * zoom;
            return new RectangleF((Width - width) / 2 + offset.X, (Height - height) / 2 + offset.Y, width, height);
        }
        private PointF ToScreen(PointF p)
        {
            RectangleF bounds = ImageBounds(); return new PointF(bounds.X + p.X * fitScale * zoom, bounds.Y + p.Y * fitScale * zoom);
        }
        private PointF ToImage(Point p)
        {
            RectangleF bounds = ImageBounds();
            return new PointF(Math.Max(0, Math.Min(source.Width - 1, (p.X - bounds.X) / (fitScale * zoom))), Math.Max(0, Math.Min(source.Height - 1, (p.Y - bounds.Y) / (fitScale * zoom))));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics;
            if (source == null)
            {
                TextRenderer.DrawText(g, "选择或粘贴一张棋盘图片", Font, ClientRectangle, Color.WhiteSmoke, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return;
            }
            RectangleF bounds = ImageBounds(); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(source, bounds);
            if (geometry == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen line = new Pen(Color.FromArgb(155, 28, 222, 180), 1f))
            {
                try
                {
                    for (int x = 0; x < geometry.Columns; x++) g.DrawLine(line, ToScreen(geometry.Project(x, 0)), ToScreen(geometry.Project(x, geometry.Rows - 1)));
                    for (int y = 0; y < geometry.Rows; y++) g.DrawLine(line, ToScreen(geometry.Project(0, y)), ToScreen(geometry.Project(geometry.Columns - 1, y)));
                }
                catch (ArgumentException) { }
            }
            PointF[] corners = new PointF[] { geometry.TopLeft, geometry.TopRight, geometry.BottomRight, geometry.BottomLeft };
            string[] labels = new string[] { "左上", "右上", "右下", "左下" };
            for (int n = 0; n < 4; n++)
            {
                PointF p = ToScreen(corners[n]);
                using (Brush fill = new SolidBrush(n == dragging ? Color.Orange : Color.FromArgb(245, 255, 255, 255))) g.FillEllipse(fill, p.X - 7, p.Y - 7, 14, 14);
                using (Pen border = new Pen(Color.FromArgb(20, 135, 111), 2f)) g.DrawEllipse(border, p.X - 7, p.Y - 7, 14, 14);
                Rectangle text = new Rectangle((int)Math.Max(0, Math.Min(Width - 38, p.X - 18)), (int)Math.Max(0, Math.Min(Height - 23, p.Y - 29)), 38, 22);
                using (Brush back = new SolidBrush(Color.FromArgb(205, 20, 57, 49))) g.FillRectangle(back, text);
                TextRenderer.DrawText(g, labels[n], Font, text, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus(); if (source == null || geometry == null) return;
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) { panning = true; lastMouse = e.Location; Capture = true; return; }
            if (e.Button != MouseButtons.Left) return;
            PointF[] corners = new PointF[] { geometry.TopLeft, geometry.TopRight, geometry.BottomRight, geometry.BottomLeft };
            for (int n = 0; n < 4; n++) { PointF p = ToScreen(corners[n]); if (Math.Abs(p.X - e.X) <= 18 && Math.Abs(p.Y - e.Y) <= 18) { dragging = n; Capture = true; Invalidate(); return; } }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (panning) { offset.X += e.X - lastMouse.X; offset.Y += e.Y - lastMouse.Y; lastMouse = e.Location; Invalidate(); return; }
            if (dragging < 0 || source == null || geometry == null) return;
            PointF point = ToImage(e.Location);
            if (dragging == 0) geometry.TopLeft = point;
            if (dragging == 1) geometry.TopRight = point;
            if (dragging == 2) geometry.BottomRight = point;
            if (dragging == 3) geometry.BottomLeft = point;
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); bool changed = dragging >= 0; dragging = -1; panning = false; Capture = false; Invalidate();
            if (changed && GeometryChanged != null) GeometryChanged(this, EventArgs.Empty);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
            {
                bool changed = dragging >= 0; dragging = -1; panning = false;
                if (changed && GeometryChanged != null) GeometryChanged(this, EventArgs.Empty);
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e); if (source == null) return;
            ImageBounds(); float newZoom = Math.Max(.5f, Math.Min(8f, zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f)));
            float factor = newZoom / zoom; offset.X = e.X - Width / 2f - (e.X - Width / 2f - offset.X) * factor; offset.Y = e.Y - Height / 2f - (e.Y - Height / 2f - offset.Y) * factor;
            zoom = newZoom; Invalidate();
        }
        protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); Fit(); }
    }

    internal sealed class ImportDraftBoard : Control
    {
        private Stone[,] stones;
        private bool[,] uncertain;
        private StudyRegion region;
        private float left, top, step;
        public event EventHandler<BoardPointEventArgs> PointClicked;
        public event EventHandler<BoardPointEventArgs> EraseClicked;
        public ImportDraftBoard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(242, 240, 234); Cursor = Cursors.Hand;
        }
        public void SetDraft(Stone[,] value, bool[,] doubts, StudyRegion visible) { stones = value; uncertain = doubts; region = visible; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (stones == null || Width < 90 || Height < 90) return;
            int size = stones.GetLength(0); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float side = Math.Min(Width - 18, Height - 18), margin = Math.Max(23f, side * .065f);
            float woodLeft = (Width - side) / 2, woodTop = (Height - side) / 2;
            left = woodLeft + margin; top = woodTop + margin; step = (side - margin * 2) / (size - 1);
            using (Brush wood = new LinearGradientBrush(new RectangleF(woodLeft, woodTop, side, side), Color.FromArgb(233, 193, 130), Color.FromArgb(216, 171, 100), 30f)) g.FillRectangle(wood, woodLeft, woodTop, side, side);
            if (region != null)
                using (Brush unknown = new SolidBrush(Color.FromArgb(85, 150, 150, 150)))
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (!region.Contains(x, y)) g.FillRectangle(unknown, left + x * step - step * .48f, top + y * step - step * .48f, step * .96f, step * .96f);
            using (Pen line = new Pen(Color.FromArgb(111, 83, 44), .8f))
                for (int n = 0; n < size; n++) { g.DrawLine(line, left + n * step, top, left + n * step, top + (size - 1) * step); g.DrawLine(line, left, top + n * step, left + (size - 1) * step, top + n * step); }
            using (Font labels = new Font(Font.FontFamily, Math.Max(7, Math.Min(10, step * .36f)), GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                for (int n = 0; n < size; n++)
                {
                    g.DrawString(((char)('A' + n)).ToString(), labels, Brushes.Black, left + n * step, top - margin * .5f, format);
                    g.DrawString((n + 1).ToString(), labels, Brushes.Black, left - margin * .5f, top + n * step, format);
                }
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float cx = left + x * step, cy = top + y * step, diameter = step * .89f;
                if (stones[x, y] != Stone.Empty)
                {
                    RectangleF stone = new RectangleF(cx - diameter / 2, cy - diameter / 2, diameter, diameter);
                    using (Brush fill = new LinearGradientBrush(stone, stones[x, y] == Stone.Black ? Color.FromArgb(80, 83, 83) : Color.White, stones[x, y] == Stone.Black ? Color.FromArgb(12, 15, 15) : Color.FromArgb(213, 216, 215), 55f)) g.FillEllipse(fill, stone);
                    using (Pen outline = new Pen(Color.FromArgb(115, 91, 75, 51), .7f)) g.DrawEllipse(outline, stone);
                }
                if (uncertain != null && uncertain[x, y])
                    using (Pen doubt = new Pen(Color.FromArgb(226, 112, 16), Math.Max(1.5f, step * .065f))) g.DrawEllipse(doubt, cx - step * .44f, cy - step * .44f, step * .88f, step * .88f);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!Enabled || stones == null || step <= 0) return;
            int x = (int)Math.Round((e.X - left) / step), y = (int)Math.Round((e.Y - top) / step);
            if (x < 0 || y < 0 || x >= stones.GetLength(0) || y >= stones.GetLength(1) || Math.Abs(e.X - (left + x * step)) > step * .49f || Math.Abs(e.Y - (top + y * step)) > step * .49f) return;
            EventHandler<BoardPointEventArgs> handler = e.Button == MouseButtons.Right ? EraseClicked : e.Button == MouseButtons.Left ? PointClicked : null;
            if (handler != null) handler(this, new BoardPointEventArgs(x, y));
        }
    }

    internal sealed class ImportScreenCapture : Form
    {
        private readonly Bitmap screenshot;
        private Point begin, end;
        private bool selecting;
        public Rectangle Selection { get; private set; }
        public ImportScreenCapture(Bitmap bitmap, Rectangle screen)
        {
            screenshot = bitmap; Name = "ImportScreenCapture"; AutoScaleMode = AutoScaleMode.None; Bounds = screen; StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; Cursor = Cursors.Cross; DoubleBuffered = true; KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(screenshot, 0, 0);
            Rectangle selected = CurrentSelection();
            using (Brush dim = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
            {
                if (!selecting) e.Graphics.FillRectangle(dim, ClientRectangle);
                else
                {
                    e.Graphics.FillRectangle(dim, 0, 0, Width, selected.Top); e.Graphics.FillRectangle(dim, 0, selected.Bottom, Width, Height - selected.Bottom);
                    e.Graphics.FillRectangle(dim, 0, selected.Top, selected.Left, selected.Height); e.Graphics.FillRectangle(dim, selected.Right, selected.Top, Width - selected.Right, selected.Height);
                }
            }
            if (selecting) using (Pen pen = new Pen(Color.FromArgb(44, 242, 187), 2f)) e.Graphics.DrawRectangle(pen, selected);
            TextRenderer.DrawText(e.Graphics, "拖动框选棋盘图片；Esc 取消", SystemFonts.MessageBoxFont, new Rectangle(20, 20, Math.Max(400, Width - 40), 32), Color.White, Color.FromArgb(25, 64, 54), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        private Rectangle CurrentSelection()
        {
            return Rectangle.FromLTRB(Math.Max(0, Math.Min(begin.X, end.X)), Math.Max(0, Math.Min(begin.Y, end.Y)), Math.Min(Width, Math.Max(begin.X, end.X)), Math.Min(Height, Math.Max(begin.Y, end.Y)));
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { selecting = true; begin = end = e.Location; Capture = true; Invalidate(); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (selecting) { end = e.Location; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (!selecting || e.Button != MouseButtons.Left) return;
            end = e.Location; Selection = CurrentSelection(); Capture = false;
            if (Selection.Width < 24 || Selection.Height < 24) { selecting = false; Invalidate(); return; }
            DialogResult = DialogResult.OK; Close();
        }
    }
}
