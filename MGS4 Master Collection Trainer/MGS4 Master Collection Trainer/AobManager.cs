using System;
using System.Collections.Generic;
using System.Linq;

namespace MGS4_Master_Collection_Trainer
{
    public sealed class AobManager
    {
        static AobManager()
        {
            foreach (SignatureDefinition signature in EffectDefinitionManager.Signatures)
                if (!AOBs.ContainsKey(signature.Key))
                    AOBs.Add(signature.Key, ((byte[])signature.Pattern.Clone(), signature.Mask, null, null));
        }

        private static readonly Lazy<AobManager> instance =
            new Lazy<AobManager>(() => new AobManager());

        private AobManager()
        {
        }

        public static AobManager Instance => instance.Value;

        /// <summary>
        /// Reads both known states without changing the game. Like Delta's byte-state checks,
        /// a match is an observation only; callers must validate its instruction/cave before adopting it.
        /// </summary>
        public AobPatternInspection InspectPatterns(IntPtr processHandle, IntPtr start, long size,
            byte[] disabledBytes, string disabledMask, byte[] enabledBytes = null, string enabledMask = null)
        {
            var disabled = MemoryManager.Instance.ScanForAllAobInstances(processHandle, start, size, disabledBytes, disabledMask);
            var enabled = enabledBytes == null ? new List<IntPtr>() :
                MemoryManager.Instance.ScanForAllAobInstances(processHandle, start, size, enabledBytes, enabledMask);
            return new AobPatternInspection(disabled, enabled);
        }

        /// <summary>
        /// Stores named patterns for the memory scanners. Each mask must have one
        /// character per byte: 'x' matches the byte and '?' accepts any byte.
        /// FindAob and FindLastAob interpret the optional bounds as offsets from
        /// the main module. FindDynamicAob requires both bounds and interprets
        /// them as absolute memory addresses.
        /// </summary>
        public static readonly Dictionary<string, (byte[] Pattern, string Mask, IntPtr? StartOffset, IntPtr? EndOffset)> AOBs =
            new Dictionary<string, (byte[] Pattern, string Mask, IntPtr? StartOffset, IntPtr? EndOffset)>(StringComparer.Ordinal)
            {
                // User-provided signature for capturing RDX as the player object.
                { Constants.PlayerHookAobKey, (new byte[] { 0x48, 0x63, 0x82, 0x60, 0x01, 0x00, 0x00, 0x49 }, "xxxxxxxx", null, null) },

                // mov [r9+0xB50],ax; the final 0x48 belongs to the next instruction.
                { Constants.StressAobKey, (new byte[] { 0x66, 0x41, 0x89, 0x81, 0x50, 0x0B, 0x00, 0x00, 0x48 }, "xxxxxxxxx", null, null) },

                // Add verified MGS4 patterns here. Registration example:
                // { "ExamplePattern", (new byte[] { 0x48, 0x00, 0x89 }, "x?x", null, null) }
                // The example bytes are illustrative and are not an MGS4 signature.
            };
    }

    public sealed class AobPatternInspection
    {
        internal AobPatternInspection(IEnumerable<IntPtr> disabled, IEnumerable<IntPtr> enabled)
        {
            DisabledMatches = Array.AsReadOnly(disabled.Distinct().ToArray());
            EnabledMatches = Array.AsReadOnly(enabled.Distinct().ToArray());
        }
        public IReadOnlyList<IntPtr> DisabledMatches { get; }
        public IReadOnlyList<IntPtr> EnabledMatches { get; }
    }
}
