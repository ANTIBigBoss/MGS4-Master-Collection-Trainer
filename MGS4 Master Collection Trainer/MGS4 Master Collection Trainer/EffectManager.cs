using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Opt-in implementations of the supplied Cheat Engine table. No timers or GUI activation.</summary>
    public sealed partial class EffectManager
    {
        public static EffectManager Instance { get; } = new EffectManager();

        private readonly object gate = new object();
        private readonly Dictionary<TableEffect, ActiveEffect> active = new Dictionary<TableEffect, ActiveEffect>();
        private readonly Dictionary<TableEffect, PatchHistory> patchHistory = new Dictionary<TableEffect, PatchHistory>();
        private readonly PlayerHookManager player = PlayerHookManager.Instance;
        private readonly StressManager stress = StressManager.Instance;
        private Process process;
        private IntPtr handle;
        private string lastError = string.Empty;

        private EffectManager()
        {
            Actions = new GameActionManager(this);
            Values = new GameValueManager(ReadValue, WriteValue, WriteFrozenValue);
        }

        public GameValueManager Values { get; }
        public GameActionManager Actions { get; }
        public string LastError { get { lock (gate) return lastError; } }

        internal T WithSession<T>(Func<Process, IntPtr, T> action)
        {
            lock (gate)
            {
                EnsureSession();
                return action(process, handle);
            }
        }

        internal IntPtr ResolveSymbolAddress(string symbol)
        {
            lock (gate) { EnsureSession(); return ResolveSymbol(symbol); }
        }

        public bool IsEnabled(TableEffect effect)
        {
            lock (gate)
            {
                bool alive = SessionAlive();
                // Existing GUI controls own these shared managers and may enable them before
                // this facade opens a session of its own.
                if (effect == TableEffect.PlayerPointer) return (!alive || player.AttachedProcessId == process.Id) && player.IsEnabled;
                if (effect == TableEffect.NoStress) return (!alive || stress.AttachedProcessId == process.Id) && stress.IsEnabled;
                if (!alive) return false;
                if (effect == TableEffect.DisableResolutionScaling) return Actions.ResolutionScalingDisabled;
                return active.TryGetValue(effect, out ActiveEffect state) && state.Installed &&
                    state.Plan.Patches.All(p => Equal(Read(p.Address, p.Replacement.Length), p.Replacement)) &&
                    HookRecoveryManager.TryRecover(effect, handle, state.Anchors, ResolveSymbol, out _, out _, out _);
            }
        }

        public bool Enable(TableEffect effect)
        {
            lock (gate)
            {
                ActiveEffect state = null;
                try
                {
                    EnsureSession();
                    EnsureRecovery();
                    if ((effect == TableEffect.ActorCollector || effect == TableEffect.WalkThroughWalls) && !UpgradeLegacyActor())
                        return false;
                    if (effect == TableEffect.DisableResolutionScaling)
                    {
                        Actions.SetResolutionScalingDisabled(true);
                        return Success();
                    }
                    if (effect == TableEffect.StageLoaderHook) Actions.BuildStageList();
                    var range = ScanRange();
                    if (effect == TableEffect.PlayerPointer)
                    {
                        if (!player.EnableForProcess(process, range.Start, range.Size)) return Fail(player.LastError);
                        GetEffectPatchSnapshots(effect);
                        return Success();
                    }
                    if (effect == TableEffect.NoStress)
                    {
                        if (!stress.EnableForProcess(process, range.Start, range.Size)) return Fail(stress.LastError);
                        GetEffectPatchSnapshots(effect);
                        return Success();
                    }
                    EffectDefinition definition = EffectDefinitionManager.All[effect];
                    if (active.TryGetValue(effect, out state))
                    {
                        if (state.Installed && state.Plan.Patches.All(p => Equal(Read(p.Address, p.Replacement.Length), p.Replacement)))
                            return Success();
                        return Fail("The effect has retained state. Disable it before enabling again: " + effect);
                    }
                    foreach (TableEffect dependency in definition.Dependencies)
                        if (!IsEnabled(dependency)) return Fail("Enable " + dependency + " before " + effect + ".");
                    foreach (TableEffect conflict in definition.Conflicts)
                        if (active.ContainsKey(conflict) || IsEnabled(conflict))
                            return Fail(effect + " cannot run together with " + conflict + ".");

                    var anchors = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
                    foreach (SignatureDefinition signature in definition.Signatures)
                    {
                        var matches = AobRecoveryManager.ScanExecutable(handle, range.Start, range.Size, signature.Pattern, signature.Mask);
                        if (matches.Count != 1)
                            throw new InvalidOperationException(signature.DisplayName + " must match exactly once; found " + matches.Count + ".");
                        anchors.Add(signature.Key, matches[0]);
                    }

                    state = new ActiveEffect { Definition = definition };
                    active.Add(effect, state);
                    if (definition.AllocationSize > 0)
                        state.Allocation = RemoteCodeManager.AllocateNear(handle, anchors[definition.PrimarySignatureKey], definition.AllocationSize);
                    state.Plan = definition.Build(new EffectBuildContext(state.Allocation, anchors, ResolveSymbol, Read));
                    state.Anchors = anchors;
                    ValidatePlan(state);
                    if (state.Allocation != IntPtr.Zero)
                    {
                        if (!MemoryManager.WriteMemory(handle, state.Allocation, state.Plan.AllocationBytes))
                            throw new InvalidOperationException("Could not write effect code and initial settings.");
                        RemoteCodeManager.MakeExecutable(handle, state.Allocation, state.Plan.AllocationBytes.Length);
                        RemoteCodeManager.Flush(handle, state.Allocation, state.Plan.AllocationBytes.Length);
                    }

                    using (var paused = RemoteCodeManager.SuspendThreads(process))
                    {
                        // Preflight every site before touching any site (especially the two suppressor decrements).
                        foreach (CodePatch patch in state.Plan.Patches)
                        {
                            if (!Equal(Read(patch.Address, patch.Original.Length), patch.Original))
                                throw new InvalidOperationException("Original instruction bytes changed before installation.");
                            if (paused.HasInstructionPointerInRange(MemoryManager.AddOffset(patch.Address, 1), patch.Original.Length - 1))
                                throw new InvalidOperationException("A thread is inside an instruction to replace. Retry Enable.");
                        }

                        var changed = new List<CodePatch>();
                        try
                        {
                            foreach (CodePatch patch in state.Plan.Patches)
                            {
                                try { RemoteCodeManager.ReplaceCode(handle, patch.Address, patch.Original, patch.Replacement); }
                                catch (RemoteCodePatchException ex) { state.MayReferenceCode = !ex.OriginalBytesRestored; throw; }
                                changed.Add(patch);
                            }
                            state.MayReferenceCode = true;
                            state.Installed = true;
                        }
                        catch (Exception installationError)
                        {
                            var failures = new List<Exception> { installationError };
                            foreach (CodePatch patch in changed.AsEnumerable().Reverse())
                            {
                                try { RemoteCodeManager.ReplaceCode(handle, patch.Address, patch.Replacement, patch.Original); }
                                catch (Exception rollbackError) { state.MayReferenceCode = true; failures.Add(rollbackError); }
                            }
                            throw new AggregateException("Effect installation failed; any uncertain state was retained for cleanup.", failures);
                        }
                    }
                    LoggingManager.Instance.Log("Enabled table effect: " + effect);
                    patchHistory[effect] = new PatchHistory { ProcessId = process.Id, StartTime = process.StartTime, Patches = state.Plan.Patches };
                    return Success();
                }
                catch (Exception ex)
                {
                    if (state != null && !state.MayReferenceCode && active.ContainsKey(effect))
                    {
                        if (state.Allocation == IntPtr.Zero || !SessionAlive() || RemoteCodeManager.Free(handle, state.Allocation))
                            active.Remove(effect);
                    }
                    return Fail("Could not enable " + effect + ": " + ex.Message);
                }
            }
        }

        private static void ValidatePlan(ActiveEffect state)
        {
            EffectPlan plan = state.Plan;
            if (plan == null || plan.Patches == null || plan.Patches.Length == 0)
                throw new InvalidOperationException("The effect contains no instruction patches.");
            if (state.Allocation != IntPtr.Zero && (plan.AllocationBytes == null || plan.AllocationBytes.Length == 0 ||
                plan.AllocationBytes.Length > state.Definition.AllocationSize))
                throw new InvalidOperationException("Invalid effect allocation data.");
            foreach (CodePatch patch in plan.Patches)
                if (patch.Address.ToInt64() <= 0 || patch.Original == null || patch.Replacement == null ||
                    patch.Original.Length == 0 || patch.Original.Length != patch.Replacement.Length)
                    throw new InvalidOperationException("Invalid instruction patch length/address.");
            foreach (int offset in plan.SymbolOffsets.Values)
                if (state.Allocation == IntPtr.Zero || offset < 0 || offset >= state.Definition.AllocationSize)
                    throw new InvalidOperationException("An effect symbol lies outside its allocation.");
        }

        public bool Disable(TableEffect effect)
        {
            lock (gate)
            {
                try
                {
                    bool alive = SessionAlive();
                    if (alive) EnsureRecovery();
                    if (effect == TableEffect.DisableResolutionScaling)
                    {
                        Actions.SetResolutionScalingDisabled(false);
                        return Success();
                    }
                    if (effect == TableEffect.PlayerPointer)
                        return (alive && player.AttachedProcessId != process.Id) || player.Disable() ? Success() : Fail(player.LastError);
                    if (effect == TableEffect.NoStress)
                        return (alive && stress.AttachedProcessId != process.Id) || stress.Disable() ? Success() : Fail(stress.LastError);
                    if (!alive) return Success();
                    if (!active.TryGetValue(effect, out ActiveEffect state)) return Success();
                    foreach (var dependent in active)
                        if (dependent.Key != effect && dependent.Value.Definition.Dependencies.Contains(effect))
                            return Fail("Disable " + dependent.Key + " before " + effect + ".");

                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        bool busy = false;
                        using (var paused = RemoteCodeManager.SuspendThreads(process))
                        {
                            busy = state.Allocation != IntPtr.Zero &&
                                paused.HasInstructionPointerInRange(state.Allocation, state.Definition.AllocationSize);
                            if (state.Plan != null)
                            {
                                foreach (CodePatch patch in state.Plan.Patches)
                                {
                                    byte[] current = Read(patch.Address, patch.Original.Length);
                                    if (!Equal(current, patch.Original) && !Equal(current, patch.Replacement))
                                    {
                                        state.Installed = false;
                                        throw new InvalidOperationException("Foreign or partial instruction bytes found. No code was overwritten; state was retained.");
                                    }
                                    busy |= paused.HasInstructionPointerInRange(MemoryManager.AddOffset(patch.Address, 1), patch.Original.Length - 1);
                                }
                                if (state.Definition.ActiveCallCounterSymbol != null && state.Allocation != IntPtr.Zero)
                                {
                                    IntPtr counterAddress = MemoryManager.AddOffset(state.Allocation,
                                        state.Plan.SymbolOffsets[state.Definition.ActiveCallCounterSymbol]);
                                    byte[] counter = Read(counterAddress, 4);
                                    busy |= counter == null || BitConverter.ToUInt32(counter, 0) != 0;
                                }
                            }
                            if (!busy)
                            {
                                if (state.Plan != null)
                                    foreach (CodePatch patch in state.Plan.Patches.Reverse())
                                        if (!Equal(Read(patch.Address, patch.Original.Length), patch.Original))
                                        {
                                            try { RemoteCodeManager.ReplaceCode(handle, patch.Address, patch.Replacement, patch.Original); }
                                            catch { state.Installed = false; throw; }
                                        }
                                state.Installed = false;
                                state.MayReferenceCode = false;
                                if (state.Allocation != IntPtr.Zero && !RemoteCodeManager.Free(handle, state.Allocation))
                                    throw new InvalidOperationException("Instructions restored, but allocation could not be freed. Retry Disable.");
                                state.Allocation = IntPtr.Zero;
                            }
                        }
                        if (!busy)
                        {
                            active.Remove(effect);
                            observations[effect] = new EffectMemoryStatus(effect, EffectMemoryState.Inactive, "Original instructions restored.");
                            LoggingManager.Instance.Log("Disabled table effect: " + effect);
                            return Success();
                        }
                        Thread.Sleep(10); // outside the pause scope
                    }
                    return Fail("Effect code or a pending call is still executing. Retry Disable: " + effect);
                }
                catch (Exception ex)
                {
                    if (!SessionAlive()) return Success();
                    return Fail("Could not disable " + effect + ": " + ex.Message);
                }
            }
        }

        /// <summary>Clears freezes, restores dependents first, and attempts every cleanup even after a failure.</summary>
        public bool DisableAll()
        {
            lock (gate)
            {
                Values.ClearFreezes();
                if (SessionAlive()) EnsureRecovery();
                Actions.SetResolutionScalingDisabled(false);
                var errors = new List<string>();
                var ordered = active.Keys.OrderByDescending(id => EffectDefinitionManager.All[id].Dependencies.Length).ToList();
                ordered.Add(TableEffect.NoStress);
                ordered.Add(TableEffect.PlayerPointer);
                foreach (TableEffect effect in ordered)
                    if (!Disable(effect)) errors.Add(lastError);
                if (errors.Count != 0) return Fail(string.Join(Environment.NewLine, errors));
                CloseSession();
                return Success();
            }
        }

        private sealed class ActiveEffect
        {
            internal EffectDefinition Definition;
            internal EffectPlan Plan;
            internal IntPtr Allocation;
            internal Dictionary<string, IntPtr> Anchors;
            internal bool Installed;
            internal bool MayReferenceCode;
            internal bool Legacy;
        }

        /// <summary>Returns the remote symbol's storage address, not the captured object it contains.</summary>
        public IntPtr GetSymbolAddress(string symbol)
        {
            lock (gate)
            {
                try { EnsureSession(); return ResolveSymbol(symbol); }
                catch (Exception ex) { Fail(ex.Message); return IntPtr.Zero; }
            }
        }

        /// <summary>Resolves a catalog field for comparison with CE; does not enable its required hook.</summary>
        public IntPtr GetValueAddress(GameValueId id) => GetValueAddress((int)id);

        public IntPtr GetValueAddress(int id)
        {
            lock (gate)
            {
                try
                {
                    GameValueDefinition definition = GameValueDefinitionManager.Get(id);
                    if (definition.IsLocal) { Success(); return IntPtr.Zero; }
                    EnsureSession();
                    IntPtr address = definition.ResolveAddress(handle, ResolveSymbol);
                    Success();
                    return address;
                }
                catch (Exception ex) { Fail(ex.Message); return IntPtr.Zero; }
            }
        }

        /// <summary>Resolves once so the displayed value and address describe the same memory read.</summary>
        public GameValueSnapshot ReadValueSnapshot(int id)
        {
            lock (gate)
            {
                IntPtr address = IntPtr.Zero;
                try
                {
                    GameValueDefinition definition = GameValueDefinitionManager.Get(id);
                    byte[] bytes;
                    if (definition.IsLocal) bytes = Actions.ReadLocalValue(definition.Symbol);
                    else
                    {
                        EnsureSession();
                        address = definition.ResolveAddress(handle, ResolveSymbol);
                        bytes = Read(address, definition.ByteCount);
                    }
                    if (bytes == null || bytes.Length != definition.ByteCount) throw new InvalidOperationException("Could not read " + definition.Name + ".");
                    object value = GameValueManager.Decode(definition.Type, bytes);
                    Success();
                    return new GameValueSnapshot(address, value, string.Empty);
                }
                catch (Exception ex) { Fail(ex.Message); return new GameValueSnapshot(address, null, ex.Message); }
            }
        }

        /// <summary>Reads saved patch sites without rescanning or writing to the game.</summary>
        public IReadOnlyList<EffectPatchSnapshot> GetEffectPatchSnapshots(TableEffect effect)
        {
            lock (gate)
            {
                var snapshots = new List<EffectPatchSnapshot>();
                try
                {
                    SessionAlive();
                    if (effect == TableEffect.PlayerPointer || effect == TableEffect.NoStress)
                    {
                        int ownerId = effect == TableEffect.PlayerPointer ? player.AttachedProcessId : stress.AttachedProcessId;
                        IntPtr address = effect == TableEffect.PlayerPointer ? player.HookAddress : stress.PatchAddress;
                        if (ownerId != 0 && address != IntPtr.Zero)
                        {
                            byte[] original = effect == TableEffect.PlayerPointer
                                ? X64CodeManager.ParseHex("48 63 82 60 01 00 00")
                                : X64CodeManager.ParseHex("66 41 89 81 50 0B 00 00");
                            byte[] replacement = effect == TableEffect.PlayerPointer ? player.InstalledPatch
                                : Enumerable.Repeat((byte)0x90, original.Length).ToArray();
                            if (replacement != null)
                                using (Process owner = Process.GetProcessById(ownerId))
                                    patchHistory[effect] = new PatchHistory { ProcessId = ownerId, StartTime = owner.StartTime,
                                        Patches = new[] { new CodePatch(address, original, replacement) } };
                        }
                    }
                    else if (active.TryGetValue(effect, out ActiveEffect state) && state.Plan != null)
                        patchHistory[effect] = new PatchHistory { ProcessId = process.Id, StartTime = process.StartTime, Patches = state.Plan.Patches };
                    if (patchHistory.TryGetValue(effect, out PatchHistory history))
                    {
                        using (Process owner = Process.GetProcessById(history.ProcessId))
                        {
                            if (owner.HasExited || owner.StartTime != history.StartTime)
                            {
                                patchHistory.Remove(effect);
                                return snapshots.AsReadOnly();
                            }
                            IntPtr ownerHandle = MemoryManager.OpenGameProcess(owner);
                            try
                            {
                                foreach (CodePatch patch in history.Patches)
                                    snapshots.Add(new EffectPatchSnapshot(patch.Address, patch.Original, patch.Replacement,
                                        MemoryManager.ReadMemoryBytes(ownerHandle, patch.Address, patch.Original.Length)));
                            }
                            finally { MemoryManager.CloseGameProcess(ownerHandle); }
                        }
                    }
                    Success();
                }
                catch (Exception ex)
                {
                    patchHistory.Remove(effect);
                    Fail("Could not read patch diagnostics: " + ex.Message);
                }
                return snapshots.AsReadOnly();
            }
        }

        private sealed class PatchHistory
        {
            internal int ProcessId;
            internal DateTime StartTime;
            internal CodePatch[] Patches;
        }

        public byte[] ReadSymbol(string symbol, int offset, int count)
        {
            lock (gate)
            {
                try
                {
                    EnsureSession();
                    ValidateSymbolRange(symbol, offset, count, false);
                    IntPtr address = ResolveSymbol(symbol);
                    if (address == IntPtr.Zero) throw new InvalidOperationException("Enable the effect that provides " + symbol + ".");
                    byte[] bytes = Read(MemoryManager.AddOffset(address, offset), count);
                    if (bytes == null) throw new InvalidOperationException("Could not read symbol " + symbol + ".");
                    Success();
                    return bytes;
                }
                catch (Exception ex) { Fail(ex.Message); return null; }
            }
        }

        public bool WriteSymbol<T>(string symbol, int offset, T value)
        {
            lock (gate)
            {
                try
                {
                    EnsureSession();
                    byte[] bytes = MemoryManager.GetValueBytes(value);
                    ValidateSymbolRange(symbol, offset, bytes.Length, true);
                    IntPtr address = ResolveSymbol(symbol);
                    if (address == IntPtr.Zero) throw new InvalidOperationException("Enable the effect that provides " + symbol + ".");
                    return MemoryManager.WriteMemory(handle, MemoryManager.AddOffset(address, offset), bytes)
                        ? Success() : Fail("Could not write symbol " + symbol + ".");
                }
                catch (Exception ex) { return Fail(ex.Message); }
            }
        }

        public bool SetEnemyMode(EnemyControlMode mode)
        {
            if (!Enum.IsDefined(typeof(EnemyControlMode), mode)) return Fail("Unknown enemy mode.");
            return WriteSetting(GameValueId.EnemyMode, (byte)mode);
        }

        /// <summary>Null uses the game's result; otherwise overrides it with the supplied value.</summary>
        public bool SetAlertLevel(uint? level) => WriteSetting(GameValueId.AlertLevel, level ?? uint.MaxValue);

        public bool SetWalkThroughWallsActive(bool value) => WriteSymbol("bWTW", 0, value ? 1 : 0);

        private bool WriteSetting(GameValueId id, object value)
        {
            lock (gate) return Values.Write(id, value) ? Success() : Fail(Values.LastError);
        }

        private void ValidateSymbolRange(string symbol, int offset, int count, bool writing)
        {
            int size;
            switch (symbol)
            {
                case "pRing": size = 64; break;
                case "pSlot": case "pPlayer": case "pAmmo": case "pInv": case "pEnemy": case "pIdx": size = 8; break;
                case "bEnemyMode": size = 1; break;
                case "iAlertLevel": case "bWTW": case "lastDX": case "lastDZ": case "wtwCnt": case "fEps":
                case "hbCnt": case "stageIdSel": case "stageFlags": case "bStageFire":
                    size = 4; break;
                case "dynResEnabled": size = 1; break;
                case "alertActiveCalls": case "stageActiveCalls": case "camoActiveCalls":
                    if (writing) throw new InvalidOperationException("The active-call counter is read-only.");
                    size = 4; break;
                default: throw new ArgumentException("Unknown data symbol: " + symbol, nameof(symbol));
            }
            if (symbol == "pSlot" && active.TryGetValue(TableEffect.ActorCollector, out ActiveEffect collector) && collector.Legacy) size = 64;
            // Limit the entire serialized access, so an offset or wide value cannot overwrite
            // adjacent settings, executable code, or the alert hook's pending-call bookkeeping.
            if (offset < 0 || count <= 0 || offset > size || count > size - offset)
                throw new ArgumentOutOfRangeException(nameof(offset), "The access must stay inside " + symbol + " (" + size + " bytes).");
        }

        private byte[] ReadValue(GameValueDefinition definition)
        {
            lock (gate)
            {
                if (definition.IsLocal) return Actions.ReadLocalValue(definition.Symbol);
                EnsureSession();
                return Read(definition.ResolveAddress(handle, ResolveSymbol), definition.ByteCount);
            }
        }

        private bool WriteValue(GameValueDefinition definition, byte[] bytes)
        {
            lock (gate)
            {
                if (definition.ReadOnly) throw new InvalidOperationException("This computed value is read-only.");
                if (bytes == null || bytes.Length != definition.ByteCount) throw new ArgumentException("Incorrect value width.");
                if (definition.IsLocal) { Actions.WriteLocalValue(definition.Symbol, bytes); return true; }
                EnsureSession();
                return MemoryManager.WriteMemory(handle, definition.ResolveAddress(handle, ResolveSymbol), bytes);
            }
        }

        private bool WriteFrozenValue(GameValueDefinition definition, byte[] bytes, Func<bool> isCurrent)
        {
            lock (gate)
            {
                // A freeze belongs to the session in which it was requested. Do not attach
                // a fresh game for an old snapshot, even for values that use fixed module offsets.
                if (!SessionAlive() || !isCurrent()) return false;
                if (definition.ReadOnly || definition.IsLocal) return false;
                if (bytes == null || bytes.Length != definition.ByteCount) throw new ArgumentException("Incorrect value width.");
                return MemoryManager.WriteMemory(handle, definition.ResolveAddress(handle, ResolveSymbol), bytes);
            }
        }

        private IntPtr ResolveSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return IntPtr.Zero;
            if (string.Equals(symbol, Constants.PROCESS_MODULE_NAME, StringComparison.OrdinalIgnoreCase))
                return process.MainModule?.BaseAddress ?? IntPtr.Zero;
            if (symbol == "pPlayer")
                return player.AttachedProcessId == process.Id && player.IsEnabled ? player.PointerStorageAddress : IntPtr.Zero;
            foreach (ActiveEffect state in active.Values)
            {
                if (state.Installed && state.Legacy && state.Definition.Effect == TableEffect.ActorCollector && symbol == "pRing")
                    return MemoryManager.AddOffset(state.Allocation, EffectDefinitionManager.DataOffset);
                if (state.Installed && state.Plan.SymbolOffsets.TryGetValue(symbol, out int offset))
                    return MemoryManager.AddOffset(state.Allocation, offset);
            }
            return Actions.ResolveSymbol(symbol);
        }

        /// <summary>
        /// Checks known hook signatures in executable, nonwritable module pages, as the table's Lua check does.
        /// Disable effects first: patched signatures can legitimately disappear while enabled.
        /// </summary>
        public IReadOnlyList<SignatureCheckResult> CheckSignatures()
        {
            lock (gate)
            {
                var results = new List<SignatureCheckResult>();
                try
                {
                    EnsureSession();
                    var range = ScanRange();
                    foreach (SignatureDefinition signature in EffectDefinitionManager.Signatures)
                    {
                        List<IntPtr> matches = AobRecoveryManager.ScanExecutable(handle, range.Start, range.Size,
                            signature.Pattern, signature.Mask).Where(a => IsExecutableReadOnly(a, signature.Pattern.Length)).ToList();
                        bool modified = active.Values.Any(s => s.Installed && s.Definition.Signatures.Any(source =>
                            source.Mask == signature.Mask && source.Pattern.SequenceEqual(signature.Pattern) &&
                            s.Anchors.TryGetValue(source.Key, out IntPtr anchor) &&
                            s.Plan.Patches.Any(p => RangesOverlap(anchor, signature.Pattern.Length, p.Address, p.Original.Length))));
                        modified |= signature.Key == Constants.PlayerHookAobKey && IsEnabled(TableEffect.PlayerPointer);
                        modified |= signature.Key == Constants.StressAobKey && IsEnabled(TableEffect.NoStress);
                        results.Add(new SignatureCheckResult(signature.Key, signature.DisplayName, matches.ToArray(),
                            modified ? "An enabled effect modifies code in this scan area; check again with effects disabled." : string.Empty));
                    }
                    Success();
                }
                catch (Exception ex)
                {
                    Fail("Signature check failed: " + ex.Message);
                    foreach (SignatureDefinition signature in EffectDefinitionManager.Signatures.Skip(results.Count))
                        results.Add(new SignatureCheckResult(signature.Key, signature.DisplayName, new IntPtr[0], lastError));
                }
                return results.AsReadOnly();
            }
        }

        private bool IsExecutableReadOnly(IntPtr address, int count)
        {
            long cursor = address.ToInt64();
            long end = checked(cursor + count);
            UIntPtr infoSize = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
            while (cursor < end)
            {
                if (MemoryManager.NativeMethods.VirtualQueryEx(handle, new IntPtr(cursor), out var region, infoSize) == UIntPtr.Zero)
                    return false;
                const uint Execute = 0x10 | 0x20 | 0x40 | 0x80;
                const uint WritableOrCopy = 0x04 | 0x08 | 0x40 | 0x80;
                if (region.State != 0x1000 || (region.Protect & 0x100) != 0 ||
                    (region.Protect & Execute) == 0 || (region.Protect & WritableOrCopy) != 0) return false;
                long next = checked(region.BaseAddress.ToInt64() + (long)region.RegionSize.ToUInt64());
                if (next <= cursor) return false;
                cursor = next;
            }
            return true;
        }

        private static bool RangesOverlap(IntPtr first, int firstLength, IntPtr second, int secondLength)
        {
            return first.ToInt64() < second.ToInt64() + secondLength && second.ToInt64() < first.ToInt64() + firstLength;
        }

        private (IntPtr Start, long Size) ScanRange()
        {
            ProcessModule module = process.MainModule;
            if (module == null) throw new InvalidOperationException("The game's main module is unavailable.");
            return (module.BaseAddress, module.ModuleMemorySize);
        }

        private void EnsureSession()
        {
            if (SessionAlive()) return;
            try
            {
                process = MemoryManager.GetGameProcess();
                if (process == null) throw new InvalidOperationException("mgs4.exe is not running.");
                using (Process current = Process.GetCurrentProcess())
                    if (process.Id == current.Id) throw new InvalidOperationException("Table effects require an external process.");
                if (process.HasExited) throw new InvalidOperationException("The game process has exited.");
                handle = MemoryManager.OpenGameProcess(process);
                if (handle == IntPtr.Zero) throw new InvalidOperationException("Could not open the game process.");
                foreach (TableEffect effect in patchHistory.Keys.ToArray())
                    if (patchHistory[effect].ProcessId != process.Id || patchHistory[effect].StartTime != process.StartTime)
                        patchHistory.Remove(effect);
            }
            catch { CloseSession(); throw; }
        }

        private bool SessionAlive()
        {
            if (process == null) return false;
            if (!process.HasExited) return true;
            // Game exit invalidates all captured pointers, allocations, and manual freeze requests.
            active.Clear();
            patchHistory.Clear();
            Values.ClearFreezes();
            CloseSession();
            return false;
        }

        private void CloseSession()
        {
            recoveryAttempted = false;
            recoveryModule = null;
            recoveryAnchors.Clear();
            imageLocatedAnchors.Clear();
            observations.Clear();
            Actions.ResetSession();
            MemoryManager.CloseGameProcess(handle);
            process?.Dispose();
            process = null;
            handle = IntPtr.Zero;
        }

        private byte[] Read(IntPtr address, int size) => MemoryManager.ReadMemoryBytes(handle, address, size);
        private static bool Equal(byte[] first, byte[] second) => first != null && second != null && first.SequenceEqual(second);
        private bool Success() { lastError = string.Empty; return true; }
        private bool Fail(string message)
        {
            lock (gate)
            {
                if (lastError != message) LoggingManager.Instance.Log(message);
                lastError = message;
                return false;
            }
        }
    }

    public enum EnemyControlMode : byte { Off = 0, Kill = 1, ClearSleepTimer = 2 }

    public sealed class GameValueSnapshot
    {
        internal GameValueSnapshot(IntPtr address, object value, string error)
        {
            Address = address; Value = value; Error = error; ReadAt = DateTime.UtcNow;
        }
        public IntPtr Address { get; }
        public object Value { get; }
        public string Error { get; }
        public DateTime ReadAt { get; }
    }

    public sealed class EffectPatchSnapshot
    {
        internal EffectPatchSnapshot(IntPtr address, byte[] original, byte[] replacement, byte[] current)
        {
            Address = address;
            OriginalBytes = original == null ? null : Array.AsReadOnly((byte[])original.Clone());
            ReplacementBytes = replacement == null ? null : Array.AsReadOnly((byte[])replacement.Clone());
            CurrentBytes = current == null ? null : Array.AsReadOnly((byte[])current.Clone());
        }
        public IntPtr Address { get; }
        public IReadOnlyList<byte> OriginalBytes { get; }
        public IReadOnlyList<byte> ReplacementBytes { get; }
        public IReadOnlyList<byte> CurrentBytes { get; }
    }

    public sealed class SignatureCheckResult
    {
        internal SignatureCheckResult(string key, string name, IntPtr[] matches, string note)
        {
            Key = key; Name = name; Matches = Array.AsReadOnly(matches); Note = note;
        }
        public string Key { get; }
        public string Name { get; }
        public IReadOnlyList<IntPtr> Matches { get; }
        public int MatchCount => Matches.Count;
        public bool IsUnique => MatchCount == 1;
        public string Note { get; }
    }
}
