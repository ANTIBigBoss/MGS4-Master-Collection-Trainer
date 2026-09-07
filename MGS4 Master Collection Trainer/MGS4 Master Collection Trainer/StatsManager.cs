using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace MGS4_Master_Collection_Trainer
{
    internal sealed class RunStatSnapshot
    {
        internal RunStatSnapshot(string gameIdentity, IntPtr playerAddress, bool isReady, string error,
            IReadOnlyDictionary<GameValueId, ulong> values = null, RankPreview rank = null)
        {
            GameIdentity = gameIdentity; PlayerAddress = playerAddress; IsReady = isReady;
            Error = error ?? string.Empty; Rank = rank;
            Values = new ReadOnlyDictionary<GameValueId, ulong>(values == null
                ? new Dictionary<GameValueId, ulong>() : values.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        internal string GameIdentity { get; }
        internal IntPtr PlayerAddress { get; }
        internal bool IsReady { get; }
        internal string Error { get; }
        internal IReadOnlyDictionary<GameValueId, ulong> Values { get; }
        internal RankPreview Rank { get; }
    }

    /// <summary>Reads a player capture and applies explicit stat edits as one verified batch.</summary>
    internal static class StatsManager
    {
        internal const ulong MaximumCombinedBoxDrumFrames = 2UL * uint.MaxValue;

        private static readonly GameValueId[] Fields = Enumerable.Range(1100, 24).Select(id => (GameValueId)id)
            .Concat(new[] { GameValueId.CurrentDifficulty, GameValueId.TimeCrouching, GameValueId.TimeProne,
                GameValueId.TimeAgainstWall, GameValueId.BoxDrumTimePart1, GameValueId.BoxDrumTimePart2 }).ToArray();

        internal static RunStatSnapshot Read(MemoryTransactionManager memory)
        {
            string identity = memory.SessionIdentity;
            IntPtr player = IntPtr.Zero;
            try
            {
                player = memory.Player();
                // Rank and its nearby counters share one read. Flashbacks are a separate distant field.
                byte[] block = memory.Read(player, RankManager.PlayerReadLength);
                var values = new Dictionary<GameValueId, ulong>();
                foreach (GameValueId id in Fields)
                {
                    GameValueDefinition field = Definition(id);
                    bool inBlock = field.Offset <= block.Length - field.ByteCount;
                    byte[] bytes = inBlock ? block : memory.Read(MemoryTransactionManager.Add(player, field.Offset), field.ByteCount);
                    int offset = inBlock ? checked((int)field.Offset) : 0;
                    values.Add(id, field.ByteCount == 2 ? BitConverter.ToUInt16(bytes, offset) : BitConverter.ToUInt32(bytes, offset));
                }
                if (memory.SessionIdentity != identity || memory.Player() != player)
                    return new RunStatSnapshot(identity, IntPtr.Zero, false, "The player capture changed while refreshing stats. Waiting for current values.");
                return new RunStatSnapshot(identity, player, true, string.Empty, values, RankManager.Evaluate(block));
            }
            catch (Exception error)
            {
                return new RunStatSnapshot(identity, player, false, error.Message);
            }
        }

        internal static void Apply(MemoryTransactionManager memory, RunStatSnapshot expected,
            IReadOnlyDictionary<GameValueId, ulong> changes, ulong? boxDrumFrames = null)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            var requested = changes.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (KeyValuePair<GameValueId, ulong> pair in requested)
            {
                GameValueDefinition field = Definition(pair.Key);
                if (pair.Value > (field.ByteCount == 2 ? ushort.MaxValue : (ulong)uint.MaxValue))
                    throw new ArgumentOutOfRangeException(nameof(changes), field.Name + " exceeds its supported whole-number range.");
                if (pair.Key == GameValueId.CurrentDifficulty && !field.Choices.Any(choice => choice.Value == pair.Value))
                    throw new ArgumentOutOfRangeException(nameof(changes), "Select one of the five supported difficulty levels.");
            }
            if (boxDrumFrames.HasValue && (boxDrumFrames > MaximumCombinedBoxDrumFrames ||
                requested.ContainsKey(GameValueId.BoxDrumTimePart1) || requested.ContainsKey(GameValueId.BoxDrumTimePart2)))
                throw new ArgumentOutOfRangeException(nameof(boxDrumFrames), "Enter one combined Box/Drum time within the supported range.");

            ValidateCapture(memory, expected);
            if (requested.Count == 0 && !boxDrumFrames.HasValue) return;

            // Take the before-images after pausing: timers can advance between a user's refresh and Apply.
            // All fields are validated before this pause, and Commit preflights every target before writing.
            using (memory.Pause())
            {
                ValidateCapture(memory, expected);
                if (boxDrumFrames.HasValue)
                {
                    ulong second = memory.U32(Address(expected.PlayerAddress, GameValueId.BoxDrumTimePart2));
                    ulong total = boxDrumFrames.Value;
                    ulong first;
                    if (total >= second && total - second <= uint.MaxValue) first = total - second;
                    else { first = Math.Min(total, uint.MaxValue); second = total - first; }
                    requested.Add(GameValueId.BoxDrumTimePart1, first);
                    requested.Add(GameValueId.BoxDrumTimePart2, second);
                }

                var edits = new List<MemoryValueEdit>();
                foreach (KeyValuePair<GameValueId, ulong> pair in requested.OrderBy(pair => Definition(pair.Key).Offset))
                {
                    GameValueDefinition field = Definition(pair.Key);
                    IntPtr address = Address(expected.PlayerAddress, pair.Key);
                    byte[] before = memory.Read(address, field.ByteCount);
                    byte[] after = field.ByteCount == 2 ? BitConverter.GetBytes((ushort)pair.Value) : BitConverter.GetBytes((uint)pair.Value);
                    if (!before.SequenceEqual(after)) edits.Add(new MemoryValueEdit(address, before, after));
                }

                memory.CommitWhilePaused(edits);
            }
        }

        internal static string FormatTime(ulong frames) => string.Format(CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00}", frames / 216000, frames / 3600 % 60, frames / 60 % 60);

        internal static bool TryParseCounter(string text, out ulong value) =>
            ulong.TryParse((text ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

        internal static bool TryParseTime(string text, out ulong frames)
        {
            frames = 0;
            string[] parts = (text ?? string.Empty).Trim().Split(':');
            if (parts.Length != 3 || !ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong hours) ||
                !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint minutes) || minutes > 59 ||
                !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint seconds) || seconds > 59) return false;
            try { frames = checked(hours * 216000 + minutes * 3600UL + seconds * 60UL); return true; }
            catch (OverflowException) { return false; }
        }

        private static void ValidateCapture(MemoryTransactionManager memory, RunStatSnapshot expected)
        {
            if (expected == null || !expected.IsReady || expected.GameIdentity != memory.SessionIdentity)
                throw new InvalidOperationException("The game session changed. Refresh the stats before applying edits.");
            if (memory.Player() != expected.PlayerAddress)
                throw new InvalidOperationException("The player capture changed. Refresh the stats before applying edits.");
        }

        private static IntPtr Address(IntPtr player, GameValueId id) => MemoryTransactionManager.Add(player, Definition(id).Offset);

        private static GameValueDefinition Definition(GameValueId id)
        {
            if (!Fields.Contains(id)) throw new InvalidOperationException("This value is not an editable run statistic.");
            GameValueDefinition field = GameValueDefinitionManager.Get(id);
            if (field.Symbol != "pPlayer" || field.SymbolOffset != 0 || !field.Dereference || field.Offset < 0 ||
                field.IsLocal || field.ReadOnly || (field.Type != Constants.DataType.UInt16 && field.Type != Constants.DataType.UInt32))
                throw new InvalidOperationException("The statistic no longer matches its table definition.");
            return field;
        }

    }
}
