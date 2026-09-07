using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static MGS4_Master_Collection_Trainer.Constants;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>
    /// Reads and writes the supplied table's typed values through an owning game session.
    /// Freezes are opt-in and caller-driven: ApplyFreezes performs one write pass; no timer starts here.
    /// </summary>
    public sealed class GameValueManager
    {
        private static readonly Encoding Ascii = Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private readonly Func<GameValueDefinition, byte[]> read;
        private readonly Func<GameValueDefinition, byte[], bool> write;
        private readonly Func<GameValueDefinition, byte[], Func<bool>, bool> writeFrozen;
        private readonly object gate = new object();
        private readonly Dictionary<int, byte[]> frozen = new Dictionary<int, byte[]>();
        private long freezeGeneration;
        private string lastError = string.Empty;

        internal GameValueManager(Func<GameValueDefinition, byte[]> read,
            Func<GameValueDefinition, byte[], bool> write,
            Func<GameValueDefinition, byte[], Func<bool>, bool> writeFrozen)
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.write = write ?? throw new ArgumentNullException(nameof(write));
            // The game session supplies a callback that holds its session lock, checks that the
            // existing process is still alive without attaching, then checks isCurrent before writing.
            this.writeFrozen = writeFrozen ?? throw new ArgumentNullException(nameof(writeFrozen));
        }

        public string LastError { get { lock (gate) return lastError; } }
        public int FrozenCount { get { lock (gate) return frozen.Count; } }
        public IReadOnlyCollection<int> FrozenIds
        {
            get { lock (gate) return Array.AsReadOnly(frozen.Keys.OrderBy(id => id).ToArray()); }
        }

        /// <summary>Returns byte, ushort, uint, ulong, float, or string; null on failure (see LastError).</summary>
        public object Read(int id)
        {
            try
            {
                GameValueDefinition definition = GameValueDefinitionManager.Get(id);
                byte[] bytes = read(definition);
                if (bytes == null || bytes.Length != definition.ByteCount)
                    throw new InvalidOperationException("Could not read all bytes for " + definition.Name + ". Its hook or pointer may be unavailable.");
                object value = Decode(definition.Type, bytes);
                SetError(string.Empty);
                return value;
            }
            catch (Exception ex) { SetError("Value read failed: " + ex.Message); return null; }
        }

        public object Read(GameValueId id) => Read((int)id);

        public string ReadString(int id)
        {
            object value = Read(id);
            if (value == null || value is string) return value as string;
            SetError("Value " + id + " is not a string.");
            return null;
        }

        public string ReadString(GameValueId id) => ReadString((int)id);

        /// <summary>Requires the value's exact CLR type. Null means the read/type check failed.</summary>
        public T? Read<T>(int id) where T : struct
        {
            object value = Read(id);
            if (value == null) return null;
            if (value is T typed) return typed;
            SetError("Value " + id + " has type " + value.GetType().Name + ", not " + typeof(T).Name + ".");
            return null;
        }

        public T? Read<T>(GameValueId id) where T : struct => Read<T>((int)id);

        /// <summary>
        /// Writes exactly the field width. Integers must be in range; floating fields accept finite
        /// float/double values. String fields accept bounded ASCII text. Readouts are read-only.
        /// </summary>
        public bool Write(int id, object value)
        {
            try
            {
                GameValueDefinition definition = GameValueDefinitionManager.Get(id);
                return WriteBytes(definition, Encode(definition, value));
            }
            catch (Exception ex) { SetError("Value write failed: " + ex.Message); return false; }
        }

        public bool Write(GameValueId id, object value) => Write((int)id, value);

        /// <summary>Writes once now and registers a value for future explicit ApplyFreezes calls.</summary>
        public bool Freeze(int id, object value)
        {
            try
            {
                GameValueDefinition definition = GameValueDefinitionManager.Get(id);
                if (definition.IsLocal)
                    throw new InvalidOperationException(definition.Name + " is a trainer setting or readout and cannot be frozen.");
                byte[] bytes = Encode(definition, value);
                long generation;
                lock (gate) generation = freezeGeneration;
                if (!WriteBytes(definition, bytes)) return false;
                lock (gate)
                {
                    if (generation != freezeGeneration)
                    {
                        lastError = "Freezes were cleared while the initial value was being written. The value was written once; retry Freeze to register it.";
                        return false;
                    }
                    frozen[id] = bytes;
                }
                return true;
            }
            catch (Exception ex) { SetError("Value freeze failed: " + ex.Message); return false; }
        }

        public bool Freeze(GameValueId id, object value) => Freeze((int)id, value);

        /// <summary>Captures the current value for freezing; it still needs explicit ApplyFreezes calls.</summary>
        public bool Freeze(int id)
        {
            object value = Read(id);
            return value != null && Freeze(id, value);
        }

        public bool Freeze(GameValueId id) => Freeze((int)id);

        public bool Unfreeze(int id)
        {
            lock (gate) return frozen.Remove(id);
        }

        public bool Unfreeze(GameValueId id) => Unfreeze((int)id);

        /// <summary>
        /// Invalidates pending registrations and future writes. A write already authorized by the
        /// session callback may finish. Does not restore a value that was already written.
        /// </summary>
        public void ClearFreezes()
        {
            lock (gate)
            {
                frozen.Clear();
                unchecked { freezeGeneration++; }
            }
        }

        /// <summary>
        /// Performs one pass and returns the number of failed writes. Resolves fresh pointers on every
        /// pass through the session callbacks. A failure leaves the requested freeze registered for retry.
        /// </summary>
        public int ApplyFreezes()
        {
            KeyValuePair<int, byte[]>[] snapshot;
            lock (gate) snapshot = frozen.ToArray();
            int failures = 0;
            string firstError = string.Empty;
            foreach (KeyValuePair<int, byte[]> entry in snapshot)
            {
                // Do not hold this lock while invoking the owner, which has its own session lock.
                Func<bool> isCurrent = () => IsCurrentFreeze(entry.Key, entry.Value);
                if (!isCurrent()) continue;
                if (!WriteBytes(GameValueDefinitionManager.Get(entry.Key), entry.Value, isCurrent))
                {
                    failures++;
                    if (firstError.Length == 0) firstError = LastError;
                }
            }
            SetError(firstError);
            return failures;
        }

        private bool IsCurrentFreeze(int id, byte[] bytes)
        {
            lock (gate)
                return frozen.TryGetValue(id, out byte[] current) && ReferenceEquals(current, bytes);
        }

        private bool WriteBytes(GameValueDefinition definition, byte[] bytes, Func<bool> isCurrentFreeze = null)
        {
            try
            {
                if (definition.ReadOnly)
                    throw new InvalidOperationException(definition.Name + " is a computed readout and cannot be written or frozen.");
                bool success = isCurrentFreeze == null
                    ? write(definition, bytes)
                    : writeFrozen(definition, bytes, isCurrentFreeze);
                if (!success)
                    throw new InvalidOperationException("Could not write " + definition.Name + ". Its hook or pointer may be unavailable.");
                SetError(string.Empty);
                return true;
            }
            catch (Exception ex) { SetError("Value write failed: " + ex.Message); return false; }
        }

        private void SetError(string message)
        {
            lock (gate) lastError = message;
        }

        internal static object Decode(DataType type, byte[] bytes)
        {
            switch (type)
            {
                case DataType.UInt8: return bytes[0];
                case DataType.UInt16: return BitConverter.ToUInt16(bytes, 0);
                case DataType.UInt32: return BitConverter.ToUInt32(bytes, 0);
                case DataType.UInt64: return BitConverter.ToUInt64(bytes, 0);
                case DataType.Float: return BitConverter.ToSingle(bytes, 0);
                case DataType.String:
                    int end = Array.IndexOf(bytes, (byte)0);
                    return Ascii.GetString(bytes, 0, end < 0 ? bytes.Length : end);
                default: throw new InvalidOperationException("Unsupported table value type: " + type);
            }
        }

        internal static byte[] Encode(GameValueDefinition definition, object value)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (value == null) throw new ArgumentNullException(nameof(value));
            DataType type = definition.Type;
            if (type == DataType.String)
            {
                if (!(value is string text)) throw new ArgumentException("This field requires ASCII text.", nameof(value));
                if (text.IndexOf('\0') >= 0) throw new ArgumentException("String input cannot contain an embedded null terminator.", nameof(value));
                byte[] encoded = Ascii.GetBytes(text);
                int maximum = definition.ByteCount - (definition.ZeroTerminate ? 1 : 0);
                if (encoded.Length > maximum)
                    throw new ArgumentOutOfRangeException(nameof(value), "This string allows at most " + maximum + " ASCII characters.");
                var field = new byte[definition.ByteCount];
                Buffer.BlockCopy(encoded, 0, field, 0, encoded.Length);
                return field;
            }
            if (type == DataType.Float)
            {
                if (!(value is float) && !(value is double))
                    throw new ArgumentException("Coordinate values must be float or double.", nameof(value));
                float number = Convert.ToSingle(value);
                if (float.IsNaN(number) || float.IsInfinity(number))
                    throw new ArgumentOutOfRangeException(nameof(value), "Coordinate values must fit in a finite 32-bit float.");
                return BitConverter.GetBytes(number);
            }

            if (!(value is byte || value is sbyte || value is short || value is ushort ||
                  value is int || value is uint || value is long || value is ulong || value.GetType().IsEnum))
                throw new ArgumentException("This field requires an integer value.", nameof(value));
            // Decimal represents every UInt64 value exactly. No floating conversion can round an
            // out-of-range value back into range, and narrowing casts below throw instead of truncating.
            decimal integer = Convert.ToDecimal(value);
            switch (type)
            {
                case DataType.UInt8: return new[] { checked((byte)integer) };
                case DataType.UInt16: return BitConverter.GetBytes(checked((ushort)integer));
                case DataType.UInt32: return BitConverter.GetBytes(checked((uint)integer));
                case DataType.UInt64: return BitConverter.GetBytes(checked((ulong)integer));
                default: throw new InvalidOperationException("Unsupported table value type: " + type);
            }
        }
    }
}
