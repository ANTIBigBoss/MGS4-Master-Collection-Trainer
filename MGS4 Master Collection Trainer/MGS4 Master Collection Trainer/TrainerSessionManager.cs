using System;
using System.Collections.Generic;
using System.Linq;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>
    /// Coordinates the trainer's capture hooks. Preparation enables neutral pointer
    /// capture only; explicit effect actions select cheats and replace conflicting providers.
    /// Status refreshes only read memory and never reinstall a hook or reset a setting.
    /// </summary>
    public sealed class TrainerSessionManager
    {
        private static readonly TableEffect[] Prerequisites =
        {
            TableEffect.PlayerPointer, TableEffect.InventoryPointer, TableEffect.ActorCollector,
            TableEffect.AmmoPointer, TableEffect.EnemyControl, TableEffect.ForceAlertLevel, TableEffect.StageLoaderHook
        };

        private readonly object gate = new object();
        private readonly EffectManager manager;
        private readonly Dictionary<TableEffect, string> preparationErrors = new Dictionary<TableEffect, string>();
        private string lastError = string.Empty;

        public TrainerSessionManager(EffectManager manager)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public string LastError { get { lock (gate) return lastError; } }

        /// <summary>Explicitly attempts each prerequisite, preserving all existing user settings.</summary>
        public IReadOnlyList<PrerequisiteStatus> Prepare()
        {
            lock (gate)
            {
                var errors = new List<string>();
                manager.InspectExistingEffects();
                if (!manager.UpgradeLegacyActor()) errors.Add(manager.LastError);
                foreach (TableEffect effect in Prerequisites)
                {
                    // These gameplay effects already own or intentionally bypass the corresponding provider.
                    if (effect == TableEffect.AmmoPointer && manager.IsEnabled(TableEffect.InfiniteAmmo))
                    {
                        preparationErrors.Remove(effect);
                        if (!EnsureInstalled(TableEffect.InfiniteAmmo)) errors.Add(lastError);
                        continue;
                    }
                    if (effect == TableEffect.ForceAlertLevel && manager.IsEnabled(TableEffect.NoAlerts))
                    {
                        preparationErrors.Remove(effect);
                        continue;
                    }
                    if (!EnsureInstalled(effect)) errors.Add(lastError);
                }
                try { manager.Actions.PrepareResolutionScaling(); preparationErrors.Remove(TableEffect.DisableResolutionScaling); }
                catch (Exception ex) { preparationErrors[TableEffect.DisableResolutionScaling] = ex.Message; errors.Add(ex.Message); }
                lastError = string.Join(Environment.NewLine, errors);
                return ReadStatuses();
            }
        }

        /// <summary>Reads current readiness without enabling hooks or changing gameplay settings.</summary>
        public IReadOnlyList<PrerequisiteStatus> RefreshStatus()
        {
            lock (gate) return ReadStatuses();
        }

        public bool Enable(TableEffect effect)
        {
            lock (gate)
            {
                if (effect == TableEffect.WalkThroughWalls && !EnsureInstalled(TableEffect.ActorCollector)) return false;
                if (effect == TableEffect.InfiniteAmmo) return ReplaceProvider(TableEffect.AmmoPointer, effect);
                if (effect == TableEffect.AmmoPointer) return ReplaceProvider(TableEffect.InfiniteAmmo, effect);
                if (effect == TableEffect.NoAlerts)
                {
                    if (!ReplaceProvider(TableEffect.ForceAlertLevel, effect)) return false;
                    manager.Values.Unfreeze(GameValueId.AlertLevel);
                    return Success();
                }
                if (effect == TableEffect.ForceAlertLevel) return ReplaceProvider(TableEffect.NoAlerts, effect);
                return EnsureInstalled(effect);
            }
        }

        /// <summary>Disables a cheat while retaining or restoring the corresponding neutral capture hook.</summary>
        public bool Disable(TableEffect effect)
        {
            lock (gate)
            {
                if (effect == TableEffect.EnemyControl)
                {
                    manager.Values.Unfreeze(GameValueId.EnemyMode);
                    if (!manager.IsEnabled(effect)) return Success();
                    return Record(effect, manager.SetEnemyMode(EnemyControlMode.Off));
                }
                if (effect == TableEffect.ForceAlertLevel)
                {
                    manager.Values.Unfreeze(GameValueId.AlertLevel);
                    if (!manager.IsEnabled(effect)) return Success();
                    return Record(effect, manager.SetAlertLevel(null));
                }
                if (!Record(effect, manager.Disable(effect))) return false;
                if (effect == TableEffect.InfiniteAmmo) return EnsureInstalled(TableEffect.AmmoPointer);
                if (effect == TableEffect.NoAlerts)
                {
                    manager.Values.Unfreeze(GameValueId.AlertLevel);
                    return EnsureInstalled(TableEffect.ForceAlertLevel);
                }
                return Success();
            }
        }

        /// <summary>
        /// Ensures a value's provider is installed, then checks that this particular value is readable.
        /// Reading the alert setting never replaces an explicitly enabled No Alerts effect.
        /// </summary>
        public bool EnsureValueReady(GameValueDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            lock (gate)
            {
                TableEffect? provider = ProviderFor(definition.Symbol);
                if (provider == TableEffect.ForceAlertLevel && manager.IsEnabled(TableEffect.NoAlerts))
                    return Fail("No Alerts is active. Select Force Alert Level to switch before editing its level.");
                if (provider == TableEffect.AmmoPointer && manager.IsEnabled(TableEffect.InfiniteAmmo))
                    provider = TableEffect.InfiniteAmmo;
                if (provider.HasValue && !EnsureInstalled(provider.Value, retryFailure: false)) return false;
                if (definition.Symbol == "dynResEnabled" || definition.Symbol == "stageIdSel")
                {
                    try
                    {
                        if (definition.Symbol == "dynResEnabled") manager.Actions.PrepareResolutionScaling();
                        else manager.Actions.BuildStageList();
                    }
                    catch (Exception ex) { return Fail(ex.Message); }
                }

                if (IsCapturedPointer(definition.Symbol))
                {
                    byte[] captured = manager.ReadSymbol(definition.Symbol, checked((int)definition.SymbolOffset), 8);
                    if (captured == null) return Fail(manager.LastError);
                    if (BitConverter.ToInt64(captured, 0) <= 0)
                        return Fail("Waiting for " + definition.Name + ". " + CaptureHint(definition.Symbol, definition.SymbolOffset));
                }

                GameValueSnapshot snapshot = manager.ReadValueSnapshot(definition.Id);
                if (snapshot.Value == null)
                    return Fail("Cannot read " + definition.Name + ": " + snapshot.Error + " " +
                        (provider.HasValue ? CaptureHint(definition.Symbol, definition.SymbolOffset) :
                            definition.Experimental ? "This value uses a game-build-specific module offset." : "Check the prerequisite details."));
                return Success();
            }
        }

        /// <summary>Explicitly retries this value's provider without preparing unrelated sources.</summary>
        public bool RetryValueReady(GameValueDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            lock (gate)
            {
                TableEffect? provider = ProviderFor(definition.Symbol);
                if (provider == TableEffect.AmmoPointer && manager.IsEnabled(TableEffect.InfiniteAmmo))
                    provider = TableEffect.InfiniteAmmo;
                if (provider.HasValue) preparationErrors.Remove(provider.Value);
                return EnsureValueReady(definition);
            }
        }

        private bool EnsureInstalled(TableEffect effect, bool retryFailure = true)
        {
            if (manager.IsEnabled(effect))
            {
                if (effect == TableEffect.DisableResolutionScaling) return Success();
                string patchError = ReadPatchError(effect);
                if (patchError.Length != 0)
                {
                    preparationErrors[effect] = patchError;
                    return Fail(patchError);
                }
                preparationErrors.Remove(effect);
                return Success();
            }
            // A visible group can contain dozens of values sharing one provider. Keep one failed
            // installation result until an explicit retry instead of rescanning the module per row.
            if (!retryFailure && preparationErrors.TryGetValue(effect, out string error))
                return Fail(error + " Use Prepare / retry to try this hook again.");
            return Record(effect, manager.Enable(effect));
        }

        private bool ReplaceProvider(TableEffect previous, TableEffect requested)
        {
            if (!manager.IsEnabled(previous)) return EnsureInstalled(requested);
            byte[] previousAlert = previous == TableEffect.ForceAlertLevel ? manager.ReadSymbol("iAlertLevel", 0, 4) : null;
            if (!Record(previous, manager.Disable(previous))) return false;
            if (Record(requested, manager.Enable(requested))) return true;

            string activationError = lastError;
            // A failed installation can retain state. Restore the former provider only after
            // the new effect has been completely removed by the normal checked cleanup path.
            if (!manager.Disable(requested))
                return Fail(activationError + " Cleanup also failed: " + manager.LastError);
            if (!manager.Enable(previous))
            {
                string restoreError = manager.LastError;
                preparationErrors[previous] = restoreError;
                return Fail(activationError + " Could not restore the previous hook: " + restoreError);
            }
            preparationErrors.Remove(previous);
            if (previousAlert != null && !manager.SetAlertLevel(BitConverter.ToUInt32(previousAlert, 0)))
                return Fail(activationError + " The previous hook was restored, but its alert setting could not be restored: " + manager.LastError);
            return Fail(activationError + " The previous hook was restored.");
        }

        private IReadOnlyList<PrerequisiteStatus> ReadStatuses()
        {
            var states = Prerequisites.Select(ReadStatus).ToList();
            bool resolutionReady = manager.Actions.ResolveSymbol("dynResEnabled") != IntPtr.Zero;
            states.Add(new PrerequisiteStatus(TableEffect.DisableResolutionScaling, "Resolution flag (discovery)",
                resolutionReady, resolutionReady, resolutionReady ? "Address located. Discovery does not change resolution scaling." :
                preparationErrors.TryGetValue(TableEffect.DisableResolutionScaling, out string error) ? error : "Not prepared."));
            return states.AsReadOnly();
        }

        private PrerequisiteStatus ReadStatus(TableEffect effect)
        {
            string name = NameFor(effect);
            bool ammoCheat = effect == TableEffect.AmmoPointer && manager.IsEnabled(TableEffect.InfiniteAmmo);
            bool installed = manager.IsEnabled(effect) || ammoCheat;
            if (effect == TableEffect.ForceAlertLevel && manager.IsEnabled(TableEffect.NoAlerts))
                return new PrerequisiteStatus(effect, name, false, false,
                    "No Alerts is active. Select Force Alert Level to switch; other effects remain available.");
            if (!installed)
            {
                string detail = preparationErrors.TryGetValue(effect, out string error) ? error :
                    "Hook is off. Use Prepare / retry to attach to mgs4.exe and enable it.";
                return new PrerequisiteStatus(effect, name, false, false, detail);
            }
            string patchError = ReadPatchError(ammoCheat ? TableEffect.InfiniteAmmo : effect);
            if (patchError.Length != 0)
                return new PrerequisiteStatus(effect, name, false, false, patchError);

            if (effect == TableEffect.StageLoaderHook)
                return new PrerequisiteStatus(effect, name, true, true, "Ready. Loading is queued only when you press Load selected stage.");

            if (effect == TableEffect.ForceAlertLevel)
            {
                byte[] setting = manager.ReadSymbol("iAlertLevel", 0, 4);
                if (setting == null) return new PrerequisiteStatus(effect, name, true, false, manager.LastError);
                uint level = BitConverter.ToUInt32(setting, 0);
                return new PrerequisiteStatus(effect, name, true, true, level == uint.MaxValue ?
                    "Ready. Game decides the alert level until you select an override." :
                    "Ready. Alert override is " + level + ".");
            }

            string symbol = SymbolFor(effect);
            byte[] pointer = manager.ReadSymbol(symbol, 0, 8);
            if (pointer == null) return new PrerequisiteStatus(effect, name, true, false, manager.LastError);
            if (BitConverter.ToInt64(pointer, 0) <= 0)
                return new PrerequisiteStatus(effect, name, true, false, "Hook installed; waiting for a capture. " + CaptureHint(symbol, 0));

            GameValueId? probe = effect == TableEffect.PlayerPointer ? GameValueId.Health :
                effect == TableEffect.InventoryPointer ? GameValueId.SolidEyeCurrent :
                effect == TableEffect.ActorCollector ? GameValueId.PlayerX : (GameValueId?)null;
            if (probe.HasValue)
            {
                GameValueSnapshot snapshot = manager.ReadValueSnapshot((int)probe.Value);
                if (snapshot.Value == null)
                    return new PrerequisiteStatus(effect, name, true, false,
                        "Pointer captured, but its value is unreadable. " + CaptureHint(symbol, 0));
            }

            string ready = "Ready. Pointer captured at 0x" + BitConverter.ToUInt64(pointer, 0).ToString("X") + ".";
            if (effect == TableEffect.ActorCollector) ready += " Actor is latched; check its coordinates before editing. The fallback ring is separate.";
            if (effect == TableEffect.AmmoPointer) ready += ammoCheat ? " Infinite Ammo is active." : " Capture only; ammo is unchanged.";
            if (effect == TableEffect.EnemyControl)
            {
                byte[] mode = manager.ReadSymbol("bEnemyMode", 0, 1);
                if (mode == null) return new PrerequisiteStatus(effect, name, true, false, manager.LastError);
                ready += mode[0] == 0 ? " Enemy mode is Off." : " Enemy mode is " + mode[0] + ".";
            }
            return new PrerequisiteStatus(effect, name, true, true, ready);
        }

        private string ReadPatchError(TableEffect effect)
        {
            IReadOnlyList<EffectPatchSnapshot> patches = manager.GetEffectPatchSnapshots(effect);
            string diagnosticError = patches.Count == 0 ? manager.LastError : string.Empty;
            if (patches.Count == 0 || patches.Any(patch => patch.CurrentBytes == null || patch.ReplacementBytes == null ||
                !patch.CurrentBytes.SequenceEqual(patch.ReplacementBytes)))
                return effect + " hook bytes are unavailable or have changed. Disable matching Cheat Engine scripts, " +
                    "use Disable All to clean up trainer hooks, then Prepare / retry." +
                    (diagnosticError.Length == 0 ? string.Empty : " " + diagnosticError);
            return string.Empty;
        }

        private bool Record(TableEffect effect, bool success)
        {
            if (success)
            {
                preparationErrors.Remove(effect);
                return Success();
            }
            string error = manager.LastError;
            preparationErrors[effect] = error;
            return Fail(error);
        }

        private bool Success() { lastError = string.Empty; return true; }
        private bool Fail(string error) { lastError = error; return false; }

        private static TableEffect? ProviderFor(string symbol)
        {
            switch (symbol)
            {
                case "mgsStage": case "mgsAct": case "mgsDiff": case "mgsRank":
                case "pPlayer": return TableEffect.PlayerPointer;
                case "pInv": return TableEffect.InventoryPointer;
                case "pSlot": case "pRing": case "hbCnt": return TableEffect.ActorCollector;
                case "pAmmo": return TableEffect.AmmoPointer;
                case "pEnemy": case "bEnemyMode": return TableEffect.EnemyControl;
                case "iAlertLevel": return TableEffect.ForceAlertLevel;
                default: return null;
            }
        }

        private static bool IsCapturedPointer(string symbol)
            => symbol == "pPlayer" || symbol == "pInv" || symbol == "pSlot" || symbol == "pRing" || symbol == "pAmmo" || symbol == "pEnemy";

        private static string SymbolFor(TableEffect effect)
        {
            switch (effect)
            {
                case TableEffect.PlayerPointer: return "pPlayer";
                case TableEffect.InventoryPointer: return "pInv";
                case TableEffect.ActorCollector: return "pSlot";
                case TableEffect.AmmoPointer: return "pAmmo";
                case TableEffect.EnemyControl: return "pEnemy";
                default: return "iAlertLevel";
            }
        }

        private static string NameFor(TableEffect effect)
        {
            switch (effect)
            {
                case TableEffect.PlayerPointer: return "Player values";
                case TableEffect.InventoryPointer: return "Inventory values";
                case TableEffect.ActorCollector: return "Actor positions";
                case TableEffect.AmmoPointer: return "Ammo pointer";
                case TableEffect.EnemyControl: return "Enemy pointer / mode";
                case TableEffect.StageLoaderHook: return "Stage loader (idle)";
                default: return "Alert setting";
            }
        }

        private static string CaptureHint(string symbol, long offset)
        {
            switch (symbol)
            {
                case "pPlayer": return "Resume gameplay so the player update can run.";
                case "pInv": return "Open the inventory and select an item or weapon, then retry.";
                case "pAmmo": return "Equip a weapon and let its ammo update, then retry.";
                case "pEnemy": case "bEnemyMode": return "Resume gameplay near an enemy so its update can run.";
                case "pSlot": return "Resume gameplay and move so the position writer can latch an actor.";
                case "pRing": return
                    "Actor slot " + (offset / GameValueDefinitionManager.ActorSlotStride) + " has not been populated. Resume gameplay where more actors update; unused slots may remain empty.";
                case "iAlertLevel": return "Use Prepare / retry to make the alert setting available.";
                default: return "Resume gameplay and retry the read.";
            }
        }
    }

    public sealed class PrerequisiteStatus
    {
        internal PrerequisiteStatus(TableEffect effect, string name, bool installed, bool ready, string detail)
        {
            Effect = effect;
            Name = name;
            Installed = installed;
            Ready = ready;
            Detail = detail;
        }
        public TableEffect Effect { get; }
        public string Name { get; }
        public bool Installed { get; }
        public bool Ready { get; }
        public string Detail { get; }
    }
}
