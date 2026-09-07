using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Scoped game-memory reads and verified writes within the owning process session.</summary>
    internal sealed class MemoryTransactionManager
    {
        internal Func<IntPtr, int, byte[]> ReadBytes;
        internal Func<IntPtr, byte[], bool> WriteBytes;
        internal Func<string, IntPtr> ResolveSymbol;
        internal Func<string, string, IntPtr> FindUnique;
        internal Func<IDisposable> Pause;
        internal Action<IntPtr, int> RequireWritable;
        internal int ProcessId;
        internal DateTime StartTime;

        internal string SessionIdentity => GetSessionIdentity(ProcessId, StartTime);

        internal static string GetSessionIdentity(int processId, DateTime startTime) =>
            processId.ToString(CultureInfo.InvariantCulture) + ":" + startTime.Ticks.ToString(CultureInfo.InvariantCulture);

        internal static MemoryTransactionManager ForProcess(Process process, IntPtr handle, Func<string, IntPtr> resolver)
        {
            return new MemoryTransactionManager
            {
                ProcessId = process.Id,
                StartTime = process.StartTime,
                ReadBytes = (address, count) => MemoryManager.ReadMemoryBytes(handle, address, count),
                WriteBytes = (address, bytes) => MemoryManager.WriteMemory(handle, address, bytes),
                ResolveSymbol = resolver,
                Pause = () => RemoteCodeManager.SuspendThreads(process),
                RequireWritable = (address, count) => CheckPages(handle, address, count, false),
                FindUnique = (name, pattern) =>
                {
                    ProcessModule module = process.MainModule;
                    if (module == null) throw new InvalidOperationException("The mgs4.exe module is unavailable.");
                    var signature = new SignatureDefinition(name, name, pattern);
                    List<IntPtr> matches = AobRecoveryManager.ScanExecutable(handle, module.BaseAddress,
                        module.ModuleMemorySize, signature.Pattern, signature.Mask).Where(address =>
                        {
                            try { CheckPages(handle, address, signature.Pattern.Length, true); return true; }
                            catch (InvalidOperationException) { return false; }
                        }).ToList();
                    if (matches.Count != 1)
                        throw new InvalidOperationException(name + " must match exactly once in executable module memory; found " + matches.Count + ".");
                    return matches[0];
                }
            };
        }

        internal byte[] Read(IntPtr address, int count)
        {
            if (address.ToInt64() <= 0 || count <= 0 || address.ToInt64() > long.MaxValue - count)
                throw new InvalidOperationException("The requested memory range is invalid.");
            byte[] bytes = ReadBytes(address, count);
            if (bytes == null || bytes.Length != count)
                throw new InvalidOperationException("Could not read " + count + " bytes at 0x" + address.ToInt64().ToString("X") + ". No unread values are treated as zero.");
            return bytes;
        }

        internal ushort U16(IntPtr address) => BitConverter.ToUInt16(Read(address, 2), 0);
        internal uint U32(IntPtr address) => BitConverter.ToUInt32(Read(address, 4), 0);
        internal int I32(IntPtr address) => BitConverter.ToInt32(Read(address, 4), 0);
        internal ulong U64(IntPtr address) => BitConverter.ToUInt64(Read(address, 8), 0);
        internal IntPtr Pointer(IntPtr address) => new IntPtr(unchecked((long)U64(address)));
        internal IntPtr Relative(IntPtr displacement) => Add(displacement, checked(4L + I32(displacement)));
        internal static IntPtr Add(IntPtr address, long offset) => new IntPtr(checked(address.ToInt64() + offset));

        internal IntPtr Symbol(string name)
        {
            IntPtr address = ResolveSymbol(name);
            if (address == IntPtr.Zero) throw new InvalidOperationException("The " + name + " source is unavailable. Enable its required capture first.");
            return address;
        }

        internal IntPtr Player()
        {
            IntPtr address = Pointer(Symbol("pPlayer"));
            if (address.ToInt64() <= 0)
                throw new InvalidOperationException("Waiting for the player capture. Load into a level, then retry.");
            return address;
        }

        /// <summary>Preflights every write before changing anything; rolls all attempted writes back on a failure.</summary>
        internal int Commit(IReadOnlyList<MemoryValueEdit> edits)
        {
            if (edits.Count == 0) return 0;
            using (Pause())
                return CommitWhilePaused(edits);
        }

        /// <summary>Commits edits while the caller holds the process pause used to capture their original values.</summary>
        internal int CommitWhilePaused(IReadOnlyList<MemoryValueEdit> edits)
        {
            foreach (MemoryValueEdit edit in edits)
            {
                RequireWritable(edit.Address, edit.Before.Length);
                if (!Read(edit.Address, edit.Before.Length).SequenceEqual(edit.Before))
                    throw new InvalidOperationException("A value changed during preparation. No writes were applied; retry the action.");
            }
            var attempted = new List<MemoryValueEdit>();
            try
            {
                foreach (MemoryValueEdit edit in edits)
                {
                    attempted.Add(edit); // a failed native write can still be partial
                    if (!WriteBytes(edit.Address, edit.After) || !Read(edit.Address, edit.After.Length).SequenceEqual(edit.After))
                        throw new InvalidOperationException("A value write failed or could not be verified.");
                }
            }
            catch (Exception failure)
            {
                var failures = new List<Exception> { failure };
                foreach (MemoryValueEdit edit in attempted.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (!WriteBytes(edit.Address, edit.Before) || !Read(edit.Address, edit.Before.Length).SequenceEqual(edit.Before))
                            throw new InvalidOperationException("Could not restore the value at 0x" + edit.Address.ToInt64().ToString("X") + ".");
                    }
                    catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                }
                throw new AggregateException(failures.Count == 1 ? "The action failed; all attempted writes were restored."
                    : "The action failed and some original values could not be restored.", failures);
            }
            return edits.Count;
        }

        private static void CheckPages(IntPtr handle, IntPtr address, int count, bool executableReadOnly)
        {
            long cursor = address.ToInt64();
            long end = checked(cursor + count);
            if (cursor <= 0 || count <= 0) throw new InvalidOperationException("Invalid memory range.");
            UIntPtr size = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
            while (cursor < end)
            {
                if (MemoryManager.NativeMethods.VirtualQueryEx(handle, new IntPtr(cursor), out var page, size) == UIntPtr.Zero ||
                    page.State != 0x1000 || (page.Protect & (0x100 | 0x01)) != 0)
                    throw new InvalidOperationException("A required memory page is inaccessible.");
                bool suitable = executableReadOnly ? (page.Protect & (0x10 | 0x20)) != 0 && (page.Protect & (0x04 | 0x08 | 0x40 | 0x80)) == 0
                    : (page.Protect & (0x04 | 0x08 | 0x40 | 0x80)) != 0;
                if (!suitable) throw new InvalidOperationException(executableReadOnly ? "Pattern is outside executable read-only memory."
                    : "A target field is not writable. No page protections were changed.");
                long next = checked(page.BaseAddress.ToInt64() + (long)page.RegionSize.ToUInt64());
                if (next <= cursor) throw new InvalidOperationException("Invalid memory-page bounds.");
                cursor = next;
            }
        }
    }

    internal sealed class MemoryValueEdit
    {
        internal MemoryValueEdit(IntPtr address, byte[] before, byte[] after)
        {
            if (before == null || after == null || before.Length == 0 || before.Length != after.Length)
                throw new ArgumentException("A value edit must preserve its field width.");
            Address = address; Before = before; After = after;
        }
        internal IntPtr Address { get; }
        internal byte[] Before { get; }
        internal byte[] After { get; }
    }
}
