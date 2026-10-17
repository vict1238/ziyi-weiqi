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
            Check(form.CurrentGame.Board[3, 15] == Stone.Black, "Hit-test coordinate did not map to D4");
            Check(form.CurrentGame.Moves[1].Color == Stone.White, "Automatic AI color is incorrect");
            report.AppendLine("Board hit-test -> human D4 -> actual KataGo reply: PASS");

            Button hints = (Button)form.Controls.Find("BestHints", true)[0];
            hints.PerformClick(); await WaitIdle(form);
            Check(board.ShowHints && board.Analysis != null && board.Analysis.Candidates.Count > 0, "Hints control has no real analysis");
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
                ((ComboBox)dialog.Controls.Find("DepthMode", true)[0]).SelectedIndex = 3;
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

        private static void CheckCancelledCustomDepth(MainForm form, ComboBox level)
        {
            level.SelectedIndex = 1;
            level.SelectedIndex = 3;
            Check((long)typeof(MainForm).GetProperty("Visits", PrivateInstance).GetValue(form, null) == 64, "Provisional custom selection changed the active budget");
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
                Check(level.SelectedIndex == 1 && (long)typeof(MainForm).GetProperty("Visits", PrivateInstance).GetValue(form, null) == 64, "Cancel changed the saved search depth");
            }
            finally { timer.Dispose(); }
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
