using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm : Form
    {
        private readonly EffectManager manager = EffectManager.Instance;
        private readonly TrainerSessionManager session;
        private readonly Dictionary<CheckBox, TableEffect> effectControls = new Dictionary<CheckBox, TableEffect>();
        private readonly Dictionary<TableEffect, EffectMemoryStatus> effectStates = new Dictionary<TableEffect, EffectMemoryStatus>();
        private readonly Timer statusTimer;
        private readonly Timer resolutionTimer;
        private bool started, busy, operationInProgress, synchronizing, closeRequested, closing, connected;
        private string gameIdentity = string.Empty;
        private string preparedIdentity = string.Empty;
        private string statusText = "Waiting for mgs4.exe...";
        private bool actionStatus;
        private Control hoveredControl;
        private DateTime nextPreparation;
        private StageCatalog availableStages, displayedStages;

        public MainForm()
        {
            DoubleBuffered = true;
            InitializeComponent();
            session = new TrainerSessionManager(manager);
            if (components == null) components = new Container();
            statusTimer = new Timer(components) { Interval = 1000 };
            resolutionTimer = new Timer(components) { Interval = 250 };
            statusTimer.Tick += async (s, e) => await RefreshAsync();
            resolutionTimer.Tick += async (s, e) => await PulseResolutionAsync();

            BindEffect(InfiniteLifeCheckBox, TableEffect.InfiniteLife, "Keeps Snake's life from decreasing.");
            BindEffect(InfiniteAmmoCheckBox, TableEffect.InfiniteAmmo, "Prevents ammunition from being consumed.");
            BindEffect(InfiniteSuppressorCheckBox, TableEffect.InfiniteSuppressor, "Prevents suppressor durability from decreasing.");
            BindEffect(NeverReloadCheckBox, TableEffect.NeverReload, "Stops the magazine count from decreasing.");
            BindEffect(NoStressCheckBox, TableEffect.NoStress, "Stops stress gain; it does not reset existing stress.");
            BindEffect(InfiniteCamoCheckBox, TableEffect.Always100PercentCamo, "Forces the camouflage calculation to 100. The game may still display 99%.");
            BindEffect(DisableMotionBlurCheckBox, TableEffect.DisableMotionBlur, "Disables the game's motion blur. Area change required for the change to take effect.");
            BindEffect(DisablePissFilterCheckBox, TableEffect.DisableScreenFilter, "Disables the game's screen colour filter. Area change required for the change to take effect.");
            BindEffect(DisableResolutionScalingCheckBox, TableEffect.DisableResolutionScaling,
                "Keeps dynamic resolution disabled. Uncheck to enable scaling again. Area change required for the change to take effect.");
            InitializeInventoryControls();
            InitializeStatsControls();
            InitializeVitalsControls();
            InitializeDebugControls();
            InitializeMainMenus();
            InitializeTextBoxSizing(this);
            RefreshStageListButton.Click += async (s, e) => await RefreshStagesAsync();
            button3.Click += async (s, e) => await ChangeStageAsync();
            comboBox1.SelectedIndexChanged += (s, e) => SetInteractive();
            BindHover(RefreshStageListButton, "Reads the stages currently registered by the game.");
            BindHover(comboBox1, "Choose a destination, then press Change to Selected Stage.");
            BindHover(button3, "Loads the selected stage. Read the inventory warning before using this.");
            SetInteractive();
        }

        private bool CanUpdate => started && !closeRequested && !closing && !IsDisposed && !Disposing;

        private void InitializeTextBoxSizing(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is NumericUpDown numeric)
                {
                    int minimumWidth = numeric.Width;
                    Action fit = () =>
                    {
                        if (numeric.IsDisposed) return;
                        int textWidth = TextRenderer.MeasureText(numeric.Text, numeric.Font,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                        int buttonWidth = numeric.Controls.Cast<Control>().Where(control => !(control is TextBox))
                            .Select(control => control.Width).DefaultIfEmpty(16).Max();
                        int width = Math.Max(minimumWidth, textWidth + buttonWidth + numeric.Width - numeric.ClientSize.Width + 2);
                        if (numeric.Width != width) numeric.Width = width;
                    };
                    numeric.TextChanged += (sender, args) => fit();
                    numeric.FontChanged += (sender, args) => fit();
                    numeric.HandleCreated += (sender, args) => fit();
                    fit();
                    // The native spinner lays out its own edit box and arrow buttons.
                    continue;
                }
                if (child is TextBox textBox && !textBox.Multiline)
                {
                    int minimumWidth = textBox.Width;
                    Action fit = () =>
                    {
                        if (textBox.IsDisposed) return;
                        // Measure with the actual font, including glyph padding. Leave
                        // space for native borders and the caret without changing height
                        // or position, and retain the designer's width as the minimum.
                        int textWidth = TextRenderer.MeasureText(textBox.Text, textBox.Font,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                        int width = Math.Max(minimumWidth, textWidth + textBox.Width - textBox.ClientSize.Width + 2);
                        if (textBox.Width != width) textBox.Width = width;
                    };
                    textBox.TextChanged += (sender, args) => fit();
                    textBox.FontChanged += (sender, args) => fit();
                    textBox.HandleCreated += (sender, args) => fit();
                    fit();
                }
                if (child.HasChildren) InitializeTextBoxSizing(child);
            }
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            try
            {
                string currentVersion = Application.ProductVersion.Split('+')[0];

                bool updateAvailable = await Task.Run(() =>
                    VersionSupport.CheckIfNewUpdateExists(currentVersion));

                if (updateAvailable)
                {
                    DialogResult result = MessageBox.Show(
                        "A new version of MGS4 Master Collection Trainer is available.\n\n" +
                        "Would you like to update now?",
                        "Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        string appDirectory = AppContext.BaseDirectory.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar);

                        string updaterPath = Path.Combine(
                            Directory.GetParent(appDirectory).FullName,
                            "AutoUpdater.exe");

                        // Download Big Daddy Sage's updater if it isn't already present.
                        if (!File.Exists(updaterPath))
                        {
                            await Task.Run(() =>
                                VersionSupport.DownloadAutoUpdater());
                        }

                        VersionSupport.StartAutoUpdater();

                        Close();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // An update-check failure should never prevent the trainer from opening.
                LoggingManager.Instance.Log("Update check failed: " + ex);
            }

            started = true;
            BeginInitializationWindow();
            SetStatus("Checking existing hooks and preparing the trainer...");
            await RefreshAsync();

            if (CanUpdate)
            {
                statusTimer.Start();
                resolutionTimer.Start();
            }
        }

        private void BindEffect(CheckBox control, TableEffect effect, string description)
        {
            effectControls.Add(control, effect);
            BindHover(control, description);
            control.CheckedChanged += async (s, e) =>
            {
                if (synchronizing || operationInProgress || !CanUpdate) return;
                bool enable = control.Checked;
                string name = control.Text;
                string expectedGame = gameIdentity;
                await RunOperationAsync((enable ? "Enabling " : "Disabling ") + name + "...", () =>
                    WithCurrentGame(expectedGame, () =>
                    {
                        if (effect == TableEffect.DisableResolutionScaling && !enable)
                            return manager.Actions.RestoreResolutionScaling().Summary;
                        Require(enable ? session.Enable(effect) : session.Disable(effect), session.LastError);
                        return name + (enable ? " enabled." : " disabled.");
                    }));
            };
        }

        private async Task RefreshAsync(bool preserveStatus = false, bool completingOperation = false)
        {
            if (!CanUpdate || busy || (operationInProgress && !completingOperation)) return;
            busy = true;
            int reportVersion = ++initializationReportVersion;
            bool trackInitialization = !initializationComplete;
            var progress = new Progress<InitializationProgress>(update =>
            {
                // Only the final snapshot may close setup, after every current value
                // has been rechecked. Intermediate reports can retain earlier progress.
                if (!update.IsComplete && CanUpdate && reportVersion == initializationReportVersion)
                    ApplyInitializationProgress(update);
            });
            try
            {
                MainFormSnapshot snapshot = await Task.Run(() => ReadSnapshot(progress, trackInitialization));
                ++initializationReportVersion; // Ignore queued intermediate reports after the final snapshot.
                if (CanUpdate) ApplySnapshot(snapshot, preserveStatus);
            }
            catch (Exception ex)
            {
                ++initializationReportVersion;
                if (CanUpdate)
                {
                    connected = false;
                    ClearInventory();
                    ClearStatsEditors();
                    ApplyVitalsSnapshot(null);
                    ApplyRankPreview(null);
                    ApplyEffectStates(new EffectMemoryStatus[0]);
                    SetStatus("Could not read game state: " + ex.Message);
                    if (!initializationComplete)
                        ApplyInitializationProgress(new InitializationProgress(initializationStatus?.CompletedSteps ?? 0,
                            InitializationManager.TotalSteps, "Initialization paused: " + ex.Message));
                }
            }
            finally { busy = false; if (CanUpdate) SetInteractive(); }
        }

        private MainFormSnapshot ReadSnapshot(IProgress<InitializationProgress> progress, bool trackInitialization)
        {
            string identity = CurrentGameIdentity();
            if (identity.Length == 0)
            {
                // Let the manager discard a terminated process and its captured addresses.
                manager.IsEnabled(TableEffect.InventoryPointer);
                preparedIdentity = string.Empty;
                pendingInitialization = null;
                availableStages = null;
                return new MainFormSnapshot
                {
                    Message = "Waiting for mgs4.exe...",
                    Initialization = new InitializationManager(progress).Build()
                };
            }

            return WithCurrentGame(identity, () =>
            {
                bool newGame = identity != preparedIdentity;
                bool reportInitialization = trackInitialization || newGame;
                var initialization = new InitializationManager(reportInitialization ? progress : null,
                    newGame ? null : pendingInitialization);
                pendingInitialization = initialization;
                initialization.SetStep(0, true);
                var preparationErrors = new List<string>();
                if (newGame)
                {
                    availableStages = null;
                    initialization.Report("Checking existing trainer settings...");
                    manager.InspectExistingEffects();
                    Require(manager.LastError.Length == 0, manager.LastError);
                    preparedIdentity = identity;
                }
                initialization.SetStep(1, true);
                bool attemptPreparation = !debugSessionOwnsPreparation && (newGame || DateTime.UtcNow >= nextPreparation);
                if (attemptPreparation) nextPreparation = DateTime.UtcNow.AddSeconds(5);
                // Only the providers needed by this form are prepared. Cheats remain opt-in.
                TableEffect[] prerequisites = { TableEffect.PlayerPointer, TableEffect.InventoryPointer, TableEffect.StageLoaderHook };
                string[] descriptions = { "Preparing player data...", "Preparing inventory data...", "Preparing stage controls..." };
                for (int index = 0; index < prerequisites.Length; index++)
                {
                    bool ready = manager.IsEnabled(prerequisites[index]);
                    string error = null;
                    if (!ready && attemptPreparation)
                    {
                        initialization.Report(descriptions[index]);
                        ready = session.Enable(prerequisites[index]);
                        if (!ready) { error = session.LastError; preparationErrors.Add(error); }
                    }
                    initialization.SetStep(index + 2, ready, error);
                }
                string graphicsError = null;
                if (manager.Actions.ResolveSymbol("dynResEnabled") == IntPtr.Zero && attemptPreparation)
                {
                    initialization.Report("Preparing graphics controls...");
                    try { manager.Actions.PrepareResolutionScaling(); }
                    catch (Exception ex) { graphicsError = ex.Message; preparationErrors.Add(graphicsError); }
                }
                initialization.SetStep(5, manager.Actions.ResolveSymbol("dynResEnabled") != IntPtr.Zero, graphicsError);
                string stageError = null;
                if ((availableStages == null || availableStages.Entries.Count == 0) && attemptPreparation)
                {
                    initialization.Report("Reading the available stages...");
                    try { availableStages = manager.Actions.BuildStageList(refresh: true); }
                    catch (Exception ex) { stageError = ex.Message; preparationErrors.Add(stageError); }
                }
                initialization.SetStep(6, availableStages != null && availableStages.Entries.Count != 0, stageError);
                initialization.Report("Reading live player and inventory values...");
                InventorySnapshot inventory = ReadInventorySnapshot();
                RunStatSnapshot stats = ReadStatsSnapshot();
                VitalsSnapshot vitals = ReadVitalsSnapshot();
                initialization.SetStep(7, stats.IsReady && vitals.IsReady);
                initialization.SetStep(8, inventory.IsReady);
                string rankError = null;
                RankPreview rank = stats.Rank ?? ReadRankPreview(out rankError);
                initialization.Report("Checking which cheats are available...");
                EffectMemoryStatus[] effects = effectControls.Values.Select(manager.GetObservedState).ToArray();
                bool effectsReady = effects.All(effect => effect.State == EffectMemoryState.Active || effect.State == EffectMemoryState.Inactive);
                initialization.SetStep(9, effectsReady, effects.FirstOrDefault(effect =>
                    effect.State != EffectMemoryState.Active && effect.State != EffectMemoryState.Inactive)?.Detail);
                return new MainFormSnapshot
                {
                    GameIdentity = identity,
                    Connected = true,
                    Effects = effects,
                    Inventory = inventory,
                    Rank = rank,
                    Stats = stats,
                    Vitals = vitals,
                    Stages = availableStages,
                    Initialization = reportInitialization ? initialization.Build() : null,
                    Message = preparationErrors.Count != 0 ? preparationErrors[0] :
                        !inventory.IsReady ? inventory.Error : !stats.IsReady ? "Stats unavailable: " + stats.Error :
                        !vitals.IsReady ? vitals.Error :
                        rankError ?? "Ready. Hover over something for more info."
                };
            });
        }

        internal void ApplySnapshot(MainFormSnapshot snapshot, bool preserveStatus = false)
        {
            bool changedGame = gameIdentity != snapshot.GameIdentity;
            bool connectionChanged = connected != snapshot.Connected;
            if (changedGame)
            {
                gameIdentity = snapshot.GameIdentity;
                ClearInventory();
                displayedStages = null;
                comboBox1.Items.Clear();
                comboBox1.Text = string.Empty;
            }
            connected = snapshot.Connected;
            ApplyEffectStates(snapshot.Effects);
            ApplyInventorySnapshot(snapshot.Inventory);
            ApplyStatsSnapshot(snapshot.Connected ? snapshot.Stats : null);
            ApplyVitalsSnapshot(snapshot.Connected ? snapshot.Vitals : null);
            ApplyRankPreview(snapshot.Connected ? snapshot.Rank : null);
            ApplyStageCatalog(snapshot.Stages);
            ApplyInitializationProgress(snapshot.Initialization);
            if ((!preserveStatus || changedGame || !connected) && !operationInProgress)
                SetPassiveStatus(snapshot.Message, changedGame || connectionChanged || !connected ||
                    snapshot.Inventory == null || !snapshot.Inventory.IsReady || (snapshot.Stats != null && !snapshot.Stats.IsReady) ||
                    (snapshot.Vitals != null && !snapshot.Vitals.IsReady));
            SetInteractive();
        }

        internal void ApplyEffectStates(IEnumerable<EffectMemoryStatus> states)
        {
            effectStates.Clear();
            foreach (EffectMemoryStatus state in states) effectStates[state.Effect] = state;
            synchronizing = true;
            try
            {
                foreach (var item in effectControls)
                {
                    EffectMemoryState state = effectStates.TryGetValue(item.Value, out EffectMemoryStatus observed)
                        ? observed.State : EffectMemoryState.NotChecked;
                    item.Key.CheckState = state == EffectMemoryState.Active ? CheckState.Checked :
                        state == EffectMemoryState.Inactive || !connected ? CheckState.Unchecked : CheckState.Indeterminate;
                }
            }
            finally { synchronizing = false; }
        }

        private async Task<bool> RunOperationAsync(string pending, Func<string> operation)
        {
            if (!CanUpdate || operationInProgress) return false;
            operationInProgress = true;
            SetInteractive();
            SetStatus(pending);
            bool succeeded = false;
            bool ownsWorker = false;
            try
            {
                // A click can arrive while a read/pulse is in progress. Keep the request and
                // its captured inputs, then execute after that work has finished.
                while (busy && CanUpdate) await Task.Delay(25);
                if (!CanUpdate) return false;
                busy = true;
                ownsWorker = true;
                string result = await Task.Run(operation);
                succeeded = true;
                if (CanUpdate) SetStatus(result);
            }
            catch (Exception ex)
            {
                LoggingManager.Instance.Log("Main form operation failed: " + ex);
                if (CanUpdate) SetStatus("Could not complete operation: " + ex.Message);
            }
            finally
            {
                if (ownsWorker) busy = false;
                // Keep editors disabled until readback finishes, so a new draft cannot be
                // overwritten by the completion of the preceding Set operation.
                if (CanUpdate && ownsWorker) await RefreshAsync(preserveStatus: true, completingOperation: true);
                operationInProgress = false;
                if (CanUpdate) SetInteractive();
            }
            return succeeded;
        }

        private async Task RefreshStagesAsync()
        {
            string expectedGame = gameIdentity;
            await RunOperationAsync("Reading the stage list...", () => WithCurrentGame(expectedGame, () =>
            {
                availableStages = manager.Actions.BuildStageList(refresh: true);
                return availableStages.Entries.Count == 0 ? "Load into gameplay, then refresh the stage list." :
                    "Stage list refreshed. Select a destination to load.";
            }));
        }

        private async Task ChangeStageAsync()
        {
            StageChoice selected = comboBox1.SelectedItem as StageChoice;
            if (selected == null) { SetStatus("Select a stage from the list first."); return; }
            await RunOperationAsync("Queuing the selected stage...", () => WithCurrentGame(selected.GameIdentity, () =>
            {
                Require(session.Enable(TableEffect.StageLoaderHook), session.LastError);
                return manager.Actions.QueueStageLoad(selected.Id).Summary;
            }));
        }

        private void ApplyStageCatalog(StageCatalog catalogue)
        {
            if (catalogue == null || ReferenceEquals(catalogue, displayedStages)) return;
            string identity = Identity(catalogue.ProcessId, catalogue.ProcessStartTime);
            if (identity != gameIdentity) return;
            int? selected = (comboBox1.SelectedItem as StageChoice)?.Id;
            StageChoice[] choices = catalogue.Entries.Select(entry => new StageChoice
                { Id = entry.Id, GameIdentity = identity, Label = entry.Name + " (" + entry.Id + ")" }).ToArray();
            comboBox1.Items.Clear();
            comboBox1.Items.AddRange(choices);
            comboBox1.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices.FirstOrDefault();
            displayedStages = catalogue;
        }

        private async Task PulseResolutionAsync()
        {
            if (!CanUpdate || busy || operationInProgress || !connected || !manager.Actions.ResolutionScalingDisabled) return;
            busy = true;
            try
            {
                await Task.Run(() => manager.Actions.PulseResolutionScaling());
            }
            catch (Exception ex)
            {
                // Stop-only cleanup does not undo a flag we can no longer safely read.
                manager.Actions.SetResolutionScalingDisabled(false);
                if (CanUpdate) SetStatus("Resolution scaling writes stopped: " + ex.Message);
            }
            finally { busy = false; }
        }

        private void SetInteractive()
        {
            bool available = connected && !operationInProgress && !closeRequested && !closing;
            foreach (var item in effectControls)
                item.Key.Enabled = available && effectStates.TryGetValue(item.Value, out EffectMemoryStatus state) &&
                    (state.State == EffectMemoryState.Active || state.State == EffectMemoryState.Inactive);
            SetInventoryInteractive(available);
            SetStatsInteractive(available);
            SetVitalsInteractive();
            SetDebugInteractive();
            RefreshStageListButton.Enabled = available;
            comboBox1.Enabled = available;
            button3.Enabled = available && comboBox1.SelectedItem is StageChoice;
        }

        private void SetStatus(string message)
        {
            // A clicked action must report its result even while the mouse is over
            // its button. Keep that result through ordinary background refreshes.
            actionStatus = true;
            statusText = string.IsNullOrWhiteSpace(message) ? "Waiting for the game..." : message;
            TooltipHoverLabel.Text = statusText;
        }

        private void SetPassiveStatus(string message, bool force)
        {
            if (actionStatus && !force) return;
            actionStatus = false;
            statusText = string.IsNullOrWhiteSpace(message) ? "Waiting for the game..." : message;
            if (hoveredControl == null || force) TooltipHoverLabel.Text = statusText;
        }

        private void BindHover(Control control, string description) => BindHover(control, () => description);

        private void BindHover(Control control, Func<string> description)
        {
            control.MouseEnter += (s, e) =>
            {
                hoveredControl = control;
                string detail = description();
                if (control is CheckBox check && effectControls.TryGetValue(check, out TableEffect effect) &&
                    effectStates.TryGetValue(effect, out EffectMemoryStatus state) &&
                    state.State != EffectMemoryState.Active && state.State != EffectMemoryState.Inactive)
                    detail = state.Detail;
                TooltipHoverLabel.Text = detail;
            };
            control.MouseLeave += (s, e) =>
            {
                if (hoveredControl != control) return;
                hoveredControl = null;
                TooltipHoverLabel.Text = statusText;
            };
        }

        private T WithCurrentGame<T>(string expectedIdentity, Func<T> action)
        {
            if (string.IsNullOrEmpty(expectedIdentity)) throw new InvalidOperationException("Wait for mgs4.exe to attach first.");
            return manager.WithSession((process, handle) =>
            {
                if (Identity(process.Id, process.StartTime) != expectedIdentity)
                    throw new InvalidOperationException("The game restarted. Wait for its current values to load, then retry.");
                return action();
            });
        }

        private static string CurrentGameIdentity()
        {
            using (Process process = MemoryManager.GetGameProcess())
                return process == null || process.HasExited ? string.Empty : Identity(process.Id, process.StartTime);
        }

        private static string Identity(int processId, DateTime startTime) =>
            MemoryTransactionManager.GetSessionIdentity(processId, startTime);
        private static void Require(bool succeeded, string error)
        {
            if (!succeeded) throw new InvalidOperationException(error);
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (closing || !started) { base.OnFormClosing(e); return; }
            e.Cancel = true;
            base.OnFormClosing(e);
            if (closeRequested) return;
            closeRequested = true;
            ++initializationReportVersion;
            CloseInitializationWindow();
            statusTimer.Stop();
            resolutionTimer.Stop();
            SetInteractive();
            SetStatus("Finishing the current operation and restoring hooks...");
            try
            {
                while (busy || operationInProgress || processingVitalWrites) await Task.Delay(25);
                await CloseDebugFormAsync();
                await Task.Run(() => Require(manager.DisableAll(), manager.LastError));
            }
            catch (Exception ex)
            {
                // Program performs its final cleanup attempt too. Verified leftover hooks
                // can be recovered on reopen; a foreign patch must not trap the window open.
                LoggingManager.Instance.Log("Main form cleanup is incomplete: " + ex);
            }
            finally
            {
                closing = true;
                if (!IsDisposed) Close();
            }
        }

        internal sealed class MainFormSnapshot
        {
            internal string GameIdentity = string.Empty;
            internal bool Connected;
            internal EffectMemoryStatus[] Effects = new EffectMemoryStatus[0];
            internal InventorySnapshot Inventory;
            internal RankPreview Rank;
            internal RunStatSnapshot Stats;
            internal VitalsSnapshot Vitals;
            internal StageCatalog Stages;
            internal InitializationProgress Initialization;
            internal string Message;
        }

        private sealed class StageChoice
        {
            internal int Id;
            internal string GameIdentity, Label;
            public override string ToString() => Label;
        }
    }
}
