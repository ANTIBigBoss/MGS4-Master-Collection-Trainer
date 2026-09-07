using System;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm
    {
        private InitializationProgressForm initializationWindow;
        private InitializationProgress initializationStatus;
        private bool initializationComplete, initializationDismissed;
        private int initializationReportVersion;
        private InitializationManager pendingInitialization;

        private void BeginInitializationWindow()
        {
            initializationDismissed = false;
            LocationChanged += (sender, args) => PositionInitializationWindow();
            SizeChanged += (sender, args) => PositionInitializationWindow();
            ApplyInitializationProgress(new InitializationProgress(0, InitializationManager.TotalSteps,
                "Looking for MGS4. Please start mgs4.exe."));
        }

        internal void ApplyInitializationProgress(InitializationProgress progress)
        {
            if (progress == null || IsDisposed || Disposing || closeRequested || closing) return;
            if (initializationComplete && !progress.IsComplete) initializationDismissed = false;
            initializationStatus = progress;
            initializationComplete = progress.IsComplete;
            if (initializationComplete) { CloseInitializationWindow(); return; }
            if (!CanUpdate || initializationDismissed) return;

            if (initializationWindow == null || initializationWindow.IsDisposed)
            {
                var window = new InitializationProgressForm();
                initializationWindow = window;
                window.FormClosed += (sender, args) =>
                {
                    if (!ReferenceEquals(initializationWindow, window)) return;
                    initializationWindow = null;
                    initializationDismissed = true;
                };
                window.ApplyProgress(progress);
                window.PositionBeside(this);
                window.Show(this);
            }
            else initializationWindow.ApplyProgress(progress);
        }

        private void PositionInitializationWindow()
        {
            if (WindowState != FormWindowState.Minimized)
                initializationWindow?.PositionBeside(this);
        }

        private void CloseInitializationWindow()
        {
            InitializationProgressForm window = initializationWindow;
            initializationWindow = null;
            if (window == null || window.IsDisposed) return;
            window.Close();
            window.Dispose();
        }
    }
}
