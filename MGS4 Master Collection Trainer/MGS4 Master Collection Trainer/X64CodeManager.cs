using System;
using System.Collections.Generic;
using System.Globalization;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Small byte emitter for the fixed instructions used by the imported table.</summary>
    internal sealed class X64CodeManager
    {
        private readonly IntPtr baseAddress;
        private readonly List<byte> bytes = new List<byte>();
        private readonly Dictionary<string, int> labels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<RelativeReference> references = new List<RelativeReference>();

        internal X64CodeManager(IntPtr baseAddress)
        {
            if (baseAddress == IntPtr.Zero) throw new ArgumentException("A code allocation is required.", nameof(baseAddress));
            this.baseAddress = baseAddress;
        }

        internal int Count => bytes.Count;

        internal void Emit(params byte[] value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            bytes.AddRange(value);
        }

        internal void EmitHex(string value) => Emit(ParseHex(value));

        internal void Label(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A label name is required.", nameof(name));
            if (labels.ContainsKey(name)) throw new InvalidOperationException("Duplicate code label: " + name);
            labels.Add(name, bytes.Count);
        }

        internal void Jump(IntPtr target) => Relative(new byte[] { 0xE9 }, target, null, Array.Empty<byte>());
        internal void Call(IntPtr target) => Relative(new byte[] { 0xE8 }, target, null, Array.Empty<byte>());
        internal void Jump(string label) => Relative(new byte[] { 0xE9 }, IntPtr.Zero, label, Array.Empty<byte>());

        // secondOpcode is the second byte of a near Jcc, e.g. 84 = JE, 85 = JNE.
        internal void ConditionalJump(byte secondOpcode, string label)
        {
            if (secondOpcode < 0x80 || secondOpcode > 0x8F)
                throw new ArgumentOutOfRangeException(nameof(secondOpcode));
            Relative(new byte[] { 0x0F, secondOpcode }, IntPtr.Zero, label, Array.Empty<byte>());
        }

        /// <summary>Emits opcode/ModRM, signed RIP displacement and any immediate after the displacement.</summary>
        internal void Rip(string opcodeAndModRm, IntPtr target, params byte[] immediate)
        {
            Relative(ParseHex(opcodeAndModRm), target, null, immediate ?? Array.Empty<byte>());
        }

        internal void MovRaxImmediate(IntPtr value)
        {
            Emit(0x48, 0xB8);
            Emit(BitConverter.GetBytes(value.ToInt64()));
        }

        private void Relative(byte[] prefix, IntPtr target, string label, byte[] suffix)
        {
            Emit(prefix);
            int displacementOffset = bytes.Count;
            Emit(0, 0, 0, 0);
            Emit(suffix);
            references.Add(new RelativeReference(displacementOffset, bytes.Count, target, label));
        }

        internal byte[] ToArray()
        {
            byte[] result = bytes.ToArray();
            foreach (RelativeReference reference in references)
            {
                long target = reference.Target.ToInt64();
                if (reference.Label != null)
                {
                    if (!labels.TryGetValue(reference.Label, out int offset))
                        throw new InvalidOperationException("Undefined code label: " + reference.Label);
                    target = checked(baseAddress.ToInt64() + offset);
                }
                long nextInstruction = checked(baseAddress.ToInt64() + reference.NextInstructionOffset);
                long displacement = checked(target - nextInstruction);
                if (displacement < int.MinValue || displacement > int.MaxValue)
                    throw new InvalidOperationException("An x64 relative branch or RIP reference is outside signed 32-bit range.");
                Buffer.BlockCopy(BitConverter.GetBytes((int)displacement), 0, result, reference.DisplacementOffset, 4);
            }
            return result;
        }

        internal static byte[] ParseHex(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            string[] parts = value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] result = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = byte.Parse(parts[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return result;
        }

        private sealed class RelativeReference
        {
            internal RelativeReference(int displacementOffset, int nextInstructionOffset, IntPtr target, string label)
            {
                DisplacementOffset = displacementOffset;
                NextInstructionOffset = nextInstructionOffset;
                Target = target;
                Label = label;
            }

            internal int DisplacementOffset { get; }
            internal int NextInstructionOffset { get; }
            internal IntPtr Target { get; }
            internal string Label { get; }
        }
    }
}
