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
                        Environment.NewLine + StoneSoundTests.Run() +
                        Environment.NewLine + LocalStudyTests.Run() +
                        Environment.NewLine + MoveNumbersTests.Run();
                    WriteReport(args, "PASS\n" + result);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--engine-test")
                {
                    string result = RunEngineChecksAsync().GetAwaiter().GetResult();
                    WriteReport(args, "PASS\n" + result);
                    return 0;
                }
                if (args.Length > 0 && (args[0] == "--gpu-test" || args[0] == "--cuda-test"))
                {
                    string modelId = args.Length > 2 ? args[2] : ModelCatalog.StrongestId;
                    EngineSettings settings = args[0] == "--cuda-test" ? EngineSettings.CreateCuda(modelId) : EngineSettings.CreateGpu(modelId);
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

                var whiteSetup = new GoGame(9, 0.5);
                whiteSetup.NewGame(9, 0.5, 2, Stone.White);
                string whiteSetupSgf = whiteSetup.ToSgf();
                Assert(whiteSetupSgf.Contains("AW[") && whiteSetupSgf.Contains("PL[B]") && !whiteSetupSgf.Contains("HA["),
                    "White handicap SGF must contain AW + PL[B], without conventional Black HA");
                whiteSetup = GoGame.FromSgf(whiteSetupSgf);
                AssertInitialSetup(whiteSetup, 2, Stone.White, Stone.Black);
                var whiteSetupAnalysis = await engine.AnalyzeAsync(whiteSetup, 8, true);
                CheckAnalysis(whiteSetupAnalysis, 81);
                Assert(whiteSetup.TryPlayMove(GoGame.ParseGtp(whiteSetupAnalysis.BestMove, Stone.Black, 9), out error),
                    "Black AI returned an illegal move against initial White setup: " + error);
                Assert(whiteSetup.Position == 1 && whiteSetup.Moves[0].Color == Stone.Black && whiteSetup.ToPlay == Stone.White,
                    "White setup must be followed by the first actual Black move");
                report.AppendLine("9x9 White setup: initialStones=W x2, initialPlayer=B, AW + PL[B], 81 valid ownership values, legal Black first move: " + whiteSetupAnalysis.BestMove);

                var blackSingle = new GoGame(9, 0.5);
                blackSingle.NewGame(9, 0.5, 1, Stone.Black);
                blackSingle = GoGame.FromSgf(blackSingle.ToSgf());
                AssertInitialSetup(blackSingle, 1, Stone.Black, Stone.White);
                Assert(blackSingle.Board[4, 4] == Stone.Black, "Single Black handicap stone must occupy tengen");
                var blackSingleAnalysis = await engine.AnalyzeAsync(blackSingle, 8, true);
                CheckAnalysis(blackSingleAnalysis, 81);
                Assert(blackSingle.TryPlayMove(GoGame.ParseGtp(blackSingleAnalysis.BestMove, Stone.White, 9), out error),
                    "White AI returned an illegal move against single Black setup: " + error);
                Assert(blackSingle.Position == 1 && blackSingle.Moves[0].Color == Stone.White && blackSingle.ToPlay == Stone.Black,
                    "Single Black setup must be followed by the first actual White move");
                report.AppendLine("9x9 single Black setup: initialStones=B x1 at tengen, initialPlayer=W, 81 valid ownership values, legal White first move: " + blackSingleAnalysis.BestMove);

                report.Append(await CheckLocalStudyEngineAsync(engine));

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

        private static async Task<string> CheckLocalStudyEngineAsync(KataGoEngine engine)
        {
            var report = new StringBuilder();
            var region = new StudyRegion(0, 0, 3, 3);
            var game = new GoGame(9, 7.5);
            string error;
            var black = await engine.AnalyzeAsync(game, 8, true, null, region);
            CheckRestrictedAnalysis(black, region, 9);
            Assert(game.TryPlayMove(GoGame.ParseGtp(black.BestMove, Stone.Black, 9), out error), "Restricted Black move is illegal: " + error);
            var white = await engine.AnalyzeAsync(game, 8, true, null, region);
            CheckRestrictedAnalysis(white, region, 9);
            Assert(game.TryPlayMove(GoGame.ParseGtp(white.BestMove, Stone.White, 9), out error), "Restricted White move is illegal: " + error);
            Assert(game.Position == 2 && game.ToPlay == Stone.Black, "Restricted searches lost actual move colors");
            report.AppendLine("9x9 local study A1–D4: Black and White legal moves; every candidate/PV coordinate restricted, no pass; full-board ownership x81: PASS");

            string beforeComparison = game.ToSgf();
            var global = await engine.AnalyzeAsync(game, 8, true);
            CheckAnalysis(global, 81);
            Assert(global.Region == null && game.ToSgf() == beforeComparison, "Global comparison retained local restriction or modified the game");
            var resumed = await engine.AnalyzeAsync(game, 8, true, null, region);
            CheckRestrictedAnalysis(resumed, region, 9);
            Assert(game.ToSgf() == beforeComparison, "Returning to local analysis changed the game");
            report.AppendLine("Local -> global comparison -> local in one native process, independent result scopes and unchanged board: PASS");

            var occupied = new GoGame(9, 7.5);
            Assert(occupied.TryPlay(0, 0, out error), error);
            await CheckLocalRejectionAsync(engine, occupied, new StudyRegion(0, 0, 0, 0), false);
            var suicide = GoGame.FromSgf("(;SZ[9]KM[7.5]RU[Chinese]AW[ab][ba]PL[B])");
            await CheckLocalRejectionAsync(engine, suicide, new StudyRegion(0, 0, 0, 0), false);
            await CheckLocalRejectionAsync(engine, game, new StudyRegion(-1, 0, 2, 2), true);
            var afterRejection = await engine.AnalyzeAsync(game, 8, true, null, region);
            CheckRestrictedAnalysis(afterRejection, region, 9);
            report.AppendLine("Occupied single-point, suicide-only and out-of-bounds regions fail before native analysis; no global fallback; same engine remains usable: PASS");
            return report.ToString();
        }

        private static async Task CheckLocalRejectionAsync(KataGoEngine engine, GoGame game, StudyRegion region, bool outOfBounds)
        {
            string before = game.ToSgf();
            if (!outOfBounds) Assert(!LocalStudy.HasLegalMove(game, region), "Rejected region unexpectedly contains a legal move");
            bool rejected = false;
            try { await engine.AnalyzeAsync(game, 8, true, null, region); }
            catch (ArgumentOutOfRangeException) { if (!outOfBounds) throw; rejected = true; }
            catch (InvalidOperationException) { if (outOfBounds) throw; rejected = true; }
            Assert(rejected, "Invalid local region silently fell back to an unrestricted search");
            Assert(game.ToSgf() == before && engine.IsReady,
                "Local preflight failure modified the board or disabled the active native engine");
        }

        private static void CheckRestrictedAnalysis(AnalysisResult result, StudyRegion expected, int size)
        {
            CheckAnalysis(result, size * size);
            Assert(result.Region != null && result.Region.Left == expected.Left && result.Region.Top == expected.Top &&
                result.Region.Right == expected.Right && result.Region.Bottom == expected.Bottom, "Local result has missing or stale region identity");
            AssertRestrictedCoordinate(result.BestMove, expected, size);
            foreach (MoveCandidate candidate in result.Candidates)
            {
                AssertRestrictedCoordinate(candidate.Move, expected, size);
                Assert(candidate.Pv != null && candidate.Pv.Length > 0, "Local candidate has no principal variation");
                foreach (string coordinate in candidate.Pv) AssertRestrictedCoordinate(coordinate, expected, size);
            }
        }

        private static void AssertRestrictedCoordinate(string coordinate, StudyRegion region, int size)
        {
            Move move = GoGame.ParseGtp(coordinate, Stone.Black, size);
            Assert(!move.IsPass && region.Contains(move.X, move.Y), "Local candidate/PV left its rectangle or passed: " + coordinate);
        }

        private static void AssertInitialSetup(GoGame game, int count, Stone recipient, Stone firstPlayer)
        {
            Assert(game.InitialStones.Count == count && game.Position == 0 && game.Moves.Count == 0,
                "Initial handicap stones were lost or counted as played moves");
            Assert(game.InitialPlayer == firstPlayer && game.ToPlay == firstPlayer, "Wrong initial player for handicap direction");
            foreach (Move stone in game.InitialStones)
                Assert(stone.Color == recipient && game.Board[stone.X, stone.Y] == recipient, "Initial setup color differs from its board stone");
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
            {
                Assert(result.Ownership != null && result.Ownership.Length == ownershipSize, "Wrong ownership map length");
                foreach (double ownership in result.Ownership)
                    Assert(!double.IsNaN(ownership) && !double.IsInfinity(ownership) && ownership >= -1.000001 && ownership <= 1.000001,
                        "Invalid ownership value");
            }
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
