using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MGS4_Master_Collection_Trainer
{
    internal sealed class VitalsSnapshot
    {
        internal VitalsSnapshot(string gameIdentity, IntPtr playerAddress, bool isReady, string error,
            IReadOnlyDictionary<GameValueId, ulong> values = null)
        {
            GameIdentity = gameIdentity; PlayerAddress = playerAddress; IsReady = isReady;
            Error = error ?? string.Empty;
            Values = new ReadOnlyDictionary<GameValueId, ulong>(values == null
                ? new Dictionary<GameValueId, ulong>() : values.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        internal string GameIdentity { get; }
        internal IntPtr PlayerAddress { get; }
        internal bool IsReady { get; }
        internal string Error { get; }
        internal IReadOnlyDictionary<GameValueId, ulong> Values { get; }
    }

    /// <summary>Reads live vitals and commits one explicitly edited field to the same player capture.</summary>
    internal static class VitalsManager
    {
        internal static readonly IReadOnlyList<GameValueId> Fields = Array.AsReadOnly(new[]
        {
            GameValueId.Health, GameValueId.HealthMax, GameValueId.Stamina,
            GameValueId.StaminaMax, GameValueId.DrebinPoints, GameValueId.Battery, GameValueId.BatteryMax
        });

        internal static VitalsSnapshot Read(MemoryTransactionManager memory)
        {
            string identity = memory.SessionIdentity;
            IntPtr player = IntPtr.Zero;
            try
            {
                player = memory.Player();
                var values = new Dictionary<GameValueId, ulong>();
                foreach (GameValueId id in Fields)
                {
                    GameValueDefinition field = Definition(id);
                    IntPtr address = MemoryTransactionManager.Add(player, field.Offset);
                    values.Add(id, field.ByteCount == 2 ? memory.U16(address) : memory.U32(address));
                }
                if (memory.SessionIdentity != identity || memory.Player() != player)
                    return new VitalsSnapshot(identity, IntPtr.Zero, false,
                        "The player capture changed while refreshing vitals. Waiting for current values.");
                return new VitalsSnapshot(identity, player, true, string.Empty, values);
            }
            catch (Exception error)
            {
                return new VitalsSnapshot(identity, player, false, error.Message);
            }
        }

        internal static void Set(MemoryTransactionManager memory, VitalsSnapshot expected, GameValueId id, ulong value)
        {
            GameValueDefinition field = Definition(id);
            if (value > (field.ByteCount == 2 ? ushort.MaxValue : (ulong)uint.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(value), field.Name + " exceeds its supported whole-number range.");
            ValidateCapture(memory, expected);

            // Live values can move while the user types. Read the before-image only after pausing
            // and checking the capture again, then change exactly this field at its native width.
            using (memory.Pause())
            {
                ValidateCapture(memory, expected);
                IntPtr address = MemoryTransactionManager.Add(expected.PlayerAddress, field.Offset);
                byte[] before = memory.Read(address, field.ByteCount);
                byte[] after = field.ByteCount == 2 ? BitConverter.GetBytes((ushort)value) : BitConverter.GetBytes((uint)value);
                if (before.SequenceEqual(after)) return;

                memory.CommitWhilePaused(new[] { new MemoryValueEdit(address, before, after) });
            }
        }

        private static void ValidateCapture(MemoryTransactionManager memory, VitalsSnapshot expected)
        {
            if (expected == null || !expected.IsReady || expected.GameIdentity != memory.SessionIdentity)
                throw new InvalidOperationException("The game session changed. Wait for current vitals before applying an edit.");
            if (memory.Player() != expected.PlayerAddress)
                throw new InvalidOperationException("The player capture changed. Wait for current vitals before applying an edit.");
        }

        private static GameValueDefinition Definition(GameValueId id)
        {
            if (!Fields.Contains(id)) throw new InvalidOperationException("This value is not an editable vital.");
            GameValueDefinition field = GameValueDefinitionManager.Get(id);
            Constants.DataType expectedType = id == GameValueId.DrebinPoints ? Constants.DataType.UInt32 : Constants.DataType.UInt16;
            if (field.Symbol != "pPlayer" || field.SymbolOffset != 0 || !field.Dereference || field.Offset < 0 ||
                field.IsLocal || field.ReadOnly || field.Type != expectedType)
                throw new InvalidOperationException("The vital no longer matches its table definition.");
            return field;
        }

    }
}
