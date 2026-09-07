using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MGS4_Master_Collection_Trainer.Constants;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Manual, catalog-driven controls for comparing table values with Cheat Engine.</summary>
    public sealed class DebugValuesPanel : UserControl
    {
        private readonly DataGridView grid;
        private readonly ComboBox groupFilter;
        private readonly TextBox search;
        private readonly TextBox editor;
        private readonly CheckBox hexInput;
        private readonly Label details;
        private readonly Label status;
        private readonly Label count;
        private readonly Button enableRequired;
        private readonly Button writeSelected, freezeSelected;
        private readonly ComboBox valueChoices;
        private readonly Dictionary<int, ValueSnapshot> snapshots = new Dictionary<int, ValueSnapshot>();
        private readonly Dictionary<TableEffect, PrerequisiteStatus> prerequisites = new Dictionary<TableEffect, PrerequisiteStatus>();
        private bool rebuilding;

        internal EffectManager Manager { get; set; } = EffectManager.Instance;
        internal TrainerSessionManager Session { get; set; }
        internal Func<string, Func<string>, Task> RunOperationAsync { get; set; }

        public DebugValuesPanel()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            BackColor = SystemColors.Control;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            filters.Controls.Add(new Label { Text = "Group", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
            groupFilter = new ComboBox
            {
                Name = "groupFilter", DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 310, DropDownWidth = 530
            };
            groupFilter.Items.Add("All groups");
            foreach (string group in GameValueDefinitionManager.All.Values.Select(value => value.Group).Distinct().OrderBy(value => value))
                groupFilter.Items.Add(group);
            groupFilter.SelectedIndex = 0;
            filters.Controls.Add(groupFilter);
            filters.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(12, 7, 6, 0) });
            search = new TextBox { Name = "valueSearch", Width = 190 };
            filters.Controls.Add(search);
            count = new Label { AutoSize = true, Margin = new Padding(12, 7, 0, 0) };
            filters.Controls.Add(count);
            layout.Controls.Add(filters, 0, 0);

            grid = new DataGridView
            {
                Name = "valuesGrid", Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false, AutoGenerateColumns = false,
                BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            };
            AddColumn("Name", "Value", 245);
            AddColumn("Id", "CE ID", 58);
            AddColumn("Type", "Type", 65);
            AddColumn("Decimal", "Value / decimal", 140);
            AddColumn("Hex", "Hex / float bits", 155);
            AddColumn("Address", "Resolved address", 155);
            AddColumn("State", "State", 170);
            layout.Controls.Add(grid, 0, 1);

            details = new Label
            {
                Name = "valueDetails", Dock = DockStyle.Fill, AutoEllipsis = true,
                Padding = new Padding(0, 7, 0, 0)
            };
            layout.Controls.Add(details, 0, 2);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                Margin = Padding.Empty
            };
            actions.Controls.Add(new Label { Text = "New value", AutoSize = true, Margin = new Padding(0, 8, 5, 0) });
            editor = new TextBox { Name = "valueInput", Width = 205, Margin = new Padding(3, 4, 6, 4) };
            actions.Controls.Add(editor);
            valueChoices = new ComboBox { Name = "valueChoices", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Visible = false };
            valueChoices.SelectedIndexChanged += (s, e) =>
            {
                if (valueChoices.SelectedItem is GameValueChoice choice)
                    editor.Text = hexInput.Checked ? "0x" + choice.Value.ToString("X", CultureInfo.InvariantCulture) : choice.Value.ToString(CultureInfo.InvariantCulture);
            };
            actions.Controls.Add(valueChoices);
            hexInput = new CheckBox
            {
                Name = "hexInput", Text = "Hex input (raw bits)", AutoSize = true,
                Margin = new Padding(3, 7, 12, 3)
            };
            actions.Controls.Add(hexInput);
            actions.Controls.Add(ActionButton("Read selected", ReadSelectedAsync));
            actions.Controls.Add(ActionButton("Refresh visible", RefreshVisibleAsync));
            writeSelected = ActionButton("Write selected", () => WriteSelectedAsync(false));
            freezeSelected = ActionButton("Freeze selected", () => WriteSelectedAsync(true));
            actions.Controls.Add(writeSelected);
            actions.Controls.Add(freezeSelected);
            actions.Controls.Add(ActionButton("Unfreeze selected", UnfreezeSelectedAsync));
            actions.Controls.Add(ActionButton("Clear freezes", ClearFreezesAsync));
            enableRequired = ActionButton("Retry required capture", EnableRequiredAsync);
            actions.Controls.Add(enableRequired);
            layout.Controls.Add(actions, 0, 3);

            status = new Label
            {
                Name = "valueStatus", AutoSize = true, Dock = DockStyle.Fill,
                MaximumSize = new Size(1100, 0), Margin = new Padding(0, 8, 0, 3),
                Text = "Manual snapshots only. Decimal input uses a dot; hex input writes raw bits. Freeze writes once; check 'Repeat frozen writes (250 ms)' to keep applying it."
            };
            layout.Controls.Add(status, 0, 4);

            groupFilter.SelectedIndexChanged += (sender, args) => RebuildRows();
            search.TextChanged += (sender, args) => RebuildRows();
            grid.CurrentCellChanged += (sender, args) => { if (!rebuilding) ShowSelected(true); };
            hexInput.CheckedChanged += (sender, args) => ConvertInputMode();
            RebuildRows();
        }

        private void AddColumn(string name, string caption, int width)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = caption, Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private Button ActionButton(string caption, Func<Task> action)
        {
            var button = new Button { Text = caption, AutoSize = true, MinimumSize = new Size(100, 28) };
            button.Click += async (sender, args) => await action();
            return button;
        }

        private GameValueDefinition SelectedDefinition => grid.CurrentRow?.Tag as GameValueDefinition;
        private bool CanUpdate => !IsDisposed && !Disposing;

        private void RebuildRows()
        {
            if (grid == null) return;
            int? selected = SelectedDefinition?.Id;
            string group = groupFilter.SelectedIndex > 0 ? (string)groupFilter.SelectedItem : null;
            string query = search.Text.Trim();
            var matching = GameValueDefinitionManager.All.Values
                .Where(value => group == null || value.Group == group)
                .Where(value => query.Length == 0 || Contains(value.Name, query) ||
                    Contains(value.Id.ToString(CultureInfo.InvariantCulture), query) ||
                    Contains(value.AddressExpression, query) || Contains(value.Group, query))
                .OrderBy(value => value.Id).ToArray();
            rebuilding = true;
            try
            {
                grid.Rows.Clear();
                foreach (GameValueDefinition definition in matching)
                {
                    int index = grid.Rows.Add(definition.Name, definition.Id, definition.Type, "", "", "", "Not read");
                    grid.Rows[index].Tag = definition;
                    if (selected == definition.Id) grid.CurrentCell = grid.Rows[index].Cells[0];
                }
                RefreshFreezeState();
                count.Text = matching.Length + " / " + GameValueDefinitionManager.All.Count + " values";
            }
            finally { rebuilding = false; }
            ShowSelected(true);
        }

        private static bool Contains(string value, string query) => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private void ShowSelected(bool updateEditor)
        {
            GameValueDefinition definition = SelectedDefinition;
            enableRequired.Enabled = definition != null && RequiredEffect(definition).HasValue;
            writeSelected.Enabled = definition != null && !definition.ReadOnly;
            freezeSelected.Enabled = definition != null && !definition.ReadOnly && !definition.IsLocal;
            editor.ReadOnly = definition == null || definition.ReadOnly;
            hexInput.Enabled = definition != null && definition.Type != DataType.String;
            if (definition?.Type == DataType.String) hexInput.Checked = false;
            valueChoices.Visible = definition != null && definition.Choices.Count != 0;
            if (updateEditor)
            {
                valueChoices.Items.Clear();
                if (definition != null) valueChoices.Items.AddRange(definition.Choices.Cast<object>().ToArray());
            }
            if (definition == null)
            {
                details.Text = "Select a value to see its address and required hook.";
                if (updateEditor) editor.Clear();
                return;
            }
            TableEffect? requirement = RequiredEffect(definition);
            string note = definition.IsComputed ? "Computed, read-only snapshot from player values." : definition.IsLocal ? "Trainer setting; no remote memory address." :
                requirement.HasValue ? "Requires " + requirement.Value + "." : definition.Symbol == "dynResEnabled" ? "Discovered resolution flag; reading does not disable scaling." :
                definition.Symbol == "stageIdSel" ? "Discovered stage selection; editing this ID alone does not queue a load." : "Uses the mgs4.exe module base; no capture hook required.";
            if (definition.Dereference) note += " The hook must execute in game before its pointer becomes available.";
            if (definition.Symbol == "pAmmo") note += " Captures the ammo record for editing.";
            if (definition.Symbol == "pSlot") note += " Latched actor; check coordinates to verify it is the player.";
            if (definition.Symbol == "pRing") note += " Fallback ring entries rotate independently of the latched actor.";
            if (definition.Experimental) note += " EXPERIMENTAL: fixed module offset, dependent on the game build.";
            if (definition.DynamicChoicesSymbol != null) note += " Use Stage / rank / unlocks for the named stage list and loading.";
            if (definition.Type == DataType.String && !definition.ReadOnly) note += " ASCII text, at most " + (definition.ByteCount - (definition.ZeroTerminate ? 1 : 0)) + " characters.";
            if (definition.Id == (int)GameValueId.AlertLevel) note += " 4294967295 / 0xFFFFFFFF lets the game decide.";
            if (definition.Type == DataType.UInt64 && !definition.Dereference)
                note += " Raw captured pointer: the Address column identifies its storage slot.";
            if (requirement.HasValue)
            {
                if (prerequisites.TryGetValue(requirement.Value, out PrerequisiteStatus prerequisite))
                    note += Environment.NewLine + "Capture source: " +
                        (prerequisite.Ready ? "ready. " : prerequisite.Installed ? "waiting. " : "unavailable. ") + prerequisite.Detail;
                else note += Environment.NewLine + "Capture status has not been checked yet. Reading checks the required capture automatically.";
            }
            string snapshotText = "Not read yet.";
            if (snapshots.TryGetValue(definition.Id, out ValueSnapshot snapshot))
            {
                snapshotText = "Last read " + snapshot.ReadAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ".";
                if (!string.IsNullOrEmpty(snapshot.Error)) snapshotText += " " + snapshot.Error;
            }
            details.Text = definition.Name + "  |  CE ID " + definition.Id + "  |  " + definition.Type + " (" + definition.ByteCount + " bytes)" +
                Environment.NewLine + "CE address: " + definition.AddressExpression + Environment.NewLine + note +
                Environment.NewLine + snapshotText;
            if (updateEditor)
                editor.Text = snapshot?.Value == null ? string.Empty : FormatValue(definition.Type, snapshot.Value, hexInput.Checked);
        }

        private void ConvertInputMode()
        {
            GameValueDefinition definition = SelectedDefinition;
            if (definition == null || string.IsNullOrWhiteSpace(editor.Text)) return;
            try
            {
                object value = ParseValue(definition.Type, editor.Text, !hexInput.Checked);
                editor.Text = FormatValue(definition.Type, value, hexInput.Checked);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                status.Text = "Input mode changed. Enter a valid " + (hexInput.Checked ? "hex bit pattern" : "decimal value using a dot") + ".";
            }
        }

        private async Task RefreshVisibleAsync()
        {
            GameValueDefinition[] visible = grid.Rows.Cast<DataGridViewRow>().Select(row => (GameValueDefinition)row.Tag).ToArray();
            var results = new List<ValueSnapshot>();
            EffectManager manager = Manager;
            TrainerSessionManager session = Session;
            await ExecuteAsync("Refresh visible values", () =>
            {
                TrainerSessionManager readySession = session ?? new TrainerSessionManager(manager);
                foreach (GameValueDefinition definition in visible) results.Add(ReadSnapshot(manager, readySession, definition));
                int failed = results.Count(value => !string.IsNullOrEmpty(value.Error));
                return "Read " + (results.Count - failed) + " / " + results.Count + " visible values." +
                    (failed == 0 ? string.Empty : " Unavailable entries include their errors in the selected details.");
            });
            if (!CanUpdate) return;
            foreach (ValueSnapshot result in results) snapshots[result.Definition.Id] = result;
            RefreshFreezeState();
            ShowSelected(false);
        }

        private async Task ReadSelectedAsync()
        {
            GameValueDefinition definition = SelectedDefinition;
            if (definition == null) { status.Text = "Select a value first."; return; }
            ValueSnapshot result = null;
            EffectManager manager = Manager;
            TrainerSessionManager session = Session;
            await ExecuteAsync("Read " + definition.Name, () =>
            {
                result = ReadSnapshot(manager, session ?? new TrainerSessionManager(manager), definition);
                return string.IsNullOrEmpty(result.Error) ? "Read " + definition.Name + ": " + FormatValue(definition.Type, result.Value, false) + "." : result.Error;
            });
            if (!CanUpdate || result == null) return;
            snapshots[definition.Id] = result;
            RefreshFreezeState();
            ShowSelected(SelectedDefinition?.Id == definition.Id && result.Value != null);
        }

        private async Task WriteSelectedAsync(bool freeze)
        {
            GameValueDefinition definition = SelectedDefinition;
            if (definition == null) { status.Text = "Select a value first."; return; }
            string input = editor.Text;
            bool hexadecimal = hexInput.Checked;
            EffectManager manager = Manager;
            TrainerSessionManager session = Session;
            ValueSnapshot result = null;
            await ExecuteAsync((freeze ? "Freeze " : "Write ") + definition.Name, () =>
            {
                object value = ParseValue(definition.Type, input, hexadecimal);
                TrainerSessionManager readySession = session ?? new TrainerSessionManager(manager);
                if (!readySession.EnsureValueReady(definition)) throw new InvalidOperationException(readySession.LastError);
                bool success = freeze ? manager.Values.Freeze(definition.Id, value) : manager.Values.Write(definition.Id, value);
                if (!success) throw new InvalidOperationException(manager.Values.LastError);
                result = ReadSnapshot(manager, readySession, definition);
                return (freeze ? "Freeze registered for " : "Wrote ") + definition.Name + ": " + FormatValue(definition.Type, value, false) + "." +
                    (freeze ? " Check 'Repeat frozen writes (250 ms)' to keep applying it." : string.Empty) +
                    (!string.IsNullOrEmpty(result.Error) ? " Readback: " + result.Error : string.Empty);
            });
            if (!CanUpdate) return;
            if (result != null) snapshots[definition.Id] = result;
            RefreshFreezeState();
            ShowSelected(false);
        }

        private async Task UnfreezeSelectedAsync()
        {
            GameValueDefinition definition = SelectedDefinition;
            if (definition == null) { status.Text = "Select a value first."; return; }
            EffectManager manager = Manager;
            await ExecuteAsync("Unfreeze " + definition.Name, () => manager.Values.Unfreeze(definition.Id)
                ? "Unfroze " + definition.Name + "." : definition.Name + " was not frozen.");
            if (CanUpdate) RefreshFreezeState();
        }

        private async Task ClearFreezesAsync()
        {
            EffectManager manager = Manager;
            await ExecuteAsync("Clear value freezes", () =>
            {
                manager.Values.ClearFreezes();
                return "Cleared all value freezes. Previously written values remain in game.";
            });
            if (CanUpdate) RefreshFreezeState();
        }

        private async Task EnableRequiredAsync()
        {
            GameValueDefinition definition = SelectedDefinition;
            TableEffect? effect = definition == null ? null : RequiredEffect(definition);
            if (!effect.HasValue) { status.Text = "This value does not need a capture hook."; return; }
            EffectManager manager = Manager;
            TrainerSessionManager session = Session;
            await ExecuteAsync("Retry " + effect.Value + " capture", () =>
            {
                TrainerSessionManager readySession = session ?? new TrainerSessionManager(manager);
                if (!readySession.RetryValueReady(definition)) throw new InvalidOperationException(readySession.LastError);
                return "The required capture is ready for " + definition.Name + ". Read the value to take a snapshot.";
            });
        }

        private async Task ExecuteAsync(string title, Func<string> operation)
        {
            if (RunOperationAsync == null) { status.Text = "The debugger operation coordinator is unavailable."; return; }
            string result = null;
            await RunOperationAsync(title, () =>
            {
                try { result = operation(); return result; }
                catch (Exception ex) { result = ex.Message; throw; }
            });
            if (CanUpdate && result != null) status.Text = result;
        }

        private static ValueSnapshot ReadSnapshot(EffectManager manager, TrainerSessionManager session, GameValueDefinition definition)
        {
            try
            {
                if (!session.EnsureValueReady(definition))
                    throw new InvalidOperationException(session.LastError);
                GameValueSnapshot snapshot = manager.ReadValueSnapshot(definition.Id);
                return new ValueSnapshot
                {
                    Definition = definition,
                    Value = snapshot.Value,
                    Address = snapshot.Address,
                    Error = snapshot.Error,
                    ReadAt = snapshot.ReadAt
                };
            }
            catch (Exception ex)
            {
                return new ValueSnapshot { Definition = definition, Error = ex.Message, ReadAt = DateTime.UtcNow };
            }
        }

        /// <summary>Applies a worker-produced readiness snapshot without reading game memory or replacing input drafts.</summary>
        internal void UpdatePrerequisiteStatus(IReadOnlyList<PrerequisiteStatus> results)
        {
            if (!CanUpdate) return;
            prerequisites.Clear();
            if (results != null)
                foreach (PrerequisiteStatus result in results)
                    prerequisites[result.Effect] = result;
            RefreshFreezeState();
            ShowSelected(false);
        }

        internal void RefreshFreezeState()
        {
            if (!CanUpdate) return;
            var frozen = new HashSet<int>(Manager.Values.FrozenIds);
            foreach (DataGridViewRow row in grid.Rows)
            {
                var definition = (GameValueDefinition)row.Tag;
                string state = "Not read";
                TableEffect? requirement = RequiredEffect(definition);
                if (requirement.HasValue && prerequisites.TryGetValue(requirement.Value, out PrerequisiteStatus prerequisite))
                {
                    state = prerequisite.Ready ? "Source ready; not read" :
                        prerequisite.Installed ? "Source waiting; not read" : "Source unavailable; not read";
                    row.Cells["State"].ToolTipText = prerequisite.Detail;
                }
                else row.Cells["State"].ToolTipText = string.Empty;
                if (snapshots.TryGetValue(definition.Id, out ValueSnapshot snapshot))
                {
                    row.Cells["Decimal"].Value = snapshot.Value == null ? string.Empty : FormatValue(definition.Type, snapshot.Value, false);
                    row.Cells["Hex"].Value = snapshot.Value == null ? string.Empty : FormatValue(definition.Type, snapshot.Value, true);
                    row.Cells["Address"].Value = definition.IsLocal ? (definition.IsComputed ? "Computed" : "Trainer setting") : snapshot.Address == IntPtr.Zero ? string.Empty : "0x" + snapshot.Address.ToInt64().ToString("X16", CultureInfo.InvariantCulture);
                    state = string.IsNullOrEmpty(snapshot.Error) ? "Read " + snapshot.ReadAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "Unavailable";
                    row.Cells["State"].ToolTipText = snapshot.Error;
                }
                if (frozen.Contains(definition.Id)) state = "Freeze registered; " + state;
                row.Cells["State"].Value = state;
                row.DefaultCellStyle.BackColor = frozen.Contains(definition.Id) ? Color.LightGoldenrodYellow : SystemColors.Window;
            }
        }

        private static TableEffect? RequiredEffect(GameValueDefinition definition)
        {
            switch (definition.Symbol)
            {
                case "mgsStage": case "mgsAct": case "mgsDiff": case "mgsRank":
                case "pPlayer": return TableEffect.PlayerPointer;
                case "pInv": return TableEffect.InventoryPointer;
                case "pAmmo": return TableEffect.AmmoPointer;
                case "pEnemy":
                case "bEnemyMode": return TableEffect.EnemyControl;
                case "iAlertLevel": return TableEffect.ForceAlertLevel;
                case "pSlot": case "pRing": case "hbCnt": return TableEffect.ActorCollector;
                default: return null;
            }
        }

        private static object ParseValue(DataType type, string input, bool hexadecimal)
        {
            if (type == DataType.String)
            {
                if (hexadecimal) throw new FormatException("String fields accept ASCII text, not hexadecimal input.");
                return input ?? string.Empty;
            }
            string text = (input ?? string.Empty).Trim();
            if (text.Length == 0) throw new FormatException("Enter a value first.");
            if (hexadecimal)
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
                if (text.Length == 0 || text.Any(character => !Uri.IsHexDigit(character)))
                    throw new FormatException("Hex input accepts hexadecimal digits, with an optional 0x prefix.");
                ulong bits = ulong.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                switch (type)
                {
                    case DataType.UInt8: return checked((byte)bits);
                    case DataType.UInt16: return checked((ushort)bits);
                    case DataType.UInt32: return checked((uint)bits);
                    case DataType.UInt64: return bits;
                    case DataType.Float:
                        float floatBits = BitConverter.ToSingle(BitConverter.GetBytes(checked((uint)bits)), 0);
                        if (float.IsNaN(floatBits) || float.IsInfinity(floatBits)) throw new FormatException("Float bits must represent a finite 32-bit value.");
                        return floatBits;
                    default: throw new ArgumentOutOfRangeException(nameof(type));
                }
            }
            switch (type)
            {
                case DataType.UInt8: return byte.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
                case DataType.UInt16: return ushort.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
                case DataType.UInt32: return uint.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
                case DataType.UInt64: return ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
                case DataType.Float:
                    float value = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("Enter a finite 32-bit float using a dot as the decimal separator.");
                    return value;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static string FormatValue(DataType type, object value, bool hexadecimal)
        {
            if (value == null) return string.Empty;
            if (!hexadecimal)
                return type == DataType.Float ? ((float)value).ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
            switch (type)
            {
                case DataType.UInt8: return "0x" + ((byte)value).ToString("X2", CultureInfo.InvariantCulture);
                case DataType.UInt16: return "0x" + ((ushort)value).ToString("X4", CultureInfo.InvariantCulture);
                case DataType.UInt32: return "0x" + ((uint)value).ToString("X8", CultureInfo.InvariantCulture);
                case DataType.UInt64: return "0x" + ((ulong)value).ToString("X16", CultureInfo.InvariantCulture);
                case DataType.Float: return "0x" + BitConverter.ToUInt32(BitConverter.GetBytes((float)value), 0).ToString("X8", CultureInfo.InvariantCulture);
                case DataType.String: return string.Join(" ", System.Text.Encoding.ASCII.GetBytes((string)value).Select(b => b.ToString("X2")));
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private sealed class ValueSnapshot
        {
            internal GameValueDefinition Definition;
            internal object Value;
            internal IntPtr Address;
            internal string Error;
            internal DateTime ReadAt;
        }
    }
}
