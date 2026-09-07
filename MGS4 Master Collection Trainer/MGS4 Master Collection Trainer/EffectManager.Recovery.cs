using System;
using System.Collections.Generic;
using System.Linq;

namespace MGS4_Master_Collection_Trainer
{
    public enum EffectMemoryState { NotChecked, Inactive, Active, Unavailable, Unrecognized }

    public sealed class EffectMemoryStatus
    {
        internal EffectMemoryStatus(TableEffect effect, EffectMemoryState state, string detail)
        { Effect = effect; State = state; Detail = detail; }
        public TableEffect Effect { get; }
        public EffectMemoryState State { get; }
        public string Detail { get; }
    }

    public sealed partial class EffectManager
    {
        private bool recoveryAttempted, upgradingActor;
        private AobRecoveryModule recoveryModule;
        private readonly Dictionary<string, IntPtr[]> recoveryAnchors = new Dictionary<string, IntPtr[]>();
        private readonly HashSet<string> imageLocatedAnchors = new HashSet<string>();
        private readonly Dictionary<TableEffect, EffectMemoryStatus> observations = new Dictionary<TableEffect, EffectMemoryStatus>();

        /// <summary>Reads actual game instructions and adopts verified existing hooks without rewriting their code or settings.</summary>
        public IReadOnlyList<EffectMemoryStatus> InspectExistingEffects()
        {
            lock (gate)
            {
                try { EnsureSession(); EnsureRecovery(); Success(); }
                catch (Exception ex) { Fail("Could not inspect existing effects: " + ex.Message); }
                return Array.AsReadOnly(Enum.GetValues(typeof(TableEffect)).Cast<TableEffect>().Select(GetObservedState).ToArray());
            }
        }

        /// <summary>Returns checked state, including unknown code, instead of equating missing local records with Off.</summary>
        public EffectMemoryStatus GetObservedState(TableEffect effect)
        {
            lock (gate)
            {
                if (IsEnabled(effect)) return new EffectMemoryStatus(effect, EffectMemoryState.Active,
                    active.TryGetValue(effect, out ActiveEffect owned) && owned.Legacy
                        ? effect == TableEffect.Always100PercentCamo
                            ? "Existing legacy camo hook detected; disable then enable to use the current hook."
                            : "Existing legacy actor hook detected."
                        : "Installed instruction bytes verified.");
                if (active.ContainsKey(effect)) return new EffectMemoryStatus(effect, EffectMemoryState.Unrecognized, "The owned hook's instruction bytes changed. Inspect or disable it before retrying.");
                if (effect == TableEffect.PlayerPointer && player.HookAddress != IntPtr.Zero)
                    return new EffectMemoryStatus(effect, EffectMemoryState.Unrecognized, player.LastError);
                if (effect == TableEffect.NoStress && stress.PatchAddress != IntPtr.Zero)
                    return new EffectMemoryStatus(effect, EffectMemoryState.Unrecognized, stress.LastError);
                if (effect == TableEffect.DisableResolutionScaling && SessionAlive())
                {
                    IntPtr flag = Actions.ResolveSymbol("dynResEnabled");
                    byte[] value = flag == IntPtr.Zero ? null : Read(flag, 1);
                    if (value != null) return new EffectMemoryStatus(effect, value[0] == 0 ? EffectMemoryState.Active : EffectMemoryState.Inactive,
                        value[0] == 0 ? "Flag is 0 (repeated writes off)" : "Resolution flag is enabled.");
                }
                if ((effect == TableEffect.PlayerPointer || effect == TableEffect.NoStress) &&
                    observations.TryGetValue(effect, out EffectMemoryStatus previous) && previous.State == EffectMemoryState.Active)
                    return new EffectMemoryStatus(effect, EffectMemoryState.Inactive, "Original instruction restored.");
                return observations.TryGetValue(effect, out EffectMemoryStatus state) ? state :
                    new EffectMemoryStatus(effect, EffectMemoryState.NotChecked, "Memory has not been checked for this game session.");
            }
        }

        private void EnsureRecovery()
        {
            if (recoveryAttempted) return;
            recoveryAttempted = true; // Session callbacks below reenter this same manager.
            var range = ScanRange();
            try { recoveryModule = AobRecoveryManager.OpenModule(process, handle, range.Start, range.Size); }
            catch (Exception ex) { LoggingManager.Instance.Log("Original-image AOB lookup unavailable: " + ex.Message); }
            InspectBasic(TableEffect.PlayerPointer, () => player.AttachExistingForProcess(process, range.Start, range.Size), () => player.LastError);
            InspectBasic(TableEffect.NoStress, () => stress.AttachExistingForProcess(process, range.Start, range.Size), () => stress.LastError);
            // Recover the actor before WTW, and both ammo variants before choosing a neutral provider.
            var order = EffectDefinitionManager.All.Keys.OrderBy(e => e == TableEffect.ActorCollector ? -1 :
                e == TableEffect.WalkThroughWalls ? 2 : e == TableEffect.StageLoaderHook ? 3 : 0).ToArray();
            foreach (TableEffect effect in order)
            {
                if (active.ContainsKey(effect)) continue;
                try
                {
                    if (effect == TableEffect.Always100PercentCamo && InspectLegacyCamo()) continue;
                    EffectDefinition definition = EffectDefinitionManager.All[effect];
                    var anchors = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
                    IntPtr[] primaryCandidates = null;
                    foreach (SignatureDefinition signature in definition.Signatures)
                    {
                        IntPtr[] candidates = LocateRecoveryAnchors(signature, effect);
                        if (signature.Key == definition.PrimarySignatureKey)
                        {
                            primaryCandidates = candidates;
                            continue;
                        }
                        if (candidates.Length != 1) throw new InvalidOperationException(signature.DisplayName + " has " + candidates.Length + " candidate locations.");
                        anchors.Add(signature.Key, candidates[0]);
                    }
                    if (primaryCandidates.Length > 1)
                        primaryCandidates = primaryCandidates.Where(candidate =>
                        {
                            anchors[definition.PrimarySignatureKey] = candidate;
                            return HookRecoveryManager.HasOriginalInstructions(effect, handle, anchors) ||
                                HookRecoveryManager.TryRecover(effect, handle, anchors, ResolveSymbol, out _, out _, out _);
                        }).ToArray();
                    if (primaryCandidates.Length != 1)
                        throw new InvalidOperationException(definition.Name + " has " + primaryCandidates.Length + " verified candidate locations.");
                    anchors[definition.PrimarySignatureKey] = primaryCandidates[0];
                    if (recoveryModule != null && imageLocatedAnchors.Contains(definition.PrimarySignatureKey))
                    {
                        IntPtr primary = anchors[definition.PrimarySignatureKey];
                        var spans = KnownPatchSpans(primary);
                        if (!recoveryModule.ValidateContext(primary, spans, out string error))
                            throw new InvalidOperationException("The surrounding code does not match this game image: " + error);
                    }
                    if (HookRecoveryManager.HasOriginalInstructions(effect, handle, anchors))
                    {
                        observations[effect] = new EffectMemoryStatus(effect, EffectMemoryState.Inactive, "Original instructions verified.");
                        continue;
                    }
                    if (effect == TableEffect.StageLoaderHook) Actions.BuildStageList();
                    if (HookRecoveryManager.TryRecover(effect, handle, anchors, ResolveSymbol,
                        out EffectPlan plan, out IntPtr allocation, out bool legacy, out string reason))
                    {
                        active.Add(effect, new ActiveEffect { Definition = legacy ? HookRecoveryManager.LegacyActorDefinition : definition,
                            Plan = plan, Allocation = allocation, Anchors = anchors, Installed = true, MayReferenceCode = true, Legacy = legacy });
                        patchHistory[effect] = new PatchHistory { ProcessId = process.Id, StartTime = process.StartTime, Patches = plan.Patches };
                        observations[effect] = new EffectMemoryStatus(effect, EffectMemoryState.Active, reason);
                        LoggingManager.Instance.Log("Reconnected to existing effect: " + effect + (legacy ? " (legacy actor layout)" : ""));
                    }
                    else observations[effect] = new EffectMemoryStatus(effect, EffectMemoryState.Unrecognized, reason);
                }
                catch (Exception ex) { observations[effect] = new EffectMemoryStatus(effect, EffectMemoryState.Unavailable, ex.Message); }
            }
            // These two definitions share a patch. A verified owner explains why the other is inactive.
            if (IsEnabled(TableEffect.InfiniteAmmo)) observations[TableEffect.AmmoPointer] = new EffectMemoryStatus(TableEffect.AmmoPointer, EffectMemoryState.Inactive, "Infinite Ammo owns the shared instruction.");
            if (IsEnabled(TableEffect.AmmoPointer)) observations[TableEffect.InfiniteAmmo] = new EffectMemoryStatus(TableEffect.InfiniteAmmo, EffectMemoryState.Inactive, "Neutral ammo capture owns the shared instruction.");
            try { Actions.PrepareResolutionScaling(); }
            catch (Exception ex) { observations[TableEffect.DisableResolutionScaling] = new EffectMemoryStatus(TableEffect.DisableResolutionScaling, EffectMemoryState.Unavailable, ex.Message); }
        }

        private void InspectBasic(TableEffect effect, Func<bool> attach, Func<string> error)
        {
            if (IsEnabled(effect)) return;
            bool found = attach();
            string detail = error();
            observations[effect] = new EffectMemoryStatus(effect, found ? EffectMemoryState.Active :
                detail.Length == 0 ? EffectMemoryState.Inactive : EffectMemoryState.Unavailable,
                found ? "Reconnected to existing instruction state." : detail.Length == 0 ? "Original instruction verified." : detail);
            if (found) GetEffectPatchSnapshots(effect);
        }

        // The previous camo implementation patched a different instruction. Retain its
        // exact ownership metadata so reopening can remove it, then a fresh enable uses
        // the current hook. Inspection itself never rewrites either site.
        private bool InspectLegacyCamo()
        {
            const TableEffect effect = TableEffect.Always100PercentCamo;
            EffectDefinition definition = HookRecoveryManager.LegacyCamoDefinition;
            SignatureDefinition signature = definition.Signatures[0];
            var recovered = new List<ActiveEffect>();
            foreach (IntPtr candidate in LocateRecoveryAnchors(signature, effect))
            {
                var anchors = new Dictionary<string, IntPtr>(StringComparer.Ordinal) { { signature.Key, candidate } };
                if (HookRecoveryManager.HasOriginalInstructions(effect, handle, anchors)) continue;
                if (recoveryModule != null && imageLocatedAnchors.Contains(signature.Key) &&
                    !recoveryModule.ValidateContext(candidate, KnownPatchSpans(candidate), out _)) continue;
                if (HookRecoveryManager.TryRecover(effect, handle, anchors, ResolveSymbol,
                    out EffectPlan plan, out IntPtr allocation, out bool legacy, out _) && legacy)
                    recovered.Add(new ActiveEffect { Definition = definition, Plan = plan, Allocation = allocation,
                        Anchors = anchors, Installed = true, MayReferenceCode = true, Legacy = true });
            }
            if (recovered.Count == 0) return false;
            if (recovered.Count != 1)
                throw new InvalidOperationException("Multiple earlier camo hooks were found; no ambiguous hook was adopted.");
            SignatureDefinition current = EffectDefinitionManager.All[effect].Signatures[0];
            foreach (IntPtr candidate in LocateRecoveryAnchors(current, effect))
            {
                var anchors = new Dictionary<string, IntPtr>(StringComparer.Ordinal) { { current.Key, candidate } };
                if (HookRecoveryManager.TryRecover(effect, handle, anchors, ResolveSymbol, out _, out _, out _))
                    throw new InvalidOperationException("Both earlier and current camo hooks are active; no conflicting hook was adopted.");
            }
            ActiveEffect state = recovered[0];
            active.Add(effect, state);
            patchHistory[effect] = new PatchHistory { ProcessId = process.Id, StartTime = process.StartTime, Patches = state.Plan.Patches };
            observations[effect] = new EffectMemoryStatus(effect, EffectMemoryState.Active,
                "Recognized the earlier camo field hook. Disable then enable camo to use the current conversion hook.");
            LoggingManager.Instance.Log("Reconnected to existing effect: Always100PercentCamo (earlier field hook).");
            return true;
        }

        private IntPtr[] LocateRecoveryAnchors(SignatureDefinition signature, TableEffect effect)
        {
            if (recoveryAnchors.TryGetValue(signature.Key, out IntPtr[] cached)) return cached;
            IntPtr[] matches = recoveryModule?.FindOriginal(signature.Pattern, signature.Mask).ToArray();
            if (matches != null && matches.Length != 0) imageLocatedAnchors.Add(signature.Key);
            else
            {
                var range = ScanRange();
                var result = AobRecoveryManager.ScanExecutable(handle, range.Start, range.Size, signature.Pattern, signature.Mask);
                if (signature.Key == EffectDefinitionManager.All[effect].PrimarySignatureKey || signature.Key == "aCamoLegacy")
                {
                    var enabled = EnabledPattern(signature, effect);
                    if (enabled != null)
                        result.AddRange(AobRecoveryManager.ScanExecutable(handle, range.Start, range.Size, enabled.Item1, enabled.Item2));
                }
                matches = result.Distinct().ToArray();
            }
            recoveryAnchors[signature.Key] = matches;
            return matches;
        }

        private List<AobRecoverySpan> KnownPatchSpans(IntPtr anchor)
        {
            var spans = new List<AobRecoverySpan>();
            // The source file is only a locator. Ignore known patch windows, then independently
            // verify actual live instructions/caves before adopting any effect.
            foreach (SignatureDefinition signature in EffectDefinitionManager.Signatures)
            {
                int[] window = PatchWindow(signature.Key);
                if (window == null) continue;
                foreach (IntPtr site in recoveryModule.FindOriginal(signature.Pattern, signature.Mask))
                {
                    long offset = checked(site.ToInt64() - anchor.ToInt64() + window[0]);
                    if (offset < -128 || offset > 128) continue;
                    spans.Add(new AobRecoverySpan(checked((int)offset), window[1]));
                }
            }
            return spans;
        }

        private static int[] PatchWindow(string key)
        {
            switch (key)
            {
                case "aPlayer": case "aBatt": case "aCamoLegacy": return new[] { 0, 7 };
                case "aCamo": return new[] { 0, 9 };
                case "aLife": case "aStam": case "aStress": return new[] { 0, 8 };
                case "aAmmo": return new[] { 0, 11 };
                case "aReload": return new[] { 0, 4 };
                case "aSupp": return new[] { 0, 16 };
                case "aEnemy": return new[] { 0, 6 };
                case "aNoAlert": case "aAlertFn": case "aAlertCall": case "aInv": return new[] { 0, 5 };
                case "aPos": return new[] { 0x17, 10 };
                case "aNoFilter": case "aNoBlur": return new[] { 0, 3 };
                case "aUiFrame": return new[] { 4, 5 };
                default: return null;
            }
        }

        private static Tuple<byte[], string> EnabledPattern(SignatureDefinition signature, TableEffect effect)
        {
            int[] window = PatchWindow(signature.Key);
            if (window == null || window[0] >= signature.Pattern.Length) return null;
            byte[] replacement;
            if (effect == TableEffect.NeverReload || effect == TableEffect.InfiniteSuppressor)
                replacement = Enumerable.Repeat((byte)0x90, 4).ToArray();
            else if (effect == TableEffect.NoAlerts) replacement = X64CodeManager.ParseHex("33 C0 C3 90 90");
            else if (effect == TableEffect.DisableScreenFilter || effect == TableEffect.DisableMotionBlur)
                replacement = X64CodeManager.ParseHex("33 C0 C3");
            else
            {
                replacement = Enumerable.Repeat((byte)0x90, window[1]).ToArray();
                replacement[0] = 0xE9;
            }
            int length = Math.Max(signature.Pattern.Length, window[0] + replacement.Length);
            byte[] bytes = new byte[length];
            char[] mask = Enumerable.Repeat('x', length).ToArray();
            Buffer.BlockCopy(signature.Pattern, 0, bytes, 0, signature.Pattern.Length);
            signature.Mask.CopyTo(0, mask, 0, signature.Mask.Length);
            Buffer.BlockCopy(replacement, 0, bytes, window[0], replacement.Length);
            for (int i = 0; i < replacement.Length; i++) mask[window[0] + i] = replacement[0] == 0xE9 && i >= 1 && i <= 4 ? '?' : 'x';
            return Tuple.Create(bytes, new string(mask));
        }

        /// <summary>Migrates the recognized earlier actor layout in place, retaining an active WTW setting.</summary>
        public bool UpgradeLegacyActor()
        {
            lock (gate)
            {
                if (upgradingActor || !active.TryGetValue(TableEffect.ActorCollector, out ActiveEffect old) || !old.Legacy) return true;
                upgradingActor = true;
                try
                {
                    byte[] ring = Read(MemoryManager.AddOffset(old.Allocation, 512), 64);
                    byte[] index = Read(MemoryManager.AddOffset(old.Allocation, 576), 8);
                    bool walls = IsEnabled(TableEffect.WalkThroughWalls);
                    byte[] wallSettings = walls ? Read(active[TableEffect.WalkThroughWalls].Allocation + 512, 20) : null;
                    if (walls && !Disable(TableEffect.WalkThroughWalls)) return false;
                    if (!Disable(TableEffect.ActorCollector)) return false;
                    // Wait for old code to finish before this pause. Keep new hooks from executing
                    // with default settings between installation and restoration of their data.
                    using (RemoteCodeManager.SuspendThreads(process))
                    {
                        if (!Enable(TableEffect.ActorCollector)) return false;
                        if (walls && !Enable(TableEffect.WalkThroughWalls)) return false;
                        if (ring != null)
                        {
                            if (!MemoryManager.WriteMemory(handle, ResolveSymbol("pRing"), ring) ||
                                !MemoryManager.WriteMemory(handle, ResolveSymbol("pSlot"), ring.Take(8).ToArray()))
                                throw new InvalidOperationException("Actor migration installed its new hook but could not restore the captured actor list.");
                        }
                        if (index != null && !MemoryManager.WriteMemory(handle, ResolveSymbol("pIdx"), BitConverter.GetBytes(BitConverter.ToUInt64(index, 0) & 7)))
                            throw new InvalidOperationException("The actor list was restored but its insertion index could not be restored.");
                        if (wallSettings != null && !MemoryManager.WriteMemory(handle, ResolveSymbol("bWTW"), wallSettings))
                            throw new InvalidOperationException("WTW was reinstalled but its previous settings could not be restored.");
                    }
                    LoggingManager.Instance.Log("Migrated recovered actor capture to the current latch/ring layout.");
                    return Success();
                }
                catch (Exception ex) { return Fail("Could not update the recovered actor hook: " + ex.Message); }
                finally { upgradingActor = false; }
            }
        }
    }
}
