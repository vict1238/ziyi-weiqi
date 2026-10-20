using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                LogError(e.Exception);
                MessageBox.Show("程序遇到错误：\n" + e.Exception.Message + "\n\n详细记录已写入本地日志。", "梓懿围棋", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogError(e.ExceptionObject as Exception);
            };
            try
            {
                if (args.Length > 0 && args[0] == "--self-test")
                {
                    string result = GoGameTests.Run() + Environment.NewLine + SearchDepthTests.Run() +
                        Environment.NewLine + BoardCoordinatesTests.Run() + Environment.NewLine + EngineSettingsTests.Run() +
                        Environment.NewLine + ModelCatalogTests.Run() +
                        Environment.NewLine + StoneSoundTests.Run();
                    WriteReport(args, "PASS\n" + result);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--engine-test")
                {
                    string result = RunEngineChecksAsync().GetAwaiter().GetResult();
                    WriteReport(args, "PASS\n" + result);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--gpu-test")
                {
                    EngineSettings settings = EngineSettings.CreateGpu(args.Length > 2 ? args[2] : ModelCatalog.StrongestId);
                    settings.Validate();
                    string result = "GPU profile: " + settings.Description + Environment.NewLine +
                        RunEngineChecksAsync(settings).GetAwaiter().GetResult();
                    WriteReport(args, "PASS\n" + result);
                    return 0;
                }
                if (args.Length > 1 && args[0] == "--screenshot")
                {
                    Capture(args[1]);
                    return 0;
                }
                if (args.Length > 1 && args[0] == "--ui-test")
                    return RunUiChecks(args);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception e)
            {
                LogError(e);
                if (args.Length > 0)
                {
                    WriteReport(args, "FAIL\n" + e);
                    return 1;
                }
                MessageBox.Show("无法启动：" + e.Message, "梓懿围棋", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void WriteReport(string[] args, string text)
        {
            Console.WriteLine(text);
            if (args.Length > 1)
            {
                string fullPath = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, text, new UTF8Encoding(true));
            }
        }

        private static async Task<string> RunEngineChecksAsync(EngineSettings settings = null)
        {
            var report = new StringBuilder();
            using (var engine = new KataGoEngine(settings ?? EngineSettings.CreateCpu()))
            {
                engine.StatusChanged += delegate(string status) { Console.WriteLine(status); };
                var game = new GoGame(19, 7.5);
                string error;
                Assert(game.TryPlay(3, 15, out error), error);
                Assert(game.TryPlay(15, 3, out error), error);
                var started = DateTime.UtcNow;
                var analysis = await engine.AnalyzeAsync(game, 16, true);
                CheckAnalysis(analysis, 361);
                var move = GoGame.ParseGtp(analysis.BestMove, game.ToPlay, game.Size);
                Assert(game.TryPlayMove(move, out error), "AI returned illegal move: " + error);
                report.AppendLine("19x19 real KataGo analysis + ownership + legal move: " + analysis.BestMove + "; " + (DateTime.UtcNow - started).TotalSeconds.ToString("F2") + "s");

                var handicap = GoGame.FromSgf("(;GM[1]FF[4]SZ[9]KM[0.5]HA[2]AB[cc][gg]PL[W];W[ee])");
                var setup = await engine.AnalyzeAsync(handicap, 8, true);
                CheckAnalysis(setup, 81);
                Assert(handicap.TryPlayMove(GoGame.ParseGtp(setup.BestMove, handicap.ToPlay, handicap.Size), out error), "Setup game AI move: " + error);
                report.AppendLine("9x9 SGF setup stones + initial player + legal move: " + setup.BestMove);

                Task<AnalysisResult> pending = engine.AnalyzeAsync(new GoGame(19, 7.5), 4096, false);
                await Task.Delay(180);
                engine.Cancel();
                try
                {
                    await pending;
                    report.AppendLine("Cancellation: request completed before cancellation");
                }
                catch (OperationCanceledException)
                {
                    report.AppendLine("Cancellation: PASS");
                }
                var afterCancel = await engine.AnalyzeAsync(new GoGame(9, 7.5), 8, false);
                CheckAnalysis(afterCancel, 0);
                report.AppendLine("Engine remains usable after cancellation: " + afterCancel.BestMove);

                var largeProgress = new TaskCompletionSource<AnalysisResult>();
                var largeTarget = engine.AnalyzeAsync(new GoGame(9, 7.5), 3000000000L, false, partial => largeProgress.TrySetResult(partial));
                Assert(await Task.WhenAny(largeProgress.Task, Task.Delay(12000)) == largeProgress.Task, "Large search did not report progress");
                engine.Cancel();
                try { await largeTarget; } catch (OperationCanceledException) { }
                report.AppendLine("Custom 3,000,000,000 visits accepted without integer overflow + live progress + cancel: PASS");

                var continuousProgress = new TaskCompletionSource<AnalysisResult>();
                var continuous = engine.AnalyzeAsync(new GoGame(19, 7.5), 0, true, partial => continuousProgress.TrySetResult(partial));
                Assert(await Task.WhenAny(continuousProgress.Task, Task.Delay(12000)) == continuousProgress.Task, "Continuous search did not report progress");
                engine.Stop();
                AnalysisResult stopped = await continuous;
                CheckAnalysis(stopped, 361);
                report.AppendLine("Continuous search -> Stop -> usable final best move and territory: " + stopped.BestMove);
            }
            return report.ToString();
        }

        private static void CheckAnalysis(AnalysisResult result, int ownershipSize)
        {
            Assert(result != null, "Missing analysis");
            Assert(!string.IsNullOrWhiteSpace(result.BestMove), "Missing AI move");
            Assert(result.BlackWinrate >= 0 && result.BlackWinrate <= 1 && !double.IsNaN(result.BlackWinrate), "Invalid winrate");
            Assert(!double.IsNaN(result.BlackScoreLead) && !double.IsInfinity(result.BlackScoreLead), "Invalid score");
            Assert(result.Visits > 0, "No search visits");
            Assert(result.Candidates != null && result.Candidates.Count > 0, "Missing candidates");
            if (ownershipSize > 0)
                Assert(result.Ownership != null && result.Ownership.Length == ownershipSize, "Wrong ownership map length");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Capture(string path)
        {
            using (var form = new MainForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Size = new Size(1180, 850);
                form.ShowInTaskbar = false;
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 350;
                timer.Tick += delegate
                {
                    timer.Stop();
                    form.PerformLayout();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        string target = Path.GetFullPath(path);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    timer.Dispose();
                    form.Close();
                };
                form.Shown += delegate { timer.Start(); };
                Application.Run(form);
            }
        }

        private static int RunUiChecks(string[] args)
        {
            int exitCode = 0;
            using (var form = new MainForm(EngineSettings.CreateCpu(), false))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Shown += async delegate
                {
                    try
                    {
                        string result = await UiSmoke.RunAsync(form, Path.GetDirectoryName(Path.GetFullPath(args[1])));
                        WriteReport(args, "PASS\n" + result);
                    }
                    catch (Exception error)
                    {
                        LogError(error);
                        WriteReport(args, "FAIL\n" + error);
                        exitCode = 1;
                    }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
            return exitCode;
        }

        internal static void LogError(Exception error)
        {
            if (error == null) return;
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoDesk", "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "app.log"), DateTime.Now.ToString("s") + " " + error + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        internal static void ShowLicenses(IWin32Window owner)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoDesk", "licenses");
                Directory.CreateDirectory(folder);
                var assembly = Assembly.GetExecutingAssembly();
                foreach (string resource in assembly.GetManifestResourceNames())
                {
                    if (!resource.StartsWith("notices.", StringComparison.Ordinal)) continue;
                    string name = resource.Substring("notices.".Length);
                    if (name != Path.GetFileName(name)) continue;
                    using (Stream source = assembly.GetManifestResourceStream(resource))
                    using (FileStream target = File.Create(Path.Combine(folder, name))) source.CopyTo(target);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show(owner, "无法打开许可文件：" + error.Message, "梓懿围棋", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
