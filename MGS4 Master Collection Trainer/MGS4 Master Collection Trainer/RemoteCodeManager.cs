using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Native support for small, caller-owned x64 code hooks.</summary>
    internal static class RemoteCodeManager
    {
        private const uint MemCommit = 0x1000;
        private const uint MemReserve = 0x2000;
        private const uint MemRelease = 0x8000;
        private const uint MemFree = 0x10000;
        private const uint PageReadWrite = 0x04;
        private const uint PageExecuteReadWrite = 0x40;

        internal static IntPtr AllocateNear(IntPtr processHandle, IntPtr hookAddress, int size)
        {
            ValidateRange(processHandle, hookAddress, size);
            NativeMethods.GetSystemInfo(out SystemInformation system);
            long granularity = system.AllocationGranularity;
            long roundedSize = AlignUp(size, system.PageSize);
            long hook = hookAddress.ToInt64();
            // Leave room for both the outgoing jump and the return from the end of the cave.
            long reach = int.MaxValue - roundedSize - 16;
            if (reach <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            long minimum = Math.Max(system.MinimumApplicationAddress.ToInt64(), hook - reach);
            long maximum = Math.Min(system.MaximumApplicationAddress.ToInt64() - roundedSize,
                checked(hook + reach));
            long cursor = AlignUp(minimum, granularity);
            UIntPtr informationSize = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
            int lastError = 0;

            while (cursor <= maximum)
            {
                if (MemoryManager.NativeMethods.VirtualQueryEx(processHandle, new IntPtr(cursor),
                    out MemoryManager.NativeMethods.MemoryBasicInformation region, informationSize) == UIntPtr.Zero)
                    throw NativeError("VirtualQueryEx could not inspect space near the player hook.");

                long regionEnd = RegionEnd(region);
                if (regionEnd <= cursor) throw new InvalidOperationException("VirtualQueryEx returned an invalid region.");
                if (region.State == MemFree)
                {
                    long candidate = AlignUp(Math.Max(cursor, region.BaseAddress.ToInt64()), granularity);
                    long lastCandidate = Math.Min(maximum, regionEnd - roundedSize);
                    while (candidate <= lastCandidate)
                    {
                        IntPtr allocation = MemoryManager.NativeMethods.VirtualAllocEx(processHandle,
                            new IntPtr(candidate), new UIntPtr((uint)size), MemReserve | MemCommit, PageReadWrite);
                        if (allocation != IntPtr.Zero) return allocation;
                        lastError = Marshal.GetLastWin32Error();
                        // A different allocator can claim a free region between query and allocation.
                        candidate = checked(candidate + granularity);
                    }
                }
                cursor = AlignUp(regionEnd, granularity);
            }

            throw new InvalidOperationException("No allocation within a signed 32-bit jump of the player hook is available.",
                lastError == 0 ? null : new Win32Exception(lastError));
        }

        internal static void MakeExecutable(IntPtr processHandle, IntPtr address, int size)
        {
            ValidateRange(processHandle, address, size);
            // The captured pointer shares this allocation, so its page must remain writable.
            if (!NativeMethods.VirtualProtectEx(processHandle, address, new UIntPtr((uint)size),
                PageExecuteReadWrite, out _))
                throw NativeError("Could not make the player hook allocation executable.");
        }

        internal static void Flush(IntPtr processHandle, IntPtr address, int size)
        {
            ValidateRange(processHandle, address, size);
            if (!NativeMethods.FlushInstructionCache(processHandle, address, new UIntPtr((uint)size)))
                throw NativeError("Could not flush the game's instruction cache.");
        }

        internal static bool Free(IntPtr processHandle, IntPtr address)
        {
            return address == IntPtr.Zero || MemoryManager.NativeMethods.VirtualFreeEx(processHandle,
                address, UIntPtr.Zero, MemRelease);
        }

        internal static ThreadPauseManager SuspendThreads(Process process)
        {
            return ThreadPauseManager.Acquire(process);
        }

        /// <summary>
        /// Replaces exactly the expected bytes and restores each region's original protection.
        /// The caller must keep the target threads suspended throughout this operation.
        /// A failed write is rolled back before the caller can resume those threads.
        /// </summary>
        internal static void ReplaceCode(IntPtr processHandle, IntPtr address, byte[] expected, byte[] replacement)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (expected.Length != replacement.Length)
                throw new ArgumentException("A code replacement must have the same length as the original instruction bytes.");
            ValidateRange(processHandle, address, expected.Length);
            VerifyBytes(processHandle, address, expected);

            List<ProtectionRange> ranges = GetProtectionRanges(processHandle, address, expected.Length);
            List<Exception> failures = new List<Exception>();
            bool writeAttempted = false;
            try
            {
                foreach (ProtectionRange range in ranges)
                {
                    if (!NativeMethods.VirtualProtectEx(processHandle, range.Address, range.Size,
                        PageExecuteReadWrite, out uint originalProtection))
                        throw NativeError("Could not make the instruction's pages writable.");
                    range.OriginalProtection = originalProtection;
                    range.Changed = true;
                }
                writeAttempted = true;
                WriteExact(processHandle, address, replacement);
                Flush(processHandle, address, replacement.Length);
                VerifyBytes(processHandle, address, replacement);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
            finally
            {
                RestoreProtection(processHandle, ranges, failures);
            }

            if (failures.Count == 0) return;
            bool originalRestored = !writeAttempted;
            if (writeAttempted)
            {
                try
                {
                    foreach (ProtectionRange range in ranges)
                    {
                        if (!range.Changed) continue;
                        if (!NativeMethods.VirtualProtectEx(processHandle, range.Address, range.Size,
                            PageExecuteReadWrite, out _))
                            failures.Add(NativeError("Could not make a page writable during code rollback."));
                    }
                    WriteExact(processHandle, address, expected);
                    Flush(processHandle, address, expected.Length);
                    VerifyBytes(processHandle, address, expected);
                    originalRestored = true;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
                finally
                {
                    RestoreProtection(processHandle, ranges, failures);
                }
            }

            throw new RemoteCodePatchException(originalRestored
                ? "The instruction patch failed; the original bytes are intact. See the inner errors for any protection failures."
                : "The instruction patch and rollback failed. The instruction bytes are uncertain; retain the hook allocation.",
                originalRestored, new AggregateException(failures));
        }

        private static List<ProtectionRange> GetProtectionRanges(IntPtr processHandle, IntPtr address, int length)
        {
            NativeMethods.GetSystemInfo(out SystemInformation system);
            long cursor = address.ToInt64() / system.PageSize * system.PageSize;
            long end = AlignUp(checked(address.ToInt64() + length), system.PageSize);
            UIntPtr informationSize = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
            List<ProtectionRange> ranges = new List<ProtectionRange>();
            while (cursor < end)
            {
                if (MemoryManager.NativeMethods.VirtualQueryEx(processHandle, new IntPtr(cursor),
                    out MemoryManager.NativeMethods.MemoryBasicInformation region, informationSize) == UIntPtr.Zero)
                    throw NativeError("Could not inspect the instruction's memory protection.");
                long next = Math.Min(end, RegionEnd(region));
                if (region.State != MemCommit || next <= cursor)
                    throw new InvalidOperationException("The instruction does not lie entirely in committed memory.");
                ranges.Add(new ProtectionRange { Address = new IntPtr(cursor), Size = new UIntPtr((ulong)(next - cursor)) });
                cursor = next;
            }
            return ranges;
        }

        private static void RestoreProtection(IntPtr processHandle, List<ProtectionRange> ranges, List<Exception> failures)
        {
            for (int index = ranges.Count - 1; index >= 0; index--)
            {
                ProtectionRange range = ranges[index];
                if (!range.Changed) continue;
                if (!NativeMethods.VirtualProtectEx(processHandle, range.Address, range.Size,
                    range.OriginalProtection, out _))
                    failures.Add(NativeError("Could not restore an instruction page's original protection."));
            }
        }

        private static void WriteExact(IntPtr processHandle, IntPtr address, byte[] bytes)
        {
            if (!MemoryManager.NativeMethods.WriteProcessMemory(processHandle, address, bytes,
                new UIntPtr((uint)bytes.Length), out UIntPtr bytesWritten) || bytesWritten.ToUInt64() != (ulong)bytes.Length)
                throw NativeError("The instruction write failed or was incomplete.");
        }

        private static void VerifyBytes(IntPtr processHandle, IntPtr address, byte[] expected)
        {
            byte[] actual = MemoryManager.ReadMemoryBytes(processHandle, address, expected.Length);
            if (actual == null) throw NativeError("Could not verify the instruction bytes.");
            for (int i = 0; i < expected.Length; i++)
                if (actual[i] != expected[i])
                    throw new InvalidOperationException("The instruction bytes do not match the expected hook state.");
        }

        private static long RegionEnd(MemoryManager.NativeMethods.MemoryBasicInformation region)
        {
            long start = region.BaseAddress.ToInt64();
            ulong length = region.RegionSize.ToUInt64();
            return length > (ulong)(long.MaxValue - start) ? long.MaxValue : start + (long)length;
        }

        private static long AlignUp(long value, long alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }

        private static void ValidateRange(IntPtr processHandle, IntPtr address, int size)
        {
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Code hooks require the x64 trainer build.");
            if (processHandle == IntPtr.Zero) throw new ArgumentException("A process handle is required.", nameof(processHandle));
            if (address.ToInt64() <= 0) throw new ArgumentOutOfRangeException(nameof(address));
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (address.ToInt64() > long.MaxValue - size)
                throw new ArgumentOutOfRangeException(nameof(size), "The address range overflows.");
        }

        private static Win32Exception NativeError(string message)
        {
            int error = Marshal.GetLastWin32Error();
            return new Win32Exception(error, message + " " + new Win32Exception(error).Message);
        }

        private sealed class ProtectionRange
        {
            internal IntPtr Address;
            internal UIntPtr Size;
            internal uint OriginalProtection;
            internal bool Changed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemInformation
        {
            internal ushort ProcessorArchitecture;
            internal ushort Reserved;
            internal uint PageSize;
            internal IntPtr MinimumApplicationAddress;
            internal IntPtr MaximumApplicationAddress;
            internal UIntPtr ActiveProcessorMask;
            internal uint NumberOfProcessors;
            internal uint ProcessorType;
            internal uint AllocationGranularity;
            internal ushort ProcessorLevel;
            internal ushort ProcessorRevision;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern void GetSystemInfo(out SystemInformation information);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool VirtualProtectEx(IntPtr processHandle, IntPtr address, UIntPtr size,
                uint newProtection, out uint oldProtection);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool FlushInstructionCache(IntPtr processHandle, IntPtr address, UIntPtr size);
        }
    }

    internal sealed class RemoteCodePatchException : InvalidOperationException
    {
        internal RemoteCodePatchException(string message, bool originalBytesRestored, Exception innerException)
            : base(message, innerException)
        {
            OriginalBytesRestored = originalBytesRestored;
        }

        internal bool OriginalBytesRestored { get; }
    }
}
