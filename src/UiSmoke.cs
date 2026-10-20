using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoDesk
{
    internal static class UiSmoke
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        public static async Task<string> RunAsync(MainForm form, string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);
            var report = new StringBuilder();
            Check(form.Text == "梓懿围棋 · KataGo", "Application title was not renamed");
            Check(MainForm.Tribute == "致敬六大至尊围棋王“梓”和“懿”", "About tribute text differs from requested sentence");
            report.AppendLine("Product title and About tribute: PASS");
            var board = (BoardControl)form.Controls.Find("GoBoard", true)[0];
            var level = (ComboBox)form.Controls.Find("SearchLevel", true)[0];
            CheckDepthPresets(form, level);
            using (var gpuDefaults = new MainForm(EngineSettings.CreateCuda(), false))
            {
                Check(gpuDefaults.CurrentEngineSettings.NormalizedBackend == "cuda" && gpuDefaults.CurrentEngineSettings.ModelId == ModelCatalog.StrongestId && CurrentVisits(gpuDefaults) == 2048,
                    "CUDA startup must default to the strongest model and 2048 visits");
                Check(((Label)Field(gpuDefaults, "profileLabel")).Text.Contains("CUDA"), "CUDA active profile does not identify its backend");
                Check(gpuDefaults.CurrentEngineSettings.ModelDescription.Contains("2026-09-25"), "Strongest model date missing from active profile");
                gpuDefaults.StartPosition = FormStartPosition.Manual;
                gpuDefaults.Location = new Point(-20000, -20000);
                gpuDefaults.ShowInTaskbar = false;
                gpuDefaults.Show(); gpuDefaults.PerformLayout();
                gpuDefaults.CaptureScreenshot(Path.Combine(outputFolder, "desktop-gpu-defaults.png"));
                gpuDefaults.Close();
            }
            using (var openClDefaults = new MainForm(EngineSettings.CreateGpu(), false))
            {
                Check(openClDefaults.CurrentEngineSettings.NormalizedBackend == "gpu" && openClDefaults.CurrentEngineSettings.ModelId == ModelCatalog.StrongestId && CurrentVisits(openClDefaults) == 2048,
                    "OpenCL startup must default to the strongest model and 2048 visits");
                Check(((Label)Field(openClDefaults, "profileLabel")).Text.Contains("OpenCL"), "OpenCL active profile does not identify its backend");
            }
            report.AppendLine("Shared 256/1024/2048/4096/8192/16384 presets; CPU 256 and strongest CUDA/OpenCL 2048 defaults: PASS");
            CheckCancelledCustomDepth(form, level);
            report.AppendLine("Unconfirmed custom selection keeps the committed depth; Cancel restores preset: PASS");
            level.SelectedIndex = 0;
            ForcePaint(board);
            RectangleF grid = (RectangleF)Field(board, "grid");
            float step = (float)Field(board, "step");
            var click = new MouseEventArgs(MouseButtons.Left, 1, (int)Math.Round(grid.Left + 3 * step), (int)Math.Round(grid.Top + 15 * step), 0);
            typeof(BoardControl).GetMethod("OnMouseClick", PrivateInstance).Invoke(board, new object[] { click });
            await WaitIdle(form);
            Check(form.CurrentGame.Position == 2, "Physical board hit-test + auto AI reply did not produce two moves");
            Check(form.CurrentGame.Board[3, 15] == Stone.Black, "Hit-test coordinate did not map to display D16");
            Check(form.CurrentGame.Moves[1].Color == Stone.White, "Automatic AI color is incorrect");
            Check(board.Analysis != null && board.Analysis.Visits >= SearchDepth.PresetVisits(0) && board.Analysis.Visits < 512,
                "The 256-visit preset was not used for the real AI reply");
            Check(((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains(form.CurrentGame.Moves[1].ToDisplay(19)), "AI move status does not use screen coordinates");
            Call(form, "Navigate", 1);
            Check(((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains("D16"), "History status uses protocol coordinates");
            Call(form, "Navigate", 2);
            report.AppendLine("Board hit-test -> human display D16 -> actual KataGo reply; AI/history screen coordinates: PASS");
            CheckEngineSettingsDialog(form, outputFolder);
            report.AppendLine("Engine/model settings cancel + validation + profile swap keep game and user preferences: PASS");
            CheckStoneVolumeSettings(form, outputFolder);
            report.AppendLine("Stone volume dialog draft/cancel/confirm; checks stay muted and do not save preferences: PASS");

            Button hints = (Button)form.Controls.Find("BestHints", true)[0];
            hints.PerformClick(); await WaitIdle(form);
            Check(board.ShowHints && board.Analysis != null && board.Analysis.Candidates.Count > 0, "Hints control has no real analysis");
            Check(((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains(BoardCoordinates.FromGtp(board.Analysis.BestMove, form.CurrentGame.Size)), "Hint status uses protocol coordinates");
            report.AppendLine("Best hints control + real candidates: PASS");
            Button score = (Button)form.Controls.Find("Score", true)[0];
            score.PerformClick(); await WaitIdle(form);
            Check(board.ShowOwnership && board.Analysis.Ownership.Length == 361, "Score control has no territory map");
            report.AppendLine("Score control + 361 ownership values: PASS");

            ((Button)form.Controls.Find("Undo", true)[0]).PerformClick();
            Check(form.CurrentGame.Position == 0, "Undo should remove AI and human moves when auto reply is enabled");
            report.AppendLine("Undo a human / AI pair: PASS");

            Button selfPlay = (Button)form.Controls.Find("SelfPlay", true)[0];
            selfPlay.PerformClick();
            DateTime selfPlayDeadline = DateTime.UtcNow.AddSeconds(20);
            while (form.CurrentGame.Position < 4 && DateTime.UtcNow < selfPlayDeadline) await Task.Delay(70);
            Check(form.CurrentGame.Position >= 4, "Self-play did not produce multiple real moves");
            selfPlay.PerformClick();
            int stoppedAt = form.CurrentGame.Position;
            await Task.Delay(450);
            Check(form.CurrentGame.Position == stoppedAt && !(bool)Field(form, "selfPlaying"), "Self-play continued after Stop");
            report.AppendLine("AI self-play multiple turns -> Stop -> no late move: PASS");

            var example = GoGame.FromSgf("(;GM[1]FF[4]CA[UTF-8]SZ[19]KM[7.5]RU[Chinese]PB[黑方]PW[白方];B[pd];W[dd];B[qp];W[dp];B[oq];W[cn];B[fq];W[cf];B[lc];W[fc];B[qn];W[ck];B[qf];W[ci];B[of];W[kq];B[nc];W[hc];B[pf];W[fp];B[eq];W[ep])");
            form.LoadGame(example);
            Check(form.CurrentGame.Position == 22, "SGF load into UI lost moves");
            Call(form, "Navigate", 12);
            Check(form.CurrentGame.Position == 12 && form.CurrentGame.Moves.Count == 22, "Review lost future moves");
            Call(form, "Navigate", 22);
            string saved = form.CurrentGame.ToSgf();
            File.WriteAllText(Path.Combine(outputFolder, "示例对局.sgf"), saved, new UTF8Encoding(false));
            Check(GoGame.FromSgf(saved).Position == 22, "SGF roundtrip after review lost moves");
            report.AppendLine("SGF load -> review -> return -> export: PASS");

            hints.PerformClick(); await WaitIdle(form);
            form.CaptureScreenshot(Path.Combine(outputFolder, "desktop-preview.png"));
            score.PerformClick(); await WaitIdle(form);
            form.CaptureScreenshot(Path.Combine(outputFolder, "desktop-territory.png"));
            form.Size = new Size(1000, 740); form.PerformLayout();
            await Task.Delay(150);
            form.CaptureScreenshot(Path.Combine(outputFolder, "desktop-small.png"));
            form.Size = new Size(1180, 850); form.PerformLayout();
            report.AppendLine("Rendered full, territory and minimum window screenshots: PASS");

            Task request = (Task)Call(form, "AnalyzeAsync", true, false);
            Call(form, "NewGame");
            await request;
            Check(form.CurrentGame.Position == 0, "Canceled result moved in a new game");
            report.AppendLine("New game cancels in-flight analysis with no stale move: PASS");
            ((ComboBox)form.Controls.Find("HumanColor", true)[0]).SelectedIndex = 1;
            await WaitIdle(form);
            Check(form.CurrentGame.Position == 1 && form.CurrentGame.Moves[0].Color == Stone.Black, "Playing White did not make AI open as Black");
            report.AppendLine("Choose White -> automatic KataGo first move: PASS");
            form.LoadGame(GoGame.FromSgf("(;SZ[9]KM[101]RU[Japanese];B[dd];W[ff])"));
            Check(form.CurrentGame.Rules == "japanese", "SGF Japanese rules lost in UI");
            score.PerformClick(); await WaitIdle(form);
            Check(board.Analysis != null && board.Analysis.Ownership.Length == 81, "Japanese 9x9 scoring failed");
            report.AppendLine("Japanese rules + 9x9 + large valid komi real engine: PASS");

            ConfigureDepth(form, "3000000000", false, Path.Combine(outputFolder, "depth-settings.png"));
            Check((long)typeof(MainForm).GetProperty("Visits", PrivateInstance).GetValue(form, null) == 3000000000L, "Custom depth was narrowed or clamped");
            int priorPosition = form.CurrentGame.Position;
            Call(form, "ClearAnalysis");
            Task finite = (Task)Call(form, "AnalyzeAsync", true, false);
            await WaitProgress(form, board);
            ((Button)form.Controls.Find("StopThinking", true)[0]).PerformClick();
            await finite;
            Check(form.CurrentGame.Position == priorPosition + 1, "Stop did not apply the current best AI move");
            report.AppendLine("Custom 3,000,000,000 via settings dialog -> real progress -> Stop and play: PASS");

            ConfigureDepth(form, "1000000", true, Path.Combine(outputFolder, "depth-settings-continuous.png"));
            Check((long)typeof(MainForm).GetProperty("Visits", PrivateInstance).GetValue(form, null) == 0, "Continuous depth setting was not applied");
            Call(form, "ClearAnalysis");
            Task continuous = (Task)Call(form, "AnalyzeAsync", false, true);
            await WaitProgress(form, board);
            form.CaptureScreenshot(Path.Combine(outputFolder, "desktop-continuous-progress.png"));
            ((Button)form.Controls.Find("StopThinking", true)[0]).PerformClick();
            await continuous;
            Check(board.Analysis != null && board.Analysis.Ownership.Length == 81 && board.ShowOwnership, "Continuous analysis did not retain its stopped territory result");
            report.AppendLine("Continuous setting -> live progress -> Stop and retain analysis: PASS");

            Task oldContinuous = (Task)Call(form, "AnalyzeAsync", true, false);
            Call(form, "NewGame");
            await oldContinuous;
            Check(form.CurrentGame.Position == 0, "Continuous search or queued progress modified a new game");
            report.AppendLine("Continuous cancellation + queued progress isolation after New Game: PASS");
            return report.ToString();
        }

        private static void ConfigureDepth(MainForm form, string countText, bool continuousValue, string screenshot)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 120 };
            timer.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms) if (candidate.Name == "SearchDepthSettings") dialog = candidate;
                if (dialog == null) return;
                timer.Stop();
                ((ComboBox)dialog.Controls.Find("DepthMode", true)[0]).SelectedIndex = SearchDepth.CustomIndex;
                ((TextBox)dialog.Controls.Find("CustomVisits", true)[0]).Text = countText;
                ((CheckBox)dialog.Controls.Find("ContinuousDepth", true)[0]).Checked = continuousValue;
                dialog.PerformLayout();
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                    bitmap.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png);
                }
                ((Button)dialog.Controls.Find("DepthOK", true)[0]).PerformClick();
            };
            try
            {
                timer.Start();
                bool applied = (bool)Call(form, "ShowDepthSettings", form);
                Check(applied, "Custom depth dialog was not confirmed");
            }
            finally { timer.Dispose(); }
        }

        private static void CheckStoneVolumeSettings(MainForm form, string outputFolder)
        {
            Check(!(bool)Field(form, "persistEnginePreferences"), "Audio checks require isolated preferences");
            StoneSound player = (StoneSound)Field(form, "stoneSound");
            Check(player.Volume == 0, "UI checks must start muted");
            string before = form.CurrentGame.ToSgf();
            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms) if (candidate.Name == "GameSettings") dialog = candidate;
                if (dialog == null) return;
                timer.Stop();
                ((TrackBar)dialog.Controls.Find("StoneVolume", true)[0]).Value = 73;
                Check(player.Volume == 0, "Draft slider modified active volume before confirmation");
                ((Button)dialog.Controls.Find("GameSettingsCancel", true)[0]).PerformClick();
            };
            try { timer.Start(); Call(form, "GameSettings"); }
            finally { timer.Dispose(); }
            Check(player.Volume == 0, "Cancel changed active sound volume");
            var confirm = new System.Windows.Forms.Timer { Interval = 100 };
            confirm.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms) if (candidate.Name == "GameSettings") dialog = candidate;
                if (dialog == null) return;
                confirm.Stop();
                ((TrackBar)dialog.Controls.Find("StoneVolume", true)[0]).Value = 37;
                Check(((Label)dialog.Controls.Find("StoneVolumePercent", true)[0]).Text == "37%", "Volume percentage did not update");
                Check(dialog.Controls.Find("StoneSoundPreview", true).Length == 1, "Sound preview control missing");
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                    bitmap.Save(Path.Combine(outputFolder, "desktop-sound-settings.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                ((Button)dialog.Controls.Find("GameSettingsOk", true)[0]).PerformClick();
            };
            try { confirm.Start(); Call(form, "GameSettings"); }
            finally { confirm.Dispose(); }
            Check(player.Volume == 37 && form.CurrentGame.ToSgf() == before, "Volume confirmation failed or changed the game");
            player.Volume = 0;
        }

        private static void CheckEngineSettingsDialog(MainForm form, string outputFolder)
        {
            Check(!(bool)Field(form, "persistEnginePreferences"), "Run UI checks with MainForm(EngineSettings.CreateCpu(), false) to isolate preferences");
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoDesk", "engine-settings.json");
            string savedBefore = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
            EngineSettings before = form.CurrentEngineSettings;
            object oldEngine = Field(form, "engine");
            string gameBefore = form.CurrentGame.ToSgf();
            int oldSession = (int)Field(form, "engineSession");
            var cancelTimer = new System.Windows.Forms.Timer { Interval = 100 };
            cancelTimer.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms) if (candidate.Name == "EngineModelSettings") dialog = candidate;
                if (dialog == null) return;
                cancelTimer.Stop();
                ComboBox backends = (ComboBox)dialog.Controls.Find("EngineBackend", true)[0];
                Check(backends.Items.Count == 4 && backends.Items[1].ToString().Contains("CUDA") && backends.Items[2].ToString().Contains("OpenCL"), "CUDA, OpenCL, CPU and custom choices missing");
                backends.SelectedIndex = 1;
                ComboBox models = (ComboBox)dialog.Controls.Find("ModelSelection", true)[0];
                Check(models.Items.Count == ModelCatalog.GpuModels.Length + 1 && ((ModelCatalogEntry)models.SelectedItem).Id == ModelCatalog.StrongestId,
                    "CUDA model selection does not default to strongest");
                Check(((Label)dialog.Controls.Find("ModelDetails", true)[0]).Text.Contains("2026-09-25"), "Strongest model date missing in settings");
                models.SelectedIndex = 1;
                Check(((ModelCatalogEntry)models.SelectedItem).Id == ModelCatalog.PreviousGpuId &&
                    ((Label)dialog.Controls.Find("ModelDetails", true)[0]).Text.Contains("2024-05-26"), "Previous b18 model or its date missing in settings");
                models.SelectedIndex = 0;
                string cudaEngine = ((TextBox)dialog.Controls.Find("EnginePath", true)[0]).Text;
                Check(cudaEngine.Replace('\\', '/').EndsWith("cuda/katago.exe", StringComparison.OrdinalIgnoreCase), "CUDA program path incorrect");
                backends.SelectedIndex = 2;
                Check(((TextBox)dialog.Controls.Find("EnginePath", true)[0]).Text.Replace('\\', '/').EndsWith("gpu/katago.exe", StringComparison.OrdinalIgnoreCase), "OpenCL program path incorrect");
                Check(((ModelCatalogEntry)models.SelectedItem).Id == ModelCatalog.StrongestId, "OpenCL model selection does not default to strongest");
                backends.SelectedIndex = 1;
                Check(((TextBox)dialog.Controls.Find("EnginePath", true)[0]).Text == cudaEngine && ((ModelCatalogEntry)models.SelectedItem).Id == ModelCatalog.StrongestId, "Switching backends changed CUDA draft");
                TextBox shownEngine = (TextBox)dialog.Controls.Find("EnginePath", true)[0];
                TextBox shownModel = (TextBox)dialog.Controls.Find("ModelPath", true)[0];
                string originalEngine = shownEngine.Text, originalModel = shownModel.Text;
                shownEngine.Text = @"cuda\katago.exe";
                shownModel.Text = ModelCatalog.Find(ModelCatalog.StrongestId).RelativePath;
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                    bitmap.Save(Path.Combine(outputFolder, "desktop-engine-settings.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                shownEngine.Text = originalEngine;
                shownModel.Text = originalModel;
                backends.SelectedIndex = 3;
                models.SelectedIndex = models.Items.Count - 1;
                ((TextBox)dialog.Controls.Find("EnginePath", true)[0]).Text = @"Z:\missing\katago.exe";
                ((TextBox)dialog.Controls.Find("ModelPath", true)[0]).Text = @"Z:\missing\model.bin.gz";
                ((Button)dialog.Controls.Find("EngineSettingsCancel", true)[0]).PerformClick();
            };
            try { cancelTimer.Start(); Check(!(bool)Call(form, "ShowEngineSettings", form), "Cancel confirmed engine settings"); }
            finally { cancelTimer.Dispose(); }
            EngineSettings afterCancel = form.CurrentEngineSettings;
            Check(before.Backend == afterCancel.Backend && before.ModelId == afterCancel.ModelId && before.EnginePath == afterCancel.EnginePath && before.ModelPath == afterCancel.ModelPath && Object.ReferenceEquals(oldEngine, Field(form, "engine")), "Canceled engine settings changed the active profile");
            Check(form.CurrentGame.ToSgf() == gameBefore, "Canceled engine settings changed the game");

            var confirmTimer = new System.Windows.Forms.Timer { Interval = 100 };
            confirmTimer.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms) if (candidate.Name == "EngineModelSettings") dialog = candidate;
                if (dialog == null) return;
                confirmTimer.Stop();
                ComboBox backend = (ComboBox)dialog.Controls.Find("EngineBackend", true)[0];
                backend.SelectedIndex = 3;
                ComboBox models = (ComboBox)dialog.Controls.Find("ModelSelection", true)[0];
                models.SelectedIndex = models.Items.Count - 1;
                ((TextBox)dialog.Controls.Find("EnginePath", true)[0]).Text = @"Z:\missing\katago.exe";
                ((TextBox)dialog.Controls.Find("ModelPath", true)[0]).Text = @"Z:\missing\model.bin.gz";
                ((Button)dialog.Controls.Find("EngineSettingsOK", true)[0]).PerformClick();
                Check(!String.IsNullOrEmpty(((Label)dialog.Controls.Find("EngineSettingsError", true)[0]).Text), "Missing custom engine files were accepted");
                backend.SelectedIndex = 0;
                models.SelectedIndex = 0;
                Check(((Label)dialog.Controls.Find("ModelDetails", true)[0]).Text.Contains("2020"), "Legacy CPU model year missing in settings");
                ((TextBox)dialog.Controls.Find("EnginePath", true)[0]).Text = "";
                ((TextBox)dialog.Controls.Find("ModelPath", true)[0]).Text = "";
                ((Button)dialog.Controls.Find("EngineSettingsOK", true)[0]).PerformClick();
            };
            try { confirmTimer.Start(); Check((bool)Call(form, "ShowEngineSettings", form), "Valid CPU settings were not applied"); }
            finally { confirmTimer.Dispose(); }
            Check(form.CurrentEngineSettings.Backend == "cpu" && form.CurrentEngineSettings.ModelId == ModelCatalog.LegacyCpuId && !Object.ReferenceEquals(oldEngine, Field(form, "engine")), "Confirmed settings did not replace the engine");
            Check(form.CurrentGame.ToSgf() == gameBefore && ((BoardControl)form.Controls.Find("GoBoard", true)[0]).Analysis == null, "Profile swap reset the game or kept stale analysis");
            Label engineStatus = (Label)Field(form, "engineLabel");
            string currentStatus = engineStatus.Text;
            Call(form, "OnEngineStatus", oldSession, "stale engine status");
            Check(engineStatus.Text == currentStatus, "A previous engine session overwrote current status");
            string savedAfter = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
            Check(savedBefore == savedAfter, "Desktop checks persisted engine preferences");
        }

        private static void CheckCancelledCustomDepth(MainForm form, ComboBox level)
        {
            level.SelectedIndex = 1;
            level.SelectedIndex = SearchDepth.CustomIndex;
            Check(CurrentVisits(form) == SearchDepth.PresetVisits(1), "Provisional custom selection changed the active budget");
            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms) if (candidate.Name == "SearchDepthSettings") dialog = candidate;
                if (dialog == null) return;
                timer.Stop();
                ((Button)dialog.Controls.Find("DepthCancel", true)[0]).PerformClick();
            };
            try
            {
                timer.Start();
                Call(form, "OnDepthSelected", level, EventArgs.Empty);
                Check(level.SelectedIndex == 1 && CurrentVisits(form) == SearchDepth.PresetVisits(1), "Cancel changed the saved search depth");
            }
            finally { timer.Dispose(); }
        }

        private static long CurrentVisits(MainForm form)
        {
            return (long)typeof(MainForm).GetProperty("Visits", PrivateInstance).GetValue(form, null);
        }

        private static void CheckDepthPresets(MainForm form, ComboBox level)
        {
            Check(CurrentVisits(form) == SearchDepth.PresetVisits(SearchDepth.CpuDefaultIndex), "CPU startup budget should be 256");
            Check(level.Items.Count == SearchDepth.PresetCount + 1, "Main depth list lost shared presets");
            for (int i = 0; i < SearchDepth.PresetCount; i++)
            {
                level.SelectedIndex = i;
                Check(CurrentVisits(form) == SearchDepth.PresetVisits(i), "Main depth preset maps to a different request budget");
            }
            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += delegate
            {
                Form dialog = null;
                foreach (Form candidate in Application.OpenForms) if (candidate.Name == "SearchDepthSettings") dialog = candidate;
                if (dialog == null) return;
                timer.Stop();
                ComboBox choices = (ComboBox)dialog.Controls.Find("DepthMode", true)[0];
                Check(choices.Items.Count == level.Items.Count, "Settings and main depth lists disagree");
                for (int i = 0; i < SearchDepth.PresetCount; i++) Check(choices.Items[i].Equals(level.Items[i]), "Settings and main preset labels disagree");
                choices.SelectedIndex = SearchDepth.GpuDefaultIndex;
                ((Button)dialog.Controls.Find("DepthOK", true)[0]).PerformClick();
            };
            try { timer.Start(); Check((bool)Call(form, "ShowDepthSettings", form), "Preset depth setting not confirmed"); }
            finally { timer.Dispose(); }
            Check(CurrentVisits(form) == 2048 && level.SelectedIndex == SearchDepth.GpuDefaultIndex, "Preset confirmation did not use shared 2048 budget");
        }

        private static async Task WaitProgress(MainForm form, BoardControl board)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(70);
                if (board.Analysis != null && board.Analysis.Visits > 0 && (bool)Field(form, "thinking")) return;
            }
            throw new TimeoutException("Long analysis did not update the desktop progress");
        }

        private static async Task WaitIdle(MainForm form)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(50);
            do
            {
                await Task.Delay(60);
                if (!(bool)Field(form, "thinking")) return;
            } while (DateTime.UtcNow < deadline);
            throw new TimeoutException("Desktop UI did not finish the requested engine action");
        }
        private static void ForcePaint(Control control)
        {
            using (var image = new Bitmap(control.Width, control.Height)) control.DrawToBitmap(image, control.ClientRectangle);
        }
        private static object Field(object instance, string name)
        {
            return instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
        }
        private static object Call(MainForm form, string method, params object[] args)
        {
            return typeof(MainForm).GetMethod(method, PrivateInstance).Invoke(form, args);
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
