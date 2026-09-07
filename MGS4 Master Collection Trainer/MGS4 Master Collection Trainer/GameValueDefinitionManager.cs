using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static MGS4_Master_Collection_Trainer.Constants;

namespace MGS4_Master_Collection_Trainer
{
    // Names and numeric IDs correspond to the supplied Cheat Engine table.
    public enum GameValueId
    {
        Health = 1018,
        HealthMax = 1019,
        Stamina = 1020,
        StaminaMax = 1021,
        Stress = 1022,
        Battery = 1023,
        BatteryMax = 1024,
        DrebinPoints = 1026,
        DrebinPointsFromSales = 1027,
        EnemyMode = 1029,
        AlertLevel = 1030,
        AmmoPointer = 1031,
        InventoryPointer = 1032,
        EnemyPointer = 1033,
        RationCurrent = 1036,
        RationMax = 1037,
        EnergyBarCurrent = 1038,
        EnergyBarMax = 1039,
        EnergyDrinkCurrent = 1040,
        EnergyDrinkMax = 1041,
        PentazeminCurrent = 1042,
        PentazeminMax = 1043,
        StupeCurrent = 1044,
        StupeMax = 1045,
        SolidEyeCurrent = 1046,
        SolidEyeMax = 1047,
        MGMkIICurrent = 1048,
        MGMkIIMax = 1049,
        CameraCurrent = 1050,
        CameraMax = 1051,
        CardboardBoxACurrent = 1052,
        CardboardBoxAMax = 1053,
        DrumCanCurrent = 1054,
        DrumCanMax = 1055,
        IPodCurrent = 1056,
        IPodMax = 1057,
        RadioCurrent = 1058,
        RadioMax = 1059,
        CigarCurrent = 1060,
        CigarMax = 1061,
        MunaCurrent = 1062,
        MunaMax = 1063,
        BandanaCurrent = 1064,
        BandanaMax = 1065,
        StealthSuitCurrent = 1066,
        StealthSuitMax = 1067,
        SyringeCurrent = 1068,
        SyringeMax = 1069,
        ScanningPlugCurrent = 1070,
        ScanningPlugMax = 1071,
        BinocularsCurrent = 1072,
        BinocularsMax = 1073,
        ScanningPlugSpareCurrent = 1074,
        ScanningPlugSpareMax = 1075,
        FaceCamoCurrent = 1076,
        FaceCamoMax = 1077,
        KerotanCurrent = 1078,
        KerotanMax = 1079,
        GakoCurrent = 1080,
        GakoMax = 1081,
        OctoCamoCurrent = 1082,
        OctoCamoMax = 1083,
        WeaponSlot0Current = 1085,
        WeaponSlot0Max = 1086,
        WeaponSlotBCurrent = 1087,
        WeaponSlotBMax = 1088,
        WeaponSlot11Current = 1089,
        WeaponSlot11Max = 1090,
        WeaponSlot14Current = 1091,
        WeaponSlot14Max = 1092,
        WeaponSlot2ACurrent = 1093,
        WeaponSlot2AMax = 1094,
        CompletedPlaythroughs = 1097,
        ScenarioProgress = 1098,
        TotalPlayTimeTicks = 1100,
        Continues = 1101,
        AlertsTriggered = 1102,
        Kills = 1103,
        SpecialItemsUsed = 1104,
        CQCUses = 1105,
        Headshots = 1106,
        KnifeKills = 1107,
        KnifeStuns = 1108,
        ProneRolls = 1109,
        Rolls = 1110,
        CombatHighs = 1111,
        WeaponPickups = 1112,
        ItemPickups = 1113,
        HoldUps = 1114,
        BodySearches = 1115,
        Praises = 1116,
        ItemsGifted = 1117,
        SyringeUses = 1118,
        ScanningPlugUses = 1119,
        PlayboyPagesTurned = 1120,
        EmotionMagazinePages = 1121,
        RecoveriesUsed = 1122,
        FlashbacksViewed = 1123,
        StageReadout = 1127,
        ActReadout = 1128,
        DifficultyReadout = 1129,
        RankReadout = 1130,
        StageToLoad = 1142,
        DynamicResolutionEnabled = 1143,
        RankTarget = 1148,
        CurrentStageCode = 1151,
        CurrentDifficulty = 1152,
        DisplayVersion = 1155,
        DisplayStageName = 1156,
        DisplayDifficulty = 1157,
        DisplayLockitGlobalId = 1158,
        TimeCrouching = 1163,
        TimeProne = 1164,
        TimeAgainstWall = 1165,
        BoxDrumTimePart1 = 1166,
        BoxDrumTimePart2 = 1167,
        WeaponStates = 1170,
        ItemStates = 1171,
        SecondaryInventory = 1172,
        PlayerX = 1179,
        PlayerY = 1180,
        PlayerZ = 1181,
        PlayerActorPointer = 1182,
        ActorMissesSinceSeen = 1183,
        Slot1Pointer = 1185,
        Slot1X = 1186,
        Slot1Y = 1187,
        Slot1Z = 1188,
        Slot2Pointer = 1189,
        Slot2X = 1190,
        Slot2Y = 1191,
        Slot2Z = 1192,
        Slot3Pointer = 1193,
        Slot3X = 1194,
        Slot3Y = 1195,
        Slot3Z = 1196,
        Slot4Pointer = 1197,
        Slot4X = 1198,
        Slot4Y = 1199,
        Slot4Z = 1200,
        Slot5Pointer = 1201,
        Slot5X = 1202,
        Slot5Y = 1203,
        Slot5Z = 1204,
        Slot6Pointer = 1205,
        Slot6X = 1206,
        Slot6Y = 1207,
        Slot6Z = 1208,
        Slot7Pointer = 1209,
        Slot7X = 1210,
        Slot7Y = 1211,
        Slot7Z = 1212,
        Outfit = 1217,
        FaceCamo = 1218,
        TacticalVestColour = 1219,
        AppearanceBrowseAnchor = 1221,
        AppearanceAdjacentPlus1 = 1222,
        AppearanceAdjacentPlus2 = 1223,
        AppearanceAdjacentPlus3 = 1224,
        AppearanceAdjacentPlus4 = 1225,
        AppearanceAdjacentPlus5 = 1226,
        AppearanceAdjacentPlus6 = 1227,
        AppearanceUnknown448 = 1228,
        AppearanceUnknown449 = 1229,
        AppearanceUnknown44A = 1230,
        AppearanceUnknown44B = 1231,
    }

    /// <summary>A named numeric choice documented by the supplied table.</summary>
    public sealed class GameValueChoice
    {
        public GameValueChoice(ulong value, string description)
        {
            Value = value;
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }
        public ulong Value { get; }
        public string Description { get; }
        public override string ToString() => Value + ": " + Description;
    }

    /// <summary>A typed address from the supplied table, without evaluating arbitrary CE expressions.</summary>
    public sealed class GameValueDefinition
    {
        internal GameValueDefinition(int id, string name, string group, DataType type,
            string expression, string symbol, long symbolOffset, bool dereference, long offset,
            bool experimental = false, int stringLength = 0, bool zeroTerminate = false,
            bool local = false, bool computed = false, bool readOnly = false,
            IEnumerable<GameValueChoice> choices = null, string dynamicChoicesSymbol = null)
        {
            if (type == DataType.String && (stringLength <= 0 || stringLength > 4096))
                throw new ArgumentOutOfRangeException(nameof(stringLength), "A string field needs a bounded declared length.");
            if (computed && (!local || !readOnly))
                throw new ArgumentException("Computed readouts must be local and read-only.");
            Id = id;
            Name = name;
            Group = group;
            Type = type;
            AddressExpression = expression;
            Symbol = symbol;
            SymbolOffset = symbolOffset;
            Dereference = dereference;
            Offset = offset;
            Experimental = experimental;
            StringLength = stringLength;
            ZeroTerminate = zeroTerminate;
            IsLocal = local;
            IsComputed = computed;
            ReadOnly = readOnly;
            Choices = Array.AsReadOnly((choices ?? Enumerable.Empty<GameValueChoice>()).ToArray());
            DynamicChoicesSymbol = dynamicChoicesSymbol;
        }

        public int Id { get; }
        public string Name { get; }
        public string Group { get; }
        public DataType Type { get; }
        public string AddressExpression { get; }
        /// <summary>The captured pointer slot, script setting, or module base required by this value.</summary>
        public string Symbol { get; }
        /// <summary>Added to the symbol before the optional pointer read (actor slot index * 8).</summary>
        public long SymbolOffset { get; }
        public bool Dereference { get; }
        /// <summary>Added after the optional pointer read. Every table offset is hexadecimal.</summary>
        public long Offset { get; }
        /// <summary>True for entries depending on an unverified, build-specific module offset.</summary>
        public bool Experimental { get; }
        /// <summary>Declared byte length of a non-Unicode string, zero for numeric fields.</summary>
        public int StringLength { get; }
        public bool ZeroTerminate { get; }
        /// <summary>A trainer setting or computed readout rather than an address in the game.</summary>
        public bool IsLocal { get; }
        public bool IsComputed { get; }
        public bool ReadOnly { get; }
        public IReadOnlyList<GameValueChoice> Choices { get; }
        /// <summary>The remote symbol whose discovered runtime table supplies choices, if any.</summary>
        public string DynamicChoicesSymbol { get; }

        public int ByteCount
        {
            get
            {
                switch (Type)
                {
                    case DataType.UInt8: return 1;
                    case DataType.UInt16: return 2;
                    case DataType.UInt32:
                    case DataType.Float: return 4;
                    case DataType.UInt64: return 8;
                    case DataType.String: return StringLength;
                    default: throw new InvalidOperationException("Unsupported table value type: " + Type);
                }
            }
        }

        /// <summary>
        /// Resolves using the caller's process handle and symbol registry. The caller must keep its
        /// session and hook allocations alive for the entire resolve/read/write operation.
        /// </summary>
        public IntPtr ResolveAddress(IntPtr processHandle, Func<string, IntPtr> resolveSymbol)
        {
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("MGS4 table values require a 64-bit trainer.");
            if (resolveSymbol == null) throw new ArgumentNullException(nameof(resolveSymbol));
            if (IsLocal) throw new InvalidOperationException(Name + " is a trainer value and has no remote game address.");
            IntPtr address = resolveSymbol(Symbol);
            if (address.ToInt64() <= 0)
                throw new InvalidOperationException("The required symbol is unavailable: " + Symbol + ". Enable its hook first.");
            address = MemoryManager.AddOffset(address, SymbolOffset);
            if (address.ToInt64() <= 0) throw new InvalidOperationException("The symbol offset produced an invalid address.");
            if (Dereference)
            {
                byte[] pointer = MemoryManager.ReadMemoryBytes(processHandle, address, 8);
                if (pointer == null) throw new InvalidOperationException("Could not read the captured pointer for " + Name + ".");
                address = new IntPtr(BitConverter.ToInt64(pointer, 0));
                if (address.ToInt64() <= 0)
                    throw new InvalidOperationException("The hook has not captured a valid pointer for " + Name + " yet.");
            }
            address = MemoryManager.AddOffset(address, Offset);
            if (address.ToInt64() <= 0 || checked(address.ToInt64() + ByteCount - 1) <= 0)
                throw new InvalidOperationException("The field offset produced an invalid address.");
            return address;
        }
    }

    public static class GameValueDefinitionManager
    {
        public const int InventoryItemBaseOffset = -0x154;
        public const int InventoryItemStride = 0x48;
        public const int InventoryWeaponBaseOffset = -0x2598;
        public const int InventoryWeaponStride = 0x18;
        public const int InventoryMaximumOffset = 2;
        public const int ActorSlotCount = 8;
        public const int ActorSlotStride = 8;
        public const int ActorXOffset = 0x10;
        public const int ActorYOffset = 0x14;
        public const int ActorZOffset = 0x18;
        public const long ExperimentalAppearanceOffset = 0x23FED45C;

        public static IReadOnlyDictionary<int, GameValueDefinition> All { get; } = Build();

        public static GameValueDefinition Get(int id)
        {
            if (!All.TryGetValue(id, out GameValueDefinition value))
                throw new ArgumentOutOfRangeException(nameof(id), "Unknown table value ID: " + id);
            return value;
        }

        public static GameValueDefinition Get(GameValueId id) => Get((int)id);

        private static IReadOnlyDictionary<int, GameValueDefinition> Build()
        {
            // The original expressions are retained for comparison with the table. The numeric
            // offsets below already account for CE's hexadecimal literals and multiplication.
            var values = new[]
            {
                new GameValueDefinition(1018, "Health", "Vitals (freeze any - no script needed)", DataType.UInt16, "[pPlayer]+B48", "pPlayer", 0, true, 2888),
                new GameValueDefinition(1019, "Health max", "Vitals (freeze any - no script needed)", DataType.UInt16, "[pPlayer]+B4A", "pPlayer", 0, true, 2890),
                new GameValueDefinition(1020, "Stamina", "Vitals (freeze any - no script needed)", DataType.UInt16, "[pPlayer]+B4C", "pPlayer", 0, true, 2892),
                new GameValueDefinition(1021, "Stamina max", "Vitals (freeze any - no script needed)", DataType.UInt16, "[pPlayer]+B4E", "pPlayer", 0, true, 2894),
                new GameValueDefinition(1022, "Stress (0 = none)", "Vitals (freeze any - no script needed)", DataType.UInt16, "[pPlayer]+B50", "pPlayer", 0, true, 2896),
                new GameValueDefinition(1023, "Battery", "Vitals (freeze any - no script needed)", DataType.UInt16, "[pPlayer]+B52", "pPlayer", 0, true, 2898),
                new GameValueDefinition(1024, "Battery max", "Vitals (freeze any - no script needed)", DataType.UInt16, "[pPlayer]+B54", "pPlayer", 0, true, 2900),
                new GameValueDefinition(1026, "Drebin Points", "Drebin Points", DataType.UInt32, "[pPlayer]+1C0", "pPlayer", 0, true, 448),
                new GameValueDefinition(1027, "Drebin Points from sales", "Drebin Points", DataType.UInt16, "[pPlayer]+1C4", "pPlayer", 0, true, 452),
                new GameValueDefinition(1029, "Enemy mode: 0 off / 1 kill / 2 wake-sleep", "Script switches & captured pointers", DataType.UInt8, "bEnemyMode", "bEnemyMode", 0, false, 0),
                new GameValueDefinition(1030, "Alert level: FFFFFFFF = game decides", "Script switches & captured pointers", DataType.UInt32, "iAlertLevel", "iAlertLevel", 0, false, 0),
                new GameValueDefinition(1031, "pAmmo (ammo record)", "Script switches & captured pointers", DataType.UInt64, "pAmmo", "pAmmo", 0, false, 0),
                new GameValueDefinition(1032, "pInv (inventory anchor)", "Script switches & captured pointers", DataType.UInt64, "pInv", "pInv", 0, false, 0),
                new GameValueDefinition(1033, "pEnemy (last enemy actor)", "Script switches & captured pointers", DataType.UInt64, "pEnemy", "pEnemy", 0, false, 0),
                new GameValueDefinition(1036, "Ration - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*0", "pInv", 0, true, -340),
                new GameValueDefinition(1037, "Ration - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*0+2", "pInv", 0, true, -338),
                new GameValueDefinition(1038, "E. Bar - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*1", "pInv", 0, true, -268),
                new GameValueDefinition(1039, "E. Bar - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*1+2", "pInv", 0, true, -266),
                new GameValueDefinition(1040, "E. Drink - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*2", "pInv", 0, true, -196),
                new GameValueDefinition(1041, "E. Drink - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*2+2", "pInv", 0, true, -194),
                new GameValueDefinition(1042, "Pentazemin - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*3", "pInv", 0, true, -124),
                new GameValueDefinition(1043, "Pentazemin - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*3+2", "pInv", 0, true, -122),
                new GameValueDefinition(1044, "Stupe - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*4", "pInv", 0, true, -52),
                new GameValueDefinition(1045, "Stupe - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*4+2", "pInv", 0, true, -50),
                new GameValueDefinition(1046, "Solid Eye - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*5", "pInv", 0, true, 20),
                new GameValueDefinition(1047, "Solid Eye - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*5+2", "pInv", 0, true, 22),
                new GameValueDefinition(1048, "MG Mk. II - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*6", "pInv", 0, true, 92),
                new GameValueDefinition(1049, "MG Mk. II - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*6+2", "pInv", 0, true, 94),
                new GameValueDefinition(1050, "Camera - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*7", "pInv", 0, true, 164),
                new GameValueDefinition(1051, "Camera - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*7+2", "pInv", 0, true, 166),
                new GameValueDefinition(1052, "C. Box A - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*8", "pInv", 0, true, 236),
                new GameValueDefinition(1053, "C. Box A - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*8+2", "pInv", 0, true, 238),
                new GameValueDefinition(1054, "Drum Can - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*9", "pInv", 0, true, 308),
                new GameValueDefinition(1055, "Drum Can - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*9+2", "pInv", 0, true, 310),
                new GameValueDefinition(1056, "iPod - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*A", "pInv", 0, true, 380),
                new GameValueDefinition(1057, "iPod - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*A+2", "pInv", 0, true, 382),
                new GameValueDefinition(1058, "Radio - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*B", "pInv", 0, true, 452),
                new GameValueDefinition(1059, "Radio - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*B+2", "pInv", 0, true, 454),
                new GameValueDefinition(1060, "Cigar - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*C", "pInv", 0, true, 524),
                new GameValueDefinition(1061, "Cigar - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*C+2", "pInv", 0, true, 526),
                new GameValueDefinition(1062, "Muna - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*D", "pInv", 0, true, 596),
                new GameValueDefinition(1063, "Muna - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*D+2", "pInv", 0, true, 598),
                new GameValueDefinition(1064, "Bandana - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*E", "pInv", 0, true, 668),
                new GameValueDefinition(1065, "Bandana - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*E+2", "pInv", 0, true, 670),
                new GameValueDefinition(1066, "Stealth - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*F", "pInv", 0, true, 740),
                new GameValueDefinition(1067, "Stealth - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*F+2", "pInv", 0, true, 742),
                new GameValueDefinition(1068, "Syringe - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*10", "pInv", 0, true, 812),
                new GameValueDefinition(1069, "Syringe - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*10+2", "pInv", 0, true, 814),
                new GameValueDefinition(1070, "Scanning Plug - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*11", "pInv", 0, true, 884),
                new GameValueDefinition(1071, "Scanning Plug - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*11+2", "pInv", 0, true, 886),
                new GameValueDefinition(1072, "Binoculars - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*12", "pInv", 0, true, 956),
                new GameValueDefinition(1073, "Binoculars - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*12+2", "pInv", 0, true, 958),
                new GameValueDefinition(1074, "Scanning Plug (spare) - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*13", "pInv", 0, true, 1028),
                new GameValueDefinition(1075, "Scanning Plug (spare) - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*13+2", "pInv", 0, true, 1030),
                new GameValueDefinition(1076, "FaceCamo - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*14", "pInv", 0, true, 1100),
                new GameValueDefinition(1077, "FaceCamo - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*14+2", "pInv", 0, true, 1102),
                new GameValueDefinition(1078, "Kerotan - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*15", "pInv", 0, true, 1172),
                new GameValueDefinition(1079, "Kerotan - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*15+2", "pInv", 0, true, 1174),
                new GameValueDefinition(1080, "Gako - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*16", "pInv", 0, true, 1244),
                new GameValueDefinition(1081, "Gako - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*16+2", "pInv", 0, true, 1246),
                new GameValueDefinition(1082, "OctoCamo - current", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*17", "pInv", 0, true, 1316),
                new GameValueDefinition(1083, "OctoCamo - max", "Items (needs Inventory Pointer on)", DataType.UInt16, "[pInv]-154+48*17+2", "pInv", 0, true, 1318),
                new GameValueDefinition(1085, "Weapon slot 0 - current", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*0", "pInv", 0, true, -9624),
                new GameValueDefinition(1086, "Weapon slot 0 - max", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*0+2", "pInv", 0, true, -9622),
                new GameValueDefinition(1087, "Weapon slot B - current", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*B", "pInv", 0, true, -9360),
                new GameValueDefinition(1088, "Weapon slot B - max", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*B+2", "pInv", 0, true, -9358),
                new GameValueDefinition(1089, "Weapon slot 11 - current", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*11", "pInv", 0, true, -9216),
                new GameValueDefinition(1090, "Weapon slot 11 - max", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*11+2", "pInv", 0, true, -9214),
                new GameValueDefinition(1091, "Weapon slot 14 - current", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*14", "pInv", 0, true, -9144),
                new GameValueDefinition(1092, "Weapon slot 14 - max", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*14+2", "pInv", 0, true, -9142),
                new GameValueDefinition(1093, "Weapon slot 2A - current", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*2A", "pInv", 0, true, -8616),
                new GameValueDefinition(1094, "Weapon slot 2A - max", "Weapons (needs Inventory Pointer on) - stride 18, browse for more", DataType.UInt16, "[pInv]-2598+18*2A+2", "pInv", 0, true, -8614),
                new GameValueDefinition(1097, "Completed playthroughs", "Run statistics", DataType.UInt32, "[pPlayer]+0", "pPlayer", 0, true, 0),
                new GameValueDefinition(1098, "Scenario progress (0-291)", "Run statistics", DataType.UInt32, "[pPlayer]+54", "pPlayer", 0, true, 84),
                new GameValueDefinition(1100, "Total play time (ticks)", "Run statistics", DataType.UInt32, "[pPlayer]+168", "pPlayer", 0, true, 360),
                new GameValueDefinition(1101, "Continues", "Run statistics", DataType.UInt16, "[pPlayer]+158", "pPlayer", 0, true, 344),
                new GameValueDefinition(1102, "Alert phases", "Run statistics", DataType.UInt16, "[pPlayer]+16E", "pPlayer", 0, true, 366),
                new GameValueDefinition(1103, "Kills", "Run statistics", DataType.UInt16, "[pPlayer]+178", "pPlayer", 0, true, 376),
                new GameValueDefinition(1104, "Special-item use (0 = none)", "Run statistics", DataType.UInt16, "[pPlayer]+17A", "pPlayer", 0, true, 378),
                new GameValueDefinition(1105, "CQC uses", "Run statistics", DataType.UInt16, "[pPlayer]+180", "pPlayer", 0, true, 384),
                new GameValueDefinition(1106, "Headshots", "Run statistics", DataType.UInt16, "[pPlayer]+182", "pPlayer", 0, true, 386),
                new GameValueDefinition(1107, "Knife kills", "Run statistics", DataType.UInt16, "[pPlayer]+184", "pPlayer", 0, true, 388),
                new GameValueDefinition(1108, "Knife knockouts", "Run statistics", DataType.UInt16, "[pPlayer]+186", "pPlayer", 0, true, 390),
                new GameValueDefinition(1109, "Prone side rolls", "Run statistics", DataType.UInt16, "[pPlayer]+188", "pPlayer", 0, true, 392),
                new GameValueDefinition(1110, "Forward rolls", "Run statistics", DataType.UInt16, "[pPlayer]+18A", "pPlayer", 0, true, 394),
                new GameValueDefinition(1111, "Combat Highs", "Run statistics", DataType.UInt16, "[pPlayer]+18C", "pPlayer", 0, true, 396),
                new GameValueDefinition(1112, "Weapon pickups", "Run statistics", DataType.UInt16, "[pPlayer]+18E", "pPlayer", 0, true, 398),
                new GameValueDefinition(1113, "Item pickups", "Run statistics", DataType.UInt16, "[pPlayer]+190", "pPlayer", 0, true, 400),
                new GameValueDefinition(1114, "Hold-ups", "Run statistics", DataType.UInt16, "[pPlayer]+192", "pPlayer", 0, true, 402),
                new GameValueDefinition(1115, "Body searches", "Run statistics", DataType.UInt16, "[pPlayer]+194", "pPlayer", 0, true, 404),
                new GameValueDefinition(1116, "Praises", "Run statistics", DataType.UInt16, "[pPlayer]+196", "pPlayer", 0, true, 406),
                new GameValueDefinition(1117, "Items donated", "Run statistics", DataType.UInt16, "[pPlayer]+198", "pPlayer", 0, true, 408),
                new GameValueDefinition(1118, "Syringe uses", "Run statistics", DataType.UInt16, "[pPlayer]+19A", "pPlayer", 0, true, 410),
                new GameValueDefinition(1119, "Scanning Plug uses", "Run statistics", DataType.UInt16, "[pPlayer]+19C", "pPlayer", 0, true, 412),
                new GameValueDefinition(1120, "Playboy pages turned", "Run statistics", DataType.UInt16, "[pPlayer]+19E", "pPlayer", 0, true, 414),
                new GameValueDefinition(1121, "Emotion Magazine pages", "Run statistics", DataType.UInt16, "[pPlayer]+1A0", "pPlayer", 0, true, 416),
                new GameValueDefinition(1122, "Recovery items used", "Run statistics", DataType.UInt16, "[pPlayer]+AE0", "pPlayer", 0, true, 2784),
                new GameValueDefinition(1123, "Flashbacks viewed (target 273)", "Run statistics", DataType.UInt16, "[pPlayer]+5A34", "pPlayer", 0, true, 23092),
                new GameValueDefinition(1127, "Stage", "LIVE readout", DataType.String, "mgsStage", "mgsStage", 0, false, 0, stringLength: 100, zeroTerminate: true, local: true, computed: true, readOnly: true),
                new GameValueDefinition(1128, "Act", "LIVE readout", DataType.String, "mgsAct", "mgsAct", 0, false, 0, stringLength: 60, zeroTerminate: true, local: true, computed: true, readOnly: true),
                new GameValueDefinition(1129, "Difficulty", "LIVE readout", DataType.String, "mgsDiff", "mgsDiff", 0, false, 0, stringLength: 40, zeroTerminate: true, local: true, computed: true, readOnly: true),
                new GameValueDefinition(1130, "Rank you would get now", "LIVE readout", DataType.String, "mgsRank", "mgsRank", 0, false, 0, stringLength: 40, zeroTerminate: true, local: true, computed: true, readOnly: true),
                new GameValueDefinition(1142, "Stage to load (pick from the list)", "Debug build options  [TriggerHappy]", DataType.UInt32, "stageIdSel", "stageIdSel", 0, false, 0, dynamicChoicesSymbol: "stageIdSel"),
                new GameValueDefinition(1143, "Dynamic resolution flag (0 = off)", "Debug build options  [TriggerHappy]", DataType.UInt8, "dynResEnabled", "dynResEnabled", 0, false, 0),
                new GameValueDefinition(1148, "Rank target: 0 BIG BOSS, 1 FOX HOUND, 2 FOX, 3 HOUND, 4 MANTIS, 5 WOLF, 6 RAVEN, 7 OCTOPUS, 8 PIGEON", "Force a rank", DataType.UInt32, "iRankTarget", "iRankTarget", 0, false, 0, local: true, choices: new[] { new GameValueChoice(0, "BIG BOSS"), new GameValueChoice(1, "FOX HOUND"), new GameValueChoice(2, "FOX"), new GameValueChoice(3, "HOUND"), new GameValueChoice(4, "MANTIS"), new GameValueChoice(5, "WOLF"), new GameValueChoice(6, "RAVEN"), new GameValueChoice(7, "OCTOPUS"), new GameValueChoice(8, "PIGEON") }),
                new GameValueDefinition(1151, "Current stage code (raw)", "Stage / difficulty (raw values)", DataType.String, "[pPlayer]+34", "pPlayer", 0, true, 52, stringLength: 16, zeroTerminate: true),
                new GameValueDefinition(1152, "Current difficulty (raw)", "Stage / difficulty (raw values)", DataType.UInt16, "[pPlayer]+6", "pPlayer", 0, true, 6, choices: new[] { new GameValueChoice(20, "Liquid Easy"), new GameValueChoice(30, "Naked Normal"), new GameValueChoice(35, "Solid Normal"), new GameValueChoice(40, "Big Boss Hard"), new GameValueChoice(50, "The Boss Extreme") }),
                new GameValueDefinition(1155, "Display Version", "Stage / difficulty (raw values)", DataType.UInt8, "mgs4.exe+1CE5EB0", "mgs4.exe", 0, false, 30301872, experimental: true),
                new GameValueDefinition(1156, "Display StageName", "Stage / difficulty (raw values)", DataType.UInt8, "mgs4.exe+1CE5EB1", "mgs4.exe", 0, false, 30301873, experimental: true),
                new GameValueDefinition(1157, "Display Difficulty", "Stage / difficulty (raw values)", DataType.UInt8, "mgs4.exe+1CE5EB2", "mgs4.exe", 0, false, 30301874, experimental: true),
                new GameValueDefinition(1158, "Display Lockit GlobalId", "Stage / difficulty (raw values)", DataType.UInt8, "mgs4.exe+1CE5EB3", "mgs4.exe", 0, false, 30301875, experimental: true),
                new GameValueDefinition(1163, "Crouch time", "Posture timers", DataType.UInt32, "[pPlayer]+1A8", "pPlayer", 0, true, 424),
                new GameValueDefinition(1164, "Crawl time", "Posture timers", DataType.UInt32, "[pPlayer]+1AC", "pPlayer", 0, true, 428),
                new GameValueDefinition(1165, "Wall-press time", "Posture timers", DataType.UInt32, "[pPlayer]+1B4", "pPlayer", 0, true, 436),
                new GameValueDefinition(1166, "Box/drum time (part 1)", "Posture timers", DataType.UInt32, "[pPlayer]+1B8", "pPlayer", 0, true, 440),
                new GameValueDefinition(1167, "Box/drum time (part 2)", "Posture timers", DataType.UInt32, "[pPlayer]+1BC", "pPlayer", 0, true, 444),
                new GameValueDefinition(1170, "Weapon states - array start (ID 0)", "Weapon / item state arrays", DataType.UInt16, "[pPlayer]+1D4", "pPlayer", 0, true, 468),
                new GameValueDefinition(1171, "Item states - array start (ID 0)", "Weapon / item state arrays", DataType.UInt16, "[pPlayer]+526", "pPlayer", 0, true, 1318),
                new GameValueDefinition(1172, "Secondary inventory array (68 entries)", "Weapon / item state arrays", DataType.UInt16, "[pPlayer]+350", "pPlayer", 0, true, 848),
                new GameValueDefinition(1179, "Player X", "Player position (live, editable - teleport works)", DataType.Float, "[pSlot]+10", "pSlot", 0, true, 16),
                new GameValueDefinition(1180, "Player Y (up/down)", "Player position (live, editable - teleport works)", DataType.Float, "[pSlot]+14", "pSlot", 0, true, 20),
                new GameValueDefinition(1181, "Player Z", "Player position (live, editable - teleport works)", DataType.Float, "[pSlot]+18", "pSlot", 0, true, 24),
                new GameValueDefinition(1182, "Player actor pointer (auto-latched, editable)", "Player position (live, editable - teleport works)", DataType.UInt64, "pSlot", "pSlot", 0, false, 0),
                new GameValueDefinition(1183, "Misses since last seen (auto-relatch at 512)", "Player position (live, editable - teleport works)", DataType.UInt32, "hbCnt", "hbCnt", 0, false, 0),
                new GameValueDefinition(1185, "slot 1 pointer", "Other actor slots (fallback)", DataType.UInt64, "pRing+8", "pRing", 8, false, 0),
                new GameValueDefinition(1186, "slot 1  X", "Other actor slots (fallback)", DataType.Float, "[pRing+8]+10", "pRing", 8, true, 16),
                new GameValueDefinition(1187, "slot 1  Y", "Other actor slots (fallback)", DataType.Float, "[pRing+8]+14", "pRing", 8, true, 20),
                new GameValueDefinition(1188, "slot 1  Z", "Other actor slots (fallback)", DataType.Float, "[pRing+8]+18", "pRing", 8, true, 24),
                new GameValueDefinition(1189, "slot 2 pointer", "Other actor slots (fallback)", DataType.UInt64, "pRing+10", "pRing", 16, false, 0),
                new GameValueDefinition(1190, "slot 2  X", "Other actor slots (fallback)", DataType.Float, "[pRing+10]+10", "pRing", 16, true, 16),
                new GameValueDefinition(1191, "slot 2  Y", "Other actor slots (fallback)", DataType.Float, "[pRing+10]+14", "pRing", 16, true, 20),
                new GameValueDefinition(1192, "slot 2  Z", "Other actor slots (fallback)", DataType.Float, "[pRing+10]+18", "pRing", 16, true, 24),
                new GameValueDefinition(1193, "slot 3 pointer", "Other actor slots (fallback)", DataType.UInt64, "pRing+18", "pRing", 24, false, 0),
                new GameValueDefinition(1194, "slot 3  X", "Other actor slots (fallback)", DataType.Float, "[pRing+18]+10", "pRing", 24, true, 16),
                new GameValueDefinition(1195, "slot 3  Y", "Other actor slots (fallback)", DataType.Float, "[pRing+18]+14", "pRing", 24, true, 20),
                new GameValueDefinition(1196, "slot 3  Z", "Other actor slots (fallback)", DataType.Float, "[pRing+18]+18", "pRing", 24, true, 24),
                new GameValueDefinition(1197, "slot 4 pointer", "Other actor slots (fallback)", DataType.UInt64, "pRing+20", "pRing", 32, false, 0),
                new GameValueDefinition(1198, "slot 4  X", "Other actor slots (fallback)", DataType.Float, "[pRing+20]+10", "pRing", 32, true, 16),
                new GameValueDefinition(1199, "slot 4  Y", "Other actor slots (fallback)", DataType.Float, "[pRing+20]+14", "pRing", 32, true, 20),
                new GameValueDefinition(1200, "slot 4  Z", "Other actor slots (fallback)", DataType.Float, "[pRing+20]+18", "pRing", 32, true, 24),
                new GameValueDefinition(1201, "slot 5 pointer", "Other actor slots (fallback)", DataType.UInt64, "pRing+28", "pRing", 40, false, 0),
                new GameValueDefinition(1202, "slot 5  X", "Other actor slots (fallback)", DataType.Float, "[pRing+28]+10", "pRing", 40, true, 16),
                new GameValueDefinition(1203, "slot 5  Y", "Other actor slots (fallback)", DataType.Float, "[pRing+28]+14", "pRing", 40, true, 20),
                new GameValueDefinition(1204, "slot 5  Z", "Other actor slots (fallback)", DataType.Float, "[pRing+28]+18", "pRing", 40, true, 24),
                new GameValueDefinition(1205, "slot 6 pointer", "Other actor slots (fallback)", DataType.UInt64, "pRing+30", "pRing", 48, false, 0),
                new GameValueDefinition(1206, "slot 6  X", "Other actor slots (fallback)", DataType.Float, "[pRing+30]+10", "pRing", 48, true, 16),
                new GameValueDefinition(1207, "slot 6  Y", "Other actor slots (fallback)", DataType.Float, "[pRing+30]+14", "pRing", 48, true, 20),
                new GameValueDefinition(1208, "slot 6  Z", "Other actor slots (fallback)", DataType.Float, "[pRing+30]+18", "pRing", 48, true, 24),
                new GameValueDefinition(1209, "slot 7 pointer", "Other actor slots (fallback)", DataType.UInt64, "pRing+38", "pRing", 56, false, 0),
                new GameValueDefinition(1210, "slot 7  X", "Other actor slots (fallback)", DataType.Float, "[pRing+38]+10", "pRing", 56, true, 16),
                new GameValueDefinition(1211, "slot 7  Y", "Other actor slots (fallback)", DataType.Float, "[pRing+38]+14", "pRing", 56, true, 20),
                new GameValueDefinition(1212, "slot 7  Z", "Other actor slots (fallback)", DataType.Float, "[pRing+38]+18", "pRing", 56, true, 24),
                new GameValueDefinition(1217, "Outfit (body / uniform)", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED446", "mgs4.exe", 0, false, 603903046, experimental: true),
                new GameValueDefinition(1218, "FaceCamo", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED447", "mgs4.exe", 0, false, 603903047, experimental: true),
                new GameValueDefinition(1219, "Tactical vest colour", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED45C", "mgs4.exe", 0, false, 603903068, experimental: true),
                new GameValueDefinition(1221, "Browse anchor (Ctrl+B here)", "Appearance / Camo (browse & experiment)", DataType.UInt32, "mgs4.exe+23FED440", "mgs4.exe", 0, false, 603903040, experimental: true),
                new GameValueDefinition(1222, "unknown +1 after vest", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED45C+1", "mgs4.exe", 0, false, 603903069, experimental: true),
                new GameValueDefinition(1223, "unknown +2 after vest", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED45C+2", "mgs4.exe", 0, false, 603903070, experimental: true),
                new GameValueDefinition(1224, "unknown +3 after vest", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED45C+3", "mgs4.exe", 0, false, 603903071, experimental: true),
                new GameValueDefinition(1225, "unknown +4 after vest", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED45C+4", "mgs4.exe", 0, false, 603903072, experimental: true),
                new GameValueDefinition(1226, "unknown +5 after vest", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED45C+5", "mgs4.exe", 0, false, 603903073, experimental: true),
                new GameValueDefinition(1227, "unknown +6 after vest", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED45C+6", "mgs4.exe", 0, false, 603903074, experimental: true),
                new GameValueDefinition(1228, "unknown at ...448", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED448", "mgs4.exe", 0, false, 603903048, experimental: true),
                new GameValueDefinition(1229, "unknown at ...449", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED449", "mgs4.exe", 0, false, 603903049, experimental: true),
                new GameValueDefinition(1230, "unknown at ...44A", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED44A", "mgs4.exe", 0, false, 603903050, experimental: true),
                new GameValueDefinition(1231, "unknown at ...44B", "Appearance / Camo (browse & experiment)", DataType.UInt8, "mgs4.exe+23FED44B", "mgs4.exe", 0, false, 603903051, experimental: true),
            };
            return new ReadOnlyDictionary<int, GameValueDefinition>(values.ToDictionary(value => value.Id));
        }
    }
}
