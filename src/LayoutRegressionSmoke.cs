using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    // Pixel-level repaint regression plus the real form's GPU/CPU layout states.
    // The bitmap DPI cases exercise drawing at four densities; they do not
    // pretend to change or test the user's Windows display configuration.
    internal static class LayoutRegressionSmoke
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int assertions;
        private static void Check(bool valid, string message)
        { assertions++; if (!valid) throw new Exception("界面残影回归：" + message); }
        private static T Find<T>(Control parent, string name) where T : Control
        { return (T)parent.Controls.Find(name, true)[0]; }
        private static object Field(MainForm main, string name)
        { return typeof(MainForm).GetField(name, Private).GetValue(main); }
        private static object Call(MainForm main, string name, params object[] args)
        { return typeof(MainForm).GetMethod(name, Private).Invoke(main, args); }
        private static void Save(MainForm main, string path)
        { main.PerformLayout(); main.CaptureScreenshot(path); }

        private static void PaintOverOldPixels(WarmToggle toggle, float dpi, string state)
        {
            using (var bitmap = new Bitmap(toggle.Width, toggle.Height))
            {
                bitmap.SetResolution(dpi, dpi);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    // Represent a backing buffer left with another row's text.
                    graphics.Clear(Color.Magenta);
                    using (Brush ink = new SolidBrush(Color.Black))
                        graphics.FillRectangle(ink, 0, 0, toggle.Width, toggle.Height / 2);
                    typeof(WarmToggle).GetMethod("OnPaint", Private).Invoke(toggle,
                        new object[] { new PaintEventArgs(graphics, toggle.ClientRectangle) });
                }
                float scale = dpi / 96f;
                int trackLeft = toggle.Width - (int)(31 * scale) - 2;
                int gap = Math.Max(1, trackLeft - (int)(3 * scale));
                Color background = toggle.Parent == null ? UiTheme.Surface : toggle.Parent.BackColor;
                Check(bitmap.GetPixel(gap, toggle.Height / 2).ToArgb() == background.ToArgb(), state + "：标签与开关之间保留旧字色");
                Check(bitmap.GetPixel(toggle.Width / 2, 0).ToArgb() == background.ToArgb(), state + "：上边缘没有擦除旧字色");
                Check(bitmap.GetPixel(toggle.Width / 2, toggle.Height - 1).ToArgb() == background.ToArgb(), state + "：下边缘没有擦除旧字色");
            }
        }

        private static void CheckDrawingDensities()
        {
            foreach (int dpi in new int[] { 96, 120, 144, 192 })
            using (var parent = new Panel { BackColor = UiTheme.Surface })
            using (var toggle = new WarmToggle())
            {
                parent.Controls.Add(toggle);
                float scale = dpi / 96f;
                toggle.Font = new Font("Microsoft YaHei UI", 9f);
                foreach (string label in new string[] { "AI 自动应手", "显示手数", "复盘中暂停自动应手" })
                foreach (bool enabled in new bool[] { true, false })
                foreach (bool selected in new bool[] { false, true })
                {
                    toggle.Text = label; toggle.Enabled = enabled; toggle.Checked = selected;
                    toggle.Size = new Size((int)(300 * scale), (int)(30 * scale));
                    PaintOverOldPixels(toggle, dpi, dpi + " DPI / " + label);
                    // A second frame follows a resize and a parent color change.
                    parent.BackColor = UiTheme.Background;
                    toggle.Width = (int)(340 * scale);
                    PaintOverOldPixels(toggle, dpi, dpi + " DPI / 缩放与父背景切换");
                    parent.BackColor = UiTheme.Surface;
                }
            }
        }

        private static void CheckFooter(MainForm main, string state)
        {
            foreach (string name in new string[] { "HistoryActions", "StudyActions", "GameActions" })
            {
                var row = Find<FlowLayoutPanel>(main, name);
                Control first = null, last = null;
                foreach (Control control in row.Controls)
                {
                    if (!control.Visible) continue;
                    Check(row.ClientRectangle.Contains(control.Bounds), state + "：操作按钮超出行边界 " + name);
                    if (last != null) Check(!last.Bounds.IntersectsWith(control.Bounds), state + "：操作按钮相互覆盖 " + name);
                    if (first == null) first = control;
                    last = control;
                }
                Check(first != null && last != null, state + "：操作行缺失 " + name);
                if (first != null && last != null)
                {
                    int left = first.Left - first.Margin.Left;
                    int right = last.Right + last.Margin.Right;
                    Check(Math.Abs(left - (row.ClientSize.Width - right)) <= 3, state + "：操作行未按可用宽度居中 " + name);
                }
            }
            var automatic = Find<WarmToggle>(main, "AutomaticReply");
            var numbers = Find<WarmToggle>(main, "ShowMoveNumbers");
            foreach (WarmToggle toggle in new WarmToggle[] { automatic, numbers })
            {
                Check(toggle.BackColor.A == 255, state + "：开关仍依赖透明背景 " + toggle.Name);
                using (Graphics graphics = toggle.CreateGraphics())
                    PaintOverOldPixels(toggle, graphics.DpiX, state + " / " + toggle.Name);
            }
            var profile = Find<Label>(main, "EngineProfile");
            Check(profile.Parent != automatic.Parent, state + "：模型文字错误进入 AI 操作容器");
            var ai = Find<Button>(main, "AiPlay");
            Rectangle automaticBounds = automatic.Parent.RectangleToClient(automatic.RectangleToScreen(automatic.ClientRectangle));
            Rectangle aiRowBounds = automatic.Parent.RectangleToClient(ai.Parent.RectangleToScreen(ai.Parent.ClientRectangle));
            Check(!automaticBounds.IntersectsWith(aiRowBounds), state + "：自动应手覆盖 AI 操作行");
        }

        public static async Task<string> RunAsync(MainForm main, string folder)
        {
            assertions = 0;
            Directory.CreateDirectory(folder);
            Check(!(bool)Field(main, "persistEnginePreferences"), "验收窗口必须关闭配置写入");
            CheckDrawingDensities();
            EngineSettings originalSettings = main.CurrentEngineSettings;
            string originalSgf = main.CurrentGame.ToSgf();
            int originalPosition = main.CurrentGame.Position;
            Size originalSize = main.Size;
            Point originalLocation = main.Location;
            FormWindowState originalWindowState = main.WindowState;
            PropertyInfo maximizedBounds = typeof(Form).GetProperty("MaximizedBounds", Private);
            Rectangle originalMaximizedBounds = (Rectangle)maximizedBounds.GetValue(main, null);
            string originalSection = (string)Field(main, "currentSection");
            var human = Find<ComboBox>(main, "HumanColor");
            var automatic = Find<WarmToggle>(main, "AutomaticReply");
            var numbers = Find<WarmToggle>(main, "ShowMoveNumbers");
            int originalHuman = human.SelectedIndex;
            bool originalAutomatic = automatic.Checked, originalNumbers = numbers.Checked;
            float actualDpi;
            using (Graphics graphics = main.CreateGraphics()) actualDpi = graphics.DpiX;
            string gpuName = "";
            try
            {
                automatic.Checked = false; human.SelectedIndex = 2;
                main.LoadGame(GoGame.FromSgf("(;SZ[19]KM[7.5]AB[pc]AW[fp]PL[B])"));
                Call(main, "ShowWorkspace", "play");
                EngineSettings gpu = EngineSettings.CudaAvailable ? EngineSettings.CreateCuda() :
                    EngineSettings.GpuAvailable ? EngineSettings.CreateGpu() : null;
                Check(gpu != null, "缺少相邻 GPU 运行库，不能将 CPU 图当作 GPU 验收");
                gpuName = gpu.Description;
                foreach (EngineSettings profile in new EngineSettings[] { EngineSettings.CreateCpu(), gpu })
                {
                    Call(main, "ApplyEngineSettings", profile);
                    Find<ComboBox>(main, "SearchLevel").SelectedIndex = SearchDepth.DefaultIndex(profile.IsGpu);
                    Check(main.CurrentEngineSettings.Backend == profile.Backend, "引擎配置未切换");
                    Check(Find<Label>(main, "EngineProfile").Text == profile.Description, "实际模型标签不匹配");
                    for (int cycle = 0; cycle < 2; cycle++)
                    {
                        // Keep this harness outside the visible desktop even if maximized.
                        maximizedBounds.SetValue(main, new Rectangle(originalLocation, Screen.PrimaryScreen.WorkingArea.Size), null);
                        main.WindowState = FormWindowState.Maximized;
                        await Task.Delay(40); Call(main, "UpdateUi"); main.PerformLayout();
                        CheckFooter(main, profile.Backend + " / 最大化 " + cycle);
                        if (profile.IsGpu && cycle == 0) Save(main, Path.Combine(folder, "layout-gpu-maximized.png"));
                        main.WindowState = FormWindowState.Normal;
                        main.Size = new Size(Math.Max(main.MinimumSize.Width, 1500), Math.Max(main.MinimumSize.Height, 975));
                        main.Location = originalLocation;
                        await Task.Delay(40); Call(main, "UpdateUi"); main.PerformLayout();
                        CheckFooter(main, profile.Backend + " / 还原 " + cycle);

                        Call(main, "CancelAnalysis");
                        Call(main, "OnEngineStatus", (int)Field(main, "engineSession"), "KataGo " +
                            (profile.IsGpu ? "GPU · " + (profile.Backend == "cuda" ? "CUDA" : "OpenCL") : "CPU") + " 分析已取消");
                        Call(main, "UpdateUi");
                        Check(Find<WarmToggle>(main, "AutomaticReply").Text == "AI 自动应手", "引擎取消状态污染开关标签");
                        automatic.Checked = true; numbers.Checked = true;
                        Check(Find<BoardControl>(main, "GoBoard").ShowMoveNumbers, "显示手数开关未生效");
                        CheckFooter(main, profile.Backend + " / 取消与开关切换");
                        Call(main, "ShowWorkspace", "review");
                        Check(!automatic.Enabled && automatic.Text.Contains("暂停"), "复盘未使用停用状态");
                        CheckFooter(main, profile.Backend + " / 复盘长标签");
                        string before = main.CurrentGame.ToSgf();
                        Call(main, "ShowWorkspace", "settings");
                        Call(main, "ShowWorkspace", "play");
                        Check(main.CurrentGame.ToSgf() == before, "导航丢失棋局");
                        var sidebar = Find<Panel>(main, "AnalysisSidebar");
                        sidebar.AutoScrollPosition = new Point(0, 10000);
                        await Task.Delay(20);
                        sidebar.AutoScrollPosition = Point.Empty;
                        await Task.Delay(20); Call(main, "UpdateUi"); main.PerformLayout();
                        CheckFooter(main, profile.Backend + " / 侧栏滚动还原");
                        if (profile.IsGpu && cycle == 1) Save(main, Path.Combine(folder, "layout-gpu-restored.png"));
                        automatic.Checked = false; numbers.Checked = false;
                    }
                }
            }
            finally
            {
                automatic.Checked = false;
                main.WindowState = FormWindowState.Normal; maximizedBounds.SetValue(main, originalMaximizedBounds, null);
                main.Size = originalSize; main.Location = originalLocation;
                Call(main, "ApplyEngineSettings", originalSettings);
                main.LoadGame(GoGame.FromSgf(originalSgf)); main.CurrentGame.Goto(originalPosition);
                human.SelectedIndex = originalHuman; numbers.Checked = originalNumbers;
                Call(main, "ShowWorkspace", originalSection);
                automatic.Checked = originalAutomatic;
                main.WindowState = originalWindowState;
                Call(main, "UpdateUi");
            }
            string report = "界面残影与布局：PASS（" + assertions + " 项）。\n" +
                "绘制模拟：96/120/144/192 DPI；旧字色背景、开/关、禁用、长标签、连续缩放与父背景切换。模拟不代表修改或实测这些 Windows 缩放设置。\n" +
                "实际窗口 DPI：" + actualDpi + "；CPU 与 " + gpuName + " 配置，各两轮最大化、还原、取消状态、导航、侧栏滚动及两个开关；未在本项启动引擎推理，真实引擎另行验证。\n";
            File.WriteAllText(Path.Combine(folder, "layout-regression-test.txt"), "PASS\n" + report, new UTF8Encoding(true));
            return report;
        }
    }
}
