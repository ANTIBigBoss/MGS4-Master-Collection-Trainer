using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm
    {
        private sealed class StatEditor
        {
            internal GameValueId Id;
            internal TextBox Control;
            internal string Name;
            internal bool IsTime, Combined;
            internal string OriginalText;
            internal ulong OriginalValue;
        }

        private readonly List<StatEditor> statEditors = new List<StatEditor>();
        private RunStatSnapshot statsSnapshot, statsDraftSnapshot;
        private bool statsLiveTracking, statsPopulated, renderingStats;

        private void InitializeStatsEditors()
        {
            AddStatEditor(PlayTimeTextBoxEdit, GameValueId.TotalPlayTimeTicks, "Play Time", true, label: PlayTimeLabelEdit);
            AddStatEditor(AlertsTextBoxEdit, GameValueId.AlertsTriggered, "Alerts");
            AddStatEditor(ContinuesTextBoxEdit, GameValueId.Continues, "Continues");
            AddStatEditor(RecoveryItemsTextBoxEdit, GameValueId.RecoveriesUsed, "Recovery Items Used");
            AddStatEditor(KnockoutsTextBox, GameValueId.KnifeStuns, "Knife Knockouts");
            AddStatEditor(KnifeKillsTextBox, GameValueId.KnifeKills, "Knife Kills");
            AddStatEditor(HeadshotsTextbox, GameValueId.Headshots, "Headshots");
            AddStatEditor(CqcUsesTextBox, GameValueId.CQCUses, "CQC Uses");
            AddStatEditor(CombatHighsTextBox, GameValueId.CombatHighs, "Combat Highs");
            AddStatEditor(ItemPickupsTextBox, GameValueId.ItemPickups, "Item Pickups");
            AddStatEditor(ItemsDonatedTextBox, GameValueId.ItemsGifted, "Items Donated");
            AddStatEditor(PraisesTextBox, GameValueId.Praises, "Praises");
            AddStatEditor(BodySearchesTextBox, GameValueId.BodySearches, "Body Searches");
            AddStatEditor(HoldUpsTextBox, GameValueId.HoldUps, "Hold-Ups");
            AddStatEditor(ScanningPlugUsesTextBox, GameValueId.ScanningPlugUses, "Scanning Plug Uses");
            AddStatEditor(SyringeUsesTextBox, GameValueId.SyringeUses, "Syringe Uses");
            AddStatEditor(WeaponPickupsTextBox, GameValueId.WeaponPickups, "Weapon Pickups");
            AddStatEditor(FlashbacksTextBox, GameValueId.FlashbacksViewed, "Flashbacks Viewed");
            AddStatEditor(BoxDrumTimeTextBox, GameValueId.BoxDrumTimePart1, "Box/Drum Time", true, true, BoxDrumTimeLabel);
            AddStatEditor(WallPressTimeTextBox, GameValueId.TimeAgainstWall, "Wall-press Time", true, label: WallPressTimeLabel);
            AddStatEditor(ForwardRollsTextBox, GameValueId.Rolls, "Forward Rolls");
            AddStatEditor(ProneSideRollsTextBox, GameValueId.ProneRolls, "Prone Side Rolls");
            AddStatEditor(CrawlTimeTextBox, GameValueId.TimeProne, "Crawl Time", true, label: CrawlTimeLabel);
            AddStatEditor(CrouchTimeTextBox, GameValueId.TimeCrouching, "Crouch Time", true, label: CrouchTimeLabel);
            AddStatEditor(EmotionPagesTextBox, GameValueId.EmotionMagazinePages, "Emotion Magazine Pages");
            AddStatEditor(PlayboyPagesTextBox, GameValueId.PlayboyPagesTurned, "Playboy Pages Turned");

            DifficultyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            DifficultyComboBox.DisplayMember = nameof(GameValueChoice.Description);
            DifficultyComboBox.Items.Clear();
            DifficultyComboBox.Items.AddRange(GameValueDefinitionManager.Get(GameValueId.CurrentDifficulty).Choices.Cast<object>().ToArray());
            DifficultyComboBox.DropDownWidth = Math.Max(DifficultyComboBox.Width,
                DifficultyComboBox.Items.Cast<GameValueChoice>().Max(choice =>
                    TextRenderer.MeasureText(choice.Description, DifficultyComboBox.Font).Width) + SystemInformation.VerticalScrollBarWidth + 8);
            // Only a user-committed selection writes difficulty. Loading a snapshot never does.
            DifficultyComboBox.SelectionChangeCommitted += async (sender, args) => await ChangeDifficultyAsync();
            BindHover(DifficultyComboBox, "Select a difficulty to apply it immediately. An area change may be required for it to take effect.");
            BindHover(DifficultyLabelEdit, "Difficulty changes apply when selected. An area change may be required for them to take effect.");
            ChangeAllStatsValuesButton.Click += async (sender, args) => await ApplyAllStatsAsync();
            RefreshStatsButton.Click += async (sender, args) => await RefreshStatsAsync();
            DisableEditingAndTrackLiveStats.Click += (sender, args) => ToggleStatsTracking();
            BindHover(ChangeAllStatsValuesButton, "Apply and verify every displayed stat value. All entries are validated before any write.");
            BindHover(RefreshStatsButton, "Replace your stat edits with the current game values.");
            BindHover(DisableEditingAndTrackLiveStats, "Switch between manual editing and read-only live tracking every second. Entering live mode replaces pending stat edits.");
            ClearStatsEditors();
        }

        private void AddStatEditor(TextBox control, GameValueId id, string name, bool time = false, bool combined = false, Label label = null)
        {
            statEditors.Add(new StatEditor { Control = control, Id = id, Name = name, IsTime = time, Combined = combined });
            string detail = time ? "Enter time as HH:MM:SS, then use Change All."
                : "Enter a whole number from 0 to 65535, then use Change All.";
            BindHover(control, name + ": " + detail);
            if (label != null)
                BindHover(label, () => name + ": " + (string.IsNullOrEmpty(control.Text) ? "Waiting for player data" : control.Text) + ". " + detail);
        }

        private RunStatSnapshot ReadStatsSnapshot()
        {
            return manager.WithSession((process, handle) =>
            {
                var memory = MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress);
                if (!manager.IsEnabled(TableEffect.PlayerPointer))
                    return new RunStatSnapshot(memory.SessionIdentity, IntPtr.Zero, false,
                        "Waiting for the player hook to read stats.");
                return StatsManager.Read(memory);
            });
        }

        internal void ApplyStatsSnapshot(RunStatSnapshot snapshot, bool refreshEditors = false)
        {
            if (snapshot == null) { ClearStatsEditors(); return; }
            if (snapshot.GameIdentity != gameIdentity) return;
            bool changedCapture = statsSnapshot == null || statsSnapshot.GameIdentity != snapshot.GameIdentity ||
                statsSnapshot.PlayerAddress != snapshot.PlayerAddress;
            if (changedCapture || !snapshot.IsReady) ClearStatsEditors();
            statsSnapshot = snapshot;
            if (snapshot.IsReady && (!statsPopulated || statsLiveTracking || refreshEditors))
            {
                renderingStats = true;
                try
                {
                    foreach (StatEditor editor in statEditors)
                    {
                        ulong value = snapshot.Values[editor.Id];
                        if (editor.Combined) value += snapshot.Values[GameValueId.BoxDrumTimePart2];
                        string text = editor.IsTime ? StatsManager.FormatTime(value) : value.ToString(CultureInfo.InvariantCulture);
                        if (editor.Control.Text != text) editor.Control.Text = text;
                        editor.OriginalText = text;
                        editor.OriginalValue = value;
                    }
                    SetDifficultySelection(snapshot);
                    statsDraftSnapshot = snapshot;
                    statsPopulated = true;
                }
                finally { renderingStats = false; }
            }
            SetStatsInteractive(connected && !operationInProgress && !closeRequested && !closing);
        }

        internal void ClearStatsEditors()
        {
            statsSnapshot = null;
            statsDraftSnapshot = null;
            statsPopulated = false;
            renderingStats = true;
            try
            {
                foreach (StatEditor editor in statEditors)
                {
                    editor.Control.Clear();
                    editor.OriginalText = null;
                    editor.OriginalValue = 0;
                }
                DifficultyComboBox.SelectedIndex = -1;
            }
            finally { renderingStats = false; }
            SetStatsInteractive(false);
        }

        private void SetStatsInteractive(bool available)
        {
            bool ready = available && statsSnapshot != null && statsSnapshot.IsReady && statsPopulated &&
                statsSnapshot.GameIdentity == gameIdentity;
            bool editing = ready && !statsLiveTracking;
            foreach (StatEditor editor in statEditors)
            {
                editor.Control.ReadOnly = !editing;
                editor.Control.Enabled = editing;
            }
            DifficultyComboBox.Enabled = editing;
            ChangeAllStatsValuesButton.Enabled = editing;
            RefreshStatsButton.Enabled = available && !statsLiveTracking;
            DisableEditingAndTrackLiveStats.Enabled = !operationInProgress && !closeRequested && !closing;
        }

        private void ToggleStatsTracking()
        {
            if (operationInProgress || closeRequested || closing) return;
            statsLiveTracking = !statsLiveTracking;
            DisableEditingAndTrackLiveStats.Text = statsLiveTracking
                ? "Enable Editing and Stop Live Tracking" : "Disable Editing and Track Stats Live";
            if (statsLiveTracking) ApplyStatsSnapshot(statsSnapshot, refreshEditors: true);
            SetStatsInteractive(connected);
            SetStatus(statsLiveTracking ? "Stats are read-only and update every second."
                : "Stat editing enabled. Refresh loads current values; Change All applies your edits.");
        }

        internal bool TryReadStatsDraft(out Dictionary<GameValueId, ulong> values, out ulong boxDrumFrames, out string error)
        {
            values = new Dictionary<GameValueId, ulong>();
            boxDrumFrames = 0;
            error = string.Empty;
            if (!statsPopulated || statsDraftSnapshot == null || !statsDraftSnapshot.IsReady)
            { error = "Wait for current stats to load before applying edits."; return false; }
            foreach (StatEditor editor in statEditors)
            {
                ulong value;
                bool parsed;
                // Keep fractional frames intact when a displayed duration wasn't edited.
                if (editor.IsTime && editor.Control.Text == editor.OriginalText)
                { value = editor.OriginalValue; parsed = true; }
                else parsed = editor.IsTime ? StatsManager.TryParseTime(editor.Control.Text, out value)
                    : StatsManager.TryParseCounter(editor.Control.Text, out value);
                ulong maximum = editor.Combined ? StatsManager.MaximumCombinedBoxDrumFrames
                    : editor.IsTime ? uint.MaxValue : ushort.MaxValue;
                if (!parsed || value > maximum)
                {
                    error = editor.Name + (editor.IsTime ? ": Enter HH:MM:SS from 00:00:00 to " + StatsManager.FormatTime(maximum) + "."
                        : ": Enter a whole number from 0 to 65535.");
                    return false;
                }
                if (editor.Combined) boxDrumFrames = value;
                else values.Add(editor.Id, value);
            }
            if (!(DifficultyComboBox.SelectedItem is GameValueChoice difficulty))
            { error = "Select one of the five difficulty levels."; return false; }
            values.Add(GameValueId.CurrentDifficulty, difficulty.Value);
            return true;
        }

        private async Task ApplyAllStatsAsync()
        {
            if (!CanUpdate || operationInProgress || statsLiveTracking) return;
            if (!TryReadStatsDraft(out Dictionary<GameValueId, ulong> values, out ulong combined, out string error))
            { SetStatus(error); return; }
            RunStatSnapshot expected = statsDraftSnapshot;
            bool success = await RunOperationAsync("Applying all displayed stats...", () =>
            {
                ApplyRunStats(expected, values, combined);
                return "All displayed stats applied and verified.";
            });
            if (success && CanUpdate) ApplyStatsSnapshot(statsSnapshot, refreshEditors: true);
        }

        private async Task RefreshStatsAsync()
        {
            if (!CanUpdate || operationInProgress || statsLiveTracking) return;
            string expectedIdentity = gameIdentity;
            bool success = await RunOperationAsync("Refreshing stats...", () => WithCurrentGame(expectedIdentity, () =>
            {
                RunStatSnapshot refreshed = ReadStatsSnapshot();
                Require(refreshed.IsReady, refreshed.Error);
                return "Stats refreshed from the game.";
            }));
            if (success && CanUpdate) ApplyStatsSnapshot(statsSnapshot, refreshEditors: true);
        }

        private async Task ChangeDifficultyAsync()
        {
            if (renderingStats || !CanUpdate || operationInProgress || statsLiveTracking || !statsPopulated ||
                !(DifficultyComboBox.SelectedItem is GameValueChoice choice)) return;
            RunStatSnapshot expected = statsDraftSnapshot;
            await RunOperationAsync("Changing difficulty...", () =>
            {
                ApplyRunStats(expected, new Dictionary<GameValueId, ulong> { { GameValueId.CurrentDifficulty, choice.Value } });
                return "Difficulty set to " + choice.Description + ". An area change may be required for it to take effect.";
            });
            if (CanUpdate) SetDifficultySelection(statsSnapshot);
        }

        private void SetDifficultySelection(RunStatSnapshot snapshot)
        {
            ulong difficulty;
            DifficultyComboBox.SelectedItem = snapshot != null && snapshot.IsReady &&
                snapshot.Values.TryGetValue(GameValueId.CurrentDifficulty, out difficulty)
                ? DifficultyComboBox.Items.Cast<GameValueChoice>().FirstOrDefault(choice => choice.Value == difficulty) : null;
        }

        private void ApplyRunStats(RunStatSnapshot expected, IReadOnlyDictionary<GameValueId, ulong> values, ulong? combined = null)
        {
            manager.WithSession((process, handle) =>
            {
                if (!manager.IsEnabled(TableEffect.PlayerPointer))
                    throw new InvalidOperationException("The player hook is no longer active. Refresh stats before applying.");
                StatsManager.Apply(MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress), expected, values, combined);
                return true;
            });
        }
    }
}
