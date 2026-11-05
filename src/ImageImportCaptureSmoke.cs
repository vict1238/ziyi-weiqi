using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    /// <summary>Exercises the real modal/capture lifetime with generated screen pixels.</summary>
    internal static class ImageImportCaptureSmoke
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int assertions;
        private static void Check(bool value, string message)
        {
            assertions++; if (!value) throw new InvalidOperationException("图片截屏／修正回归：" + message);
        }
        private static T Find<T>(Control control, string name) where T : Control
        { return (T)control.Controls.Find(name, true)[0]; }
        private static object Field(object obj, string name)
        { return obj.GetType().GetField(name, Private).GetValue(obj); }
        private static void Mouse(Control control, string method, MouseEventArgs args)
        { typeof(Control).GetMethod(method, Private).Invoke(control, new object[] { args }); }

        internal static async Task<string> RunAsync()
        {
            assertions = 0;
            await CheckDraftPreservationAsync();
            using (Form owner = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, Opacity = .88 })
            {
                owner.Show();
                CheckModalCapture(owner, false);
                CheckModalCapture(owner, true);
                owner.Close();
            }
            return "截屏完整模态与修正保留：PASS（" + assertions + " 项；合成屏幕、实际ESC与鼠标选区、模态等待、透明度恢复、区域移动／冲突／撤销、清除确认）。";
        }

        private static async Task CheckDraftPreservationAsync()
        {
            using (Bitmap image = new Bitmap(500, 500))
            using (ImageImportForm dialog = new ImageImportForm(7.5, "chinese-ogs"))
            {
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.BurlyWood);
                    for (int n = 0; n < 9; n++) { float v = 50 + n * 50; graphics.DrawLine(Pens.Black, v, 50, v, 450); graphics.DrawLine(Pens.Black, 50, v, 450, v); }
                    graphics.FillEllipse(Brushes.Black, 78, 78, 44, 44);
                    graphics.FillEllipse(Brushes.White, 128, 128, 44, 44);
                }
                dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.ShowInTaskbar = false;
                dialog.Show(); dialog.LoadImage(image);
                var calibration = new BoardImageGeometry { Columns = 3, Rows = 3, TopLeft = new PointF(50, 50), TopRight = new PointF(150, 50), BottomRight = new PointF(150, 150), BottomLeft = new PointF(50, 150) };
                dialog.SetGeometryForTest(calibration); await dialog.RecognizeForTestAsync();
                Check(dialog.IsRecognizedForTest, "修正测试未能识别");
                dialog.EditPointForTest(0, 0, Stone.White); dialog.EditPointForTest(18, 18, Stone.Black);
                Find<ComboBox>(dialog, "ImportAnchor").SelectedIndex = 1;
                Check(dialog.IsRecognizedForTest && dialog.GetDraftForTest(16, 0) == Stone.White && dialog.GetDraftForTest(0, 0) == Stone.Empty, "移动区域丢失区域内修正");
                Check(dialog.GetDraftForTest(18, 18) == Stone.Black, "移动区域丢失原图外补子");
                dialog.UndoForTest();
                Check(dialog.GetDraftForTest(0, 0) == Stone.White && dialog.GetDraftForTest(16, 0) == Stone.Empty && dialog.GetDraftForTest(18, 18) == Stone.Black, "撤销区域移动未恢复草稿");
                dialog.EditPointForTest(16, 0, Stone.Black); Find<ComboBox>(dialog, "ImportAnchor").SelectedIndex = 1;
                Check(Find<NumericUpDown>(dialog, "ImportStartColumn").Value == 1 && dialog.GetDraftForTest(0, 0) == Stone.White && dialog.GetDraftForTest(16, 0) == Stone.Black, "冲突移动覆盖原图外修正");
                Check(Find<Label>(dialog, "ImportStatus").Text.Contains("冲突"), "冲突没有明确提示");
                dialog.UndoForTest();
                int prompts = 0;
                dialog.ConfirmDiscardCorrectionsForTest = delegate(string message) { prompts++; return false; };
                dialog.SetGeometryForTest(calibration); await dialog.RecognizeForTestAsync();
                Check(prompts == 1 && dialog.IsRecognizedForTest && dialog.GetDraftForTest(0, 0) == Stone.White && dialog.GetDraftForTest(18, 18) == Stone.Black, "取消重新识别未恢复校准与手动修正");
                Find<ComboBox>(dialog, "ImportBoardSize").SelectedIndex = 2;
                Check(prompts == 2 && Find<ComboBox>(dialog, "ImportBoardSize").SelectedIndex == 0 && dialog.GetDraftForTest(18, 18) == Stone.Black, "取消改盘大小未保留草稿");
                dialog.ConfirmDiscardCorrectionsForTest = delegate(string message) { prompts++; return true; };
                await dialog.RecognizeForTestAsync();
                Check(prompts == 3 && dialog.GetDraftForTest(0, 0) == Stone.Empty && dialog.GetDraftForTest(18, 18) == Stone.Empty, "明确确认后未重新识别");
                dialog.Close();
            }
        }

        private static void CheckModalCapture(Form owner, bool select)
        {
            bool completed = false; Exception failure = null;
            using (ImageImportForm dialog = new ImageImportForm(7.5, "chinese-ogs"))
            {
                dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.ShowInTaskbar = false; dialog.Opacity = .91;
                using (Bitmap seed = new Bitmap(400, 400)) { using (Graphics graphics = Graphics.FromImage(seed)) graphics.Clear(Color.BurlyWood); dialog.LoadImage(seed); }
                object previous = Field(dialog, "source");
                dialog.CaptureScreenshotForTest = delegate(Rectangle bounds)
                {
                    Check(dialog.Visible && dialog.Modal && !dialog.IsDisposed, "截屏前模态窗已结束");
                    Check(owner.Visible && owner.Opacity == 0 && dialog.Opacity == 0, "截屏期间窗口未保持透明且可见");
                    Bitmap screen = new Bitmap(bounds.Width, bounds.Height);
                    using (Graphics graphics = Graphics.FromImage(screen))
                    {
                        graphics.Clear(Color.BurlyWood);
                        for (int n = 0; n < 19; n++) { int v = 210 + n * 21; graphics.DrawLine(Pens.Black, v, 210, v, 588); graphics.DrawLine(Pens.Black, 210, v, 588, v); }
                    }
                    return screen;
                };
                dialog.CaptureOverlayShownForTest = delegate(Form overlay)
                {
                    overlay.Opacity = 0;
                    overlay.BeginInvoke(new Action(delegate
                    {
                        if (select)
                        {
                            Mouse(overlay, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 200, 200, 0));
                            Mouse(overlay, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 600, 600, 0));
                            Mouse(overlay, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 600, 600, 0));
                        }
                        else typeof(Control).GetMethod("OnKeyDown", Private).Invoke(overlay, new object[] { new KeyEventArgs(Keys.Escape) });
                    }));
                };
                dialog.Shown += async delegate
                {
                    try
                    {
                        await dialog.CaptureForTestAsync();
                        Check(dialog.Visible && dialog.Modal && !dialog.IsDisposed, "await截屏导致ShowDialog提前结束");
                        Check(Math.Abs(owner.Opacity - .88) < .001 && Math.Abs(dialog.Opacity - .91) < .001, "截屏后未恢复窗口透明度");
                        Bitmap image = (Bitmap)Field(dialog, "source");
                        Check(select ? !Object.ReferenceEquals(previous, image) : Object.ReferenceEquals(previous, image), "取消改变了图片或成功截屏未替换图片");
                        Check(image.Width == 400 && image.Height == 400, "截屏选区尺寸错误");
                        completed = true;
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { dialog.Close(); }
                };
                dialog.ShowDialog(owner);
                if (failure != null) throw failure;
                Check(completed, "ShowDialog在异步截屏结束前返回");
                Check(owner.Visible && Math.Abs(owner.Opacity - .88) < .001, "截屏改变了原窗口显示状态");
            }
        }
    }
}
