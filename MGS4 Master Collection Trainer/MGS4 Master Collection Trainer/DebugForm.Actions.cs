using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class DebugForm
    {
        private ComboBox stageSelection, rankSelection;
        private TextBox actionReadout;

        private void BuildTableActions()
        {
            var page = new TabPage("Stage / rank / unlocks") { UseVisualStyleBackColor = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(1050, 0), Margin = new Padding(3, 3, 3, 14),
                Text = "Readouts and previews only read memory. Rank and unlock actions write once; Disable All does not undo those values. Stage loading runs through the game's own frame hook." }, 0, 0);

            var stageRow = ActionRow();
            stageRow.Controls.Add(new Label { Text = "Stage", AutoSize = true, Margin = new Padding(3, 8, 8, 3) });
            stageSelection = new ComboBox { Name = "StageSelection", DropDownStyle = ComboBoxStyle.DropDownList, Width = 470, DropDownWidth = 700 };
            stageRow.Controls.Add(stageSelection);
            var refreshStages = ActionButton("Refresh stage list", "RefreshStageList");
            refreshStages.Click += async (s, e) => await RefreshStagesAsync();
            stageRow.Controls.Add(refreshStages);
            var loadStage = ActionButton("Load selected stage", "LoadSelectedStage");
            loadStage.Click += async (s, e) =>
            {
                var selected = stageSelection.SelectedItem as StageChoice;
                if (selected == null) { SetResult("Refresh the stage list, then select a destination."); return; }
                await RunOperationAsync("Queuing stage load...", () =>
                {
                    RequireSession(session.Enable(TableEffect.StageLoaderHook));
                    return manager.WithSession((process, handle) =>
                    {
                        if (process.Id != selected.ProcessId || process.StartTime != selected.ProcessStartTime)
                            throw new InvalidOperationException("The game restarted after this stage list was read. Refresh the list and select the destination again.");
                        return manager.Actions.QueueStageLoad(selected.Id).Summary;
                    });
                });
            };
            stageRow.Controls.Add(loadStage);
            layout.Controls.Add(stageRow, 0, 1);

            var rankRow = ActionRow();
            rankRow.Controls.Add(new Label { Text = "Rank target", AutoSize = true, Margin = new Padding(3, 8, 8, 3) });
            rankSelection = new ComboBox { Name = "RankSelection", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            rankSelection.Items.AddRange(new object[] { "BIG BOSS", "FOXHOUND", "FOX", "HOUND", "MANTIS", "WOLF", "RAVEN", "OCTOPUS", "PIGEON" });
            rankSelection.SelectedIndex = 0;
            rankRow.Controls.Add(rankSelection);
            var applyRank = ActionButton("Apply rank requirements", "ApplyRankRequirements");
            applyRank.Click += async (s, e) =>
            {
                int target = rankSelection.SelectedIndex;
                await RunOperationAsync("Applying rank requirements...", () =>
                {
                    RequireSession(session.EnsureValueReady(GameValueDefinitionManager.Get(GameValueId.Health)));
                    manager.Actions.RankTarget = target;
                    return manager.Actions.ApplyRankTarget().Summary;
                });
            };
            rankRow.Controls.Add(applyRank);
            var preview = ActionButton("Preview rank", "PreviewRank");
            preview.Click += async (s, e) => await ReadActionResultAsync("Reading rank requirements...", () => manager.Actions.PreviewRank().Summary);
            rankRow.Controls.Add(preview);
            layout.Controls.Add(rankRow, 0, 2);

            var commands = ActionRow();
            var read = ActionButton("Read stage / act / difficulty / rank", "ReadLiveInfo");
            read.Click += async (s, e) => await ReadActionResultAsync("Reading game information...", () =>
            {
                var live = manager.Actions.ReadLive();
                return "Stage: " + live.Stage + Environment.NewLine + "Act: " + live.Act + Environment.NewLine +
                    "Difficulty: " + live.Difficulty + Environment.NewLine + "Rank: " + live.Rank;
            });
            commands.Controls.Add(read);
            var weapons = ActionButton("Unlock all weapons", "UnlockAllWeapons");
            weapons.Click += async (s, e) => await ReadActionResultAsync("Unlocking weapons...", () => manager.Actions.UnlockAllWeapons().Summary, requirePlayer: false);
            commands.Controls.Add(weapons);
            var items = ActionButton("Unlock all items", "UnlockAllItems");
            items.Click += async (s, e) => await ReadActionResultAsync("Unlocking items...", () => manager.Actions.UnlockAllItems().Summary, requirePlayer: false);
            commands.Controls.Add(items);
            layout.Controls.Add(commands, 0, 3);

            actionReadout = new TextBox { Name = "TableActionReadout", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10F), Margin = new Padding(3, 14, 3, 3),
                Text = "Choose a readout, preview, or action above. Stage list discovery does not load a stage or change your selected stage ID." };
            layout.Controls.Add(actionReadout, 0, 4);
            page.Controls.Add(layout);
            tabs.TabPages.Add(page);
        }

        private async Task RefreshStagesAsync()
        {
            StageChoice[] choices = null;
            int? selectedId = (stageSelection.SelectedItem as StageChoice)?.Id;
            await RunOperationAsync("Reading stage list...", () =>
            {
                var catalogue = manager.Actions.BuildStageList(refresh: true);
                choices = catalogue.Entries.Select(entry => new StageChoice { Id = entry.Id, Label = entry.Name + " (" + entry.Id + ")",
                    ProcessId = catalogue.ProcessId, ProcessStartTime = catalogue.ProcessStartTime }).ToArray();
                return choices.Length == 0 ? "The game's stage registry is empty. Resume gameplay, then refresh the list." : "Read " + choices.Length + " stages. Select a destination, then press Load selected stage.";
            });
            if (!CanUpdate || choices == null) return;
            stageSelection.Items.Clear();
            stageSelection.Items.AddRange(choices);
            if (choices.Length != 0) stageSelection.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selectedId) ?? choices[0];
        }

        private async Task ReadActionResultAsync(string pending, Func<string> action, bool requirePlayer = true)
        {
            string result = null;
            await RunOperationAsync(pending, () =>
            {
                if (requirePlayer) RequireSession(session.EnsureValueReady(GameValueDefinitionManager.Get(GameValueId.Health)));
                result = action();
                return result;
            });
            if (CanUpdate && result != null) actionReadout.Text = result;
        }

        private static FlowLayoutPanel ActionRow() => new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        private static Button ActionButton(string text, string name) => new Button { Name = name, Text = text, AutoSize = true, MinimumSize = new Size(110, 30), Margin = new Padding(3) };
        private sealed class StageChoice
        {
            internal int Id;
            internal int ProcessId;
            internal DateTime ProcessStartTime;
            internal string Label;
            public override string ToString() => Label;
        }
    }
}
