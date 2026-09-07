using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    internal sealed class InitializationProgressForm : Form
    {
        private readonly Label statusLabel;
        private readonly Label stepsLabel;
        private readonly ProgressBar progressBar;

        internal InitializationProgressForm()
        {
            Text = "Preparing MGS4 Trainer";
            Name = nameof(InitializationProgressForm);
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(360, 218);
            BackColor = Color.FromArgb(10, 17, 11);
            ForeColor = Color.FromArgb(208, 209, 178);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            DoubleBuffered = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = BackColor
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            statusLabel = new Label
            {
                Name = "InitializationStatus",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Text = "Waiting for MGS4...",
                TextAlign = ContentAlignment.TopLeft
            };
            progressBar = new ProgressBar
            {
                Name = "InitializationProgressBar",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Minimum = 0,
                Maximum = InitializationManager.TotalSteps,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };
            stepsLabel = new Label
            {
                Name = "InitializationSteps",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 0),
                Text = "0 of " + InitializationManager.TotalSteps + " steps complete (0%)",
                TextAlign = ContentAlignment.TopLeft
            };
            var instructionsLabel = new Label
            {
                Name = "InitializationInstructions",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Text = "Start MGS4 (mgs4.exe), load into gameplay, and open the item menu if inventory data is still loading.",
                TextAlign = ContentAlignment.TopLeft
            };
            layout.Controls.Add(statusLabel, 0, 0);
            layout.Controls.Add(progressBar, 0, 1);
            layout.Controls.Add(stepsLabel, 0, 2);
            layout.Controls.Add(instructionsLabel, 0, 3);
            Controls.Add(layout);
        }

        protected override bool ShowWithoutActivation => true;

        internal void ApplyProgress(InitializationProgress progress)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (IsDisposed || Disposing) return;

            progressBar.Maximum = progress.TotalSteps;
            progressBar.Value = progress.CompletedSteps;
            statusLabel.Text = progress.Message;
            int percentage = (int)(100L * progress.CompletedSteps / progress.TotalSteps);
            stepsLabel.Text = string.Format(CultureInfo.CurrentCulture,
                "{0} of {1} steps complete ({2}%)", progress.CompletedSteps, progress.TotalSteps, percentage);
        }

        internal void PositionBeside(Form owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (owner.IsDisposed || IsDisposed || Disposing) return;

            Rectangle area = Screen.FromControl(owner).WorkingArea;
            const int gap = 12;
            int right = owner.Right + gap;
            int left = owner.Left - Width - gap;
            int x = right + Width <= area.Right ? right : left >= area.Left ? left : right;
            x = Math.Max(area.Left, Math.Min(x, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(owner.Top, area.Bottom - Height));
            Location = new Point(x, y);
        }
    }
}
