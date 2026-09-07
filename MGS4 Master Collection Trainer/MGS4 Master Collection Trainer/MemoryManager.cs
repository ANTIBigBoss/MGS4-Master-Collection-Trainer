using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using static MGS4_Master_Collection_Trainer.Constants;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Process memory access and AOB scanning, independent of any form or effect.</summary>
    public sealed class MemoryManager
    {
        private static readonly Lazy<MemoryManager> instance =
            new Lazy<MemoryManager>(() => new MemoryManager());
        private const int ScanBufferSize = 65536;
        private const int WideScanBufferSize = 10000000;
        private const uint ProcessAccess = 0x0400 | 0x0010 | 0x0020 | 0x0008;

        private MemoryManager() { }

        public static MemoryManager Instance => instance.Value;

        #region Native process access

        public static class NativeMethods
        {
            // SIZE_T parameters and return values must remain pointer-sized on x64.
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint GetProcessId(IntPtr processHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ReadProcessMemory(IntPtr processHandle, IntPtr address,
                [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool WriteProcessMemory(IntPtr processHandle, IntPtr address,
                byte[] buffer, UIntPtr size, out UIntPtr bytesWritten);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern UIntPtr VirtualQueryEx(IntPtr processHandle, IntPtr address,
                out MemoryBasicInformation information, UIntPtr informationLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr VirtualAllocEx(IntPtr processHandle, IntPtr address,
                UIntPtr size, uint allocationType, uint protection);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool VirtualFreeEx(IntPtr processHandle, IntPtr address,
                UIntPtr size, uint freeType);

            [StructLayout(LayoutKind.Sequential)]
            public struct MemoryBasicInformation
            {
                public IntPtr BaseAddress;
                public IntPtr AllocationBase;
                public uint AllocationProtect;
                public ushort PartitionId;
                public UIntPtr RegionSize;
                public uint State;
                public uint Protect;
                public uint Type;
            }
        }

        /// <summary>Returns the game process, or null. The caller must dispose the Process.</summary>
        public static Process GetMGS4Process()
        {
            Process[] processes = Process.GetProcessesByName(PROCESS_NAME);
            if (processes.Length == 0) return null;
            for (int i = 1; i < processes.Length; i++) processes[i].Dispose();
            return processes[0];
        }

        public static Process GetGameProcess() => GetMGS4Process();

        /// <summary>Opens a caller-owned handle. Pair it with CloseGameProcess in a finally block.</summary>
        public static IntPtr OpenGameProcess(Process process)
        {
            if (process == null) return IntPtr.Zero;
            try
            {
                IntPtr handle = NativeMethods.OpenProcess(ProcessAccess, false, process.Id);
                if (handle == IntPtr.Zero)
                    LoggingManager.Instance.Log("OpenGameProcess failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                return handle;
            }
            catch (InvalidOperationException ex)
            {
                LoggingManager.Instance.Log("OpenGameProcess: " + ex.Message);
                return IntPtr.Zero;
            }
        }

        public static void CloseGameProcess(IntPtr processHandle)
        {
            if (processHandle != IntPtr.Zero) NativeMethods.CloseHandle(processHandle);
        }

        /// <summary>Current module base, queried fresh so a restarted game cannot leave a stale address.</summary>
        public static IntPtr PROCESS_BASE_ADDRESS
        {
            get
            {
                using (Process process = GetGameProcess())
                {
                    if (process == null) return IntPtr.Zero;
                    try { return process.MainModule?.BaseAddress ?? IntPtr.Zero; }
                    catch (Win32Exception) { return IntPtr.Zero; }
                    catch (InvalidOperationException) { return IntPtr.Zero; }
                }
            }
        }

        public static IntPtr AddOffset(IntPtr address, long offset) => new IntPtr(checked(address.ToInt64() + offset));

        #endregion

        #region Memory reading and writing

        public static bool ReadProcessMemory(IntPtr processHandle, IntPtr address, byte[] buffer,
            uint size, out int bytesRead)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (size > buffer.Length) throw new ArgumentOutOfRangeException(nameof(size));
            bytesRead = 0;
            if (processHandle == IntPtr.Zero) return false;
            bool success = NativeMethods.ReadProcessMemory(processHandle, address, buffer,
                new UIntPtr(size), out UIntPtr nativeBytesRead);
            bytesRead = checked((int)nativeBytesRead.ToUInt64());
            return success && bytesRead == size;
        }

        /// <summary>Returns all requested bytes, or null if the read fails or is incomplete.</summary>
        public static byte[] ReadMemoryBytes(IntPtr processHandle, IntPtr address, int bytesToRead)
        {
            if (bytesToRead < 0) throw new ArgumentOutOfRangeException(nameof(bytesToRead));
            if (processHandle == IntPtr.Zero) return null;
            byte[] buffer = new byte[bytesToRead];
            if (bytesToRead == 0) return buffer;
            return ReadProcessMemory(processHandle, address, buffer, (uint)bytesToRead, out _) ? buffer : null;
        }

        /// <summary>Writes an integer, float, double, or byte array. Returns true only for a complete write.</summary>
        public static bool WriteMemory<T>(IntPtr processHandle, IntPtr address, T value)
        {
            byte[] buffer = GetValueBytes(value);
            if (processHandle == IntPtr.Zero || address == IntPtr.Zero) return false;
            return NativeMethods.WriteProcessMemory(processHandle, address, buffer, new UIntPtr((uint)buffer.Length),
                out UIntPtr bytesWritten) && bytesWritten.ToUInt64() == (ulong)buffer.Length;
        }

        internal static byte[] GetValueBytes<T>(T value)
        {
            object boxed = value;
            if (boxed == null) throw new ArgumentNullException(nameof(value));
            if (boxed is byte[] bytes)
            {
                if (bytes.Length == 0) throw new ArgumentException("The byte array must not be empty.", nameof(value));
                return bytes;
            }
            if (boxed is byte unsignedByte) return new[] { unsignedByte };
            if (boxed is sbyte signedByte) return new[] { unchecked((byte)signedByte) };
            if (boxed is short int16) return BitConverter.GetBytes(int16);
            if (boxed is ushort uint16) return BitConverter.GetBytes(uint16);
            if (boxed is int int32) return BitConverter.GetBytes(int32);
            if (boxed is uint uint32) return BitConverter.GetBytes(uint32);
            if (boxed is long int64) return BitConverter.GetBytes(int64);
            if (boxed is ulong uint64) return BitConverter.GetBytes(uint64);
            if (boxed is float single) return BitConverter.GetBytes(single);
            if (boxed is double real) return BitConverter.GetBytes(real);
            throw new ArgumentException("Supported values are integer types, float, double, and byte[].", nameof(value));
        }

        public IntPtr ReadIntPtr(IntPtr processHandle, IntPtr address)
        {
            byte[] buffer = ReadMemoryBytes(processHandle, address, IntPtr.Size);
            if (buffer == null) return IntPtr.Zero;
            return IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(buffer, 0)) : new IntPtr(BitConverter.ToInt32(buffer, 0));
        }

        public static short SetSpecificBits(short currentValue, int startBit, int endBit, int valueToSet)
        {
            if (startBit < 0 || startBit > 15) throw new ArgumentOutOfRangeException(nameof(startBit));
            if (endBit < startBit || endBit > 15) throw new ArgumentOutOfRangeException(nameof(endBit));
            int valueMask = (1 << (endBit - startBit + 1)) - 1;
            if (valueToSet < 0 || valueToSet > valueMask) throw new ArgumentOutOfRangeException(nameof(valueToSet));
            int mask = valueMask << startBit;
            return unchecked((short)((currentValue & ~mask) | ((valueToSet << startBit) & mask)));
        }

        public static string FormatReadValue(byte[] buffer, DataType dataType)
        {
            int required = GetDataTypeSize(dataType);
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length < required) throw new ArgumentException("The buffer is too short for the selected data type.", nameof(buffer));
            object value;
            switch (dataType)
            {
                case DataType.UInt8: value = buffer[0]; break;
                case DataType.Int8: value = unchecked((sbyte)buffer[0]); break;
                case DataType.Int16: value = BitConverter.ToInt16(buffer, 0); break;
                case DataType.UInt16: value = BitConverter.ToUInt16(buffer, 0); break;
                case DataType.Int32: value = BitConverter.ToInt32(buffer, 0); break;
                case DataType.UInt32: value = BitConverter.ToUInt32(buffer, 0); break;
                case DataType.Int64: value = BitConverter.ToInt64(buffer, 0); break;
                case DataType.UInt64: value = BitConverter.ToUInt64(buffer, 0); break;
                case DataType.Float: return BitConverter.ToSingle(buffer, 0).ToString("R", CultureInfo.InvariantCulture);
                case DataType.Double: return BitConverter.ToDouble(buffer, 0).ToString("R", CultureInfo.InvariantCulture);
                case DataType.ByteArray: return BitConverter.ToString(buffer).Replace('-', ' ');
                default: throw new ArgumentOutOfRangeException(nameof(dataType));
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int GetDataTypeSize(DataType dataType)
        {
            switch (dataType)
            {
                case DataType.UInt8: case DataType.Int8: return 1;
                case DataType.Int16: case DataType.UInt16: return 2;
                case DataType.Int32: case DataType.UInt32: case DataType.Float: return 4;
                case DataType.Int64: case DataType.UInt64: case DataType.Double: return 8;
                case DataType.ByteArray: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(dataType));
            }
        }

        public static string FormatMemoryRead(byte[] buffer, int bytesToRead, string addressHex,
            string moduleOffset, DataType dataType)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (bytesToRead < 0 || bytesToRead > buffer.Length) throw new ArgumentOutOfRangeException(nameof(bytesToRead));
            byte[] selected = buffer.Take(bytesToRead).ToArray();
            string value = FormatReadValue(selected, dataType);
            string hex = dataType == DataType.ByteArray ? value :
                string.Concat(selected.Take(GetDataTypeSize(dataType)).Reverse().Select(b => b.ToString("X2")));
            string decimalValue = dataType == DataType.ByteArray ? string.Join(" ", selected) : value;
            return $"Address Offset: {moduleOffset}\nAddress in Hex: {addressHex}\n{dataType}\nValue in Decimal: {decimalValue}\nValue in Hex: {hex}";
        }

        public static string ReadMemoryValueAsString(IntPtr processHandle, IntPtr address, int bytesToRead, DataType dataType)
        {
            uint processId = NativeMethods.GetProcessId(processHandle);
            if (processId == 0) return "Error: Process handle is invalid.";
            try
            {
                using (Process process = Process.GetProcessById(checked((int)processId)))
                {
                    ProcessModule module = process.MainModule;
                    if (module == null) return "Error: Process module is unavailable.";
                    byte[] buffer = ReadMemoryBytes(processHandle, address, bytesToRead);
                    if (buffer == null) return "Error: Could not read memory.";
                    long offset = checked(address.ToInt64() - module.BaseAddress.ToInt64());
                    string moduleOffset = module.ModuleName + (offset < 0 ? "-" : "+") + Math.Abs(offset).ToString("X");
                    return FormatMemoryRead(buffer, bytesToRead, $"0x{address.ToInt64():X}",
                        moduleOffset, dataType);
                }
            }
            catch (ArgumentException ex) { return "Error: " + ex.Message; }
            catch (InvalidOperationException ex) { return "Error: " + ex.Message; }
            catch (Win32Exception ex) { return "Error: " + ex.Message; }
        }

        #endregion

        #region AOB scanning

        public IntPtr ScanMemory(IntPtr processHandle, IntPtr startAddress, long size, byte[] pattern, string mask)
        {
            return ScanMatches(processHandle, startAddress, size, pattern, mask, ScanBufferSize).FirstOrDefault();
        }

        public IntPtr ScanWideMemory(IntPtr processHandle, IntPtr startAddress, long size, byte[] pattern, string mask)
        {
            return ScanMatches(processHandle, startAddress, size, pattern, mask, WideScanBufferSize).FirstOrDefault();
        }

        public IntPtr ScanForStringMemory(IntPtr processHandle, IntPtr startAddress, long size, byte[] pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            return ScanMemory(processHandle, startAddress, size, pattern, new string('x', pattern.Length));
        }

        /// <summary>Finds every match, including overlapping matches. The range end is exclusive.</summary>
        public List<IntPtr> ScanForAllAobInstances(IntPtr processHandle, IntPtr baseAddress, long moduleSize, byte[] pattern, string mask)
        {
            return ScanMatches(processHandle, baseAddress, moduleSize, pattern, mask, ScanBufferSize).ToList();
        }

        public List<IntPtr> ScanForAllInstances(IntPtr processHandle, IntPtr startAddress, long size, byte[] pattern, string mask)
        {
            return ScanMatches(processHandle, startAddress, size, pattern, mask, WideScanBufferSize).ToList();
        }

        public bool IsMatch(byte[] buffer, int position, byte[] pattern, string mask)
        {
            string normalized = ValidatePattern(pattern, mask);
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (position < 0 || position > buffer.Length - pattern.Length) return false;
            return Matches(buffer, position, pattern, normalized);
        }

        public bool IsDirectMatch(byte[] buffer, int position, byte[] pattern, int bytesRead)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (pattern.Length == 0) throw new ArgumentException("The pattern must not be empty.", nameof(pattern));
            if (bytesRead < 0 || bytesRead > buffer.Length) throw new ArgumentOutOfRangeException(nameof(bytesRead));
            if (position < 0 || position > bytesRead - pattern.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
                if (buffer[position + i] != pattern[i]) return false;
            return true;
        }

        private static string ValidatePattern(byte[] pattern, string mask)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (pattern.Length == 0) throw new ArgumentException("The pattern must not be empty.", nameof(pattern));
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            string normalized = new string(mask.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (normalized.Length != pattern.Length || normalized.Any(c => c != 'x' && c != 'X' && c != '?'))
                throw new ArgumentException("Use one x or ? per pattern byte; whitespace is optional.", nameof(mask));
            return normalized;
        }

        private static bool Matches(byte[] buffer, int position, byte[] pattern, string mask)
        {
            for (int i = 0; i < pattern.Length; i++)
                if (mask[i] != '?' && buffer[position + i] != pattern[i]) return false;
            return true;
        }

        private static bool IsReadable(NativeMethods.MemoryBasicInformation region)
        {
            const uint MemCommit = 0x1000;
            const uint PageGuard = 0x100;
            const uint ReadableProtection = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;
            return region.State == MemCommit && (region.Protect & PageGuard) == 0 &&
                (region.Protect & ReadableProtection) != 0;
        }

        private static IEnumerable<IntPtr> ScanMatches(IntPtr processHandle, IntPtr startAddress, long size,
            byte[] pattern, string mask, int bufferSize)
        {
            string normalized = ValidatePattern(pattern, mask);
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            long cursor = startAddress.ToInt64();
            if (cursor < 0) throw new ArgumentOutOfRangeException(nameof(startAddress));
            long endAddress = checked(cursor + size);
            if (processHandle == IntPtr.Zero || size < pattern.Length) yield break;

            byte[] readBuffer = new byte[bufferSize];
            byte[] window = new byte[checked(bufferSize + pattern.Length - 1)];
            int carry = 0;
            UIntPtr informationSize = new UIntPtr((uint)Marshal.SizeOf(typeof(NativeMethods.MemoryBasicInformation)));

            while (cursor < endAddress)
            {
                if (NativeMethods.VirtualQueryEx(processHandle, new IntPtr(cursor), out var region, informationSize) == UIntPtr.Zero)
                    yield break;
                ulong nativeEnd = checked((ulong)region.BaseAddress.ToInt64() + region.RegionSize.ToUInt64());
                long regionEnd = (long)Math.Min((ulong)endAddress, nativeEnd);
                if (regionEnd <= cursor) yield break;
                if (!IsReadable(region))
                {
                    carry = 0;
                    cursor = regionEnd;
                    continue;
                }

                while (cursor < regionEnd)
                {
                    int requested = (int)Math.Min(bufferSize, regionEnd - cursor);
                    ReadProcessMemory(processHandle, new IntPtr(cursor), readBuffer, (uint)requested, out int bytesRead);
                    // Protection can change after VirtualQueryEx. Retry one page before skipping an unreadable page.
                    if (bytesRead == 0)
                    {
                        int pageBytes = (int)Math.Min(Environment.SystemPageSize - cursor % Environment.SystemPageSize, regionEnd - cursor);
                        if (requested > pageBytes)
                            ReadProcessMemory(processHandle, new IntPtr(cursor), readBuffer, (uint)pageBytes, out bytesRead);
                        if (bytesRead == 0)
                        {
                            carry = 0;
                            cursor += pageBytes;
                            continue;
                        }
                    }

                    Buffer.BlockCopy(readBuffer, 0, window, carry, bytesRead);
                    int available = carry + bytesRead;
                    long windowAddress = cursor - carry;
                    for (int i = 0; i <= available - pattern.Length; i++)
                        if (Matches(window, i, pattern, normalized)) yield return new IntPtr(windowAddress + i);

                    // Preserve only the incomplete suffix, including across adjacent readable regions.
                    carry = Math.Min(pattern.Length - 1, available);
                    Buffer.BlockCopy(window, available - carry, window, 0, carry);
                    cursor += bytesRead;
                }
            }
        }

        #endregion

        #region Named AOB lookup

        public IntPtr FindAob(string key)
        {
            return WithGameProcess((process, handle) => FindAob(process, handle, key));
        }

        /// <summary>Uses a caller-owned handle; never closes it.</summary>
        public IntPtr FindAob(IntPtr processHandle, string key)
        {
            uint id = NativeMethods.GetProcessId(processHandle);
            if (id == 0) return IntPtr.Zero;
            try
            {
                using (Process process = Process.GetProcessById(checked((int)id)))
                    return FindAob(process, processHandle, key);
            }
            catch (ArgumentException ex) { LoggingManager.Instance.Log("FindAob: " + ex.Message); return IntPtr.Zero; }
            catch (InvalidOperationException ex) { LoggingManager.Instance.Log("FindAob: " + ex.Message); return IntPtr.Zero; }
            catch (Win32Exception ex) { LoggingManager.Instance.Log("FindAob: " + ex.Message); return IntPtr.Zero; }
        }

        /// <summary>Scans the supplied process's main module with its caller-owned handle.</summary>
        public IntPtr FindAob(Process process, IntPtr processHandle, string key)
        {
            if (process == null || processHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(key) ||
                !AobManager.AOBs.TryGetValue(key, out var data)) return IntPtr.Zero;
            if (NativeMethods.GetProcessId(processHandle) != (uint)process.Id)
                throw new ArgumentException("The handle does not belong to the supplied process.", nameof(processHandle));
            var range = GetModuleRange(process, data.StartOffset, data.EndOffset);
            return ScanMemory(processHandle, range.Start, range.Size, data.Pattern, data.Mask);
        }

        public IntPtr FindDynamicAob(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !AobManager.AOBs.TryGetValue(key, out var data) ||
                !data.StartOffset.HasValue || !data.EndOffset.HasValue) return IntPtr.Zero;
            long size = checked(data.EndOffset.Value.ToInt64() - data.StartOffset.Value.ToInt64());
            return WithGameProcess((process, handle) =>
                ScanWideMemory(handle, data.StartOffset.Value, size, data.Pattern, data.Mask));
        }

        public IntPtr FindLastAob(string key, string aobName)
        {
            if (string.IsNullOrWhiteSpace(key) || !AobManager.AOBs.TryGetValue(key, out var data)) return IntPtr.Zero;
            return WithGameProcess((process, handle) =>
            {
                var range = GetModuleRange(process, data.StartOffset, data.EndOffset);
                // Keep only the last address instead of allocating a list of every match.
                IntPtr last = IntPtr.Zero;
                foreach (IntPtr match in ScanMatches(handle, range.Start, range.Size, data.Pattern, data.Mask, WideScanBufferSize))
                    last = match;
                if (last != IntPtr.Zero)
                    LoggingManager.Instance.Log($"Last instance of {aobName ?? key} found at 0x{last.ToInt64():X}.");
                return last;
            });
        }

        private static (IntPtr Start, long Size) GetModuleRange(Process process, IntPtr? startOffset, IntPtr? endOffset)
        {
            ProcessModule module = process.MainModule;
            if (module == null) throw new InvalidOperationException("Process module is unavailable.");
            long start = startOffset?.ToInt64() ?? 0;
            long end = endOffset?.ToInt64() ?? module.ModuleMemorySize;
            if (start < 0 || end < start || end > module.ModuleMemorySize)
                throw new ArgumentOutOfRangeException(nameof(startOffset), "AOB bounds must lie within the main module; the end is exclusive.");
            return (AddOffset(module.BaseAddress, start), end - start);
        }

        private static IntPtr WithGameProcess(Func<Process, IntPtr, IntPtr> action)
        {
            using (Process process = GetGameProcess())
            {
                if (process == null) return IntPtr.Zero;
                IntPtr handle = OpenGameProcess(process);
                if (handle == IntPtr.Zero) return IntPtr.Zero;
                try { return action(process, handle); }
                catch (Win32Exception ex) { LoggingManager.Instance.Log("AOB lookup: " + ex.Message); return IntPtr.Zero; }
                catch (InvalidOperationException ex) { LoggingManager.Instance.Log("AOB lookup: " + ex.Message); return IntPtr.Zero; }
                finally { CloseGameProcess(handle); }
            }
        }

        #endregion
    }
}
