using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    internal static class RedesignSmoke
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int assertions;
        private static void Check(bool valid, string message) { assertions++; if (!valid) throw new Exception("改版界面验收：" + message); }
        private static T Find<T>(Control root, string name) where T : Control { return (T)root.Controls.Find(name, true)[0]; }
        private static object Field(object owner, string name) { return owner.GetType().GetField(name, Private).GetValue(owner); }
        private static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        private static void Save(Control c, string path) { c.PerformLayout(); using (var bitmap = new Bitmap(c.Width, c.Height)) { c.DrawToBitmap(bitmap, new Rectangle(Point.Empty, c.Size)); bitmap.Save(path); } }
        public static async Task<string> RunAsync(MainForm main, string folder)
        {
            assertions = 0;
            string original = main.CurrentGame.ToSgf(); int originalPosition = main.CurrentGame.Position;
            Size oldSize = main.Size;
            var automatic = Find<CheckBox>(main, "AutomaticReply"); automatic.Checked = false;
            Find<ComboBox>(main, "SearchLevel").SelectedIndex = 0;
            Call(main, "ShowWorkspace", "play");
            main.LoadGame(GoGame.FromSgf("(;SZ[9]KM[7.5];B[cc];W[gg])"));
            Find<ComboBox>(main, "HumanColor").SelectedIndex = 1;
            string before = main.CurrentGame.ToSgf();
            Find<Button>(main, "NavReview").PerformClick();
            Check(main.CurrentGame.ToSgf() == before && (bool)Field(main, "reviewMode"), "复盘导航改变棋局");
            var board = Find<BoardControl>(main, "GoBoard"); Check(board.AllowInput && !automatic.Enabled, "复盘未允许双方试下或暂停自动应手");
            Call(main, "OnBoardPoint", board, new BoardPointEventArgs(0, 0));
            await Task.Delay(100);
            Check(main.CurrentGame.Position == 3 && main.CurrentGame.Moves[2].Color == Stone.Black, "复盘黑方试下错误或自动应手未暂停");
            Find<Button>(main, "Undo").PerformClick(); Check(main.CurrentGame.Position == 2, "复盘悔棋不是单手");
            before = main.CurrentGame.ToSgf(); // Undo preserves the trial line for forward review.
            Find<Button>(main, "NavSettings").PerformClick();
            Check(Find<Panel>(main, "SettingsWorkspace").Visible && !Find<Panel>(main, "PlayWorkspace").Visible, "设置导航没有切换页面");
            Check(main.CurrentGame.ToSgf() == before, "进入设置丢失棋局");
            Save(main, Path.Combine(folder, "desktop-settings.png"));
            Find<Button>(main, "NavPlay").PerformClick();
            Check(Find<Panel>(main, "PlayWorkspace").Visible && automatic.Enabled && main.CurrentGame.Position == 2, "返回对局状态错误");

            main.LoadGame(GoGame.FromSgf("(;SZ[9]KM[7.5];B[cc];W[gg])"));

            await (Task)Call(main, "AnalyzeAsync", false, false);
            var chart = Find<WinrateChart>(main, "WinrateHistory");
            Check(chart.Points.Length == 3 && chart.Points[2].HasValue && !chart.Points[1].HasValue, "真实分析未进入历史或未分析手数被填充");
            await (Task)Call(main, "AnalyzeAsync", true, false);
            Check(main.CurrentGame.Position == 3 && chart.Points[2].HasValue && !chart.Points[3].HasValue, "AI落子前后历史错一手");
            Check(Field(Find<CandidateTable>(main, "CandidateTable"), "analysis") == null, "实际落子后还展示过期候选");
            await (Task)Call(main, "AnalyzeAsync", false, false);
            Check(chart.Points[3].HasValue, "分支末端分析未记录");
            Move previous = main.CurrentGame.Moves[2]; Call(main, "Navigate", 2); Find<Button>(main, "NavReview").PerformClick();
            int x = previous.X == 0 && previous.Y == 1 ? 1 : 0;
            Call(main, "OnBoardPoint", board, new BoardPointEventArgs(x, 1));
            Check(main.CurrentGame.Position == 3 && !chart.Points[3].HasValue, "新分支仍显示旧分支胜率");
            Call(main, "OnStudyRegionSelected", board, new StudyRegionEventArgs(new StudyRegion(0, 0, 3, 3)));
            await (Task)Call(main, "AnalyzeAsync", false, false);
            Check(!chart.Points[3].HasValue && board.Analysis.Region != null, "局部分析进入全局曲线");
            await (Task)Call(main, "CompareGloballyAsync");
            Check(chart.Points[3].HasValue && board.StudyRegion != null, "全局对比未记入真实全盘历史或丢失局部框");
            Find<Button>(main, "NewGame").PerformClick();
            foreach (double? value in chart.Points) Check(!value.HasValue, "新对局沿用旧胜率");
            await (Task)Call(main, "AnalyzeAsync", false, false);
            Check(chart.Points[0].HasValue, "新盘全局分析未记录");
            Call(main, "ApplyEngineSettings", EngineSettings.CreateCpu());
            Check(!chart.Points[0].HasValue, "换引擎或模型后没有清空历史");

            main.LoadGame(GoGame.FromSgf("(;SZ[19]KM[7.5]AB[pc]AW[fp]PL[B])"));
            Find<Button>(main, "NavPlay").PerformClick();
            await (Task)Call(main, "AnalyzeAsync", false, false);
            main.Size = oldSize; Call(main, "UpdateUi");
            main.CaptureScreenshot(Path.Combine(folder, "desktop-preview.png"));
            main.Size = new Size(1500, 1260); Call(main, "UpdateUi");
            main.CaptureScreenshot(Path.Combine(folder, "design-target-main.png"));
            main.Size = oldSize; Call(main, "UpdateUi");
            Check(UiTheme.ResourceImage("brand.png") != null, "品牌图未嵌入程序");
            foreach (Control card in (IEnumerable)Field(main, "rightCards"))
            {
                Check(card.Padding.Top < 100 && card.Padding.Left < 50, "高DPI卡片留白重复缩放");
                Check(card.Controls[0].Bounds.Top >= card.Padding.Top, "卡片内容覆盖标题");
            }
            Check(!Find<Panel>(main, "AnalysisSidebar").HorizontalScroll.Visible, "侧栏出现横向滚动");
            using (Graphics g = main.CreateGraphics())
            {
                foreach (string name in new string[] { "NewGame", "AiPlay", "ImportBoardImage" })
                {
                    var button = Find<Button>(main, name);
                    int icon = (int)(24 * g.DpiX / 96f);
                    int text = TextRenderer.MeasureText(g, button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    Check(button.Width - button.Padding.Horizontal >= text + icon, "常用按钮文字被裁切：" + name);
                }
            }
            bool wasUpdating = (bool)Field(main, "updating");
            var jump = Find<NumericUpDown>(main, "JumpToMove"); jump.Value = 0;
            Check(main.CurrentGame.Position == 0 && (bool)Field(main, "updating") == wasUpdating, "跳手控件或更新锁错误");
            Find<Button>(main, "ShowVariation").PerformClick();
            Check(Find<Label>(main, "AnalysisVariation").Visible, "后续走法展开失败");
            Find<Button>(main, "ShowVariation").PerformClick();
            Check(!Find<Label>(main, "AnalysisVariation").Visible, "后续走法收起失败");
            main.Size = new Size(1000, 740); Call(main, "UpdateUi");
            main.CaptureScreenshot(Path.Combine(folder, "desktop-small.png"));
            foreach (string name in new string[] { "Pass", "Undo", "SaveSgf", "ImportImageShortcut" })
            {
                var button = Find<Button>(main, name); Rectangle bounds = main.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                Check(main.ClientRectangle.Contains(bounds), "最小窗口常用操作被遮挡：" + name);
            }
            main.Size = oldSize;
            using (var image = ImageImportSmoke.Fixture())
            using (var dialog = new ImageImportForm(7.5, "chinese-ogs"))
            {
                dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.Show();
                Size importSize = dialog.Size; dialog.Size = new Size(1500, 1260);
                Save(dialog, Path.Combine(folder, "image-import-empty.png")); dialog.Size = importSize;
                Find<ComboBox>(dialog, "ImportBoardSize").SelectedIndex = 2;
                dialog.LoadImage(image);
                dialog.SetGeometryForTest(new BoardImageGeometry { Columns = 9, Rows = 9, TopLeft = new PointF(55, 55), TopRight = new PointF(515, 55), BottomRight = new PointF(515, 515), BottomLeft = new PointF(55, 515) });
                await dialog.RecognizeForTestAsync();
                Check(Find<Control>(dialog, "ImportSteps") != null && Find<Label>(dialog, "ImportCounts").Text.Contains("2"), "识别步骤或真实数量缺失");
                Save(dialog, Path.Combine(folder, "image-import-preview.png"));
                Find<Button>(dialog, "ImportAdvanced").PerformClick();
                Check(Find<Panel>(dialog, "ImportAdvancedPanel").Visible, "高级局部参数不能展开");
                dialog.Size = new Size(1000, 720); Save(dialog, Path.Combine(folder, "image-import-small.png"));
                Button confirm = Find<Button>(dialog, "ImportConfirm");
                Check(dialog.ClientRectangle.Contains(dialog.RectangleToClient(confirm.RectangleToScreen(confirm.ClientRectangle))), "识别页导入按钮不可见");
                dialog.Close();
            }
            main.LoadGame(GoGame.FromSgf(original)); main.CurrentGame.Goto(originalPosition);
            string report = "改版界面：PASS（" + assertions + " 项；导航保持局面、双边复盘、真实曲线与分支/局部/模型隔离、候选时效、DPI留白、按钮文字、后续走法展开、识别步骤及最小窗口操作）。";
            File.WriteAllText(Path.Combine(folder, "redesign-test.txt"), "PASS\n" + report + "\n", new System.Text.UTF8Encoding(true));
            return report;
        }
    }
}
