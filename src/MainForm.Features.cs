using System;
using System.Windows.Forms;

namespace GoDesk
{
    public partial class MainForm
    {
        private void OpenOnlineRoom()
        {
            if (featureWindowOpen) return;
            using (var room = new OnlineRoomForm(engineSettings.Clone(), Visits > 0 ? Visits : 2048))
            {
                ShowFeatureDialog(room, "online");
                if (room.ReviewGame != null)
                {
                    LoadGame(room.ReviewGame);
                    ShowWorkspace("review");
                }
            }
        }

        private void OpenWholeGameReview()
        {
            if (featureWindowOpen) return;
            using (var report = new WholeGameReviewForm(game, engineSettings.Clone()))
                ShowFeatureDialog(report, "review");
        }

        private void ShowFeatureDialog(Form dialog, string section)
        {
            featureWindowOpen = true;
            CancelAnalysis();
            bool restoreEngine = engine != null;
            if (restoreEngine)
            {
                engineSession++;
                engine.StatusChanged -= engineStatusHandler;
                engine.Dispose();
                engine = null;
            }
            SetNavigationSelection(section);
            try { dialog.ShowDialog(this); }
            finally
            {
                featureWindowOpen = false;
                if (restoreEngine && !closing) InstallEngine(engineSettings, false);
                SetNavigationSelection(currentSection);
                UpdateUi();
            }
        }
    }
}
