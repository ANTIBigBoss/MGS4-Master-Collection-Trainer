using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Explicit C# actions translated from the supplied CE table's Lua scripts. No timers start here.</summary>
    public sealed class GameActionManager
    {
        internal const string StageFastLoadPattern = "40 57 41 56 41 57 48 83 EC 20 48 8B F9 48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? FF 15 ?? ?? ?? ?? 4C 8B 7F 18 48 8B C7 4C 8B 77 10 49 83 FF 10 72 ?? 48 8B 07 49 83 FE 06 75 ??";
        internal const string StageHandlerPattern = "48 83 EC 28 E8 ?? ?? ?? ?? 85 C0 0F 85 ?? ?? ?? ?? 48 89 5C 24 20 E8 ?? ?? ?? ?? 48 8B D8 80 38 00 0F 84 ?? ?? ?? ?? B1 6E E8 ?? ?? ?? ?? 48 85 C0 74 ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? E8 ?? ?? ?? ?? 85 C0 75 ??";
        internal const string WeaponsPattern = "48 89 5C 24 08 57 48 83 EC 30 4C 63 D1";
        internal const string ItemsPattern = "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 30 48 63 F9 8B";
        internal const string ResolutionPattern = "48 81 EC 28 02 00 00 45 8B C8 44 8B C2 8B D1 48 8D 4C 24 20 E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? BA 03 00 00 00";

        private readonly EffectManager manager;
        private int processId;
        private DateTime startTime;
        private StageCatalog stages;
        private IntPtr resolutionFlag;
        private bool resolutionDisabled;
        private int rankTarget;

        public GameActionManager(EffectManager manager)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public int RankTarget
        {
            get => rankTarget;
            set
            {
                if (value < 0 || value >= RankManager.Targets.Length)
                    throw new ArgumentOutOfRangeException(nameof(value), "Rank target must be between 0 and 8.");
                rankTarget = value;
            }
        }

        public bool ResolutionScalingDisabled => resolutionDisabled;

        /// <summary>Discards game-session addresses and periodic-write intent. The managed rank selection remains an input draft.</summary>
        public void ResetSession()
        {
            processId = 0;
            startTime = default(DateTime);
            stages = null;
            resolutionFlag = IntPtr.Zero;
            resolutionDisabled = false;
        }

        /// <summary>Looks up cached storage only. Safe to call from the owning manager's symbol resolver.</summary>
        public IntPtr ResolveSymbol(string symbol)
        {
            if (symbol == "stageIdSel") return stages?.StageIdAddress ?? IntPtr.Zero;
            if (symbol == "stageFlags") return stages?.StageFlagsAddress ?? IntPtr.Zero;
            if (symbol == "dynResEnabled") return resolutionFlag;
            return IntPtr.Zero;
        }

        public byte[] ReadLocalValue(string symbol)
        {
            if (symbol == "iRankTarget") return BitConverter.GetBytes(RankTarget);
            if (symbol != "mgsStage" && symbol != "mgsAct" && symbol != "mgsDiff" && symbol != "mgsRank")
                throw new ArgumentException("Unknown managed table value: " + symbol, nameof(symbol));
            LiveGameReadout live = ReadLive();
            string value = symbol == "mgsStage" ? live.Stage : symbol == "mgsAct" ? live.Act
                : symbol == "mgsDiff" ? live.Difficulty : live.Rank;
            int count = GameValueDefinitionManager.All.Values.First(definition => definition.Symbol == symbol).ByteCount;
            byte[] result = new byte[count];
            byte[] text = Encoding.ASCII.GetBytes(value ?? string.Empty);
            Buffer.BlockCopy(text, 0, result, 0, Math.Min(Math.Min(100, count - 1), text.Length));
            return result;
        }

        public void WriteLocalValue(string symbol, byte[] bytes)
        {
            if (symbol != "iRankTarget") throw new InvalidOperationException("Computed live readouts are read-only.");
            if (bytes == null || bytes.Length != 4) throw new ArgumentException("Rank target requires exactly four bytes.", nameof(bytes));
            RankTarget = BitConverter.ToInt32(bytes, 0);
        }

        public LiveGameReadout ReadLive()
        {
            return WithMemory(memory =>
            {
                byte[] player = ReadPlayer(memory);
                RankPreview rank = RankManager.Evaluate(player);
                string code = ReadCode(player.Skip(0x34).Take(8).ToArray(), false);
                uint progress = BitConverter.ToUInt32(player, 0x54);
                string act = progress <= 50 ? "Act 1" : progress <= 100 ? "Act 2" : progress <= 162 ? "Act 3"
                    : progress <= 222 ? "Act 4" : progress <= 261 ? "Act 5" : "Ending / results";
                return new LiveGameReadout(code, AreaLabel(code), act + "  (progress " + progress + ")",
                    rank.Difficulty, rank.PriorityEmblem ?? rank.GridFallback + " (grid)", progress);
            });
        }

        public RankPreview PreviewRank() => WithMemory(memory => RankManager.Evaluate(ReadPlayer(memory)));

        public GameActionResult ApplyRankTarget()
        {
            int target = RankTarget;
            return WithMemory(memory =>
            {
                IntPtr playerAddress = memory.Player();
                byte[] player = memory.Read(playerAddress, RankManager.PlayerReadLength);
                var edits = new List<MemoryValueEdit>();
                var descriptions = new List<string>();
                Action<int, ushort, string> set16 = (offset, value, fieldName) =>
                {
                    byte[] before = player.Skip(offset).Take(2).ToArray();
                    if (BitConverter.ToUInt16(before, 0) == value) return;
                    edits.Add(new MemoryValueEdit(Add(playerAddress, offset), before, BitConverter.GetBytes(value)));
                    descriptions.Add(fieldName + " -> " + value);
                };
                Action<double> capTime = hours =>
                {
                    uint limit = checked((uint)Math.Floor(hours * RankManager.FramesPerHour) - 60);
                    uint current = BitConverter.ToUInt32(player, 0x168);
                    if (current <= limit) return;
                    edits.Add(new MemoryValueEdit(Add(playerAddress, 0x168), BitConverter.GetBytes(current), BitConverter.GetBytes(limit)));
                    descriptions.Add("play time capped below " + hours.ToString("0.0", CultureInfo.InvariantCulture) + " h");
                };
                Action<ushort> minDifficulty = value =>
                {
                    if (BitConverter.ToUInt16(player, 6) < value) set16(6, value, "difficulty");
                };
                Action clean = () =>
                {
                    set16(0x178, 0, "kills"); set16(0x158, 0, "continues");
                    set16(0xAE0, 0, "recovery items"); set16(0x17A, 0, "special items");
                };
                switch (target)
                {
                    case 0: set16(6, 50, "difficulty"); set16(0x16E, 0, "alerts"); clean(); capTime(5); break;
                    case 1: minDifficulty(40); set16(0x16E, 0, "alerts"); clean(); capTime(5.5); break;
                    case 2: minDifficulty(35); set16(0x16E, 0, "alerts"); clean(); capTime(6); break;
                    case 3: minDifficulty(30); set16(0x16E, 0, "alerts"); clean(); capTime(6.5); break;
                    case 4: set16(0x16E, 0, "alerts"); set16(0x158, 0, "continues"); set16(0xAE0, 0, "recovery items"); capTime(5); break;
                    case 5: set16(0x158, 0, "continues"); set16(0xAE0, 0, "recovery items"); break;
                    case 6: capTime(5); break;
                    case 7: set16(0x16E, 0, "alerts"); break;
                    case 8: set16(0x178, 0, "kills"); break;
                }
                int changed = memory.Commit(edits);
                string name = RankManager.Targets[target];
                return new GameActionResult(changed, changed == 0 ? "No counter changes are needed for " + name + "."
                    : "Adjusted counters for " + name + ": " + string.Join("; ", descriptions) +
                        ". The game derives its emblem from the counters at results; earlier eligible emblems still take priority.");
            });
        }

        /// <summary>Experimental engine-layout discovery from the supplied research. Does not choose or load a stage.</summary>
        public StageCatalog BuildStageList(bool refresh = false)
        {
            return WithMemory(memory =>
            {
                if (stages != null && !refresh) return stages;
                IntPtr fastLoad = memory.FindUnique("Stage fast-load function", StageFastLoadPattern);
                IntPtr handler = memory.FindUnique("Stage handler", StageHandlerPattern);
                IntPtr mapHead = memory.Relative(Add(fastLoad, 0x88));
                IntPtr stageId = memory.Relative(Add(fastLoad, 0x124));
                // The Lua action resolves these two calls as a consistency check. The
                // native StageLoaderHook independently scans its own function entries.
                IntPtr blocked = memory.Relative(Add(handler, 0x05));
                IntPtr finalize = memory.Relative(Add(handler, 0xD3));
                IntPtr flags = Add(memory.Relative(Add(handler, 0xAF)), 1);
                memory.Read(blocked, 1); memory.Read(finalize, 1);
                memory.Read(stageId, 8); memory.Read(flags, 4);
                IntPtr head = memory.Pointer(mapHead);
                IntPtr root = head == IntPtr.Zero ? IntPtr.Zero : memory.Pointer(Add(head, 8));
                var entries = new List<StageEntry>();
                var visited = new HashSet<long>();
                var pending = new Stack<Tuple<IntPtr, bool>>();
                pending.Push(Tuple.Create(root, false));
                int nodes = 0;
                while (pending.Count != 0)
                {
                    Tuple<IntPtr, bool> next = pending.Pop();
                    IntPtr node = next.Item1;
                    if (node == IntPtr.Zero || node == head) continue;
                    if (!next.Item2)
                    {
                        if (!visited.Add(node.ToInt64())) continue; // cycles/shared nodes cannot recurse forever
                        if (visited.Count > 4096) throw new InvalidOperationException("The stage tree exceeded the source's 4096-node limit. No partial list was published.");
                        if (memory.Read(Add(node, 0x19), 1)[0] != 0) continue;
                        nodes++;
                        pending.Push(Tuple.Create(memory.Pointer(Add(node, 0x10)), false));
                        pending.Push(Tuple.Create(node, true));
                        pending.Push(Tuple.Create(memory.Pointer(node), false));
                        continue;
                    }
                    int id = memory.I32(Add(node, 0x40));
                    ulong size = memory.U64(Add(node, 0x30));
                    ulong capacity = memory.U64(Add(node, 0x38));
                    if (size == 0 || size >= 16) continue;
                    IntPtr source = capacity >= 16 ? memory.Pointer(Add(node, 0x20)) : Add(node, 0x20);
                    if (source == IntPtr.Zero) continue;
                    string code = ReadCode(memory.Read(source, (int)size), true);
                    if (code.Length != 0 && code != "select") entries.Add(new StageEntry(id, code, AreaLabel(code)));
                }
                entries.Sort((first, second) => StringComparer.Ordinal.Compare(first.Code, second.Code));
                stages = new StageCatalog(memory.ProcessId, memory.StartTime, entries, stageId, flags, head, root, nodes);
                return stages;
            });
        }

        public GameActionResult QueueStageLoad(int stageId)
        {
            return WithMemory(memory =>
            {
                if (stages == null || stages.Entries.Count == 0)
                    throw new InvalidOperationException("Build the stage list after loading a level, then select a stage first.");
                StageEntry stage = stages.Entries.FirstOrDefault(entry => entry.Id == stageId);
                if (stageId == 0 || stage == null) throw new InvalidOperationException("Choose a nonzero stage ID from the discovered stage list.");
                IntPtr fire = memory.Symbol("bStageFire");
                byte[] trigger = memory.Read(fire, 4);
                if (BitConverter.ToUInt32(trigger, 0) != 0)
                    throw new InvalidOperationException("A stage load is already queued. Let the game finish that request first.");
                var edits = new List<MemoryValueEdit>();
                AddEdit(memory, edits, stages.StageIdAddress, BitConverter.GetBytes(stageId));
                edits.Add(new MemoryValueEdit(fire, trigger, BitConverter.GetBytes(1)));
                int changed = memory.Commit(edits);
                return new GameActionResult(changed, "Queued " + stage.Code + ". The stage loader will perform the handoff on a game frame when the engine is ready.");
            });
        }

        public GameActionResult UnlockAllWeapons()
        {
            return WithMemory(memory =>
            {
                IntPtr function = memory.FindUnique("Weapon table function", WeaponsPattern);
                IntPtr table = memory.Relative(Add(function, 0x27 + 3));
                var edits = new List<MemoryValueEdit>();
                for (int index = 0; index < 68; index++)
                {
                    IntPtr entry = Add(table, index * 0x18);
                    ushort capacity = memory.U16(Add(entry, 0x12));
                    AddEdit(memory, edits, Add(entry, 0x10), BitConverter.GetBytes(capacity > 0 ? capacity : (ushort)1));
                }
                int changed = memory.Commit(edits);
                return new GameActionResult(changed, "Updated " + changed + " of 68 weapon entries at 0x" + table.ToInt64().ToString("X") + ".");
            });
        }

        public GameActionResult UnlockAllItems()
        {
            return WithMemory(memory =>
            {
                IntPtr function = memory.FindUnique("Item table function", ItemsPattern);
                IntPtr table = memory.Relative(Add(function, 0x29 + 3));
                var edits = new List<MemoryValueEdit>();
                for (int index = 0; index < 95; index++)
                    AddEdit(memory, edits, Add(table, index * 0x50 + 0x2A), BitConverter.GetBytes((ushort)1));
                int changed = memory.Commit(edits);
                return new GameActionResult(changed, "Updated " + changed + " of 95 item entries at 0x" + table.ToInt64().ToString("X") + ".");
            });
        }

        /// <summary>Discovers the dynamic-resolution flag for inspection without changing its value.</summary>
        public IntPtr PrepareResolutionScaling() => WithMemory(FindResolutionFlag);

        public GameActionResult SetResolutionScalingDisabled(bool disabled)
        {
            if (!disabled)
            {
                resolutionDisabled = false;
                return new GameActionResult(0, "Stopped repeated dynamic-resolution writes. The last written flag is unchanged.");
            }
            return WithMemory(memory =>
            {
                IntPtr flag = FindResolutionFlag(memory);
                var edits = new List<MemoryValueEdit>();
                AddEdit(memory, edits, flag, new byte[] { 0 });
                int changed = memory.Commit(edits);
                resolutionDisabled = true;
                return new GameActionResult(changed, "Dynamic resolution is disabled. Explicit timer pulses keep its enable flag at zero.");
            });
        }

        /// <summary>Explicitly enables dynamic resolution and stops repeated disable writes after the flag is verified.</summary>
        public GameActionResult RestoreResolutionScaling()
        {
            return WithMemory(memory =>
            {
                var edits = new List<MemoryValueEdit>();
                AddEdit(memory, edits, FindResolutionFlag(memory), new byte[] { 1 });
                int changed = memory.Commit(edits);
                // WithMemory holds the manager's session lock, so an already queued
                // pulse rechecks this intent after the restore completes. Keep the
                // prior intent if the checked write fails or is rolled back.
                resolutionDisabled = false;
                return new GameActionResult(changed, "Dynamic resolution is enabled. Repeated disable writes have stopped.");
            });
        }

        public GameActionResult PulseResolutionScaling()
        {
            if (!resolutionDisabled) return new GameActionResult(0, "Dynamic-resolution writes are inactive.");
            return WithMemory(memory =>
            {
                // WithMemory can discover a restarted process and clear periodic-write intent.
                if (!resolutionDisabled) return new GameActionResult(0, "The game session changed; dynamic-resolution writes stopped.");
                var edits = new List<MemoryValueEdit>();
                AddEdit(memory, edits, FindResolutionFlag(memory), new byte[] { 0 });
                return new GameActionResult(memory.Commit(edits), "Applied the dynamic-resolution flag request.");
            });
        }

        private IntPtr FindResolutionFlag(MemoryTransactionManager memory)
        {
            if (resolutionFlag != IntPtr.Zero) return resolutionFlag;
            IntPtr function = memory.FindUnique("Dynamic-resolution function", ResolutionPattern);
            IntPtr flag = Add(memory.Relative(Add(function, 0x1C)), 0x1F8);
            memory.Read(flag, 1);
            resolutionFlag = flag;
            return flag;
        }

        private T WithMemory<T>(Func<MemoryTransactionManager, T> action)
        {
            return manager.WithSession((process, handle) =>
            {
                var memory = MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress);
                Synchronize(memory);
                return action(memory);
            });
        }

        private void Synchronize(MemoryTransactionManager memory)
        {
            if (processId != 0 && (processId != memory.ProcessId || startTime != memory.StartTime)) ResetSession();
            processId = memory.ProcessId;
            startTime = memory.StartTime;
        }

        private static byte[] ReadPlayer(MemoryTransactionManager memory) => memory.Read(memory.Player(), RankManager.PlayerReadLength);
        private static IntPtr Add(IntPtr address, long offset) => MemoryTransactionManager.Add(address, offset);
        private static void AddEdit(MemoryTransactionManager memory, List<MemoryValueEdit> edits, IntPtr address, byte[] after)
        {
            byte[] before = memory.Read(address, after.Length);
            if (!before.SequenceEqual(after)) edits.Add(new MemoryValueEdit(address, before, after));
        }

        private static string ReadCode(byte[] bytes, bool underscore)
        {
            int count = 0;
            while (count < bytes.Length && (bytes[count] >= 'a' && bytes[count] <= 'z' || bytes[count] >= 'A' && bytes[count] <= 'Z'
                || bytes[count] >= '0' && bytes[count] <= '9' || underscore && bytes[count] == '_')) count++;
            return Encoding.ASCII.GetString(bytes, 0, count);
        }

        private static string AreaLabel(string code) => Areas.TryGetValue(code, out string area) ? code + "  -  " + area : code;

        private static readonly IReadOnlyDictionary<string, string> Areas = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "s00a00l", "Prologue Cemetery" }, { "s00a10l", "Ending Cemetery" },
                { "s01a00l", "Middle East Infiltration" }, { "s01a05l", "Middle East Infiltration" }, { "s01a10l", "Red Zone" },
                { "s01a20l", "Militia Safehouse" }, { "s01a30l", "Urban Ruins" }, { "s01a40l", "Advent Palace" },
                { "s01a50l", "Crescent Meridian" }, { "s01a55l", "Crescent Meridian" }, { "s01a57l", "Millennium Park" }, { "s01a60l", "Liquids Encampment" },
                { "s02a10l", "Cove Valley Village" }, { "s02a20l", "Power Station" }, { "s02a25l", "Power Station" },
                { "s02a30l", "Confinement Facility" }, { "s02a40l", "Vista Mansion" }, { "s02a50l", "Research Lab" },
                { "s02a60l", "Mountain Trail / Riverside" }, { "s02a70l", "Vamp Ambush" }, { "s02a73l", "Stryker Escape" },
                { "s02a75l", "Stryker Escape" }, { "s02a78l", "Stryker Escape" }, { "s02a80l", "High Woodlands Highway" },
                { "s02a85l", "Marketplace Entrance" }, { "s02a90l", "Marketplace" }, { "s02a95l", "Marketplace Plaza" },
                { "s03a00l", "Eastern Europe Station" }, { "s03a10l", "Midtown: Resistance Tail" }, { "s03a15l", "Midtown: Resistance Tail" },
                { "s03a16l", "Midtown: Canals" }, { "s03a20l", "Midtown: Plaza" }, { "s03a25l", "Midtown: North Sector" },
                { "s03a30l", "Church Courtyard" }, { "s03a35l", "Motorcycle Chase" }, { "s03a40l", "Motorcycle Chase" },
                { "s03a60l", "Motorcycle Chase" }, { "s03a50l", "Raging Raven Ambush" }, { "s03a65l", "Echos Beacon" },
                { "s03a70l", "Echos Beacon" }, { "s03a90l", "Volta River" }, { "s04a05l", "Metal Gear Solid Flashback" },
                { "s04a10l", "Snowfield / Heliport / Tank Hangar" }, { "s04a20l", "Nuclear Warhead Storage Building" },
                { "s04a30l", "Snowfield / Communications Tower" }, { "s04a40l", "Blast Furnace / Casting Facility" },
                { "s04a50l", "Underground Base" }, { "s04a60l", "Underground Supply Tunnel" }, { "s04a65l", "REX Escape" },
                { "s04a68l", "Port Area" }, { "s04a70l", "Port Area: REX vs. RAY" }, { "s04a75l", "Outer Haven Arrival" },
                { "s05a10l", "Ship Bow" }, { "s05a20l", "Command Center / Missile Hangar" }, { "s05a30l", "Microwave Corridor" },
                { "s05a40l", "GW" }, { "s05a45l", "Liquid Ocelot: Prelude" }, { "s05a50l", "Liquid Ocelot" }, { "s05a55l", "Liquid Ocelot: Aftermath" },
                { "s10a10l", "Nomad Mission Briefing" }, { "s10a20l", "Nomad: South America Briefing" },
                { "s10a30l", "Nomad: Eastern Europe Briefing" }, { "s10a40l", "Nomad: Shadow Moses Briefing" },
                { "s20a00l", "USS Missouri" }, { "s20a10l", "USS Missouri vs. Outer Haven" }, { "s20a20l", "Campbells Room" },
                { "s30a00l", "Wedding" }, { "s30a10l", "Hospital" }
            });
    }

    public sealed class LiveGameReadout
    {
        internal LiveGameReadout(string code, string stage, string act, string difficulty, string rank, uint progress)
        { StageCode = code; Stage = stage; Act = act; Difficulty = difficulty; Rank = rank; Progress = progress; }
        public string StageCode { get; }
        public string Stage { get; }
        public string Act { get; }
        public string Difficulty { get; }
        public string Rank { get; }
        public uint Progress { get; }
    }

    public sealed class GameActionResult
    {
        internal GameActionResult(int changedCount, string summary) { ChangedCount = changedCount; Summary = summary; }
        public int ChangedCount { get; }
        public string Summary { get; }
        public override string ToString() => Summary;
    }

    public sealed class StageEntry
    {
        internal StageEntry(int id, string code, string name) { Id = id; Code = code; Name = name; }
        public int Id { get; }
        public string Code { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    public sealed class StageCatalog
    {
        internal StageCatalog(int processId, DateTime startTime, List<StageEntry> entries, IntPtr id, IntPtr flags,
            IntPtr head, IntPtr root, int nodes)
        {
            ProcessId = processId; ProcessStartTime = startTime; Entries = entries.AsReadOnly();
            StageIdAddress = id; StageFlagsAddress = flags; MapHead = head; MapRoot = root; NodesVisited = nodes;
        }
        public int ProcessId { get; }
        public DateTime ProcessStartTime { get; }
        public IReadOnlyList<StageEntry> Entries { get; }
        public IntPtr StageIdAddress { get; }
        public IntPtr StageFlagsAddress { get; }
        public IntPtr MapHead { get; }
        public IntPtr MapRoot { get; }
        public int NodesVisited { get; }
        public bool Experimental => true;
        public string Summary => Entries.Count == 0 ? "No stages found. The registry fills after a level is loaded; enter gameplay and refresh the list."
            : Entries.Count + " stages found. Stage loading uses the supplied experimental engine-layout research.";
    }
}
