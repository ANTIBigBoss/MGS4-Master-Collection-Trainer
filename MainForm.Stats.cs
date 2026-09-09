using System;
using System.Globalization;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm
    {
        private void InitializeStatsControls()
        {
            BindHover(ProjectedRankLabel, "Read-only estimates from your current run, refreshed every second.");
            BindHover(RankTextBox, "Projected emblem using the supplied table's rules. The game's final results may differ.");
            BindHover(DifficultyTextBox, "Current run difficulty.");
            BindHover(PlayTimeTextBox, "Recorded play time in hours, minutes and seconds.");
            BindHover(AlertsTextBox, "Number of alert phases triggered during this run.");
            BindHover(ContinuesTextBox, "Number of continues used during this run.");
            BindHover(RecoveryItemsTextBox, "Number of recovery items used during this run.");
            // These designer controls are Labels despite their TextBox names;
            // they display snapshots and have no handlers that write game values.
            ApplyRankPreview(null);
            InitializeStatsEditors();
        }

        private RankPreview ReadRankPreview(out string error)
        {
            error = null;
            try
            {
                if (!manager.IsEnabled(TableEffect.PlayerPointer))
                {
                    error = "Waiting for the player hook to read projected stats.";
                    return null;
                }
                return manager.Actions.PreviewRank();
            }
            catch (Exception ex)
            {
                // A missing capture must not turn into a fabricated zero-stat run
                // or prevent the independent inventory/cheat readouts refreshing.
                error = "Projected stats unavailable: " + ex.Message;
                return null;
            }
        }

        private void ApplyRankPreview(RankPreview rank)
        {
            const string unavailable = "\u2014";
            SetReadout(RankTextBox, rank == null ? unavailable : rank.PriorityEmblem ?? rank.GridFallback);
            SetReadout(DifficultyTextBox, rank == null ? unavailable : rank.Difficulty);
            SetReadout(PlayTimeTextBox, rank == null ? unavailable : string.Format(CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}", rank.Hours, rank.Minutes, rank.TimeFrames / 60 % 60));
            SetReadout(AlertsTextBox, rank == null ? unavailable : rank.Alerts.ToString(CultureInfo.InvariantCulture));
            SetReadout(ContinuesTextBox, rank == null ? unavailable : rank.Continues.ToString(CultureInfo.InvariantCulture));
            SetReadout(RecoveryItemsTextBox, rank == null ? unavailable : rank.Recoveries.ToString(CultureInfo.InvariantCulture));
        }

        private static void SetReadout(Label control, string value)
        {
            if (control.Text != value) control.Text = value;
        }
    }
}
