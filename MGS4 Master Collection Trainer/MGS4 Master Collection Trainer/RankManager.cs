using System;
using System.Globalization;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>The priority and estimates from CE entries 1125/1161, including their original ordering.</summary>
    internal static class RankManager
    {
        internal const uint FramesPerHour = 216000;
        internal const int PlayerReadLength = 0xAE2;
        internal static readonly string[] Targets = { "BIG BOSS", "FOX HOUND", "FOX", "HOUND", "MANTIS", "WOLF", "RAVEN", "OCTOPUS", "PIGEON" };

        internal static string DifficultyName(ushort value)
        {
            switch (value)
            {
                case 20: return "Liquid Easy";
                case 30: return "Naked Normal";
                case 35: return "Solid Normal";
                case 40: return "Big Boss Hard";
                case 50: return "The Boss Extreme";
                default: return "unknown (" + value + ")";
            }
        }

        internal static RankPreview Evaluate(byte[] player)
        {
            if (player == null || player.Length < PlayerReadLength) throw new ArgumentException("A complete player counter snapshot is required.");
            Func<int, ushort> u16 = offset => BitConverter.ToUInt16(player, offset);
            Func<int, uint> u32 = offset => BitConverter.ToUInt32(player, offset);
            ushort diff = u16(6), alerts = u16(0x16E), kills = u16(0x178), continues = u16(0x158), recoveries = u16(0xAE0);
            uint time = u32(0x168);
            int cqc = u16(0x180), heads = u16(0x182), knives = u16(0x184) + u16(0x186);
            bool clean = kills == 0 && continues == 0 && recoveries == 0;
            bool noSpecial = u16(0x17A) == 0;
            string emblem = null;
            if (diff == 50 && alerts == 0 && clean && time <= 5 * FramesPerHour && noSpecial) emblem = "BIG BOSS";
            else if (diff >= 40 && alerts <= 3 && clean && time <= 5.5 * FramesPerHour && noSpecial) emblem = "FOX HOUND";
            else if (diff >= 35 && alerts <= 5 && clean && time <= 6 * FramesPerHour && noSpecial) emblem = "FOX";
            else if (diff >= 30 && alerts <= 10 && clean && time <= 6.5 * FramesPerHour && noSpecial) emblem = "HOUND";
            else if (alerts == 0 && continues == 0 && recoveries == 0 && time <= 5 * FramesPerHour) emblem = "MANTIS";
            else if (continues == 0 && recoveries == 0) emblem = "WOLF";
            else if (time <= 5 * FramesPerHour) emblem = "RAVEN";
            else if (alerts == 0) emblem = "OCTOPUS";
            else if (cqc >= 100) emblem = "BEAR";
            else if (heads >= 150) emblem = "EAGLE";
            else if (knives >= 50 && cqc >= 50 && alerts <= 25) emblem = "ASSASSIN";
            else if (kills == 0) emblem = "PIGEON";
            else if (u16(0x198) >= 50) emblem = "BLUE BIRD";
            else if (u16(0x196) >= 25) emblem = "HAWK";
            else if (u16(0x194) >= 50) emblem = "ANT";
            else if (u16(0x192) >= 50) emblem = "GIBBON";
            else if ((ulong)u32(0x1B8) + u32(0x1BC) >= 216000) emblem = "TORTOISE";
            else if (u16(0x19E) + u16(0x1A0) >= 100) emblem = "RABBIT";
            else if (u16(0x19A) + u16(0x19C) >= 50) emblem = "BEE";
            else if (u32(0x1B4) >= 216000) emblem = "GECKO";
            else if (u16(0x188) >= 100) emblem = "SCARAB";
            else if (u16(0x18A) >= 200) emblem = "FROG";
            else if (u32(0x1AC) >= 216000) emblem = "INCH WORM";
            else if (u32(0x1A8) >= 540000) emblem = "LOBSTER";
            else if (u16(0x18E) + u16(0x190) >= 400) emblem = "HYENA";
            else if (u16(0x18C) >= 10) emblem = "HOG";
            else if (recoveries >= 40) emblem = "PIG";
            else if (alerts >= 100) emblem = "COW";
            else if (kills >= 400) emblem = "CROCODILE";
            else if (time >= 30 * FramesPerHour) emblem = "GIANT PANDA";
            // Preserved from the supplied preview: earlier PIG/COW rules take precedence,
            // so the table's CHICKEN branch cannot win for these same counter values.
            else if (alerts >= 150 && kills >= 500 && continues >= 50 && recoveries >= 50 && time >= 35 * FramesPerHour) emblem = "CHICKEN";

            string[] grid = alerts <= 75 ? new[] { "SCORPION", "TARANTULA", "CENTIPEDE", "SPIDER" }
                : new[] { "JAGUAR", "PANTHER", "LEOPARD", "PUMA" };
            string fallback = grid[(kills <= 250 ? 0 : 2) + (continues <= 25 ? 0 : 1)];
            long points = time <= 5 * FramesPerHour ? 15000 : time <= 12.5 * FramesPerHour
                ? (long)Math.Floor(10000.0 * (12.5 * FramesPerHour - time) / (7.5 * FramesPerHour)) : 0;
            points += Taper(continues, 10000, 5000, 25) + Taper(alerts, 20000, 10000, 25)
                + Taper(kills, 20000, 10000, 50) + Taper(recoveries, 10000, 5000, 25) + (noSpecial ? 5000 : 0);
            int multiplier = diff == 20 ? 1 : diff == 30 || diff == 35 ? 2 : diff == 40 ? 5 : diff == 50 ? 10 : 0;
            return new RankPreview(emblem, fallback, points * multiplier, diff, time, alerts, kills, continues, recoveries);
        }

        private static long Taper(int value, int zero, int from, int last)
        {
            if (value == 0) return zero;
            if (value > last) return 0;
            long result = (long)from * (last - value) / (last - 1);
            if (result > 0 && result < 10) result = 10;
            return result - result % 10;
        }
    }

    public sealed class RankPreview
    {
        internal RankPreview(string priority, string fallback, long points, ushort difficulty, uint frames,
            ushort alerts, ushort kills, ushort continues, ushort recoveries)
        {
            PriorityEmblem = priority;
            GridFallback = fallback;
            BonusDrebinPoints = points;
            DifficultyValue = difficulty;
            TimeFrames = frames;
            Alerts = alerts; Kills = kills; Continues = continues; Recoveries = recoveries;
        }
        internal string PriorityEmblem { get; }
        public string Emblem => PriorityEmblem ?? "none of the priority emblems";
        public string GridFallback { get; }
        public long BonusDrebinPoints { get; }
        public ushort DifficultyValue { get; }
        public string Difficulty => RankManager.DifficultyName(DifficultyValue);
        public uint TimeFrames { get; }
        public uint Hours => TimeFrames / RankManager.FramesPerHour;
        public uint Minutes => TimeFrames % RankManager.FramesPerHour / 3600;
        public ushort Alerts { get; }
        public ushort Kills { get; }
        public ushort Continues { get; }
        public ushort Recoveries { get; }
        public string Summary => string.Format(CultureInfo.InvariantCulture,
            "Difficulty: {0} ({1})\nPlay time: {2:00}:{3:00}; alerts {4}; kills {5}; continues {6}; recovery {7}\n" +
            "Emblem now: {8}\nRegular grid fallback: {9}\nBonus DP estimate: {10}\n" +
            "The supplied table estimates time/continues/alerts/kills/recovery/special bonuses; weapon and flashback bonuses are excluded.",
            Difficulty, DifficultyValue, Hours, Minutes, Alerts, Kills, Continues, Recoveries, Emblem, GridFallback, BonusDrebinPoints);
    }
}
