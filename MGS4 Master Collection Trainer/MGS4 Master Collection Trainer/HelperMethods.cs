using System;
using System.Diagnostics;
using static MGS4_Master_Collection_Trainer.Constants;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Reusable AOB-relative, module-pointer, and captured-player memory operations.</summary>
    public sealed class HelperMethods
    {
        public static HelperMethods Instance { get; } = new HelperMethods();

        private HelperMethods()
        {
        }

        /// <summary>
        /// Opens the game process. The caller owns the returned handle and must call
        /// MemoryManager.CloseGameProcess in a finally block when finished.
        /// </summary>
        public IntPtr GetProcessHandle()
        {
            using (Process process = MemoryManager.GetGameProcess())
            {
                return process == null ? IntPtr.Zero : MemoryManager.OpenGameProcess(process);
            }
        }

        /// <summary>
        /// Finds an AOB in the supplied process and subtracts offset, preserving Delta's calling convention.
        /// The supplied handle remains owned by the caller.
        /// </summary>
        public IntPtr GetTargetAddress(IntPtr processHandle, string patternName, int offset)
        {
            IntPtr address = MemoryManager.Instance.FindAob(processHandle, patternName);
            return OffsetAddress(address, -(long)offset);
        }

        /// <summary>Returns true only when all requested bytes were read and match.</summary>
        public bool VerifyMemory(IntPtr processHandle, IntPtr address, byte[] expectedBytes)
        {
            if (expectedBytes == null || expectedBytes.Length == 0) return false;

            byte[] actualBytes = MemoryManager.ReadMemoryBytes(processHandle, address, expectedBytes.Length);
            if (actualBytes == null || actualBytes.Length != expectedBytes.Length) return false;

            for (int i = 0; i < expectedBytes.Length; i++)
            {
                if (actualBytes[i] != expectedBytes[i]) return false;
            }

            return true;
        }

        /// <summary>
        /// Reads and formats memory relative to an AOB. True adds offset; false subtracts offset.
        /// </summary>
        public string ReadMemoryValue(string aobKey, int offset, bool forwardInMemory, int bytesToRead, DataType dataType)
        {
            if (bytesToRead <= 0) return "Error: bytesToRead must be positive.";

            return WithGameProcess((process, handle) =>
            {
                IntPtr address = FindRelativeAddress(process, handle, aobKey, offset, forwardInMemory);
                if (address == IntPtr.Zero) return "Error: AOB pattern or target address not found.";

                byte[] buffer = MemoryManager.ReadMemoryBytes(handle, address, bytesToRead);
                if (buffer == null || buffer.Length != bytesToRead) return "Error: Could not read memory.";

                long moduleOffset = checked(address.ToInt64() - process.MainModule.BaseAddress.ToInt64());
                string offsetText = Constants.PROCESS_MODULE_NAME + (moduleOffset < 0 ? "-" : "+")
                    + Math.Abs(moduleOffset).ToString("X");
                return MemoryManager.FormatMemoryRead(buffer, bytesToRead, "0x" + address.ToInt64().ToString("X"),
                    offsetText, dataType);
            }, "Error: Could not access process or read memory.");
        }

        /// <summary>Reads bytes relative to an AOB. Returns null if scanning or reading fails.</summary>
        public byte[] ReadMemoryBytes(string aobKey, int offset, bool forwardInMemory, int bytesToRead)
        {
            if (bytesToRead <= 0) return null;

            return WithGameProcess((process, handle) =>
            {
                IntPtr address = FindRelativeAddress(process, handle, aobKey, offset, forwardInMemory);
                return address == IntPtr.Zero ? null : MemoryManager.ReadMemoryBytes(handle, address, bytesToRead);
            }, (byte[])null);
        }

        /// <summary>Writes a supported primitive or byte array relative to an AOB.</summary>
        public bool WriteMemoryValue<T>(string aobKey, int offset, bool forwardInMemory, T value)
        {
            return WithGameProcess((process, handle) =>
            {
                IntPtr address = FindRelativeAddress(process, handle, aobKey, offset, forwardInMemory);
                bool written = address != IntPtr.Zero && MemoryManager.WriteMemory(handle, address, value);
                LoggingManager.Instance.Log(written
                    ? aobKey + " written at 0x" + address.ToInt64().ToString("X") + "."
                    : "Could not find or write " + aobKey + ".");
                return written;
            }, false);
        }

        /// <summary>Dereferences the pointer at the game's module base plus baseOffset, then writes at valueOffset.</summary>
        public bool WriteToPointer<T>(int baseOffset, int valueOffset, T value)
        {
            return WithGameProcess((process, handle) =>
            {
                IntPtr address = ResolvePointerAddress(process, handle, baseOffset, valueOffset);
                return address != IntPtr.Zero && MemoryManager.WriteMemory(handle, address, value);
            }, false);
        }

        /// <summary>Dereferences the pointer at the game's module base plus baseOffset, then reads at valueOffset.</summary>
        public byte[] ReadPointerBytes(int baseOffset, int valueOffset, int bytesToRead)
        {
            if (bytesToRead <= 0) return null;

            return WithGameProcess((process, handle) =>
            {
                IntPtr address = ResolvePointerAddress(process, handle, baseOffset, valueOffset);
                return address == IntPtr.Zero ? null : MemoryManager.ReadMemoryBytes(handle, address, bytesToRead);
            }, (byte[])null);
        }

        /// <summary>Returns the last player address captured by the active hook, or zero if unavailable.</summary>
        public IntPtr GetPlayerAddress()
        {
            return PlayerHookManager.Instance.GetPlayerAddress();
        }

        /// <summary>Reads at the captured player address plus fieldOffset; returns null if unavailable.</summary>
        public byte[] ReadPlayerBytes(int fieldOffset, int count)
        {
            return PlayerHookManager.Instance.ReadPlayerBytes(fieldOffset, count);
        }

        /// <summary>Writes a supported primitive or byte array at the captured player address plus fieldOffset.</summary>
        public bool WritePlayerValue<T>(int fieldOffset, T value)
        {
            return PlayerHookManager.Instance.WritePlayerValue(fieldOffset, value);
        }

        /// <summary>Returns a running process's module base; throws if it cannot be found or inspected.</summary>
        public IntPtr GetBaseAddress(string processName = Constants.PROCESS_NAME)
        {
            if (string.IsNullOrWhiteSpace(processName)) throw new ArgumentException("A process name is required.", nameof(processName));
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName = processName.Substring(0, processName.Length - 4);

            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length == 0)
                    throw new ArgumentException("Process '" + processName + "' not found. Make sure it is running.", nameof(processName));

                ProcessModule module = processes[0].MainModule;
                if (module == null) throw new InvalidOperationException("The process has no accessible main module.");
                return module.BaseAddress;
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }

        /// <summary>Reads only the value at AOB minus offset, preserving Delta's subtract-offset convention.</summary>
        public string ReadMemoryValueOnlyAsString(string aobKey, int offset, int bytesToRead, DataType dataType)
        {
            if (bytesToRead <= 0) return "Error: bytesToRead must be positive.";

            return WithGameProcess((process, handle) =>
            {
                IntPtr address = FindRelativeAddress(process, handle, aobKey, offset, false);
                if (address == IntPtr.Zero) return "Error: AOB pattern or target address not found.";

                byte[] buffer = MemoryManager.ReadMemoryBytes(handle, address, bytesToRead);
                return buffer == null || buffer.Length != bytesToRead
                    ? "Error: Could not read memory."
                    : FormatReadValue(buffer, dataType);
            }, "Error: Could not access process or read memory.");
        }

        /// <summary>Formats a value without an address or any dependency on UI controls.</summary>
        public string FormatReadValue(byte[] buffer, DataType dataType)
        {
            return MemoryManager.FormatReadValue(buffer, dataType);
        }

        private static IntPtr FindRelativeAddress(Process process, IntPtr handle, string aobKey, int offset, bool forwardInMemory)
        {
            IntPtr address = MemoryManager.Instance.FindAob(process, handle, aobKey);
            return OffsetAddress(address, forwardInMemory ? (long)offset : -(long)offset);
        }

        private static IntPtr ResolvePointerAddress(Process process, IntPtr handle, int baseOffset, int valueOffset)
        {
            ProcessModule module = process.MainModule;
            if (module == null) return IntPtr.Zero;

            IntPtr pointerAddress = OffsetAddress(module.BaseAddress, baseOffset);
            if (pointerAddress == IntPtr.Zero) return IntPtr.Zero;

            IntPtr pointer = MemoryManager.Instance.ReadIntPtr(handle, pointerAddress);
            return OffsetAddress(pointer, valueOffset);
        }

        private static IntPtr OffsetAddress(IntPtr address, long offset)
        {
            if (address == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                long result = checked(address.ToInt64() + offset);
                return result > 0 ? new IntPtr(result) : IntPtr.Zero;
            }
            catch (OverflowException)
            {
                return IntPtr.Zero;
            }
        }

        // Each high-level operation keeps its scan and memory access attached to the same process.
        private static T WithGameProcess<T>(Func<Process, IntPtr, T> action, T failureValue)
        {
            try
            {
                using (Process process = MemoryManager.GetGameProcess())
                {
                    if (process == null) return failureValue;
                    IntPtr handle = MemoryManager.OpenGameProcess(process);
                    if (handle == IntPtr.Zero) return failureValue;

                    try
                    {
                        return action(process, handle);
                    }
                    finally
                    {
                        MemoryManager.CloseGameProcess(handle);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingManager.Instance.Log("Memory helper failed: " + ex.Message);
                return failureValue;
            }
        }
    }
}
