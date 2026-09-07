using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>
    /// Briefly pauses an external x64 process for instruction patching. Every successful
    /// SuspendThread is paired with exactly one successful ResumeThread, preserving preexisting pauses.
    /// Keep this scope limited to native memory operations: no waits, logging, or target module enumeration.
    /// </summary>
    internal sealed class ThreadPauseManager : IDisposable
    {
        private const uint ThreadAccess = 0x0002 | 0x0008 | 0x0800 | 0x00100000;
        private const uint InvalidSuspendCount = uint.MaxValue;
        private const uint WaitObject0 = 0;
        private const int ErrorNoMoreFiles = 18;
        private const int ErrorInvalidParameter = 87;
        private const int ContextSize = 1232;
        private const int ContextFlagsOffset = 48;
        private const int InstructionPointerOffset = 248;
        private const int ContextAmd64Control = 0x00100001;
        private readonly Dictionary<uint, PausedThread> threads = new Dictionary<uint, PausedThread>();
        private bool disposed;

        private ThreadPauseManager() { }

        internal static ThreadPauseManager Acquire(Process process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Thread inspection requires the x64 trainer build.");
            uint processId = checked((uint)process.Id);
            if (processId == NativeMethods.GetCurrentProcessId())
                throw new InvalidOperationException("The trainer must never suspend its own process.");
            ValidateArchitecture(processId);

            ThreadPauseManager pause = new ThreadPauseManager();
            try
            {
                // A running thread can create a thread while the initial snapshot is being suspended.
                // Re-snapshot until every thread in a complete later snapshot is already paused.
                // External debuggers creating/resuming threads concurrently are not supported.
                for (int pass = 0; pass < 8; pass++)
                {
                    HashSet<uint> currentThreads = SnapshotThreads(processId);
                    if (currentThreads.Count == 0)
                        throw new InvalidOperationException("The game has no accessible running threads.");
                    bool added = false;
                    bool racedWithExit = false;
                    foreach (uint threadId in currentThreads)
                    {
                        if (pause.threads.TryGetValue(threadId, out PausedThread existing))
                        {
                            if (NativeMethods.WaitForSingleObject(existing.Handle, 0) != WaitObject0) continue;
                            // Terminated threads need no resume and their identifiers can be reused.
                            MemoryManager.NativeMethods.CloseHandle(existing.Handle);
                            pause.threads.Remove(threadId);
                        }

                        IntPtr handle = NativeMethods.OpenThread(ThreadAccess, false, threadId);
                        if (handle == IntPtr.Zero)
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error == ErrorInvalidParameter)
                            {
                                racedWithExit = true;
                                continue;
                            }
                            throw NativeError(error, "Could not open a game thread for a safe instruction patch.");
                        }

                        bool ownedByPause = false;
                        try
                        {
                            uint owner = NativeMethods.GetProcessIdOfThread(handle);
                            if (owner == 0) throw NativeError(Marshal.GetLastWin32Error(), "Could not verify a game thread's owner.");
                            if (owner != processId)
                            {
                                racedWithExit = true;
                                continue;
                            }
                            if (NativeMethods.SuspendThread(handle) == InvalidSuspendCount)
                            {
                                int error = Marshal.GetLastWin32Error();
                                if (NativeMethods.WaitForSingleObject(handle, 0) == WaitObject0)
                                {
                                    racedWithExit = true;
                                    continue;
                                }
                                throw NativeError(error, "Could not suspend a game thread for a safe instruction patch.");
                            }

                            PausedThread thread = new PausedThread { Handle = handle };
                            pause.threads.Add(threadId, thread);
                            ownedByPause = true;
                            added = true;
                            // SuspendThread is asynchronous. Reading the context also ensures the
                            // kernel has actually stopped the thread before any code can be changed.
                            thread.InstructionPointer = ReadInstructionPointer(handle);
                        }
                        finally
                        {
                            if (!ownedByPause) MemoryManager.NativeMethods.CloseHandle(handle);
                        }
                    }
                    if (!added && !racedWithExit) return pause;
                }
                throw new InvalidOperationException("The game's thread list did not stabilize; no instructions were changed.");
            }
            catch (Exception failure)
            {
                try { pause.Dispose(); }
                catch (Exception resumeFailure)
                {
                    throw new AggregateException("Thread suspension failed, and one or more threads could not be resumed.",
                        failure, resumeFailure);
                }
                throw;
            }
        }

        internal bool HasInstructionPointerInRange(IntPtr start, int length)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ThreadPauseManager));
            if (start.ToInt64() < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            ulong first = (ulong)start.ToInt64();
            ulong end = checked(first + (ulong)length);
            foreach (PausedThread thread in threads.Values)
                if (thread.InstructionPointer >= first && thread.InstructionPointer < end) return true;
            return false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            List<Exception> failures = new List<Exception>();
            foreach (PausedThread thread in threads.Values)
            {
                try
                {
                    if (NativeMethods.WaitForSingleObject(thread.Handle, 0) == WaitObject0) continue;
                    bool resumed = false;
                    int error = 0;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (NativeMethods.ResumeThread(thread.Handle) != InvalidSuspendCount)
                        {
                            resumed = true;
                            break;
                        }
                        error = Marshal.GetLastWin32Error();
                        if (NativeMethods.WaitForSingleObject(thread.Handle, 0) == WaitObject0)
                        {
                            resumed = true;
                            break;
                        }
                    }
                    if (!resumed) failures.Add(NativeError(error, "Could not resume a paused game thread."));
                }
                finally
                {
                    MemoryManager.NativeMethods.CloseHandle(thread.Handle);
                }
            }
            threads.Clear();
            if (failures.Count != 0)
                throw new AggregateException("One or more game threads could not be resumed.", failures);
        }

        private static ulong ReadInstructionPointer(IntPtr threadHandle)
        {
            // AMD64 CONTEXT has 16-byte alignment, flags at byte 48, and RIP at byte 248.
            // Allocate the full native size even though only CONTEXT_CONTROL is requested.
            IntPtr storage = Marshal.AllocHGlobal(ContextSize + 15);
            try
            {
                IntPtr context = new IntPtr((storage.ToInt64() + 15) & ~15L);
                Marshal.Copy(new byte[ContextSize], 0, context, ContextSize);
                Marshal.WriteInt32(context, ContextFlagsOffset, ContextAmd64Control);
                if (!NativeMethods.GetThreadContext(threadHandle, context))
                    throw NativeError(Marshal.GetLastWin32Error(), "Could not read a suspended game thread's instruction pointer.");
                return unchecked((ulong)Marshal.ReadInt64(context, InstructionPointerOffset));
            }
            finally
            {
                Marshal.FreeHGlobal(storage);
            }
        }

        private static HashSet<uint> SnapshotThreads(uint processId)
        {
            const uint SnapshotThread = 0x00000004;
            IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(SnapshotThread, 0);
            if (snapshot == new IntPtr(-1))
                throw NativeError(Marshal.GetLastWin32Error(), "Could not take a game thread snapshot.");
            try
            {
                HashSet<uint> ids = new HashSet<uint>();
                ThreadEntry entry = new ThreadEntry { Size = (uint)Marshal.SizeOf(typeof(ThreadEntry)) };
                if (!NativeMethods.Thread32First(snapshot, ref entry))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreFiles) return ids;
                    throw NativeError(error, "Could not read the first thread in a snapshot.");
                }
                do
                {
                    if (entry.OwnerProcessId == processId) ids.Add(entry.ThreadId);
                    entry.Size = (uint)Marshal.SizeOf(typeof(ThreadEntry));
                }
                while (NativeMethods.Thread32Next(snapshot, ref entry));
                int lastError = Marshal.GetLastWin32Error();
                if (lastError != ErrorNoMoreFiles)
                    throw NativeError(lastError, "Could not finish reading the game thread snapshot.");
                return ids;
            }
            finally
            {
                MemoryManager.NativeMethods.CloseHandle(snapshot);
            }
        }

        private static void ValidateArchitecture(uint processId)
        {
            const uint QueryLimitedInformation = 0x1000;
            IntPtr handle = MemoryManager.NativeMethods.OpenProcess(QueryLimitedInformation, false, checked((int)processId));
            if (handle == IntPtr.Zero)
                throw NativeError(Marshal.GetLastWin32Error(), "Could not inspect the game's process architecture.");
            try
            {
                if (!NativeMethods.IsWow64Process(handle, out bool isWow64))
                    throw NativeError(Marshal.GetLastWin32Error(), "Could not inspect the game's process architecture.");
                if (isWow64) throw new PlatformNotSupportedException("The player hook requires a native x64 game process.");
            }
            finally
            {
                MemoryManager.NativeMethods.CloseHandle(handle);
            }
        }

        private static Win32Exception NativeError(int error, string message)
        {
            return new Win32Exception(error, message + " " + new Win32Exception(error).Message);
        }

        private sealed class PausedThread
        {
            internal IntPtr Handle;
            internal ulong InstructionPointer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ThreadEntry
        {
            internal uint Size;
            internal uint Usage;
            internal uint ThreadId;
            internal uint OwnerProcessId;
            internal int BasePriority;
            internal int DeltaPriority;
            internal uint Flags;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern uint GetCurrentProcessId();

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Thread32First(IntPtr snapshot, ref ThreadEntry entry);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Thread32Next(IntPtr snapshot, ref ThreadEntry entry);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint GetProcessIdOfThread(IntPtr threadHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint SuspendThread(IntPtr threadHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint ResumeThread(IntPtr threadHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetThreadContext(IntPtr threadHandle, IntPtr context);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWow64Process(IntPtr processHandle, [MarshalAs(UnmanagedType.Bool)] out bool isWow64);
        }
    }
}
