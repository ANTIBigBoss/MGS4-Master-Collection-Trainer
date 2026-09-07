using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>
    /// Recognizes installed trainer templates from the target's actual instructions and allocation.
    /// This class never patches, allocates, frees, adopts, or resets a captured value.
    /// The caller validates candidate anchors and owns synchronization and subsequent cleanup.
    /// </summary>
    internal static class HookRecoveryManager
    {
        internal static readonly EffectDefinition LegacyActorDefinition = new EffectDefinition(
            TableEffect.ActorCollector, "Actor Collector (legacy rotating slots)",
            new[] { EffectDefinitionManager.Signatures.Single(signature => signature.Key == "aPos") },
            "aPos", EffectDefinitionManager.CaveAllocationSize, BuildLegacyActorCollector);

        internal static readonly EffectDefinition LegacyCamoDefinition = new EffectDefinition(
            TableEffect.Always100PercentCamo, "Always 100% Camo (earlier field hook)",
            new[] { EffectDefinitionManager.Signatures.Single(signature => signature.Key == "aCamoLegacy") },
            "aCamoLegacy", EffectDefinitionManager.CaveAllocationSize, BuildLegacyCamo);

        private static EffectDefinition RecoveryDefinition(TableEffect effect, IReadOnlyDictionary<string, IntPtr> anchors)
            => effect == TableEffect.Always100PercentCamo && IsLegacyCamo(anchors)
                ? LegacyCamoDefinition : EffectDefinitionManager.All[effect];

        private static bool IsLegacyCamo(IReadOnlyDictionary<string, IntPtr> anchors)
            => anchors.ContainsKey("aCamoLegacy") && !anchors.ContainsKey("aCamo");

        internal static bool TryRecover(TableEffect effect, IntPtr handle, IReadOnlyDictionary<string, IntPtr> anchors,
            Func<string, IntPtr> resolveSymbol, out EffectPlan plan, out IntPtr allocation, out bool legacy)
            => TryRecover(effect, handle, anchors, resolveSymbol, out plan, out allocation, out legacy, out _);

        internal static bool TryRecover(TableEffect effect, IntPtr handle, IReadOnlyDictionary<string, IntPtr> anchors,
            Func<string, IntPtr> resolveSymbol, out EffectPlan plan, out IntPtr allocation, out bool legacy, out string reason)
        {
            return TryRecover(effect, anchors, resolveSymbol,
                (address, count) => handle == IntPtr.Zero ? null : MemoryManager.ReadMemoryBytes(handle, address, count),
                (address, count) => IsOwnedExecutableAllocation(handle, address, count),
                out plan, out allocation, out legacy, out reason);
        }

        internal static bool HasOriginalInstructions(TableEffect effect, IntPtr handle,
            IReadOnlyDictionary<string, IntPtr> anchors)
            => HasOriginalInstructions(effect, anchors,
                (address, count) => handle == IntPtr.Zero ? null : MemoryManager.ReadMemoryBytes(handle, address, count));

        private static bool HasOriginalInstructions(TableEffect effect, IReadOnlyDictionary<string, IntPtr> anchors,
            Func<IntPtr, int, byte[]> readMemory)
        {
            try
            {
                if (anchors == null || readMemory == null || !EffectDefinitionManager.All.TryGetValue(effect, out EffectDefinition definition))
                    return false;
                definition = RecoveryDefinition(effect, anchors);
                // No allocation is created: a nearby template base and placeholder external
                // symbols let the definition provide its complete stolen-instruction metadata.
                // Only the Original arrays are inspected, never the generated code/data.
                IntPtr templateBase = definition.AllocationSize == 0 ? IntPtr.Zero : Add(anchors[definition.PrimarySignatureKey], 0x10000);
                EffectPlan expected = definition.Build(new EffectBuildContext(templateBase, anchors,
                    symbol => Add(anchors[definition.PrimarySignatureKey], 0x20000),
                    OriginalInstructionReader(effect, anchors, readMemory)));
                return expected.Patches.All(patch => ReadExact(readMemory, patch.Address, patch.Original.Length).SequenceEqual(patch.Original));
            }
            catch (Exception) { return false; }
        }

        private static bool TryRecover(TableEffect effect, IReadOnlyDictionary<string, IntPtr> anchors,
            Func<string, IntPtr> resolveSymbol, Func<IntPtr, int, byte[]> readMemory,
            Func<IntPtr, int, bool> validateAllocation, out EffectPlan plan, out IntPtr allocation,
            out bool legacy, out string reason)
        {
            plan = null;
            allocation = IntPtr.Zero;
            legacy = false;
            reason = string.Empty;
            try
            {
                if (anchors == null || resolveSymbol == null || readMemory == null || validateAllocation == null)
                    throw new ArgumentException("Recovery requires anchors, a symbol resolver and memory inspection callbacks.");
                if (!EffectDefinitionManager.All.TryGetValue(effect, out EffectDefinition definition))
                {
                    reason = "This effect has no generic instruction template.";
                    return false;
                }
                definition = RecoveryDefinition(effect, anchors);
                if (definition.AllocationSize == 0)
                {
                    EffectPlan direct = definition.Build(new EffectBuildContext(IntPtr.Zero, anchors, resolveSymbol, readMemory));
                    bool allOriginal = true, allReplacement = true;
                    foreach (CodePatch patch in direct.Patches)
                    {
                        byte[] actual = ReadExact(readMemory, patch.Address, patch.Original.Length);
                        allOriginal &= actual.SequenceEqual(patch.Original);
                        allReplacement &= actual.SequenceEqual(patch.Replacement);
                    }
                    if (allOriginal)
                    {
                        reason = "Original instructions are intact; the effect is inactive.";
                        return false;
                    }
                    if (!allReplacement)
                    {
                        reason = "The patch sites contain mixed, partial, or foreign bytes. No effect was adopted.";
                        return false;
                    }
                    plan = direct;
                    reason = "All direct patch sites exactly match the enabled template.";
                    return true;
                }

                IntPtr site = PatchSite(effect, definition, anchors);
                byte[] branch = ReadExact(readMemory, site, 5);
                if (branch[0] != 0xE9)
                {
                    reason = "The candidate site does not contain a relative hook jump.";
                    return false;
                }
                IntPtr candidate = Add(site, checked(5L + BitConverter.ToInt32(branch, 1)));
                if (candidate.ToInt64() <= 0 || !validateAllocation(candidate, definition.AllocationSize))
                {
                    reason = "The jump destination is not a complete, private executable trainer allocation.";
                    return false;
                }
                // Read the entire allocation once. Mutable fields may change while it is read;
                // they are intentionally excluded from identity checks and are never written back.
                byte[] actualAllocation = ReadExact(readMemory, candidate, definition.AllocationSize);
                Func<IntPtr, int, byte[]> originalReader = OriginalInstructionReader(effect, anchors, readMemory);
                var context = new EffectBuildContext(candidate, anchors, resolveSymbol, originalReader);
                EffectPlan expected = definition.Build(context);
                bool legacyCamo = definition == LegacyCamoDefinition;
                if (Matches(effect, expected, actualAllocation, readMemory, legacyCamo))
                {
                    plan = WithActualData(expected, actualAllocation);
                    allocation = candidate;
                    legacy = legacyCamo;
                    reason = legacyCamo ? "Recognized the earlier camo field hook. Disable then enable camo to use the current conversion hook."
                        : "The branch, full immutable code and immutable data match the installed trainer template.";
                    return true;
                }
                if (effect == TableEffect.ActorCollector)
                {
                    EffectPlan old = LegacyActorDefinition.Build(context);
                    if (Matches(effect, old, actualAllocation, readMemory, true))
                    {
                        plan = WithActualData(old, actualAllocation);
                        allocation = candidate;
                        legacy = true;
                        reason = "Recognized the earlier rotating-slot actor collector. Its original layout must be migrated before using the current actor values.";
                        return true;
                    }
                }
                reason = "The jump destination or patch padding does not exactly match a supported trainer template. No effect was adopted.";
                return false;
            }
            catch (Exception ex)
            {
                plan = null;
                allocation = IntPtr.Zero;
                legacy = false;
                reason = "Could not verify the installed effect: " + ex.Message;
                return false;
            }
        }

        private static IntPtr PatchSite(TableEffect effect, EffectDefinition definition, IReadOnlyDictionary<string, IntPtr> anchors)
        {
            int offset = effect == TableEffect.ActorCollector ? 0x1C : effect == TableEffect.WalkThroughWalls ? 0x17
                : effect == TableEffect.StageLoaderHook ? 4 : 0;
            return Add(anchors[definition.PrimarySignatureKey], offset);
        }

        private static Func<IntPtr, int, byte[]> OriginalInstructionReader(TableEffect effect,
            IReadOnlyDictionary<string, IntPtr> anchors, Func<IntPtr, int, byte[]> readMemory)
        {
            if (effect == TableEffect.Always100PercentCamo && !IsLegacyCamo(anchors))
            {
                IntPtr camo = anchors["aCamo"];
                byte[] originalCamo = X64CodeManager.ParseHex(EffectDefinitionManager.CamoOriginalInstructions);
                return (address, count) => address == camo && count == originalCamo.Length
                    ? (byte[])originalCamo.Clone() : readMemory(address, count);
            }
            if (effect != TableEffect.ForceAlertLevel && effect != TableEffect.StageLoaderHook) return readMemory;
            IntPtr site = effect == TableEffect.ForceAlertLevel ? anchors["aAlertCall"] : Add(anchors["aUiFrame"], 4);
            IntPtr target = effect == TableEffect.ForceAlertLevel ? anchors["aAlertFn"] : anchors["aUiInner"];
            var original = new X64CodeManager(site);
            original.Call(target);
            byte[] call = original.ToArray();
            // BuildAlert/BuildStageLoader need the stolen CALL, not the currently installed
            // JMP. Its exact rel32 is uniquely determined by the verified source and target.
            return (address, count) => address == site && count == call.Length ? (byte[])call.Clone() : readMemory(address, count);
        }

        private static bool Matches(TableEffect effect, EffectPlan expected, byte[] actual,
            Func<IntPtr, int, byte[]> readMemory, bool legacyLayout)
        {
            if (expected.AllocationBytes == null || expected.AllocationBytes.Length != actual.Length) return false;
            bool[] mutable = MutableBytes(effect, actual.Length, legacyLayout);
            for (int index = 0; index < actual.Length; index++)
                if (!mutable[index] && actual[index] != expected.AllocationBytes[index]) return false;
            foreach (CodePatch patch in expected.Patches)
                if (!ReadExact(readMemory, patch.Address, patch.Replacement.Length).SequenceEqual(patch.Replacement)) return false;
            return true;
        }

        private static bool[] MutableBytes(TableEffect effect, int size, bool legacyLayout)
        {
            var mutable = new bool[size];
            Action<int, int> mark = (offset, count) =>
            {
                if (offset < EffectDefinitionManager.DataOffset || count <= 0 || offset > size - count)
                    throw new InvalidOperationException("An invalid mutable range would overlap immutable hook code.");
                for (int index = offset; index < offset + count; index++) mutable[index] = true;
            };
            const int data = EffectDefinitionManager.DataOffset;
            switch (effect)
            {
                case TableEffect.InfiniteLife:
                case TableEffect.InfiniteStamina:
                case TableEffect.InfiniteBattery:
                    break;
                case TableEffect.Always100PercentCamo:
                    if (!legacyLayout) mark(data, 4); // pending original CALLs; float and target stay immutable
                    break;
                case TableEffect.InfiniteAmmo:
                case TableEffect.AmmoPointer:
                case TableEffect.InventoryPointer:
                    mark(data, 8);
                    break;
                case TableEffect.ForceAlertLevel:
                case TableEffect.StageLoaderHook:
                    // Both have a user setting and an active-call counter. The function
                    // pointers/string data that follow them remain mandatory identity bytes.
                    mark(data, 8);
                    break;
                case TableEffect.EnemyControl:
                    mark(data, 8); mark(data + 8, 1);
                    break;
                case TableEffect.ActorCollector:
                    if (legacyLayout) mark(data, 72); // pSlot[8] followed by pIdx
                    else
                    {
                        mark(data, 8);       // latched pSlot
                        mark(data + 8, 4);   // heartbeat miss count (four padding bytes stay immutable)
                        mark(data + 16, 64); // separate actor ring
                        mark(data + 80, 8);  // ring index
                    }
                    break;
                case TableEffect.WalkThroughWalls:
                    mark(data, 20); // bWTW, lastDX, lastDZ, wtwCnt and editable fEps
                    break;
                default:
                    throw new InvalidOperationException("No reviewed recovery data layout exists for " + effect + ".");
            }
            return mutable;
        }

        private static EffectPlan WithActualData(EffectPlan expected, byte[] actual) => new EffectPlan(
            (byte[])actual.Clone(), expected.Patches,
            new Dictionary<string, int>(expected.SymbolOffsets, StringComparer.Ordinal));

        private static bool IsOwnedExecutableAllocation(IntPtr handle, IntPtr allocation, int count)
        {
            if (handle == IntPtr.Zero || allocation.ToInt64() <= 0 || count <= 0) return false;
            UIntPtr informationSize = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
            long cursor = allocation.ToInt64();
            long end = checked(cursor + count);
            while (cursor < end)
            {
                if (MemoryManager.NativeMethods.VirtualQueryEx(handle, new IntPtr(cursor), out var page, informationSize) == UIntPtr.Zero ||
                    page.State != 0x1000 || page.Type != 0x20000 || page.AllocationBase != allocation ||
                    (page.Protect & 0x100) != 0 || (page.Protect & 0x40) == 0) return false;
                long next = checked(page.BaseAddress.ToInt64() + (long)page.RegionSize.ToUInt64());
                if (next <= cursor) return false;
                cursor = next;
            }
            return true;
        }

        private static byte[] ReadExact(Func<IntPtr, int, byte[]> readMemory, IntPtr address, int count)
        {
            if (address.ToInt64() <= 0 || count <= 0) throw new InvalidOperationException("Invalid recovery read range.");
            byte[] bytes = readMemory(address, count);
            if (bytes == null || bytes.Length != count) throw new InvalidOperationException("A required code/data range could not be read completely.");
            return bytes;
        }

        private static IntPtr Add(IntPtr address, long offset) => new IntPtr(checked(address.ToInt64() + offset));

        /// <summary>Exact original trainer camo cave, retained only to inspect and remove existing hooks.</summary>
        private static EffectPlan BuildLegacyCamo(EffectBuildContext context)
        {
            IntPtr site = context.Anchors["aCamoLegacy"];
            byte[] original = X64CodeManager.ParseHex("0F BF 87 38 01 00 00");
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("66 C7 87 38 01 00 00 64 00 0F BF 87 38 01 00 00");
            code.Jump(Add(site, original.Length));
            byte[] allocation = new byte[EffectDefinitionManager.CaveAllocationSize];
            byte[] instructions = code.ToArray();
            Buffer.BlockCopy(instructions, 0, allocation, 0, instructions.Length);
            var branch = new X64CodeManager(site);
            branch.Jump(context.Allocation);
            branch.EmitHex("90 90");
            return new EffectPlan(allocation, new[] { new CodePatch(site, original, branch.ToArray()) });
        }

        /// <summary>The exact initial C# collector, before the latched-player/ring split was introduced.</summary>
        private static EffectPlan BuildLegacyActorCollector(EffectBuildContext context)
        {
            const int data = EffectDefinitionManager.DataOffset;
            IntPtr slots = Add(context.Allocation, data);
            IntPtr index = Add(context.Allocation, data + 64);
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("9C 50 51");
            code.Rip("48 8D 05", slots);
            code.EmitHex("B9 08 00 00 00");
            code.Label("scan");
            code.EmitHex("48 39 18");
            code.ConditionalJump(0x84, "done");
            code.EmitHex("48 83 C0 08 83 E9 01");
            code.ConditionalJump(0x85, "scan");
            code.Rip("48 8B 0D", index);
            code.EmitHex("48 83 E1 07");
            code.Rip("48 8D 05", slots);
            code.EmitHex("48 89 1C C8 48 83 C1 01 48 83 E1 07");
            code.Rip("48 89 0D", index);
            code.Label("done");
            byte[] original = X64CodeManager.ParseHex("F3 0F 11 63 14");
            code.EmitHex("59 58 9D");
            code.Emit(original);
            IntPtr site = Add(context.Anchors["aPos"], 0x1C);
            code.Jump(Add(site, original.Length));
            byte[] allocation = new byte[EffectDefinitionManager.CaveAllocationSize];
            byte[] instructions = code.ToArray();
            if (instructions.Length > data) throw new InvalidOperationException("Legacy collector code overlaps its data.");
            Buffer.BlockCopy(instructions, 0, allocation, 0, instructions.Length);
            var branch = new X64CodeManager(site);
            branch.Jump(context.Allocation);
            return new EffectPlan(allocation, new[] { new CodePatch(site, original, branch.ToArray()) },
                new Dictionary<string, int>(StringComparer.Ordinal) { { "pSlot", data }, { "pIdx", data + 64 } });
        }
    }
}
