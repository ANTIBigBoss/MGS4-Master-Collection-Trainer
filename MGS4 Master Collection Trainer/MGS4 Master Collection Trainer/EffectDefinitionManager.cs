using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MGS4_Master_Collection_Trainer
{
    public enum TableEffect
    {
        PlayerPointer,
        NoStress,
        InfiniteLife,
        InfiniteBattery,
        InfiniteAmmo,
        NeverReload,
        InfiniteSuppressor,
        Always100PercentCamo,
        NoAlerts,
        ForceAlertLevel,
        EnemyControl,
        InventoryPointer,
        ActorCollector,
        WalkThroughWalls,
        AmmoPointer,
        InfiniteStamina,
        DisableScreenFilter,
        DisableMotionBlur,
        DisableResolutionScaling,
        StageLoaderHook
    }

    internal sealed class SignatureDefinition
    {
        internal SignatureDefinition(string key, string displayName, string pattern)
        {
            Key = key;
            DisplayName = displayName;
            string[] tokens = pattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Pattern = new byte[tokens.Length];
            char[] mask = new char[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                bool wildcard = tokens[i] == "?" || tokens[i] == "??";
                Pattern[i] = wildcard ? (byte)0 : Convert.ToByte(tokens[i], 16);
                mask[i] = wildcard ? '?' : 'x';
            }
            Mask = new string(mask);
        }

        internal string Key { get; }
        internal string DisplayName { get; }
        internal byte[] Pattern { get; }
        internal string Mask { get; }
    }

    internal sealed class EffectDefinition
    {
        internal EffectDefinition(TableEffect effect, string name, SignatureDefinition[] signatures,
            string primarySignatureKey, int allocationSize, Func<EffectBuildContext, EffectPlan> build,
            TableEffect[] dependencies = null, TableEffect[] conflicts = null, string activeCallCounterSymbol = null)
        {
            Effect = effect;
            Name = name;
            Signatures = signatures;
            PrimarySignatureKey = primarySignatureKey;
            AllocationSize = allocationSize;
            Build = build;
            Dependencies = dependencies ?? Array.Empty<TableEffect>();
            Conflicts = conflicts ?? Array.Empty<TableEffect>();
            ActiveCallCounterSymbol = activeCallCounterSymbol;
        }

        internal TableEffect Effect { get; }
        internal string Name { get; }
        internal SignatureDefinition[] Signatures { get; }
        internal string PrimarySignatureKey { get; }
        internal int AllocationSize { get; }
        internal TableEffect[] Dependencies { get; }
        internal TableEffect[] Conflicts { get; }
        internal string ActiveCallCounterSymbol { get; }
        internal Func<EffectBuildContext, EffectPlan> Build { get; }
    }

    internal sealed class EffectBuildContext
    {
        internal EffectBuildContext(IntPtr allocation, IReadOnlyDictionary<string, IntPtr> anchors,
            Func<string, IntPtr> resolveSymbol, Func<IntPtr, int, byte[]> readMemory)
        {
            Allocation = allocation;
            Anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
            ResolveSymbol = resolveSymbol ?? throw new ArgumentNullException(nameof(resolveSymbol));
            ReadMemory = readMemory ?? throw new ArgumentNullException(nameof(readMemory));
        }

        internal IntPtr Allocation { get; }
        internal IReadOnlyDictionary<string, IntPtr> Anchors { get; }
        internal Func<string, IntPtr> ResolveSymbol { get; }
        internal Func<IntPtr, int, byte[]> ReadMemory { get; }
    }

    internal sealed class EffectPlan
    {
        internal EffectPlan(byte[] allocationBytes, CodePatch[] patches, Dictionary<string, int> symbolOffsets = null)
        {
            AllocationBytes = allocationBytes;
            Patches = patches ?? throw new ArgumentNullException(nameof(patches));
            SymbolOffsets = symbolOffsets ?? new Dictionary<string, int>(StringComparer.Ordinal);
        }

        internal byte[] AllocationBytes { get; }
        internal CodePatch[] Patches { get; }
        internal Dictionary<string, int> SymbolOffsets { get; }
    }

    internal sealed class CodePatch
    {
        internal CodePatch(IntPtr address, byte[] original, byte[] replacement)
        {
            if (address == IntPtr.Zero) throw new ArgumentException("A patch address is required.", nameof(address));
            if (original == null || replacement == null || original.Length == 0 || original.Length != replacement.Length)
                throw new ArgumentException("Original and replacement bytes must have the same nonzero length.");
            Address = address;
            Original = original;
            Replacement = replacement;
        }

        internal IntPtr Address { get; }
        internal byte[] Original { get; }
        internal byte[] Replacement { get; }
    }

    /// <summary>
    /// Fixed translations of the supplied CE table. CE numeric literals were hexadecimal;
    /// no runtime assembler or Cheat Engine installation is needed.
    /// </summary>
    internal static class EffectDefinitionManager
    {
        internal const int DataOffset = 512;
        internal const int CaveAllocationSize = 1024;
        internal const string CamoOriginalInstructions = "F3 0F 2C F8 E8 D3 0B 85 00";

        internal static readonly SignatureDefinition[] Signatures =
        {
            new SignatureDefinition("aPlayer", "[BASE] Player Pointer", "48 63 82 60 01 00 00 49"),
            new SignatureDefinition("aLife", "Infinite Life", "66 41 89 89 48 0B 00 00 F6"),
            new SignatureDefinition("aStam", "Infinite Stamina", "66 41 89 89 4C 0B 00 00 48"),
            new SignatureDefinition("aStress", "No Stress", "66 41 89 81 50 0B 00 00 48"),
            new SignatureDefinition("aBatt", "Infinite Battery", "66 89 82 52 0B 00 00 48"),
            new SignatureDefinition("aAmmo", "Infinite Ammo", "0F BF 78 10 48 8B 8B 00"),
            new SignatureDefinition("aReload", "Never Reload", "66 89 4B 30 45 85 D2 0F"),
            new SignatureDefinition("aSupp", "Infinite Suppressor", "66 FF 48 30 48 8D 14 49"),
            new SignatureDefinition("aCamo", "Always 100% Camo", "F3 0F 2C F8 E8 D3"),
            new SignatureDefinition("aCamoLegacy", "Earlier camo field hook (recovery only)", "0F BF 87 38 01 00 00 83"),
            new SignatureDefinition("aNoAlert", "No Alerts", "48 89 4C 24 08 53 48 81 EC 90 00"),
            new SignatureDefinition("aAlertFn", "Force Alert Level (fn)", "48 89 4C 24 08 53 48 81 EC 90 00"),
            new SignatureDefinition("aAlertCall", "Force Alert Level (call)", "E8 ?? ?? ?? ?? 48 8B CD 89 05"),
            new SignatureDefinition("aEnemy", "Enemy Control", "8B 87 24 03 00 00 F7 D8 48 63 C8 48 C1 F9 3F 48 83 C1 01 74 04"),
            new SignatureDefinition("aInv", "Inventory Pointer", "66 83 78 14 00 7E 20 F6 83"),
            new SignatureDefinition("aPos", "Position/WTW anchor", "F3 0F 11 43 20 23 C8 F3 0F 11 4B 28 03 CA B8 00 00 00 40 D3 F8 89 03"),
            new SignatureDefinition("aNoFilter", "Disable screen filter", "40 53 48 83 EC 20 48 BA 00 00 00 00 ?? ?? ?? ?? B9 60 04 00 00 E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 ?? 4C 8D 0D ?? ?? ?? ?? 48 89 7C 24 30"),
            new SignatureDefinition("aNoBlur", "Disable motion blur", "40 53 48 83 EC 70 0F 29 74 24 60 BA FA 00 00 00 B9 12 BF 32 00 0F 29 7C 24 50 E8 ?? ?? ?? ?? F3 0F 10 3D ?? ?? ?? ?? BA 0A 00 00 00 B9 73 97 5A 00"),
            new SignatureDefinition("aDynRes", "Dynamic resolution state", "48 81 EC 28 02 00 00 45 8B C8 44 8B C2 8B D1 48 8D 4C 24 20 E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? BA 03 00 00 00"),
            new SignatureDefinition("aUiFrame", "Stage loader frame", "48 83 EC 28 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B7 90 BA 00 00 00 0F B7 88 B8 00 00 00 E8 ?? ?? ?? ?? 48 83 C4 28 E9 ?? ?? ?? ??"),
            new SignatureDefinition("aBlocked", "Stage loader blocked check", "8B 05 1E B9 D1 01 0B 05"),
            new SignatureDefinition("aSetName", "Stage loader name setter", "48 83 EC 28 4C 8B C1 48 C7"),
            new SignatureDefinition("aFinalize", "Stage loader finalize", "48 8B 15 91 C1 BC 01 4C"),
            new SignatureDefinition("aUiInner", "Stage loader original frame call", "48 83 EC 28 80 3D 98 52"),
            new SignatureDefinition("aStageFastLoad", "Stage list / selection discovery", GameActionManager.StageFastLoadPattern),
            new SignatureDefinition("aStageHandler", "Stage flags discovery", GameActionManager.StageHandlerPattern),
            new SignatureDefinition("aWeaponTable", "Unlock all weapons table", GameActionManager.WeaponsPattern),
            new SignatureDefinition("aItemTable", "Unlock all items table", GameActionManager.ItemsPattern)
        };

        internal static readonly IReadOnlyDictionary<TableEffect, EffectDefinition> All = CreateDefinitions();

        private static IReadOnlyDictionary<TableEffect, EffectDefinition> CreateDefinitions()
        {
            var result = new Dictionary<TableEffect, EffectDefinition>();
            Add(result, TableEffect.InfiniteLife, "Infinite Life", "aLife", BuildLife);
            Add(result, TableEffect.InfiniteStamina, "Infinite Stamina", "aStam", BuildStamina);
            Add(result, TableEffect.InfiniteBattery, "Infinite Battery", "aBatt", BuildBattery);
            Add(result, TableEffect.InfiniteAmmo, "Infinite Ammo", "aAmmo", BuildAmmo,
                conflicts: new[] { TableEffect.AmmoPointer });
            Add(result, TableEffect.AmmoPointer, "Ammo Pointer", "aAmmo", BuildAmmoPointer,
                conflicts: new[] { TableEffect.InfiniteAmmo });
            Add(result, TableEffect.NeverReload, "Never Reload", "aReload", c => Direct(
                Patch(c.Anchors["aReload"], "66 89 4B 30", "90 90 90 90")), allocationSize: 0);
            Add(result, TableEffect.InfiniteSuppressor, "Infinite Suppressor", "aSupp", c => Direct(
                Patch(c.Anchors["aSupp"], "66 FF 48 30", "90 90 90 90"),
                Patch(At(c.Anchors["aSupp"], 8), "66 FF 8C D7 A0 00 00 00", "90 90 90 90 90 90 90 90")), allocationSize: 0);
            Add(result, TableEffect.Always100PercentCamo, "Always 100% Camo", "aCamo", BuildCamo,
                activeCallCounterSymbol: "camoActiveCalls");
            Add(result, TableEffect.NoAlerts, "No Alerts", "aNoAlert", c => Direct(
                Patch(c.Anchors["aNoAlert"], "48 89 4C 24 08", "33 C0 C3 90 90")),
                allocationSize: 0, conflicts: new[] { TableEffect.ForceAlertLevel });
            Add(result, TableEffect.ForceAlertLevel, "Force Alert Level", "aAlertCall", BuildAlert,
                additionalSignature: "aAlertFn", conflicts: new[] { TableEffect.NoAlerts }, activeCallCounterSymbol: "alertActiveCalls");
            Add(result, TableEffect.EnemyControl, "Enemy Control", "aEnemy", BuildEnemy);
            Add(result, TableEffect.InventoryPointer, "Inventory Pointer", "aInv", BuildInventory);
            Add(result, TableEffect.ActorCollector, "Actor Collector", "aPos", BuildActorCollector);
            Add(result, TableEffect.WalkThroughWalls, "Walk Through Walls", "aPos", BuildWalkThroughWalls,
                dependencies: new[] { TableEffect.ActorCollector });
            Add(result, TableEffect.DisableScreenFilter, "Disable screen filter", "aNoFilter", c => Direct(
                Patch(c.Anchors["aNoFilter"], "40 53 48", "33 C0 C3")), allocationSize: 0);
            Add(result, TableEffect.DisableMotionBlur, "Disable motion blur", "aNoBlur", c => Direct(
                Patch(c.Anchors["aNoBlur"], "40 53 48", "33 C0 C3")), allocationSize: 0);
            string[] stageSignatures = { "aUiFrame", "aBlocked", "aSetName", "aFinalize", "aUiInner" };
            result.Add(TableEffect.StageLoaderHook, new EffectDefinition(TableEffect.StageLoaderHook, "Stage loader hook",
                stageSignatures.Select(key => Signatures.Single(s => s.Key == key)).ToArray(), "aUiFrame",
                CaveAllocationSize, BuildStageLoader, activeCallCounterSymbol: "stageActiveCalls"));
            return new ReadOnlyDictionary<TableEffect, EffectDefinition>(result);
        }

        private static void Add(Dictionary<TableEffect, EffectDefinition> definitions, TableEffect effect, string name,
            string signatureKey, Func<EffectBuildContext, EffectPlan> build, int allocationSize = CaveAllocationSize,
            string additionalSignature = null, TableEffect[] dependencies = null, TableEffect[] conflicts = null,
            string activeCallCounterSymbol = null)
        {
            SignatureDefinition primary = Signatures.Single(s => s.Key == signatureKey);
            SignatureDefinition[] signatures = additionalSignature == null ? new[] { primary }
                : new[] { primary, Signatures.Single(s => s.Key == additionalSignature) };
            definitions.Add(effect, new EffectDefinition(effect, name, signatures, signatureKey,
                allocationSize, build, dependencies, conflicts, activeCallCounterSymbol));
        }

        private static EffectPlan BuildLife(EffectBuildContext context)
        {
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("51");                              // push rcx; the original store did not change it
            code.EmitHex("66 41 8B 89 4A 0B 00 00");          // mov cx,[r9+B4A]
            code.EmitHex("66 41 89 89 48 0B 00 00");          // mov [r9+B48],cx
            code.EmitHex("59");                              // pop rcx
            return Hook(context, "aLife", 0, "66 41 89 89 48 0B 00 00", code);
        }

        private static EffectPlan BuildBattery(EffectBuildContext context)
        {
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("50");                              // push rax
            code.EmitHex("66 8B 82 54 0B 00 00");             // mov ax,[rdx+B54]
            code.EmitHex("66 89 82 52 0B 00 00");             // mov [rdx+B52],ax
            code.EmitHex("58");                              // pop rax
            return Hook(context, "aBatt", 0, "66 89 82 52 0B 00 00", code);
        }

        private static EffectPlan BuildStamina(EffectBuildContext context)
        {
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("51 66 41 8B 89 4E 0B 00 00 66 41 89 89 4C 0B 00 00 59");
            return Hook(context, "aStam", 0, "66 41 89 89 4C 0B 00 00", code);
        }

        private static EffectPlan BuildAmmo(EffectBuildContext context)
        {
            var code = new X64CodeManager(context.Allocation);
            code.Rip("48 89 05", At(context.Allocation, DataOffset)); // mov [pAmmo],rax
            code.EmitHex("66 8B 48 12 66 89 48 10");          // mov cx,[rax+12]; mov [rax+10],cx
            code.EmitHex("0F BF 78 10 48 8B 8B 00 01 00 00"); // replay both stolen instructions
            return Hook(context, "aAmmo", 0, "0F BF 78 10 48 8B 8B 00 01 00 00", code,
                Symbols("pAmmo", DataOffset));
        }

        private static EffectPlan BuildAmmoPointer(EffectBuildContext context)
        {
            var code = new X64CodeManager(context.Allocation);
            code.Rip("48 89 05", At(context.Allocation, DataOffset)); // mov [pAmmo],rax
            // Capture the record without refilling ammo or changing the original result.
            code.EmitHex("0F BF 78 10 48 8B 8B 00 01 00 00");
            return Hook(context, "aAmmo", 0, "0F BF 78 10 48 8B 8B 00 01 00 00", code,
                Symbols("pAmmo", DataOffset));
        }

        private static EffectPlan BuildCamo(EffectBuildContext context)
        {
            IntPtr site = context.Anchors["aCamo"];
            byte[] original = context.ReadMemory(site, 9);
            // The six-byte locator alone cannot authorize stealing the entire CALL.
            // Check the complete supplied instruction pair, then derive its target from
            // the observed rel32 rather than a fixed module-relative function address.
            if (original == null || !original.SequenceEqual(X64CodeManager.ParseHex(CamoOriginalInstructions)))
                throw new InvalidOperationException("The camo conversion and complete original CALL do not match the supported instructions.");
            IntPtr function = At(site, checked(9L + BitConverter.ToInt32(original, 5)));
            IntPtr activeCalls = At(context.Allocation, DataOffset);
            IntPtr percentage = At(context.Allocation, DataOffset + 4);
            IntPtr functionPointer = At(context.Allocation, DataOffset + 8);
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("9C");
            code.Rip("F0 FF 05", activeCalls);
            code.EmitHex("9D");
            code.Rip("F3 0F 10 05", percentage);             // movss xmm0,[100.0f]
            code.EmitHex("F3 0F 2C F8");                     // cvttss2si edi,xmm0
            // Keep the original caller's RSP, shadow space and argument registers.
            // An indirect CALL remains valid even when a near cave and the original
            // target lie on opposite edges of the source instruction's rel32 range.
            code.Rip("FF 15", functionPointer);
            code.EmitHex("9C");
            code.Rip("F0 FF 0D", activeCalls);
            code.EmitHex("9D");
            EffectPlan plan = Hook(context, site, original, code, Symbols("camoActiveCalls", DataOffset));
            WriteData(plan, DataOffset + 4, BitConverter.GetBytes(100.0f));
            WriteData(plan, DataOffset + 8, BitConverter.GetBytes(function.ToInt64()));
            return plan;
        }

        private static EffectPlan BuildAlert(EffectBuildContext context)
        {
            IntPtr callAddress = context.Anchors["aAlertCall"];
            IntPtr functionAddress = context.Anchors["aAlertFn"];
            byte[] original = context.ReadMemory(callAddress, 5);
            if (original == null || original.Length != 5 || original[0] != 0xE8 ||
                At(callAddress, checked(5L + BitConverter.ToInt32(original, 1))) != functionAddress)
                throw new InvalidOperationException("The alert call does not target the uniquely scanned alert function.");

            IntPtr level = At(context.Allocation, DataOffset);
            IntPtr activeCalls = At(context.Allocation, DataOffset + 4);
            IntPtr functionPointer = At(context.Allocation, DataOffset + 8);
            var code = new X64CodeManager(context.Allocation);
            // Enter via JMP, keeping the original CALL site's RSP and its caller-provided
            // shadow space/stack arguments. Both pushes below are balanced before CALL.
            code.EmitHex("9C");                              // pushfq
            code.Rip("F0 FF 05", activeCalls);               // lock inc dword ptr [alertActiveCalls]
            code.EmitHex("9D");                              // popfq
            // The scanned function can be outside rel32 reach of the allocation even
            // though the allocation is near the original call site. A local indirect
            // CALL reaches any x64 address without changing a parameter register.
            code.Rip("FF 15", functionPointer);
            code.EmitHex("9C");                              // preserve flags returned by the real function
            code.Rip("F0 FF 0D", activeCalls);               // lock dec dword ptr [alertActiveCalls]
            code.Rip("83 3D", level, 0xFF);                  // cmp dword ptr [iAlertLevel],-1
            code.ConditionalJump(0x84, "unchanged");
            code.Rip("8B 05", level);                        // mov eax,[iAlertLevel]
            code.Label("unchanged");
            code.EmitHex("9D");
            var symbols = Symbols("iAlertLevel", DataOffset);
            symbols.Add("alertActiveCalls", DataOffset + 4);
            EffectPlan plan = Hook(context, callAddress, original, code, symbols);
            WriteData(plan, DataOffset, BitConverter.GetBytes(-1));
            WriteData(plan, DataOffset + 8, BitConverter.GetBytes(functionAddress.ToInt64()));
            return plan;
        }

        private static EffectPlan BuildEnemy(EffectBuildContext context)
        {
            IntPtr mode = At(context.Allocation, DataOffset + 8);
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("9C");                              // original MOV preserved flags
            code.Rip("48 89 3D", At(context.Allocation, DataOffset)); // mov [pEnemy],rdi
            code.Rip("80 3D", mode, 1);                      // cmp byte ptr [bEnemyMode],1
            code.ConditionalJump(0x85, "notKill");
            code.EmitHex("C7 87 14 03 00 00 00 00 00 00");   // mov dword ptr [rdi+314],0
            code.Label("notKill");
            code.Rip("80 3D", mode, 2);
            code.ConditionalJump(0x85, "done");
            code.EmitHex("C7 87 24 03 00 00 00 00 00 00");   // mov dword ptr [rdi+324],0
            code.Label("done");
            code.EmitHex("9D 8B 87 24 03 00 00");             // popfq; mov eax,[rdi+324]
            var symbols = Symbols("pEnemy", DataOffset);
            symbols.Add("bEnemyMode", DataOffset + 8);
            return Hook(context, "aEnemy", 0, "8B 87 24 03 00 00", code, symbols);
        }

        private static EffectPlan BuildInventory(EffectBuildContext context)
        {
            var code = new X64CodeManager(context.Allocation);
            code.Rip("48 89 05", At(context.Allocation, DataOffset)); // mov [pInv],rax
            code.EmitHex("66 83 78 14 00");                   // cmp word ptr [rax+14],0; following JLE needs these flags
            return Hook(context, "aInv", 0, "66 83 78 14 00", code, Symbols("pInv", DataOffset));
        }

        private static EffectPlan BuildActorCollector(EffectBuildContext context)
        {
            IntPtr player = At(context.Allocation, DataOffset);
            IntPtr misses = At(context.Allocation, DataOffset + 8);
            IntPtr slots = At(context.Allocation, DataOffset + 16);
            IntPtr index = At(context.Allocation, DataOffset + 80);
            var code = new X64CodeManager(context.Allocation);
            code.EmitHex("9C 50 51");                         // pushfq; push rax; push rcx
            code.Rip("48 8B 05", player);                   // mov rax,[pSlot] (latched player, separate from ring)
            code.EmitHex("48 85 C0");
            code.ConditionalJump(0x84, "latchNew");
            code.EmitHex("48 39 C3");                       // cmp rbx,rax
            code.ConditionalJump(0x85, "notPlayer");
            code.Rip("C7 05", misses, 0, 0, 0, 0);
            code.Jump("ringAdd");
            code.Label("notPlayer");
            code.Rip("8B 0D", misses);
            code.EmitHex("83 C1 01");
            code.Rip("89 0D", misses);
            code.EmitHex("81 F9 00 02 00 00");              // cmp ecx,200h (512 misses)
            code.ConditionalJump(0x82, "ringAdd");
            code.Label("latchNew");
            code.Rip("48 89 1D", player);
            code.Rip("C7 05", misses, 0, 0, 0, 0);
            code.Rip("48 8D 05", slots);
            code.EmitHex("B9 08 00 00 00");
            code.Label("clearRing");
            code.EmitHex("48 C7 00 00 00 00 00 48 83 C0 08 83 E9 01");
            code.ConditionalJump(0x85, "clearRing");
            code.Rip("48 C7 05", index, 0, 0, 0, 0);
            code.Label("ringAdd");
            code.Rip("48 8D 05", slots);                     // lea rax,[pRing]
            code.EmitHex("B9 08 00 00 00");                   // mov ecx,8
            code.Label("scan");
            code.EmitHex("48 39 18");                        // cmp [rax],rbx
            code.ConditionalJump(0x84, "done");
            code.EmitHex("48 83 C0 08 83 E9 01");             // add rax,8; sub ecx,1
            code.ConditionalJump(0x85, "scan");
            code.Rip("48 8B 0D", index);                     // mov rcx,[pIdx]
            code.EmitHex("48 83 E1 07");                     // bound index even if edited externally
            code.Rip("48 8D 05", slots);
            code.EmitHex("48 89 1C C8");                     // mov [rax+rcx*8],rbx
            code.EmitHex("48 83 C1 01 48 83 E1 07");          // add rcx,1; and rcx,7
            code.Rip("48 89 0D", index);                     // mov [pIdx],rcx
            code.Label("done");
            code.EmitHex("59 58 9D F3 0F 11 63 14");          // restore flags/registers; movss [rbx+14],xmm4
            var symbols = Symbols("pSlot", DataOffset);
            symbols.Add("hbCnt", DataOffset + 8);
            symbols.Add("pRing", DataOffset + 16);
            symbols.Add("pIdx", DataOffset + 80);
            return Hook(context, "aPos", 0x1C, "F3 0F 11 63 14", code, symbols);
        }

        private static EffectPlan BuildStageLoader(EffectBuildContext context)
        {
            IntPtr stageId = context.ResolveSymbol("stageIdSel");
            IntPtr stageFlags = context.ResolveSymbol("stageFlags");
            if (stageId == IntPtr.Zero || stageFlags == IntPtr.Zero)
                throw new InvalidOperationException("Build the stage list before enabling its loader hook (stageIdSel/stageFlags required).");
            IntPtr callAddress = At(context.Anchors["aUiFrame"], 4);
            IntPtr originalTarget = context.Anchors["aUiInner"];
            byte[] original = context.ReadMemory(callAddress, 5);
            if (original == null || original.Length != 5 || original[0] != 0xE8 ||
                At(callAddress, checked(5L + BitConverter.ToInt32(original, 1))) != originalTarget)
                throw new InvalidOperationException("The stage frame CALL does not target the uniquely scanned inner routine.");

            IntPtr fire = At(context.Allocation, DataOffset);
            IntPtr activeCalls = At(context.Allocation, DataOffset + 4);
            IntPtr name = At(context.Allocation, DataOffset + 8);
            IntPtr blockedFunction = At(context.Allocation, DataOffset + 16);
            IntPtr nameFunction = At(context.Allocation, DataOffset + 24);
            IntPtr finalizeFunction = At(context.Allocation, DataOffset + 32);
            IntPtr originalFunction = At(context.Allocation, DataOffset + 40);
            IntPtr stageIdPointer = At(context.Allocation, DataOffset + 48);
            IntPtr stageFlagsPointer = At(context.Allocation, DataOffset + 56);
            var code = new X64CodeManager(context.Allocation);
            // The replaced instruction was CALL, so RSP is already 16-byte aligned here.
            // Eight pushes (flags + seven volatile GPRs) and 80h scratch/shadow bytes retain
            // alignment for the injected calls. Preserve volatile XMM inputs for the original call.
            code.EmitHex("9C");
            code.Rip("F0 FF 05", activeCalls);
            code.Rip("83 3D", fire, 0);
            code.ConditionalJump(0x84, "stageSkip");
            code.EmitHex("50 51 52 41 50 41 51 41 52 41 53 48 81 EC 80 00 00 00");
            code.EmitHex("F3 0F 7F 44 24 20 F3 0F 7F 4C 24 30 F3 0F 7F 54 24 40");
            code.EmitHex("F3 0F 7F 5C 24 50 F3 0F 7F 64 24 60 F3 0F 7F 6C 24 70");
            code.Rip("FF 15", blockedFunction);
            code.EmitHex("85 C0");
            code.ConditionalJump(0x85, "stageBusy");
            code.Rip("48 8B 05", stageIdPointer);
            code.EmitHex("C7 40 04 01 00 00 00");
            code.Rip("48 8D 0D", name);
            code.Rip("FF 15", nameFunction);
            code.Rip("48 8B 05", stageFlagsPointer);
            code.EmitHex("83 08 11");                       // or dword ptr [stageFlags],11h
            code.Rip("FF 15", finalizeFunction);
            code.Rip("C7 05", fire, 0, 0, 0, 0);
            code.Label("stageBusy");
            code.EmitHex("F3 0F 6F 44 24 20 F3 0F 6F 4C 24 30 F3 0F 6F 54 24 40");
            code.EmitHex("F3 0F 6F 5C 24 50 F3 0F 6F 64 24 60 F3 0F 6F 6C 24 70");
            code.EmitHex("48 81 C4 80 00 00 00 41 5B 41 5A 41 59 41 58 5A 59 58");
            code.Label("stageSkip");
            code.EmitHex("9D");
            code.Rip("FF 15", originalFunction);
            // The counter covers all injected calls AND the original call, including the time
            // a thread's RIP is outside this allocation with a pending return address into it.
            code.EmitHex("9C");
            code.Rip("F0 FF 0D", activeCalls);
            code.EmitHex("9D");
            var symbols = Symbols("bStageFire", DataOffset);
            symbols.Add("stageActiveCalls", DataOffset + 4);
            EffectPlan plan = Hook(context, callAddress, original, code, symbols);
            WriteData(plan, DataOffset + 8, new byte[] { 0x73, 0x65, 0x6C, 0x65, 0x63, 0x74, 0 });
            WriteData(plan, DataOffset + 16, BitConverter.GetBytes(context.Anchors["aBlocked"].ToInt64()));
            WriteData(plan, DataOffset + 24, BitConverter.GetBytes(context.Anchors["aSetName"].ToInt64()));
            WriteData(plan, DataOffset + 32, BitConverter.GetBytes(context.Anchors["aFinalize"].ToInt64()));
            WriteData(plan, DataOffset + 40, BitConverter.GetBytes(originalTarget.ToInt64()));
            WriteData(plan, DataOffset + 48, BitConverter.GetBytes(stageId.ToInt64()));
            WriteData(plan, DataOffset + 56, BitConverter.GetBytes(stageFlags.ToInt64()));
            return plan;
        }

        private static EffectPlan BuildWalkThroughWalls(EffectBuildContext context)
        {
            IntPtr slots = context.ResolveSymbol("pSlot");
            if (slots == IntPtr.Zero) throw new InvalidOperationException("Walk Through Walls requires Actor Collector's pSlot.");
            IntPtr toggle = At(context.Allocation, DataOffset);
            IntPtr dx = At(context.Allocation, DataOffset + 4);
            IntPtr dz = At(context.Allocation, DataOffset + 8);
            IntPtr counter = At(context.Allocation, DataOffset + 12);
            IntPtr epsilon = At(context.Allocation, DataOffset + 16);
            var code = new X64CodeManager(context.Allocation);
            // This site is in a shared position writer, not a function boundary. Preserve
            // its live scratch state; only XMM2/XMM3's deliberate movement adjustment remains.
            code.EmitHex("9C 50 48 83 EC 20");                // pushfq; push rax; sub rsp,20h
            code.EmitHex("F3 0F 7F 04 24 F3 0F 7F 4C 24 10"); // movdqu [rsp],xmm0; movdqu [rsp+10],xmm1
            code.MovRaxImmediate(slots);                     // collector cave may be outside RIP reach of this cave
            code.EmitHex("48 3B 18");                        // cmp rbx,[rax]
            code.ConditionalJump(0x85, "original");
            code.EmitHex("F3 0F 10 C3 F3 0F 5C 43 10");       // movss xmm0,xmm3; subss xmm0,[rbx+10]
            code.EmitHex("F3 0F 10 CA F3 0F 5C 4B 18");       // movss xmm1,xmm2; subss xmm1,[rbx+18]
            code.EmitHex("F3 0F 59 C0 F3 0F 59 C9 F3 0F 58 C1"); // square deltas; addss xmm0,xmm1
            code.Rip("0F 2F 05", epsilon);                   // comiss xmm0,[fEps]
            code.ConditionalJump(0x86, "blocked");            // jbe
            code.EmitHex("F3 0F 10 C3 F3 0F 5C 43 10");
            code.Rip("F3 0F 11 05", dx);                     // movss [lastDX],xmm0
            code.EmitHex("F3 0F 10 CA F3 0F 5C 4B 18");
            code.Rip("F3 0F 11 0D", dz);                     // movss [lastDZ],xmm1
            code.Rip("C7 05", counter, 0, 0, 0, 0);          // mov dword ptr [wtwCnt],0
            code.Jump("original");
            code.Label("blocked");
            code.Rip("83 3D", toggle, 0);                    // cmp dword ptr [bWTW],0
            code.ConditionalJump(0x84, "original");
            code.Rip("8B 05", counter);                      // mov eax,[wtwCnt]
            code.EmitHex("83 F8 78");                        // cmp eax,78h (120 decimal)
            code.ConditionalJump(0x8D, "original");            // jge
            code.EmitHex("83 C0 01");                        // add eax,1
            code.Rip("89 05", counter);                      // mov [wtwCnt],eax
            code.Rip("F3 0F 58 1D", dx);                     // addss xmm3,[lastDX]
            code.Rip("F3 0F 58 15", dz);                     // addss xmm2,[lastDZ]
            code.Label("original");
            code.EmitHex("F3 0F 6F 04 24 F3 0F 6F 4C 24 10"); // restore complete XMM0/XMM1
            code.EmitHex("48 83 C4 20 58 9D");                // add rsp,20h; pop rax; popfq
            code.EmitHex("F3 0F 11 5B 10");                   // movss [rbx+10],xmm3; Y untouched
            var symbols = Symbols("bWTW", DataOffset);
            symbols.Add("lastDX", DataOffset + 4);
            symbols.Add("lastDZ", DataOffset + 8);
            symbols.Add("wtwCnt", DataOffset + 12);
            symbols.Add("fEps", DataOffset + 16);
            EffectPlan plan = Hook(context, "aPos", 0x17, "F3 0F 11 5B 10", code, symbols);
            WriteData(plan, DataOffset, BitConverter.GetBytes(1));
            WriteData(plan, DataOffset + 16, BitConverter.GetBytes(0.000001f));
            return plan;
        }

        private static EffectPlan Hook(EffectBuildContext context, string signature, int offset,
            string original, X64CodeManager code, Dictionary<string, int> symbols = null)
            => Hook(context, At(context.Anchors[signature], offset), X64CodeManager.ParseHex(original), code, symbols);

        private static EffectPlan Hook(EffectBuildContext context, IntPtr address, byte[] original,
            X64CodeManager code, Dictionary<string, int> symbols = null)
        {
            if (original.Length < 5) throw new InvalidOperationException("A relative jump needs five whole instruction bytes.");
            code.Jump(At(address, original.Length));
            if (code.Count > DataOffset) throw new InvalidOperationException("The emitted code overlaps its symbol storage.");
            byte[] allocation = new byte[CaveAllocationSize];
            byte[] instructions = code.ToArray();
            Buffer.BlockCopy(instructions, 0, allocation, 0, instructions.Length);
            var jump = new X64CodeManager(address);
            jump.Jump(context.Allocation);
            for (int i = 5; i < original.Length; i++) jump.Emit(0x90);
            return new EffectPlan(allocation, new[] { new CodePatch(address, original, jump.ToArray()) }, symbols);
        }

        private static EffectPlan Direct(params CodePatch[] patches) => new EffectPlan(null, patches);
        private static CodePatch Patch(IntPtr address, string original, string replacement)
            => new CodePatch(address, X64CodeManager.ParseHex(original), X64CodeManager.ParseHex(replacement));
        private static Dictionary<string, int> Symbols(string name, int offset)
            => new Dictionary<string, int>(StringComparer.Ordinal) { { name, offset } };
        private static IntPtr At(IntPtr address, long offset) => new IntPtr(checked(address.ToInt64() + offset));
        private static void WriteData(EffectPlan plan, int offset, byte[] bytes)
            => Buffer.BlockCopy(bytes, 0, plan.AllocationBytes, offset, bytes.Length);
    }
}
