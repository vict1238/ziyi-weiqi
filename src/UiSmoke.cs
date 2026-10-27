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
        private static int assertionCount;
        public static async Task<string> RunAsync(MainForm form, string outputFolder)
        {
            assertionCount = 0;
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
            int handicapChecks = await CheckHandicapGames(form, outputFolder);
            report.AppendLine("Bidirectional handicap settings draft/cancel/confirm, New Game, 0/1/2 stones, SGF save/load and both real AI first replies: PASS (" + handicapChecks + " assertions)");
            int studyChecks = await CheckLocalStudyUiAsync(form, outputFolder);
            report.AppendLine("Real forward/reverse study-region drags, original move numbers, local replies, global comparison and canceled-result isolation: PASS (" + studyChecks + " assertions)");

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
                if (screenshot != null) using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
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

        private static async Task<int> CheckLocalStudyUiAsync(MainForm form, string outputFolder)
        {
            int startedAssertions = assertionCount;
            string originalSgf = form.CurrentGame.ToSgf();
            int originalPosition = form.CurrentGame.Position;
            var board = (BoardControl)form.Controls.Find("GoBoard", true)[0];
            var numbers = (CheckBox)form.Controls.Find("ShowMoveNumbers", true)[0];
            var automatic = (CheckBox)form.Controls.Find("AutomaticReply", true)[0];
            var human = (ComboBox)form.Controls.Find("HumanColor", true)[0];
            var size = (ComboBox)form.Controls.Find("BoardSize", true)[0];
            var depth = (ComboBox)form.Controls.Find("SearchLevel", true)[0];
            var select = (Button)form.Controls.Find("SelectStudyRegion", true)[0];
            var clear = (Button)form.Controls.Find("ClearStudyRegion", true)[0];
            var compare = (Button)form.Controls.Find("GlobalComparison", true)[0];
            var hints = (Button)form.Controls.Find("BestHints", true)[0];
            int oldHuman = human.SelectedIndex, oldSize = size.SelectedIndex, oldDepth = (int)Field(form, "selectedDepthIndex");
            int oldHandicap = (int)Field(form, "handicap");
            Stone oldRecipient = (Stone)Field(form, "handicapColor");
            double oldKomi = (double)Field(form, "komi");
            string oldRules = (string)Field(form, "rules");
            bool oldAutomatic = automatic.Checked, oldNumbers = numbers.Checked, oldContinuous = (bool)Field(form, "continuousDepth");
            long oldCustomVisits = (long)Field(form, "customVisits"), oldVisits = CurrentVisits(form);
            object oldCustomLabel = depth.Items[SearchDepth.CustomIndex];
            Size oldWindow = form.Size;
            automatic.Checked = false;
            const string numberedSgf = "(;SZ[9]KM[7.5]RU[Chinese]AB[hh]PL[B];B[aa];W[ba];B[];W[ab];B[cc];W[dd];B[ee])";
            try
            {
                form.LoadGame(GoGame.FromSgf(numberedSgf));
                string before = form.CurrentGame.ToSgf();
                numbers.Checked = false;
                Check(!board.ShowMoveNumbers && form.CurrentGame.ToSgf() == before, "Hiding move numbers changed the game or left board numbering enabled");
                numbers.Checked = true;
                Check(board.ShowMoveNumbers && form.CurrentGame.ToSgf() == before && form.CurrentGame.Position == 7, "Showing move numbers changed the game");
                int[,] map = MoveNumbers.Build(form.CurrentGame);
                Check(map[0, 0] == 0 && map[1, 0] == 2 && map[0, 1] == 4 && map[2, 2] == 5 && map[3, 3] == 6 && map[4, 4] == 7 && map[7, 7] == 0,
                    "Board numbering lost original plies, pass gaps, captures or unnumbered initial stones");
                Call(form, "Navigate", 2);
                map = MoveNumbers.Build(form.CurrentGame);
                Check(map[0, 0] == 1 && map[1, 0] == 2 && map[2, 2] == 0, "Review numbering includes future moves or loses a stone captured later");
                Call(form, "Navigate", 7);
                Check(numbers.Checked && board.ShowMoveNumbers && form.CurrentGame.ToSgf() == before, "Review changed the numbering toggle or retained SGF");
                form.CaptureScreenshot(Path.Combine(outputFolder, "desktop-move-numbers.png"));

                ConfigureDepth(form, "8", false, null);
                select.PerformClick();
                Check(board.SelectingRegion && !board.AllowInput && form.CurrentGame.ToSgf() == before,
                    "Selecting a study region did not separate dragging from stone input");
                DragStudyRegion(form, board, 0, 0, 3, 3, null);
                var region = new StudyRegion(0, 0, 3, 3);
                CheckSameRegion(board.StudyRegion, region, "Forward drag selected incorrect board coordinates");
                CheckSameRegion((StudyRegion)Field(form, "studyRegion"), region, "Main window did not retain the board selection");
                Check(!board.SelectingRegion && form.CurrentGame.ToSgf() == before && form.CurrentGame.Position == 7,
                    "Finishing a study-region drag played a stone");
                Check(board.Analysis == null && !(bool)Field(form, "thinking"), "Selecting a region unexpectedly started analysis or played a move");
                Check(((Label)form.Controls.Find("StudyRegionStatus", true)[0]).Text.Contains(region.Describe(9)), "Study status does not identify the selected screen coordinates");
                hints.PerformClick(); await WaitIdle(form);
                CheckUiRestrictedAnalysis(board.Analysis, region, 9);
                Check(form.CurrentGame.ToSgf() == before && board.ShowHints, "Local recommendation changed the game or did not display candidates");

                form.Size = new Size(1000, 740); form.PerformLayout();
                select.PerformClick();
                DragStudyRegion(form, board, 3, 3, 0, 0, Path.Combine(outputFolder, "desktop-local-study-select-small.png"));
                CheckSameRegion(board.StudyRegion, region, "Reverse drag did not normalize its endpoints");
                Check(form.CurrentGame.ToSgf() == before && board.Analysis == null, "Reverse selection played a stone or retained old analysis");
                form.Size = oldWindow; form.PerformLayout();
                hints.PerformClick(); await WaitIdle(form);
                CheckUiRestrictedAnalysis(board.Analysis, region, 9);
                Check(((Label)form.Controls.Find("AnalysisVariation", true)[0]).Text.Length > 0, "Local recommendation has no displayed principal variation");
                form.CaptureScreenshot(Path.Combine(outputFolder, "desktop-local-study.png"));

                compare.PerformClick(); await WaitIdle(form);
                Check(board.Analysis != null && board.Analysis.Region == null && form.CurrentGame.ToSgf() == before && form.CurrentGame.Position == 7,
                    "Global comparison applied a move or retained a local restriction");
                CheckSameRegion(board.StudyRegion, region, "Global comparison cleared the selected study frame");
                CheckSameRegion((StudyRegion)Field(form, "studyRegion"), region, "Global comparison replaced the active local mode");
                Check(hints.Text == "返回局部推荐", "Global comparison did not expose a direct return-to-local action");
                hints.PerformClick(); await WaitIdle(form);
                CheckUiRestrictedAnalysis(board.Analysis, region, 9);
                Check(board.ShowHints && form.CurrentGame.ToSgf() == before && form.CurrentGame.Position == 7,
                    "One return-to-local click merely hid hints, changed the game or failed to display local candidates");
                CheckSameRegion(board.StudyRegion, region, "Returning to local recommendations changed the selected frame");

                // Also exercise an AI move immediately following a global view,
                // independently of the explicit return-to-local button above.
                compare.PerformClick(); await WaitIdle(form);
                Check(board.Analysis != null && board.Analysis.Region == null && form.CurrentGame.ToSgf() == before,
                    "Repeated global comparison changed the game or kept the local result");
                CheckSameRegion(board.StudyRegion, region, "Repeated global comparison cleared the active local frame");

                ((Button)form.Controls.Find("AiPlay", true)[0]).PerformClick(); await WaitIdle(form);
                Check(form.CurrentGame.Position == 8, "AI did not resume local play after global comparison");
                Move played = form.CurrentGame.Moves[7];
                Check(!played.IsPass && played.Color == Stone.White && region.Contains(played.X, played.Y), "AI move after comparison escaped the region or changed turn color");
                CheckUiRestrictedAnalysis(board.Analysis, region, 9);
                Check(MoveNumbers.Build(form.CurrentGame)[played.X, played.Y] == 8, "Local play restarted move numbering instead of keeping the original ply");

                human.SelectedIndex = 0;
                automatic.Checked = true;
                Move humanMove = FindLegalRegionMove(form.CurrentGame, region);
                ClickBoard(board, humanMove.X, humanMove.Y);
                await WaitIdle(form);
                Check(form.CurrentGame.Position == 10 && form.CurrentGame.Moves[8].Color == Stone.Black && form.CurrentGame.Moves[9].Color == Stone.White,
                    "Local human move did not receive the actual automatic AI reply");
                played = form.CurrentGame.Moves[9];
                Check(!played.IsPass && region.Contains(played.X, played.Y) && MoveNumbers.Build(form.CurrentGame)[played.X, played.Y] == 10,
                    "Automatic local reply escaped its frame or lost the original move number");
                CheckUiRestrictedAnalysis(board.Analysis, region, 9);
                string afterReply = form.CurrentGame.ToSgf();
                AnalysisResult late = board.Analysis;
                ClickBoard(board, 8, 8);
                Check(form.CurrentGame.ToSgf() == afterReply && form.CurrentGame.Position == 10, "Human was allowed to leave the local study frame");
                automatic.Checked = false;
                human.SelectedIndex = 2;

                // Selecting/canceling must retain the prior frame, while clearing
                // must remove it. Neither action is a request to place a stone.
                select.PerformClick();
                select.PerformClick();
                Check(!board.SelectingRegion && form.CurrentGame.ToSgf() == afterReply, "Canceling selection changed the game");
                CheckSameRegion(board.StudyRegion, region, "Canceling selection discarded the committed frame");
                ConfigureDepth(form, "1000000", true, null);
                Task pending = (Task)Call(form, "AnalyzeAsync", true, false);
                int staleStamp = (int)Field(form, "generation");
                select.PerformClick();
                await pending;
                if (late != null) Call(form, "ShowAnalysisProgress", staleStamp, late, true, false);
                Check(board.SelectingRegion && board.Analysis == null && form.CurrentGame.ToSgf() == afterReply,
                    "Selecting during continuous local search accepted a stale move or analysis callback");
                clear.PerformClick();
                Check(board.StudyRegion == null && Field(form, "studyRegion") == null && !board.SelectingRegion && board.Analysis == null,
                    "Exit local study did not clear selection mode, frame and stale analysis");
                Check(form.CurrentGame.ToSgf() == afterReply && numbers.Checked, "Exiting local mode altered the game or numbering preference");

                // New Game must clear a live local request; importing and an
                // actual board-size change must also clear frame state.
                select.PerformClick(); DragStudyRegion(form, board, 0, 0, 3, 3, null);
                pending = (Task)Call(form, "AnalyzeAsync", true, false);
                staleStamp = (int)Field(form, "generation");
                ((Button)form.Controls.Find("NewGame", true)[0]).PerformClick();
                await pending;
                if (late != null) Call(form, "ShowAnalysisProgress", staleStamp, late, true, false);
                Check(form.CurrentGame.Position == 0 && board.StudyRegion == null && Field(form, "studyRegion") == null && board.Analysis == null,
                    "New Game accepted an old local result or kept its study frame");
                Check(numbers.Checked && board.ShowMoveNumbers, "New Game reset the numbering preference");
                select.PerformClick(); DragStudyRegion(form, board, 0, 0, 3, 3, null);
                size.SelectedIndex = 1;
                Check(form.CurrentGame.Size == 9 && board.StudyRegion != null, "Next-game board-size selection unexpectedly replaced the current board");
                ((Button)form.Controls.Find("NewGame", true)[0]).PerformClick();
                Check(form.CurrentGame.Size == 13 && board.StudyRegion == null && numbers.Checked, "Changing board size through New Game kept an obsolete frame or reset numbering");
                select.PerformClick(); DragStudyRegion(form, board, 5, 5, 1, 1, null);
                form.LoadGame(GoGame.FromSgf(numberedSgf));
                Check(form.CurrentGame.Size == 9 && form.CurrentGame.Position == 7 && board.StudyRegion == null && Field(form, "studyRegion") == null && !board.SelectingRegion,
                    "Import kept the previous board's region or selection gesture");
                Check(numbers.Checked && board.ShowMoveNumbers, "Import reset the numbering preference");

                ConfigureDepth(form, "8", false, null);
                select.PerformClick(); DragStudyRegion(form, board, 1, 0, 1, 0, null);
                string occupiedBefore = form.CurrentGame.ToSgf();
                ((Button)form.Controls.Find("AiPlay", true)[0]).PerformClick(); await WaitIdle(form);
                Check(form.CurrentGame.ToSgf() == occupiedBefore && board.Analysis == null && !(bool)Field(form, "thinking"),
                    "An occupied single-point region fell back to a global AI move");
                Check(((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains("合法"), "Empty legal-region preflight did not explain why local analysis stopped");
                clear.PerformClick();
            }
            finally
            {
                automatic.Checked = false;
                Call(form, "CancelAnalysis");
                GoGame restored = GoGame.FromSgf(originalSgf); restored.Goto(originalPosition);
                form.LoadGame(restored);
                SetField(form, "updating", true);
                try
                {
                    SetField(form, "handicap", oldHandicap); SetField(form, "handicapColor", oldRecipient);
                    SetField(form, "komi", oldKomi); SetField(form, "rules", oldRules);
                    SetField(form, "customVisits", oldCustomVisits); SetField(form, "continuousDepth", oldContinuous);
                    SetField(form, "selectedDepthIndex", oldDepth);
                    depth.Items[SearchDepth.CustomIndex] = oldCustomLabel; depth.SelectedIndex = oldDepth;
                    size.SelectedIndex = oldSize; human.SelectedIndex = oldHuman; automatic.Checked = oldAutomatic;
                    numbers.Checked = oldNumbers; board.ShowMoveNumbers = oldNumbers;
                    form.Size = oldWindow;
                }
                finally { SetField(form, "updating", false); }
                Call(form, "UpdateUi");
            }
            Check(form.CurrentGame.ToSgf() == originalSgf && form.CurrentGame.Position == originalPosition && CurrentVisits(form) == oldVisits &&
                board.StudyRegion == null && Field(form, "studyRegion") == null && numbers.Checked == oldNumbers,
                "Local-study checks leaked game, depth, frame or numbering state into other UI checks");
            return assertionCount - startedAssertions;
        }

        private static void DragStudyRegion(MainForm form, BoardControl board, int x1, int y1, int x2, int y2, string previewScreenshot)
        {
            ForcePaint(board);
            Point start = BoardPixel(board, x1, y1), end = BoardPixel(board, x2, y2);
            typeof(BoardControl).GetMethod("OnMouseDown", PrivateInstance).Invoke(board, new object[] { new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0) });
            typeof(BoardControl).GetMethod("OnMouseMove", PrivateInstance).Invoke(board, new object[] { new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0) });
            if (previewScreenshot != null) form.CaptureScreenshot(previewScreenshot);
            typeof(BoardControl).GetMethod("OnMouseUp", PrivateInstance).Invoke(board, new object[] { new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0) });
            // Real WinForms mouse-up is followed by MouseClick. Check that the
            // selection gesture suppresses that final event instead of placing a stone.
            typeof(BoardControl).GetMethod("OnMouseClick", PrivateInstance).Invoke(board, new object[] { new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0) });
        }

        private static Point BoardPixel(BoardControl board, int x, int y)
        {
            RectangleF grid = (RectangleF)Field(board, "grid");
            float step = (float)Field(board, "step");
            return new Point((int)Math.Round(grid.Left + x * step), (int)Math.Round(grid.Top + y * step));
        }

        private static void ClickBoard(BoardControl board, int x, int y)
        {
            ForcePaint(board);
            Point point = BoardPixel(board, x, y);
            typeof(BoardControl).GetMethod("OnMouseClick", PrivateInstance).Invoke(board, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        }

        private static Move FindLegalRegionMove(GoGame game, StudyRegion region)
        {
            GoGame copy = GoGame.FromSgf(game.ToSgf()); copy.Goto(game.Position);
            for (int y = region.Top; y <= region.Bottom; y++) for (int x = region.Left; x <= region.Right; x++)
            {
                string error;
                if (copy.TryPlay(x, y, out error)) return new Move(x, y, game.ToPlay);
            }
            throw new InvalidOperationException("UI test region unexpectedly has no legal human move");
        }

        private static void CheckSameRegion(StudyRegion actual, StudyRegion expected, string message)
        {
            Check(actual != null && actual.Left == expected.Left && actual.Top == expected.Top && actual.Right == expected.Right && actual.Bottom == expected.Bottom, message);
        }

        private static void CheckUiRestrictedAnalysis(AnalysisResult result, StudyRegion expected, int size)
        {
            Check(result != null && result.Candidates != null && result.Candidates.Count > 0, "Local desktop analysis has no candidates");
            CheckSameRegion(result.Region, expected, "Desktop local analysis belongs to a different or global region");
            foreach (MoveCandidate candidate in result.Candidates)
            {
                Move move = GoGame.ParseGtp(candidate.Move, Stone.Black, size);
                Check(!move.IsPass && expected.Contains(move.X, move.Y), "Desktop local candidate escaped its frame");
                Check(candidate.Pv != null && candidate.Pv.Length > 0, "Desktop local candidate has no PV");
                foreach (string coordinate in candidate.Pv)
                {
                    move = GoGame.ParseGtp(coordinate, Stone.Black, size);
                    Check(!move.IsPass && expected.Contains(move.X, move.Y), "Desktop local PV escaped its frame or passed");
                }
            }
        }

        private static async Task<int> CheckHandicapGames(MainForm form, string outputFolder)
        {
            int startedAssertions = assertionCount;
            string originalSgf = form.CurrentGame.ToSgf();
            int originalPosition = form.CurrentGame.Position;
            int originalHandicap = (int)Field(form, "handicap");
            Stone originalRecipient = (Stone)Field(form, "handicapColor");
            double originalKomi = (double)Field(form, "komi");
            string originalRules = (string)Field(form, "rules");
            var human = (ComboBox)form.Controls.Find("HumanColor", true)[0];
            var automatic = (CheckBox)form.Controls.Find("AutomaticReply", true)[0];
            var size = (ComboBox)form.Controls.Find("BoardSize", true)[0];
            var depth = (ComboBox)form.Controls.Find("SearchLevel", true)[0];
            int originalHuman = human.SelectedIndex, originalSize = size.SelectedIndex;
            int originalDepth = (int)Field(form, "selectedDepthIndex");
            long originalCustomVisits = (long)Field(form, "customVisits");
            bool originalContinuous = (bool)Field(form, "continuousDepth"), originalAutomatic = automatic.Checked;
            object originalCustomLabel = depth.Items[SearchDepth.CustomIndex];
            automatic.Checked = false;
            human.SelectedIndex = 2;
            try
            {
                EditHandicapSettings(form, 1, 9, 0.5m, 1, false, null);
                Check((int)Field(form, "handicap") == originalHandicap && (Stone)Field(form, "handicapColor") == originalRecipient,
                    "Cancel changed the pending handicap direction/count");
                Check((double)Field(form, "komi") == originalKomi && (string)Field(form, "rules") == originalRules,
                    "Cancel changed pending komi/rules");
                Check(form.CurrentGame.ToSgf() == originalSgf && form.CurrentGame.Position == originalPosition,
                    "Cancel reset or altered the active game");

                EditHandicapSettings(form, 1, 2, 6.5m, 1, true, Path.Combine(outputFolder, "desktop-handicap-settings.png"));
                Check((int)Field(form, "handicap") == 2 && (Stone)Field(form, "handicapColor") == Stone.White &&
                    (double)Field(form, "komi") == 6.5 && (string)Field(form, "rules") == "japanese",
                    "Confirmation failed to save pending White handicap, komi and rules");
                Check(form.CurrentGame.ToSgf() == originalSgf && form.CurrentGame.Position == originalPosition,
                    "Confirming next-game settings immediately reset the active game");
                EditHandicapSettings(form, 0, 7, -3.5m, 0, false, null);
                Check((int)Field(form, "handicap") == 2 && (Stone)Field(form, "handicapColor") == Stone.White &&
                    (double)Field(form, "komi") == 6.5 && (string)Field(form, "rules") == "japanese",
                    "Cancel lost the previously confirmed pending settings");
                Check(form.CurrentGame.ToSgf() == originalSgf, "A second canceled draft altered the active game");

                size.SelectedIndex = 2;
                ((Button)form.Controls.Find("NewGame", true)[0]).PerformClick();
                CheckHandicapInitial(form, 2, Stone.White, Stone.Black);
                Check(form.CurrentGame.Size == 9 && form.CurrentGame.Komi == 6.5 && form.CurrentGame.Rules == "japanese",
                    "New Game did not apply pending board size, komi and rules");
                Check(((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains("黑让白 2 子") &&
                    ((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains("黑方先行"), "White setup status omits direction or first player");
                form.CaptureScreenshot(Path.Combine(outputFolder, "desktop-handicap-white.png"));
                CheckHandicapRoundTrip(form, outputFolder, "让白初始局面.sgf", 2, Stone.White, Stone.Black, 0);

                // Use the real custom-depth dialog to keep these two additional
                // native CPU replies quick; the ordinary 256-preset check above stays intact.
                ConfigureDepth(form, "8", false, null);
                human.SelectedIndex = 1;
                automatic.Checked = true;
                await WaitIdle(form);
                Check(form.CurrentGame.Position == 1 && form.CurrentGame.Moves[0].Color == Stone.Black && form.CurrentGame.ToPlay == Stone.White,
                    "Human receiving White handicap did not make Black AI take the first actual turn");
                Check(form.CurrentGame.InitialStones.Count == 2 && form.CurrentGame.InitialPlayer == Stone.Black,
                    "Black AI reply lost the original White setup");
                CheckHandicapRoundTrip(form, outputFolder, "让白AI首手.sgf", 2, Stone.White, Stone.Black, 1);

                automatic.Checked = false;
                human.SelectedIndex = 0;
                EditHandicapSettings(form, 0, 2, 0.5m, 0, true, null);
                ((Button)form.Controls.Find("NewGame", true)[0]).PerformClick();
                CheckHandicapInitial(form, 2, Stone.Black, Stone.White);
                Check(((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains("白让黑 2 子") &&
                    ((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains("白方先行"), "Black setup status omits direction or first player");
                automatic.Checked = true;
                await WaitIdle(form);
                Check(form.CurrentGame.Position == 1 && form.CurrentGame.Moves[0].Color == Stone.White && form.CurrentGame.ToPlay == Stone.Black,
                    "Human receiving Black handicap did not make White AI take the first actual turn");
                Check(form.CurrentGame.InitialStones.Count == 2 && form.CurrentGame.InitialPlayer == Stone.White,
                    "White AI reply lost the original Black setup");
                CheckHandicapRoundTrip(form, outputFolder, "让黑AI首手.sgf", 2, Stone.Black, Stone.White, 1);

                automatic.Checked = false;
                human.SelectedIndex = 2;
                for (int direction = 0; direction < 2; direction++)
                {
                    Stone recipient = direction == 0 ? Stone.Black : Stone.White;
                    Stone firstPlayer = GoGame.Opponent(recipient);
                    EditHandicapSettings(form, direction, 1, 0.5m, 0, true, null);
                    ((Button)form.Controls.Find("NewGame", true)[0]).PerformClick();
                    CheckHandicapInitial(form, 1, recipient, firstPlayer);
                    Check(form.CurrentGame.Board[4, 4] == recipient, "One-stone handicap was not placed at tengen");
                    Check(!form.CurrentGame.ToSgf().Contains("HA["), "One-stone setup incorrectly exported conventional HA");
                    CheckHandicapRoundTrip(form, outputFolder, direction == 0 ? "黑一子天元.sgf" : "白一子天元.sgf", 1, recipient, firstPlayer, 0);

                    EditHandicapSettings(form, direction, 0, 7.5m, 0, true, null);
                    ((Button)form.Controls.Find("NewGame", true)[0]).PerformClick();
                    CheckHandicapInitial(form, 0, recipient, Stone.Black);
                    Check(((ToolStripStatusLabel)Field(form, "statusLabel")).Text.Contains("分先，黑方先行"), "Zero-stone status did not show an even game with Black first");
                    CheckHandicapRoundTrip(form, outputFolder, direction == 0 ? "分先黑方向.sgf" : "分先白方向.sgf", 0, recipient, Stone.Black, 0);
                }
            }
            finally
            {
                automatic.Checked = false;
                GoGame restored = GoGame.FromSgf(originalSgf);
                restored.Goto(originalPosition);
                form.LoadGame(restored);
                SetField(form, "updating", true);
                try
                {
                    SetField(form, "handicap", originalHandicap);
                    SetField(form, "handicapColor", originalRecipient);
                    SetField(form, "komi", originalKomi);
                    SetField(form, "rules", originalRules);
                    SetField(form, "customVisits", originalCustomVisits);
                    SetField(form, "continuousDepth", originalContinuous);
                    SetField(form, "selectedDepthIndex", originalDepth);
                    depth.Items[SearchDepth.CustomIndex] = originalCustomLabel;
                    depth.SelectedIndex = originalDepth;
                    size.SelectedIndex = originalSize;
                    human.SelectedIndex = originalHuman;
                    automatic.Checked = originalAutomatic;
                }
                finally { SetField(form, "updating", false); }
                Call(form, "UpdateUi");
            }
            Check(form.CurrentGame.ToSgf() == originalSgf && form.CurrentGame.Position == originalPosition &&
                (int)Field(form, "handicap") == originalHandicap && (Stone)Field(form, "handicapColor") == originalRecipient &&
                CurrentVisits(form) == SearchDepth.PresetVisits(originalDepth), "Handicap checks leaked pending/current state into other UI checks");
            return assertionCount - startedAssertions;
        }

        private static void EditHandicapSettings(MainForm form, int direction, int count, decimal komi, int rule, bool accept, string screenshot)
        {
            Exception failure = null;
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                timer.Tick += delegate
                {
                    Form dialog = null;
                    foreach (Form candidate in Application.OpenForms) if (candidate.Name == "GameSettings") dialog = candidate;
                    if (dialog == null) return;
                    timer.Stop();
                    try
                    {
                        var directions = (ComboBox)dialog.Controls.Find("HandicapDirection", true)[0];
                        var stones = (NumericUpDown)dialog.Controls.Find("HandicapCount", true)[0];
                        Check(directions.Items.Count == 2 && directions.Items[0].ToString().Contains("白让黑") && directions.Items[1].ToString().Contains("黑让白"),
                            "Handicap direction choices do not identify both recipients");
                        Check(stones.Minimum == 0 && stones.Maximum == 9, "Handicap numeric range must include 0, 1 and 9");
                        directions.SelectedIndex = direction;
                        stones.Value = count;
                        ((NumericUpDown)dialog.Controls.Find("KomiInput", true)[0]).Value = komi;
                        ((ComboBox)dialog.Controls.Find("GameRule", true)[0]).SelectedIndex = rule;
                        string preview = ((Label)dialog.Controls.Find("HandicapPreview", true)[0]).Text;
                        Check(count == 0 ? preview.Contains("分先") && preview.Contains("黑方先行") :
                            preview.Contains(direction == 0 ? "黑棋先摆 " + count : "白棋先摆 " + count) && preview.Contains(direction == 0 ? "白方先行" : "黑方先行"),
                            "Live handicap explanation disagrees with direction/count");
                        Check(count != 1 || preview.Contains("天元"), "One-stone preview does not explain tengen");
                        Check(preview.Contains("贴目仍归白方") && preview.Contains("下一局"), "Handicap preview omits komi or next-game behavior");
                        if (screenshot != null)
                        {
                            dialog.PerformLayout();
                            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                            {
                                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                                bitmap.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                        ((Button)dialog.Controls.Find(accept ? "GameSettingsOk" : "GameSettingsCancel", true)[0]).PerformClick();
                    }
                    catch (Exception error) { failure = error; dialog.DialogResult = DialogResult.Cancel; }
                };
                timer.Start();
                Call(form, "GameSettings");
            }
            if (failure != null) throw new InvalidOperationException("Handicap settings automation failed", failure);
        }

        private static void CheckHandicapInitial(MainForm form, int count, Stone recipient, Stone firstPlayer)
        {
            GoGame game = form.CurrentGame;
            Check(game.Position == 0 && game.Moves.Count == 0 && game.InitialStones.Count == count, "New Game counted setup stones as turns or kept old moves");
            Check(game.InitialPlayer == firstPlayer && game.ToPlay == firstPlayer, "New Game starts with the wrong color for its handicap direction");
            foreach (Move stone in game.InitialStones)
                Check(stone.Color == recipient && game.Board[stone.X, stone.Y] == recipient, "Handicap setup has the wrong stone color");
            int occupied = 0;
            foreach (Stone stone in game.Board) if (stone != Stone.Empty) occupied++;
            Check(occupied == count, "Initial handicap board contains extra or missing stones");
            Check(((Label)Field(form, "turnLabel")).Text.Contains(firstPlayer == Stone.Black ? "黑方行棋" : "白方行棋"),
                "Turn indicator does not identify the first actual player");
            Check(((Label)Field(form, "moveLabel")).Text == "第 0 / 0 手", "Move counter counts handicap setup as played moves");
        }

        private static void CheckHandicapRoundTrip(MainForm form, string outputFolder, string filename, int count, Stone recipient, Stone firstPlayer, int played)
        {
            string before = form.CurrentGame.ToSgf();
            string path = Path.Combine(outputFolder, filename);
            File.WriteAllText(path, before, new UTF8Encoding(false));
            GoGame loaded = GoGame.FromSgf(File.ReadAllText(path, Encoding.UTF8));
            Check(loaded.InitialStones.Count == count && loaded.InitialPlayer == firstPlayer && loaded.Position == played,
                "Saved handicap SGF lost setup, initial player or played moves");
            foreach (Move stone in loaded.InitialStones) Check(stone.Color == recipient && loaded.Board[stone.X, stone.Y] == recipient, "SGF reload changed setup color or placement");
            Check(played == 0 || loaded.Moves[0].Color == firstPlayer, "SGF reload changed the color of the first actual move");
            form.LoadGame(loaded);
            Check(form.CurrentGame.ToSgf() == before, "Saving and loading a handicap game changed its SGF");
            Check((int)Field(form, "handicap") == 0 && (Stone)Field(form, "handicapColor") == Stone.Black,
                "Imported setup incorrectly became pending New Game handicap settings");
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
        private static void SetField(object instance, string name, object value)
        {
            instance.GetType().GetField(name, PrivateInstance).SetValue(instance, value);
        }
        private static object Call(MainForm form, string method, params object[] args)
        {
            return typeof(MainForm).GetMethod(method, PrivateInstance).Invoke(form, args);
        }
        private static void Check(bool condition, string message)
        {
            assertionCount++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
