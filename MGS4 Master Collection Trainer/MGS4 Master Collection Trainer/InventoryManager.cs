using System;
using System.Collections.Generic;
using System.Globalization;

namespace MGS4_Master_Collection_Trainer
{
    internal sealed class InventoryItemDefinition
    {
        internal InventoryItemDefinition(string name, GameValueId current, GameValueId? maximum = null)
        { Name = name; Current = current; Maximum = maximum; }
        internal string Name { get; }
        internal GameValueId Current { get; }
        internal GameValueId? Maximum { get; }
        internal int ByteCount => Maximum.HasValue ? 4 : 2;
    }

    internal sealed class InventoryItemValue
    {
        internal InventoryItemValue(ushort current, ushort maximum) { Current = current; Maximum = maximum; }
        internal ushort Current { get; }
        internal ushort Maximum { get; }
    }

    internal sealed class InventorySnapshot
    {
        internal InventorySnapshot(string gameIdentity, IntPtr inventoryAddress, bool isReady, string error,
            IReadOnlyDictionary<GameValueId, InventoryItemValue> items = null)
        {
            GameIdentity = gameIdentity; InventoryAddress = inventoryAddress; IsReady = isReady;
            Error = error ?? string.Empty;
            Items = items ?? new Dictionary<GameValueId, InventoryItemValue>();
        }
        internal string GameIdentity { get; }
        internal IntPtr InventoryAddress { get; }
        internal bool IsReady { get; }
        internal string Error { get; }
        internal IReadOnlyDictionary<GameValueId, InventoryItemValue> Items { get; }
    }

    /// <summary>Inventory operations use the table's offsets and verify each edit at its native width.</summary>
    internal static class InventoryManager
    {
        internal static readonly InventoryItemDefinition Box = new InventoryItemDefinition("C BOX", GameValueId.CardboardBoxACurrent);
        internal static readonly InventoryItemDefinition[] Items =
        {
            new InventoryItemDefinition("Ration", GameValueId.RationCurrent, GameValueId.RationMax),
            new InventoryItemDefinition("Noodles", GameValueId.EnergyBarCurrent, GameValueId.EnergyBarMax),
            new InventoryItemDefinition("Regain", GameValueId.EnergyDrinkCurrent, GameValueId.EnergyDrinkMax),
            new InventoryItemDefinition("Pentazemin", GameValueId.PentazeminCurrent, GameValueId.PentazeminMax),
            new InventoryItemDefinition("Compress", GameValueId.StupeCurrent, GameValueId.StupeMax),
            new InventoryItemDefinition("Cigs", GameValueId.CigarCurrent, GameValueId.CigarMax),
            new InventoryItemDefinition("Muna", GameValueId.MunaCurrent, GameValueId.MunaMax),
            new InventoryItemDefinition("Syringe", GameValueId.SyringeCurrent, GameValueId.SyringeMax),
            new InventoryItemDefinition("Bandana", GameValueId.BandanaCurrent, GameValueId.BandanaMax),
            new InventoryItemDefinition("Stealth", GameValueId.StealthSuitCurrent, GameValueId.StealthSuitMax),
            new InventoryItemDefinition("Camera", GameValueId.CameraCurrent, GameValueId.CameraMax),
            new InventoryItemDefinition("Radio", GameValueId.RadioCurrent, GameValueId.RadioMax),
            new InventoryItemDefinition("Scanning Plug", GameValueId.ScanningPlugCurrent, GameValueId.ScanningPlugMax),
            new InventoryItemDefinition("Drum Can", GameValueId.DrumCanCurrent, GameValueId.DrumCanMax),
            Box
        };

        internal static InventorySnapshot Read(MemoryTransactionManager memory)
        {
            string identity = memory.SessionIdentity;
            IntPtr inventory = IntPtr.Zero;
            try
            {
                inventory = Capture(memory);
                var values = new Dictionary<GameValueId, InventoryItemValue>();
                foreach (InventoryItemDefinition item in Items)
                {
                    byte[] bytes = memory.Read(Address(inventory, item), item.ByteCount);
                    values.Add(item.Current, new InventoryItemValue(BitConverter.ToUInt16(bytes, 0),
                        item.Maximum.HasValue ? BitConverter.ToUInt16(bytes, 2) : (ushort)0));
                }
                if (Capture(memory) != inventory)
                    return new InventorySnapshot(identity, IntPtr.Zero, false, "Inventory changed while refreshing. Waiting for a new capture.");
                return new InventorySnapshot(identity, inventory, true, string.Empty, values);
            }
            catch (Exception error)
            {
                return new InventorySnapshot(identity, inventory, false, error.Message);
            }
        }

        internal static bool TryParsePair(string currentText, string maximumText, out ushort current, out ushort maximum, out string error)
        {
            current = 0; maximum = 0;
            if (!TryParseQuantity(currentText, out current) || !TryParseQuantity(maximumText, out maximum))
            { error = "Enter whole numbers from 0 to 65534, or -1, for Current and Max."; return false; }
            error = string.Empty;
            return true;
        }

        internal static string FormatQuantity(ushort value) =>
            value == ushort.MaxValue ? "-1" : value.ToString(CultureInfo.InvariantCulture);

        internal static bool TryParseQuantity(string text, out ushort value)
        {
            text = (text ?? string.Empty).Trim();
            if (text == "-1") { value = ushort.MaxValue; return true; }
            // Continue accepting the raw representation, but always display its shorter alias.
            return ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        internal static bool TryParseBoxDurability(string text, out ushort value, out string error)
        {
            value = 0;
            if (!short.TryParse((text ?? string.Empty).Trim(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out short durability) || durability < -1 || durability > 25)
            {
                error = "Enter a whole number from -1 to 25 for C BOX durability.";
                return false;
            }
            value = unchecked((ushort)durability);
            error = string.Empty;
            return true;
        }

        internal static void SetBoxDurability(MemoryTransactionManager memory, InventorySnapshot expected, ushort value)
        {
            if (value != ushort.MaxValue && value > 25)
                throw new ArgumentOutOfRangeException(nameof(value), "C BOX durability must be from -1 to 25.");
            // C BOX has only one two-byte durability field. Never read or write a fabricated max beside it.
            Change(memory, expected, Box, before => new InventoryItemValue(value, 0));
        }

        internal static void Set(MemoryTransactionManager memory, InventorySnapshot expected, InventoryItemDefinition item,
            ushort current, ushort maximum)
        {
            if (!item.Maximum.HasValue) throw new InvalidOperationException("Use the durability editor for C BOX.");
            // These are independent UInt16 fields in the table. Write the user's exact
            // quantities, including a current count above the configured capacity.
            Change(memory, expected, item, before => new InventoryItemValue(current, maximum));
        }

        internal static void SetOwned(MemoryTransactionManager memory, InventorySnapshot expected, InventoryItemDefinition item, bool owned)
        {
            if (!item.Maximum.HasValue) throw new InvalidOperationException("Use the durability editor for C BOX.");
            Change(memory, expected, item, before =>
            {
                if (!owned) return new InventoryItemValue(ushort.MaxValue, before.Maximum);
                // -1 means absent, so it must not win the numeric comparison when granting an item again.
                ushort current = before.Current == ushort.MaxValue ? (ushort)1 : (ushort)Math.Max(1, (int)before.Current);
                ushort maximum = (ushort)Math.Max(current, before.Maximum == ushort.MaxValue ? 0 : (int)before.Maximum);
                return new InventoryItemValue(current, maximum);
            });
        }

        private static void Change(MemoryTransactionManager memory, InventorySnapshot expected, InventoryItemDefinition item,
            Func<InventoryItemValue, InventoryItemValue> edit)
        {
            ValidateCapture(memory, expected);
            IntPtr address = Address(expected.InventoryAddress, item);
            byte[] before = memory.Read(address, item.ByteCount);
            InventoryItemValue after = edit(new InventoryItemValue(BitConverter.ToUInt16(before, 0),
                item.Maximum.HasValue ? BitConverter.ToUInt16(before, 2) : (ushort)0));
            byte[] replacement = new byte[item.ByteCount];
            Buffer.BlockCopy(BitConverter.GetBytes(after.Current), 0, replacement, 0, 2);
            if (item.Maximum.HasValue) Buffer.BlockCopy(BitConverter.GetBytes(after.Maximum), 0, replacement, 2, 2);

            // Recheck the capture after suspending game threads, before Commit preflights or writes anything.
            Func<IDisposable> pause = memory.Pause;
            memory.Pause = () =>
            {
                IDisposable held = pause();
                try { ValidateCapture(memory, expected); return held; }
                catch { held.Dispose(); throw; }
            };
            try { memory.Commit(new[] { new MemoryValueEdit(address, before, replacement) }); }
            finally { memory.Pause = pause; }
        }

        private static void ValidateCapture(MemoryTransactionManager memory, InventorySnapshot expected)
        {
            if (expected == null || !expected.IsReady || expected.GameIdentity != memory.SessionIdentity)
                throw new InvalidOperationException("The game session changed. Wait for the inventory values to refresh before applying.");
            if (Capture(memory) != expected.InventoryAddress)
                throw new InvalidOperationException("The inventory capture changed. Wait for the current values to refresh before applying.");
        }

        private static IntPtr Capture(MemoryTransactionManager memory)
        {
            IntPtr inventory = memory.Pointer(memory.Symbol("pInv"));
            if (inventory.ToInt64() <= 0)
                throw new InvalidOperationException("Waiting for inventory capture. Load into a level and open the item menu.");
            return inventory;
        }

        private static IntPtr Address(IntPtr inventory, InventoryItemDefinition item)
        {
            GameValueDefinition current = GameValueDefinitionManager.Get(item.Current);
            if (current.Symbol != "pInv" || !current.Dereference || current.ByteCount != 2)
                throw new InvalidOperationException("The inventory field no longer matches its table definition.");
            if (item.Maximum.HasValue)
            {
                GameValueDefinition maximum = GameValueDefinitionManager.Get(item.Maximum.Value);
                if (maximum.Symbol != "pInv" || !maximum.Dereference || maximum.ByteCount != 2 || maximum.Offset != current.Offset + 2)
                    throw new InvalidOperationException("The inventory pair no longer matches its table definition.");
            }
            return MemoryTransactionManager.Add(inventory, current.Offset);
        }
    }
}
