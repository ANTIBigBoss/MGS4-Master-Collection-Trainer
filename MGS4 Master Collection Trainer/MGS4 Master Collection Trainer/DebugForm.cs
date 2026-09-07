using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class DebugForm : Form
    {
        private readonly EffectManager manager = EffectManager.Instance;
        private readonly TrainerSessionManager session;
        private readonly bool preserveSessionOnClose;
        private readonly TaskCompletionSource<bool> closeCompletion = new TaskCompletionSource<bool>();
        private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<TableEffect, Label> effectStatus = new Dictionary<TableEffect, Label>();
        private bool initialized, operationInProgress, statusInProgress, freezeInProgress, closeRequested, closing;
        private DebugValuesPanel valuesPanel;
        private DataGridView patchesGrid, symbolsGrid, signaturesGrid;
        private ComboBox enemyMode;
        private TextBox alertLevel;
        private CheckBox alertPassthrough, wallsActive;
        private DataGridView prerequisitesGrid;
        private IReadOnlyList<PrerequisiteStatus> prerequisiteStatus;
        private bool prepareOnNewGame = true;
        private string preparedGameIdentity = string.Empty;

        public DebugForm() : this(false)
        {
        }

        internal DebugForm(bool preserveSessionOnClose)
        {
            this.preserveSessionOnClose = preserveSessionOnClose;
            session = new TrainerSessionManager(manager);
            InitializeComponent();
            if (preserveSessionOnClose)
                instructions.Text = "Capture hooks prepare automatically when this page opens. Existing gameplay cheats remain active.\r\n" +
                    "Load gameplay to let the hooks capture their pointers; see Capture status for readiness.\r\n" +
                    "Disable matching Cheat Engine scripts before using this tool. " + ClosingInstructions;
            Disposed += (sender, args) => closeCompletion.TrySetResult(true);
        }

        internal bool IsClosing => closeRequested || closing || IsDisposed;
        private string ClosingInstructions => preserveSessionOnClose
            ? "Closing this tool clears freezes and keeps enabled effects active."
            : "Closing this panel restores effects and clears freezes.";

        internal Task CloseForOwnerAsync()
        {
            if (IsDisposed) return Task.CompletedTask;
            Close();
            return closeCompletion.Task;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!DesignMode) InitializeDebugControls();
        }

        // Runtime rows leave the standard outer form editable in the WinForms designer.
        private void InitializeDebugControls()
        {
            if (initialized) return;
            initialized = true;
            BuildEffects();
            effectsTab.Text = "Gameplay effects (" + effectStatus.Count + ")";
            valuesTab.Text = "Values (" + GameValueDefinitionManager.All.Count + ")";
            valuesPanel = new DebugValuesPanel { Dock = DockStyle.Fill, Manager = manager, Session = session, RunOperationAsync = RunOperationAsync };
            valuesTab.Controls.Add(valuesPanel);
            BuildMemoryComparison();
            BuildSignatures();
            BuildPrerequisites();
            BuildTableActions();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await PrepareSessionAsync();
            if (!CanUpdate) return;
            statusTimer.Start();
            freezeTimer.Start();
            await RefreshEffectStatusAsync();
        }

        private static bool IsCaptureOnly(TableEffect effect) => effect == TableEffect.PlayerPointer ||
            effect == TableEffect.InventoryPointer || effect == TableEffect.ActorCollector || effect == TableEffect.AmmoPointer || effect == TableEffect.StageLoaderHook;

        private void BuildPrerequisites()
        {
            var page = new TabPage("Capture status") { UseVisualStyleBackColor = true };
            var area = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
            area.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            area.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "These prerequisites are prepared automatically when this page opens or the game restarts.\r\nA hook can be installed while waiting for gameplay to supply its pointer; the details below explain what is still needed." }, 0, 0);
            prerequisitesGrid = Grid("prerequisitesGrid", "Capture / prerequisite", "State", "Details");
            prerequisitesGrid.Columns[0].FillWeight = 24;
            prerequisitesGrid.Columns[1].FillWeight = 18;
            prerequisitesGrid.Columns[2].FillWeight = 58;
            prerequisitesGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            prerequisitesGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            foreach (TableEffect effect in new[] { TableEffect.PlayerPointer, TableEffect.InventoryPointer, TableEffect.ActorCollector,
                TableEffect.AmmoPointer, TableEffect.EnemyControl, TableEffect.ForceAlertLevel, TableEffect.StageLoaderHook, TableEffect.DisableResolutionScaling })
                prerequisitesGrid.Rows.Add(EffectName(effect), "Not prepared", "Prepared when the debugger opens.");
            area.Controls.Add(prerequisitesGrid, 0, 1);
            page.Controls.Add(area);
            tabs.TabPages.Add(page);
        }

        private async Task PrepareSessionAsync()
        {
            prepareOnNewGame = true;
            await RunOperationAsync("Preparing capture hooks and prerequisites...", () =>
            {
                preparedGameIdentity = ReadGameIdentity();
                prerequisiteStatus = session.Prepare();
                return "Capture preparation completed. " + PrerequisiteSummary(prerequisiteStatus) +
                    " See Capture status for waiting pointers or setup errors. Gameplay cheats remain under your control.";
            });
        }

        private async void PrepareSession_Click(object sender, EventArgs e) { await PrepareSessionAsync(); }

        private static string ReadGameIdentity()
        {
            using (var game = MemoryManager.GetGameProcess())
                return game == null || game.HasExited ? string.Empty : MemoryTransactionManager.GetSessionIdentity(game.Id, game.StartTime);
        }

        private static string PrerequisiteSummary(IReadOnlyList<PrerequisiteStatus> states)
        {
            if (states == null) return "Capture hooks have not been prepared yet.";
            return "Captures: " + states.Count(s => s.Ready) + " ready, " + states.Count(s => s.Installed && !s.Ready) +
                " waiting, " + states.Count(s => !s.Installed && !s.Ready) + " unavailable.";
        }

        private void UpdatePrerequisiteStatus()
        {
            if (prerequisiteStatus == null) return;
            instructions.Text = "Capture hooks prepare automatically; gameplay cheats change only when you enable them.\r\n" +
                PrerequisiteSummary(prerequisiteStatus) + " See Capture status for details.\r\nDisable matching Cheat Engine scripts before using this tool. " + ClosingInstructions;
            ReplaceRows(prerequisitesGrid, prerequisiteStatus.Select(s => new object[] { s.Name,
                s.Ready ? "Ready" : s.Installed ? "Waiting for capture" : "Unavailable", s.Detail }));
            for (int index = 0; index < prerequisiteStatus.Count; index++)
                prerequisitesGrid.Rows[index].DefaultCellStyle.ForeColor = prerequisiteStatus[index].Ready ? Color.DarkGreen :
                    prerequisiteStatus[index].Installed ? Color.DarkGoldenrod : Color.Firebrick;
            valuesPanel.UpdatePrerequisiteStatus(prerequisiteStatus);
        }

        private void BuildEffects()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
            var rows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new Padding(0, 0, 12, 0) };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;
            foreach (TableEffect effect in Enum.GetValues(typeof(TableEffect)))
            {
                if (IsCaptureOnly(effect)) continue;
                rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
                rows.Controls.Add(new Label { Text = EffectName(effect), AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
                var enable = Button("Enable", "Enable_" + effect);
                var disable = Button("Disable", "Disable_" + effect);
                enable.Click += async (s, e) => await ChangeEffectAsync(effect, true);
                disable.Click += async (s, e) => await ChangeEffectAsync(effect, false);
                rows.Controls.Add(enable, 1, row);
                rows.Controls.Add(disable, 2, row);
                var state = new Label { Text = "Off", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
                effectStatus.Add(effect, state);
                rows.Controls.Add(state, 3, row);
                rows.Controls.Add(new Label { Text = EffectNote(effect), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 4, row++);
            }

            var switches = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 10, 0, 8) };
            enemyMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145, Name = "EnemyMode" };
            enemyMode.Items.AddRange(new object[] { "Off", "Kill", "Clear sleep timer" });
            enemyMode.SelectedIndex = 1;
            var applyEnemy = Button("Apply enemy mode", "ApplyEnemyMode");
            applyEnemy.AutoSize = true;
            applyEnemy.Click += async (s, e) =>
            {
                var mode = (EnemyControlMode)enemyMode.SelectedIndex;
                await RunOperationAsync("Setting enemy mode...", () =>
                {
                    RequireSession(session.Enable(TableEffect.EnemyControl));
                    manager.Values.Unfreeze(GameValueId.EnemyMode);
                    return Require(manager.SetEnemyMode(mode), "Enemy mode set to " + mode + ".");
                });
            };
            switches.Controls.Add(new Label { Text = "Enemy control:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            switches.Controls.Add(enemyMode);
            switches.Controls.Add(applyEnemy);
            switches.SetFlowBreak(applyEnemy, true);

            alertPassthrough = new CheckBox { Text = "Game decides (no override)", Checked = false, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
            alertLevel = new TextBox { Text = "0", Width = 105, Enabled = true, Name = "AlertLevel" };
            alertPassthrough.CheckedChanged += (s, e) => alertLevel.Enabled = !alertPassthrough.Checked;
            var applyAlert = Button("Apply alert level", "ApplyAlertLevel");
            applyAlert.AutoSize = true;
            applyAlert.Click += async (s, e) =>
            {
                bool passthrough = alertPassthrough.Checked;
                string text = alertLevel.Text;
                await RunOperationAsync("Setting alert level...", () =>
                {
                    uint? level = passthrough ? (uint?)null : ParseAlertLevel(text);
                    RequireSession(session.Enable(TableEffect.ForceAlertLevel));
                    manager.Values.Unfreeze(GameValueId.AlertLevel);
                    return Require(manager.SetAlertLevel(level), passthrough ? "Alert function uses the game's result." : "Alert level set to " + level + ".");
                });
            };
            switches.Controls.Add(new Label { Text = "Force alert level:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            switches.Controls.Add(alertPassthrough);
            switches.Controls.Add(alertLevel);
            switches.Controls.Add(applyAlert);
            switches.SetFlowBreak(applyAlert, true);

            wallsActive = new CheckBox { Text = "Replay movement while blocked", Checked = false, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
            var applyWalls = Button("Apply WTW switch", "ApplyWallsSwitch");
            applyWalls.AutoSize = true;
            applyWalls.Click += async (s, e) =>
            {
                bool active = wallsActive.Checked;
                await RunOperationAsync("Setting WTW switch...", () =>
                {
                    if (!active) { RequireSession(session.Disable(TableEffect.WalkThroughWalls)); return "WTW disabled."; }
                    RequireSession(session.Enable(TableEffect.WalkThroughWalls));
                    return Require(manager.SetWalkThroughWallsActive(true), "WTW movement replay on.");
                });
            };
            switches.Controls.Add(new Label { Text = "Walk Through Walls:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            switches.Controls.Add(wallsActive);
            switches.Controls.Add(applyWalls);
            rows.Controls.Add(switches, 0, row);
            rows.SetColumnSpan(switches, 5);
            scroll.Controls.Add(rows);
            effectsTab.Controls.Add(scroll);
        }

        private async Task ChangeEffectAsync(TableEffect effect, bool enable)
        {
            var mode = (EnemyControlMode)enemyMode.SelectedIndex;
            bool passthrough = alertPassthrough.Checked;
            string levelText = alertLevel.Text;
            await RunOperationAsync((enable ? "Enabling " : "Disabling ") + EffectName(effect) + "...", () =>
            {
                uint? level = enable && effect == TableEffect.ForceAlertLevel && !passthrough ? (uint?)ParseAlertLevel(levelText) : null;
                RequireSession(enable ? session.Enable(effect) : session.Disable(effect));
                if (enable && effect == TableEffect.EnemyControl)
                {
                    manager.Values.Unfreeze(GameValueId.EnemyMode);
                    return Require(manager.SetEnemyMode(mode), "Enemy control: " + mode + ".");
                }
                if (enable && effect == TableEffect.ForceAlertLevel)
                {
                    manager.Values.Unfreeze(GameValueId.AlertLevel);
                    return Require(manager.SetAlertLevel(level), level.HasValue ? "Alert override: " + level + "." : "Alert override off; game decides.");
                }
                return EffectName(effect) + (enable ? " enabled." : " off. Capture prerequisites remain ready for debugging.");
            });
            if (!CanUpdate) return;
            if (enable && effect == TableEffect.WalkThroughWalls && manager.IsEnabled(effect)) wallsActive.Checked = true;
        }

        private void BuildMemoryComparison()
        {
            var area = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            area.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            area.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            area.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            area.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            var refresh = Button("Refresh patch bytes and captured pointers", "RefreshMemoryComparison");
            refresh.AutoSize = true;
            refresh.Click += async (s, e) => await RefreshMemoryAsync();
            patchesGrid = Grid("patchesGrid", "Effect", "Patch address", "Original bytes", "Installed bytes", "Current bytes", "Comparison");
            patchesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            symbolsGrid = Grid("symbolsGrid", "Symbol / slot", "Storage address", "Captured address / value", "State");
            area.Controls.Add(refresh, 0, 0);
            area.Controls.Add(patchesGrid, 0, 1);
            area.Controls.Add(new Label { Text = "Storage is the symbol's allocation; a captured address is the object held in that storage. Ctrl+C copies selected cells.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            area.Controls.Add(symbolsGrid, 0, 3);
            memoryTab.Controls.Add(area);
        }

        private async Task RefreshMemoryAsync()
        {
            List<object[]> patches = null, symbols = null;
            await RunOperationAsync("Reading patch bytes and captured pointers...", () =>
            {
                patches = new List<object[]>();
                symbols = new List<object[]>();
                foreach (TableEffect effect in Enum.GetValues(typeof(TableEffect)))
                {
                    var sites = manager.GetEffectPatchSnapshots(effect);
                    if (sites.Count == 0)
                    {
                        EffectMemoryStatus observed = manager.GetObservedState(effect);
                        patches.Add(new object[] { EffectName(effect), "—", "—", "—", "—", observed.State + ": " + observed.Detail });
                    }
                    foreach (EffectPatchSnapshot site in sites)
                        patches.Add(new object[] { EffectName(effect), Address(site.Address), Bytes(site.OriginalBytes), Bytes(site.ReplacementBytes), Bytes(site.CurrentBytes),
                            site.CurrentBytes == null ? "Unreadable" : site.CurrentBytes.SequenceEqual(site.OriginalBytes) ? "Original / restored"
                            : site.ReplacementBytes != null && site.CurrentBytes.SequenceEqual(site.ReplacementBytes) ? "Patch installed" : "Unexpected bytes" });
                }
                AddSymbolRows(symbols, TableEffect.PlayerPointer, "pPlayer", 8, true);
                AddSymbolRows(symbols, manager.IsEnabled(TableEffect.InfiniteAmmo) ? TableEffect.InfiniteAmmo : TableEffect.AmmoPointer, "pAmmo", 8, true);
                AddSymbolRows(symbols, TableEffect.InventoryPointer, "pInv", 8, true);
                AddSymbolRows(symbols, TableEffect.EnemyControl, "pEnemy", 8, true);
                AddSymbolRows(symbols, TableEffect.EnemyControl, "bEnemyMode", 1, false);
                AddSymbolRows(symbols, TableEffect.ForceAlertLevel, "iAlertLevel", 4, false);
                AddSymbolRows(symbols, TableEffect.ForceAlertLevel, "alertActiveCalls", 4, false);
                AddSymbolRows(symbols, TableEffect.ActorCollector, "pSlot", 8, true);
                AddSymbolRows(symbols, TableEffect.ActorCollector, "pRing", 64, true);
                AddSymbolRows(symbols, TableEffect.ActorCollector, "hbCnt", 4, false);
                AddSymbolRows(symbols, TableEffect.ActorCollector, "pIdx", 8, false);
                foreach (string symbol in new[] { "stageIdSel", "stageFlags", "bStageFire", "stageActiveCalls" })
                    AddSymbolRows(symbols, TableEffect.StageLoaderHook, symbol, 4, false);
                if (manager.Actions.ResolveSymbol("dynResEnabled") != IntPtr.Zero)
                {
                    byte[] flag = manager.ReadSymbol("dynResEnabled", 0, 1);
                    symbols.Add(new object[] { "dynResEnabled", Address(manager.GetSymbolAddress("dynResEnabled")),
                        flag == null ? "Unavailable" : flag[0].ToString(), flag == null ? manager.LastError : "Discovered flag" });
                }
                foreach (string symbol in new[] { "bWTW", "lastDX", "lastDZ", "wtwCnt", "fEps" })
                    AddSymbolRows(symbols, TableEffect.WalkThroughWalls, symbol, 4, false);
                return "Memory comparison refreshed. Saved patch sites remain readable after disabling, for this game session. Use AOB scans to locate effects you have not enabled yet.";
            });
            if (!CanUpdate || patches == null) return;
            ReplaceRows(patchesGrid, patches);
            ReplaceRows(symbolsGrid, symbols);
        }

        private void AddSymbolRows(List<object[]> rows, TableEffect owner, string symbol, int count, bool pointer)
        {
            if (!manager.IsEnabled(owner)) { rows.Add(new object[] { symbol, "—", "—", "Enable " + EffectName(owner) }); return; }
            IntPtr storage = manager.GetSymbolAddress(symbol);
            byte[] bytes = manager.ReadSymbol(symbol, 0, count);
            if (bytes == null) { rows.Add(new object[] { symbol, Address(storage), "Unavailable", manager.LastError }); return; }
            if (pointer)
            {
                for (int offset = 0; offset < count; offset += 8)
                {
                    ulong value = BitConverter.ToUInt64(bytes, offset);
                    rows.Add(new object[] { count == 64 ? symbol + "[" + offset / 8 + "]" : symbol,
                        Address(MemoryManager.AddOffset(storage, offset)), "0x" + value.ToString("X16"), value == 0 ? "Waiting for capture" : "Captured" });
                }
            }
            else
            {
                string value = symbol == "lastDX" || symbol == "lastDZ" || symbol == "fEps"
                    ? BitConverter.ToSingle(bytes, 0).ToString("R", CultureInfo.InvariantCulture)
                    : count == 1 ? bytes[0].ToString() : count == 8 ? BitConverter.ToUInt64(bytes, 0).ToString() : BitConverter.ToUInt32(bytes, 0).ToString();
                rows.Add(new object[] { symbol, Address(storage), value + "  [" + Bytes(bytes) + "]", symbol.EndsWith("ActiveCalls", StringComparison.Ordinal) ? "Read-only bookkeeping" : "Available" });
            }
        }

        private void BuildSignatures()
        {
            var area = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            area.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            area.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            area.Controls.Add(new Label { Text = "This check clears gameplay effects and freezes, removes hooks, scans original code, then prepares the capture hooks again. Disable CE scripts first.", Dock = DockStyle.Fill }, 0, 0);
            var scan = Button("Check original AOBs (resets effects)", "CheckAllSignatures");
            scan.AutoSize = true;
            scan.Click += async (s, e) =>
            {
                IReadOnlyList<SignatureCheckResult> results = null;
                repeatFreezes.Checked = false;
                await RunOperationAsync("Scanning mgs4.exe signatures...", () =>
                {
                    Require(manager.DisableAll(), string.Empty);
                    string scanMessage;
                    try
                    {
                        results = manager.CheckSignatures();
                        scanMessage = manager.LastError.Length == 0 ? results.Count(r => r.IsUnique) + " of " + results.Count + " original signature rows matched exactly once." : manager.LastError;
                    }
                    finally
                    {
                        preparedGameIdentity = ReadGameIdentity();
                        prerequisiteStatus = session.Prepare();
                        prepareOnNewGame = true;
                    }
                    return scanMessage + " Capture hooks prepared again; gameplay cheats and freezes were cleared.";
                });
                if (!CanUpdate || results == null) return;
                ReplaceRows(signaturesGrid, results.Select(r => new object[] { r.Key, r.Name, r.MatchCount,
                    string.Join(", ", r.Matches.Select(Address)), r.Note.Length != 0 ? r.Note : r.IsUnique ? "Unique match" : "Check signature / game version" }));
            };
            area.Controls.Add(scan, 0, 1);
            signaturesGrid = Grid("signaturesGrid", "AOB", "Effect", "Matches", "Addresses", "Result / note");
            area.Controls.Add(signaturesGrid, 0, 2);
            signaturesTab.Controls.Add(area);
        }

        private async Task RunOperationAsync(string pending, Func<string> operation)
        {
            if (operationInProgress || closeRequested || closing || IsDisposed) return;
            operationInProgress = true;
            SetInteractive(false);
            SetResult(pending);
            await operationGate.WaitAsync();
            try { SetResult(await Task.Run(operation)); }
            catch (Exception ex) { SetResult("Could not complete operation: " + ex.Message); }
            finally
            {
                operationGate.Release();
                operationInProgress = false;
                if (CanUpdate)
                {
                    SetInteractive(true);
                    UpdateFreezeCount();
                }
            }
            if (CanUpdate) await RefreshEffectStatusAsync();
        }

        private async void DisableAll_Click(object sender, EventArgs e)
        {
            prepareOnNewGame = false;
            repeatFreezes.Checked = false;
            await RunOperationAsync("Restoring all effects...", () => Require(manager.DisableAll(), "All effects and capture hooks disabled; freezes cleared. Automatic preparation paused. Use Prepare captures to resume."));
        }

        private async void RefreshStatus_Click(object sender, EventArgs e) { await RefreshEffectStatusAsync(); }
        private async void StatusTimer_Tick(object sender, EventArgs e) { await RefreshEffectStatusAsync(); }

        private async Task RefreshEffectStatusAsync()
        {
            if (!initialized || statusInProgress || operationInProgress || !CanUpdate) return;
            if (!operationGate.Wait(0)) return;
            statusInProgress = true;
            bool shouldPrepare = false;
            try
            {
                var statuses = await Task.Run(() =>
                {
                    prerequisiteStatus = session.RefreshStatus();
                    string identity = ReadGameIdentity();
                    shouldPrepare = identity.Length != 0 && identity != preparedGameIdentity;
                    return effectStatus.Keys.ToDictionary(effect => effect, effect =>
                    {
                        if (!manager.IsEnabled(effect))
                        {
                            EffectMemoryStatus observed = manager.GetObservedState(effect);
                            if (observed.State == EffectMemoryState.Active) return observed.Detail;
                            return observed.State == EffectMemoryState.Unrecognized ? "Unrecognized patch (see memory)" :
                                observed.State == EffectMemoryState.Unavailable ? "Unavailable / not verified" :
                                observed.State == EffectMemoryState.NotChecked ? "Not checked" : "Off";
                        }
                        TableEffect? source = effect == TableEffect.InfiniteAmmo ? TableEffect.AmmoPointer :
                            effect == TableEffect.WalkThroughWalls ? TableEffect.ActorCollector :
                            effect == TableEffect.EnemyControl || effect == TableEffect.ForceAlertLevel ? effect : (TableEffect?)null;
                        PrerequisiteStatus capture = source.HasValue ? prerequisiteStatus.First(s => s.Effect == source.Value) : null;
                        if (capture != null && !capture.Installed) return "Hook unavailable (see Capture status)";
                        if (effect == TableEffect.EnemyControl)
                        {
                            byte[] mode = manager.ReadSymbol("bEnemyMode", 0, 1);
                            return mode == null ? "Capture unavailable" : mode[0] == 0 ? "Off (capture hook installed)" : "Enabled: " + (EnemyControlMode)mode[0];
                        }
                        if (effect == TableEffect.ForceAlertLevel)
                        {
                            byte[] level = manager.ReadSymbol("iAlertLevel", 0, 4);
                            return level == null ? "Capture unavailable" : BitConverter.ToUInt32(level, 0) == uint.MaxValue ? "Off (game decides)" : "Enabled: level " + BitConverter.ToUInt32(level, 0);
                        }
                        if (effect == TableEffect.WalkThroughWalls)
                        {
                            byte[] active = manager.ReadSymbol("bWTW", 0, 4);
                            return active == null ? "Switch unavailable" : BitConverter.ToUInt32(active, 0) == 0 ? "Off (hook installed)" :
                                capture != null && !capture.Ready ? "Enabled; waiting for actor capture" : "Enabled";
                        }
                        return "Enabled";
                    });
                });
                if (CanUpdate && !operationInProgress)
                {
                    foreach (var state in statuses)
                    {
                        effectStatus[state.Key].Text = state.Value;
                        effectStatus[state.Key].ForeColor = state.Value.StartsWith("Enabled", StringComparison.Ordinal) ? Color.DarkGreen : SystemColors.ControlText;
                    }
                    UpdatePrerequisiteStatus();
                    UpdateFreezeCount();
                }
            }
            catch (Exception ex) { if (CanUpdate && !operationInProgress) SetResult("Status refresh failed: " + ex.Message); }
            finally { operationGate.Release(); statusInProgress = false; }
            if (shouldPrepare && prepareOnNewGame && CanUpdate && !operationInProgress) await PrepareSessionAsync();
        }

        private void RepeatFreezes_Changed(object sender, EventArgs e)
        {
            if (!closeRequested) freezeTimer.Start();
        }

        private async void FreezeTimer_Tick(object sender, EventArgs e)
        {
            if (!CanUpdate || operationInProgress || freezeInProgress) return;
            bool applyFreezes = repeatFreezes.Checked && manager.Values.FrozenCount != 0;
            if (!applyFreezes && !manager.IsEnabled(TableEffect.DisableResolutionScaling)) return;
            if (!operationGate.Wait(0)) return;
            freezeInProgress = true;
            try
            {
                var result = await Task.Run(() =>
                {
                    manager.Actions.PulseResolutionScaling();
                    return (Failed: applyFreezes ? manager.Values.ApplyFreezes() : 0, Error: manager.Values.LastError);
                });
                if (CanUpdate && result.Failed != 0)
                {
                    repeatFreezes.Checked = false;
                    SetResult("Repeated freezes paused after " + result.Failed + " failed write(s): " + result.Error + " Recheck the capture and turn repeat back on to retry.");
                }
            }
            catch (Exception ex)
            {
                manager.Disable(TableEffect.DisableResolutionScaling);
                if (CanUpdate) { repeatFreezes.Checked = false; SetResult("Repeated writes paused: " + ex.Message); }
            }
            finally
            {
                operationGate.Release();
                freezeInProgress = false;
                if (CanUpdate) UpdateFreezeCount();
            }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (closing) { base.OnFormClosing(e); return; }
            e.Cancel = true;
            base.OnFormClosing(e);
            if (closeRequested) return;
            closeRequested = true;
            statusTimer.Stop();
            freezeTimer.Stop();
            repeatFreezes.Checked = false;
            SetInteractive(false);
            SetResult(preserveSessionOnClose
                ? "Finishing the current operation and stopping repeated value writes before closing..."
                : "Finishing the current operation and restoring effects before closing...");
            try
            {
                while (operationInProgress || freezeInProgress || statusInProgress) await Task.Delay(25);
                if (preserveSessionOnClose)
                {
                    // Only the debugger value panel registers freezes. The host owns the
                    // shared hooks and performs their final cleanup when it exits.
                    manager.Values.ClearFreezes();
                }
                else
                {
                    bool restored = await Task.Run(() => manager.DisableAll());
                    if (!restored) throw new InvalidOperationException(manager.LastError);
                }
                closing = true;
                Close();
            }
            catch (Exception ex)
            {
                closeRequested = false;
                if (!IsDisposed)
                {
                    SetInteractive(true);
                    statusTimer.Start();
                    freezeTimer.Start();
                    SetResult("Cleanup is incomplete: " + ex.Message + " Use Disable all to retry, then close the panel.");
                }
            }
        }

        private bool CanUpdate => !closeRequested && !closing && !IsDisposed && !Disposing;
        private void SetInteractive(bool enabled) { tabs.Enabled = enabled; footer.Enabled = enabled; }
        private void SetResult(string message)
        {
            if (!IsDisposed && !Disposing) operationResult.Text = message ?? string.Empty;
        }
        private void UpdateFreezeCount()
        {
            freezeCount.Text = "Frozen values: " + manager.Values.FrozenCount;
            valuesPanel?.RefreshFreezeState();
        }
        private string Require(bool succeeded, string message)
        {
            if (!succeeded) throw new InvalidOperationException(manager.LastError);
            return message;
        }
        private void RequireSession(bool succeeded)
        {
            if (!succeeded) throw new InvalidOperationException(session.LastError);
        }
        private static uint ParseAlertLevel(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value == "-1") return uint.MaxValue;
            return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.Parse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        private static Button Button(string text, string name) => new Button { Text = text, Name = name, Width = 79, Height = 30, Margin = new Padding(3), UseVisualStyleBackColor = true };
        private static string Address(IntPtr address) => address == IntPtr.Zero ? "—" : "0x" + address.ToInt64().ToString("X16");
        private static string Bytes(IEnumerable<byte> bytes) => bytes == null ? "Unavailable" : string.Join(" ", bytes.Select(b => b.ToString("X2")));
        private static DataGridView Grid(string name, params string[] columns)
        {
            var grid = new DataGridView { Name = name, Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window, SelectionMode = DataGridViewSelectionMode.CellSelect,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText };
            foreach (string column in columns) grid.Columns.Add(column, column);
            grid.DefaultCellStyle.Font = new Font("Consolas", 9F);
            return grid;
        }
        private static void ReplaceRows(DataGridView grid, IEnumerable<object[]> rows)
        {
            grid.Rows.Clear();
            foreach (object[] row in rows) grid.Rows.Add(row);
        }
        private static string EffectName(TableEffect effect)
        {
            switch (effect)
            {
                case TableEffect.PlayerPointer: return "Player pointer";
                case TableEffect.NoStress: return "No stress";
                case TableEffect.Always100PercentCamo: return "100% camo";
                default: return System.Text.RegularExpressions.Regex.Replace(effect.ToString(), "([a-z])([A-Z])", "$1 $2");
            }
        }
        private static string EffectNote(TableEffect effect)
        {
            switch (effect)
            {
                case TableEffect.PlayerPointer: return "Needed for vitals, points and statistics.";
                case TableEffect.NoStress: return "Stops updates; does not reset current stress.";
                case TableEffect.InfiniteAmmo: return "Also captures the current ammo record.";
                case TableEffect.InfiniteSuppressor: return "Patches both decrement instructions.";
                case TableEffect.NoAlerts: return "Switches off the alert override automatically.";
                case TableEffect.ForceAlertLevel: return "Applies the level below; switches off No Alerts.";
                case TableEffect.EnemyControl: return "Applies the selected mode below; capture is automatic.";
                case TableEffect.InventoryPointer: return "Needed for item and weapon value editing.";
                case TableEffect.ActorCollector: return "Latched actor plus a separate fallback ring.";
                case TableEffect.WalkThroughWalls: return "Uses the latched actor; check coordinates before enabling movement replay.";
                case TableEffect.DisableResolutionScaling: return "Writes 0 every 250 ms; Disable stops repetition.";
                case TableEffect.InfiniteStamina: return "Refills stamina to its maximum on update.";
                default: return "Enable, compare gameplay, then disable to restore.";
            }
        }
    }
}
