using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    internal static class ImageImportSmoke
    {
        private static int assertions;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool ok, string message)
        {
            assertions++;
            if (!ok) throw new InvalidOperationException("图片导入界面检查：" + message);
        }
        private static T Find<T>(Control form, string name) where T : Control
        { return (T)form.Controls.Find(name, true)[0]; }
        private static object Field(object obj, string name) { return obj.GetType().GetField(name, Private).GetValue(obj); }
        private static object Call(object obj, string name, params object[] args)
        { return obj.GetType().GetMethod(name, Private).Invoke(obj, args); }
        private static void Paint(Control c, string path = null)
        {
            c.PerformLayout();
            using (Bitmap bitmap = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(bitmap, new Rectangle(Point.Empty, c.Size));
                if (path != null) bitmap.Save(path, ImageFormat.Png);
            }
        }
        internal static Bitmap Fixture()
        {
            Bitmap bitmap = new Bitmap(570, 570);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(222, 180, 110));
                for (int n = 0; n < 9; n++)
                {
                    float v = 55 + n * 57.5f;
                    g.DrawLine(Pens.Black, v, 55, v, 515); g.DrawLine(Pens.Black, 55, v, 515, v);
                }
                g.FillEllipse(Brushes.Black, 281, 281, 8, 8);
                int[,] points = { { 0, 0, 1 }, { 2, 3, 2 }, { 4, 5, 1 }, { 8, 8, 2 } };
                for (int i = 0; i < 4; i++)
                    g.FillEllipse(points[i, 2] == 1 ? Brushes.Black : Brushes.White,
                        55 + points[i, 0] * 57.5f - 25, 55 + points[i, 1] * 57.5f - 25, 50, 50);
            }
            return bitmap;
        }
        private static BoardImageGeometry Geometry()
        {
            return new BoardImageGeometry { Columns = 9, Rows = 9, TopLeft = new PointF(55, 55),
                TopRight = new PointF(515, 55), BottomRight = new PointF(515, 515), BottomLeft = new PointF(55, 515) };
        }
        private static void ShowOffscreen(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            dialog.ShowInTaskbar = false; dialog.Show(); dialog.PerformLayout();
        }
        public static async Task<string> RunAsync(MainForm main, string folder)
        {
            assertions = 0;
            string original = main.CurrentGame.ToSgf(); int originalPosition = main.CurrentGame.Position;
            Check(Find<Button>(main, "ImportBoardImage").Enabled, "主界面缺少图片入口");
            using (var picture = Fixture())
            using (var dialog = new ImageImportForm(6.5, "japanese"))
            {
                ShowOffscreen(dialog);
                Check(!Find<Button>(dialog, "ImportConfirm").Enabled, "未识别就允许导入");
                bool rejected = false;
                try { dialog.CreateImportedGame(); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "空白草稿未拒绝");
                Find<ComboBox>(dialog, "ImportBoardSize").SelectedIndex = 2;
                dialog.LoadImage(picture); dialog.SetGeometryForTest(Geometry());
                await dialog.RecognizeForTestAsync();
                Check(dialog.IsRecognizedForTest && Find<Button>(dialog, "ImportConfirm").Enabled, "识别后仍不能导入");
                for (int y = 0; y < 9; y++) for (int x = 0; x < 9; x++)
                {
                    Stone expected = (x == 0 && y == 0) || (x == 4 && y == 5) ? Stone.Black :
                        (x == 2 && y == 3) || (x == 8 && y == 8) ? Stone.White : Stone.Empty;
                    Check(dialog.GetDraftForTest(x, y) == expected, "草稿点位 " + x + "," + y);
                }
                Check(main.CurrentGame.ToSgf() == original, "识别预览修改了原棋局");
                Control preview = Find<Control>(dialog, "ImportBoardPreview"); Paint(preview);
                float left = (float)Field(preview, "left"), top = (float)Field(preview, "top"), step = (float)Field(preview, "step");
                Find<RadioButton>(dialog, "ImportWhiteTool").Checked = true;
                Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, (int)Math.Round(left + step), (int)Math.Round(top + step), 0));
                Check(dialog.GetDraftForTest(1, 1) == Stone.White, "实际点击未放白子");
                Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, (int)Math.Round(left + 2 * step), (int)Math.Round(top + step), 0));
                Check(dialog.GetDraftForTest(2, 1) == Stone.White, "修正不支持连续同色");
                Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, (int)Math.Round(left + step), (int)Math.Round(top + step), 0));
                Check(dialog.GetDraftForTest(1, 1) == Stone.Empty, "右键未擦除");
                Find<Button>(dialog, "ImportUndo").PerformClick();
                Check(dialog.GetDraftForTest(1, 1) == Stone.White, "撤销未还原被擦的棋子");
                Find<Button>(dialog, "ImportUndo").PerformClick(); Find<Button>(dialog, "ImportUndo").PerformClick();
                Check(dialog.GetDraftForTest(1, 1) == Stone.Empty && dialog.GetDraftForTest(2, 1) == Stone.Empty, "连续撤销失败");
                dialog.EditPointForTest(1, 0, Stone.White); dialog.EditPointForTest(0, 1, Stone.White);
                bool invalidSetupRejected = false;
                try { dialog.CreateImportedGame(); } catch (InvalidOperationException) { invalidSetupRejected = true; }
                Check(invalidSetupRejected, "无气棋块未提示修正");
                dialog.UndoForTest(); dialog.UndoForTest();
                Paint(dialog, Path.Combine(folder, "image-import-preview.png"));
                dialog.Size = new Size(1000, 720); Paint(dialog, Path.Combine(folder, "image-import-small.png"));
                Button confirm = Find<Button>(dialog, "ImportConfirm");
                Rectangle buttonBounds = dialog.RectangleToClient(confirm.RectangleToScreen(confirm.ClientRectangle));
                Check(dialog.ClientRectangle.Contains(buttonBounds), "最小窗口导入按钮被裁切");

                Control originalView = Find<Control>(dialog, "ImportImagePreview"); Paint(originalView);
                PointF handle = (PointF)Call(originalView, "ToScreen", Geometry().TopLeft);
                Call(originalView, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, (int)handle.X, (int)handle.Y, 0));
                Call(originalView, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, (int)handle.X + 3, (int)handle.Y + 2, 0));
                Call(originalView, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, (int)handle.X + 3, (int)handle.Y + 2, 0));
                Check(!dialog.IsRecognizedForTest && !confirm.Enabled, "调整四角后仍能导入过时结果");

                Find<ComboBox>(dialog, "ImportBoardSize").SelectedIndex = 0;
                var partial = new BoardImageGeometry { Columns = 3, Rows = 4, TopLeft = new PointF(170, 170),
                    TopRight = new PointF(285, 170), BottomRight = new PointF(285, 342.5f), BottomLeft = new PointF(170, 342.5f) };
                dialog.SetGeometryForTest(partial);
                Find<ComboBox>(dialog, "ImportAnchor").SelectedIndex = 1;
                await dialog.RecognizeForTestAsync();
                Check(dialog.GetDraftForTest(16, 1) == Stone.White && dialog.GetDraftForTest(18, 3) == Stone.Black,
                    "右上局部未放回十九路正确位置");
                dialog.EditPointForTest(16, 1, Stone.Black);
                dialog.EditPointForTest(0, 18, Stone.White);
                Find<ComboBox>(dialog, "ImportAnchor").SelectedIndex = 3;
                Check(dialog.IsRecognizedForTest && dialog.GetDraftForTest(16, 16) == Stone.Black &&
                    dialog.GetDraftForTest(18, 18) == Stone.Black && dialog.GetDraftForTest(0, 18) == Stone.White,
                    "改变角边丢失区域内修正或灰区补子");
                dialog.UndoForTest();
                Check(dialog.GetDraftForTest(16, 1) == Stone.Black && dialog.GetDraftForTest(0, 18) == Stone.White &&
                    Find<ComboBox>(dialog, "ImportAnchor").SelectedIndex == 1, "撤销区域移动丢失修正或原位置");
                dialog.EditPointForTest(16, 1, Stone.White);
                dialog.EditPointForTest(18, 18, Stone.White);
                Find<ComboBox>(dialog, "ImportAnchor").SelectedIndex = 3;
                Check(Find<ComboBox>(dialog, "ImportAnchor").SelectedIndex == 1 &&
                    dialog.GetDraftForTest(18, 18) == Stone.White && dialog.GetDraftForTest(18, 3) == Stone.Black,
                    "区域移动冲突覆盖了灰区手动补子");
                dialog.UndoForTest();
                Find<ComboBox>(dialog, "ImportNextPlayer").SelectedIndex = 1;
                GoGame imported = dialog.CreateImportedGame();
                Check(imported.Size == 19 && imported.Komi == 6.5 && imported.Rules == "japanese" && imported.ToPlay == Stone.White,
                    "完整盘大小、规则或白先丢失");
                Check(imported.Position == 0 && imported.Moves.Count == 0 && imported.InitialStones.Count == 3,
                    "图片摆子被当成历史落子");
                Check(imported.ToSgf().Contains("其余区域未提供") && imported.ToSgf().Contains("AB[") && imported.ToSgf().Contains("AW["),
                    "局部未知说明或黑白起始摆子丢失");
                GoGame reloaded = GoGame.FromSgf(imported.ToSgf());
                Check(reloaded.Board[0, 18] == Stone.White && reloaded.Board[18, 3] == Stone.Black && reloaded.ToPlay == Stone.White,
                    "导入局面SGF往返失败");
                Paint(dialog, Path.Combine(folder, "image-import-partial.png"));
                dialog.ConfirmForTest();
                Check(dialog.DialogResult == DialogResult.OK && dialog.ImportedRegion.Left == 16 && dialog.ImportedRegion.Bottom == 3,
                    "确认未保留局部研究范围");
                main.ImportImagePosition(dialog.ImportedGame, dialog.ImportedRegion);
            }
            Check(main.CurrentGame.Position == 0 && !Find<CheckBox>(main, "AutomaticReply").Checked &&
                Find<ComboBox>(main, "HumanColor").SelectedIndex == 2, "导入后未暂停自动应手");
            int[,] numbers = MoveNumbers.Build(main.CurrentGame);
            foreach (Move stone in main.CurrentGame.InitialStones) Check(numbers[stone.X, stone.Y] == 0, "预摆棋子出现虚假手数");
            Find<ComboBox>(main, "SearchLevel").SelectedIndex = 0;
            await (Task)Call(main, "AnalyzeAsync", false, false);
            var mainBoard = Find<BoardControl>(main, "GoBoard");
            Check(mainBoard.Analysis != null && mainBoard.Analysis.Region != null && main.CurrentGame.Position == 0,
                "图片局面未进入真实局部分析或分析自动落子");
            Move best = GoGame.ParseGtp(mainBoard.Analysis.BestMove, main.CurrentGame.ToPlay, 19);
            Check(mainBoard.StudyRegion.Contains(best.X, best.Y), "图片研究推荐跑出区域");
            Check(Find<Label>(main, "AnalysisVariation").Text.StartsWith("下在 A 后的预测走法"), "后续走法未改为易懂标签");

            using (var image = Fixture())
            using (var dialog = new ImageImportForm(7.5, "chinese-ogs"))
            {
                ShowOffscreen(dialog); dialog.LoadImage(image); dialog.SetGeometryForTest(Geometry());
                Task pending = dialog.RecognizeForTestAsync(); dialog.Close(); await pending;
                Check(dialog.ImportedGame == null, "关闭识别窗口后仍自动导入");
            }
            main.LoadGame(GoGame.FromSgf(original)); main.CurrentGame.Goto(originalPosition);
            using (var timer = new Timer { Interval = 70 })
            {
                timer.Tick += delegate
                {
                    foreach (Form open in Application.OpenForms)
                    {
                        var dialog = open as ImageImportForm;
                        if (dialog == null) continue;
                        timer.Stop(); Find<Button>(dialog, "ImportCancel").PerformClick(); break;
                    }
                };
                timer.Start(); Find<Button>(main, "ImportBoardImage").PerformClick();
            }
            Check(main.CurrentGame.ToSgf() == original && main.CurrentGame.Position == originalPosition, "主入口取消破坏原棋局");
            using (Bitmap image = Fixture())
            using (var overlay = new ImportScreenCapture(image, new Rectangle(-20000, -20000, image.Width, image.Height)))
            {
                overlay.ShowInTaskbar = false; overlay.Show();
                Call(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 300, 300, 0));
                Call(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 100, 120, 0));
                Call(overlay, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 100, 120, 0));
                Check(overlay.DialogResult == DialogResult.OK && overlay.Selection == new Rectangle(100, 120, 200, 180),
                    "屏幕反向框选或负屏幕坐标失败");
            }
            using (Bitmap image = Fixture())
            using (var overlay = new ImportScreenCapture(image, new Rectangle(-20000, -20000, image.Width, image.Height)))
            {
                overlay.ShowInTaskbar = false; overlay.Show();
                Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Escape));
                Check(overlay.DialogResult == DialogResult.Cancel, "屏幕框选Esc未取消");
            }
            string captureChecks = await ImageImportCaptureSmoke.RunAsync();
            return "图片导入：PASS（" + assertions + " 项；逐点预览、实际点击修正/擦除/撤销、透视校准失效、局部定位、白先、SGF、取消隔离、真实CPU分析、框选/Esc）。" + Environment.NewLine + captureChecks;
        }
    }
}
