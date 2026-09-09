using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm
    {
        private readonly List<NumericInputManager> vitalEditors = new List<NumericInputManager>();
        private readonly Dictionary<NumericInputManager, NumericValueCommit> pendingVitalWrites = new Dictionary<NumericInputManager, NumericValueCommit>();
        private bool processingVitalWrites;

        private void InitializeVitalsControls()
        {
            BindVital(SnakesHPNumeric, GameValueId.Health, "HP", 100, ushort.MaxValue);
            BindVital(SnakesMaxHPNumeric, GameValueId.HealthMax, "Max HP", 100, ushort.MaxValue);
            BindVital(SnakesPsycheNumeric, GameValueId.Stamina, "Psyche", 100, ushort.MaxValue);
            BindVital(SnakesMaxPsycheNumeric, GameValueId.StaminaMax, "Max Psyche", 100, ushort.MaxValue);
            BindVital(DrebinPointsNumeric, GameValueId.DrebinPoints, "Drebin Points", 5000, uint.MaxValue);
            BindVital(SnakesBatteryNumeric, GameValueId.Battery, "Battery", 100, ushort.MaxValue);
            BindVital(SnakesMaxBatteryNumeric, GameValueId.BatteryMax, "Max Battery", 100, ushort.MaxValue);
        }

        private void BindVital(NumericUpDown control, GameValueId id, string name, decimal increment, decimal maximum)
        {
            var editor = new NumericInputManager(control, id, increment, maximum);
            vitalEditors.Add(editor);
            editor.InvalidInput += error => SetStatus(name + ": " + error);
            editor.CommitRequested += request =>
            {
                if (!CanUpdate) { editor.Complete(request); return; }
                pendingVitalWrites[editor] = request;
                ProcessVitalWritesAsync();
            };
            BindHover(control, name + ": live every second. Typing pauses this field; Enter or leaving applies it. Arrows change by " + increment + ".");
        }

        private VitalsSnapshot ReadVitalsSnapshot()
        {
            return manager.WithSession((process, handle) =>
            {
                var memory = MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress);
                if (!manager.IsEnabled(TableEffect.PlayerPointer))
                    return new VitalsSnapshot(memory.SessionIdentity, IntPtr.Zero, false, "Waiting for player vitals.");
                return VitalsManager.Read(memory);
            });
        }

        internal void ApplyVitalsSnapshot(VitalsSnapshot snapshot)
        {
            if (snapshot != null && snapshot.GameIdentity != gameIdentity) return;
            foreach (NumericInputManager editor in vitalEditors) editor.ApplySnapshot(snapshot);
            SetVitalsInteractive();
        }

        private void SetVitalsInteractive()
        {
            // A different field's queued write must not steal focus from a new draft.
            bool available = connected && !closeRequested && !closing;
            foreach (NumericInputManager editor in vitalEditors) editor.SetInteractive(available);
        }

        private async void ProcessVitalWritesAsync()
        {
            if (processingVitalWrites) return;
            processingVitalWrites = true;
            try
            {
                while (CanUpdate && pendingVitalWrites.Count != 0)
                {
                    while (operationInProgress && CanUpdate) await Task.Delay(25);
                    if (!CanUpdate) break;
                    var next = pendingVitalWrites.First();
                    pendingVitalWrites.Remove(next.Key);
                    NumericInputManager editor = next.Key;
                    NumericValueCommit request = next.Value;
                    if (!editor.IsCurrent(request)) continue;
                    await RunOperationAsync("Applying " + GameValueDefinitionManager.Get(request.Id).Name + "...", () => manager.WithSession((process, handle) =>
                    {
                        if (!manager.IsEnabled(TableEffect.PlayerPointer))
                            throw new InvalidOperationException("The player hook is no longer active. Wait for live values to return.");
                        VitalsManager.Set(MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress),
                            request.Source, request.Id, request.Value);
                        return GameValueDefinitionManager.Get(request.Id).Name + " applied and verified.";
                    }));
                    if (CanUpdate) editor.Complete(request);
                }
            }
            finally
            {
                processingVitalWrites = false;
                if (!CanUpdate) pendingVitalWrites.Clear();
            }
        }
    }
}
